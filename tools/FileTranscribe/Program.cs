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
// evidence for V0.5 Phases 2-4.
//
// Usage:
//   FileTranscribe --file <path> [--mode fast|standard|high] [--models-root <path>] [--track N]
//                  [--hotwords <file>] [--lexicon] [--diarize] [--out <path>] [--json]
//   FileTranscribe --jobs [--file <path>] [--db <path>] [--interrupt-after N] [--resume-job <id>]
//                  [--list-jobs] [--models-root <path>] [--diarize] [--json]

var opts = CliArgs.Parse(args);
if (opts.ContainsKey("help") || (!opts.ContainsKey("file") && !opts.ContainsKey("jobs")))
{
    Console.WriteLine("Usage: FileTranscribe --file <path> [--mode fast|standard|high] [--models-root <path>] "
        + "[--track N] [--hotwords <file>] [--lexicon] [--diarize] [--out <path>] [--json]");
    Console.WriteLine("       FileTranscribe --jobs [--file <path>] [--db <path>] [--interrupt-after N] "
        + "[--resume-job <id>] [--list-jobs]");
    return opts.ContainsKey("help") ? 0 : 2;
}

string? file = opts.GetValueOrDefault("file");
if (file is not null && !File.Exists(file))
{
    Console.Error.WriteLine($"ERROR: file not found: {file}");
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

// ---- Phase 4: job queue + checkpoint/resume --------------------------------
if (opts.ContainsKey("jobs"))
{
    return await RunJobQueueAsync();
}

if (!TryParseMode(opts.GetValueOrDefault("mode") ?? "fast", out var mode))
{
    Console.Error.WriteLine("ERROR: --mode must be one of fast|standard|high.");
    return 2;
}

string input = file!;

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
    info = await media.ProbeAsync(input);
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

    string dbPath = opts.GetValueOrDefault("db") ?? TempDbPath("ft");
    bool deleteDb = !opts.ContainsKey("db");
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
                input,
                engine,
                resolved.TranscriptionOptions,
                track,
                RunDiarization: true,
                Title: Path.GetFileName(input)),
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
        if (deleteDb)
        {
            TryDelete(dbPath);
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");
        }
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
            media.DecodeAsync(new MediaDecodeRequest(input, track), cts.Token),
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
        file = input,
        diarized,
        audioSeconds = Math.Round(audioDuration.TotalSeconds, 3),
        elapsedSeconds = Math.Round(elapsed.TotalSeconds, 3),
        rtf = audioDuration.TotalSeconds > 0 ? Math.Round(elapsed.TotalSeconds / audioDuration.TotalSeconds, 4) : 0,
        completed,
        cancelled,
        segments = segments.Select(ProjectSegment),
        participants = dialogue?.Participants.Select(p => new
        {
            p.SpeakerId,
            p.Name,
            speakingSeconds = Math.Round(p.SpeakingTime.TotalSeconds, 3),
            p.TurnCount,
            p.SegmentCount
        }),
        turns = dialogue?.Turns.Select(ProjectTurn)
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

