namespace LocalMeetingSubtitle.App.Infrastructure;

/// <summary>
/// Window/shell navigation surfaced to view-models so they never touch <see cref="System.Windows.Window"/>
/// types directly. Implemented by <see cref="ShellService"/> (created by the composition root).
/// </summary>
public interface IShellService
{
    void ShowMainWindow();

    void HideMainWindow();

    void ShowSettings();

    bool IsFloatingVisible { get; }

    void SetFloatingVisible(bool visible);

    void ToggleFloating();

    /// <summary>Request a full, graceful shutdown (stops transcription first).</summary>
    void RequestExit();

    /// <summary>Show a balloon/tray notification (best effort).</summary>
    void Notify(string title, string message);
}
