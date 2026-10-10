using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Media;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// Exercises the real FFmpeg-backed media layer against the sample media in <c>testmedia/</c>.
/// Skips when the media samples or the bundled FFmpeg are not provisioned.
/// </summary>
public sealed class MediaDecodeTests
{
    private readonly ITestOutputHelper _output;

    public MediaDecodeTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("0.mp3")]
    [InlineData("0.m4a")]
    [InlineData("0.aac")]
    [InlineData("0.flac")]
    [InlineData("0.ogg")]
    [InlineData("0.mkv")]
    [InlineData("0.mov")]
    [InlineData("0.avi")]
    public async Task Probe_reports_audio_and_a_valid_duration(string fileName)
    {
        if (!TryCreateService(out var service, out var mediaRoot)) return;

        var path = Path.Combine(mediaRoot!, fileName);
        var info = await service!.ProbeAsync(path);

        _output.WriteLine($"{fileName}: kind={info.Kind} duration={info.Duration.TotalSeconds:F3}s "
                          + $"audio={info.AudioStreams.Count} container='{info.ContainerFormat}'");

        Assert.True(info.HasAudio, $"{fileName} should expose at least one audio stream.");
        Assert.True(info.AudioStreams.Count >= 1);
        Assert.InRange(info.Duration.TotalSeconds, 5.0, 6.5);
    }

    [Fact]
    public async Task Probe_reports_no_audio_for_a_video_only_file()
    {
        if (!TryCreateService(out var service, out var mediaRoot)) return;

        var info = await service!.ProbeAsync(Path.Combine(mediaRoot!, "noaudio.mp4"));

        Assert.False(info.HasAudio);
        Assert.Equal(MediaKind.Video, info.Kind);
    }

    [Fact]
    public async Task Decoding_a_video_only_file_throws_NoAudioTrack()
    {
        if (!TryCreateService(out var service, out var mediaRoot)) return;

        var request = new MediaDecodeRequest(Path.Combine(mediaRoot!, "noaudio.mp4"));
        var ex = await Assert.ThrowsAsync<MediaDecodeException>(async () =>
        {
            await foreach (var _ in service!.DecodeAsync(request)) { }
        });

        Assert.Equal(MediaErrorKind.NoAudioTrack, ex.Kind);
    }

    [Fact]
    public async Task Decoding_a_mp4_extracts_16kHz_mono_audio()
    {
        if (!TryCreateService(out var service, out var mediaRoot)) return;

        var request = new MediaDecodeRequest(Path.Combine(mediaRoot!, "0.mp4"));
        var (samples, sampleRate, blockCount, allNonEmpty) = await DecodeAllAsync(service!, request);

        _output.WriteLine($"0.mp4: {samples} samples, {blockCount} block(s), rate={sampleRate}");

        Assert.True(blockCount > 0);
        Assert.True(allNonEmpty, "every PCM block should be non-empty");
        Assert.Equal(16000, sampleRate);
        Assert.InRange(samples / 16000.0, 5.0, 6.5);
    }

    [Fact]
    public async Task Decoding_the_second_audio_track_succeeds()
    {
        if (!TryCreateService(out var service, out var mediaRoot)) return;

        var request = new MediaDecodeRequest(Path.Combine(mediaRoot!, "two-tracks.mp4"), AudioStreamIndex: 1);
        var (samples, sampleRate, blockCount, allNonEmpty) = await DecodeAllAsync(service!, request);

        _output.WriteLine($"two-tracks.mp4 [1]: {samples} samples, {blockCount} block(s), rate={sampleRate}");

        Assert.True(blockCount > 0);
        Assert.True(allNonEmpty);
        Assert.Equal(16000, sampleRate);
        Assert.InRange(samples / 16000.0, 5.0, 6.5);
    }

    [Fact]
    public async Task Decoding_an_out_of_range_stream_index_throws()
    {
        if (!TryCreateService(out var service, out var mediaRoot)) return;

        var request = new MediaDecodeRequest(Path.Combine(mediaRoot!, "two-tracks.mp4"), AudioStreamIndex: 9);
        var ex = await Assert.ThrowsAsync<MediaDecodeException>(async () =>
        {
            await foreach (var _ in service!.DecodeAsync(request)) { }
        });

        Assert.Equal(MediaErrorKind.StreamIndexOutOfRange, ex.Kind);
    }

    [Fact]
    public async Task Decoding_a_unicode_path_with_a_space_succeeds()
    {
        if (!TryCreateService(out var service, out var mediaRoot)) return;

        var tempDirectory = Path.Combine(Path.GetTempPath(), "测试 folder");
        Directory.CreateDirectory(tempDirectory);
        var copied = Path.Combine(tempDirectory, "0 拷贝.mp3");
        File.Copy(Path.Combine(mediaRoot!, "0.mp3"), copied, overwrite: true);

        try
        {
            var request = new MediaDecodeRequest(copied);
            var (samples, sampleRate, blockCount, _) = await DecodeAllAsync(service!, request);

            _output.WriteLine($"unicode path: {samples} samples, {blockCount} block(s), rate={sampleRate}");

            Assert.True(blockCount > 0);
            Assert.Equal(16000, sampleRate);
            Assert.InRange(samples / 16000.0, 5.0, 6.5);
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Probing_a_missing_file_throws_FileNotFound()
    {
        if (!TryCreateService(out var service, out _)) return;

        var missing = Path.Combine(Path.GetTempPath(), "lms-" + Guid.NewGuid().ToString("N") + ".mp3");
        var ex = await Assert.ThrowsAsync<MediaDecodeException>(() => service!.ProbeAsync(missing));

        Assert.Equal(MediaErrorKind.FileNotFound, ex.Kind);
    }

    /// <summary>Consumes a decode stream and returns aggregate counts without retaining the audio.</summary>
    private static async Task<(long Samples, int SampleRate, int BlockCount, bool AllNonEmpty)> DecodeAllAsync(
        IMediaDecodeService service,
        MediaDecodeRequest request)
    {
        long samples = 0;
        int sampleRate = 0;
        int blockCount = 0;
        bool allNonEmpty = true;

        await foreach (var block in service.DecodeAsync(request))
        {
            blockCount++;
            samples += block.Samples.Length;
            if (block.SampleRate != 0)
            {
                sampleRate = block.SampleRate;
            }

            if (block.Samples.Length == 0)
            {
                allNonEmpty = false;
            }
        }

        return (samples, sampleRate, blockCount, allNonEmpty);
    }

    /// <summary>Resolves the media samples + FFmpeg tools, skipping (and returning false) when absent.</summary>
    private bool TryCreateService(out FFmpegMediaDecodeService? service, out string? mediaRoot)
    {
        mediaRoot = TestMediaLocator.FindTestMediaRoot();
        MediaToolPaths? tools = null;
        try
        {
            tools = FFmpegLocator.Resolve();
        }
        catch (MediaDecodeException)
        {
            // FFmpeg not provisioned; skip below.
        }

        Skip.If(mediaRoot is null || tools is null,
            "testmedia/ or the bundled FFmpeg is not present.", _output);

        if (mediaRoot is null || tools is null)
        {
            service = null;
            return false;
        }

        service = new FFmpegMediaDecodeService(tools);
        return true;
    }
}

/// <summary>Locates the repository <c>testmedia</c> directory by walking up from the test binaries.</summary>
internal static class TestMediaLocator
{
    public static string? FindTestMediaRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "testmedia");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
