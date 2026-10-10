using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// V0.5 Phase 3: the file job must decode once (tee'd to a temporary WAV), transcribe with global
/// timestamps, persist the segments <i>before</i> diarizing, and assemble a role-tagged dialogue.
/// </summary>
public sealed class FileTranscriptionServiceTests
{
    private const int Rate = 16000;

    [Fact]
    public async Task RunsTheWholePipeline_AndPersistsSegmentsBeforeDiarizing()
    {
        await using var harness = await Harness.CreateAsync();
        int segmentsSeenByDiarization = -1;
        harness.Diarization.OnRun = async (sessionId, ct) =>
        {
            segmentsSeenByDiarization = (await harness.Subtitles.GetSegmentsAsync(sessionId, ct)).Count;
            await harness.SeedTwoSpeakersAsync(sessionId, ct);
        };

        var result = await harness.Service.RunAsync(harness.Request());

        Assert.Null(result.Error);
        Assert.True(result.Completed);
        Assert.True(result.Diarized);
        Assert.Equal(2, result.Segments.Count);

        // The diarizer must see the transcript already in the database (it aligns against it).
        Assert.Equal(2, segmentsSeenByDiarization);

        var dialogue = Assert.IsType<DialogueTranscript>(result.Dialogue);
        Assert.Equal(2, dialogue.Turns.Count);
        Assert.Equal("你好世界", dialogue.Turns[0].Text);
        Assert.Equal("再见", dialogue.Turns[1].Text);
        Assert.Equal(new[] { "A", "B" }, dialogue.Turns.Select(t => t.SpeakerName).ToArray());
        Assert.Equal(2, dialogue.Participants.Count);

        // Segments are persisted with global times, in order, one row each.
        var stored = (await harness.Subtitles.GetSegmentsAsync(result.SessionId)).OrderBy(s => s.SequenceNumber).ToList();
        Assert.Equal(2, stored.Count);
        Assert.Equal(new[] { 0, 1 }, stored.Select(s => s.SequenceNumber).ToArray());
        Assert.InRange(stored[0].StartOffset.TotalSeconds, 0.98, 1.02);
        Assert.Equal("你好世界", stored[0].OriginalText);
        Assert.Equal("你好世界", stored[0].CorrectedText);
        Assert.False(stored[0].IsEdited);

        // The diarizer was pointed at the tee'd WAV inside the staging directory, now cleaned up.
        var wav = harness.Diarization.LastRequest!.AudioFilePath;
        Assert.EndsWith(".wav", wav);
        Assert.StartsWith(harness.StagingDirectory, wav);
        Assert.False(File.Exists(wav), "the temporary WAV must be deleted after the job");
        Assert.Empty(Directory.GetFiles(harness.StagingDirectory, "*.wav"));

        // The caller owns the engine.
        Assert.True(harness.Engine.IsInitialized);
    }

    [Fact]
    public async Task DiarizationFailure_DegradesToAnUnknownDialogue()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Diarization.Result = new DiarizationResult("run-1", false, 0, 0, 0, "model missing");

        var result = await harness.Service.RunAsync(harness.Request());

