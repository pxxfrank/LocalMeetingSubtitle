using System.Globalization;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalMeetingSubtitle.Storage;

/// <summary>SQLite-backed <see cref="ISpeakerRepository"/>: diarization runs, speakers, intervals and assignments.</summary>
public sealed class SqliteSpeakerRepository : ISpeakerRepository
{
    private const string RunColumns =
        "RunId, SessionId, AudioAssetId, CreatedAt, Status, RequestedSpeakerCount, ResolvedSpeakerCount, " +
        "ClusteringThreshold, MinDurationOn, MinDurationOff, SegmentationModelId, EmbeddingModelId, DurationMs, ErrorMessage";

    private const string SpeakerColumns =
        "SpeakerId, SessionId, Label, DisplayName, ColorArgb, SortOrder, CreatedAt, IsMerged, MergedIntoSpeakerId";

    private const string IntervalColumns =
        "SpeakerSegmentId, RunId, SessionId, StartMs, EndMs, RawSpeakerIndex, Confidence";

    private const string AssignmentColumns =
        "AssignmentId, SessionId, SegmentId, RunId, SpeakerId, Source, Confidence, NeedsConfirmation, UpdatedAt";

    private readonly SqliteDatabase _database;

    public SqliteSpeakerRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _database.InitializeAsync(cancellationToken);

    public async Task SaveRunAsync(DiarizationRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO diarization_runs
                (RunId, SessionId, AudioAssetId, CreatedAt, Status, RequestedSpeakerCount, ResolvedSpeakerCount,
                 ClusteringThreshold, MinDurationOn, MinDurationOff, SegmentationModelId, EmbeddingModelId, DurationMs, ErrorMessage)
            VALUES
                ($runId, $sessionId, $audioAssetId, $createdAt, $status, $requested, $resolved,
                 $threshold, $minOn, $minOff, $segModel, $embModel, $durationMs, $error)
            ON CONFLICT(RunId) DO UPDATE SET
                Status               = excluded.Status,
                ResolvedSpeakerCount = excluded.ResolvedSpeakerCount,
                DurationMs           = excluded.DurationMs,
                ErrorMessage         = excluded.ErrorMessage;
            """,
            cancellationToken,
            null,
            ("$runId", run.RunId),
            ("$sessionId", run.SessionId),
            ("$audioAssetId", (object?)run.AudioAssetId ?? DBNull.Value),
            ("$createdAt", SqliteConvert.ToIso(run.CreatedAt)),
            ("$status", (int)run.Status),
            ("$requested", run.RequestedSpeakerCount),
            ("$resolved", run.ResolvedSpeakerCount),
            ("$threshold", run.ClusteringThreshold),
            ("$minOn", run.MinDurationOn),
            ("$minOff", run.MinDurationOff),
            ("$segModel", run.SegmentationModelId),
            ("$embModel", run.EmbeddingModelId),
            ("$durationMs", run.DurationMs),
            ("$error", (object?)run.ErrorMessage ?? DBNull.Value)).ConfigureAwait(false);
    }

    public async Task<DiarizationRun?> GetLatestRunAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {RunColumns} FROM diarization_runs WHERE SessionId = $sessionId ORDER BY CreatedAt DESC LIMIT 1;";
        command.Parameters.AddWithValue("$sessionId", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRun(reader) : null;
    }

    public async Task ReplaceIntervalsAsync(string runId, IReadOnlyList<SpeakerInterval> intervals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            await SqliteDatabase.ExecuteNonQueryAsync(
                connection,
                "DELETE FROM speaker_intervals WHERE RunId = $runId;",
                cancellationToken,
                transaction,
                ("$runId", runId)).ConfigureAwait(false);

            foreach (var interval in intervals)
            {
                await SqliteDatabase.ExecuteNonQueryAsync(
                    connection,
                    """
                    INSERT INTO speaker_intervals (RunId, SessionId, StartMs, EndMs, RawSpeakerIndex, Confidence)
                    VALUES ($runId, $sessionId, $start, $end, $raw, $confidence);
                    """,
                    cancellationToken,
                    transaction,
                    ("$runId", runId),
                    ("$sessionId", interval.SessionId),
                    ("$start", SqliteConvert.ToMs(interval.Start)),
                    ("$end", SqliteConvert.ToMs(interval.End)),
                    ("$raw", interval.RawSpeakerIndex),
                    ("$confidence", (object?)interval.Confidence ?? DBNull.Value)).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<SpeakerInterval>> GetIntervalsAsync(string runId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {IntervalColumns} FROM speaker_intervals WHERE RunId = $runId ORDER BY StartMs;";
        command.Parameters.AddWithValue("$runId", runId);

        var results = new List<SpeakerInterval>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new SpeakerInterval
            {
                SpeakerSegmentId = reader.GetInt64(0),
                RunId = reader.GetString(1),
                SessionId = reader.GetString(2),
                Start = SqliteConvert.FromMs(reader.GetInt64(3)),
                End = SqliteConvert.FromMs(reader.GetInt64(4)),
                RawSpeakerIndex = reader.GetInt32(5),
                Confidence = reader.IsDBNull(6) ? null : reader.GetFloat(6)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<Speaker>> GetSpeakersAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SpeakerColumns} FROM speakers WHERE SessionId = $sessionId ORDER BY SortOrder, CreatedAt;";
        command.Parameters.AddWithValue("$sessionId", sessionId);

        var results = new List<Speaker>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadSpeaker(reader));
        }

        return results;
    }

    public async Task UpsertSpeakerAsync(Speaker speaker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(speaker);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO speakers
                (SpeakerId, SessionId, Label, DisplayName, ColorArgb, SortOrder, CreatedAt, IsMerged, MergedIntoSpeakerId)
            VALUES
                ($id, $sessionId, $label, $name, $color, $sort, $createdAt, $merged, $mergedInto)
            ON CONFLICT(SpeakerId) DO UPDATE SET
                Label               = excluded.Label,
                DisplayName         = excluded.DisplayName,
                ColorArgb           = excluded.ColorArgb,
                SortOrder           = excluded.SortOrder,
                IsMerged            = excluded.IsMerged,
                MergedIntoSpeakerId = excluded.MergedIntoSpeakerId;
            """,
            cancellationToken,
            null,
            ("$id", speaker.SpeakerId),
            ("$sessionId", speaker.SessionId),
            ("$label", speaker.Label),
            ("$name", speaker.DisplayName),
            ("$color", speaker.ColorArgb),
            ("$sort", speaker.SortOrder),
            ("$createdAt", SqliteConvert.ToIso(speaker.CreatedAt)),
            ("$merged", speaker.IsMerged ? 1 : 0),
            ("$mergedInto", (object?)speaker.MergedIntoSpeakerId ?? DBNull.Value)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<long>> MergeSpeakersAsync(string sessionId, string fromSpeakerId, string intoSpeakerId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            var affected = new List<long>();
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText =
                    "SELECT SegmentId FROM speaker_assignments WHERE SessionId = $sessionId AND SpeakerId = $from;";
                select.Parameters.AddWithValue("$sessionId", sessionId);
                select.Parameters.AddWithValue("$from", fromSpeakerId);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    affected.Add(reader.GetInt64(0));
                }
            }

