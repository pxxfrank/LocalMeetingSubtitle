using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LocalMeetingSubtitle.App.ViewModels;

namespace LocalMeetingSubtitle.App;

/// <summary>
/// Always-on-top floating subtitle overlay. It never appears in the taskbar/Alt-Tab and never
/// takes activation or keyboard focus away from the meeting app. Click-through is opt-in and can
/// always be turned back off from the tray menu or its own context menu.
/// </summary>
public partial class FloatingSubtitleWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private MainViewModel? _vm;

    public FloatingSubtitleWindow()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        SourceInitialized += (_, _) => ApplyExtendedStyles();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) ApplyExtendedStyles();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = e.NewValue as MainViewModel;

        if (_vm is not null) _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FloatingClickThrough))
        {
            ApplyExtendedStyles();
        }
    }

    private void ApplyExtendedStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        int style = GetWindowLong(handle, GwlExStyle);
        style |= WsExToolWindow;   // keep out of the taskbar / Alt-Tab
        style |= WsExNoActivate;   // never steal activation

        if (_vm?.FloatingClickThrough == true)
        {
            style |= WsExTransparent;
        }
        else
        {
            style &= ~WsExTransparent;
        }

        SetWindowLong(handle, GwlExStyle, style);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove throws if the button is released mid-call; harmless.
        }
    }

    private void OnDisableClickThrough(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.FloatingClickThrough = false;
        ApplyExtendedStyles();
    }

    private void OnHideFloating(object sender, RoutedEventArgs e) => Hide();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
