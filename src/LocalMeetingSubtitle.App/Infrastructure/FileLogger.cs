using System.IO;
using System.Text;
using LocalMeetingSubtitle.Core.Abstractions;

namespace LocalMeetingSubtitle.App.Infrastructure;

/// <summary>
/// Simple file + in-memory logger. Writes one line per entry to a per-day log file under the
/// logs directory and keeps a bounded in-memory tail so the UI (or a crash report) can show the
/// most recent activity. Logging never throws — an app that cannot log must still run.
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private const int MemoryCapacity = 500;

    private readonly object _gate = new();
    private readonly Queue<string> _memory = new();
    private readonly string _filePath;

    public FileLogger(string logsDirectory)
    {
        LogsDirectory = logsDirectory;
        try
        {
            Directory.CreateDirectory(logsDirectory);
        }
        catch
        {
            // Fall back to the temp directory if the configured location is unusable.
            LogsDirectory = Path.GetTempPath();
        }

        _filePath = Path.Combine(LogsDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public string LogsDirectory { get; }

    public string LogFilePath => _filePath;

    public void Debug(string message) => Write("DEBUG", message, null);

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    /// <summary>Returns a snapshot of the most recent in-memory log lines (oldest first).</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _memory.ToArray();
        }
    }

    private void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (exception is not null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        var text = line.ToString();

        lock (_gate)
        {
            _memory.Enqueue(text);
            while (_memory.Count > MemoryCapacity) _memory.Dequeue();

            try
            {
                File.AppendAllText(_filePath, text + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
                // Ignore: logging must never take the app down.
            }
        }
    }
}
