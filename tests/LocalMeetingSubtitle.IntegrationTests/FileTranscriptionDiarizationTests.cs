using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Audio;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Media;
using LocalMeetingSubtitle.Storage;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// V0.5 Phase 3 on real models: a real media file is decoded, transcribed, diarized by the V0.4
/// pipeline and assembled into a role-tagged dialogue. Skipped (never faked) when FFmpeg, the test
/// media, the ASR model or the diarization models are absent.
/// </summary>
public sealed class FileTranscriptionDiarizationTests
{
    private const int SampleRate = 16000;

    private readonly ITestOutputHelper _output;

    public FileTranscriptionDiarizationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task TwoSpeakerFile_ProducesARoleTaggedDialogue()
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        Skip.If(modelsRoot is null, "models/ directory not present.", _output);
        if (modelsRoot is null)
        {
            return;
        }

        var diarizationOptions = ResolveDiarizationOptions(modelsRoot);
        Skip.If(diarizationOptions is null, "diarization models not present under models/.", _output);
        if (diarizationOptions is null)
        {
            return;
        }

        var tools = TryResolveTools();
        if (tools is null)
        {
            return;
        }

        var clip = TwoSpeakerClip(modelsRoot);
        Skip.If(clip is null, "two-speaker evaluation wavs not present under models/_diar-eval/.", _output);
        if (clip is null)
        {
            return;
        }

        var modelManager = new DiskModelManager(modelsRoot);
        var mode = TranscriptionModeCatalog.Resolve(TranscriptionMode.Fast, modelManager);
        Skip.If(!mode.IsAvailable, mode.UnavailableReason ?? "ASR model not installed.", _output);
        if (!mode.IsAvailable)
        {
            return;
        }

        string audioPath = Path.Combine(Path.GetTempPath(), "fts-" + Guid.NewGuid().ToString("N") + ".wav");
        string staging = Path.Combine(Path.GetTempPath(), "fts-stage-" + Guid.NewGuid().ToString("N"));
        using (var wav = new PcmWavWriter(audioPath, SampleRate))
        {
            wav.Write(clip.Value.Samples);
        }

        await using var temp = await TempSqlite.CreateAsync();
        var subtitles = temp.CreateSubtitleRepository();
        var speakers = new SqliteSpeakerRepository(temp.Database);

