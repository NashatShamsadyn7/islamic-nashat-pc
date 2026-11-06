using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

/// <summary>First run: language and city on one page, then the widget appears.</summary>
public sealed class WelcomeWindow : Window
{
    readonly WidgetController _controller;
    readonly AppSettings _s;
    LocationPicker _location = null!;

    public WelcomeWindow(WidgetController controller)
    {
        _controller = controller;
        _s = controller.Settings.Clone();
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Build();
    }

    void Build()
    {
        Strings.Language = _s.Language;
        Fluent.Apply(this);
        Title = Strings.T("app_name");

        var p = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Image
        {
            Source = new BitmapImage(new System.Uri("pack://application:,,,/IslamicNashat;component/Assets/icon.png")),
            Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0),
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = Strings.T("welcome_title"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = Strings.T("app_name"), Foreground = Fluent.Gold });
        header.Children.Add(titles);
        p.Children.Add(header);
        p.Children.Add(new TextBlock { Text = Strings.T("welcome_body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0), Foreground = Fluent.Muted });

        p.Children.Add(Fluent.Label(Strings.T("language")));
        var languages = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (code, name) in new[] { ("ar", "العربية"), ("ckb", "کوردی"), ("en", "English") })
        {
            var b = new Button { Content = name, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            if (code == _s.Language) b.Loaded += (_, _) => b.Style = Fluent.AccentButton(b);
            b.Click += (_, _) =>
            {
                _s.Language = code;
                _s.Place = _location.Selected;
                Build(); // Rebuild in the chosen language and direction.
            };
            languages.Children.Add(b);
        }
        p.Children.Add(languages);

        p.Children.Add(Fluent.Label(Strings.T("location")));
        _location = new LocationPicker(_s.Place);
        p.Children.Add(_location);

        p.Children.Add(Fluent.Hint(Strings.T("tip_drag")));

        var start = new Button { Content = Strings.T("start"), MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0), IsDefault = true };
        start.Loaded += (_, _) => start.Style = Fluent.AccentButton(start);
        start.Click += (_, _) =>
        {
            _s.Place = _location.Selected;
            _s.Onboarded = true;
            _s.AnnouncedUntil = _controller.Settings.AnnouncedUntil;
            _controller.Apply(_s);
            Close();
        };
        p.Children.Add(start);
        Content = p;
    }
}
