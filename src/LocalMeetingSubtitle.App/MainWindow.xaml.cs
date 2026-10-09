using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LocalMeetingSubtitle.App.ViewModels;

namespace LocalMeetingSubtitle.App;

/// <summary>
/// Main window. Only UI wiring lives here: auto-scroll handling, copy-to-clipboard and the
/// close-to-tray prompt. All state lives in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => ScrollToEndIfFollowing();
        Closing += OnClosing;

        // Scroll notifications from the ListBox's internal ScrollViewer bubble up to us here.
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
    }

    /// <summary>Called by the shell right before an intentional shutdown so we do not re-prompt.</summary>
    public void AllowClose() => _allowClose = true;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SubtitleChanged -= OnSubtitleChanged;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = e.NewValue as MainViewModel;

        if (_vm is not null)
        {
            _vm.SubtitleChanged += OnSubtitleChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnSubtitleChanged(object? sender, EventArgs e) => ScrollToEndIfFollowing();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsTailLocked) && _vm?.IsTailLocked == true)
        {
            ScrollToEndIfFollowing();
        }
    }

    private void ScrollToEndIfFollowing()
    {
        if (_vm is null || !_vm.IsTailLocked) return;
        if (SubtitleList.Items.Count == 0) return;

        var last = SubtitleList.Items[SubtitleList.Items.Count - 1];
        SubtitleList.ScrollIntoView(last);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_vm is null) return;

        // Only react to genuine user scrolling (content unchanged) so programmatic auto-scroll
        // never flips the follow state.
        if (Math.Abs(e.ExtentHeightChange) > 0.5) return;

        double bottomEdge = e.VerticalOffset + e.ViewportHeight;
        bool atBottom = bottomEdge >= e.ExtentHeight - 8.0;

        // Never yank the view down while the user is reading history.
        _vm.IsTailLocked = atBottom;
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e) => _vm?.ToggleTheme();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        var selected = SubtitleList.SelectedItems
            .OfType<SubtitleLineViewModel>()
            .Select(l => l.Display)
            .ToList();

        if (selected.Count == 0)
        {
            selected = _vm.Lines.Select(l => l.Display).ToList();
        }

        if (selected.Count == 0) return;

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, selected));
        }
        catch
        {
            // Clipboard can be locked by another process; ignore.
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _vm is null) return;

        e.Cancel = true; // Decide explicitly; with OnExplicitShutdown closing never equals exit.

        bool busy = _vm.State is Core.Models.TranscriptionState.Transcribing or Core.Models.TranscriptionState.Paused;
        if (busy)
        {
            var result = MessageBox.Show(this,
                "正在转写中。\n\n是：最小化到托盘继续转写\n否：停止并退出程序\n\nStill transcribing.\nYes: minimize to tray\nNo: stop and exit",
                "字幕君", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) _vm.Shell.HideMainWindow();
            else _vm.Shell.RequestExit();
            return;
        }

        if (_vm.MinimizeToTrayOnClose) _vm.Shell.HideMainWindow();
        else _vm.Shell.RequestExit();
    }
}
