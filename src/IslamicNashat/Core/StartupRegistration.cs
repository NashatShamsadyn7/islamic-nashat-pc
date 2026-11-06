using System;
using Microsoft.Win32;

namespace IslamicNashat.Core;

public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "IslamicNashat";
    /// <summary>Passed when Windows starts the app, so it starts quietly (no "ready" notification).</summary>
    public const string StartupArgument = "--startup";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\" {StartupArgument}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error("Updating the startup entry failed", ex);
        }
    }

    /// <summary>The 1.x Python widget started itself from a script in the Startup folder; two widgets would both call the adhan.</summary>
    public static void RemoveLegacyEntry()
    {
        try
        {
            var script = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "IslamicNashatWidget.vbs");
            if (System.IO.File.Exists(script)) System.IO.File.Delete(script);
        }
        catch (Exception ex)
        {
            Log.Error("Removing the old widget's startup entry failed", ex);
        }
    }
}
