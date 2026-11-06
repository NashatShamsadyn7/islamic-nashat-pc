using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

/// <summary>Downloads the new installer and runs it silently; it brings the app back by itself.</summary>
public sealed class UpdateWindow : Window
{
    readonly CancellationTokenSource _cts = new();

    public UpdateWindow(UpdateInfo update)
    {
        Fluent.Apply(this);
        Title = Strings.T("update_title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var text = new TextBlock { Text = Strings.F("update_body", update.Version.ToString(3)), TextWrapping = TextWrapping.Wrap };
        var progress = new ProgressBar { Height = 6, Margin = new Thickness(0, 16, 0, 0), Maximum = 1, Visibility = Visibility.Collapsed };
        var status = Fluent.Hint("");
        var now = new Button { Content = Strings.T("update_now"), MinWidth = 120, IsDefault = true };
        now.Loaded += (_, _) => now.Style = Fluent.AccentButton(now);
        var later = new Button { Content = Strings.T("later"), MinWidth = 100, Margin = new Thickness(8, 0, 8, 0), IsCancel = true };
        later.Click += (_, _) => Close();

        now.Click += async (_, _) =>
        {
            now.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            try
            {
                var file = await Updater.DownloadAsync(update, new Progress<double>(v => progress.Value = v), _cts.Token);
                Updater.Install(file);
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error("Update failed", ex);
                status.Text = Strings.T("update_failed");
                now.IsEnabled = true;
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(later);
        buttons.Children.Add(now);
        var p = new StackPanel { Margin = new Thickness(24) };
        p.Children.Add(text);
        p.Children.Add(progress);
        p.Children.Add(status);
        p.Children.Add(buttons);
        Content = p;
        Closed += (_, _) => _cts.Cancel();
    }
}
