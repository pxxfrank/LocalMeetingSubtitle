namespace LocalMeetingSubtitle.Storage;

/// <summary>
/// Resolves the per-user writable data directory for the application. Everything the app
/// persists lives under <c>%LOCALAPPDATA%\LocalMeetingSubtitle\</c> so it never touches
/// Program Files (which is read-only for standard users).
/// </summary>
public static class LocalDataPaths
{
    public const string AppFolderName = "LocalMeetingSubtitle";

    /// <summary>The per-user root: <c>%LOCALAPPDATA%\LocalMeetingSubtitle</c>.</summary>
    public static string Root { get; } = ResolveRoot();

    public static string DatabaseFile => Path.Combine(Root, "subtitles.db");
    public static string LogsDirectory => Path.Combine(Root, "logs");
    public static string ModelsDirectory => Path.Combine(Root, "models");
    public static string ExportsDirectory => Path.Combine(Root, "exports");

    public static string EnsureRootDirectory()
    {
        Directory.CreateDirectory(Root);
        return Root;
    }

    public static string EnsureLogsDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
        return LogsDirectory;
    }

    public static string EnsureModelsDirectory()
    {
        Directory.CreateDirectory(ModelsDirectory);
        return ModelsDirectory;
    }

    public static string EnsureExportsDirectory()
    {
        Directory.CreateDirectory(ExportsDirectory);
        return ExportsDirectory;
    }

    /// <summary>Creates the root plus every well-known sub-directory.</summary>
    public static void EnsureAllDirectories()
    {
        EnsureRootDirectory();
        EnsureLogsDirectory();
        EnsureModelsDirectory();
        EnsureExportsDirectory();
    }

    private static string ResolveRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            // Extremely unlikely on Windows; fall back to the temp directory so we never fail hard.
            localAppData = Path.GetTempPath();
        }
        return Path.Combine(localAppData, AppFolderName);
    }
}