            await SqliteDatabase.ExecuteNonQueryAsync(
                connection,
                """
                UPDATE speaker_assignments
                SET SpeakerId = $into, Source = $manual, UpdatedAt = $now
                WHERE SessionId = $sessionId AND SpeakerId = $from;
                """,
                cancellationToken,
                transaction,
                ("$into", intoSpeakerId),
                ("$manual", (int)SpeakerAssignmentSource.Manual),
                ("$now", SqliteConvert.ToIso(DateTimeOffset.Now)),
                ("$sessionId", sessionId),
                ("$from", fromSpeakerId)).ConfigureAwait(false);

            await SqliteDatabase.ExecuteNonQueryAsync(
                connection,
                "UPDATE speakers SET IsMerged = 1, MergedIntoSpeakerId = $into WHERE SpeakerId = $from;",
                cancellationToken,
                transaction,
                ("$into", intoSpeakerId),
                ("$from", fromSpeakerId)).ConfigureAwait(false);

            transaction.Commit();
            return affected;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<SpeakerAssignment>> GetAssignmentsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {AssignmentColumns} FROM speaker_assignments WHERE SessionId = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId);

        var results = new List<SpeakerAssignment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadAssignment(reader));
        }

        return results;
    }

    public async Task UpsertAssignmentsAsync(IReadOnlyList<SpeakerAssignment> assignments, bool overwriteManual, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var guard = overwriteManual ? string.Empty : " WHERE speaker_assignments.Source = 0";
        var sql =
            $"""
            INSERT INTO speaker_assignments
                (SessionId, SegmentId, RunId, SpeakerId, Source, Confidence, NeedsConfirmation, UpdatedAt)
            VALUES
                ($sessionId, $segmentId, $runId, $speakerId, $source, $confidence, $needs, $updatedAt)
            ON CONFLICT(SegmentId) DO UPDATE SET
                RunId             = excluded.RunId,
                SpeakerId         = excluded.SpeakerId,
                Source            = excluded.Source,
                Confidence        = excluded.Confidence,
                NeedsConfirmation = excluded.NeedsConfirmation,
                UpdatedAt         = excluded.UpdatedAt{guard};
            """;

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var assignment in assignments)
            {
                await SqliteDatabase.ExecuteNonQueryAsync(
                    connection,
                    sql,
                    cancellationToken,
                    transaction,
                    ("$sessionId", assignment.SessionId),
                    ("$segmentId", assignment.SegmentId),
                    ("$runId", (object?)assignment.RunId ?? DBNull.Value),
                    ("$speakerId", (object?)assignment.SpeakerId ?? DBNull.Value),
                    ("$source", (int)assignment.Source),
                    ("$confidence", (object?)assignment.Confidence ?? DBNull.Value),
                    ("$needs", assignment.NeedsConfirmation ? 1 : 0),
                    ("$updatedAt", SqliteConvert.ToIso(assignment.UpdatedAt))).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task SetAssignmentSpeakerAsync(string sessionId, long segmentId, string? speakerId, SpeakerAssignmentSource source, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO speaker_assignments
                (SessionId, SegmentId, RunId, SpeakerId, Source, Confidence, NeedsConfirmation, UpdatedAt)
            VALUES
                ($sessionId, $segmentId, NULL, $speakerId, $source, NULL, 0, $updatedAt)
            ON CONFLICT(SegmentId) DO UPDATE SET
                SpeakerId         = excluded.SpeakerId,
                Source            = excluded.Source,
                NeedsConfirmation = 0,
                UpdatedAt         = excluded.UpdatedAt;
            """,
            cancellationToken,
            null,
            ("$sessionId", sessionId),
            ("$segmentId", segmentId),
            ("$speakerId", (object?)speakerId ?? DBNull.Value),
            ("$source", (int)source),
            ("$updatedAt", SqliteConvert.ToIso(DateTimeOffset.Now))).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private static DiarizationRun ReadRun(SqliteDataReader reader) => new()
    {
        RunId = reader.GetString(0),
        SessionId = reader.GetString(1),
        AudioAssetId = reader.IsDBNull(2) ? null : reader.GetString(2),
        CreatedAt = SqliteConvert.ParseIso(reader.GetString(3)),
        Status = (DiarizationRunStatus)reader.GetInt32(4),
        RequestedSpeakerCount = reader.GetInt32(5),
        ResolvedSpeakerCount = reader.GetInt32(6),
        ClusteringThreshold = reader.GetDouble(7),
        MinDurationOn = reader.GetDouble(8),
        MinDurationOff = reader.GetDouble(9),
        SegmentationModelId = reader.GetString(10),
        EmbeddingModelId = reader.GetString(11),
        DurationMs = reader.GetInt64(12),
        ErrorMessage = reader.IsDBNull(13) ? null : reader.GetString(13)
    };

    private static Speaker ReadSpeaker(SqliteDataReader reader) => new()
    {
        SpeakerId = reader.GetString(0),
        SessionId = reader.GetString(1),
        Label = reader.GetString(2),
        DisplayName = reader.GetString(3),
        ColorArgb = reader.GetInt32(4),
        SortOrder = reader.GetInt32(5),
        CreatedAt = SqliteConvert.ParseIso(reader.GetString(6)),
        IsMerged = reader.GetInt64(7) != 0,
        MergedIntoSpeakerId = reader.IsDBNull(8) ? null : reader.GetString(8)
    };

    private static SpeakerAssignment ReadAssignment(SqliteDataReader reader) => new()
    {
        AssignmentId = reader.GetInt64(0),
        SessionId = reader.GetString(1),
        SegmentId = reader.GetInt64(2),
        RunId = reader.IsDBNull(3) ? null : reader.GetString(3),
        SpeakerId = reader.IsDBNull(4) ? null : reader.GetString(4),
        Source = (SpeakerAssignmentSource)reader.GetInt32(5),
        Confidence = reader.IsDBNull(6) ? null : reader.GetFloat(6),
        NeedsConfirmation = reader.GetInt64(7) != 0,
        UpdatedAt = SqliteConvert.ParseIso(reader.GetString(8))
    };
}
