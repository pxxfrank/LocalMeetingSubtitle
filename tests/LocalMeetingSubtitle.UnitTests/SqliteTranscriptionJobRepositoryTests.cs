using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>V0.5 Phase 4: the migration-5 job/media repositories round-trip every column.</summary>
public sealed class SqliteTranscriptionJobRepositoryTests
{
    [Fact]
    public async Task MediaFile_RoundTrips()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateMediaFileRepository();

        var record = new MediaFileRecord
        {
            Path = @"C:\clips\standup.mp4",
            FileName = "standup.mp4",
            Kind = MediaKind.Video,
            ContainerFormat = "mov,mp4,m4a",
            Duration = TimeSpan.FromSeconds(123.456),
            SizeBytes = 987654,
            AudioStreamIndex = 1
        };

        await repository.AddAsync(record);
        var loaded = await repository.GetAsync(record.MediaFileId);

        Assert.NotNull(loaded);
        Assert.Equal(record.MediaFileId, loaded!.MediaFileId);
        Assert.Equal(record.Path, loaded.Path);
        Assert.Equal(record.FileName, loaded.FileName);
        Assert.Equal(MediaKind.Video, loaded.Kind);
        Assert.Equal(record.ContainerFormat, loaded.ContainerFormat);
        Assert.Equal(123_456, (long)loaded.Duration.TotalMilliseconds);
        Assert.Equal(record.SizeBytes, loaded.SizeBytes);
        Assert.Equal(1, loaded.AudioStreamIndex);
    }

    [Fact]
    public async Task MediaFile_NullStreamIndex_RoundTrips()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateMediaFileRepository();

        var record = new MediaFileRecord { Path = "a.wav", FileName = "a.wav", Kind = MediaKind.Audio };
        await repository.AddAsync(record);

        var loaded = await repository.GetAsync(record.MediaFileId);
        Assert.Null(loaded!.AudioStreamIndex);
    }

    [Fact]
    public async Task Job_RoundTripsEveryField()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateTranscriptionJobRepository();

        var job = new TranscriptionJob
        {
            MediaFileId = "media-1",
            SessionId = "session-1",
            Title = "standup",
            Mode = TranscriptionMode.HighAccuracy,
            ModelId = "sense-voice-small-int8",
            AudioStreamIndex = 2,
            RunDiarization = false,
            DiarizationCountMode = SpeakerCountMode.Manual,
            ManualSpeakerCount = 3,
            ClusteringThreshold = 0.62,
            Status = TranscriptionJobStatus.Running,
            Phase = FileTranscriptionPhase.Diarize,
            Processed = TimeSpan.FromSeconds(12.5),
            Total = TimeSpan.FromSeconds(40),
            SegmentsEmitted = 7,
            StartedAt = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.FromHours(8)),
            Attempts = 2,
            ResumeCount = 1,
            Error = "boom",
            Warning = "careful"
        };

        await repository.AddAsync(job);
        var loaded = await repository.GetAsync(job.JobId);

        Assert.NotNull(loaded);
        Assert.Equal(job.JobId, loaded!.JobId);
        Assert.Equal("media-1", loaded.MediaFileId);
        Assert.Equal("session-1", loaded.SessionId);
        Assert.Equal("standup", loaded.Title);
        Assert.Equal(TranscriptionMode.HighAccuracy, loaded.Mode);
        Assert.Equal("sense-voice-small-int8", loaded.ModelId);
        Assert.Equal(2, loaded.AudioStreamIndex);
        Assert.False(loaded.RunDiarization);
        Assert.Equal(SpeakerCountMode.Manual, loaded.DiarizationCountMode);
        Assert.Equal(3, loaded.ManualSpeakerCount);
        Assert.Equal(0.62, loaded.ClusteringThreshold, 5);
        Assert.Equal(TranscriptionJobStatus.Running, loaded.Status);
        Assert.Equal(FileTranscriptionPhase.Diarize, loaded.Phase);
        Assert.Equal(12_500, (long)loaded.Processed.TotalMilliseconds);
        Assert.Equal(40_000, (long)loaded.Total.TotalMilliseconds);
        Assert.Equal(7, loaded.SegmentsEmitted);
        Assert.Equal(job.StartedAt, loaded.StartedAt);
        Assert.Null(loaded.FinishedAt);
        Assert.Equal(2, loaded.Attempts);
        Assert.Equal(1, loaded.ResumeCount);
        Assert.Equal("boom", loaded.Error);
        Assert.Equal("careful", loaded.Warning);
    }

    [Fact]
    public async Task Update_WritesTheMutableColumns()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateTranscriptionJobRepository();

        var job = new TranscriptionJob { MediaFileId = "m", Title = "t" };
        await repository.AddAsync(job);

        job.Status = TranscriptionJobStatus.Succeeded;
        job.SessionId = "s";
        job.Phase = FileTranscriptionPhase.Assemble;
        job.Processed = TimeSpan.FromSeconds(30);
        job.SegmentsEmitted = 9;
        job.FinishedAt = DateTimeOffset.Now;
        job.ResumeCount = 2;
        job.Error = null;
        job.Warning = "w";
        await repository.UpdateAsync(job);

        var loaded = await repository.GetAsync(job.JobId);
        Assert.Equal(TranscriptionJobStatus.Succeeded, loaded!.Status);
        Assert.Equal("s", loaded.SessionId);
        Assert.Equal(FileTranscriptionPhase.Assemble, loaded.Phase);
        Assert.Equal(30_000, (long)loaded.Processed.TotalMilliseconds);
        Assert.Equal(9, loaded.SegmentsEmitted);
        Assert.NotNull(loaded.FinishedAt);
        Assert.Equal(2, loaded.ResumeCount);
        Assert.Null(loaded.Error);
        Assert.Equal("w", loaded.Warning);
    }

    [Fact]
    public async Task GetJobs_FiltersByStatusAndOrdersByQueueTime()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateTranscriptionJobRepository();

        var first = new TranscriptionJob { MediaFileId = "m", Status = TranscriptionJobStatus.Queued, QueuedAt = DateTimeOffset.Now };
        var second = new TranscriptionJob { MediaFileId = "m", Status = TranscriptionJobStatus.Succeeded, QueuedAt = DateTimeOffset.Now.AddSeconds(1) };
        var third = new TranscriptionJob { MediaFileId = "m", Status = TranscriptionJobStatus.Queued, QueuedAt = DateTimeOffset.Now.AddSeconds(2) };
        await repository.AddAsync(first);
        await repository.AddAsync(second);
        await repository.AddAsync(third);

        var all = await repository.GetJobsAsync();
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { first.JobId, second.JobId, third.JobId }, all.Select(j => j.JobId).ToArray());

        var queued = await repository.GetJobsAsync(TranscriptionJobStatus.Queued);
        Assert.Equal(new[] { first.JobId, third.JobId }, queued.Select(j => j.JobId).ToArray());
    }
}
