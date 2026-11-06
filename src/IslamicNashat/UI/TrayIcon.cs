using System;
using System.Drawing;
using System.Windows.Forms;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

public sealed class TrayIcon : IDisposable
{
    readonly WidgetController _controller;
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _toggleItem = new();
    readonly ToolStripMenuItem _stopItem = new();
    readonly ToolStripMenuItem _settingsItem = new();
    readonly ToolStripMenuItem _updatesItem = new();
    readonly ToolStripMenuItem _exitItem = new();
    Action? _balloonClick;

    public TrayIcon(WidgetController controller, Action toggleWidget, Action openSettings, Action checkUpdates, Action quit)
    {
        _controller = controller;
        _toggleItem.Click += (_, _) => toggleWidget();
        _stopItem.Click += (_, _) => controller.StopAdhan();
        _settingsItem.Click += (_, _) => openSettings();
        _updatesItem.Click += (_, _) => checkUpdates();
        _exitItem.Click += (_, _) => quit();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_stopItem, _toggleItem, _settingsItem, _updatesItem, new ToolStripSeparator(), _exitItem]);
        menu.Opening += (_, _) => Refresh();

        _icon = new NotifyIcon { Icon = LoadIcon(), Visible = true, ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            // While the adhan plays the first click silences it; otherwise it shows or hides the widget.
            if (controller.Player.IsPlaying) controller.StopAdhan();
            else toggleWidget();
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var click = _balloonClick;
            _balloonClick = null;
            click?.Invoke();
        };
        controller.StateChanged += UpdateTooltip;
        Refresh();
    }

    public void ShowMessage(string title, string body, Action? onClick = null)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(8000, title, body, ToolTipIcon.None);
    }

    void Refresh()
    {
        _toggleItem.Text = Strings.T(_controller.Settings.WidgetVisible ? "tray_hide" : "tray_show");
        _stopItem.Text = Strings.T("stop_adhan");
        _stopItem.Visible = _controller.Player.IsPlaying;
        _settingsItem.Text = Strings.T("tray_settings");
        _updatesItem.Text = Strings.T("tray_updates");
        _exitItem.Text = Strings.T("tray_exit");
        _icon.ContextMenuStrip!.RightToLeft = Strings.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        UpdateTooltip();
    }

    void UpdateTooltip()
    {
        var s = _controller.State;
        var text = s is null ? Strings.T("app_name") : $"{s.Name} {s.Prefix} {s.Countdown} · {s.At}".Replace("  ", " ");
        _icon.Text = text.Length > 63 ? text[..63] : text; // Windows limit for tray tooltips
    }

    static Icon LoadIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/IslamicNashat;component/Assets/icon.ico"));
        using var stream = info!.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
