using System;
using System.IO;

namespace VotschVc3.App;

/// <summary>
/// Central filesystem layout for the application, all under the user's Documents
/// folder in <c>Documents\Lab Control</c>.
/// </summary>
public static class AppPaths
{
    private static readonly string Documents =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Root of all application data: <c>Documents\Lab Control</c>.</summary>
    public static string Root { get; } = Path.Combine(Documents, "Lab Control");

    /// <summary>Profile library folder – one JSON file per profile, named after it.</summary>
    public static string ProfilesDir { get; } = Path.Combine(Root, "Profiles");

    /// <summary>Application diagnostic-log folder.</summary>
    public static string AppLogDir { get; } = Path.Combine(Root, "App log");

    /// <summary>Per-profile temperature-log folder (records from running profiles).</summary>
    public static string ProfileLogDir { get; } = Path.Combine(Root, "Profilelog");

    /// <summary>
    /// Continuous per-connection temperature recordings (started automatically whenever
    /// a chamber connects, so routine manual operation outside a profile is captured too).
    /// </summary>
    public static string RecordingDir { get; } = Path.Combine(Root, "Recordings");

    /// <summary>PeakLogger-backed FBG calibration setups, runs, raw samples and checkpoints.</summary>
    public static string CalibrationDir { get; } = Path.Combine(Root, "Calibration");

    /// <summary>Settings folder (chambers, users, e-mail, audit, UI, seed markers).</summary>
    public static string SettingsDir => Root;

    /// <summary>
    /// Profile-run checkpoints (one JSON file per chamber), written on every set-point
    /// update so an interrupted run (power outage, crash) can be offered for resume.
    /// </summary>
    public static string ProfileRecoveryDir { get; } = Path.Combine(Root, "Profile recovery");

    private static readonly string LegacyRoot = Path.Combine(Documents, "VotschVc3");

    private static readonly object Gate = new();
    private static bool _initialised;

    /// <summary>
    /// Creates the folder layout and, on first run, migrates data from the old
    /// <c>Documents\VotschVc3</c> location. Idempotent – safe to call repeatedly.
    /// </summary>
    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialised)
            {
                return;
            }

            _initialised = true;

            foreach (string dir in new[] { Root, ProfilesDir, AppLogDir, ProfileLogDir, RecordingDir, ProfileRecoveryDir, CalibrationDir })
            {
                TryCreate(dir);
            }

            TryMigrateFromLegacy();
        }
    }

    private static void TryCreate(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            // Best effort: a folder we cannot create is reported later when a
            // store actually fails to write, not by crashing startup here.
        }
    }

    private static void TryMigrateFromLegacy()
    {
        try
        {
            string doneMarker = Path.Combine(Root, ".migrated_from_votschvc3");
            if (File.Exists(doneMarker) || !Directory.Exists(LegacyRoot))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(LegacyRoot))
            {
                string name = Path.GetFileName(file);
                string dest = name.Equals("profiles.json", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(ProfilesDir, name)
                    : Path.Combine(SettingsDir, name);
                CopyIfMissing(file, dest);
            }

            string legacyProfileLogs = Path.Combine(LegacyRoot, "profil-logy");
            if (Directory.Exists(legacyProfileLogs))
            {
                foreach (string file in Directory.GetFiles(legacyProfileLogs))
                {
                    CopyIfMissing(file, Path.Combine(ProfileLogDir, Path.GetFileName(file)));
                }
            }

            File.WriteAllText(doneMarker, DateTime.Now.ToString("o"));
        }
        catch
        {
            // Never block startup on a migration problem.
        }
    }

    private static void CopyIfMissing(string src, string dest)
    {
        try
        {
            if (File.Exists(dest))
            {
                return;
            }

            string? destDir = Path.GetDirectoryName(dest);
            if (destDir is not null)
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(src, dest);
        }
        catch
        {
            // Skip an individual file we cannot copy; the rest still migrate.
        }
    }
}