        try
        {
            using var engine = new SherpaOnnxAsrEngine();
            var init = await engine.InitializeAsync(mode.EngineOptions);
            Assert.True(init.Ok, init.Message);

            var diarization = new SpeakerDiarizationService(
                () => new SherpaOfflineSpeakerDiarizer(),
                new NaudioAudioFileLoader(),
                new SpeakerAlignmentService(),
                speakers,
                subtitles,
                diarizationOptions);

            var service = new FileTranscriptionService(
                new FFmpegMediaDecodeService(tools),
                diarization,
                subtitles,
                speakers,
                new TranscriptAlignmentService(),
                staging);

            var result = await service.RunAsync(new FileTranscriptionRequest(
                audioPath,
                engine,
                mode.TranscriptionOptions,
                Title: "two-speakers"));

            Assert.Null(result.Error);
            Assert.True(result.Completed);
            Assert.True(result.Diarized, result.Warning);
            Assert.NotEmpty(result.Segments);

            var dialogue = Assert.IsType<DialogueTranscript>(result.Dialogue);
            foreach (var turn in dialogue.Turns)
            {
                _output.WriteLine($"[{turn.Start:hh\\:mm\\:ss\\.fff} - {turn.End:hh\\:mm\\:ss\\.fff}] {turn.SpeakerName}: {turn.Text}");
            }

            _output.WriteLine($"participants={dialogue.Participants.Count} turns={dialogue.Turns.Count} segments={result.Segments.Count}");

            Assert.True(dialogue.Participants.Count >= 2,
                $"the two-speaker clip must yield at least two speakers, got {dialogue.Participants.Count}");
            Assert.True(dialogue.Turns.Count >= 2, $"expected at least two turns, got {dialogue.Turns.Count}");

            // A turn is by construction a single speaker, and adjacent turns must differ.
            for (int i = 1; i < dialogue.Turns.Count; i++)
            {
                Assert.NotEqual(dialogue.Turns[i].SpeakerId, dialogue.Turns[i - 1].SpeakerId);
                Assert.True(dialogue.Turns[i].Start >= dialogue.Turns[i - 1].End - TimeSpan.FromMilliseconds(1));
            }

            Assert.All(dialogue.Turns, t => Assert.NotEmpty(t.SegmentIds));

            // Every segment got exactly one assignment.
            var assignments = await speakers.GetAssignmentsAsync(result.SessionId);
            Assert.Equal(result.Segments.Count, assignments.Count);

            var text = string.Concat(dialogue.Turns.Select(t => t.Text));
            Assert.Contains(text, IsCjk);

            // Participant speaking time equals the sum of that speaker's turn durations.
            foreach (var participant in dialogue.Participants)
            {
                var expected = dialogue.Turns
                    .Where(t => t.SpeakerId == participant.SpeakerId)
                    .Sum(t => t.Duration.TotalSeconds);
                Assert.Equal(expected, participant.SpeakingTime.TotalSeconds, 3);
            }
        }
        finally
        {
            TryDelete(audioPath);
            TryDeleteDirectory(staging);
        }
    }

    [Fact]
    public async Task VideoFile_IsDiarizableThroughTheTemporaryWav()
    {
        // The V0.4 diarizer loads its input with NAudio, which cannot read a video container. The file
        // job decodes with FFmpeg and tees the PCM into a WAV, so a video must still be diarizable.
        var modelsRoot = ModelLocator.FindModelsRoot();
        Skip.If(modelsRoot is null, "models/ directory not present.", _output);
        if (modelsRoot is null)
        {
            return;
        }

        var diarizationOptions = ResolveDiarizationOptions(modelsRoot);
        Skip.If(diarizationOptions is null, "diarization models not present under models/.", _output);
        if (diarizationOptions is null)
        {
            return;
        }

        var mediaPath = FindTestMedia("two-tracks.mp4");
        Skip.If(mediaPath is null, "testmedia/two-tracks.mp4 not present.", _output);
        if (mediaPath is null)
        {
            return;
        }

        var tools = TryResolveTools();
        if (tools is null)
        {
            return;
        }

        var modelManager = new DiskModelManager(modelsRoot);
        var mode = TranscriptionModeCatalog.Resolve(TranscriptionMode.Fast, modelManager);
        Skip.If(!mode.IsAvailable, mode.UnavailableReason ?? "ASR model not installed.", _output);
        if (!mode.IsAvailable)
        {
            return;
        }

        string staging = Path.Combine(Path.GetTempPath(), "fts-stage-" + Guid.NewGuid().ToString("N"));
        await using var temp = await TempSqlite.CreateAsync();
        var subtitles = temp.CreateSubtitleRepository();
        var speakers = new SqliteSpeakerRepository(temp.Database);

        try
        {
            using var engine = new SherpaOnnxAsrEngine();
            var init = await engine.InitializeAsync(mode.EngineOptions);
            Assert.True(init.Ok, init.Message);

            var diarization = new SpeakerDiarizationService(
                () => new SherpaOfflineSpeakerDiarizer(),
                new NaudioAudioFileLoader(),
                new SpeakerAlignmentService(),
                speakers,
                subtitles,
                diarizationOptions);

            var service = new FileTranscriptionService(
                new FFmpegMediaDecodeService(tools),
                diarization,
                subtitles,
                speakers,
                new TranscriptAlignmentService(),
                staging);

            var result = await service.RunAsync(new FileTranscriptionRequest(
                mediaPath,
                engine,
                mode.TranscriptionOptions,
                AudioStreamIndex: 1,
                Title: "two-tracks"));

            Assert.Null(result.Error);
            Assert.True(result.Completed);
            Assert.NotEmpty(result.Segments);
            Assert.True(result.Diarized, result.Warning);
            Assert.NotNull(result.Dialogue);
            Assert.NotEmpty(result.Dialogue!.Turns);

            foreach (var turn in result.Dialogue!.Turns)
            {
                _output.WriteLine($"[{turn.Start:hh\\:mm\\:ss\\.fff} - {turn.End:hh\\:mm\\:ss\\.fff}] {turn.SpeakerName}: {turn.Text}");
            }
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------

    private MediaToolPaths? TryResolveTools()
    {
        try
        {
            return FFmpegLocator.Resolve();
        }
        catch (MediaDecodeException ex)
        {
            Skip.If(true, "FFmpeg not available: " + ex.Message, _output);
            return null;
        }
    }

    private static DiarizationEngineOptions? ResolveDiarizationOptions(string modelsRoot)
    {
        var segmentation = DiarizationModelCatalog.Segmentation;
        var embedding = DiarizationModelCatalog.Embedding;
        string segmentationPath = Path.Combine(modelsRoot, segmentation.DirectoryName, segmentation.Files[0].RelativePath);
        string embeddingPath = Path.Combine(modelsRoot, embedding.DirectoryName, embedding.Files[0].RelativePath);
        if (!File.Exists(segmentationPath) || !File.Exists(embeddingPath))
        {
            return null;
        }

        return new DiarizationEngineOptions
        {
            SegmentationModelPath = segmentationPath,
            EmbeddingModelPath = embeddingPath,
            ClusteringThreshold = 0.5f,
            MinDurationOn = 0.3f,
            MinDurationOff = 0.5f
        };
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

    /// <summary>A, gap, A, gap, B, gap, B — the first half is speaker A, the second half speaker B.</summary>
    private static (float[] Samples, double SplitSeconds)? TwoSpeakerClip(string modelsRoot)
    {
        string a = Path.Combine(modelsRoot, "_diar-eval", "fangjun-sr-1.wav");
        string b = Path.Combine(modelsRoot, "_diar-eval", "leijun-sr-1.wav");
        if (!File.Exists(a) || !File.Exists(b))
        {
            return null;
        }

        var (speakerA, rateA) = WavReader.ReadMono16BitPcm(a);
        var (speakerB, rateB) = WavReader.ReadMono16BitPcm(b);
        if (rateA != SampleRate || rateB != SampleRate)
        {
            return null;
        }

        var gapShort = TestAudio.Silence(0.3);
        var gapLong = TestAudio.Silence(0.5);
        var firstBlock = TestAudio.Concat(speakerA, gapShort, speakerA);
        var secondBlock = TestAudio.Concat(speakerB, gapShort, speakerB);

        return (TestAudio.Concat(firstBlock, gapLong, secondBlock), firstBlock.Length / (double)SampleRate + 0.25);
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0xF900 && c <= 0xFAFF);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
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
}
