using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Audio;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;
using LocalMeetingSubtitle.Storage;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// Real-model diarization validation. Skipped when the diarization models (or the two-speaker
/// evaluation clips) are not present under <c>models/</c>, so a clean clone stays green.
///
/// The clip is built by concatenating two different single-speaker recordings
/// (fangjun-sr-1 as speaker A, leijun-sr-1 as speaker B) with short silences between them.
/// </summary>
public sealed class SpeakerDiarizationRealModelTests
{
    private const int SampleRate = 16000;

    private readonly ITestOutputHelper _output;

    public SpeakerDiarizationRealModelTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RealDiarizer_SeparatesTwoKnownSpeakers()
    {
        var options = ResolveOptions();
        Skip.If(options is null, "diarization models not present under models/.", _output);
        if (options is null) return;

        var clip = TwoSpeakerClip();
        Skip.If(clip is null, "two-speaker evaluation wavs not present under models/_diar-eval/.", _output);
        if (clip is null) return;

        using var engine = new SherpaOfflineSpeakerDiarizer();
        var init = engine.Initialize(options);
        Assert.True(init.Ok, init.Message);

        var intervals = engine.Process(clip.Samples, null, CancellationToken.None);
        Assert.NotEmpty(intervals);

        foreach (var interval in intervals.OrderBy(i => i.Start))
        {
            _output.WriteLine($"{interval.Start:mm\\:ss\\.ff} - {interval.End:mm\\:ss\\.ff}  speaker {interval.RawSpeakerIndex}  confidence {interval.Confidence}");
        }

        var distinct = intervals.Select(i => i.RawSpeakerIndex).Distinct().Count();
        Assert.True(distinct >= 2, $"expected at least 2 speakers, got {distinct}");

        // The first speaker(s) of the clip must differ from the last speaker(s): A ... B.
        var firstSpeaker = intervals.OrderBy(i => i.Start).First().RawSpeakerIndex;
        var lastSpeaker = intervals.OrderBy(i => i.End).Last().RawSpeakerIndex;
        Assert.NotEqual(firstSpeaker, lastSpeaker);
    }

    [Fact]
    public async Task DiarizationService_PersistsSpeakersAndAlignsSegments()
    {
        var options = ResolveOptions();
        Skip.If(options is null, "diarization models not present under models/.", _output);
        if (options is null) return;

        var clip = TwoSpeakerClip();
        Skip.If(clip is null, "two-speaker evaluation wavs not present under models/_diar-eval/.", _output);
        if (clip is null) return;

        await using var temp = await TempSqlite.CreateAsync();
        var subtitles = temp.CreateSubtitleRepository();
        var speakers = new SqliteSpeakerRepository(temp.Database);

        var session = new MeetingSession { Title = "diarization-it" };
        await subtitles.CreateSessionAsync(session);

        // Segment 1 sits entirely in speaker A's block, segment 2 entirely in speaker B's block.
        const string firstText = "第一位发言人 / first speaker";
        const string secondText = "第二位发言人 / second speaker";
        await subtitles.AppendSegmentAsync(new SubtitleSegment
        {
            SessionId = session.SessionId,
            SequenceNumber = 0,
            StartOffset = TimeSpan.Zero,
            EndOffset = TimeSpan.FromSeconds(clip.SplitSeconds - 0.3),
            OriginalText = firstText,
            CorrectedText = firstText
        });
        await subtitles.AppendSegmentAsync(new SubtitleSegment
        {
            SessionId = session.SessionId,
            SequenceNumber = 1,
            StartOffset = TimeSpan.FromSeconds(clip.SplitSeconds + 0.3),
            EndOffset = TimeSpan.FromSeconds(clip.TotalSeconds - 0.1),
            OriginalText = secondText,
            CorrectedText = secondText
        });

        var wavPath = Path.Combine(Path.GetTempPath(), "diar-" + Guid.NewGuid().ToString("N") + ".wav");
        WriteWav16(wavPath, clip.Samples);

        try
        {
            var service = new SpeakerDiarizationService(
                () => new SherpaOfflineSpeakerDiarizer(),
                new NaudioAudioFileLoader(),
                new SpeakerAlignmentService(),
                speakers,
                subtitles,
                options);

            var result = await service.RunAsync(new DiarizationRequest(session.SessionId, wavPath));
            _output.WriteLine($"result: success={result.Success} speakers={result.SpeakerCount} assigned={result.AssignedSegments} confirm={result.NeedsConfirmation} error={result.Error}");

            Assert.True(result.Success, result.Error);
            Assert.True(result.SpeakerCount >= 2, $"expected at least 2 speakers, got {result.SpeakerCount}");

            var assignments = (await speakers.GetAssignmentsAsync(session.SessionId)).OrderBy(a => a.SegmentId).ToList();
            Assert.Equal(2, assignments.Count);
            Assert.All(assignments, a => Assert.NotNull(a.SpeakerId));
            Assert.NotEqual(assignments[0].SpeakerId, assignments[1].SpeakerId);

            var stored = await speakers.GetSpeakersAsync(session.SessionId);
            Assert.Equal(result.SpeakerCount, stored.Count);

            // Original ASR text must never be modified by diarization.
            var segments = (await subtitles.GetSegmentsAsync(session.SessionId)).OrderBy(s => s.SequenceNumber).ToList();
            Assert.Equal(firstText, segments[0].OriginalText);
            Assert.Equal(secondText, segments[1].OriginalText);
            Assert.False(segments[0].IsEdited);
        }
        finally
        {
            try { File.Delete(wavPath); } catch (IOException) { }
        }
    }

