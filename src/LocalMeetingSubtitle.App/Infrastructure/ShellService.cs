using System.Windows;
using System.Windows.Threading;
using LocalMeetingSubtitle.App.ViewModels;
using LocalMeetingSubtitle.Core.Abstractions;

namespace LocalMeetingSubtitle.App.Infrastructure;

/// <summary>
/// Owns the application's windows (main, settings, floating subtitles) and exposes coarse
/// navigation to view-models. All window creation happens on the WPF dispatcher thread.
/// </summary>
public sealed class ShellService : IShellService
{
    private readonly IAppLogger _log;
    private readonly Func<SettingsViewModel> _settingsFactory;
    private readonly Func<SpeakerManagementViewModel> _speakerManagementFactory;
    private readonly Action _requestExit;
    private readonly Action<string, string> _notify;

    private MainViewModel? _main;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private SpeakerManagementWindow? _speakerManagementWindow;
    private FloatingSubtitleWindow? _floatingWindow;

    public ShellService(
        IAppLogger log,
        Func<SettingsViewModel> settingsFactory,
        Func<SpeakerManagementViewModel> speakerManagementFactory,
        Action requestExit,
        Action<string, string> notify)
    {
        _log = log;
        _settingsFactory = settingsFactory;
        _speakerManagementFactory = speakerManagementFactory;
        _requestExit = requestExit;
        _notify = notify;
    }

    /// <summary>Associates the main view-model once it has been constructed.</summary>
    public void Attach(MainViewModel main) => _main = main;

    public MainWindow? MainWindow => _mainWindow;

    private static Dispatcher UiDispatcher => System.Windows.Application.Current.Dispatcher;

    public void ShowMainWindow()
    {
        UiDispatcher.Invoke(() =>
        {
            if (_mainWindow is null)
            {
                _mainWindow = new MainWindow();
                if (_main is not null) _mainWindow.DataContext = _main;
            }

            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
        });
    }

    public void HideMainWindow() => UiDispatcher.Invoke(() => _mainWindow?.Hide());

    public void ShowSettings()
    {
        UiDispatcher.Invoke(() =>
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            try
            {
                var vm = _settingsFactory();
                _settingsWindow = new SettingsWindow { DataContext = vm };
                _ = vm.LoadAsync();
                if (_mainWindow is { IsVisible: true })
                {
                    _settingsWindow.Owner = _mainWindow;
                }
                _settingsWindow.Show();
            }
            catch (Exception ex)
            {
                _log.Error("Failed to open settings window", ex);
                MessageBox.Show("Unable to open settings: " + ex.Message, "字幕君",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }

    public void ShowSpeakerManagement()
    {
        UiDispatcher.Invoke(() =>
        {
            if (_speakerManagementWindow is { IsVisible: true })
            {
                _speakerManagementWindow.Activate();
                return;
            }

            try
            {
                var vm = _speakerManagementFactory();
                _speakerManagementWindow = new SpeakerManagementWindow { DataContext = vm };
                _ = vm.LoadAsync();
                if (_mainWindow is { IsVisible: true })
                {
                    _speakerManagementWindow.Owner = _mainWindow;
                }
                _speakerManagementWindow.Show();
            }
            catch (Exception ex)
            {
                _log.Error("Failed to open the speaker-management window", ex);
                MessageBox.Show("Unable to open speaker management: " + ex.Message, "字幕君",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }

    public bool IsFloatingVisible => _floatingWindow is { IsVisible: true };

    public void ToggleFloating() => SetFloatingVisible(!IsFloatingVisible);

    public void SetFloatingVisible(bool visible)
    {
        UiDispatcher.Invoke(() =>
        {
            if (visible)
            {
                if (_main is null) return;
                _floatingWindow ??= new FloatingSubtitleWindow { DataContext = _main };
                if (!_floatingWindow.IsVisible) _floatingWindow.Show();
            }
            else
            {
                _floatingWindow?.Hide();
            }
        });
    }

    public void RequestExit() => _requestExit();

    public string? PickAudioFile()
    {
        string? result = null;
        UiDispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择会议录音 / Choose a meeting recording",
                Filter = "音频 / Audio (*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma)|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma|所有文件 / All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                result = dialog.FileName;
            }
        });
        return result;
    }

    public void Notify(string title, string message)
    {
        try
        {
            _notify(title, message);
        }
        catch (Exception ex)
        {
            _log.Warn($"Tray notification failed: {ex.Message}");
        }
    }
}
