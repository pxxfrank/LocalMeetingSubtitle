using System.Globalization;
using System.Text;
using System.Text.Json;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Audio;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Media;
using LocalMeetingSubtitle.ModelDownloads;
using LocalMeetingSubtitle.Storage;

// FileTranscribe: decodes a local audio/video file with the bundled FFmpeg toolchain and transcribes it
// offline with a real sherpa-onnx model, optionally offlining it into role-tagged dialogue. Used as
// evidence for V0.5 Phases 2-3.
//
// Usage:
//   FileTranscribe --file <path> [--mode fast|standard|high] [--models-root <path>] [--track N]
//                  [--hotwords <file>] [--lexicon] [--diarize] [--out <path>] [--json]

var opts = CliArgs.Parse(args);
if (opts.ContainsKey("help") || !opts.ContainsKey("file"))
{
    Console.WriteLine("Usage: FileTranscribe --file <path> [--mode fast|standard|high] [--models-root <path>] "
        + "[--track N] [--hotwords <file>] [--lexicon] [--diarize] [--out <path>] [--json]");
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
bool diarize = opts.ContainsKey("diarize");
string? outputPath = opts.GetValueOrDefault("out");
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

MediaInfo info;
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

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

IReadOnlyList<OfflineTranscriptSegment> segments;
DialogueTranscript? dialogue = null;
TimeSpan audioDuration;
TimeSpan elapsed;
bool completed;
bool cancelled;
bool diarized = false;
string? warning = null;

if (diarize)
{
    var diarizationOptions = ResolveDiarizationOptions(modelsRoot);
    if (diarizationOptions is null)
    {
        Console.Error.WriteLine("ERROR: diarization models are not present under models/ "
            + "(sherpa-onnx-pyannote-segmentation-3-0/model.onnx and 3dspeaker-eres2net-base-zh-16k/…).");
        return 3;
    }

    string dbPath = Path.Combine(Path.GetTempPath(), "ft-" + Guid.NewGuid().ToString("N") + ".db");
    var database = new SqliteDatabase(dbPath);

    try
    {
        await database.InitializeAsync();

        var subtitles = new SqliteSubtitleRepository(database);
        var speakers = new SqliteSpeakerRepository(database);
        var diarization = new SpeakerDiarizationService(
            () => new SherpaOfflineSpeakerDiarizer(log),
            new NaudioAudioFileLoader(),
            new SpeakerAlignmentService(),
            speakers,
            subtitles,
            diarizationOptions,
            audioAssets: null,
            log);

        var service = new FileTranscriptionService(
            media,
            diarization,
            subtitles,
            speakers,
            new TranscriptAlignmentService(),
            Path.GetTempPath(),
            log);

        var progress = new SyncProgress<FileTranscriptionProgress>(p =>
        {
            if (!json && p.Total > TimeSpan.Zero)
            {
                Console.Error.Write($"\r{p.Phase,-10} {p.Processed.TotalSeconds:F1}/{p.Total.TotalSeconds:F1}s "
                    + $"({p.SegmentsEmitted} segments)      ");
            }
        });

        var result = await service.RunAsync(
            new FileTranscriptionRequest(
                file,
                engine,
                resolved.TranscriptionOptions,
                track,
                RunDiarization: true,
                Title: Path.GetFileName(file)),
            progress,
            cts.Token);

        if (!json)
        {
            Console.Error.WriteLine();
        }

        if (result.Error is not null)
        {
            Console.Error.WriteLine($"ERROR: {result.Error}");
            return 1;
        }

        segments = result.Segments;
        dialogue = result.Dialogue;
        audioDuration = result.AudioDuration;
        elapsed = result.Elapsed;
        completed = result.Completed;
        cancelled = result.Cancelled;
        diarized = result.Diarized;
        warning = result.Warning;
    }
    finally
    {
        await database.DisposeAsync();
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }
}
else
{
    var correctionEngine = new TextCorrectionEngine();
    Func<string, string>? correct = useLexicon
        ? text => correctionEngine.Apply(text, BuiltInLexicon.Rules).Text
        : null;

    var transcriber = new OfflineTranscriptionEngine(engine, resolved.TranscriptionOptions, correct, log);
    var progress = new SyncProgress<OfflineTranscriptionProgress>(p =>
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

    segments = result.Segments;
    audioDuration = result.AudioDuration;
    elapsed = result.Elapsed;
    completed = result.Completed;
    cancelled = result.Cancelled;
    warning = result.Error;
}

if (warning is not null)
{
    Console.Error.WriteLine($"WARN: {warning}");
}

if (json)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode = mode.ToString(),
        modelId = resolved.Descriptor.Id,
        file,
        diarized,
        audioSeconds = Math.Round(audioDuration.TotalSeconds, 3),
        elapsedSeconds = Math.Round(elapsed.TotalSeconds, 3),
        rtf = audioDuration.TotalSeconds > 0 ? Math.Round(elapsed.TotalSeconds / audioDuration.TotalSeconds, 4) : 0,
        completed,
        cancelled,
        segments = segments.Select(s => new
        {
            start = s.Start.ToString(@"hh\:mm\:ss\.fff"),
            end = s.End.ToString(@"hh\:mm\:ss\.fff"),
            startMs = (long)s.Start.TotalMilliseconds,
            endMs = (long)s.End.TotalMilliseconds,
            chunkId = s.SourceChunkId,
            modelId = s.ModelId,
            text = s.Text
        }),
        participants = dialogue?.Participants.Select(p => new
        {
            p.SpeakerId,
            p.Name,
            speakingSeconds = Math.Round(p.SpeakingTime.TotalSeconds, 3),
            p.TurnCount,
            p.SegmentCount
        }),
        turns = dialogue?.Turns.Select(t => new
        {
            speakerId = t.SpeakerId,
            speaker = t.SpeakerName,
            start = t.Start.ToString(@"hh\:mm\:ss\.fff"),
            end = t.End.ToString(@"hh\:mm\:ss\.fff"),
            startMs = (long)t.Start.TotalMilliseconds,
            endMs = (long)t.End.TotalMilliseconds,
            t.NeedsConfirmation,
            segmentIds = t.SegmentIds,
            text = t.Text
        })
    }, new JsonSerializerOptions { WriteIndented = true }));
}
else
{
    foreach (var segment in segments)
    {
        Console.WriteLine($"[{segment.Start:hh\\:mm\\:ss\\.fff} - {segment.End:hh\\:mm\\:ss\\.fff}] "
            + $"(#{segment.SourceChunkId} {segment.ModelId}) {segment.Text}");
    }

    if (dialogue is not null)
    {
        Console.WriteLine();
        foreach (var turn in dialogue.Turns)
        {
            Console.WriteLine($"[{turn.Start:hh\\:mm\\:ss\\.fff} - {turn.End:hh\\:mm\\:ss\\.fff}] "
                + $"{turn.SpeakerName}: {turn.Text}");
        }

        Console.WriteLine();
        foreach (var participant in dialogue.Participants)
        {
            Console.WriteLine($"PARTICIPANT {participant.Name} ({participant.SpeakerId}) "
                + $"speaking={participant.SpeakingTime.TotalSeconds:F1}s turns={participant.TurnCount} "
                + $"segments={participant.SegmentCount}");
        }

        Console.WriteLine($"PARTICIPANTS={dialogue.Participants.Count} TURNS={dialogue.Turns.Count}");
    }

    Console.WriteLine($"SEGMENTS={segments.Count}");
    Console.WriteLine($"AUDIO_SECONDS={audioDuration.TotalSeconds:F3}");
    Console.WriteLine($"ELAPSED_SECONDS={elapsed.TotalSeconds:F3}");
    Console.WriteLine($"RTF={(audioDuration.TotalSeconds > 0 ? elapsed.TotalSeconds / audioDuration.TotalSeconds : 0):F4}");
    Console.WriteLine($"DIARIZED={diarized} COMPLETED={completed} CANCELLED={cancelled}");
}

if (outputPath is not null)
{
    var text = dialogue is not null
        ? string.Join(Environment.NewLine, dialogue.Turns.Select(t =>
            $"[{t.Start:hh\\:mm\\:ss\\.fff} - {t.End:hh\\:mm\\:ss\\.fff}] {t.SpeakerName}: {t.Text}"))
        : string.Join(Environment.NewLine, segments.Select(s =>
            $"[{s.Start:hh\\:mm\\:ss\\.fff} - {s.End:hh\\:mm\\:ss\\.fff}] {s.Text}"));

    File.WriteAllText(outputPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    Console.WriteLine($"OUT={Path.GetFullPath(outputPath)}");
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

static DiarizationEngineOptions? ResolveDiarizationOptions(string modelsRoot)
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

static void TryDelete(string path)
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
    catch (UnauthorizedAccessException)
    {
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

/// <summary>Progress that reports synchronously so ordering is preserved.</summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public SyncProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
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
