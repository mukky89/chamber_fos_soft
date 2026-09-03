using System.IO;
using System.Text.Json;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Persists the lightweight operator workspace selection that is not part of one
/// profile/chamber wiring setup. The detailed wiring itself remains in CalibrationStore;
/// this store only remembers which profile and PeakLogger endpoint should be restored
/// when the FBG calibration workspace is opened again.
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

    public void Save(Guid chamberId, Guid profileId, string peakLoggerHost, int peakLoggerPort)
    {
        if (chamberId == Guid.Empty || profileId == Guid.Empty) return;

        lock (_gate)
        {
            _states[chamberId] = new CalibrationWorkspaceState(
                chamberId,
                profileId,
                string.IsNullOrWhiteSpace(peakLoggerHost) ? "localhost" : peakLoggerHost.Trim(),
                peakLoggerPort,
                DateTimeOffset.Now);
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

public sealed record CalibrationWorkspaceState(
    Guid ChamberId,
    Guid ProfileId,
    string PeakLoggerHost,
    int PeakLoggerPort,
    DateTimeOffset SavedAt);
