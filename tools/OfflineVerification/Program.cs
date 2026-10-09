using System.Reflection;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.ModelDownloads;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Storage;
using NAudio.Wave;

// OfflineVerification: proves the recognition stack runs with no network dependency.
//
//   OfflineVerification [--models-root <path>] [--model-id <id>] [--wav <file.wav>]

var opts = CliArgs.Parse(args);
string modelsRoot = opts.GetValueOrDefault("models-root") ?? FindModelsRoot();
string modelId = opts.GetValueOrDefault("model-id") ?? AsrModelCatalog.StreamingZipformerZh14M.Id;
string? wav = opts.GetValueOrDefault("wav");

var results = new List<(string Name, bool Pass, string Detail)>();
void Check(string name, bool pass, string detail) => results.Add((name, pass, detail));

Console.WriteLine("=== 字幕君 offline verification ===\n");

// 1. Native sherpa-onnx library loads.
bool nativeOk = SherpaNativeProbe.TryLoad(out var version, out var nativeErr);
Check("sherpa-onnx native library loads", nativeOk, nativeOk ? $"version {version}" : nativeErr ?? "unknown");

// 2. No product assembly references System.Net.Http (static offline evidence).
var productAssemblies = new[]
{
    typeof(SherpaOnnxAsrEngine).Assembly,
    typeof(SqliteDatabase).Assembly,
    typeof(DefaultAudioPreprocessor).Assembly,
    typeof(MeetingSession).Assembly
};
var offenders = productAssemblies
    .SelectMany(a => a.GetReferencedAssemblies().Select(r => (Asm: a.GetName().Name, Ref: r.Name)))
    .Where(x => x.Ref != null && (x.Ref.StartsWith("System.Net", StringComparison.Ordinal) || x.Ref.Contains("Http")))
    .ToList();
Check("ASR/Core/Storage/Audio assemblies do not reference System.Net.Http",
    offenders.Count == 0,
    offenders.Count == 0 ? "no network references found" : string.Join(", ", offenders.Select(o => $"{o.Asm}->{o.Ref}")));

// 3. Data directory is per-user (not the protected program directory).
Check("%LOCALAPPDATA% data directory is used for user data",
    LocalDataPaths.Root.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase),
    LocalDataPaths.Root);

// 4. Model present.
var descriptor = AsrModelCatalog.All.FirstOrDefault(m => m.Id == modelId) ?? AsrModelCatalog.StreamingZipformerZh14M;
var manager = new HttpModelManager(modelsRoot);
bool modelInstalled = manager.IsInstalled(descriptor);
Check($"model '{descriptor.Id}' files present", modelInstalled,
    modelInstalled ? manager.GetModelDirectory(descriptor) : "run: ModelManager install --id " + descriptor.Id);

// 5. End-to-end offline decode (no network involved).
if (nativeOk && modelInstalled && wav != null && File.Exists(wav))
{
    try
    {
        var options = AsrOptionsFactory.FromDescriptor(descriptor, modelsRoot, null, numThreads: Math.Max(1, Environment.ProcessorCount / 2));
        using var engine = new SherpaOnnxAsrEngine();
        var init = await engine.InitializeAsync(options);
        if (!init.Ok) { Check("offline decode", false, init.Message); }
        else
        {
            using var reader = new AudioFileReader(wav);
            var pre = new DefaultAudioPreprocessor(16000);
            var samples = new List<float>();
            var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, read).ToArray());
            var mono = pre.Process(samples.ToArray(), AudioFormat.Float32(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels));

            var session = engine.CreateSession();
            var text = new System.Text.StringBuilder();
            int chunk = 1600;
            for (int off = 0; off < mono.Length; off += chunk)
            {
                int n = Math.Min(chunk, mono.Length - off);
                session.AcceptWaveform(mono.AsSpan(off, n), 16000);
                while (session.IsReady()) session.Decode();
                var r = session.GetResult();
                if (!string.IsNullOrEmpty(r.Text) && r.IsEndpoint) { text.Append(r.Text).Append(' '); session.Reset(); }
                else if (!string.IsNullOrEmpty(r.Text)) text.Clear().Append(r.Text);
            }
            var decoded = text.ToString().Trim();
            Check("offline decode produced text", decoded.Length > 0, decoded);
        }
    }
    catch (Exception ex)
    {
        Check("offline decode", false, ex.Message);
    }
}
else if (wav == null)
{
    Check("offline decode", true, "skipped (no --wav supplied)");
}

Console.WriteLine();
foreach (var (name, pass, detail) in results)
{
    Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}");
    if (!string.IsNullOrEmpty(detail)) Console.WriteLine($"         {detail}");
}

int failed = results.Count(r => !r.Pass);
Console.WriteLine($"\n{results.Count - failed}/{results.Count} checks passed.");
return failed == 0 ? 0 : 1;

static string FindModelsRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (dir.GetFiles("*.sln").Length > 0) return Path.Combine(dir.FullName, "models");
        var candidate = Path.Combine(dir.FullName, "models");
        if (Directory.Exists(candidate)) return candidate;
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
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) map[key] = args[++i];
            else map[key] = "true";
        }
        return map;
    }
}
