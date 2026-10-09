using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Storage;

/// <summary>
/// Owns the on-disk side of the audio-asset registry: deletes expired temporary recordings (their
/// file and their row) and sweeps orphaned recording files left behind by a crash. Recording files
/// are named <c>&lt;audioAssetId&gt;.wav</c>, so a file with no matching row is an orphan. Locked
/// files are logged and skipped, never fatal.
/// </summary>
public sealed class LocalAudioAssetStore
{
    private const string RecordingExtension = ".wav";

    /// <summary>An unreferenced file older than this is considered a crash leftover.</summary>
    private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(24);

    private readonly string _recordingsDirectory;
    private readonly IAppLogger? _log;

    public LocalAudioAssetStore(string recordingsDirectory, IAppLogger? log = null)
    {
        _recordingsDirectory = recordingsDirectory ?? throw new ArgumentNullException(nameof(recordingsDirectory));
        _log = log;
    }

    /// <summary>
    /// Deletes every expired temporary asset (file + row), then sweeps orphaned recording files
    /// older than 24 h. Returns the number of files deleted.
    /// </summary>
    public async Task<int> CleanupAsync(IAudioAssetRepository assets, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var deleted = 0;

        var expired = await assets.GetExpiredTemporaryAsync(now, cancellationToken).ConfigureAwait(false);
        foreach (var asset in expired)
        {
            if (IsUnderRecordingsDirectory(asset.Path))
            {
                if (TryDeleteFile(asset.Path, out var reason))
                {
                    deleted++;
                    _log?.Info($"Deleted expired temporary recording '{asset.Path}'.");
                }
                else if (reason is not null)
                {
                    _log?.Warn($"Could not delete temporary recording '{asset.Path}': {reason}");
                }
            }

            try
            {
                await assets.DeleteAsync(asset.AudioAssetId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Warn($"Deleting audio-asset row '{asset.AudioAssetId}' failed: {ex.Message}");
            }
        }

        deleted += await SweepOrphansAsync(assets, now, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    private async Task<int> SweepOrphansAsync(IAudioAssetRepository assets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var deleted = 0;
        if (!Directory.Exists(_recordingsDirectory))
        {
            return deleted;
        }

        var cutoffUtc = now.UtcDateTime - OrphanAge;

        foreach (var file in Directory.EnumerateFiles(_recordingsDirectory, "*" + RecordingExtension))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > cutoffUtc)
                {
                    continue; // Too fresh to be treated as a leftover.
                }

                var audioAssetId = Path.GetFileNameWithoutExtension(file);
                var registered = await assets.GetAsync(audioAssetId, cancellationToken).ConfigureAwait(false);
                if (registered is not null)
                {
                    continue; // Still referenced by a row.
                }

                if (TryDeleteFile(file, out var reason))
                {
                    deleted++;
                    _log?.Info($"Deleted orphaned recording '{file}'.");
                }
                else if (reason is not null)
                {
                    _log?.Warn($"Could not delete orphaned recording '{file}': {reason}");
                }
            }
            catch (Exception ex)
            {
                _log?.Warn($"Sweeping recording '{file}' failed: {ex.Message}");
            }
        }

        return deleted;
    }

    private bool IsUnderRecordingsDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(_recordingsDirectory);
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteFile(string path, out string? reason)
    {
        reason = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = ex.Message;
            return false;
        }
    }
}
