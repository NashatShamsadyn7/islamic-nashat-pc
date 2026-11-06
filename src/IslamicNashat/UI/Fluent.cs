using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

/// <summary>Shared look for the app's normal windows: Fluent dark with the app's emerald accent, and small builders for settings rows.</summary>
static class Fluent
{
    static readonly Color Accent = (Color)ColorConverter.ConvertFromString("#3DBE8B");
    static readonly Color AccentHover = (Color)ColorConverter.ConvertFromString("#5FD3A2");
    static readonly Color AccentPressed = (Color)ColorConverter.ConvertFromString("#2C9C70");
    static readonly Color OnAccent = (Color)ColorConverter.ConvertFromString("#03140D");
    public static readonly SolidColorBrush Muted = new((Color)ColorConverter.ConvertFromString("#A8B4AE"));
    public static readonly SolidColorBrush Gold = new((Color)ColorConverter.ConvertFromString("#E3C567"));

    public static void Apply(Window w)
    {
        w.ThemeMode = ThemeMode.Dark;
        w.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        w.FontSize = 14;
        w.FlowDirection = UiText.Flow;
        w.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/IslamicNashat;component/Assets/icon.ico"));
        // Set directly on the window, not in a merged dictionary, or the Fluent theme's own values win.
        var r = w.Resources;
        r["AccentButtonBackground"] = new SolidColorBrush(Accent);
        r["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(AccentHover);
        r["AccentButtonBackgroundPressed"] = new SolidColorBrush(AccentPressed);
        r["AccentButtonForeground"] = new SolidColorBrush(OnAccent);
        r["AccentButtonForegroundPointerOver"] = new SolidColorBrush(OnAccent);
        r["AccentButtonForegroundPressed"] = new SolidColorBrush(OnAccent);
        r["CheckBoxCheckBackgroundFillChecked"] = new SolidColorBrush(Accent);
        r["CheckBoxCheckBackgroundFillCheckedPointerOver"] = new SolidColorBrush(AccentHover);
        r["CheckBoxCheckBackgroundFillCheckedPressed"] = new SolidColorBrush(AccentPressed);
        r["CheckBoxCheckBackgroundStrokeChecked"] = new SolidColorBrush(Accent);
        r["AccentFillColorDefaultBrush"] = new SolidColorBrush(Accent);
        r["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(AccentHover);
        r["SliderTrackValueFill"] = new SolidColorBrush(Accent);
        r["SliderTrackValueFillPointerOver"] = new SolidColorBrush(AccentHover);
        r["SliderTrackValueFillPressed"] = new SolidColorBrush(AccentPressed);
        r["SliderThumbBackground"] = new SolidColorBrush(Accent);
        r["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(AccentHover);
        r["SliderThumbBackgroundPressed"] = new SolidColorBrush(AccentPressed);
        r["TextControlFocusedBorderBrush"] = new SolidColorBrush(Accent);
    }

    public static Style? AccentButton(FrameworkElement scope) => scope.TryFindResource("AccentButtonStyle") as Style;

    public static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };

    public static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) };

    public static ComboBox Combo<T>(IEnumerable<(T Value, string Text)> items, T selected)
    {
        var box = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (value, text) in items)
        {
            var item = new ComboBoxItem { Content = text, Tag = value };
            box.Items.Add(item);
            if (EqualityComparer<T>.Default.Equals(value, selected)) box.SelectedItem = item;
        }
        if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    public static T Selected<T>(ComboBox box, T fallback) => box.SelectedItem is ComboBoxItem { Tag: T v } ? v : fallback;

    /// <summary>A slider with its value shown beside it.</summary>
    public static (FrameworkElement Row, Slider Slider) Slider(double min, double max, double value, double step, Func<double, string> format)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = format(value), MinWidth = 64, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Muted };
        slider.ValueChanged += (_, e) => label.Text = format(e.NewValue);
        var dock = new DockPanel();
        DockPanel.SetDock(label, Dock.Right);
        dock.Children.Add(label);
        dock.Children.Add(slider);
        return (dock, slider);
    }

    public static IEnumerable<(int, string)> MinuteChoices(params int[] values) =>
        values.Select(v => (v, v == 0 ? Strings.T("off") : Strings.Minutes(v)));
}
