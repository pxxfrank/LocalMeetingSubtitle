using LocalMeetingSubtitle.Core.Hotwords;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// Guards the constraint that bit us in production: sherpa-onnx cannot read a non-ASCII path, so the
/// hotwords file must live under an ASCII directory. A Chinese temp directory silently produced no
/// subtitles at all (verified A/B: empty result, RTF ~0.0002).
/// </summary>
public sealed class ModelHotwordFileTempPathTests
{
    [Fact]
    public void Default_temp_path_is_ascii()
    {
        var path = ModelHotwordFile.DefaultTempPath;

        Assert.True(ModelHotwordFile.IsAsciiPath(path), $"hotwords temp path must be ASCII: {path}");
        Assert.EndsWith("hotwords.txt", path);
        Assert.DoesNotContain("字幕君", path);
    }

    [Fact]
    public void IsAsciiPath_detects_non_ascii_segments()
    {
        Assert.True(ModelHotwordFile.IsAsciiPath(@"C:\Users\me\AppData\Local\Temp\SubtitleJun\hotwords.txt"));
        Assert.False(ModelHotwordFile.IsAsciiPath(@"C:\Users\me\AppData\Local\Temp\字幕君\hotwords.txt"));
        Assert.False(ModelHotwordFile.IsAsciiPath(@"C:\Users\张三\Temp\hotwords.txt"));
    }
}
