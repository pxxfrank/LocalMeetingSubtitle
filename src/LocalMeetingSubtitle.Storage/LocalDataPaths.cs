namespace LocalMeetingSubtitle.Storage;

/// <summary>
/// Resolves the per-user writable data directory for the application. Everything the app
/// persists lives under <c>%LOCALAPPDATA%\SubtitleJun\</c> so it never touches Program Files
/// (which is read-only for standard users).
///
/// The folder name is deliberately **ASCII**: sherpa-onnx's native layer cannot open model files
/// under a path with non-ASCII characters (it logs "Errors in config!" and builds a recognizer
/// that never decodes). The product's display name is still 字幕君 — only on-disk paths are ASCII.
/// </summary>
public static class LocalDataPaths
{
    public const string AppFolderName = "SubtitleJun";

    /// <summary>
    /// Folder names used by earlier versions, newest first. Data is moved out of whichever of these
    /// exists so an upgrade keeps the user's history.
    /// </summary>
    public static readonly string[] LegacyAppFolderNames = { "字幕君", "LocalMeetingSubtitle" };

    /// <summary>The per-user root: <c>%LOCALAPPDATA%\SubtitleJun</c>.</summary>
    public static string Root { get; } = ResolveRoot();

    public static string DatabaseFile => Path.Combine(Root, "subtitles.db");
    public static string LogsDirectory => Path.Combine(Root, "logs");
    public static string ModelsDirectory => Path.Combine(Root, "models");
    public static string ExportsDirectory => Path.Combine(Root, "exports");

    /// <summary>Holds opt-in post-meeting recordings (only ever created when the user records).</summary>
    public static string RecordingsDirectory => Path.Combine(Root, "recordings");

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

    public static string EnsureRecordingsDirectory()
    {
        Directory.CreateDirectory(RecordingsDirectory);
        return RecordingsDirectory;
    }

    /// <summary>Creates the root plus every well-known sub-directory.</summary>
    public static void EnsureAllDirectories()
    {
        EnsureRootDirectory();
        EnsureLogsDirectory();
        EnsureModelsDirectory();
        EnsureExportsDirectory();
        EnsureRecordingsDirectory();
    }

    /// <summary>
    /// One-time move of user data from any <see cref="LegacyAppFolderNames"/> folder to
    /// <see cref="AppFolderName"/>. It runs only when the current folder is missing or empty (so it
    /// never merges on top of live data) and moves each top-level entry separately, so one locked
    /// file cannot abort the whole migration. Returns the number of entries moved.
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

        var current = Path.Combine(baseDir, AppFolderName);
        if (Directory.Exists(current) && Directory.EnumerateFileSystemEntries(current).Any())
        {
            return 0;
        }

        var moved = 0;
        foreach (var legacyName in LegacyAppFolderNames)
        {
            var legacy = Path.Combine(baseDir, legacyName);
            if (!Directory.Exists(legacy))
            {
                continue;
            }

            moved += MoveContents(legacy, current);
        }

        return moved;
    }

    private static int MoveContents(string legacy, string current)
    {
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