        Assert.True(result.Completed);
        Assert.False(result.Diarized);
        Assert.Equal("model missing", result.Warning);
        Assert.All(result.Dialogue!.Turns, t => Assert.True(t.IsUnknownSpeaker));
        Assert.Empty(result.Dialogue!.Participants);
    }

    [Fact]
    public async Task DiarizationBusy_IsASoftOutcome()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Diarization.Busy = true;

        var result = await harness.Service.RunAsync(harness.Request());

        Assert.True(result.Completed);
        Assert.False(result.Diarized);
        Assert.NotNull(result.Warning);
        Assert.Equal(0, harness.Diarization.RunCount);
        Assert.All(result.Dialogue!.Turns, t => Assert.True(t.IsUnknownSpeaker));
    }

    [Fact]
    public async Task RunDiarizationFalse_SkipsDiarizationEntirely()
    {
        await using var harness = await Harness.CreateAsync();

        var result = await harness.Service.RunAsync(harness.Request() with { RunDiarization = false });

        Assert.True(result.Completed);
        Assert.False(result.Diarized);
        Assert.Equal(0, harness.Diarization.RunCount);
        Assert.Null(result.Warning);
        Assert.All(result.Dialogue!.Turns, t => Assert.True(t.IsUnknownSpeaker));
    }

    [Fact]
    public async Task Cancellation_CleansUpTheTemporaryWav()
    {
        await using var harness = await Harness.CreateAsync();
        using var cts = new CancellationTokenSource();
        harness.Media.CancelAfterFirstBlock = cts;

        var result = await harness.Service.RunAsync(harness.Request(), cancellationToken: cts.Token);

        Assert.True(result.Cancelled);
        Assert.False(result.Completed);
        // The job got past the gate (so the WAV was created) and removed it again on the way out.
        Assert.True(Directory.Exists(harness.StagingDirectory));
        Assert.Empty(Directory.GetFiles(harness.StagingDirectory, "*.wav"));
    }

    [Fact]
    public async Task UnreadableFile_IsReportedAsAnError()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Media.ProbeError = new MediaDecodeException(MediaErrorKind.FileNotFound, "not found");

        var result = await harness.Service.RunAsync(harness.Request());

        Assert.NotNull(result.Error);
        Assert.False(result.Completed);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task SecondConcurrentRun_IsRejected()
    {
        await using var harness = await Harness.CreateAsync();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        harness.Diarization.OnRun = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var first = harness.Service.RunAsync(harness.Request());
        await entered.Task;                       // the first job is now inside diarization

        var second = await harness.Service.RunAsync(harness.Request());
        Assert.NotNull(second.Error);
        Assert.Contains("already running", second.Error);

        release.SetResult();
        var firstResult = await first;
        Assert.True(firstResult.Completed);
    }

    [Fact]
    public async Task Resume_ContinuesFromTheLastCommittedSegment()
    {
        await using var harness = await Harness.CreateAsync();

        // Attempt 1 sees only the first utterance.
        harness.Media.Blocks = harness.AllBlocks.Take(3).ToList();
        var first = await harness.Service.RunAsync(harness.Request());

        Assert.True(first.Completed);
        var only = Assert.Single(first.Segments);
        Assert.Equal("你好世界", only.Text);
        Assert.NotNull(first.SessionId);

        // Attempt 2 resumes the same session; the committed prefix must not be transcribed again.
        harness.Media.Blocks = harness.AllBlocks;
        int decodesBeforeResume = harness.Media.DecodeCalls;
        var second = await harness.Service.RunAsync(harness.Request() with { SessionId = first.SessionId });

        Assert.True(second.Completed);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal("再见", Assert.Single(second.Segments).Text);

        // The transcription pass of the resumed attempt was seeked to the end of the last committed
        // segment (the extra request after it is the whole-file WAV rebuild for diarization).
        Assert.Contains(
            harness.Media.Requests.Skip(decodesBeforeResume),
            r => Math.Abs(r.StartOffset.TotalSeconds - 3.0) < 0.01);

        var stored = (await harness.Subtitles.GetSegmentsAsync(first.SessionId!))
            .OrderBy(s => s.SequenceNumber).ToList();
        Assert.Equal(2, stored.Count);
        Assert.Equal(new[] { 0, 1 }, stored.Select(s => s.SequenceNumber).ToArray());
        Assert.Equal("你好世界", stored[0].OriginalText);
        Assert.Equal("再见", stored[1].OriginalText);
        Assert.True(stored[1].StartOffset >= stored[0].EndOffset - TimeSpan.FromMilliseconds(1),
            "the resumed segment must not overlap the committed one.");

        // A resumed job has no whole-file staging WAV, so diarization rebuilds it with one extra pass.
        Assert.Equal(decodesBeforeResume + 2, harness.Media.DecodeCalls);
    }

    // ---- harness ---------------------------------------------------------------------------------

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(TempDatabase database, string stagingDirectory)
        {
            Database = database;
            StagingDirectory = stagingDirectory;
        }

        public TempDatabase Database { get; }
        public string StagingDirectory { get; }
        public FakeMediaDecodeService Media { get; private set; } = null!;
        public FakeDiarizationService Diarization { get; private set; } = null!;
        public SqliteSubtitleRepository Subtitles { get; private set; } = null!;
        public SqliteSpeakerRepository Speakers { get; private set; } = null!;
        public ScriptedSegmentsAsrEngine Engine { get; private set; } = null!;
        public FileTranscriptionService Service { get; private set; } = null!;
        public OfflineTranscriptionOptions Options { get; } = new() { ModelId = "test-model" };
        public IReadOnlyList<PcmBlock> AllBlocks { get; private set; } = Array.Empty<PcmBlock>();

        public static async Task<Harness> CreateAsync()
        {
            var database = await TempDatabase.CreateAsync();
            string staging = Path.Combine(Path.GetTempPath(), "lms-ft-" + Guid.NewGuid().ToString("N"));
            var harness = new Harness(database, staging);

            harness.Subtitles = database.CreateSubtitleRepository();
            harness.Speakers = database.CreateSpeakerRepository();
            harness.AllBlocks = Blocks();
            harness.Media = new FakeMediaDecodeService(Info(), harness.AllBlocks);
            harness.Diarization = new FakeDiarizationService();
            harness.Engine = new ScriptedSegmentsAsrEngine("你好世界", "再见");
            harness.Service = new FileTranscriptionService(
                harness.Media,
                harness.Diarization,
                harness.Subtitles,
                harness.Speakers,
                new TranscriptAlignmentService(),
                staging);
            return harness;
        }

        public FileTranscriptionRequest Request() =>
            new("input.wav", Engine, Options, Title: "unit-file");

        /// <summary>Mimics what the real diarizer persists: two speakers and one assignment each.</summary>
        public async Task SeedTwoSpeakersAsync(string sessionId, CancellationToken cancellationToken)
        {
            var segments = (await Subtitles.GetSegmentsAsync(sessionId, cancellationToken))
                .OrderBy(s => s.SequenceNumber).ToList();
            var a = new Speaker { SessionId = sessionId, Label = "A", ColorArgb = unchecked((int)0xFFC96442) };
            var b = new Speaker { SessionId = sessionId, Label = "B", SortOrder = 1, ColorArgb = unchecked((int)0xFF4C6EF5) };
            await Speakers.UpsertSpeakerAsync(a, cancellationToken);
            await Speakers.UpsertSpeakerAsync(b, cancellationToken);
            await Speakers.UpsertAssignmentsAsync(new[]
            {
                new SpeakerAssignment { SessionId = sessionId, SegmentId = segments[0].SegmentId, SpeakerId = a.SpeakerId },
                new SpeakerAssignment { SessionId = sessionId, SegmentId = segments[1].SegmentId, SpeakerId = b.SpeakerId }
            }, overwriteManual: false, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            try
            {
                if (Directory.Exists(StagingDirectory))
                {
                    Directory.Delete(StagingDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        private static MediaInfo Info() => new()
        {
            Path = "input.wav",
            FileName = "input.wav",
            FileSize = 1024,
            Duration = TimeSpan.FromSeconds(7),
            Kind = MediaKind.Audio,
            ContainerFormat = "wav",
            AudioStreams = new[] { new AudioStreamInfo { Index = 0, Codec = "pcm_s16le", Channels = 1, SampleRate = Rate } }
        };

        private static IReadOnlyList<PcmBlock> Blocks() => new[]
        {
            new PcmBlock(new float[Rate], TimeSpan.Zero, Rate),
            new PcmBlock(Speech(2.0), TimeSpan.FromSeconds(1), Rate),
            new PcmBlock(new float[Rate], TimeSpan.FromSeconds(3), Rate),
            new PcmBlock(Speech(2.0), TimeSpan.FromSeconds(4), Rate),
            new PcmBlock(new float[Rate], TimeSpan.FromSeconds(6), Rate)
        };

        private static float[] Speech(double seconds)
        {
            var data = new float[(int)(seconds * Rate)];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (float)(0.3 * Math.Sin(2.0 * Math.PI * 440.0 * i / Rate));
            }

            return data;
        }
    }
}
