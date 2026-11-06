using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

/// <summary>All settings, built in code so every label follows the chosen language.</summary>
public sealed class SettingsWindow : Window
{
    readonly WidgetController _controller;
    readonly Action _checkForUpdates;
    readonly AppSettings _s;

    readonly ComboBox _language;
    readonly LocationPicker _location;
    readonly ComboBox _method;
    readonly ComboBox _madhab;
    readonly Dictionary<Prayer, Slider> _offsets = [];
    readonly ComboBox _hijri;
    readonly CheckBox _startup;

    readonly ComboBox _reciter;
    readonly ComboBox _fajrReciter;
    readonly Dictionary<Prayer, ComboBox> _modes = [];
    readonly Slider _volume;
    readonly CheckBox _notifications;
    readonly ComboBox _reminder;
    readonly ComboBox _elapsed;
    readonly TextBlock _previewStatus = Fluent.Hint("");

    readonly ComboBox _theme;
    readonly Slider _opacity;
    readonly Slider _scale;
    readonly CheckBox _dock;
    readonly CheckBox _hideFullscreen;

    readonly CheckBox _ramadan;
    readonly ComboBox _imsak;
    readonly CheckBox _friday;
    readonly CheckBox _makruh;

    public SettingsWindow(WidgetController controller, Action checkForUpdates)
    {
        _controller = controller;
        _checkForUpdates = checkForUpdates;
        _s = controller.Settings.Clone();

        Fluent.Apply(this);
        Title = $"{Strings.T("app_name")} — {Strings.T("settings")}";
        Width = 560;
        Height = 700;
        MinWidth = 480;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _language = Fluent.Combo([("ar", "العربية"), ("ckb", "کوردی (سۆرانی)"), ("en", "English")], _s.Language);
        _location = new LocationPicker(_s.Place);
        _method = Fluent.Combo(
            new (CalculationMethod?, string)[] { (null, Strings.T("method_auto")) }.Concat(
                Enum.GetValues<CalculationMethod>().Select(m => ((CalculationMethod?)m, MethodName(m)))), _s.Method);
        _madhab = Fluent.Combo([(0, Strings.T("shafi")), (1, Strings.T("hanafi"))], _s.AsrJuristic);
        _hijri = Fluent.Combo(Enumerable.Range(-2, 5).Select(d => (d, d > 0 ? $"+{Strings.Digits(d)}" : Strings.Digits(d.ToString()))), _s.HijriOffset);
        _startup = Fluent.Check(Strings.T("start_with_windows"), _s.StartWithWindows);

        var reciters = AdhanCatalog.All.Select(r => (r.Id, r.Name(_s.Language))).ToList();
        _reciter = Fluent.Combo(reciters, _s.Reciter);
        _fajrReciter = Fluent.Combo(reciters, _s.FajrReciter);
        (FrameworkElement Row, Slider Slider) volume = Fluent.Slider(0, 100, _s.Volume, 5, v => Strings.Digits($"{v:0}%"));
        _volume = volume.Slider;
        _notifications = Fluent.Check(Strings.T("notifications"), _s.Notifications);
        _reminder = Fluent.Combo(Fluent.MinuteChoices(0, 5, 10, 15, 20, 30), _s.ReminderMinutes);
        _elapsed = Fluent.Combo(Fluent.MinuteChoices(0, 10, 15, 20, 30, 45), _s.ElapsedMinutes);

        _theme = Fluent.Combo(Enum.GetValues<WidgetTheme>().Select(t => (t, Strings.T("theme_" + t.ToString().ToLowerInvariant()))), _s.Theme);
        (FrameworkElement Row, Slider Slider) opacity = Fluent.Slider(0.5, 1, _s.Opacity, 0.05, v => Strings.Digits($"{v * 100:0}%"));
        _opacity = opacity.Slider;
        (FrameworkElement Row, Slider Slider) scale = Fluent.Slider(0.8, 1.6, _s.Scale, 0.1, v => Strings.Digits($"{v * 100:0}%"));
        _scale = scale.Slider;
        _dock = Fluent.Check(Strings.T("dock_taskbar"), _s.DockToTaskbar);
        _hideFullscreen = Fluent.Check(Strings.T("hide_fullscreen"), _s.HideInFullscreen);

        _ramadan = Fluent.Check(Strings.T("ramadan_mode"), _s.RamadanMode);
        _imsak = Fluent.Combo(Fluent.MinuteChoices(0, 5, 10, 15, 20), _s.ImsakMinutes);
        _friday = Fluent.Check(Strings.T("friday"), _s.FridayReminder);
        _makruh = Fluent.Check(Strings.T("show_makruh"), _s.ShowMakruh);

        var tabs = new TabControl();
        tabs.Items.Add(Tab("tab_general", General()));
        tabs.Items.Add(Tab("tab_adhan", AdhanTab(volume.Row)));
        tabs.Items.Add(Tab("tab_look", LookTab(opacity.Row, scale.Row)));
        tabs.Items.Add(Tab("tab_more", MoreTab()));

        var save = new Button { Content = Strings.T("save"), MinWidth = 100, IsDefault = true };
        save.Loaded += (_, _) => save.Style = Fluent.AccentButton(save);
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = Strings.T("cancel"), MinWidth = 100, IsCancel = true, Margin = new Thickness(8, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;

        Closed += (_, _) => { if (_controller.Player.PlayingFor is null) _controller.Player.Stop(); };
    }

    static string MethodName(CalculationMethod m) => m switch
    {
        CalculationMethod.Makkah => "Umm al-Qura (Makkah)",
        CalculationMethod.Mwl => "Muslim World League",
        CalculationMethod.Isna => "ISNA (North America)",
        CalculationMethod.Karachi => "Karachi",
        CalculationMethod.Egypt => "Egyptian",
        CalculationMethod.Tehran => "Tehran",
        CalculationMethod.Jafari => "Jafari",
        _ => "Custom",
    };

    static TabItem Tab(string key, UIElement content) => new()
    {
        Header = Strings.T(key),
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content },
    };

