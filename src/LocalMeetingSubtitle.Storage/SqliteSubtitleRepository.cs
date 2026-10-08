using System.Globalization;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalMeetingSubtitle.Storage;

/// <summary>SQLite-backed <see cref="ISubtitleRepository"/>.</summary>
public sealed class SqliteSubtitleRepository : ISubtitleRepository
{
    private const string SegmentColumns =
        "SegmentId, SessionId, SequenceNumber, StartOffsetMs, EndOffsetMs, OriginalText, CorrectedText, IsEdited, CreatedAt";

    private readonly SqliteDatabase _database;

    public SqliteSubtitleRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _database.InitializeAsync(cancellationToken);

    public async Task<MeetingSession> CreateSessionAsync(MeetingSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO sessions (SessionId, Title, StartTime, EndTime, Status, ModelId, AudioDeviceId, AudioDeviceName)
            VALUES ($id, $title, $start, $end, $status, $model, $deviceId, $deviceName)
            ON CONFLICT(SessionId) DO UPDATE SET
                Title           = excluded.Title,
                StartTime       = excluded.StartTime,
                EndTime         = excluded.EndTime,
                Status          = excluded.Status,
                ModelId         = excluded.ModelId,
                AudioDeviceId   = excluded.AudioDeviceId,
                AudioDeviceName = excluded.AudioDeviceName;
            """,
            cancellationToken,
            null,
            ("$id", session.SessionId),
            ("$title", session.Title),
            ("$start", SqliteConvert.ToIso(session.StartTime)),
            ("$end", SqliteConvert.ToIsoOrNull(session.EndTime)),
            ("$status", (int)session.Status),
            ("$model", session.ModelId),
            ("$deviceId", session.AudioDeviceId),
            ("$deviceName", session.AudioDeviceName)).ConfigureAwait(false);

        return session;
    }

    public async Task AppendSegmentAsync(SubtitleSegment segment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var affected = await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT OR IGNORE INTO segments
                (SessionId, SequenceNumber, StartOffsetMs, EndOffsetMs, OriginalText, CorrectedText, IsEdited, CreatedAt)
            VALUES ($sessionId, $sequence, $start, $end, $original, $corrected, $edited, $createdAt);
            """,
            cancellationToken,
            null,
            ("$sessionId", segment.SessionId),
            ("$sequence", segment.SequenceNumber),
            ("$start", SqliteConvert.ToMs(segment.StartOffset)),
            ("$end", SqliteConvert.ToMs(segment.EndOffset)),
            ("$original", segment.OriginalText),
            ("$corrected", segment.CorrectedText),
            ("$edited", segment.IsEdited ? 1 : 0),
            ("$createdAt", SqliteConvert.ToIso(segment.CreatedAt))).ConfigureAwait(false);

