using System;
using System.IO;
using System.Text;

namespace IslamicNashat.Core;

/// <summary>
/// User data lives in %APPDATA%\IslamicNashat; downloaded adhan recitations in %LOCALAPPDATA%.
/// Files shipped with the app (the location database, the default adhan) sit next to the exe.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslamicNashat");

    /// <summary>Settings folder of the 1.x Python widget, read once to carry the user's choices over.</summary>
    public static string LegacyDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslamicNashatWidget");

    public static string SoundsDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IslamicNashat", "adhan");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "islamic-nashat.log");

    public static string AppDir { get; } = AppContext.BaseDirectory;
    public static string LocationsDb => Path.Combine(AppDir, "Assets", "locations.db");
    public static string DefaultAdhan => Path.Combine(AppDir, "Assets", "azan_makkah.mp3");

    public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);

    /// <summary>Writes via a temp file so a crash mid-write never leaves a half-written file.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        EnsureDataDir();
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
