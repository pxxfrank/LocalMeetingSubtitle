using System.Drawing;
using System.Windows.Forms;
using LocalMeetingSubtitle.App.ViewModels;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.App.Infrastructure;

/// <summary>
/// Owns the notification-area icon. Uses <see cref="NotifyIcon"/> (WinForms) because WPF has no
/// first-class tray support. The context menu is refreshed every time it opens so it always
/// reflects the live transcription state.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly IShellService _shell;
    private readonly IAppLogger _log;

    private NotifyIcon? _icon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _startPauseItem;
    private ToolStripMenuItem? _stopItem;
    private ToolStripMenuItem? _floatingItem;
    private ToolStripMenuItem? _clickThroughItem;

    public TrayIconController(MainViewModel vm, IShellService shell, IAppLogger log)
    {
        _vm = vm;
        _shell = shell;
        _log = log;

        try
        {
            BuildMenu();

            _icon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "字幕君",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _icon.DoubleClick += (_, _) => _shell.ShowMainWindow();
        }
        catch (Exception ex)
        {
            _log.Error("Creating the tray icon failed", ex);
            Dispose();
        }
    }

    private void BuildMenu()
    {
        _menu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem("状态 / Status: …") { Enabled = false };
        _startPauseItem = new ToolStripMenuItem("开始 / Start");
        _stopItem = new ToolStripMenuItem("停止 / Stop");
        _floatingItem = new ToolStripMenuItem("显示悬浮字幕 / Show floating subtitles");
        var clickThroughItem = new ToolStripMenuItem("悬浮字幕鼠标穿透 / Floating click-through");
        _clickThroughItem = clickThroughItem;
        var openItem = new ToolStripMenuItem("打开主窗口 / Open main window");
        var settingsItem = new ToolStripMenuItem("设置 / Settings");
        var exitItem = new ToolStripMenuItem("退出 / Exit");

        _startPauseItem.Click += (_, _) => OnStartPause();
        _stopItem.Click += (_, _) => _vm.StopCommand.Execute(null);
        _floatingItem.Click += (_, _) =>
        {
            _shell.ToggleFloating();
            Refresh();
        };
        clickThroughItem.Click += (_, _) =>
        {
            _vm.FloatingClickThrough = !_vm.FloatingClickThrough;
            Refresh();
        };
        openItem.Click += (_, _) => _shell.ShowMainWindow();
        settingsItem.Click += (_, _) => _shell.ShowSettings();
        exitItem.Click += (_, _) => _shell.RequestExit();

        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(openItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startPauseItem);
        _menu.Items.Add(_stopItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_floatingItem);
        _menu.Items.Add(clickThroughItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _menu.Opening += (_, _) => Refresh();
    }

    private void OnStartPause()
    {
        switch (_vm.State)
        {
            case TranscriptionState.Transcribing:
                _vm.PauseCommand.Execute(null);
                break;
            case TranscriptionState.Paused:
                _vm.ResumeCommand.Execute(null);
                break;
            default:
                _vm.StartCommand.Execute(null);
                break;
        }
    }

    private void Refresh()
    {
        if (_statusItem is null || _startPauseItem is null || _stopItem is null || _floatingItem is null) return;

        _statusItem.Text = "状态 / Status: " + _vm.StatusText;

        _startPauseItem.Text = _vm.State switch
        {
            TranscriptionState.Transcribing => "暂停 / Pause",
            TranscriptionState.Paused => "继续 / Resume",
            _ => "开始 / Start"
        };
        _startPauseItem.Enabled = _vm.State is TranscriptionState.Idle or TranscriptionState.Ready
            or TranscriptionState.Transcribing or TranscriptionState.Paused;

        _stopItem.Enabled = _vm.State is TranscriptionState.Transcribing or TranscriptionState.Paused;

        bool floating = _shell.IsFloatingVisible;
        _floatingItem.Text = floating ? "隐藏悬浮字幕 / Hide floating subtitles" : "显示悬浮字幕 / Show floating subtitles";
        _floatingItem.Checked = floating;

        if (_clickThroughItem is not null)
        {
            _clickThroughItem.Checked = _vm.FloatingClickThrough;
            _clickThroughItem.Enabled = floating;
        }
    }

    public void ShowBalloon(string title, string message)
    {
        if (_icon is null) return;
        try
        {
            _icon.ShowBalloonTip(3000, title, Truncate(message, 250), ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _log.Debug($"ShowBalloonTip failed: {ex.Message}");
        }
    }

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max];

    public void Dispose()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }

        _menu?.Dispose();
        _menu = null;
    }
}
