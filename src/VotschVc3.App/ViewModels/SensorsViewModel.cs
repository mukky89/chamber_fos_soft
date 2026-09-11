using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

public sealed class SensorsViewModel : ObservableObject
{
    private IReadOnlyList<SensorHistoryDetail> _all = [];
    private SensorHistoryGroup? _selected;
    private SensorHistoryDetail? _detail;
    private string _status = "Zatiaľ nie sú načítané údaje.";
    public SensorsViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, onError: ex => Status = "Históriu nemožno načítať: " + ex.Message);
        FilterCommand = new RelayCommand(ApplyFilters);
        OpenRunCommand = new RelayCommand(() =>
        {
            try
            {
                if (SelectedDetail is not { } detail || !Directory.Exists(detail.Folder))
                { Status = "Priečinok kalibrácie nie je dostupný."; return; }
                Process.Start(new ProcessStartInfo(detail.Folder) { UseShellExecute = true });
            }
            catch (Exception ex) { Status = "Kalibráciu nemožno otvoriť: " + ex.Message; }
        }, () => SelectedDetail is not null);
    }
    public string Search { get; set; } = "";
    public string Chamber { get; set; } = "";
    public string Temperature { get; set; } = "";
    public string Criteria { get; set; } = "";
    public DateTime? From { get; set; }
    public DateTime? Until { get; set; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public ObservableCollection<SensorHistoryGroup> Groups { get; } = [];
    public ObservableCollection<SensorHistoryDetail> Details { get; } = [];
    public SensorHistoryGroup? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            Details.Clear();
            foreach (var row in value?.Rows ?? []) Details.Add(row);
            SelectedDetail = Details.FirstOrDefault();
        }
    }
    public SensorHistoryDetail? SelectedDetail
    {
        get => _detail;
        set { if (SetProperty(ref _detail, value)) OpenRunCommand.RaiseCanExecuteChanged(); }
    }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand FilterCommand { get; }
    public RelayCommand OpenRunCommand { get; }
    public async Task RefreshAsync()
    {
        Status = "Načítavam históriu…";
        _all = await Task.Run(() =>
        {
            var store = CalibrationStorage.CreateStore();
            return SensorSettlingHistory.Rows(store.LoadHistory()).Select(r => new SensorHistoryDetail(r, store.GetRunDirectory(r.Run))).ToArray();
        });
        ApplyFilters();
    }
    private void ApplyFilters()
    {
        double? target = null;
        if (!string.IsNullOrWhiteSpace(Temperature))
        {
            if (!double.TryParse(Temperature.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            { Status = "Cieľová teplota musí byť číslo, napr. 25 alebo −20,5."; return; }
            target = number;
        }
        if (From > Until) { Status = "Dátum od musí byť pred dátumom do."; return; }
        static bool Contains(string value, string term) => value.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
        var rows = _all.Where(r => Contains(r.Data.Name + " " + r.Data.Peak.Sensor.SerialNumber, Search)
            && Contains(r.Data.Run.ChamberName, Chamber) && Contains(r.Criteria, Criteria)
            && (!target.HasValue || Math.Abs(r.Data.Attempt.TargetTemperatureC - target.Value) < 0.0001)
            && (!From.HasValue || r.Data.Attempt.StartedAt.LocalDateTime.Date >= From.Value.Date)
            && (!Until.HasValue || r.Data.Attempt.StartedAt.LocalDateTime.Date <= Until.Value.Date)).ToArray();
        string? name = Selected?.Name;
        Groups.Clear();
        foreach (var group in rows.GroupBy(r => r.Data.Name, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
            Groups.Add(new SensorHistoryGroup(group.Key, group.OrderByDescending(r => r.Data.Attempt.StartedAt).ToArray()));
        Selected = Groups.FirstOrDefault(g => g.Name == name) ?? Groups.FirstOrDefault();
        Status = rows.Length == 0 ? "Žiadne záznamy pre zvolené filtre." : $"Typy snímačov: {Groups.Count} · záznamy peakov: {rows.Length}. Priemery rešpektujú filtre.";
    }
}

public sealed record SensorHistoryDetail(SensorSettlingRow Data, string Folder)
{
    public DateTime Started => Data.Attempt.StartedAt.LocalDateTime;
    public string ChamberTime => Format(Data.Attempt.Chamber);
    public string WikaTime => Format(Data.Attempt.Wika);
    public string FbgTime => Format(Data.Peak.Fbg);
    public string State => $"{Data.Attempt.Status} · {Translate(Data.Peak.Result)}" + (Data.Attempt.CriteriaChanged ? " · zmenené kritériá" : "");
    private static string Translate(string status) => status switch
    {
        "Stable" => "Stabilný", "TimedOut" => "Timeout", "PeakLost" => "Strata peaku",
        "Disconnected" => "Odpojený", "Overridden" => "Manuálne preskočené", "Failed" => "Chyba",
        "CompletedWithStabilityWarning" => "Dokončené bez potvrdenej stability", "NoTemperatureResponse" => "Bez teplotnej odozvy",
        "Completed" => "Dokončené", "CompletedWithWarnings" => "Dokončené s upozorneniami", "Aborted" => "Prerušené",
        _ => status
    };
    public string Criteria => Data.Attempt.Status.StartsWith("Starší") ? "N/A – pôvodné kritériá nie sú doložené" :
        DescribeCriteria(Data.Attempt.Criteria, Data.Peak.Sensor.StabilizationTimeoutOverride) +
        string.Concat(Data.Attempt.CriteriaChanges.Select(change => $"\nZmena {change.At.ToLocalTime():dd.MM.yyyy HH:mm:ss}: " + DescribeCriteria(change.Settings, Data.Peak.Sensor.StabilizationTimeoutOverride)));
    private static string DescribeCriteria(CalibrationProfileSettings c, TimeSpan? sensorTimeout) =>
        FormattableString.Invariant($"Komora: vstup {c.ChamberEntryEnabled}, ±{c.ChamberEntryToleranceC} °C, okno {c.ChamberEntryStableSeconds} s, rozsah {c.ChamberEntryRangeC} °C, drift {c.ChamberEntryDriftCPerMinute} °C/min; WIKA/teplotná brána: ±{c.ChamberToleranceC} °C, čas {c.ChamberStableDuration}, rozsah {c.MaxChamberRangeC} °C, σ {c.MaxChamberStdDevC} °C, drift {c.MaxChamberDriftCPerMinute} °C/min; FBG: {c.RequiredStableSamples} vzoriek, rozsah {c.MaxWavelengthRangePm} pm, σ {c.MaxWavelengthStdDevPm} pm, drift {c.MaxWavelengthDriftPmPerMinute} pm/min; interval {c.SampleAcquisitionIntervalSeconds} s; timeout {sensorTimeout ?? c.DefaultSensorStabilizationTimeout}.");
    public string PhaseDetails => $"Komora: {Phase(Data.Attempt.Chamber)}\nWIKA: {Phase(Data.Attempt.Wika)}\nFBG: {Phase(Data.Peak.Fbg)}\nBeh: {Data.Run.DisplayRunId} · {Translate(Data.Run.State.ToString())} · pokus {Data.Attempt.Id}";
    private static string Phase(SettlingPhase phase) => $"{Translate(phase.Status)} · {phase.StartedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "N/A"} → {phase.EndedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "N/A"}" +
        string.Concat(phase.Intervals.Select(i => $"\n    {i.From.ToLocalTime():dd.MM.yyyy HH:mm:ss} → {i.Until.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {Duration((i.Until - i.From).TotalSeconds)} · {Translate(i.Status)}"));
    public static string Duration(double? seconds) => seconds is double s ? $"{(int)(s / 3600):00}:{(int)(s / 60) % 60:00}:{(int)s % 60:00}" : "N/A";
    private static string Format(SettlingPhase p) => p.ActiveSince.HasValue ? "N/A · nedokončené" : Duration(p.DurationSeconds) + (p.Status is "Úspešné" or "N/A" ? "" : " · " + Translate(p.Status));
}

public sealed record SensorHistoryGroup(string Name, IReadOnlyList<SensorHistoryDetail> Rows)
{
    public int Count => Rows.Select(r => r.Data.Run.RunId).Distinct().Count();
    public DateTime Last => Rows.Max(r => r.Data.Attempt.StartedAt).LocalDateTime;
    public string LastChamber => Rows[0].ChamberTime;
    public string LastWika => Rows[0].WikaTime;
    public string LastFbg => Rows.OrderByDescending(r => r.Data.Peak.Fbg.EndedAt ?? r.Data.Attempt.StartedAt).First().FbgTime;
    public string AverageChamber => Average("Komora");
    public string AverageWika => Average("WIKA");
    public string AverageFbg => Average("FBG");
    private string Average(string phase)
    {
        var result = SensorSettlingHistory.Average(Rows.Select(r => r.Data), phase);
        return $"{SensorHistoryDetail.Duration(result.Average)} (n={result.Count})";
    }
}