    private static DiarizationEngineOptions? ResolveOptions()
    {
        var root = ModelLocator.FindModelsRoot();
        if (root is null) return null;

        var segmentation = Path.Combine(root, "sherpa-onnx-pyannote-segmentation-3-0", "model.onnx");
        var embedding = Path.Combine(root, "3dspeaker-eres2net-base-zh-16k", "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx");
        if (!File.Exists(segmentation) || !File.Exists(embedding)) return null;

        return new DiarizationEngineOptions
        {
            SegmentationModelPath = segmentation,
            EmbeddingModelPath = embedding,
            ClusteringThreshold = 0.5f,
            MinDurationOn = 0.3f,
            MinDurationOff = 0.5f
        };
    }

    private static TwoSpeakerSample? TwoSpeakerClip()
    {
        var root = ModelLocator.FindModelsRoot();
        if (root is null) return null;

        var a = Path.Combine(root, "_diar-eval", "fangjun-sr-1.wav");
        var b = Path.Combine(root, "_diar-eval", "leijun-sr-1.wav");
        if (!File.Exists(a) || !File.Exists(b)) return null;

        var (speakerA, rateA) = WavReader.ReadMono16BitPcm(a);
        var (speakerB, rateB) = WavReader.ReadMono16BitPcm(b);
        if (rateA != SampleRate || rateB != SampleRate) return null;

        var gapShort = TestAudio.Silence(0.3);
        var gapLong = TestAudio.Silence(0.5);

        // A, gap, A, gap, B, gap, B  -> first half is speaker A, second half is speaker B.
        var firstBlock = TestAudio.Concat(speakerA, gapShort, speakerA);
        var secondBlock = TestAudio.Concat(speakerB, gapShort, speakerB);
        var samples = TestAudio.Concat(firstBlock, gapLong, secondBlock);

        return new TwoSpeakerSample(samples, firstBlock.Length / (double)SampleRate + 0.25);
    }

    private static void WriteWav16(string path, float[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        int dataLength = samples.Length * 2;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write((short)Math.Clamp(sample * 32767f, -32768f, 32767f));
        }
    }

    private sealed record TwoSpeakerSample(float[] Samples, double SplitSeconds)
    {
        public double TotalSeconds => Samples.Length / (double)SampleRate;
    }
}
