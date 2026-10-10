using LocalMeetingSubtitle.Media;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// Covers the deterministic part of <see cref="FFmpegLocator"/> — explicit-directory resolution —
/// without needing a real FFmpeg install (only the file names must exist).
/// </summary>
public sealed class FFmpegLocatorTests
{
    [Fact]
    public void Explicit_directory_resolves_the_two_tools()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "ffmpeg.exe"), "");
        File.WriteAllText(Path.Combine(directory, "ffprobe.exe"), "");

        try
        {
            var tools = FFmpegLocator.Resolve(directory);

            Assert.Equal(Path.Combine(directory, "ffmpeg.exe"), tools.FfmpegPath);
            Assert.Equal(Path.Combine(directory, "ffprobe.exe"), tools.FfprobePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Explicit_root_uses_its_bin_subfolder_when_the_root_has_no_tools()
    {
        var directory = CreateTempDirectory();
        var bin = Path.Combine(directory, "bin");
        Directory.CreateDirectory(bin);
        // Only ffmpeg sits at the root (incomplete); both tools sit under bin/.
        File.WriteAllText(Path.Combine(directory, "ffmpeg.exe"), "");
        File.WriteAllText(Path.Combine(bin, "ffmpeg.exe"), "");
        File.WriteAllText(Path.Combine(bin, "ffprobe.exe"), "");

        try
        {
            var tools = FFmpegLocator.Resolve(directory);

            Assert.Equal(Path.Combine(bin, "ffmpeg.exe"), tools.FfmpegPath);
            Assert.Equal(Path.Combine(bin, "ffprobe.exe"), tools.FfprobePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-ffmpeg-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
