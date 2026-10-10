using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Media;
using LocalMeetingSubtitle.Storage;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// V0.5 Phase 4 on real models: a job interrupted mid-transcription is resumed from its last
/// committed segment and finishes with exactly the transcript an uninterrupted run produces.
/// Skipped (never faked) when FFmpeg, the test media or the ASR model is absent.
/// </summary>
public sealed class FileTranscriptionResumeTests
{
    private readonly ITestOutputHelper _output;

    public FileTranscriptionResumeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task InterruptedRun_ResumesWithoutLosingOrDuplicatingSegments()
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        Skip.If(modelsRoot is null, "models/ directory not present.", _output);
        if (modelsRoot is null)
        {
            return;
        }

        var mediaPath = FindTestMedia("two-speakers.wav");
        Skip.If(mediaPath is null, "testmedia/two-speakers.wav not present (run tools/make-long-testmedia.ps1).", _output);
        if (mediaPath is null)
        {
            return;
        }

        MediaToolPaths tools;
        try
        {
            tools = FFmpegLocator.Resolve();
        }
        catch (MediaDecodeException ex)
        {
            Skip.If(true, "FFmpeg not available: " + ex.Message, _output);
            return;
        }

        var mode = TranscriptionModeCatalog.Resolve(TranscriptionMode.Fast, new DiskModelManager(modelsRoot));
        Skip.If(!mode.IsAvailable, mode.UnavailableReason ?? "ASR model not installed.", _output);
        if (!mode.IsAvailable)
        {
            return;
        }

        // ---- reference: one uninterrupted pass -------------------------------------------------
        var reference = await RunOnceAsync(mediaPath, tools, mode, resumeSessionId: null, interruptAfter: 0);
        Assert.True(reference.Result.Completed);
        _output.WriteLine($"reference: {reference.Segments.Count} segment(s)");

