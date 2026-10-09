using System.Windows;
using LocalMeetingSubtitle.App.ViewModels;

namespace LocalMeetingSubtitle.App;

/// <summary>
/// Speaker-management window. Code-behind only loads data on show and closes the window when the
/// view-model asks; all state lives in <see cref="SpeakerManagementViewModel"/>.
/// </summary>
public partial class SpeakerManagementWindow : Window
{
    private SpeakerManagementViewModel? _vm;

    public SpeakerManagementWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The view-model guards against concurrent loads, so this is a no-op when the shell has
        // already kicked one off.
        _ = _vm?.LoadAsync();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.CloseRequested -= OnCloseRequested;

        _vm = e.NewValue as SpeakerManagementViewModel;

        if (_vm is not null) _vm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
