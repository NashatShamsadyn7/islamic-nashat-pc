using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IslamicNashat.Core;

public enum Prayer { Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha }

/// <summary>What plays when a prayer comes in.</summary>
public enum AdhanMode { Full, Short, Silent }

public enum WidgetTheme { Emerald, Dark, Light, Glass }

public sealed class AppSettings
{
    /// <summary>"ar", "ckb" or "en".</summary>
    public string Language { get; set; } = "ar";
    public bool Onboarded { get; set; }

    public Place Place { get; set; } = Place.Default;
    /// <summary>Null means "Auto": the method the Android app picks for the country.</summary>
    public CalculationMethod? Method { get; set; }
    /// <summary>0 Shafi'i, 1 Hanafi (later Asr).</summary>
    public int AsrJuristic { get; set; }
    public Dictionary<Prayer, int> Offsets { get; set; } = Enum.GetValues<Prayer>().ToDictionary(p => p, _ => 0);
    /// <summary>Days added to the Umm al-Qura date to match the local moon sighting (-2..2).</summary>
    public int HijriOffset { get; set; }

    public string Reciter { get; set; } = AdhanCatalog.BundledId;
    public string FajrReciter { get; set; } = AdhanCatalog.BundledId;
    public Dictionary<Prayer, AdhanMode> Sounds { get; set; } = new()
    {
        [Prayer.Fajr] = AdhanMode.Full,
        [Prayer.Dhuhr] = AdhanMode.Full,
        [Prayer.Asr] = AdhanMode.Full,
        [Prayer.Maghrib] = AdhanMode.Full,
        [Prayer.Isha] = AdhanMode.Full,
    };
    /// <summary>0..100.</summary>
    public int Volume { get; set; } = 80;
    public bool Notifications { get; set; } = true;
    /// <summary>Minutes before each prayer to remind; 0 turns it off.</summary>
    public int ReminderMinutes { get; set; } = 10;
    /// <summary>After a prayer comes in, show the time since the adhan for this long before moving on.</summary>
    public int ElapsedMinutes { get; set; } = 20;

    public bool RamadanMode { get; set; } = true;
    public int ImsakMinutes { get; set; } = 10;
    public bool FridayReminder { get; set; } = true;
    public bool ShowMakruh { get; set; } = true;

    public WidgetTheme Theme { get; set; } = WidgetTheme.Emerald;
    /// <summary>0.5..1.</summary>
    public double Opacity { get; set; } = 1.0;
    /// <summary>0.8..1.6, scales the whole widget.</summary>
    public double Scale { get; set; } = 1.0;
    /// <summary>Sit just above the taskbar clock instead of wherever it was dragged.</summary>
    public bool DockToTaskbar { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public bool WidgetVisible { get; set; } = true;
    public double? Left { get; set; }
    public double? Top { get; set; }

    public bool StartWithWindows { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    /// <summary>Everything up to here has been announced; a restart or a wake from sleep must not replay it.</summary>
    public DateTime? AnnouncedUntil { get; set; }

    public int OffsetFor(Prayer p) => Offsets.TryGetValue(p, out var m) ? m : 0;
    public AdhanMode SoundFor(Prayer p) => Sounds.TryGetValue(p, out var m) ? m : AdhanMode.Full;

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, SettingsStore.Json), SettingsStore.Json)!;
}

public static class SettingsStore
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new());
            var legacy = LegacySettings.TryImport();
            if (legacy is not null)
            {
                Save(legacy);
                return legacy;
            }
        }
        catch (Exception ex)
        {
            // A corrupted settings file must not stop the widget from telling the time.
            Log.Error("Settings file unreadable, using defaults", ex);
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            AppPaths.WriteAllTextAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
        }
    }

    static AppSettings Normalize(AppSettings s)
    {
        if (s.Language is not ("ar" or "ckb" or "en")) s.Language = "ar";
        s.Place ??= Place.Default;
        s.Offsets ??= [];
        s.Sounds ??= [];
        s.Volume = Math.Clamp(s.Volume, 0, 100);
        s.Opacity = Math.Clamp(s.Opacity, 0.5, 1.0);
        s.Scale = Math.Clamp(s.Scale, 0.8, 1.6);
        s.HijriOffset = Math.Clamp(s.HijriOffset, -2, 2);
        if (AdhanCatalog.Find(s.Reciter) is null) s.Reciter = AdhanCatalog.BundledId;
        if (AdhanCatalog.Find(s.FajrReciter) is null) s.FajrReciter = AdhanCatalog.BundledId;
        return s;
    }
}

/// <summary>Carries the 1.x Python widget's location, offsets and position over, once.</summary>
static class LegacySettings
{
    public static AppSettings? TryImport()
    {
        var file = Path.Combine(AppPaths.LegacyDataDir, "settings.json");
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var s = new AppSettings { Onboarded = true };

            if (root.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object
                && loc.TryGetProperty("id", out var id) && Places.ById(id.GetInt32()) is { } place)
                s.Place = place;

            if (root.TryGetProperty("offsets", out var offsets) && offsets.ValueKind == JsonValueKind.Object)
                foreach (var o in offsets.EnumerateObject())
                    if (Enum.TryParse<Prayer>(o.Name, ignoreCase: true, out var p) && o.Value.ValueKind == JsonValueKind.Number)
                        s.Offsets[p] = o.Value.GetInt32();

            if (root.TryGetProperty("pre_athan_minutes", out var pre) && pre.ValueKind == JsonValueKind.Number)
                s.ReminderMinutes = pre.GetInt32();
            if (root.TryGetProperty("athan_sound", out var sound) && sound.ValueKind == JsonValueKind.False)
                foreach (var p in s.Sounds.Keys.ToList()) s.Sounds[p] = AdhanMode.Silent;
            if (root.TryGetProperty("notifications", out var n) && n.ValueKind == JsonValueKind.False)
                s.Notifications = false;
            if (root.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Object
                && pos.TryGetProperty("x", out var x) && pos.TryGetProperty("y", out var y))
            {
                s.Left = x.GetDouble();
                s.Top = y.GetDouble();
            }
            Log.Info("Imported settings from the 1.x widget");
            return s;
        }
        catch (Exception ex)
        {
            Log.Error("Old widget settings could not be imported", ex);
            return null;
        }
    }
}
