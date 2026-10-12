using System.Windows;
using LocalMeetingSubtitle.App.ViewModels;

namespace LocalMeetingSubtitle.App;

/// <summary>Settings window. Code-behind only closes the window when the view-model asks.</summary>
public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            Closing -= OnClosing;
        }

        _vm = e.NewValue as SettingsViewModel;

        if (_vm is not null)
        {
            _vm.CloseRequested += OnCloseRequested;
            // The window's ✕ button does not go through CloseCommand, so cancel any in-flight
            // model download here (the manager deletes its partial ".download" file on cancel).
            Closing += OnClosing;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => _vm?.OnClosing();

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
