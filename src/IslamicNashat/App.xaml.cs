using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IslamicNashat.Core;
using IslamicNashat.UI;

namespace IslamicNashat;

public partial class App : Application
{
    Mutex? _singleInstance;
    WidgetController? _controller;
    WidgetWindow? _widget;
    TrayIcon? _tray;
    SettingsWindow? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\IslamicNashat.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        AppPaths.EnsureDataDir();
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        Log.Info($"Islamic Nashat {Updater.Current} starting");

        StopLegacyWidget();
        _controller = new WidgetController();
        _widget = new WidgetWindow(_controller, OpenSettings);
        _tray = new TrayIcon(_controller, ToggleWidget, OpenSettings, () => CheckForUpdates(manual: true), Quit);
        _controller.Notify += (title, body) => _tray.ShowMessage(title, body);
        _controller.Start();

        if (!_controller.Settings.Onboarded)
        {
            var welcome = new WelcomeWindow(_controller);
            welcome.Closed += (_, _) =>
            {
                _widget.ShowIfWanted();
                _tray.ShowMessage(Strings.T("ready_title"), Strings.T("ready_body"));
            };
            welcome.Show();
        }
        else
        {
            _widget.ShowIfWanted();
            if (e.Args.Contains("--settings")) OpenSettings();
        }

        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        updateTimer.Tick += (_, _) =>
        {
            updateTimer.Stop();
            var last = _controller.Settings.LastUpdateCheck;
            if (last is null || DateTime.UtcNow - last.Value > TimeSpan.FromDays(1)) CheckForUpdates(manual: false);
        };
        updateTimer.Start();
    }

    /// <summary>The 1.x Python widget would announce every adhan a second time.</summary>
    static void StopLegacyWidget()
    {
        StartupRegistration.RemoveLegacyEntry();
        foreach (var p in Process.GetProcessesByName("IslamicNashatWidget"))
        {
            try { p.Kill(); }
            catch (Exception ex) { Log.Error("Could not stop the old widget", ex); }
            finally { p.Dispose(); }
        }
    }

    void ToggleWidget()
    {
        if (_controller is null || _widget is null) return;
        _controller.Settings.WidgetVisible = !_controller.Settings.WidgetVisible;
        _controller.SaveQuietly();
        _widget.ShowIfWanted();
    }

    void OpenSettings()
    {
        if (_controller is null) return;
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_controller, () => CheckForUpdates(manual: true));
        _settings.Show();
        _settings.Activate();
    }

    public async void CheckForUpdates(bool manual)
    {
        if (_controller is null || _tray is null) return;
        UpdateInfo? update;
        try
        {
            update = await Updater.CheckAsync();
            _controller.Settings.LastUpdateCheck = DateTime.UtcNow;
            _controller.SaveQuietly();
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            if (manual) _tray.ShowMessage(Strings.T("app_name"), Strings.T("update_failed"));
            return;
        }

        if (update is null)
        {
            if (manual) _tray.ShowMessage(Strings.T("app_name"), Strings.T("up_to_date"));
            return;
        }
        if (manual) new UpdateWindow(update).Show();
        else _tray.ShowMessage(Strings.T("update_title"), Strings.F("update_body", update.Version), () => new UpdateWindow(update).Show());
    }

    void Quit()
    {
        _controller?.SaveQuietly();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
