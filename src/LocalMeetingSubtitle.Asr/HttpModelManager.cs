using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// Downloads model files from their declared sources. Used only at development/installation time
/// (the ModelManager tool and first-run setup). Recognition itself never touches the network.
/// </summary>
public sealed class HttpModelManager : IModelManager
{
    private readonly HttpClient _http;

    public HttpModelManager(string modelsRoot, IEnumerable<ModelDescriptor>? catalog = null, HttpClient? http = null)
    {
        ModelsRoot = modelsRoot;
        Catalog = (catalog ?? AsrModelCatalog.All).ToList();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalMeetingSubtitle/0.1");
        }
    }

    public string ModelsRoot { get; }
    public IReadOnlyList<ModelDescriptor> Catalog { get; }

    public ModelDescriptor? FindById(string id) => Catalog.FirstOrDefault(m => m.Id == id);

    public string GetModelDirectory(ModelDescriptor descriptor) => Path.Combine(ModelsRoot, descriptor.DirectoryName);

    public bool IsInstalled(ModelDescriptor descriptor) => MissingFiles(descriptor).Count == 0;

    public IReadOnlyList<ModelFileSpec> MissingFiles(ModelDescriptor descriptor)
    {
        var dir = GetModelDirectory(descriptor);
        return descriptor.Files
            .Where(f => f.Required)
            .Where(f =>
            {
                var path = Path.Combine(dir, f.RelativePath);
                if (!File.Exists(path)) return true;
                if (f.SizeBytes is { } size && new FileInfo(path).Length != size) return true;
                return false;
            })
            .ToList();
    }

    public async Task<ModelInstallResult> EnsureInstalledAsync(ModelDescriptor descriptor, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var dir = GetModelDirectory(descriptor);
        Directory.CreateDirectory(dir);

        var required = descriptor.Files.Where(f => f.Required).ToList();
        long totalBytes = required.Sum(f => f.SizeBytes ?? 0);
        long doneBytes = 0;

        foreach (var file in required)
        {
            var dest = Path.Combine(dir, file.RelativePath);
            if (File.Exists(dest) && (file.SizeBytes is not { } s || new FileInfo(dest).Length == s))
            {
                doneBytes += file.SizeBytes ?? new FileInfo(dest).Length;
                continue;
            }

            if (string.IsNullOrEmpty(file.SourceUrl))
            {
                return new ModelInstallResult(false, $"No source URL declared for {file.RelativePath}.");
            }

            var dirOfFile = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dirOfFile)) Directory.CreateDirectory(dirOfFile);

            var tmp = dest + ".download";
            try
            {
                using var response = await _http.GetAsync(file.SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                long fileTotal = response.Content.Headers.ContentLength ?? file.SizeBytes ?? 0;

                await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var dst = File.Create(tmp))
                {
                    var buffer = new byte[1 << 20];
                    long fileRead = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        fileRead += read;
                        if (totalBytes > 0)
                        {
                            progress?.Report((doneBytes + fileRead) / (double)totalBytes);
                        }
                    }
                }

                if (file.SizeBytes is { } expected && new FileInfo(tmp).Length != expected)
                {
                    File.Delete(tmp);
                    return new ModelInstallResult(false, $"Size mismatch for {file.RelativePath}.");
                }

                File.Move(tmp, dest, overwrite: true);
                doneBytes += fileTotal;
                progress?.Report(totalBytes > 0 ? doneBytes / (double)totalBytes : 1.0);
            }
            catch (Exception ex)
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                return new ModelInstallResult(false, $"Failed to download {file.RelativePath}: {ex.Message}");
            }
        }

        progress?.Report(1.0);
        return new ModelInstallResult(true, $"Model \"{descriptor.Id}\" is installed at {dir}.");
    }
}