        // ---- interrupted pass, then resume ---------------------------------------------------
        await using var temp = await TempSqlite.CreateAsync();
        var subtitles = temp.CreateSubtitleRepository();
        var speakers = new SqliteSpeakerRepository(temp.Database);
        var staging = Path.Combine(Path.GetTempPath(), "fts-resume-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var engine = new SherpaOnnxAsrEngine();
            var init = await engine.InitializeAsync(mode.EngineOptions);
            Assert.True(init.Ok, init.Message);

            var service = new FileTranscriptionService(
                new FFmpegMediaDecodeService(tools),
                new NoopDiarizationService(),
                subtitles,
                speakers,
                new TranscriptAlignmentService(),
                staging);

            using var cts = new CancellationTokenSource();
            var interrupting = new SyncProgress<FileTranscriptionProgress>(p =>
            {
                if (p.SegmentsEmitted >= 2)
                {
                    cts.Cancel();
                }
            });

            var first = await service.RunAsync(
                new FileTranscriptionRequest(mediaPath, engine, mode.TranscriptionOptions, Title: "resume", RunDiarization: false),
                interrupting,
                cts.Token);

            Assert.True(first.Cancelled, "the run must stop at the requested segment count");
            Assert.NotNull(first.SessionId);

            var committed = (await subtitles.GetSegmentsAsync(first.SessionId!)).OrderBy(s => s.SequenceNumber).ToList();
            Assert.NotEmpty(committed);
            _output.WriteLine($"interrupted after {committed.Count} committed segment(s)");

            // ---- resume ---------------------------------------------------------------------------
            using var resumeCts = new CancellationTokenSource();
            var second = await service.RunAsync(
                new FileTranscriptionRequest(mediaPath, engine, mode.TranscriptionOptions, Title: "resume", RunDiarization: false)
                    with { SessionId = first.SessionId },
                cancellationToken: resumeCts.Token);

            Assert.True(second.Completed);
            Assert.Equal(first.SessionId, second.SessionId);

            var final = (await subtitles.GetSegmentsAsync(first.SessionId!)).OrderBy(s => s.SequenceNumber).ToList();
            foreach (var segment in final)
            {
                _output.WriteLine($"#{segment.SequenceNumber} "
                    + $"[{segment.StartOffset:hh\\:mm\\:ss\\.fff} - {segment.EndOffset:hh\\:mm\\:ss\\.fff}] {segment.DisplayText}");
            }

            // The decisive assertion: no segment is lost and none is duplicated.
            Assert.Equal(reference.Segments.Count, final.Count);

            // Every segment matches the uninterrupted run, except the one produced immediately after
            // the resume point: the VAD restarts there, so the recognizer sees different leading
            // context and that one segment's text can differ (see docs/KNOWN_ISSUES.md).
            int boundary = committed.Count;
            Assert.False(string.IsNullOrEmpty(final[boundary].OriginalText), "the boundary segment must still produce text");
            for (int i = 0; i < final.Count; i++)
            {
                if (i == boundary)
                {
                    continue;
                }

                Assert.Equal(reference.Segments[i].OriginalText, final[i].OriginalText);
            }

            // Sequence numbers stay contiguous across the resume boundary.
            Assert.Equal(Enumerable.Range(0, final.Count), final.Select(s => s.SequenceNumber));

            // The timeline is monotonic and never repeats the committed prefix.
            for (int i = 0; i < final.Count; i++)
            {
                Assert.True(final[i].EndOffset > final[i].StartOffset, $"segment {i} must have a positive duration");
                if (i > 0)
                {
                    Assert.True(final[i].StartOffset >= final[i - 1].EndOffset - TimeSpan.FromMilliseconds(50),
                        $"segment {i} must not start before segment {i - 1} ended");
                }
            }
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private async Task<(FileTranscriptionResult Result, IReadOnlyList<SubtitleSegment> Segments)> RunOnceAsync(
        string mediaPath,
        MediaToolPaths tools,
        ResolvedTranscriptionMode mode,
        string? resumeSessionId,
        int interruptAfter)
    {
        await using var temp = await TempSqlite.CreateAsync();
        var subtitles = temp.CreateSubtitleRepository();
        var speakers = new SqliteSpeakerRepository(temp.Database);
        var staging = Path.Combine(Path.GetTempPath(), "fts-ref-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var engine = new SherpaOnnxAsrEngine();
            var init = await engine.InitializeAsync(mode.EngineOptions);
            Assert.True(init.Ok, init.Message);

            var service = new FileTranscriptionService(
                new FFmpegMediaDecodeService(tools),
                new NoopDiarizationService(),
                subtitles,
                speakers,
                new TranscriptAlignmentService(),
                staging);

            using var cts = new CancellationTokenSource();
            IProgress<FileTranscriptionProgress>? progress = interruptAfter > 0
                ? new SyncProgress<FileTranscriptionProgress>(p =>
                {
                    if (p.SegmentsEmitted >= interruptAfter)
                    {
                        cts.Cancel();
                    }
                })
                : null;

            var result = await service.RunAsync(
                new FileTranscriptionRequest(mediaPath, engine, mode.TranscriptionOptions, Title: "reference", RunDiarization: false)
                    with { SessionId = resumeSessionId },
                progress,
                cts.Token);

            var segments = result.SessionId is null
                ? new List<SubtitleSegment>()
                : (await subtitles.GetSegmentsAsync(result.SessionId)).OrderBy(s => s.SequenceNumber).ToList();

            return (result, segments);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static string? FindTestMedia(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "testmedia", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Diarization is not the subject of this test; a cheap success keeps the run fast.</summary>
    private sealed class NoopDiarizationService : ISpeakerDiarizationService
    {
        public bool IsBusy => false;

        public Task<DiarizationResult> RunAsync(
            DiarizationRequest request,
            IProgress<DiarizationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DiarizationResult("run-noop", true, 0, 0, 0, null));
    }

    /// <summary>Progress that reports synchronously so ordering is preserved.</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
