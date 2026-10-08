using System.Text;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Export;

namespace LocalMeetingSubtitle.IntegrationTests;

public sealed class ExportRoundTripTests
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    [Fact]
    public async Task Export_FromRealSqliteSegments_WritesTxtSrtMarkdown()
    {
        await using var temp = await TempSqlite.CreateAsync();
        var repository = temp.CreateSubtitleRepository();

        var session = new MeetingSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Title = "产品评审",
            StartTime = new DateTimeOffset(2026, 3, 4, 9, 5, 0, TimeSpan.Zero)
        };
        await repository.CreateSessionAsync(session);

        await repository.AppendSegmentAsync(new SubtitleSegment
        {
            SessionId = session.SessionId,
            SequenceNumber = 1,
            StartOffset = TimeSpan.Zero,
            EndOffset = TimeSpan.FromSeconds(2.5),
            OriginalText = "大家好",
            CorrectedText = "大家好"
        });
        await repository.AppendSegmentAsync(new SubtitleSegment
        {
            SessionId = session.SessionId,
            SequenceNumber = 2,
            StartOffset = TimeSpan.FromSeconds(2.5),
            EndOffset = TimeSpan.FromSeconds(5),
            OriginalText = "欢迎参加评审",
            CorrectedText = "欢迎参加评审"
        });

        var segments = await repository.GetSegmentsAsync(session.SessionId);
        Assert.Equal(2, segments.Count);

        var directory = Path.Combine(Path.GetTempPath(), "lms-export-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var service = new SubtitleExportService();

        try
        {
            // TXT: UTF-8 with BOM, clock timestamps, Chinese preserved.
            var txtPath = Path.Combine(directory, "transcript.txt");
            await service.ExportAsync(new ExportRequest(session, segments, ExportFormat.Txt, txtPath, IncludeTimestamps: true));
            var txtBytes = await File.ReadAllBytesAsync(txtPath);
            Assert.True(txtBytes.Length > 3);
            Assert.Equal(Utf8Bom, txtBytes.Take(3).ToArray());
            var txt = await File.ReadAllTextAsync(txtPath, Encoding.UTF8);
            Assert.Contains("Meeting: 产品评审", txt);
            Assert.Contains("[00:00:00] 大家好", txt);
            Assert.Contains("[00:00:02] 欢迎参加评审", txt);

            // SRT: SubRip timestamp format and Chinese preserved.
            var srtPath = Path.Combine(directory, "transcript.srt");
            await service.ExportAsync(new ExportRequest(session, segments, ExportFormat.Srt, srtPath, IncludeTimestamps: true));
            var srt = await File.ReadAllTextAsync(srtPath, Encoding.UTF8);
            Assert.Contains("1\n00:00:00,000 --> 00:00:02,500\n大家好", srt);
            Assert.Contains("2\n00:00:02,500 --> 00:00:05,000\n欢迎参加评审", srt);

            // Markdown: UTF-8 without BOM, bullet with bold clock.
            var mdPath = Path.Combine(directory, "transcript.md");
            await service.ExportAsync(new ExportRequest(session, segments, ExportFormat.Markdown, mdPath, IncludeTimestamps: true));
            var mdBytes = await File.ReadAllBytesAsync(mdPath);
            Assert.True(mdBytes.Length > 3);
            Assert.NotEqual(Utf8Bom, mdBytes.Take(3).ToArray());
            var md = await File.ReadAllTextAsync(mdPath, Encoding.UTF8);
            Assert.StartsWith("# 产品评审", md);
            Assert.Contains("- **00:00:00** 大家好", md);
            Assert.Contains("- **00:00:02** 欢迎参加评审", md);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }
}
