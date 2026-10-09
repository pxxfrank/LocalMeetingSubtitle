using System.Globalization;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SqliteDatabaseTests
{
    [Fact]
    public async Task Initialize_EnablesWalAndCreatesVersionedSchema()
    {
        await using var temp = await TempDatabase.CreateAsync();

        await using var connection = await temp.Database.OpenConnectionAsync();

        await using var journalMode = connection.CreateCommand();
        journalMode.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", ((string?)await journalMode.ExecuteScalarAsync())!.ToLowerInvariant());

        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, Convert.ToInt64(await foreignKeys.ExecuteScalarAsync()));

        await using var schemaVersion = connection.CreateCommand();
        schemaVersion.CommandText = "SELECT COUNT(*) FROM schema_version;";
        Assert.Equal(4, Convert.ToInt32(await schemaVersion.ExecuteScalarAsync()));

        await using var tables = connection.CreateCommand();
        tables.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN " +
            "('sessions','segments','metrics','hotword_groups','hotwords','correction_rules','settings'," +
            "'diarization_runs','speakers','speaker_intervals','speaker_assignments','audio_assets');";
        Assert.Equal(12, Convert.ToInt32(await tables.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        await using var temp = await TempDatabase.CreateAsync();
        await temp.Database.InitializeAsync();
        await temp.Database.InitializeAsync();

        await using var connection = await temp.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_version;";
        Assert.Equal(4, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task CreateSession_EnsuresDatabaseDirectoryExists_AndPersistsAcrossConnections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-paths-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "nested", "subtitles.db");
        try
        {
            var database = new SqliteDatabase(file);
            var repository = new SqliteSubtitleRepository(database);
            await repository.InitializeAsync();

            Assert.True(File.Exists(file));

            var session = await repository.CreateSessionAsync(TestData.NewSession());
            await repository.AppendSegmentAsync(TestData.NewSegment(session.SessionId, 0, "durable"));

            // A brand new repository over the same file must see the committed rows.
            var reopened = new SqliteSubtitleRepository(new SqliteDatabase(file));
            var segments = await reopened.GetSegmentsAsync(session.SessionId);

            Assert.Single(segments);
            Assert.Equal("durable", segments[0].OriginalText);

            await repository.DisposeAsync();
            await reopened.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenConnection_AppliesTimestampsAsRoundTripIsoStrings()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = TestData.NewSession();
        session.StartTime = new DateTimeOffset(2026, 5, 6, 7, 8, 9, 123, TimeSpan.FromHours(8));
        await repository.CreateSessionAsync(session);

        await using var connection = await temp.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT StartTime FROM sessions WHERE SessionId = $id;";
        command.Parameters.AddWithValue("$id", session.SessionId);
        var stored = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(stored);
        Assert.Equal(session.StartTime, DateTimeOffset.Parse(stored!, CultureInfo.InvariantCulture));
    }
}
