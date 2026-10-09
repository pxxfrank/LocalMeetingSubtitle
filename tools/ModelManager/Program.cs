using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.ModelDownloads;

// ModelManager: list / install / verify sherpa-onnx models. Development & install-time only.
//
//   ModelManager list    [--catalog asr|diarization|all] [--models-root <path>]
//   ModelManager install --id <id> [--catalog asr|diarization|all] [--models-root <path>]
//   ModelManager verify  [--id <id>] [--catalog asr|diarization|all] [--models-root <path>]

var opts = CliArgs.Parse(args);
string command = opts.GetValueOrDefault("_cmd") ?? "list";
string modelsRoot = opts.GetValueOrDefault("models-root") ?? FindModelsRoot();
string catalogName = (opts.GetValueOrDefault("catalog") ?? "all").ToLowerInvariant();

if (opts.ContainsKey("help"))
{
    Console.WriteLine("Commands: list | install --id <id> | verify --id <id>   [--catalog asr|diarization|all] [--models-root <path>]");
    return 0;
}

IReadOnlyList<ModelDescriptor>? catalog = catalogName switch
{
    "asr" => AsrModelCatalog.All,
    "diarization" => DiarizationModelCatalog.All,
    // The default combines both so every model the app may need is visible to one tool.
    "all" => AsrModelCatalog.All.Concat(DiarizationModelCatalog.All).ToList(),
    _ => null
};

if (catalog is null)
{
    Console.Error.WriteLine($"Unknown catalog '{catalogName}'. Use asr | diarization | all.");
    return 2;
}

var manager = new HttpModelManager(modelsRoot, catalog);
Console.WriteLine($"models-root = {manager.ModelsRoot}");
Console.WriteLine();

switch (command)
{
    case "list":
        foreach (var m in manager.Catalog)
        {
            bool installed = manager.IsInstalled(m);
            Console.WriteLine($"[{(installed ? "x" : " ")}] {m.Id}");
            Console.WriteLine($"     name     : {m.DisplayName}");
            Console.WriteLine($"     kind     : {m.Kind}");
            Console.WriteLine($"     license  : {m.License}");
            Console.WriteLine($"     hotwords : {m.SupportsHotwords}");
            Console.WriteLine($"     approx   : {m.ApproxSizeBytes / 1_000_000.0:F1} MB");
            Console.WriteLine($"     source   : {m.SourceUrl}");
        }
        return 0;

    case "install":
    {
        string id = opts.GetValueOrDefault("id") ?? "";
        var descriptor = manager.FindById(id);
        if (descriptor == null) { Console.Error.WriteLine($"Unknown id '{id}'. Run `list`."); return 2; }

        Console.WriteLine($"Installing {descriptor.Id} ...");
        var progress = new Progress<double>(p => Console.Write($"\r  {p * 100,5:F1}%"));
        var result = await manager.EnsureInstalledAsync(descriptor, progress);
        Console.WriteLine();
        Console.WriteLine($"{(result.Success ? "OK" : "FAILED")}: {result.Message}");
        return result.Success ? 0 : 1;
    }

    case "verify":
    {
        var targets = string.IsNullOrEmpty(opts.GetValueOrDefault("id"))
            ? manager.Catalog
            : manager.Catalog.Where(m => m.Id == opts["id"]).ToList();
        bool allOk = true;
        foreach (var m in targets)
        {
            bool ok = manager.IsInstalled(m);
            allOk &= ok;
            Console.WriteLine($"{m.Id,-45} {(ok ? "INSTALLED" : "MISSING")}");
            if (!ok)
            {
                foreach (var f in manager.MissingFiles(m)) Console.WriteLine($"    missing: {f.RelativePath}");
            }
        }
        return allOk ? 0 : 1;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        return 2;
}

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
        var positionals = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) { positionals.Add(a); continue; }
            var key = a[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) map[key] = args[++i];
            else map[key] = "true";
        }
        if (positionals.Count > 0) map["_cmd"] = positionals[0];
        return map;
    }
}
