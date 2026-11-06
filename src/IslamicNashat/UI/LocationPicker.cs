using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

/// <summary>Search 166,404 places by name, or detect from the internet connection.</summary>
sealed class LocationPicker : StackPanel
{
    readonly TextBox _search = new() { Margin = new Thickness(0, 0, 8, 0) };
    readonly Button _detect = new() { MinWidth = 110 };
    readonly ListBox _results = new() { MaxHeight = 190, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _current = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), Foreground = Fluent.Gold };
    readonly TextBlock _status = Fluent.Hint("");
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public Place Selected { get; private set; }
    public event Action? Changed;

    public LocationPicker(Place current)
    {
        Selected = current;
        _detect.Content = Strings.T("detect");

        // WPF's TextBox has no placeholder; a hint laid over it does the job.
        var placeholder = new TextBlock
        {
            Text = Strings.T("search_city"),
            Foreground = Fluent.Muted,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
        };
        var searchBox = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        _search.Margin = new Thickness(0);
        searchBox.Children.Add(_search);
        searchBox.Children.Add(placeholder);

        var row = new DockPanel();
        DockPanel.SetDock(_detect, Dock.Right);
        row.Children.Add(_detect);
        row.Children.Add(searchBox);
        Children.Add(row);
        Children.Add(_results);
        Children.Add(_current);
        Children.Add(_status);

        _search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _debounce.Stop();
            _debounce.Start();
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RunSearch();
        };
        _results.SelectionChanged += (_, _) =>
        {
            if (_results.SelectedItem is ListBoxItem { Tag: Place p }) Choose(p, null);
        };
        _detect.Click += async (_, _) => await DetectAsync();
        ShowCurrent();
    }

    void RunSearch()
    {
        _results.Items.Clear();
        var query = _search.Text.Trim();
        if (query.Length < 2)
        {
            _results.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            foreach (var p in Places.Search(query))
                _results.Items.Add(new ListBoxItem { Content = p.HasFixedTimetable ? $"{p.Label}  ✓" : p.Label, Tag = p, FlowDirection = FlowDirection.LeftToRight });
        }
        catch (Exception ex)
        {
            Log.Error("City search failed", ex);
        }
        _results.Visibility = _results.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task DetectAsync()
    {
        _detect.IsEnabled = false;
        _status.Text = Strings.T("detecting");
        try
        {
            var found = await Places.DetectAsync();
            if (found is null)
            {
                _status.Text = Strings.T("detect_failed");
                return;
            }
            var (place, km) = found.Value;
            Choose(place, km > 30 ? Strings.F("detect_far", Strings.Digits((int)Math.Round(km))) : null);
        }
        finally
        {
            _detect.IsEnabled = true;
        }
    }

    void Choose(Place p, string? note)
    {
        Selected = p;
        _results.Visibility = Visibility.Collapsed;
        _search.Text = "";
        ShowCurrent(note);
        Changed?.Invoke();
    }

    void ShowCurrent(string? note = null)
    {
        _current.Text = "📍 " + Selected.Label;
        _status.Text = note ?? Strings.T(Selected.HasFixedTimetable ? "source_fixed" : "source_calc");
    }
}
