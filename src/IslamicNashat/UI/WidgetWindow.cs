using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

/// <summary>
/// The floating bar: next prayer and a ticking countdown. A click opens today's times beneath it,
/// a drag moves it (it snaps to screen edges), a right-click opens the settings.
/// </summary>
public sealed class WidgetWindow : Window
{
    readonly WidgetController _controller;
    readonly Action _openSettings;
    readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    readonly Border _root = new();
    readonly Border _dot = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _prefix = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _countdown = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _at = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _stop = new();
    readonly StackPanel _details = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(2, 10, 2, 2) };

    Palette _palette = Palette.For(WidgetTheme.Emerald);
    bool _expanded;
    bool _anchorBottom = true;
    double _bottomEdge;
    Point _pressAt;

    public WidgetWindow(WidgetController controller, Action openSettings)
    {
        _controller = controller;
        _openSettings = openSettings;

        Title = "Islamic Nashat";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true;
        Content = _root;

        BuildBar();
        ApplyLook();

        _controller.StateChanged += Render;
        _controller.SettingsChanged += () =>
        {
            ApplyLook();
            PlaceOnScreen();
            ShowIfWanted();
        };
        _controller.FullscreenChanged += ShowIfWanted;
        _topmostTimer.Tick += (_, _) => { if (IsVisible) { Topmost = false; Topmost = true; } };
        _topmostTimer.Start();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            // Out of Alt+Tab, and clicking it never steals focus from what the user is typing in.
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            ApplyBlur();
        };
        Loaded += (_, _) => PlaceOnScreen();
        SizeChanged += (_, e) => KeepAnchored(e.PreviousSize.Width);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PlaceOnScreen);

        _root.MouseLeftButtonDown += OnPress;
        _root.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _openSettings();
        };
    }

    public void ShowIfWanted()
    {
        var wanted = _controller.Settings.WidgetVisible && !_controller.FullscreenActive;
        if (wanted && !IsVisible) Show();
        else if (!wanted && IsVisible) Hide();
    }

    void BuildBar()
    {
        _stop.Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12 };
        _stop.ToolTip = Strings.T("stop_adhan");
        _stop.Width = 28;
        _stop.Height = 28;
        _stop.Cursor = Cursors.Hand;
        _stop.Focusable = false;
        _stop.Visibility = Visibility.Collapsed;
        _stop.Template = RoundButtonTemplate();
        _stop.Click += (_, _) => _controller.StopAdhan();
        _stop.PreviewMouseLeftButtonDown += (_, e) => e.Handled = false;

        var countdown = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        countdown.Children.Add(_prefix);
        countdown.Children.Add(_countdown);
        _prefix.Margin = new Thickness(0, 0, 6, 0);
        _countdown.FlowDirection = FlowDirection.LeftToRight; // hh:mm:ss reads left to right in every language
        _countdown.TextAlignment = TextAlignment.Center;
        Typography.SetNumeralAlignment(_countdown, FontNumeralAlignment.Tabular);

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        _dot.Margin = new Thickness(2, 0, 10, 0);
        _name.Margin = new Thickness(0, 0, 14, 0);
        _at.Margin = new Thickness(14, 0, 2, 0);
        _stop.Margin = new Thickness(10, 0, 0, 0);
        bar.Children.Add(_dot);
        bar.Children.Add(_name);
        bar.Children.Add(countdown);
        bar.Children.Add(_at);
        bar.Children.Add(_stop);

        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(_details);
        _root.Child = stack;
        _root.CornerRadius = new CornerRadius(16);
        _root.Padding = new Thickness(14, 9, 12, 9);
        _root.BorderThickness = new Thickness(1);
        _root.Cursor = Cursors.SizeAll;
    }

    void ApplyLook()
    {
        var s = _controller.Settings;
        _palette = Palette.For(s.Theme);
        _root.Background = _palette.BackgroundBrush(s.Opacity);
        _root.BorderBrush = _palette.Brush(_palette.Border);
        _root.LayoutTransform = new ScaleTransform(s.Scale, s.Scale);
        _root.Effect = s.Theme == WidgetTheme.Light
            ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.18 }
            : null;
        FlowDirection = UiText.Flow;
        _name.Foreground = _palette.Brush(_palette.Text);
        _countdown.Foreground = _palette.Brush(_palette.Text);
        _prefix.Foreground = _palette.Brush(_palette.Muted);
        _at.Foreground = _palette.Brush(_palette.Muted);
        _stop.Foreground = _palette.Brush(_palette.Text);
        _stop.Background = _palette.Brush(_palette.Row);
        _stop.ToolTip = Strings.T("stop_adhan");
        // Reserve the widest the countdown can get, so the bar doesn't twitch as digits change.
        var widest = new TextBlock { Text = Strings.Digits("88:88:88"), FontSize = _countdown.FontSize, FontWeight = _countdown.FontWeight, FontFamily = FontFamily };
        widest.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _countdown.MinWidth = widest.DesiredSize.Width + 2;
        ApplyBlur();
        Render();
    }

    void ApplyBlur()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var policy = new NativeMethods.AccentPolicy { AccentState = _controller.Settings.Theme == WidgetTheme.Glass ? 3 : 0 };
        var size = Marshal.SizeOf(policy);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new NativeMethods.WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    void Render()
    {
        var st = _controller.State;
        if (st is null) return;

        _name.Text = st.Name;
        _prefix.Text = st.Prefix;
        _prefix.Visibility = st.Prefix.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _countdown.Text = st.Countdown;
        _at.Text = st.At;

        var accent = st.Phase switch
        {
            WidgetPhase.Soon => _palette.Gold,
            WidgetPhase.Elapsed => _palette.Accent,
            _ => st.MakruhNow is not null ? _palette.Warning : _palette.Accent,
        };
        _dot.Background = _palette.Brush(accent);
        _countdown.Foreground = _palette.Brush(st.Phase == WidgetPhase.Soon ? _palette.Gold : _palette.Text);
        _dot.ToolTip = st.MakruhNow;

        var playing = _controller.Player.IsPlaying;
        _stop.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        Pulse(playing || st.Phase == WidgetPhase.Soon);

        if (_expanded) RenderDetails(st);
    }

    bool _pulsing;

    void Pulse(bool on)
    {
        if (on == _pulsing) return;
        _pulsing = on;
        if (on)
            _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        else
            _dot.BeginAnimation(OpacityProperty, null);
    }

    void RenderDetails(WidgetState st)
    {
        _details.Children.Clear();
        _details.Children.Add(new Border { Height = 1, Background = _palette.Brush(_palette.Border), Margin = new Thickness(0, 0, 0, 10) });

        _details.Children.Add(Text(st.Place, 14, FontWeights.SemiBold, _palette.Text));
        _details.Children.Add(Text(st.DateLine, 12, FontWeights.Normal, _palette.Muted, new Thickness(0, 2, 0, 0)));
        if (st.HijriLine is not null) _details.Children.Add(Text(st.HijriLine, 12.5, FontWeights.Normal, _palette.Gold, new Thickness(0, 2, 0, 0)));

        if (st.MakruhNow is not null)
        {
            var banner = new Border
            {
                Background = _palette.Brush(Color.FromArgb(40, _palette.Warning.R, _palette.Warning.G, _palette.Warning.B)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 8, 0, 0),
                Child = Text(st.MakruhNow, 12.5, FontWeights.SemiBold, _palette.Warning),
            };
            _details.Children.Add(banner);
        }

        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var row in st.Rows) list.Children.Add(Row(row, 14));
        _details.Children.Add(list);

        if (st.MakruhRows.Count > 0)
        {
            _details.Children.Add(Text(Strings.T("makruh_title"), 12, FontWeights.SemiBold, _palette.Muted, new Thickness(0, 10, 0, 4)));
            foreach (var row in st.MakruhRows) _details.Children.Add(Row(row, 12, makruh: true));
        }

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        var settings = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13 },
            Width = 28, Height = 28, Cursor = Cursors.Hand, Focusable = false,
            Template = RoundButtonTemplate(),
            Foreground = _palette.Brush(_palette.Text),
            Background = _palette.Brush(_palette.Row),
            ToolTip = Strings.T("settings"),
        };
        settings.Click += (_, _) => _openSettings();
        DockPanel.SetDock(settings, Dock.Right);
        footer.Children.Add(settings);
        var note = Text(st.SourceNote, 10.5, FontWeights.Normal, _palette.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxWidth = 230;
        note.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(note);
        _details.Children.Add(footer);
    }

    UIElement Row(TimeRow row, double size, bool makruh = false)
    {
        var color = row.IsNext ? _palette.Text : row.IsPast ? _palette.Muted : _palette.Text;
        var grid = new Grid { MinWidth = 250 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Text(row.Name, size, row.IsNext ? FontWeights.SemiBold : FontWeights.Normal, color);
        var time = Text(row.Time, size, row.IsNext ? FontWeights.SemiBold : FontWeights.Normal, row.IsNext ? (makruh ? _palette.Warning : _palette.Gold) : color);
        Grid.SetColumn(time, 1);
        grid.Children.Add(name);
        grid.Children.Add(time);
        return new Border
        {
            Child = grid,
            Padding = new Thickness(10, makruh ? 2 : 5, 10, makruh ? 2 : 5),
            CornerRadius = new CornerRadius(8),
            Background = row.IsNext && !makruh ? _palette.Brush(_palette.Row) : Brushes.Transparent,
            Opacity = row.IsPast && !row.IsNext ? 0.7 : 1,
        };
    }

    static TextBlock Text(string text, double size, FontWeight weight, Color color, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = new SolidColorBrush(color),
        Margin = margin ?? new Thickness(0),
    };

    static ControlTemplate RoundButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindButton(d)) return;
        _pressAt = new Point(Left, Top);
        try { DragMove(); }
        catch (InvalidOperationException) { return; }

        if (Math.Abs(Left - _pressAt.X) < 3 && Math.Abs(Top - _pressAt.Y) < 3)
        {
            ToggleExpanded();
            return;
        }
        SnapToEdges();
        RememberPosition();
    }

    static bool FindButton(DependencyObject d)
    {
        for (var e = d; e is not null; e = VisualTreeHelper.GetParent(e))
            if (e is Button) return true;
        return false;
    }

    void ToggleExpanded()
    {
        _expanded = !_expanded;
        _details.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        if (_expanded && _controller.State is { } st) RenderDetails(st);
    }

    System.Windows.Forms.Screen CurrentScreen() =>
        System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left + ActualWidth / 2) * Dpi), (int)((Top + ActualHeight / 2) * Dpi)));

    double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

    Rect WorkArea(System.Windows.Forms.Screen screen)
    {
        var w = screen.WorkingArea;
        var scale = Dpi;
        return new Rect(w.Left / scale, w.Top / scale, w.Width / scale, w.Height / scale);
    }

    void PlaceOnScreen()
    {
        if (!IsLoaded && ActualWidth == 0) UpdateLayout();
        var s = _controller.Settings;
        var primary = WorkArea(System.Windows.Forms.Screen.PrimaryScreen!);
        const double margin = 12;

        if (s.DockToTaskbar || s.Left is null || s.Top is null || !OnAnyScreen(s.Left.Value, s.Top.Value))
        {
            // Bottom corner by the clock: right in left-to-right Windows, which is how nearly every taskbar is laid out.
            Left = primary.Right - ActualWidth - margin;
            Top = primary.Bottom - ActualHeight - margin;
        }
        else
        {
            Left = s.Left.Value;
            Top = s.Top.Value;
        }
        UpdateAnchor();
    }

    bool OnAnyScreen(double left, double top)
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            if (WorkArea(screen).Contains(new Point(left + 20, top + 10))) return true;
        return false;
    }

    void UpdateAnchor()
    {
        var area = WorkArea(CurrentScreen());
        // In the lower half the widget grows upwards when opened, so it never runs under the taskbar.
        _anchorBottom = Top + ActualHeight / 2 > area.Top + area.Height / 2;
        _bottomEdge = Top + ActualHeight;
    }

    void KeepAnchored(double previousWidth)
    {
        if (!IsLoaded) return;
        // Right-to-left, the widget's start is its right edge: grow and shrink to the left.
        if (Strings.IsRightToLeft && previousWidth > 0) Left -= ActualWidth - previousWidth;
        var area = WorkArea(CurrentScreen());
        if (_controller.Settings.DockToTaskbar)
        {
            Left = area.Right - ActualWidth - 12;
            Top = area.Bottom - ActualHeight - 12;
            return;
        }
        if (_anchorBottom) Top = _bottomEdge - ActualHeight;
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
    }

    void SnapToEdges()
    {
        var area = WorkArea(CurrentScreen());
        const double snap = 18, margin = 8;
        if (Math.Abs(Left - area.Left) < snap) Left = area.Left + margin;
        if (Math.Abs(area.Right - (Left + ActualWidth)) < snap) Left = area.Right - ActualWidth - margin;
        if (Math.Abs(Top - area.Top) < snap) Top = area.Top + margin;
        if (Math.Abs(area.Bottom - (Top + ActualHeight)) < snap) Top = area.Bottom - ActualHeight - margin;
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
    }

    void RememberPosition()
    {
        UpdateAnchor();
        var s = _controller.Settings;
        s.Left = Left;
        s.Top = Top;
        s.DockToTaskbar = false;
        _controller.SaveQuietly();
    }
}
