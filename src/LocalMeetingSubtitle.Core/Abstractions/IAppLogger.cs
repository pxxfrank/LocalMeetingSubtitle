namespace LocalMeetingSubtitle.Core.Abstractions;

public interface IAppLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>No-op logger used as a safe default so Core never depends on a logging framework.</summary>
public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
