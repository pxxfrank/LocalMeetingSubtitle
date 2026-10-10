using System.Globalization;
using System.Text.Json;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Media;

/// <summary>
/// Runs <c>ffprobe -print_format json</c> and maps the result to <see cref="MediaInfo"/>.
/// Parsing is deliberately defensive: a container may omit any field, so missing values simply
/// fall back to a sensible default rather than failing the probe.
/// </summary>
internal sealed class FFprobeMediaProbe
{
    private readonly MediaToolPaths _tools;
    private readonly IAppLogger _log;

    public FFprobeMediaProbe(MediaToolPaths tools, IAppLogger? log = null)
    {
        _tools = tools;
        _log = log ?? NullLogger.Instance;
    }

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new MediaDecodeException(MediaErrorKind.FileNotFound, "No media path was supplied.");
        }

        if (!File.Exists(path))
        {
            throw new MediaDecodeException(MediaErrorKind.FileNotFound, $"Media file not found: {path}");
        }

        var fullPath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(fullPath);

        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunTextAsync(
                _tools.FfprobePath,
                new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", fullPath },
                cancellationToken).ConfigureAwait(false);
        }
        catch (MediaDecodeException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new MediaDecodeException(MediaErrorKind.Cancelled, "Probing was cancelled.", null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new MediaDecodeException(MediaErrorKind.Cancelled, "Probing was cancelled.");
        }

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new MediaDecodeException(MediaErrorKind.NotReadable,
                $"ffprobe could not read '{fileName}' (exit {result.ExitCode}). {result.StandardErrorTail}".Trim());
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var info = Map(fullPath, fileName, document.RootElement);
            _log.Debug($"Probed '{fileName}': kind={info.Kind}, duration={info.Duration.TotalSeconds:F3}s, "
                       + $"audio={info.AudioStreams.Count} stream(s).");
            return info;
        }
        catch (JsonException ex)
        {
            throw new MediaDecodeException(MediaErrorKind.UnsupportedFormat,
                $"ffprobe returned JSON that could not be parsed for '{fileName}'. {result.StandardErrorTail}".Trim(), ex);
        }
    }

    private static MediaInfo Map(string fullPath, string fileName, JsonElement root)
    {
        var warnings = new List<string>();
        var audioStreams = new List<AudioStreamInfo>();
        bool hasVideo = false;

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                string codecType = GetString(stream, "codec_type");
                if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                {
                    audioStreams.Add(ParseAudioStream(stream, fileName, warnings));
                }
                else if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase) && !IsAttachedPicture(stream))
                {
                    hasVideo = true;
                }
            }
        }

        string containerFormat = "";
        long fileSize = 0;
        TimeSpan duration = TimeSpan.Zero;

        if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
        {
            containerFormat = GetString(format, "format_name");
            fileSize = GetLong(format, "size") ?? 0;
            duration = ParseSeconds(GetString(format, "duration")) ?? TimeSpan.Zero;
        }

        // Some ad-hoc containers have no format-level duration; fall back to the longest stream.
        if (duration <= TimeSpan.Zero)
        {
            duration = audioStreams
                .Where(s => s.Duration.HasValue)
                .Select(s => s.Duration!.Value)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max();
        }

        if (duration <= TimeSpan.Zero)
        {
            warnings.Add("The container reports no duration.");
        }

        if (fileSize <= 0 && File.Exists(fullPath))
        {
            try
            {
                fileSize = new FileInfo(fullPath).Length;
            }
            catch (IOException)
            {
                // Size is advisory; ignore.
            }
        }

        if (audioStreams.Count == 0)
        {
            warnings.Add("No audio stream was found.");
        }

        var kind = hasVideo
            ? MediaKind.Video
            : audioStreams.Count > 0 ? MediaKind.Audio : MediaKind.Unknown;

        return new MediaInfo
        {
            Path = fullPath,
            FileName = fileName,
            FileSize = fileSize,
            Duration = duration,
            Kind = kind,
            ContainerFormat = containerFormat,
            AudioStreams = audioStreams,
            Warnings = warnings
        };
    }

    private static AudioStreamInfo ParseAudioStream(JsonElement stream, string fileName, List<string> warnings)
    {
        int index = GetInt(stream, "index") ?? -1;
        string codec = GetString(stream, "codec_name");
        int channels = GetInt(stream, "channels") ?? 0;
        int sampleRate = GetInt(stream, "sample_rate") ?? 0;

        string? language = null;
        string? title = null;
        if (stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
        {
            language = GetString(tags, "language") is { Length: > 0 } lang ? lang : null;
            title = GetString(tags, "title");
        }

        if (sampleRate <= 0)
        {
            warnings.Add($"Audio stream {index} in '{fileName}' reports no sample rate.");
        }

        return new AudioStreamInfo
        {
            Index = index,
            Codec = codec,
            Channels = channels,
            SampleRate = sampleRate,
            Language = language,
            StartTime = ParseSeconds(GetString(stream, "start_time")),
            Duration = ParseSeconds(GetString(stream, "duration")),
            Title = title ?? ""
        };
    }

    /// <summary>Cover art surfaces as a "video" stream with the attached_pic disposition; it is not real video.</summary>
    private static bool IsAttachedPicture(JsonElement stream)
    {
        if (stream.TryGetProperty("disposition", out var disposition) && disposition.ValueKind == JsonValueKind.Object)
        {
            return GetInt(disposition, "attached_pic") == 1;
        }

        return false;
    }

    private static string GetString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? "";
        }

        return "";
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out var value) ? value : null,
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null,
            _ => null
        };
    }

    private static long? GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out var value) ? value : null,
            JsonValueKind.String => long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null,
            _ => null
        };
    }

    private static TimeSpan? ParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || double.IsNaN(seconds)
            || double.IsInfinity(seconds))
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
