using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Media;

/// <summary>
/// Decodes local audio/video to 16 kHz mono float32 through the bundled FFmpeg.
///
/// <see cref="DecodeAsync"/> probes first (to validate the file and resolve the track), then runs
/// the decode command and streams stdout block by block — the whole file is never buffered. A
/// bounded channel provides back-pressure between the reader thread and the consumer.
/// </summary>
public sealed class FFmpegMediaDecodeService : IMediaDecodeService
{
    /// <summary>Samples per emitted block: one second at 16 kHz.</summary>
    private const int BlockSamples = 16000;

    /// <summary>Bytes read from ffmpeg's stdout per pass (any partial float32 is carried to the next pass).</summary>
    private const int ReadBufferBytes = 64 * 1024;

    /// <summary>How many decoded blocks may be in flight before the reader is throttled.</summary>
    private const int ChannelCapacity = 4;

    private readonly MediaToolPaths _tools;
    private readonly IAppLogger _log;
    private readonly FFprobeMediaProbe _probe;

    public FFmpegMediaDecodeService(MediaToolPaths tools, IAppLogger? log = null)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _log = log ?? NullLogger.Instance;
        _probe = new FFprobeMediaProbe(tools, _log);
    }

    /// <inheritdoc />
    public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default) =>
        _probe.ProbeAsync(path, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<PcmBlock> DecodeAsync(
        MediaDecodeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        int sampleRate = request.TargetSampleRate > 0 ? request.TargetSampleRate : 16000;

        var info = await _probe.ProbeAsync(request.Path, cancellationToken).ConfigureAwait(false);

        if (!info.HasAudio)
        {
            throw new MediaDecodeException(MediaErrorKind.NoAudioTrack,
                $"'{info.FileName}' has no audio stream to decode.");
        }

        // The index is the ordinal among the file's audio streams (ffmpeg's "0:a:N" selector).
        int ordinal = request.AudioStreamIndex ?? 0;
        if (ordinal < 0 || ordinal >= info.AudioStreams.Count)
        {
            throw new MediaDecodeException(MediaErrorKind.StreamIndexOutOfRange,
                $"Audio stream index {ordinal} is out of range for '{info.FileName}' "
                + $"(it has {info.AudioStreams.Count} audio stream(s)).");
        }

        var arguments = new List<string>
        {
            "-v", "error",
            "-accurate_seek"
        };

        // Input seeking: accurate because we transcode, and O(1) rather than decoding-and-discarding
        // the whole prefix of a multi-hour file.
        if (request.StartOffset > TimeSpan.Zero)
        {
            arguments.Add("-ss");
            arguments.Add(request.StartOffset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        arguments.AddRange(new[]
        {
            "-i", info.Path,
            "-map", $"0:a:{ordinal}",
            "-vn", "-sn", "-dn",
            "-f", "f32le",
            "-acodec", "pcm_f32le",
            "-ac", "1",
            "-ar", sampleRate.ToString(CultureInfo.InvariantCulture),
            "pipe:1"
        });

        _log.Debug($"Decoding '{info.FileName}' (stream 0:a:{ordinal}) as {sampleRate} Hz mono float32"
            + (request.StartOffset > TimeSpan.Zero ? $", from {request.StartOffset.TotalSeconds:F3}s" : "") + ".");

        var channel = Channel.CreateBounded<PcmBlock>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        // A linked source lets an abandoned enumeration (dispose without cancellation) also stop ffmpeg.
        using var decodeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var pump = PumpAsync(arguments, sampleRate, request.StartOffset, channel.Writer, decodeCancellation.Token);
        // Surface the pump's outcome through the channel so the reader sees mapped failures and terminates.
        _ = pump.ContinueWith(
            static (finished, state) =>
            {
                var writer = (ChannelWriter<PcmBlock>)state!;
                var failure = finished.IsFaulted ? finished.Exception?.InnerException ?? finished.Exception : null;
                writer.TryComplete(failure);
            },
            channel.Writer,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            await foreach (var block in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                yield return block;
            }
        }
        finally
        {
            // Kills the process tree if the consumer stopped early; a no-op once the pump has finished.
            decodeCancellation.Cancel();
        }

        // Reached only when the pump completed successfully; a faulted pump throws via the channel above.
        await pump.ConfigureAwait(false);
    }

    /// <summary>Drives one decode process to completion, mapping its outcome to <see cref="MediaDecodeException"/>.</summary>
    private async Task PumpAsync(
        IReadOnlyList<string> arguments,
        int sampleRate,
        TimeSpan startOffset,
        ChannelWriter<PcmBlock> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunBinaryAsync(
                _tools.FfmpegPath,
                arguments,
                (stream, token) => PumpBlocksAsync(stream, sampleRate, startOffset, writer, token),
                cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new MediaDecodeException(MediaErrorKind.Cancelled, "Media decoding was cancelled.");
            }

            if (result.ExitCode != 0)
            {
                throw new MediaDecodeException(MediaErrorKind.Failed,
                    $"ffmpeg exited with code {result.ExitCode} while decoding. {result.StandardErrorTail}".Trim());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new MediaDecodeException(MediaErrorKind.Cancelled, "Media decoding was cancelled.");
        }
    }

    /// <summary>
    /// Reads raw little-endian float32 from stdout, re-assembles whole samples across reads and
    /// emits one-second blocks. Only a small fixed buffer plus the current block are held.
    /// </summary>
    private static async Task PumpBlocksAsync(
        Stream stdout,
        int sampleRate,
        TimeSpan startOffset,
        ChannelWriter<PcmBlock> writer,
        CancellationToken cancellationToken)
    {
        var block = new float[BlockSamples];
        int blockCount = 0;
        long totalSamples = 0;
        int carry = 0;

        var bytes = new byte[ReadBufferBytes + 4];
        var floats = new float[bytes.Length / 4];

        while (true)
        {
            int read = await stdout.ReadAsync(
                bytes.AsMemory(carry, bytes.Length - carry), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            int available = carry + read;
            int usable = available & ~3; // largest multiple of 4 bytes = whole float32 samples

            if (usable > 0)
            {
                int floatCount = usable / 4;
                Buffer.BlockCopy(bytes, 0, floats, 0, usable);

                for (int i = 0; i < floatCount; i++)
                {
                    block[blockCount++] = floats[i];
                    if (blockCount == BlockSamples)
                    {
                        await EmitBlockAsync(writer, block, BlockSamples, totalSamples, sampleRate, startOffset, cancellationToken)
                            .ConfigureAwait(false);
                        totalSamples += BlockSamples;
                        blockCount = 0;
                    }
                }

                carry = available - usable;
                if (carry > 0)
                {
                    Buffer.BlockCopy(bytes, usable, bytes, 0, carry);
                }
            }
            else
            {
                carry = available;
            }
        }

        if (blockCount > 0)
        {
            await EmitBlockAsync(writer, block, blockCount, totalSamples, sampleRate, startOffset, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EmitBlockAsync(
        ChannelWriter<PcmBlock> writer,
        float[] block,
        int count,
        long totalSamplesBefore,
        int sampleRate,
        TimeSpan startOffset,
        CancellationToken cancellationToken)
    {
        var samples = new float[count];
        Array.Copy(block, samples, count);
        // Keep the position absolute on the media timeline so a seeked decode lines up with the
        // segments already committed before the resume point.
        var start = startOffset + TimeSpan.FromSeconds((double)totalSamplesBefore / sampleRate);
        var pcmBlock = new PcmBlock(samples, start, sampleRate);
        await writer.WriteAsync(pcmBlock, cancellationToken).ConfigureAwait(false);
    }
}
