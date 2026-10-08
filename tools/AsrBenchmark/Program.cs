using System.Diagnostics;
using System.Globalization;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using NAudio.Wave;

// AsrBenchmark: decodes a local WAV with a real sherpa-onnx model and reports timing / RTF / memory.
//
// Usage:
//   AsrBenchmark --wav <file.wav> [--model-id <id>] [--model-dir <path>] [--models-root <path>]
//                [--threads N] [--hotwords <file>] [--hotwords-score X] [--offline] [--chunk-ms N]

var opts = CliArgs.Parse(args);
if (opts.ContainsKey("help") || !opts.ContainsKey("wav"))
{
    Console.WriteLine("Usage: AsrBenchmark --wav <file.wav> [--model-id <id>] [--model-dir <path>] [--models-root <path>] [--threads N] [--hotwords <file>] [--hotwords-score X] [--offline] [--chunk-ms N]");
    return opts.ContainsKey("help") ? 0 : 2;
}

string wavPath = opts["wav"];
int threads = opts.TryGetValue("threads", out var t) && int.TryParse(t, out var tn) ? tn : 0;
int chunkMs = opts.TryGetValue("chunk-ms", out var c) && int.TryParse(c, out var cn) ? cn : 100;
float hotwordsScore = opts.TryGetValue("hotwords-score", out var hs) && float.TryParse(hs, NumberStyles.Float, CultureInfo.InvariantCulture, out var hsf) ? hsf : 1.5f;
string? hotwords = opts.GetValueOrDefault("hotwords");
bool offline = opts.ContainsKey("offline");

AsrEngineOptions options;
if (opts.TryGetValue("model-dir", out var explicitDir))
{
    options = new AsrEngineOptions
    {
        ModelDirectory = explicitDir,
        Kind = offline ? AsrModelKind.Offline : AsrModelKind.StreamingTransducer,
        EncoderFileName = "encoder-epoch-99-avg-1.int8.onnx",
        DecoderFileName = "decoder-epoch-99-avg-1.int8.onnx",
        JoinerFileName = "joiner-epoch-99-avg-1.int8.onnx",
        TokensFileName = "tokens.txt",
        ModelFileName = "model.int8.onnx",
        NumThreads = threads,
        HotwordsFile = hotwords,
        HotwordsScore = hotwordsScore
    };
    Console.WriteLine("MODEL_ID=custom");
}
else
{
    string modelsRoot = opts.GetValueOrDefault("models-root") ?? FindModelsRoot();
    string modelId = opts.GetValueOrDefault("model-id") ?? AsrModelCatalog.StreamingZipformerZh14M.Id;
    var desc = AsrModelCatalog.All.FirstOrDefault(m => m.Id == modelId);
    if (desc == null)
    {
        Console.Error.WriteLine($"Unknown model id '{modelId}'. Known: {string.Join(", ", AsrModelCatalog.All.Select(m => m.Id))}");
        return 2;
    }
    options = AsrOptionsFactory.FromDescriptor(desc, modelsRoot, hotwords, threads, hotwordsScore);
    Console.WriteLine($"MODEL_ID={desc.Id}");
}

// Read WAV (any format) -> float32 at source rate -> 16 kHz mono via the shared preprocessor.
using var reader = new AudioFileReader(wavPath);
int srcRate = reader.WaveFormat.SampleRate;
int channels = reader.WaveFormat.Channels;
var pre = new DefaultAudioPreprocessor(16000);

var sourceSamples = new List<float>();
var readBuffer = new float[srcRate * channels];
int read;
while ((read = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
{
    sourceSamples.AddRange(readBuffer.AsSpan(0, read).ToArray());
}

float[] mono16k = pre.Process(sourceSamples.ToArray(), AudioFormat.Float32(srcRate, channels));
double audioSeconds = mono16k.Length / 16000.0;

Console.WriteLine($"WAV={wavPath}");
Console.WriteLine($"SOURCE={srcRate}Hz {channels}ch");
Console.WriteLine($"AUDIO_SECONDS={audioSeconds:F3}");
int effectiveThreads = options.NumThreads > 0 ? options.NumThreads : Math.Max(1, Environment.ProcessorCount / 2);
Console.WriteLine($"THREADS={effectiveThreads}");
Console.WriteLine($"HOTWORDS={(string.IsNullOrEmpty(options.HotwordsFile) ? "none" : options.HotwordsFile)}");
Console.WriteLine($"MODE={(offline ? "offline" : "streaming")}");

using var engine = new SherpaOnnxAsrEngine();
var loadSw = Stopwatch.StartNew();
var init = await engine.InitializeAsync(options);
loadSw.Stop();
Console.WriteLine($"LOAD_STATUS={init.Status}");
Console.WriteLine($"LOAD_MS={loadSw.ElapsedMilliseconds}");
Console.WriteLine($"CAPABILITIES={init.Capabilities?.Description}");
if (!init.Ok)
{
    Console.Error.WriteLine($"ERROR: {init.Message}");
    return 1;
}

var session = engine.CreateSession();
int chunkSamples = Math.Max(160, 16000 * chunkMs / 1000);
var inferSw = new Stopwatch();
var text = new System.Text.StringBuilder();
int endpoints = 0;
string lastPartial = "";

for (int offset = 0; offset < mono16k.Length; offset += chunkSamples)
{
    int n = Math.Min(chunkSamples, mono16k.Length - offset);
    inferSw.Start();
    session.AcceptWaveform(mono16k.AsSpan(offset, n), 16000);
    while (session.IsReady()) session.Decode();
    var result = session.GetResult();
    inferSw.Stop();

    if (!string.IsNullOrEmpty(result.Text))
    {
        lastPartial = result.Text;
        if (result.IsEndpoint)
        {
            text.Append(result.Text).Append(' ');
            endpoints++;
            session.Reset();
            lastPartial = "";
        }
    }
    else if (result.IsEndpoint)
    {
        if (lastPartial.Length > 0) { text.Append(lastPartial).Append(' '); endpoints++; }
        session.Reset();
        lastPartial = "";
    }
}
if (lastPartial.Length > 0) text.Append(lastPartial);

double inferenceSeconds = inferSw.Elapsed.TotalSeconds;
double rtf = audioSeconds > 0 ? inferenceSeconds / audioSeconds : 0;
long workingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

Console.WriteLine($"INFER_SECONDS={inferenceSeconds:F3}");
Console.WriteLine($"RTF={rtf:F4}");
Console.WriteLine($"ENDPOINTS={endpoints}");
Console.WriteLine($"WORKING_SET_MB={workingSetMb}");
Console.WriteLine($"TEXT={text.ToString().Trim()}");
return 0;

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
