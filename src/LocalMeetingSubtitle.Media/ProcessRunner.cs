using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LocalMeetingSubtitle.Core.Abstractions;

namespace LocalMeetingSubtitle.Media;

/// <summary>Exit code, stdout text (probe only) and a bounded tail of the child process' stderr.</summary>
internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardErrorTail);

/// <summary>
/// Minimal, injection-safe launcher for the FFmpeg executables.
///
/// Every argument is passed through <see cref="ProcessStartInfo.ArgumentList"/> — never a shell and
/// never string concatenation — so paths with spaces, non-ASCII characters or long names survive
/// intact. Stdout is captured either as text or, for decoding, as a raw byte stream fed to a
/// callback. Cancellation kills the whole process tree.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>How many trailing stderr characters a failure message keeps.</summary>
    private const int MaxStderrChars = 1500;

    /// <summary>Runs to completion and returns stdout as text (used by ffprobe).</summary>
    public static async Task<ProcessResult> RunTextAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess(fileName, arguments);
        using var registration = cancellationToken.Register(static state => TryKillTree((Process)state!), process);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = ReadTailAsync(process.StandardError, cancellationToken);

        await WaitForExitAsync(process).ConfigureAwait(false);

        string stdout = "";
        try
        {
            stdout = await stdoutTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The process was killed on cancellation; whatever was buffered is discarded.
        }
        catch (IOException)
        {
            // Broken pipe while the process exited.
        }

        string stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Runs to completion while <paramref name="consumeStdout"/> reads stdout as a raw byte stream
    /// (used by ffmpeg). stderr is drained concurrently so the pipe cannot fill and deadlock.
    /// </summary>
    public static async Task<ProcessResult> RunBinaryAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Func<Stream, CancellationToken, Task> consumeStdout,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess(fileName, arguments);
        using var registration = cancellationToken.Register(static state => TryKillTree((Process)state!), process);

        var stderrTask = ReadTailAsync(process.StandardError, cancellationToken);

        Exception? consumeError = null;
        try
        {
            await consumeStdout(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            consumeError = ex;
        }

        await WaitForExitAsync(process).ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        if (consumeError is not null)
        {
            throw consumeError;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return new ProcessResult(process.ExitCode, "", stderr);
    }

    private static Process StartProcess(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new MediaDecodeException(MediaErrorKind.ToolMissing, $"Failed to start '{fileName}'.");
            }
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new MediaDecodeException(MediaErrorKind.ToolMissing,
                $"Could not start '{fileName}': {ex.Message}", ex);
        }

        return process;
    }

    private static async Task WaitForExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Exit is signalled by the pipes closing; a kill on cancellation can surface here.
        }
    }

    /// <summary>Reads a reader to end, keeping only its last <see cref="MaxStderrChars"/> characters.</summary>
    private static async Task<string> ReadTailAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var chunk = new char[4096];
        var tail = new StringBuilder();
        try
        {
            int read;
            while ((read = await reader.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                tail.Append(chunk, 0, read);
                int overflow = tail.Length - MaxStderrChars;
                if (overflow > 0)
                {
                    tail.Remove(0, overflow);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Process being killed.
        }
        catch (IOException)
        {
            // Pipe closed.
        }

        return tail.ToString().Trim();
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process already exited or could not be killed; nothing to do.
        }
    }
}
