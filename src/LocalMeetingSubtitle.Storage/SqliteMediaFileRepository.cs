using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Storage;

/// <summary>SQLite-backed <see cref="IMediaFileRepository"/>: the imported-file registry.</summary>
public sealed class SqliteMediaFileRepository : IMediaFileRepository
{
    private const string Columns =
        "MediaFileId, Path, FileName, Kind, ContainerFormat, DurationMs, SizeBytes, AudioStreamIndex, CreatedAt";

    private readonly SqliteDatabase _database;

    public SqliteMediaFileRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _database.InitializeAsync(cancellationToken);

    public async Task AddAsync(MediaFileRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO media_files
                (MediaFileId, Path, FileName, Kind, ContainerFormat, DurationMs, SizeBytes, AudioStreamIndex, CreatedAt)
            VALUES
                ($id, $path, $fileName, $kind, $container, $durationMs, $size, $streamIndex, $createdAt)
            ON CONFLICT(MediaFileId) DO NOTHING;
            """,
            cancellationToken,
            null,
            ("$id", record.MediaFileId),
            ("$path", record.Path),
            ("$fileName", record.FileName),
            ("$kind", (int)record.Kind),
            ("$container", record.ContainerFormat),
            ("$durationMs", SqliteConvert.ToMs(record.Duration)),
            ("$size", record.SizeBytes),
            ("$streamIndex", (object?)record.AudioStreamIndex ?? DBNull.Value),
            ("$createdAt", SqliteConvert.ToIso(record.CreatedAt))).ConfigureAwait(false);
    }

    public async Task<MediaFileRecord?> GetAsync(string mediaFileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM media_files WHERE MediaFileId = $id;";
        command.Parameters.AddWithValue("$id", mediaFileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private static MediaFileRecord Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        MediaFileId = reader.GetString(0),
        Path = reader.GetString(1),
        FileName = reader.GetString(2),
        Kind = (MediaKind)reader.GetInt32(3),
        ContainerFormat = reader.GetString(4),
        Duration = SqliteConvert.FromMs(reader.GetInt64(5)),
        SizeBytes = reader.GetInt64(6),
        AudioStreamIndex = reader.IsDBNull(7) ? null : reader.GetInt32(7),
        CreatedAt = SqliteConvert.ParseIso(reader.GetString(8))
    };
}
