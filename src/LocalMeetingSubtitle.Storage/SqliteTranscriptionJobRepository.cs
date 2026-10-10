using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Storage;

/// <summary>SQLite-backed <see cref="ITranscriptionJobRepository"/>: the file-transcription queue.</summary>
public sealed class SqliteTranscriptionJobRepository : ITranscriptionJobRepository
{
    private const string Columns =
        "JobId, MediaFileId, SessionId, Title, Mode, ModelId, AudioStreamIndex, RunDiarization, "
        + "DiarizationCountMode, ManualSpeakerCount, ClusteringThreshold, Status, Phase, ProcessedMs, "
        + "TotalMs, SegmentsEmitted, QueuedAt, StartedAt, FinishedAt, Attempts, ResumeCount, Error, Warning";

    private readonly SqliteDatabase _database;

    public SqliteTranscriptionJobRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _database.InitializeAsync(cancellationToken);

    public async Task AddAsync(TranscriptionJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO transcription_jobs
                (JobId, MediaFileId, SessionId, Title, Mode, ModelId, AudioStreamIndex, RunDiarization,
                 DiarizationCountMode, ManualSpeakerCount, ClusteringThreshold, Status, Phase, ProcessedMs,
                 TotalMs, SegmentsEmitted, QueuedAt, StartedAt, FinishedAt, Attempts, ResumeCount, Error, Warning)
            VALUES
                ($jobId, $mediaFileId, $sessionId, $title, $mode, $modelId, $streamIndex, $runDiarization,
                 $countMode, $manualCount, $threshold, $status, $phase, $processedMs,
                 $totalMs, $segments, $queuedAt, $startedAt, $finishedAt, $attempts, $resumeCount, $error, $warning)
            ON CONFLICT(JobId) DO NOTHING;
            """,
            cancellationToken,
            null,
            ("$jobId", job.JobId),
            ("$mediaFileId", job.MediaFileId),
            ("$sessionId", (object?)job.SessionId ?? DBNull.Value),
            ("$title", job.Title),
            ("$mode", (int)job.Mode),
            ("$modelId", job.ModelId),
            ("$streamIndex", (object?)job.AudioStreamIndex ?? DBNull.Value),
            ("$runDiarization", job.RunDiarization ? 1 : 0),
            ("$countMode", (int)job.DiarizationCountMode),
            ("$manualCount", job.ManualSpeakerCount),
            ("$threshold", job.ClusteringThreshold),
            ("$status", (int)job.Status),
            ("$phase", (int)job.Phase),
            ("$processedMs", SqliteConvert.ToMs(job.Processed)),
            ("$totalMs", SqliteConvert.ToMs(job.Total)),
            ("$segments", job.SegmentsEmitted),
            ("$queuedAt", SqliteConvert.ToIso(job.QueuedAt)),
            ("$startedAt", SqliteConvert.ToIsoOrNull(job.StartedAt)),
            ("$finishedAt", SqliteConvert.ToIsoOrNull(job.FinishedAt)),
            ("$attempts", job.Attempts),
            ("$resumeCount", job.ResumeCount),
            ("$error", (object?)job.Error ?? DBNull.Value),
            ("$warning", (object?)job.Warning ?? DBNull.Value)).ConfigureAwait(false);
    }

    public async Task UpdateAsync(TranscriptionJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ExecuteNonQueryAsync(
            connection,
            """
            UPDATE transcription_jobs SET
                SessionId       = $sessionId,
                Status          = $status,
                Phase           = $phase,
                ProcessedMs     = $processedMs,
                TotalMs         = $totalMs,
                SegmentsEmitted = $segments,
                StartedAt       = $startedAt,
                FinishedAt      = $finishedAt,
                Attempts        = $attempts,
                ResumeCount     = $resumeCount,
                Error           = $error,
                Warning         = $warning
            WHERE JobId = $jobId;
            """,
            cancellationToken,
            null,
            ("$jobId", job.JobId),
            ("$sessionId", (object?)job.SessionId ?? DBNull.Value),
            ("$status", (int)job.Status),
            ("$phase", (int)job.Phase),
            ("$processedMs", SqliteConvert.ToMs(job.Processed)),
            ("$totalMs", SqliteConvert.ToMs(job.Total)),
            ("$segments", job.SegmentsEmitted),
            ("$startedAt", SqliteConvert.ToIsoOrNull(job.StartedAt)),
            ("$finishedAt", SqliteConvert.ToIsoOrNull(job.FinishedAt)),
            ("$attempts", job.Attempts),
            ("$resumeCount", job.ResumeCount),
            ("$error", (object?)job.Error ?? DBNull.Value),
            ("$warning", (object?)job.Warning ?? DBNull.Value)).ConfigureAwait(false);
    }

    public async Task<TranscriptionJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM transcription_jobs WHERE JobId = $id;";
        command.Parameters.AddWithValue("$id", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<TranscriptionJob>> GetJobsAsync(
        TranscriptionJobStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = status is null
            ? $"SELECT {Columns} FROM transcription_jobs ORDER BY QueuedAt, JobId;"
            : $"SELECT {Columns} FROM transcription_jobs WHERE Status = $status ORDER BY QueuedAt, JobId;";
        if (status is not null)
        {
            command.Parameters.AddWithValue("$status", (int)status.Value);
        }

        var results = new List<TranscriptionJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private static TranscriptionJob Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        JobId = reader.GetString(0),
        MediaFileId = reader.GetString(1),
        SessionId = reader.IsDBNull(2) ? null : reader.GetString(2),
        Title = reader.GetString(3),
        Mode = (TranscriptionMode)reader.GetInt32(4),
        ModelId = reader.GetString(5),
        AudioStreamIndex = reader.IsDBNull(6) ? null : reader.GetInt32(6),
        RunDiarization = reader.GetInt32(7) != 0,
        DiarizationCountMode = (SpeakerCountMode)reader.GetInt32(8),
        ManualSpeakerCount = reader.GetInt32(9),
        ClusteringThreshold = reader.GetDouble(10),
        Status = (TranscriptionJobStatus)reader.GetInt32(11),
        Phase = (FileTranscriptionPhase)reader.GetInt32(12),
        Processed = SqliteConvert.FromMs(reader.GetInt64(13)),
        Total = SqliteConvert.FromMs(reader.GetInt64(14)),
        SegmentsEmitted = reader.GetInt32(15),
        QueuedAt = SqliteConvert.ParseIso(reader.GetString(16)),
        StartedAt = reader.IsDBNull(17) ? null : SqliteConvert.ParseIso(reader.GetString(17)),
        FinishedAt = reader.IsDBNull(18) ? null : SqliteConvert.ParseIso(reader.GetString(18)),
        Attempts = reader.GetInt32(19),
        ResumeCount = reader.GetInt32(20),
        Error = reader.IsDBNull(21) ? null : reader.GetString(21),
        Warning = reader.IsDBNull(22) ? null : reader.GetString(22)
    };
}
