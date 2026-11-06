using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using IslamicNashat.Core;
using Microsoft.Win32;

namespace IslamicNashat;

/// <summary>
/// The widget's heart: ticks once a second, works out what to show from the clock, and announces
/// adhans and reminders exactly once. Everything runs on the UI thread.
/// </summary>
public sealed class WidgetController
{
    /// <summary>Alerts found later than this after their time (sleep, a closed lid) are skipped, not played late.</summary>
    static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    readonly DispatcherTimer _tick = new(DispatcherPriority.Render);
    readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    DateTime _lastSaved = DateTime.MinValue;

    public AppSettings Settings { get; private set; }
    public Timetable Timetable { get; private set; }
    public AdhanPlayer Player { get; } = new();
    public WidgetState? State { get; private set; }
    public bool FullscreenActive { get; private set; }

    public event Action? StateChanged;
    public event Action? SettingsChanged;
    public event Action? FullscreenChanged;
    /// <summary>Title, body; the tray shows it as a Windows notification.</summary>
    public event Action<string, string>? Notify;

    public WidgetController()
    {
        Settings = SettingsStore.Load();
        Strings.Language = Settings.Language;
        Timetable = Timetable.From(Settings);

        _tick.Tick += (_, _) => Tick();
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        Player.PlayingChanged += () => StateChanged?.Invoke();

        // A wake from sleep or a clock change: refresh at once instead of waiting for the next tick.
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) Dispatch(Tick); };
        SystemEvents.TimeChanged += (_, _) => Dispatch(() => { Timetable = Timetable.From(Settings); Tick(); });
    }

    public void Start()
    {
        // First run: nothing before now counts as missed.
        Settings.AnnouncedUntil ??= DateTime.Now;
        Tick();
        _fullscreenTimer.Start();
        PrefetchReciters();
    }

    public void Apply(AppSettings updated)
    {
        var languageChanged = updated.Language != Settings.Language;
        Settings = updated;
        Strings.Language = updated.Language;
        Timetable = Timetable.From(updated);
        SettingsStore.Save(updated);
        StartupRegistration.Apply(updated.StartWithWindows);
        PrefetchReciters();
        SettingsChanged?.Invoke();
        Tick();
        if (languageChanged) StateChanged?.Invoke();
    }

    public void SaveQuietly() => SettingsStore.Save(Settings);

    public void StopAdhan() => Player.Stop();

    void Tick()
    {
        var now = DateTime.Now;
        try
        {
            Announce(now);
            State = WidgetState.Compute(Timetable, Settings, now);
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Tick failed", ex);
        }
        // Wake just after the next whole second so the countdown never skips or repeats a digit.
        _tick.Interval = TimeSpan.FromMilliseconds(1000 - now.Millisecond + 15);
        _tick.Start();
    }

    void Announce(DateTime now)
    {
        var from = Settings.AnnouncedUntil ?? now;
        if (now <= from)
        {
            // The clock went backwards (manual change, time sync): don't wait hours for it to catch up.
            if (from - now > TimeSpan.FromMinutes(5)) Settings.AnnouncedUntil = now;
            return;
        }

        var due = Alerts.Between(Timetable, AlertOptions.From(Settings), from, now);
        Settings.AnnouncedUntil = now;

        var fresh = due.Where(a => now - a.At <= StaleAfter
            // Friday's reminder is still useful if the PC is switched on later that morning.
            || (a.Kind == AlertKind.Friday && now < a.Target)).ToList();
        // Of several fresh alerts (rare), the adhan wins over its own reminder.
        foreach (var alert in fresh.Where(a => !(a.Kind == AlertKind.Reminder && fresh.Any(b => b.Kind == AlertKind.Adhan && b.Prayer == a.Prayer))))
            Fire(alert);

        if (due.Count > 0 || now - _lastSaved > TimeSpan.FromMinutes(1))
        {
            SettingsStore.Save(Settings);
            _lastSaved = now;
        }
    }

    void Fire(Alert alert)
    {
        Log.Info($"Alert {alert.Kind} {alert.Prayer} at {alert.At:HH:mm:ss}");
        var ramadan = Alerts.IsRamadan(DateOnly.FromDateTime(alert.Target), AlertOptions.From(Settings));
        var name = Strings.PrayerName(alert.Prayer, alert.Target, ramadan);
        switch (alert.Kind)
        {
            case AlertKind.Adhan:
                Player.PlayAdhan(alert.Prayer, Settings);
                if (Settings.Notifications)
                    Notify?.Invoke(Strings.F("notify_adhan_title", name), Strings.F("notify_adhan_body", Strings.Clock(alert.Target), Settings.Place.Name));
                break;
            case AlertKind.Reminder:
                if (Settings.SoundFor(alert.Prayer) != AdhanMode.Silent && !Player.IsPlaying) Player.PlayTone(Math.Min(Settings.Volume, 60));
                if (Settings.Notifications)
                    Notify?.Invoke(Strings.F("notify_reminder_title", name),
                        Strings.F("notify_reminder_body", Strings.Minutes(Settings.ReminderMinutes), Strings.Clock(alert.Target)));
                break;
            case AlertKind.Imsak:
                if (!Player.IsPlaying) Player.PlayTone(Settings.Volume);
                Notify?.Invoke(Strings.T("notify_imsak_title"), Strings.F("notify_imsak_body", Strings.Clock(alert.Target)));
                break;
            case AlertKind.Friday:
                Notify?.Invoke(Strings.T("notify_friday_title"), Strings.T("notify_friday_body"));
                break;
        }
    }

    void CheckFullscreen()
    {
        var active = Settings.HideInFullscreen && Fullscreen.IsActive();
        if (active == FullscreenActive) return;
        FullscreenActive = active;
        FullscreenChanged?.Invoke();
    }

    /// <summary>Downloads the chosen recitations in the background so the adhan never waits on the network.</summary>
    public void PrefetchReciters()
    {
        foreach (var id in new[] { Settings.Reciter, Settings.FajrReciter }.Distinct())
            if (AdhanCatalog.Find(id) is { IsBundled: false } r && AdhanCatalog.LocalPath(r) is null)
                _ = Task.Run(async () =>
                {
                    try { await AdhanCatalog.EnsureAsync(r); }
                    catch (Exception ex) { Log.Error($"Downloading {r.Id} failed", ex); }
                });
    }

    static void Dispatch(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
}
