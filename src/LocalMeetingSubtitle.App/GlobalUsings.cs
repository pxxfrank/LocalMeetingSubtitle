// The WindowsDesktop SDK enables implicit usings for both WPF (System.Windows) and WinForms
// (System.Windows.Forms / System.Drawing), which makes a handful of type names ambiguous.
// These aliases pin them to the WPF types (WinForms is only used for the tray icon, which does
// not reference any of them).
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
