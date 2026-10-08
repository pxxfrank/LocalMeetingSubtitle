namespace LocalMeetingSubtitle.App.Infrastructure;

/// <summary>
/// Named-mutex single-instance guard. The mutex is held for the lifetime of the process; if a
/// second instance starts it observes <see cref="IsPrimary"/> == false and exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;

    public SingleInstanceGuard(string name)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            IsPrimary = createdNew;
        }
        catch (Exception)
        {
            // If the mutex cannot be created we must not block the app from starting.
            _mutex = null;
            IsPrimary = true;
        }
    }

    /// <summary>True when this process is the first/only instance.</summary>
    public bool IsPrimary { get; }

    public void Dispose()
    {
        if (_mutex is null) return;
        try
        {
            if (IsPrimary) _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread — ignore.
        }
        catch
        {
            // Ignore any other disposal failure.
        }
        finally
        {
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
