using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// Covers the one-time move of user data from the pre-rename folder to the current one.
/// All cases run inside a throwaway base directory, never the real %LOCALAPPDATA%.
/// </summary>
public sealed class LocalDataPathsMigrationTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "lms-migrate-" + Guid.NewGuid().ToString("N"));

    public LocalDataPathsMigrationTests() => Directory.CreateDirectory(_base);

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Moves_legacy_contents_when_the_current_folder_is_absent()
    {
        var legacy = Path.Combine(_base, LocalDataPaths.LegacyAppFolderName);
        Directory.CreateDirectory(Path.Combine(legacy, "logs"));
        File.WriteAllText(Path.Combine(legacy, "subtitles.db"), "db");
        File.WriteAllText(Path.Combine(legacy, "logs", "app.log"), "log");

        var moved = LocalDataPaths.MigrateLegacyFolderIfNeeded(_base);

        Assert.Equal(2, moved);
        Assert.False(Directory.Exists(legacy));
        var current = Path.Combine(_base, LocalDataPaths.AppFolderName);
        Assert.Equal("db", File.ReadAllText(Path.Combine(current, "subtitles.db")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(current, "logs", "app.log")));
    }

    [Fact]
    public void Does_nothing_when_there_is_no_legacy_folder()
    {
        Assert.Equal(0, LocalDataPaths.MigrateLegacyFolderIfNeeded(_base));
        Assert.False(Directory.Exists(Path.Combine(_base, LocalDataPaths.AppFolderName)));
    }

    [Fact]
    public void Never_overwrites_data_that_already_exists_in_the_current_folder()
    {
        var legacy = Path.Combine(_base, LocalDataPaths.LegacyAppFolderName);
        var current = Path.Combine(_base, LocalDataPaths.AppFolderName);
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "subtitles.db"), "legacy");
        File.WriteAllText(Path.Combine(current, "subtitles.db"), "current");

        var moved = LocalDataPaths.MigrateLegacyFolderIfNeeded(_base);

        Assert.Equal(0, moved);
        Assert.Equal("current", File.ReadAllText(Path.Combine(current, "subtitles.db")));
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(legacy, "subtitles.db")));
    }

    [Fact]
    public void Is_idempotent()
    {
        var legacy = Path.Combine(_base, LocalDataPaths.LegacyAppFolderName);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "a.txt"), "a");

        Assert.Equal(1, LocalDataPaths.MigrateLegacyFolderIfNeeded(_base));
        Assert.Equal(0, LocalDataPaths.MigrateLegacyFolderIfNeeded(_base));
    }
}