// ---- Phase 4: queue the file, run it, and optionally interrupt / resume it -------------------
async Task<int> RunJobQueueAsync()
{
    bool listOnly = opts.ContainsKey("list-jobs");
    string? resumeJob = opts.GetValueOrDefault("resume-job");
    if (!listOnly && resumeJob is null && file is null)
    {
        Console.Error.WriteLine("ERROR: --jobs needs --file <path>, --resume-job <id> or --list-jobs.");
        return 2;
    }

    var diarizationOptions = ResolveDiarizationOptions(modelsRoot);
    if (diarizationOptions is null)
    {
        Console.Error.WriteLine("ERROR: diarization models are not present under models/.");
        return 3;
    }

    int interruptAfter = opts.TryGetValue("interrupt-after", out var ia)
        && int.TryParse(ia, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : 0;

    using var jobCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; jobCts.Cancel(); };

    string dbPath = opts.GetValueOrDefault("db") ?? TempDbPath("ftjobs");
    bool deleteDb = !opts.ContainsKey("db");
    var database = new SqliteDatabase(dbPath);
    Console.WriteLine($"DB={Path.GetFullPath(dbPath)}");

    // Declared outside the try so the resolver local function below can capture them.
    var subtitles = new SqliteSubtitleRepository(database);
    var speakers = new SqliteSpeakerRepository(database);
    var mediaFiles = new SqliteMediaFileRepository(database);
    var jobs = new SqliteTranscriptionJobRepository(database);

    try
    {
        await database.InitializeAsync();
        var diarization = new SpeakerDiarizationService(
            () => new SherpaOfflineSpeakerDiarizer(log),
            new NaudioAudioFileLoader(),
            new SpeakerAlignmentService(),
            speakers,
            subtitles,
            diarizationOptions,
            audioAssets: null,
            log);
        var jobMedia = new FFmpegMediaDecodeService(FFmpegLocator.Resolve(), log);
        var fileService = new FileTranscriptionService(
            media: jobMedia,
            diarization: diarization,
            subtitles: subtitles,
            speakers: speakers,
            alignment: new TranscriptAlignmentService(),
            stagingDirectory: Path.GetTempPath(),
            log: log);

        await using var jobService = new TranscriptionJobService(fileService, jobs, mediaFiles, subtitles, ResolveJobRequestAsync, log);

        int recovered = await jobService.RecoverUnfinishedAsync();
        if (recovered > 0)
        {
            Console.WriteLine($"RECOVERED={recovered}");
        }

        if (listOnly)
        {
            PrintJobs(await jobService.GetJobsAsync());
            return 0;
        }

        if (resumeJob is not null)
        {
            if (!await jobService.ResumeAsync(resumeJob))
            {
                Console.Error.WriteLine($"ERROR: job {resumeJob} cannot be resumed.");
                return 2;
            }

            Console.WriteLine($"RESUMED={resumeJob}");
        }
        else
        {
            if (!TryParseMode(opts.GetValueOrDefault("mode") ?? "fast", out var jobMode))
            {
                Console.Error.WriteLine("ERROR: --mode must be one of fast|standard|high.");
                return 2;
            }

            var modeChoice = TranscriptionModeCatalog.Resolve(jobMode, modelManager, hotwords);
            if (!modeChoice.IsAvailable)
            {
                Console.Error.WriteLine($"ERROR: {modeChoice.UnavailableReason}");
                return 3;
            }

            var probe = await jobMedia.ProbeAsync(file!);
            var queued = await jobService.EnqueueAsync(
                new TranscriptionJobRequest(
                    file!,
                    jobMode,
                    modeChoice.Descriptor.Id,
                    track,
                    RunDiarization: diarize,
                    Title: Path.GetFileName(file!)),
                probe);
            Console.WriteLine($"JOB={queued.JobId} STATUS={queued.Status}");
        }

        if (interruptAfter > 0)
        {
            int remaining = interruptAfter;
            jobService.JobChanged += (_, job) =>
            {
                if (remaining > 0 && job.SegmentsEmitted >= remaining && !job.IsFinished)
                {
                    remaining = -1;
                    Console.Error.WriteLine($"INTENTIONAL-INTERRUPT after {job.SegmentsEmitted} persisted segment(s).");
                    _ = jobService.CancelAsync(job.JobId);
                }
            };
        }

        int processed = await jobService.DrainAsync(jobCts.Token);
        Console.WriteLine($"DRAINED={processed}");
        PrintJobs(await jobService.GetJobsAsync());

        // Report the transcript of the newest session so the resume can be eyeballed.
        foreach (var job in (await jobService.GetJobsAsync()).Where(j => j.SessionId is not null))
        {
            var stored = await subtitles.GetSegmentsAsync(job.SessionId!);
            if (stored.Count == 0)
            {
                continue;
            }

            Console.WriteLine($"SESSION={job.SessionId} SEGMENTS={stored.Count}");
            foreach (var segment in stored.OrderBy(s => s.SequenceNumber))
            {
                Console.WriteLine($"  #{segment.SequenceNumber} "
                    + $"[{segment.StartOffset:hh\\:mm\\:ss\\.fff} - {segment.EndOffset:hh\\:mm\\:ss\\.fff}] {segment.DisplayText}");
            }
        }

        return 0;
    }
    finally
    {
        await database.DisposeAsync();
        if (deleteDb)
        {
            TryDelete(dbPath);
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");
        }
    }

    async Task<FileTranscriptionRequest> ResolveJobRequestAsync(TranscriptionJob job, CancellationToken token)
    {
        var record = await mediaFiles.GetAsync(job.MediaFileId, token)
            ?? throw new InvalidOperationException($"Media file {job.MediaFileId} is missing.");

        var modeChoice = TranscriptionModeCatalog.Resolve(job.Mode, modelManager, hotwords);
        if (!modeChoice.IsAvailable)
        {
            throw new InvalidOperationException(modeChoice.UnavailableReason ?? "The model is not installed.");
        }

        var jobEngine = new SherpaOnnxAsrEngine(log);
        var jobInit = await jobEngine.InitializeAsync(modeChoice.EngineOptions, token);
        if (!jobInit.Ok)
        {
            jobEngine.Dispose();
            throw new InvalidOperationException(jobInit.Message);
        }

        return new FileTranscriptionRequest(
            record.Path,
            jobEngine,
            modeChoice.TranscriptionOptions,
            job.AudioStreamIndex,
            job.RunDiarization,
            job.DiarizationCountMode,
            job.ManualSpeakerCount,
            job.ClusteringThreshold,
            Title: job.Title,
            SessionId: job.SessionId);
    }
}

static void PrintJobs(IReadOnlyList<TranscriptionJob> jobs)
{
    foreach (var job in jobs)
    {
        Console.WriteLine($"JOB {job.JobId} {job.Status} segments={job.SegmentsEmitted} "
            + $"processed={job.Processed.TotalSeconds:F1}/{job.Total.TotalSeconds:F1}s "
            + $"attempts={job.Attempts} resumes={job.ResumeCount} session={job.SessionId} error={job.Error}");
    }
}

static object ProjectSegment(OfflineTranscriptSegment segment) => new
{
    start = segment.Start.ToString(@"hh\:mm\:ss\.fff"),
    end = segment.End.ToString(@"hh\:mm\:ss\.fff"),
    startMs = (long)segment.Start.TotalMilliseconds,
    endMs = (long)segment.End.TotalMilliseconds,
    chunkId = segment.SourceChunkId,
    modelId = segment.ModelId,
    text = segment.Text
};

static object ProjectTurn(DialogueTurn turn) => new
{
    speakerId = turn.SpeakerId,
    speaker = turn.SpeakerName,
    start = turn.Start.ToString(@"hh\:mm\:ss\.fff"),
    end = turn.End.ToString(@"hh\:mm\:ss\.fff"),
    startMs = (long)turn.Start.TotalMilliseconds,
    endMs = (long)turn.End.TotalMilliseconds,
    turn.NeedsConfirmation,
    segmentIds = turn.SegmentIds,
    text = turn.Text
};

static string TempDbPath(string prefix) =>
    Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N") + ".db");

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
