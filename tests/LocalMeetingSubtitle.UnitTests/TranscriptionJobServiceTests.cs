using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>V0.5 Phase 4: the queue serializes jobs, cancels cooperatively and resumes from the session.</summary>
public sealed class TranscriptionJobServiceTests
{
    [Fact]
    public async Task EnqueueThenDrain_RunsTheJobAndSucceeds()
    {
        await using var harness = await Harness.CreateAsync();

        var job = await harness.Service.EnqueueAsync(harness.Request(), Harness.Media());
        Assert.Equal(TranscriptionJobStatus.Queued, job.Status);

        var processed = await harness.Service.DrainAsync();

        Assert.Equal(1, processed);
        var stored = await harness.Service.GetJobAsync(job.JobId);
        Assert.Equal(TranscriptionJobStatus.Succeeded, stored!.Status);
        Assert.Equal("session-1", stored.SessionId);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(0, stored.ResumeCount);
        Assert.NotNull(stored.FinishedAt);
        Assert.Equal(10_000, (long)stored.Processed.TotalMilliseconds);
    }

    [Fact]
    public async Task Queue_RunsInEnqueueOrder()
    {
        await using var harness = await Harness.CreateAsync();

        var a = await harness.Service.EnqueueAsync(harness.Request("a"), Harness.Media());
        var b = await harness.Service.EnqueueAsync(harness.Request("b"), Harness.Media());
        var c = await harness.Service.EnqueueAsync(harness.Request("c"), Harness.Media());

        var processed = await harness.Service.DrainAsync();

        Assert.Equal(3, processed);
        Assert.Equal(new[] { "a", "b", "c" }, harness.Files.Requests.Select(r => r.Title).ToArray());
        foreach (var id in new[] { a.JobId, b.JobId, c.JobId })
        {
            Assert.Equal(TranscriptionJobStatus.Succeeded, (await harness.Service.GetJobAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task CancelWhileRunning_MarksTheJobCancelledAndKeepsTheSession()
    {
        await using var harness = await Harness.CreateAsync();
        var entered = new TaskCompletionSource();
        harness.Files.OnRun = async (request, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);   // runs until cancelled
            return new FileTranscriptionResult();
        };

        var job = await harness.Service.EnqueueAsync(harness.Request(), Harness.Media());
        var drain = harness.Service.DrainAsync();
        await entered.Task;

        Assert.True(await harness.Service.CancelAsync(job.JobId));
        await drain;

        var stored = await harness.Service.GetJobAsync(job.JobId);
        Assert.Equal(TranscriptionJobStatus.Cancelled, stored!.Status);
    }

    [Fact]
    public async Task Resume_RerunsAgainstTheStoredSession()
    {
        await using var harness = await Harness.CreateAsync();
        int attempt = 0;
        harness.Files.OnRun = (request, _) =>
        {
            attempt++;
            return Task.FromResult(attempt == 1
                ? new FileTranscriptionResult { SessionId = "sess-1", Cancelled = true, AudioDuration = TimeSpan.FromSeconds(3) }
                : new FileTranscriptionResult { SessionId = request.SessionId ?? "sess-1", Completed = true, AudioDuration = TimeSpan.FromSeconds(10) });
        };

        var job = await harness.Service.EnqueueAsync(harness.Request(), Harness.Media());
        await harness.Service.DrainAsync();

        var afterFirst = await harness.Service.GetJobAsync(job.JobId);
        Assert.Equal(TranscriptionJobStatus.Cancelled, afterFirst!.Status);
        Assert.Equal("sess-1", afterFirst.SessionId);

        Assert.True(await harness.Service.ResumeAsync(job.JobId));
        await harness.Service.DrainAsync();

        var afterResume = await harness.Service.GetJobAsync(job.JobId);
        Assert.Equal(TranscriptionJobStatus.Succeeded, afterResume!.Status);
        Assert.Equal("sess-1", afterResume.SessionId);
        Assert.Equal(2, afterResume.Attempts);
        Assert.Equal(1, afterResume.ResumeCount);

        // The second attempt was told to resume the same session.
        Assert.Null(harness.Files.Requests[0].SessionId);
        Assert.Equal("sess-1", harness.Files.Requests[1].SessionId);
    }

    [Fact]
    public async Task CancelWhileQueued_IsNotRun()
    {
        await using var harness = await Harness.CreateAsync();
        var job = await harness.Service.EnqueueAsync(harness.Request(), Harness.Media());

        Assert.True(await harness.Service.CancelAsync(job.JobId));
        var processed = await harness.Service.DrainAsync();

        Assert.Equal(0, processed);
        Assert.Empty(harness.Files.Requests);
        Assert.Equal(TranscriptionJobStatus.Cancelled, (await harness.Service.GetJobAsync(job.JobId))!.Status);
    }

    [Fact]
    public async Task RecoverUnfinished_MarksRunningJobsInterrupted()
    {
        await using var harness = await Harness.CreateAsync();
        var job = await harness.Service.EnqueueAsync(harness.Request(), Harness.Media());

        // Simulate the process dying mid-run: the row is left Running.
        job.Status = TranscriptionJobStatus.Running;
        job.SessionId = "sess-1";
        await harness.Jobs.UpdateAsync(job);

        var recovered = await harness.Service.RecoverUnfinishedAsync();

        Assert.Equal(1, recovered);
        var stored = await harness.Service.GetJobAsync(job.JobId);
        Assert.Equal(TranscriptionJobStatus.Interrupted, stored!.Status);
        Assert.True(stored.IsRunnable, "an interrupted job must be resumable");
    }

    [Fact]
    public async Task FailedRun_RecordsTheError()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Files.OnRun = (_, _) => Task.FromResult(new FileTranscriptionResult { Error = "no audio" });

        var job = await harness.Service.EnqueueAsync(harness.Request(), Harness.Media());
        await harness.Service.DrainAsync();

        var stored = await harness.Service.GetJobAsync(job.JobId);
        Assert.Equal(TranscriptionJobStatus.Failed, stored!.Status);
        Assert.Equal("no audio", stored.Error);
    }

    [Fact]
    public async Task JobChanged_IsRaised()
    {
        await using var harness = await Harness.CreateAsync();
        var seen = new List<TranscriptionJobStatus>();
        harness.Service.JobChanged += (_, job) => seen.Add(job.Status);

        var job = await harness.Service.EnqueueAsync(harness.Request(), Harness.Media());
        await harness.Service.DrainAsync();

        Assert.Contains(TranscriptionJobStatus.Queued, seen);
        Assert.Contains(TranscriptionJobStatus.Running, seen);
        Assert.Equal(TranscriptionJobStatus.Succeeded, seen[^1]);
    }

    // ---- harness ---------------------------------------------------------------------------------

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(TempDatabase database)
        {
            Database = database;
            MediaFiles = database.CreateMediaFileRepository();
            Jobs = database.CreateTranscriptionJobRepository();
            Subtitles = database.CreateSubtitleRepository();
            Service = new TranscriptionJobService(Files, Jobs, MediaFiles, Subtitles, Resolve);
        }

        public TempDatabase Database { get; }
        public FakeFileTranscriptionService Files { get; } = new();
        public SqliteMediaFileRepository MediaFiles { get; }
        public SqliteTranscriptionJobRepository Jobs { get; }
        public SqliteSubtitleRepository Subtitles { get; }
        public TranscriptionJobService Service { get; }

        public static async Task<Harness> CreateAsync() => new(await TempDatabase.CreateAsync());

        public TranscriptionJobRequest Request(string title = "job") =>
            new("input.wav", TranscriptionMode.Fast, "streaming-zipformer-zh-14M", Title: title);

        public static MediaInfo Media() => new()
        {
            Path = "input.wav",
            FileName = "input.wav",
            Kind = MediaKind.Audio,
            ContainerFormat = "wav",
            Duration = TimeSpan.FromSeconds(10),
            AudioStreams = new[] { new AudioStreamInfo { Index = 0, Codec = "pcm_s16le", Channels = 1, SampleRate = 16000 } }
        };

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            await Database.DisposeAsync();
        }

        private static Task<FileTranscriptionRequest> Resolve(TranscriptionJob job, CancellationToken cancellationToken) =>
            Task.FromResult(new FileTranscriptionRequest(
                "input.wav",
                new ScriptedSegmentsAsrEngine("x"),
                new OfflineTranscriptionOptions { ModelId = job.ModelId },
                RunDiarization: job.RunDiarization,
                Title: job.Title));
    }
}
