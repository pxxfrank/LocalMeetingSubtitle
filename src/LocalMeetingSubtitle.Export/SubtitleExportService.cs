using System.Text;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Export;

/// <summary>
/// Picks the <see cref="ITranscriptFormatter"/> for the requested format and writes the rendered
/// transcript to <see cref="ExportRequest.OutputPath"/>. Always renders <c>DisplayText</c> so a
/// user's edits win over the corrected/original text.
/// </summary>
public sealed class SubtitleExportService : ISubtitleExportService
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IReadOnlyDictionary<ExportFormat, ITranscriptFormatter> _formatters;

    public SubtitleExportService(IEnumerable<ITranscriptFormatter>? formatters = null)
    {
        var resolved = (formatters ?? DefaultFormatters()).ToList();
        _formatters = resolved
            .GroupBy(f => f.Format)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    public static IReadOnlyList<ITranscriptFormatter> DefaultFormatters() => new ITranscriptFormatter[]
    {
        new TxtTranscriptFormatter(),
        new SrtTranscriptFormatter(),
        new MarkdownTranscriptFormatter(),
        new CsvTranscriptFormatter()
    };

    public IReadOnlyList<ExportFormat> SupportedFormats =>
        _formatters.Keys.OrderBy(f => (int)f).ToList();

    public string GetExtension(ExportFormat format) => format switch
    {
        ExportFormat.Txt => ".txt",
        ExportFormat.Srt => ".srt",
        ExportFormat.Markdown => ".md",
        ExportFormat.Csv => ".csv",
        _ => throw new NotSupportedException($"Unsupported export format '{format}'.")
    };

    public async Task ExportAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_formatters.TryGetValue(request.Format, out var formatter))
        {
            throw new NotSupportedException($"No formatter registered for export format '{request.Format}'.");
        }

        var content = formatter.FormatTranscript(request.Session, request.Segments);

        var outputPath = request.OutputPath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoding = formatter is TranscriptFormatterBase { WriteUtf8Bom: true } ? Utf8WithBom : Utf8NoBom;
        await File.WriteAllTextAsync(outputPath, content, encoding, cancellationToken).ConfigureAwait(false);
    }
}
