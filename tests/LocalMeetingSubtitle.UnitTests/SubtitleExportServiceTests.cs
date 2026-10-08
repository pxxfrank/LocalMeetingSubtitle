using System.Text;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Export;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SubtitleExportServiceTests : IDisposable
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    private readonly string _directory;

    public SubtitleExportServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lms-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    [Fact]
    public async Task ExportAsync_Txt_WritesUtf8WithBom()
    {
        var service = new SubtitleExportService();
        var path = PathFor("transcript.txt");
        var request = new ExportRequest(
            TestData.ExportSession("Weekly Sync"),
            new[] { TestData.ExportSegment(0, "hello", 0, 1000) },
            ExportFormat.Txt,
            path,
            IncludeTimestamps: true);

        await service.ExportAsync(request);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 3);
        Assert.Equal(Utf8Bom, bytes.Take(3).ToArray());

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
        Assert.Contains("[00:00:00] hello", text);
    }

    [Fact]
    public async Task ExportAsync_Markdown_WritesUtf8WithoutBom()
    {
        var service = new SubtitleExportService();
        var path = PathFor("transcript.md");
        var request = new ExportRequest(
            TestData.ExportSession(),
            new[] { TestData.ExportSegment(0, "hello", 0, 1000) },
            ExportFormat.Markdown,
            path,
            IncludeTimestamps: true);

        await service.ExportAsync(request);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 3);
        Assert.NotEqual(Utf8Bom, bytes.Take(3).ToArray());
        Assert.Equal((byte)'#', bytes[0]);
    }

    [Fact]
    public async Task ExportAsync_UsesDisplayText_SoUserEditsWin()
    {
        var service = new SubtitleExportService();
        var path = PathFor("edited.srt");
        var segment = TestData.ExportSegment(0, "原始文本", 0, 1000);
        segment.CorrectedText = "用户修改后的字幕";
        segment.IsEdited = true;

        var request = new ExportRequest(
            TestData.ExportSession(),
            new[] { segment },
            ExportFormat.Srt,
            path,
            IncludeTimestamps: true);

        await service.ExportAsync(request);

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
        Assert.Contains("用户修改后的字幕", text);
        Assert.DoesNotContain("原始文本", text);
    }

    [Fact]
    public async Task ExportAsync_CreatesMissingOutputDirectory()
    {
        var service = new SubtitleExportService();
        var path = Path.Combine(_directory, "nested", "deeper", "out.txt");
        var request = new ExportRequest(
            TestData.ExportSession(),
            Array.Empty<SubtitleSegment>(),
            ExportFormat.Txt,
            path,
            IncludeTimestamps: true);

        await service.ExportAsync(request);

        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(ExportFormat.Txt, ".txt")]
    [InlineData(ExportFormat.Srt, ".srt")]
    [InlineData(ExportFormat.Markdown, ".md")]
    public void GetExtension_ReturnsExpected(ExportFormat format, string expected)
    {
        Assert.Equal(expected, new SubtitleExportService().GetExtension(format));
    }

    [Fact]
    public void SupportedFormats_AndDefaultFormatters_CoverAllExportFormats()
    {
        var service = new SubtitleExportService();

        Assert.Equal(
            new[] { ExportFormat.Txt, ExportFormat.Srt, ExportFormat.Markdown },
            service.SupportedFormats);
        Assert.Equal(3, SubtitleExportService.DefaultFormatters().Count);
    }
}
