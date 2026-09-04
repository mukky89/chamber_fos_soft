using System.Globalization;
using System.Text.Json;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Allocates operator-friendly calibration run identifiers in the form 01-2026-09-04.
/// The numeric prefix resets every local calendar day. A small state file stored next to
/// calibration data guarantees that the sequence survives app restarts.
/// </summary>
public static class HumanReadableRunId
{
    private static readonly object Gate = new();

    public static string Allocate(string calibrationRoot, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calibrationRoot);
        Directory.CreateDirectory(calibrationRoot);

        string localDate = timestamp.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string path = Path.Combine(calibrationRoot, "run-sequence.json");

        lock (Gate)
        {
            RunSequenceState state = Load(path);
            int next = string.Equals(state.Date, localDate, StringComparison.Ordinal)
                ? Math.Max(0, state.LastNumber) + 1
                : 1;

            Save(path, new RunSequenceState(localDate, next));
            return $"{next:00}-{localDate}";
        }
    }

    private static RunSequenceState Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new RunSequenceState(string.Empty, 0);
            return JsonSerializer.Deserialize<RunSequenceState>(File.ReadAllText(path))
                ?? new RunSequenceState(string.Empty, 0);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A damaged sequence file must not prevent calibration from starting. Resetting
            // the counter is safer than surfacing an infrastructure failure to the operator.
            return new RunSequenceState(string.Empty, 0);
        }
    }

    private static void Save(string path, RunSequenceState state)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, path, overwrite: true);
    }

    private sealed record RunSequenceState(string Date, int LastNumber);
}