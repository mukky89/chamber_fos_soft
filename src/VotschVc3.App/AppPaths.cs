using System;
using System.IO;

namespace VotschVc3.App;

/// <summary>
/// Central filesystem layout for the application, all under the user's Documents
/// folder in <c>Documents\Lab Control</c>:
/// <list type="bullet">
///   <item><see cref="ProfilesDir"/> – profile library (profiles.json + seeded defaults),</item>
///   <item><see cref="AppLogDir"/> – application diagnostic log,</item>
///   <item><see cref="ProfileLogDir"/> – per-run temperature logs of running profiles,</item>
///   <item><see cref="SettingsDir"/> – other settings (chambers, users, e-mail, audit, UI, markers).</item>
/// </list>
/// On first run the folders are created; data from the previous
/// <c>Documents\VotschVc3</c> layout is migrated once so an existing profile
/// library / settings are not lost. Everything is best-effort – a filesystem
/// error never blocks startup.
/// </summary>
public static class AppPaths
{
    private static readonly string Documents =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Root of all application data: <c>Documents\Lab Control</c>.</summary>
    public static string Root { get; } = Path.Combine(Documents, "Lab Control");

    /// <summary>Profile library folder (profiles.json and the seeded defaults).</summary>
    public static string ProfilesDir { get; } = Path.Combine(Root, "Profiles");

    /// <summary>Application diagnostic-log folder.</summary>
    public static string AppLogDir { get; } = Path.Combine(Root, "App log");

    /// <summary>Per-profile temperature-log folder (records from running profiles).</summary>
    public static string ProfileLogDir { get; } = Path.Combine(Root, "Profilelog");

    /// <summary>Settings folder (chambers, users, e-mail, audit, UI, seed markers).</summary>
    public static string SettingsDir => Root;

    private static readonly string LegacyRoot = Path.Combine(Documents, "VotschVc3");

    private static readonly object Gate = new();
    private static bool _initialised;

    /// <summary>
    /// Creates the folder layout and, on first run, migrates data from the old
    /// <c>Documents\VotschVc3</c> location. Idempotent – safe to call repeatedly;
    /// the actual work runs once per process.
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

            foreach (string dir in new[] { Root, ProfilesDir, AppLogDir, ProfileLogDir })
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

    /// <summary>
    /// One-time copy of the previous <c>Documents\VotschVc3</c> layout into the new
    /// one, so an upgrading install keeps its profile library, settings and logs.
    /// Files are copied (not moved) so the old folder stays as a backup, and never
    /// overwrite an already-present file in the new layout.
    /// </summary>
    private static void TryMigrateFromLegacy()
    {
        try
        {
            string doneMarker = Path.Combine(Root, ".migrated_from_votschvc3");
            if (File.Exists(doneMarker) || !Directory.Exists(LegacyRoot))
            {
                return;
            }

            // Top-level files: profiles.json → Profiles, everything else (settings
            // JSONs, audit.csv, seed/reset markers) → settings root.
            foreach (string file in Directory.GetFiles(LegacyRoot))
            {
                string name = Path.GetFileName(file);
                string dest = name.Equals("profiles.json", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(ProfilesDir, name)
                    : Path.Combine(SettingsDir, name);
                CopyIfMissing(file, dest);
            }

            // The old monolithic app.log is intentionally NOT migrated – it grew
            // unbounded (hundreds of MB) and is exactly what the new daily-rotated
            // layout replaces. It stays in the legacy folder as a backup.

            // Per-profile temperature logs (old folder name: "profil-logy").
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
            // Never block startup on a migration problem – the app still works with
            // a fresh (empty) layout, and the old folder remains untouched.
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
