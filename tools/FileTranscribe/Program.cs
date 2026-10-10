using System.Globalization;
using System.Text.Json;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Media;
using LocalMeetingSubtitle.ModelDownloads;

// FileTranscribe: decodes a local audio/video file with the bundled FFmpeg toolchain and transcribes it
// offline with a real sherpa-onnx model, printing the segment timeline. Used as evidence for V0.5 Phase 2.
//
// Usage:
//   FileTranscribe --file <path> [--mode fast|standard|high] [--models-root <path>] [--track N]
//                  [--hotwords <file>] [--lexicon] [--json]

var opts = CliArgs.Parse(args);
if (opts.ContainsKey("help") || !opts.ContainsKey("file"))
{
    Console.WriteLine("Usage: FileTranscribe --file <path> [--mode fast|standard|high] [--models-root <path>] "
        + "[--track N] [--hotwords <file>] [--lexicon] [--json]");
    return opts.ContainsKey("help") ? 0 : 2;
}

string file = opts["file"];
if (!File.Exists(file))
{
    Console.Error.WriteLine($"ERROR: file not found: {file}");
    return 2;
}

if (!TryParseMode(opts.GetValueOrDefault("mode") ?? "fast", out var mode))
{
    Console.Error.WriteLine("ERROR: --mode must be one of fast|standard|high.");
    return 2;
}

string modelsRoot = opts.GetValueOrDefault("models-root") ?? FindModelsRoot();
int? track = opts.TryGetValue("track", out var tr) && int.TryParse(tr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackIndex)
    ? trackIndex
    : null;
string? hotwords = opts.GetValueOrDefault("hotwords");
bool useLexicon = opts.ContainsKey("lexicon");
bool json = opts.ContainsKey("json");

var log = new ConsoleLogger();

var modelManager = new HttpModelManager(modelsRoot);
var resolved = TranscriptionModeCatalog.Resolve(mode, modelManager, hotwords);
Console.WriteLine($"MODE={mode} DISPLAY={resolved.DisplayName} MODEL={resolved.Descriptor.Id} "
    + $"INSTALLED={resolved.IsAvailable}");
if (!resolved.IsAvailable)
{
    Console.Error.WriteLine($"ERROR: {resolved.UnavailableReason}");
    return 3;
}

using var engine = new SherpaOnnxAsrEngine(log);
var init = await engine.InitializeAsync(resolved.EngineOptions);
Console.WriteLine($"LOAD_STATUS={init.Status}");
Console.WriteLine($"CAPABILITIES={init.Capabilities?.Description}");
if (!init.Ok)
{
    Console.Error.WriteLine($"ERROR: {init.Message}");
    return 1;
}

var tools = FFmpegLocator.Resolve();
var media = new FFmpegMediaDecodeService(tools, log);

MediaInfo? info = null;
try
{
    info = await media.ProbeAsync(file);
}
catch (MediaDecodeException ex)
{
    Console.Error.WriteLine($"ERROR: probe failed ({ex.Kind}): {ex.Message}");
    return 1;
}

Console.WriteLine($"FILE={info.Path}");
Console.WriteLine($"KIND={info.Kind} CONTAINER={info.ContainerFormat} DURATION={info.Duration.TotalSeconds:F3}s "
    + $"AUDIO_STREAMS={info.AudioStreams.Count}");

var correctionEngine = new TextCorrectionEngine();
Func<string, string>? correct = useLexicon
    ? text => correctionEngine.Apply(text, BuiltInLexicon.Rules).Text
    : null;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var transcriber = new OfflineTranscriptionEngine(engine, resolved.TranscriptionOptions, correct, log);
var progress = new Progress<OfflineTranscriptionProgress>(p =>
{
    if (!json && p.Total > TimeSpan.Zero)
    {
        Console.Error.Write($"\rprogress {p.Processed.TotalSeconds:F1}/{p.Total.TotalSeconds:F1}s "
            + $"({p.SegmentsEmitted} segments)      ");
    }
});

OfflineTranscriptionResult result;
try
{
    result = await transcriber.TranscribeAsync(
        media.DecodeAsync(new MediaDecodeRequest(file, track), cts.Token),
        progress,
        info.Duration,
        cts.Token);
}
catch (MediaDecodeException ex)
{
    Console.Error.WriteLine($"\nERROR: decode failed ({ex.Kind}): {ex.Message}");
    return 1;
}

if (!json)
{
    Console.Error.WriteLine();
}

if (json)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode = mode.ToString(),
        modelId = resolved.Descriptor.Id,
        file,
        audioSeconds = Math.Round(result.AudioDuration.TotalSeconds, 3),
        elapsedSeconds = Math.Round(result.Elapsed.TotalSeconds, 3),
        rtf = Math.Round(result.Rtf, 4),
        completed = result.Completed,
        cancelled = result.Cancelled,
        segments = result.Segments.Select(s => new
        {
            start = s.Start.ToString(@"hh\:mm\:ss\.fff"),
            end = s.End.ToString(@"hh\:mm\:ss\.fff"),
            startMs = (long)s.Start.TotalMilliseconds,
            endMs = (long)s.End.TotalMilliseconds,
            chunkId = s.SourceChunkId,
            modelId = s.ModelId,
            text = s.Text
        })
    }, new JsonSerializerOptions { WriteIndented = true }));
}
else
{
    foreach (var segment in result.Segments)
    {
        Console.WriteLine($"[{segment.Start:hh\\:mm\\:ss\\.fff} - {segment.End:hh\\:mm\\:ss\\.fff}] "
            + $"(#{segment.SourceChunkId} {segment.ModelId}) {segment.Text}");
    }

    Console.WriteLine($"SEGMENTS={result.Segments.Count}");
    Console.WriteLine($"AUDIO_SECONDS={result.AudioDuration.TotalSeconds:F3}");
    Console.WriteLine($"ELAPSED_SECONDS={result.Elapsed.TotalSeconds:F3}");
    Console.WriteLine($"RTF={result.Rtf:F4}");
    Console.WriteLine($"COMPLETED={result.Completed} CANCELLED={result.Cancelled}");
}

return 0;

static bool TryParseMode(string value, out TranscriptionMode mode)
{
    switch (value.Trim().ToLowerInvariant())
    {
        case "fast":
        case "f":
            mode = TranscriptionMode.Fast;
            return true;
        case "standard":
        case "std":
        case "s":
            mode = TranscriptionMode.Standard;
            return true;
        case "high":
        case "highaccuracy":
        case "h":
            mode = TranscriptionMode.HighAccuracy;
            return true;
        default:
            mode = TranscriptionMode.Fast;
            return false;
    }
}

static string FindModelsRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "models");
        if (Directory.Exists(candidate)) return candidate;
        if (dir.GetFiles("*.sln").Length > 0) return Path.Combine(dir.FullName, "models");
        dir = dir.Parent;
    }
    return Path.Combine(Directory.GetCurrentDirectory(), "models");
}

internal sealed class ConsoleLogger : IAppLogger
{
    public void Debug(string message) { }
    public void Info(string message) => Console.Error.WriteLine($"INFO  {message}");
    public void Warn(string message) => Console.Error.WriteLine($"WARN  {message}");
    public void Error(string message, Exception? exception = null) =>
        Console.Error.WriteLine($"ERROR {message}{(exception == null ? "" : $" :: {exception.Message}")}");
}

internal static class CliArgs
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                map[key] = args[++i];
            }
            else
            {
                map[key] = "true";
            }
        }
        return map;
    }
}
