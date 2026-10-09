using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Storage;

/// <summary>SQLite-backed <see cref="IAudioAssetRepository"/>: the local audio-asset registry.</summary>
public sealed class SqliteAudioAssetRepository : IAudioAssetRepository
{
    private const string Columns =
        "AudioAssetId, SessionId, Path, Kind, SampleRate, Channels, DurationMs, SizeBytes, CreatedAt, DeleteAfterUtc, IsTemporary";

    private readonly SqliteDatabase _database;

    public SqliteAudioAssetRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _database.InitializeAsync(cancellationToken);

    public async Task AddAsync(AudioAsset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO audio_assets
                (AudioAssetId, SessionId, Path, Kind, SampleRate, Channels, DurationMs, SizeBytes, CreatedAt, DeleteAfterUtc, IsTemporary)
            VALUES
                ($id, $sessionId, $path, $kind, $sampleRate, $channels, $durationMs, $size, $createdAt, $deleteAfter, $temporary)
            ON CONFLICT(AudioAssetId) DO UPDATE SET
                Path           = excluded.Path,
                Kind           = excluded.Kind,
                SampleRate     = excluded.SampleRate,
                Channels       = excluded.Channels,
                DurationMs     = excluded.DurationMs,
                SizeBytes      = excluded.SizeBytes,
                DeleteAfterUtc = excluded.DeleteAfterUtc,
                IsTemporary    = excluded.IsTemporary;
            """,
            cancellationToken,
            null,
            ("$id", asset.AudioAssetId),
            ("$sessionId", (object?)asset.SessionId ?? DBNull.Value),
            ("$path", asset.Path),
            ("$kind", (int)asset.Kind),
            ("$sampleRate", asset.SampleRate),
            ("$channels", asset.Channels),
            ("$durationMs", asset.DurationMs),
            ("$size", asset.SizeBytes),
            ("$createdAt", SqliteConvert.ToIso(asset.CreatedAt)),
            ("$deleteAfter", SqliteConvert.ToIsoOrNull(asset.DeleteAfterUtc)),
            ("$temporary", asset.IsTemporary ? 1 : 0)).ConfigureAwait(false);
    }

    public async Task<AudioAsset?> GetAsync(string audioAssetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM audio_assets WHERE AudioAssetId = $id;";
        command.Parameters.AddWithValue("$id", audioAssetId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadAsset(reader) : null;
    }

    public async Task<IReadOnlyList<AudioAsset>> GetExpiredTemporaryAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {Columns} FROM audio_assets WHERE IsTemporary = 1 AND DeleteAfterUtc IS NOT NULL AND DeleteAfterUtc <= $now;";
        command.Parameters.AddWithValue("$now", SqliteConvert.ToIso(now));

        var results = new List<AudioAsset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadAsset(reader));
        }

        return results;
    }

    public async Task DeleteAsync(string audioAssetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "DELETE FROM audio_assets WHERE AudioAssetId = $id;",
            cancellationToken,
            null,
            ("$id", audioAssetId)).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private static AudioAsset ReadAsset(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        AudioAssetId = reader.GetString(0),
        SessionId = reader.IsDBNull(1) ? null : reader.GetString(1),
        Path = reader.GetString(2),
        Kind = (AudioAssetKind)reader.GetInt32(3),
        SampleRate = reader.GetInt32(4),
        Channels = reader.GetInt32(5),
        DurationMs = reader.GetInt64(6),
        SizeBytes = reader.GetInt64(7),
        CreatedAt = SqliteConvert.ParseIso(reader.GetString(8)),
        DeleteAfterUtc = reader.IsDBNull(9) ? null : SqliteConvert.ParseIso(reader.GetString(9)),
        IsTemporary = reader.GetInt64(10) != 0
    };
}
