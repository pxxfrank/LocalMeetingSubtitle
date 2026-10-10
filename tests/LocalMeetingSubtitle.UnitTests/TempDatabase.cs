using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// A throwaway SQLite database in a private temp directory. Disposing removes the file (and its
/// WAL siblings) so each test starts clean and leaves nothing behind.
/// </summary>
internal sealed class TempDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private TempDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lms-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Database = new SqliteDatabase(Path.Combine(_directory, "subtitles.db"));
    }

    public SqliteDatabase Database { get; }

    public static async Task<TempDatabase> CreateAsync()
    {
        var temp = new TempDatabase();
        await temp.Database.InitializeAsync();
        return temp;
    }

    public SqliteSubtitleRepository CreateSubtitleRepository() => new(Database);

    public SqliteHotwordRepository CreateHotwordRepository() => new(Database);

    public SqliteSettingsRepository CreateSettingsRepository() => new(Database);

    public SqliteSpeakerRepository CreateSpeakerRepository() => new(Database);

    public SqliteAudioAssetRepository CreateAudioAssetRepository() => new(Database);

    public SqliteMediaFileRepository CreateMediaFileRepository() => new(Database);

    public SqliteTranscriptionJobRepository CreateTranscriptionJobRepository() => new(Database);

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(25);
            }
        }
    }
}
