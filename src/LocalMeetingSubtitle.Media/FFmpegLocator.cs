using LocalMeetingSubtitle.Core.Abstractions;

namespace LocalMeetingSubtitle.Media;

/// <summary>
/// Resolves the FFmpeg/ffprobe executables the media layer drives. Both tools must be present
/// in the same directory (ffmpeg ships as a set of shared DLLs next to the two CLIs).
///
/// Search order: an explicit directory, the portable layout copied next to the app
/// (<c>&lt;BaseDirectory&gt;\ffmpeg\bin</c>), the development tree
/// (<c>third_party\ffmpeg\bin</c> found by walking up from the app), then <c>PATH</c>.
/// </summary>
public static class FFmpegLocator
{
    private const string FfmpegExeName = "ffmpeg.exe";
    private const string FfprobeExeName = "ffprobe.exe";
    private const string BundledFolderName = "ffmpeg";
    private const string BinFolderName = "bin";

    /// <summary>Finds the toolchain. Throws <see cref="MediaDecodeException"/> (<c>ToolMissing</c>) listing every location tried.</summary>
    public static MediaToolPaths Resolve(string? explicitDirectory = null)
    {
        var searched = new List<string>();

        // (a) An explicitly supplied directory: either the tools' own directory or an FFmpeg root with a bin/ sub-folder.
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            foreach (var directory in CandidateDirectories(explicitDirectory))
            {
                searched.Add(directory);
                if (TryResolve(directory, out var explicitPaths))
                {
                    return explicitPaths;
                }
            }
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory))
        {
            // (b) The portable layout: <BaseDirectory>\ffmpeg\bin (copied next to the app by the build/publish).
            var bundled = Path.Combine(baseDirectory, BundledFolderName, BinFolderName);
            searched.Add(bundled);
            if (TryResolve(bundled, out var bundledPaths))
            {
                return bundledPaths;
            }

            // (c) The development tree: walk up from the app looking for third_party/ffmpeg/bin.
            var current = new DirectoryInfo(baseDirectory);
            while (current is not null)
            {
                var devBin = Path.Combine(current.FullName, "third_party", BundledFolderName, BinFolderName);
                if (!searched.Contains(devBin))
                {
                    searched.Add(devBin);
                    if (TryResolve(devBin, out var devPaths))
                    {
                        return devPaths;
                    }
                }

                current = current.Parent;
            }
        }

        // (d) PATH.
        searched.Add("PATH");
        if (TryResolveFromPath(out var pathPaths))
        {
            return pathPaths;
        }

        throw new MediaDecodeException(MediaErrorKind.ToolMissing,
            "FFmpeg was not found. Provide ffmpeg.exe and ffprobe.exe (they must sit in the same "
            + $"directory). Searched: {string.Join("; ", searched)}");
    }

    private static IEnumerable<string> CandidateDirectories(string directory)
    {
        string full;
        try
        {
            full = Path.GetFullPath(directory);
        }
        catch
        {
            yield break;
        }

        yield return full;
        yield return Path.Combine(full, BinFolderName);
    }

    private static bool TryResolve(string directory, out MediaToolPaths paths)
    {
        paths = null!;
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        try
        {
            var ffmpeg = Path.Combine(directory, FfmpegExeName);
            var ffprobe = Path.Combine(directory, FfprobeExeName);
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                paths = new MediaToolPaths(ffmpeg, ffprobe);
                return true;
            }
        }
        catch
        {
            // Malformed directory (e.g. invalid characters): treat as "not found here".
        }

        return false;
    }

    private static bool TryResolveFromPath(out MediaToolPaths paths)
    {
        paths = null!;
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return false;
        }

        foreach (var entry in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            string directory;
            try
            {
                directory = Path.GetFullPath(entry.Trim());
            }
            catch
            {
                continue;
            }

            if (TryResolve(directory, out paths))
            {
                return true;
            }
        }

        return false;
    }
}
