namespace VotschVc3.Core.Calibration;

/// <summary>Run-persistent evidence, separate from transient API indices. Ambiguity is latched:
/// wavelength/intensity alone cannot prove identity after a merge or an unobserved crossing.</summary>
public sealed class PeakIdentityChannel
{
    public string Device { get; set; } = "";
    public string Channel { get; set; } = "";
    public DateTimeOffset LastObservedAt { get; set; }
    public string? Problem { get; set; }
    public bool CommunicationGap { get; set; }
    public List<PeakIdentityTrack> Tracks { get; set; } = new();
}

public sealed class PeakIdentityTrack
{
    public string? Problem { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public Guid PhysicalFbgId { get; set; }
    public string OriginalPeakId { get; set; } = "";
    public int OriginalPeakIndex { get; set; }
    public string ApiPeakId { get; set; } = "";
    public double WavelengthNm { get; set; }
    public DateTimeOffset? LastSourceTimestamp { get; set; }
}

public sealed class PeakIdentityEvent
{
    public DateTimeOffset Timestamp { get; set; }
    public string Device { get; set; } = "";
    public string Channel { get; set; } = "";
    public Guid PhysicalFbgId { get; set; }
    public string? PreviousApiPeakId { get; set; }
    public string? CurrentApiPeakId { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>Unique bipartite matching within independently configured motion bounds.
/// No calibration coefficients or API ordering are used as evidence.</summary>
public static class PeakIdentityGuard
{
    public static List<PeakIdentityChannel> Initialize(IEnumerable<PeakLoggerSensor> sensors,
        IEnumerable<CalibrationSensorMapping> mappings, DateTimeOffset now)
    {
        var selected = mappings.Where(m => m.Selected).ToArray();
        return sensors.Where(s => selected.Any(m => Same(m.SourceDeviceSerialNumber, s.SerialNumber) && Same(m.Channel, s.Channel)))
            .Select(s => new PeakIdentityChannel
            {
                Device = s.SerialNumber, Channel = s.Channel, LastObservedAt = now,
                Tracks = s.Peaks.Select(p => new PeakIdentityTrack
                {
                    PhysicalFbgId = selected.FirstOrDefault(m => Same(m.SourceDeviceSerialNumber, s.SerialNumber) &&
                        Same(m.Channel, s.Channel) && m.PeakId == p.PeakId)?.PhysicalFbgId ?? Guid.Empty,
                    OriginalPeakId = p.PeakId, OriginalPeakIndex = p.PeakIndex,
                    ApiPeakId = p.PeakId, WavelengthNm = p.WavelengthNm,
                }).ToList(),
            }).ToList();
    }

    public static IReadOnlyList<PeakLoggerMeasurement> Observe(IReadOnlyList<PeakLoggerMeasurement> batch,
        IList<PeakIdentityChannel> channels, CalibrationProfileSettings settings,
        DateTimeOffset now, Action<PeakIdentityEvent> audit)
    {
        var accepted = new List<PeakLoggerMeasurement>();
        // An empty transport frame is not evidence that every physical sensor disappeared.
        // Leave identity untouched; the orchestrator waits in the same plateau for fresh data.
        if (batch.Count == 0) return accepted;
        foreach (var channel in channels)
        {
            if (channel.Problem is not null && !channel.CommunicationGap) continue;
            var observations = batch.Where(p => Same(p.SerialNumber, channel.Device) && Same(p.Channel, channel.Channel)).ToArray();
            if (observations.Length == 0)
            {
                if (!channel.CommunicationGap)
                {
                    channel.CommunicationGap = true;
                    channel.Problem = "Výpadok dát kanála; čaká sa na overenie kontinuity po návrate peakov.";
                    audit(new PeakIdentityEvent { Timestamp = now, Device = channel.Device,
                        Channel = channel.Channel, Reason = channel.Problem });
                }
                continue;
            }
            string? problem = null;
            double elapsed = (now - channel.LastObservedAt).TotalSeconds;
            double maxGap = settings.IdentityMaximumGapSeconds;
            bool recovering = channel.CommunicationGap || elapsed > maxGap;
            double separation = settings.IdentityMinimumSeparationNm;
            double movement = settings.IdentityBaseToleranceNm + settings.IdentityMaximumMotionNmPerMinute * Math.Max(0, elapsed) / 60;
            if (!double.IsFinite(movement) || !double.IsFinite(separation) || !double.IsFinite(maxGap) ||
                settings.IdentityBaseToleranceNm <= 0 || settings.IdentityMaximumMotionNmPerMinute < 0 || separation <= 0 || maxGap <= 0)
                problem = "Neplatné limity sledovania identity.";
            else if (elapsed < 0)
                problem = "Prerušená časová kontinuita sledovania; identitu nemožno potvrdiť.";
            else if (channel.Problem is null && observations.Length > 0 && observations.Length <= channel.Tracks.Count &&
                     (observations.Length < channel.Tracks.Count || channel.Tracks.Any(t => t.Problem is not null)))
            {
                ObserveRemainingPeaks(channel, observations, settings, now, accepted, audit);
                continue;
            }
            else if (observations.Length != channel.Tracks.Count || observations.Length == 0)
                problem = $"Počet peakov sa zmenil z {channel.Tracks.Count} na {observations.Length}; možné prekrytie, výpadok alebo nový peak.";
            else if (observations.Any(p => !double.IsFinite(p.WavelengthNm) || p.WavelengthNm <= 0 ||
                         p.Timestamp > now.AddSeconds(5) || now - p.Timestamp > TimeSpan.FromSeconds(10)) ||
                     observations.Select(p => p.PeakId).Distinct(StringComparer.Ordinal).Count() != observations.Length)
                problem = "Neplatný, zastaraný alebo duplicitný rámec peakov.";
            else if (TooClose(observations.Select(p => p.WavelengthNm), separation) ||
                     TooClose(channel.Tracks.Select(p => p.WavelengthNm), separation))
                problem = "Peaky sú príliš blízko na jednoznačné priradenie; možné prekrytie.";

            // Disjoint reachable intervals rule out a hidden crossing under the configured
            // motion bound. Merely finding a nearest/unique endpoint after a gap is insufficient.
            if (problem is null && recovering &&
                TooClose(channel.Tracks.Select(t => t.WavelengthNm), 2 * movement + separation))
                problem = "Po výpadku sa možné rozsahy pohybu peakov prekrývajú; identitu nemožno potvrdiť.";

            int[]? assignment = null;
            if (problem is null)
            {
                bool[,] edges = new bool[channel.Tracks.Count, observations.Length];
                for (int i = 0; i < channel.Tracks.Count; i++)
                    for (int j = 0; j < observations.Length; j++)
                        edges[i, j] = Math.Abs(channel.Tracks[i].WavelengthNm - observations[j].WavelengthNm) <= movement;
                assignment = Match(edges);
                if (assignment is null)
                    problem = "Pohyb peakov prekročil overovací rozsah; chýba jednoznačná kontinuita.";
                else
                    for (int i = 0; i < assignment.Length; i++)
                        if (Match(edges, i, assignment[i]) is not null)
                        {
                            problem = "Existuje viac možných priradení peakov; identitu nemožno určiť.";
                            break;
                        }
            }
            if (problem is not null)
            {
                channel.CommunicationGap = false;
                channel.Problem = problem;
                audit(new PeakIdentityEvent { Timestamp = now, Device = channel.Device, Channel = channel.Channel, Reason = problem });
                continue;
            }
            if (assignment!.Select((j, i) => channel.Tracks[i].LastSourceTimestamp is { } previous && observations[j].Timestamp <= previous).Any(stale => stale))
            {
                channel.CommunicationGap = false;
                channel.Problem = "Čas zdrojových vzoriek sa neposunul; opakované alebo oneskorené dáta.";
                audit(new PeakIdentityEvent { Timestamp = now, Device = channel.Device, Channel = channel.Channel, Reason = channel.Problem });
                continue;
            }
            if (recovering)
            {
                audit(new PeakIdentityEvent { Timestamp = now, Device = channel.Device, Channel = channel.Channel,
                    Reason = "Identita po výpadku automaticky potvrdená: neprekrývajúce sa rozsahy pohybu a jediné úplné priradenie. Neistý úsek zostáva nepoužiteľný." });
                channel.Problem = null;
                channel.CommunicationGap = false;
            }
            for (int i = 0; i < assignment!.Length; i++)
            {
                var track = channel.Tracks[i];
                var observation = observations[assignment[i]];
                if (track.ApiPeakId != observation.PeakId)
                    audit(new PeakIdentityEvent { Timestamp = now, Device = channel.Device, Channel = channel.Channel,
                        PhysicalFbgId = track.PhysicalFbgId, PreviousApiPeakId = track.ApiPeakId,
                        CurrentApiPeakId = observation.PeakId, Reason = "Jednoznačné prečíslovanie API v medziach kontinuity." });
                track.ApiPeakId = observation.PeakId;
                track.WavelengthNm = observation.WavelengthNm;
                track.LastSourceTimestamp = observation.Timestamp;
                track.LastObservedAt = now;
                // Downstream joins retain the original binding. The unmodified API frame is logged separately.
                accepted.Add(observation with { PeakId = track.OriginalPeakId, PeakIndex = track.OriginalPeakIndex });
            }
            channel.LastObservedAt = now;
        }
        return accepted;
    }

    private static void ObserveRemainingPeaks(PeakIdentityChannel channel, PeakLoggerMeasurement[] observations,
        CalibrationProfileSettings settings, DateTimeOffset now, List<PeakLoggerMeasurement> accepted,
        Action<PeakIdentityEvent> audit)
    {
        // Keep missing tracks as possible competitors. Never let their disappearance make
        // an ambiguous surviving detection look unique or renumber the physical bindings.
        var bounds = channel.Tracks.Select(t => settings.IdentityBaseToleranceNm +
            settings.IdentityMaximumMotionNmPerMinute * Math.Max(0, (now - (t.LastObservedAt ?? channel.LastObservedAt)).TotalMinutes)).ToArray();
        var candidates = channel.Tracks.Select((t, i) => observations.Select((p, j) => (p, j))
            .Where(x => Math.Abs(x.p.WavelengthNm - t.WavelengthNm) <= bounds[i]).Select(x => x.j).ToArray()).ToArray();
        var updates = new List<(PeakIdentityTrack Track, PeakLoggerMeasurement Observation)>();
        for (int i = 0; i < channel.Tracks.Count; i++)
        {
            var track = channel.Tracks[i];
            if (track.Problem is not null) continue;
            bool isolated = candidates[i].Length == 1;
            var observation = isolated ? observations[candidates[i][0]] : null;
            if (observation is not null)
            {
                isolated &= double.IsFinite(observation.WavelengthNm) && observation.WavelengthNm > 0 &&
                    observation.Timestamp <= now.AddSeconds(5) && now - observation.Timestamp <= TimeSpan.FromSeconds(10) &&
                    (track.LastSourceTimestamp is null || observation.Timestamp > track.LastSourceTimestamp) &&
                    observations.Count(p => p.PeakId == observation.PeakId) == 1;
                for (int other = 0; other < channel.Tracks.Count; other++)
                    if (other != i && Math.Abs(track.WavelengthNm - channel.Tracks[other].WavelengthNm) <=
                        bounds[i] + bounds[other] + settings.IdentityMinimumSeparationNm) isolated = false;
                if (observations.Any(p => !ReferenceEquals(p, observation) &&
                    Math.Abs(p.WavelengthNm - observation.WavelengthNm) < settings.IdentityMinimumSeparationNm)) isolated = false;
            }
            if (!isolated)
            {
                track.Problem = "Peak chýba alebo sa jeho identita nedá jednoznačne odlíšiť; ostatné overené peaky pokračujú.";
                audit(new PeakIdentityEvent { Timestamp = now, Device = channel.Device, Channel = channel.Channel,
                    PhysicalFbgId = track.PhysicalFbgId, PreviousApiPeakId = track.ApiPeakId, Reason = track.Problem });
            }
            else updates.Add((track, observation!));
        }
        foreach (var (track, observation) in updates)
        {
            if (track.ApiPeakId != observation.PeakId)
                audit(new PeakIdentityEvent { Timestamp = now, Device = channel.Device, Channel = channel.Channel,
                    PhysicalFbgId = track.PhysicalFbgId, PreviousApiPeakId = track.ApiPeakId,
                    CurrentApiPeakId = observation.PeakId, Reason = "Jednoznačné priradenie zostávajúceho peaku po čiastočnom výpadku." });
            track.ApiPeakId = observation.PeakId;
            track.WavelengthNm = observation.WavelengthNm;
            track.LastSourceTimestamp = observation.Timestamp;
            track.LastObservedAt = now;
            accepted.Add(observation with { PeakId = track.OriginalPeakId, PeakIndex = track.OriginalPeakIndex });
        }
        // Channel timestamp stays at the last complete frame, preserving missing-track evidence.
    }
    private static bool TooClose(IEnumerable<double> values, double separation)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Any(v => !double.IsFinite(v)) || sorted.Zip(sorted.Skip(1), (a, b) => b - a).Any(d => d < separation);
    }

    private static int[]? Match(bool[,] edges, int excludedTrack = -1, int excludedObservation = -1)
    {
        int count = edges.GetLength(0);
        var owners = Enumerable.Repeat(-1, count).ToArray();
        bool Augment(int track, bool[] visited)
        {
            for (int observation = 0; observation < count; observation++)
            {
                if (!edges[track, observation] || visited[observation] ||
                    (track == excludedTrack && observation == excludedObservation)) continue;
                visited[observation] = true;
                if (owners[observation] < 0 || Augment(owners[observation], visited))
                { owners[observation] = track; return true; }
            }
            return false;
        }
        for (int track = 0; track < count; track++)
            if (!Augment(track, new bool[count])) return null;
        var result = new int[count];
        for (int observation = 0; observation < count; observation++) result[owners[observation]] = observation;
        return result;
    }

    internal static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