    static StackPanel Page() => new() { Margin = new Thickness(8, 0, 16, 16) };

    UIElement General()
    {
        var p = Page();
        p.Children.Add(Fluent.Label(Strings.T("language")));
        p.Children.Add(_language);
        p.Children.Add(Fluent.Label(Strings.T("location")));
        p.Children.Add(_location);
        p.Children.Add(Fluent.Label(Strings.T("method")));
        p.Children.Add(_method);
        p.Children.Add(Fluent.Label(Strings.T("madhab")));
        p.Children.Add(_madhab);

        p.Children.Add(Fluent.Label(Strings.T("offsets")));
        foreach (var prayer in Enum.GetValues<Prayer>())
        {
            (FrameworkElement Row, Slider Slider) row = Fluent.Slider(-30, 30, _s.OffsetFor(prayer), 1,
                v => Strings.Digits(v > 0 ? $"+{v:0}" : $"{v:0}"));
            _offsets[prayer] = row.Slider;
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var name = new TextBlock { Text = Strings.PrayerName(prayer), Width = 90, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(name, Dock.Left);
            line.Children.Add(name);
            line.Children.Add(row.Row);
            p.Children.Add(line);
        }

        p.Children.Add(Fluent.Label(Strings.T("hijri_offset")));
        p.Children.Add(_hijri);
        p.Children.Add(_startup);
        return p;
    }

    UIElement AdhanTab(FrameworkElement volumeRow)
    {
        var p = Page();
        p.Children.Add(Fluent.Label(Strings.T("reciter")));
        p.Children.Add(WithPreview(_reciter));
        p.Children.Add(Fluent.Label(Strings.T("fajr_reciter")));
        p.Children.Add(WithPreview(_fajrReciter));
        p.Children.Add(_previewStatus);

        p.Children.Add(Fluent.Label(Strings.T("per_prayer_sound")));
        var modes = new[] { (AdhanMode.Full, Strings.T("mode_full")), (AdhanMode.Short, Strings.T("mode_short")), (AdhanMode.Silent, Strings.T("mode_silent")) };
        foreach (var prayer in Timetable.Announced)
        {
            var box = Fluent.Combo(modes, _s.SoundFor(prayer));
            box.MinWidth = 180;
            _modes[prayer] = box;
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var name = new TextBlock { Text = Strings.PrayerName(prayer), Width = 90, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(name, Dock.Left);
            line.Children.Add(name);
            line.Children.Add(box);
            p.Children.Add(line);
        }

        p.Children.Add(Fluent.Label(Strings.T("volume")));
        p.Children.Add(volumeRow);
        p.Children.Add(_notifications);
        p.Children.Add(Fluent.Label(Strings.T("reminder")));
        p.Children.Add(_reminder);
        p.Children.Add(Fluent.Label(Strings.T("elapsed")));
        p.Children.Add(_elapsed);
        return p;
    }

    FrameworkElement WithPreview(ComboBox box)
    {
        var play = new Button { Content = "▶  " + Strings.T("preview"), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        play.Click += async (_, _) =>
        {
            var player = _controller.Player;
            if (player.IsPlaying && player.PlayingFor is null)
            {
                player.Stop();
                return;
            }
            if (AdhanCatalog.Find(Fluent.Selected(box, AdhanCatalog.BundledId)) is not { } r) return;
            play.IsEnabled = false;
            try
            {
                _previewStatus.Text = r.IsBundled || AdhanCatalog.LocalPath(r) is not null ? "" : Strings.T("downloading");
                var file = await AdhanCatalog.EnsureAsync(r);
                _previewStatus.Text = "";
                player.Play(file, (int)_volume.Value);
            }
            catch (Exception ex)
            {
                Log.Error("Preview failed", ex);
                _previewStatus.Text = Strings.T("download_failed");
            }
            finally
            {
                play.IsEnabled = true;
            }
        };
        var dock = new DockPanel();
        DockPanel.SetDock(play, Dock.Right);
        dock.Children.Add(play);
        dock.Children.Add(box);
        return dock;
    }

    UIElement LookTab(FrameworkElement opacityRow, FrameworkElement scaleRow)
    {
        var p = Page();
        p.Children.Add(Fluent.Label(Strings.T("theme")));
        p.Children.Add(_theme);
        p.Children.Add(Fluent.Label(Strings.T("opacity")));
        p.Children.Add(opacityRow);
        p.Children.Add(Fluent.Label(Strings.T("size")));
        p.Children.Add(scaleRow);
        p.Children.Add(_dock);
        p.Children.Add(_hideFullscreen);
        p.Children.Add(Fluent.Hint(Strings.T("hide_fullscreen_note")));
        p.Children.Add(Fluent.Hint(Strings.T("tip_drag")));
        return p;
    }

    UIElement MoreTab()
    {
        var p = Page();
        p.Children.Add(_ramadan);
        p.Children.Add(Fluent.Label(Strings.T("imsak_before")));
        p.Children.Add(_imsak);
        p.Children.Add(_friday);
        p.Children.Add(_makruh);

        p.Children.Add(Fluent.Label(Strings.T("app_name")));
        p.Children.Add(new TextBlock { Text = Strings.F("version", Updater.Current.ToString(3)), Foreground = Fluent.Muted });
        p.Children.Add(Fluent.Hint(Strings.T("data_note")));
        var check = new Button { Content = Strings.T("check_updates"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        check.Click += (_, _) => _checkForUpdates();
        p.Children.Add(check);
        return p;
    }

    void Save()
    {
        _s.Language = Fluent.Selected(_language, "ar");
        _s.Place = _location.Selected;
        _s.Method = _method.SelectedItem is ComboBoxItem { Tag: CalculationMethod m } ? m : null;
        _s.AsrJuristic = Fluent.Selected(_madhab, 0);
        foreach (var (prayer, slider) in _offsets) _s.Offsets[prayer] = (int)slider.Value;
        _s.HijriOffset = Fluent.Selected(_hijri, 0);
        _s.StartWithWindows = _startup.IsChecked == true;

        _s.Reciter = Fluent.Selected(_reciter, AdhanCatalog.BundledId);
        _s.FajrReciter = Fluent.Selected(_fajrReciter, AdhanCatalog.BundledId);
        foreach (var (prayer, box) in _modes) _s.Sounds[prayer] = Fluent.Selected(box, AdhanMode.Full);
        _s.Volume = (int)_volume.Value;
        _s.Notifications = _notifications.IsChecked == true;
        _s.ReminderMinutes = Fluent.Selected(_reminder, 10);
        _s.ElapsedMinutes = Fluent.Selected(_elapsed, 20);

        _s.Theme = Fluent.Selected(_theme, WidgetTheme.Emerald);
        _s.Opacity = _opacity.Value;
        _s.Scale = _scale.Value;
        _s.DockToTaskbar = _dock.IsChecked == true;
        _s.HideInFullscreen = _hideFullscreen.IsChecked == true;

        _s.RamadanMode = _ramadan.IsChecked == true;
        _s.ImsakMinutes = Fluent.Selected(_imsak, 10);
        _s.FridayReminder = _friday.IsChecked == true;
        _s.ShowMakruh = _makruh.IsChecked == true;

        // Keep what changed while this window was open (the widget's position, what was announced).
        var live = _controller.Settings;
        if (!_s.DockToTaskbar)
        {
            _s.Left = live.Left;
            _s.Top = live.Top;
        }
        _s.AnnouncedUntil = live.AnnouncedUntil;
        _s.WidgetVisible = live.WidgetVisible;
        _s.LastUpdateCheck = live.LastUpdateCheck;
        _s.Onboarded = true;

        _controller.Apply(_s);
        Close();
    }
}