        // A duplicate (SessionId, SequenceNumber) is silently ignored: never throw, never overwrite.
        if (affected > 0)
        {
            segment.SegmentId = await SqliteDatabase.LastInsertRowIdAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task UpdateSegmentTextAsync(long segmentId, string correctedText, bool isEdited, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "UPDATE segments SET CorrectedText = $corrected, IsEdited = $edited WHERE SegmentId = $id;",
            cancellationToken,
            null,
            ("$corrected", correctedText),
            ("$edited", isEdited ? 1 : 0),
            ("$id", segmentId)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubtitleSegment>> GetSegmentsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SegmentColumns} FROM segments WHERE SessionId = $sessionId ORDER BY SequenceNumber;";
        command.Parameters.AddWithValue("$sessionId", sessionId);

        var results = new List<SubtitleSegment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadSegment(reader));
        }

        return results;
    }

    public async Task<MeetingSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT SessionId, Title, StartTime, EndTime, Status, ModelId, AudioDeviceId, AudioDeviceName " +
            "FROM sessions WHERE SessionId = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadSession(reader) : null;
    }

    public async Task<IReadOnlyList<MeetingSession>> GetRecentSessionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT SessionId, Title, StartTime, EndTime, Status, ModelId, AudioDeviceId, AudioDeviceName " +
            "FROM sessions ORDER BY StartTime DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<MeetingSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadSession(reader));
        }

        return results;
    }

    public async Task<int> GetNextSequenceAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var value = await SqliteDatabase.ExecuteScalarAsync(
            connection,
            "SELECT COALESCE(MAX(SequenceNumber), -1) + 1 FROM segments WHERE SessionId = $sessionId;",
            cancellationToken,
            null,
            ("$sessionId", sessionId)).ConfigureAwait(false);

        return Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture);
    }

    public async Task UpdateSessionStatusAsync(string sessionId, SessionStatus status, DateTimeOffset? endTime, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "UPDATE sessions SET Status = $status, EndTime = $end WHERE SessionId = $id;",
            cancellationToken,
            null,
            ("$status", (int)status),
            ("$end", SqliteConvert.ToIsoOrNull(endTime)),
            ("$id", sessionId)).ConfigureAwait(false);
    }

    public async Task<int> RecoverAbortedSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            "UPDATE sessions SET Status = $aborted, EndTime = $now WHERE Status IN ($recording, $paused);",
            cancellationToken,
            null,
            ("$aborted", (int)SessionStatus.Aborted),
            ("$now", SqliteConvert.ToIso(DateTimeOffset.UtcNow)),
            ("$recording", (int)SessionStatus.Recording),
            ("$paused", (int)SessionStatus.Paused)).ConfigureAwait(false);
    }

    public async Task AppendMetricAsync(PerformanceMetric metric, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metric);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var affected = await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO metrics
                (SessionId, Timestamp, CpuPercent, WorkingSetMb, Rtf, QueueLength, MaxQueueLength, DroppedAudioSeconds, EndToEndLatencyMs)
            VALUES
                ($sessionId, $timestamp, $cpu, $workingSet, $rtf, $queue, $maxQueue, $dropped, $latency);
            """,
            cancellationToken,
            null,
            ("$sessionId", metric.SessionId),
            ("$timestamp", SqliteConvert.ToIso(metric.Timestamp)),
            ("$cpu", metric.CpuPercent),
            ("$workingSet", metric.WorkingSetMb),
            ("$rtf", metric.Rtf),
            ("$queue", metric.QueueLength),
            ("$maxQueue", metric.MaxQueueLength),
            ("$dropped", metric.DroppedAudioSeconds),
            ("$latency", metric.EndToEndLatencyMs)).ConfigureAwait(false);

        if (affected > 0)
        {
            metric.Id = await SqliteDatabase.LastInsertRowIdAsync(connection, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<PerformanceMetric>> GetMetricsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, SessionId, Timestamp, CpuPercent, WorkingSetMb, Rtf, QueueLength, MaxQueueLength, " +
            "DroppedAudioSeconds, EndToEndLatencyMs FROM metrics WHERE SessionId = $sessionId ORDER BY Timestamp;";
        command.Parameters.AddWithValue("$sessionId", sessionId);

        var results = new List<PerformanceMetric>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new PerformanceMetric
            {
                Id = reader.GetInt64(0),
                SessionId = reader.GetString(1),
                Timestamp = SqliteConvert.ParseIso(reader.GetString(2)),
                CpuPercent = reader.GetDouble(3),
                WorkingSetMb = reader.GetDouble(4),
                Rtf = reader.GetDouble(5),
                QueueLength = reader.GetInt32(6),
                MaxQueueLength = reader.GetInt32(7),
                DroppedAudioSeconds = reader.GetDouble(8),
                EndToEndLatencyMs = reader.GetDouble(9)
            });
        }

        return results;
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private static SubtitleSegment ReadSegment(SqliteDataReader reader) => new()
    {
        SegmentId = reader.GetInt64(0),
        SessionId = reader.GetString(1),
        SequenceNumber = reader.GetInt32(2),
        StartOffset = SqliteConvert.FromMs(reader.GetInt64(3)),
        EndOffset = SqliteConvert.FromMs(reader.GetInt64(4)),
        OriginalText = reader.GetString(5),
        CorrectedText = reader.GetString(6),
        IsEdited = reader.GetInt64(7) != 0,
        CreatedAt = SqliteConvert.ParseIso(reader.GetString(8))
    };

    private static MeetingSession ReadSession(SqliteDataReader reader) => new()
    {
        SessionId = reader.GetString(0),
        Title = reader.GetString(1),
        StartTime = SqliteConvert.ParseIso(reader.GetString(2)),
        EndTime = SqliteConvert.ParseIsoOrNull(reader.IsDBNull(3) ? null : reader.GetString(3)),
        Status = (SessionStatus)reader.GetInt32(4),
        ModelId = reader.GetString(5),
        AudioDeviceId = reader.GetString(6),
        AudioDeviceName = reader.GetString(7)
    };
}
