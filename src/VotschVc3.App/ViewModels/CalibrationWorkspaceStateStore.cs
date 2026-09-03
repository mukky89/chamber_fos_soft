using System.IO;
using System.Text.Json;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Persists operator workspace state that does not belong to one profile/chamber wiring setup.
///
/// Detailed calibration data (plateau selection, mappings/SN/CHAIN, peak selection, per-peak
/// timeout and stability settings) remains authoritative in CalibrationStore. The physical WIKA
/// assignment remains authoritative in CalibrationReferenceStatusStore. This file remembers the
/// remaining workspace/UI choices needed to reconstruct the same FBG workspace after app restart.
/// </summary>
public sealed class CalibrationWorkspaceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<Guid, CalibrationWorkspaceState> _states = new();

    public static CalibrationWorkspaceStateStore Instance { get; } = new();

    private CalibrationWorkspaceStateStore()
    {
        AppPaths.Initialize();
        _path = Path.Combine(AppPaths.SettingsDir, "fbg-calibration-workspaces.json");
        Load();
    }

    public CalibrationWorkspaceState? Get(Guid chamberId)
    {
        lock (_gate)
        {
            return _states.TryGetValue(chamberId, out CalibrationWorkspaceState? state)
                ? state with { }
                : null;
        }
    }

    /// <summary>Compatibility overload for older call sites.</summary>
    public void Save(Guid chamberId, Guid profileId, string peakLoggerHost, int peakLoggerPort) =>
        Save(new CalibrationWorkspaceState(
            chamberId,
            profileId,
            string.IsNullOrWhiteSpace(peakLoggerHost) ? "localhost" : peakLoggerHost.Trim(),
            peakLoggerPort,
            DateTimeOffset.Now));

    public void Save(CalibrationWorkspaceState state)
    {
        if (state.ChamberId == Guid.Empty || state.ProfileId == Guid.Empty) return;

        CalibrationWorkspaceState normalized = state with
        {
            PeakLoggerHost = string.IsNullOrWhiteSpace(state.PeakLoggerHost)
                ? "localhost"
                : state.PeakLoggerHost.Trim(),
            PeakLoggerPort = Math.Max(0, state.PeakLoggerPort),
            SelectedTabHeader = string.IsNullOrWhiteSpace(state.SelectedTabHeader)
                ? "Zapojenie"
                : state.SelectedTabHeader.Trim(),
            SavedAt = DateTimeOffset.Now,
        };

        lock (_gate)
        {
            _states[state.ChamberId] = normalized;
            SaveUnsafe();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            List<CalibrationWorkspaceState>? saved = JsonSerializer.Deserialize<List<CalibrationWorkspaceState>>(
                File.ReadAllText(_path), JsonOptions);
            if (saved is null) return;

            foreach (CalibrationWorkspaceState state in saved)
            {
                if (state.ChamberId == Guid.Empty || state.ProfileId == Guid.Empty) continue;
                _states[state.ChamberId] = state;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Načítanie uloženého workspace: {ex.Message}");
        }
    }

    private void SaveUnsafe()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(_states.Values.OrderBy(x => x.ChamberId).ToList(), JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Uloženie workspace: {ex.Message}");
        }
    }
}

/// <summary>
/// Backward-compatible workspace envelope. The original five constructor properties are retained,
/// so existing fbg-calibration-workspaces.json files continue to deserialize. New init properties
/// simply receive their safe defaults when an older file is loaded.
/// </summary>
public sealed record CalibrationWorkspaceState(
    Guid ChamberId,
    Guid ProfileId,
    string PeakLoggerHost,
    int PeakLoggerPort,
    DateTimeOffset SavedAt)
{
    public bool UseSimulator { get; init; }
    public FakePeakLoggerScenario SimulatorScenario { get; init; } = FakePeakLoggerScenario.Normal;
    public bool ShowF100Chart { get; init; }
    public string SelectedTabHeader { get; init; } = "Zapojenie";
}
