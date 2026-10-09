namespace LocalMeetingSubtitle.Storage;

/// <summary>
/// Resolves the per-user writable data directory for the application. Everything the app
/// persists lives under <c>%LOCALAPPDATA%\字幕君\</c> so it never touches
/// Program Files (which is read-only for standard users).
/// </summary>
public static class LocalDataPaths
{
    public const string AppFolderName = "字幕君";

    /// <summary>
    /// Folder name used before the app was renamed to 字幕君. Kept only so an existing installation's
    /// data can be moved across once; see <see cref="MigrateLegacyFolderIfNeeded"/>.
    /// </summary>
    public const string LegacyAppFolderName = "LocalMeetingSubtitle";

    /// <summary>The per-user root: <c>%LOCALAPPDATA%\字幕君</c>.</summary>
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

    /// <summary>
    /// One-time move of user data from <see cref="LegacyAppFolderName"/> to <see cref="AppFolderName"/>.
    /// It runs only when the current folder is missing or empty (so it never merges on top of live data)
    /// and moves each top-level entry separately, so one locked file cannot abort the whole migration.
    /// Returns the number of entries moved; 0 means there was nothing to do.
    /// </summary>
    /// <param name="localAppDataRoot">Base directory to migrate inside; defaults to the real %LOCALAPPDATA%.</param>
    public static int MigrateLegacyFolderIfNeeded(string? localAppDataRoot = null)
    {
        var baseDir = string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataRoot;

        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return 0;
        }

        var legacy = Path.Combine(baseDir, LegacyAppFolderName);
        if (!Directory.Exists(legacy))
        {
            return 0;
        }

        var current = Path.Combine(baseDir, AppFolderName);
        if (Directory.Exists(current) && Directory.EnumerateFileSystemEntries(current).Any())
        {
            return 0;
        }

        Directory.CreateDirectory(current);

        var moved = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(legacy))
        {
            var destination = Path.Combine(current, Path.GetFileName(entry));
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Move(entry, destination);
                }
                else
                {
                    File.Move(entry, destination);
                }

                moved++;
            }
            catch (IOException)
            {
                // Locked (an open database, say); leave it behind for a later attempt.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(legacy).Any())
            {
                Directory.Delete(legacy);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return moved;
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
