using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LocalMeetingSubtitle.Storage;

/// <summary>
/// Owns the SQLite connection string and file path, enables WAL + foreign keys + NORMAL
/// synchronous mode, guarantees the data directory exists, and applies versioned migrations
/// recorded in a <c>schema_version</c> table (each migration runs in its own transaction).
/// </summary>
public sealed class SqliteDatabase : IAsyncDisposable
{
    private const string JournalModeWal = "PRAGMA journal_mode=WAL;";
    private const string ForeignKeysOn = "PRAGMA foreign_keys=ON;";
    private const string SynchronousNormal = "PRAGMA synchronous=NORMAL;";

    private static readonly Migration[] Migrations =
    {
        new(1, "core session/segment/metric schema", new[]
        {
            """
            CREATE TABLE IF NOT EXISTS sessions (
                SessionId       TEXT    NOT NULL PRIMARY KEY,
                Title           TEXT    NOT NULL DEFAULT '',
                StartTime       TEXT    NOT NULL,
                EndTime         TEXT    NULL,
                Status          INTEGER NOT NULL,
                ModelId         TEXT    NOT NULL DEFAULT '',
                AudioDeviceId   TEXT    NOT NULL DEFAULT '',
                AudioDeviceName TEXT    NOT NULL DEFAULT ''
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS segments (
                SegmentId       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                SessionId       TEXT    NOT NULL,
                SequenceNumber  INTEGER NOT NULL,
                StartOffsetMs   INTEGER NOT NULL,
                EndOffsetMs     INTEGER NOT NULL,
                OriginalText    TEXT    NOT NULL DEFAULT '',
                CorrectedText   TEXT    NOT NULL DEFAULT '',
                IsEdited        INTEGER NOT NULL DEFAULT 0,
                CreatedAt       TEXT    NOT NULL,
                UNIQUE (SessionId, SequenceNumber)
            );
            """,
            "CREATE INDEX IF NOT EXISTS ix_segments_session ON segments (SessionId, SequenceNumber);",
            """
            CREATE TABLE IF NOT EXISTS metrics (
                Id                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                SessionId           TEXT    NOT NULL,
                Timestamp           TEXT    NOT NULL,
                CpuPercent          REAL    NOT NULL,
                WorkingSetMb        REAL    NOT NULL,
                Rtf                 REAL    NOT NULL,
                QueueLength         INTEGER NOT NULL,
                MaxQueueLength      INTEGER NOT NULL,
                DroppedAudioSeconds REAL    NOT NULL,
                EndToEndLatencyMs   REAL    NOT NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS ix_metrics_session ON metrics (SessionId, Timestamp);"
        }),
        new(2, "hotword schema", new[]
        {
            """
            CREATE TABLE IF NOT EXISTS hotword_groups (
                GroupId   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name      TEXT    NOT NULL DEFAULT 'Default',
                Enabled   INTEGER NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS hotwords (
                Id      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Text    TEXT    NOT NULL DEFAULT '',
                Enabled INTEGER NOT NULL DEFAULT 1,
                GroupId INTEGER NULL,
                Score   REAL    NOT NULL DEFAULT 1.5
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS correction_rules (
                Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Pattern        TEXT    NOT NULL DEFAULT '',
                Replacement    TEXT    NOT NULL DEFAULT '',
                Enabled        INTEGER NOT NULL DEFAULT 1,
                IsRegex        INTEGER NOT NULL DEFAULT 0,
                Priority       INTEGER NOT NULL DEFAULT 0,
                WholeTokenOnly INTEGER NOT NULL DEFAULT 1,
                CaseSensitive  INTEGER NOT NULL DEFAULT 0
            );
            """
        }),
        new(3, "settings schema", new[]
        {
            """
            CREATE TABLE IF NOT EXISTS settings (
                Key   TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """
        }),
        new(4, "speaker diarization schema", new[]
        {
            """
            CREATE TABLE IF NOT EXISTS diarization_runs (
                RunId                 TEXT    NOT NULL PRIMARY KEY,
                SessionId             TEXT    NOT NULL,
                AudioAssetId          TEXT    NULL,
                CreatedAt             TEXT    NOT NULL,
                Status                INTEGER NOT NULL,
                RequestedSpeakerCount INTEGER NOT NULL DEFAULT 0,
                ResolvedSpeakerCount  INTEGER NOT NULL DEFAULT 0,
                ClusteringThreshold   REAL    NOT NULL DEFAULT 0.5,
                MinDurationOn         REAL    NOT NULL DEFAULT 0.3,
                MinDurationOff        REAL    NOT NULL DEFAULT 0.5,
                SegmentationModelId   TEXT    NOT NULL DEFAULT '',
                EmbeddingModelId      TEXT    NOT NULL DEFAULT '',
                DurationMs            INTEGER NOT NULL DEFAULT 0,
                ErrorMessage          TEXT    NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS ix_diaruns_session ON diarization_runs (SessionId, CreatedAt);",
            """
            CREATE TABLE IF NOT EXISTS speakers (
                SpeakerId           TEXT    NOT NULL PRIMARY KEY,
                SessionId           TEXT    NOT NULL,
                Label               TEXT    NOT NULL DEFAULT '',
                DisplayName         TEXT    NOT NULL DEFAULT '',
                ColorArgb           INTEGER NOT NULL DEFAULT 0,
                SortOrder           INTEGER NOT NULL DEFAULT 0,
                CreatedAt           TEXT    NOT NULL,
                IsMerged            INTEGER NOT NULL DEFAULT 0,
                MergedIntoSpeakerId TEXT    NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS ix_speakers_session ON speakers (SessionId, SortOrder);",
            """
            CREATE TABLE IF NOT EXISTS speaker_intervals (
                SpeakerSegmentId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                RunId            TEXT    NOT NULL,
                SessionId        TEXT    NOT NULL,
                StartMs          INTEGER NOT NULL,
                EndMs            INTEGER NOT NULL,
                RawSpeakerIndex  INTEGER NOT NULL,
                Confidence       REAL    NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS ix_speaker_intervals_run ON speaker_intervals (RunId, StartMs);",
            """
            CREATE TABLE IF NOT EXISTS speaker_assignments (
                AssignmentId      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                SessionId         TEXT    NOT NULL,
                SegmentId         INTEGER NOT NULL,
                RunId             TEXT    NULL,
                SpeakerId         TEXT    NULL,
                Source            INTEGER NOT NULL DEFAULT 0,
                Confidence        REAL    NULL,
                NeedsConfirmation INTEGER NOT NULL DEFAULT 0,
                UpdatedAt         TEXT    NOT NULL,
                UNIQUE (SegmentId)
            );
            """,
            "CREATE INDEX IF NOT EXISTS ix_speaker_assignments_session ON speaker_assignments (SessionId);",
            """
            CREATE TABLE IF NOT EXISTS audio_assets (
                AudioAssetId   TEXT    NOT NULL PRIMARY KEY,
                SessionId      TEXT    NULL,
                Path           TEXT    NOT NULL,
                Kind           INTEGER NOT NULL DEFAULT 0,
                SampleRate     INTEGER NOT NULL DEFAULT 0,
                Channels       INTEGER NOT NULL DEFAULT 0,
                DurationMs     INTEGER NOT NULL DEFAULT 0,
                SizeBytes      INTEGER NOT NULL DEFAULT 0,
                CreatedAt      TEXT    NOT NULL,
                DeleteAfterUtc TEXT    NULL,
                IsTemporary    INTEGER NOT NULL DEFAULT 0
            );
            """,
            "CREATE INDEX IF NOT EXISTS ix_audio_assets_expiry ON audio_assets (IsTemporary, DeleteAfterUtc);"
        })
    };

    public SqliteDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database file path is required.", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        DatabaseDirectory = Path.GetDirectoryName(DatabasePath) ?? ".";

        // Pooling is disabled so that closing the last connection releases the file handle,
        // which keeps the database (and its -wal/-shm siblings) deletable.
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public string DatabasePath { get; }

    public string DatabaseDirectory { get; }

    public string ConnectionString { get; }

    /// <summary>Ensures the directory exists and brings the schema up to the current version.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ApplyMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a new connection with the required PRAGMAs already applied.</summary>
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void EnsureDirectory()
    {
        if (!string.IsNullOrEmpty(DatabaseDirectory))
        {
            Directory.CreateDirectory(DatabaseDirectory);
        }
    }

    private static async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteScalarAsync(connection, JournalModeWal, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, ForeignKeysOn, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, SynchronousNormal, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            "CREATE TABLE IF NOT EXISTS schema_version (Version INTEGER NOT NULL PRIMARY KEY, AppliedAt TEXT NOT NULL);",
            cancellationToken).ConfigureAwait(false);

        var current = Convert.ToInt32(
            await ExecuteScalarAsync(connection, "SELECT COALESCE(MAX(Version), 0) FROM schema_version;", cancellationToken)
                .ConfigureAwait(false) ?? 0,
            CultureInfo.InvariantCulture);

        foreach (var migration in Migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var statement in migration.Statements)
                {
                    await ExecuteNonQueryAsync(connection, statement, cancellationToken, transaction).ConfigureAwait(false);
                }

                await ExecuteNonQueryAsync(
                    connection,
                    "INSERT INTO schema_version (Version, AppliedAt) VALUES ($version, $appliedAt);",
                    cancellationToken,
                    transaction,
                    ("$version", migration.Version),
                    ("$appliedAt", SqliteConvert.ToIso(DateTimeOffset.UtcNow))).ConfigureAwait(false);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    internal static async Task<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, sql, transaction, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, sql, transaction, parameters);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is DBNull ? null : result;
    }

    internal static async Task<long> LastInsertRowIdAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        var value = await ExecuteScalarAsync(connection, "SELECT last_insert_rowid();", cancellationToken, transaction)
            .ConfigureAwait(false);
        return Convert.ToInt64(value ?? 0L, CultureInfo.InvariantCulture);
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction,
        (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private sealed record Migration(int Version, string Description, IReadOnlyList<string> Statements);
}
