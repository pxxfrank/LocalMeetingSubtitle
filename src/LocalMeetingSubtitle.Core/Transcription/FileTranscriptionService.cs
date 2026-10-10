using System.Diagnostics;
using System.Runtime.CompilerServices;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Transcription;

/// <summary>
/// Runs one file-transcription job: probe → decode (tee'd into a temporary 16 kHz mono WAV) →
/// timestamped offline transcription → persist the session and segments → diarize → assemble the
/// role-tagged dialogue.
///
/// Segments are persisted <b>as they are produced</b>, so an interrupted run keeps everything up to
/// the last committed segment and can resume. The resume cursor is derived from those segments
/// (max end / max sequence), never from a separately stored counter, so it can never run ahead of
/// the data that actually exists.
///
/// The temporary WAV exists because the V0.4 diarizer is a whole-file engine that loads its input
/// through <c>IAudioFileLoader</c> (NAudio), which cannot read a video container. We already decode
/// with FFmpeg, so writing our own WAV makes video files diarizable and keeps the ASR path streaming:
/// the file is never buffered in memory. The WAV lives under the caller's staging directory (the app
/// passes its recordings folder, so the existing orphan sweep cleans up after a crash).
/// </summary>
public sealed class FileTranscriptionService : IFileTranscriptionService
{
    private readonly IMediaDecodeService _media;
    private readonly ISpeakerDiarizationService _diarization;
    private readonly ISubtitleRepository _subtitles;
    private readonly ISpeakerRepository _speakers;
    private readonly ITranscriptAlignmentService _alignment;
    private readonly IAppLogger _log;
    private readonly string _stagingDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileTranscriptionService(
        IMediaDecodeService media,
        ISpeakerDiarizationService diarization,
        ISubtitleRepository subtitles,
        ISpeakerRepository speakers,
        ITranscriptAlignmentService alignment,
        string stagingDirectory,
        IAppLogger? log = null)
    {
        _media = media;
        _diarization = diarization;
        _subtitles = subtitles;
        _speakers = speakers;
        _alignment = alignment;
        _stagingDirectory = stagingDirectory;
        _log = log ?? NullLogger.Instance;
    }

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<FileTranscriptionResult> RunAsync(
        FileTranscriptionRequest request,
        IProgress<FileTranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool acquired;
        try
        {
            acquired = await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new FileTranscriptionResult { Cancelled = true };
        }

        if (!acquired)
        {
            return new FileTranscriptionResult { Error = "A file transcription job is already running." };
        }

        try
        {
            return await RunCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileTranscriptionResult> RunCoreAsync(
        FileTranscriptionRequest request,
        IProgress<FileTranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(_stagingDirectory);
        string tempWav = Path.Combine(_stagingDirectory, $"dijob-{Guid.NewGuid():N}.wav");
        string? warning = null;
        string? sessionId = null;

        try
        {
            MediaInfo info;
            try
            {
                info = await _media.ProbeAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
            }
            catch (MediaDecodeException ex)
            {
                return Failure(null, ex.Message);
            }

            if (!info.HasAudio)
            {
                return Failure(info, "The file has no audio track.");
            }

            // ---- session + resume cursor (the cursor IS the committed data) ----
            bool resuming = !string.IsNullOrEmpty(request.SessionId);
            TimeSpan resumeFrom = TimeSpan.Zero;
            int nextSequence = 0;

            if (resuming)
            {
                sessionId = request.SessionId!;
                var existing = await _subtitles.GetSegmentsAsync(sessionId, cancellationToken).ConfigureAwait(false);
                if (existing.Count > 0)
                {
                    resumeFrom = existing.Max(s => s.EndOffset);
                    nextSequence = existing.Max(s => s.SequenceNumber) + 1;
                }

                await _subtitles.UpdateSessionStatusAsync(sessionId, SessionStatus.Recording, null, cancellationToken)
                    .ConfigureAwait(false);
                _log.Info($"Resuming session {sessionId} at {resumeFrom.TotalSeconds:F3}s (sequence {nextSequence}).");
            }
            else
            {
                var session = new MeetingSession
                {
                    Title = request.Title ?? Path.GetFileNameWithoutExtension(request.InputPath),
                    StartTime = DateTimeOffset.Now,
                    Status = SessionStatus.Recording,
                    ModelId = request.TranscriptionOptions.ModelId
                };
                await _subtitles.CreateSessionAsync(session, cancellationToken).ConfigureAwait(false);
                sessionId = session.SessionId;
            }

            var options = request.TranscriptionOptions;
            var facts = new List<TranscriptSegmentFact>();

            async Task PersistAsync(OfflineTranscriptSegment segment, CancellationToken token)
            {
                var row = new SubtitleSegment
                {
                    SessionId = sessionId!,
                    SequenceNumber = nextSequence++,
                    StartOffset = segment.Start,
                    EndOffset = segment.End,
                    OriginalText = segment.Text,
                    CorrectedText = segment.Text,
                    CreatedAt = DateTimeOffset.Now
                };

                await _subtitles.AppendSegmentAsync(row, token).ConfigureAwait(false);
                if (row.SegmentId == 0)
                {
                    _log.Warn($"Segment {row.SequenceNumber} was not inserted (duplicate sequence number).");
                    return;
                }

                facts.Add(new TranscriptSegmentFact(row.SegmentId, segment.Start, segment.End, segment.Text));
            }

            // ---- decode + transcribe, persisting every segment as it appears ----
            var engine = new OfflineTranscriptionEngine(request.Engine, options, correct: null, _log);
            var decodeRequest = new MediaDecodeRequest(request.InputPath, request.AudioStreamIndex, options.SampleRate, resumeFrom);
            var transcribeProgress = new ProgressAdapter<OfflineTranscriptionProgress>(p => progress?.Report(
                new FileTranscriptionProgress(
                    FileTranscriptionPhase.Transcribe,
                    Fraction(resumeFrom + p.Processed, info.Duration),
                    resumeFrom + p.Processed,
                    info.Duration,
                    facts.Count)));

            OfflineTranscriptionResult transcript;
            TimeSpan stagedAudio = TimeSpan.Zero;
            if (resumeFrom == TimeSpan.Zero)
            {
                using var wav = new PcmWavWriter(tempWav, options.SampleRate);
                transcript = await engine.TranscribeAsync(
                    Tee(_media.DecodeAsync(decodeRequest, cancellationToken), wav, cancellationToken),
                    transcribeProgress,
                    info.Duration,
                    cancellationToken,
                    PersistAsync).ConfigureAwait(false);
                stagedAudio = wav.Duration;

                // A dropped tee frame would shift the whole diarization timeline, so verify the copy.
                if ((stagedAudio - transcript.AudioDuration).Duration() > TimeSpan.FromMilliseconds(100))
                {
                    warning = $"Audio copy is {stagedAudio.TotalSeconds:F2}s but {transcript.AudioDuration.TotalSeconds:F2}s was decoded; "
                        + "speaker timings may be off.";
                    _log.Warn(warning);
                }
            }
            else
            {
                // No tee on a resume: the staging WAV would only hold the tail. It is rebuilt below
                // if diarization is requested.
                transcript = await engine.TranscribeAsync(
                    _media.DecodeAsync(decodeRequest, cancellationToken),
                    transcribeProgress,
                    info.Duration,
                    cancellationToken,
                    PersistAsync).ConfigureAwait(false);
            }

            if (transcript.Cancelled)
            {
                await SetSessionStatusAsync(sessionId, SessionStatus.Paused, null, cancellationToken).ConfigureAwait(false);
                return new FileTranscriptionResult
                {
                    SessionId = sessionId,
                    Segments = transcript.Segments,
                    AudioDuration = info.Duration,
                    Elapsed = stopwatch.Elapsed,
                    Cancelled = true,
                    Warning = warning
                };
            }

            if (facts.Count == 0)
            {
                await SetSessionStatusAsync(sessionId, SessionStatus.Completed, DateTimeOffset.Now, cancellationToken)
                    .ConfigureAwait(false);
                return new FileTranscriptionResult
                {
                    SessionId = sessionId,
                    AudioDuration = info.Duration,
                    Elapsed = stopwatch.Elapsed,
                    Completed = true,
                    Dialogue = new DialogueTranscript { SessionId = sessionId },
                    Warning = warning ?? "No speech was recognized in the file."
                };
            }

            // ---- diarize (soft failure: the transcript is still valuable) --------
            bool diarized = false;
            if (request.RunDiarization)
            {
                if (stagedAudio == TimeSpan.Zero)
                {
                    // A resumed run has no whole-file staging WAV; build one with a single decode pass.
                    stagedAudio = await WriteStagingWavAsync(
                        tempWav,
                        new MediaDecodeRequest(request.InputPath, request.AudioStreamIndex, options.SampleRate),
                        cancellationToken).ConfigureAwait(false);
                }

                if (_diarization.IsBusy)
                {
                    warning = Append(warning, "Another diarization run is in progress; speakers were not assigned.");
                }
                else
                {
                    var diarizationProgress = new ProgressAdapter<DiarizationProgress>(p => progress?.Report(
                        new FileTranscriptionProgress(FileTranscriptionPhase.Diarize, p.Fraction, TimeSpan.Zero, info.Duration, facts.Count)));

                    try
                    {
                        var diarization = await _diarization.RunAsync(
                            new DiarizationRequest(
                                sessionId,
                                tempWav,
                                request.DiarizationCountMode,
                                request.ManualSpeakerCount,
                                request.ClusteringThreshold),
                            diarizationProgress,
                            cancellationToken).ConfigureAwait(false);

                        if (diarization.Success)
                        {
                            diarized = true;
                        }
                        else
                        {
                            warning = Append(warning, diarization.Error ?? "Speaker diarization failed.");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        await SetSessionStatusAsync(sessionId, SessionStatus.Paused, null, CancellationToken.None).ConfigureAwait(false);
                        return new FileTranscriptionResult
                        {
                            SessionId = sessionId,
                            Segments = transcript.Segments,
                            AudioDuration = info.Duration,
                            Elapsed = stopwatch.Elapsed,
                            Cancelled = true,
                            Warning = warning
                        };
                    }
                }
            }

            // ---- assemble the role-tagged dialogue ------------------------------
            progress?.Report(new FileTranscriptionProgress(
                FileTranscriptionPhase.Assemble, 1.0, info.Duration, info.Duration, facts.Count));

            var assignments = await _speakers.GetAssignmentsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var speakers = await _speakers.GetSpeakersAsync(sessionId, cancellationToken).ConfigureAwait(false);

            var dialogue = _alignment.Align(
                sessionId,
                facts,
                assignments.ToDictionary(a => a.SegmentId),
                speakers.Where(s => !s.IsMerged).ToDictionary(s => s.SpeakerId, StringComparer.Ordinal),
                request.DialogueOptions);

            await SetSessionStatusAsync(sessionId, SessionStatus.Completed, DateTimeOffset.Now.Add(info.Duration), cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            _log.Info($"File transcription finished: {facts.Count} segment(s), {dialogue.Turns.Count} turn(s), "
                + $"diarized={diarized}, resumed={resuming}, elapsed={stopwatch.Elapsed.TotalSeconds:F1}s.");

            return new FileTranscriptionResult
            {
                SessionId = sessionId,
                Segments = transcript.Segments,
                Dialogue = dialogue,
                AudioDuration = info.Duration,
                Elapsed = stopwatch.Elapsed,
                Completed = true,
                Diarized = diarized,
                Warning = warning
            };

            FileTranscriptionResult Failure(MediaInfo? media, string message)
            {
                _log.Warn($"File transcription failed: {message}");
                return new FileTranscriptionResult
                {
                    SessionId = sessionId ?? "",
                    AudioDuration = media?.Duration ?? TimeSpan.Zero,
                    Elapsed = stopwatch.Elapsed,
                    Error = message
                };
            }
        }
        catch (OperationCanceledException)
        {
            await SetSessionStatusAsync(sessionId, SessionStatus.Paused, null, CancellationToken.None).ConfigureAwait(false);
            return new FileTranscriptionResult { SessionId = sessionId ?? "", Cancelled = true, Elapsed = stopwatch.Elapsed };
        }
        catch (MediaDecodeException ex)
        {
            await SetSessionStatusAsync(sessionId, SessionStatus.Aborted, null, CancellationToken.None).ConfigureAwait(false);
            return new FileTranscriptionResult { SessionId = sessionId ?? "", Elapsed = stopwatch.Elapsed, Error = ex.Message };
        }
        finally
        {
            TryDelete(tempWav);
        }
    }

    /// <summary>Decodes a whole file into a fresh staging WAV (used when a resume needs one).</summary>
    private async Task<TimeSpan> WriteStagingWavAsync(
        string path,
        MediaDecodeRequest request,
        CancellationToken cancellationToken)
    {
        using var wav = new PcmWavWriter(path, request.TargetSampleRate);
        await foreach (var block in _media.DecodeAsync(request, cancellationToken).ConfigureAwait(false))
        {
            wav.Write(block.Samples);
        }

        return wav.Duration;
    }

    private async Task SetSessionStatusAsync(
        string? sessionId,
        SessionStatus status,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        try
        {
            await _subtitles.UpdateSessionStatusAsync(sessionId, status, endTime, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"Could not update session {sessionId} status to {status}: {ex.Message}");
        }
    }

    /// <summary>Copies every decoded block into the WAV while streaming it on to the recognizer.</summary>
    private static async IAsyncEnumerable<PcmBlock> Tee(
        IAsyncEnumerable<PcmBlock> source,
        PcmWavWriter wav,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var block in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            wav.Write(block.Samples);
            yield return block;
        }
    }

    private static double Fraction(TimeSpan processed, TimeSpan total) =>
        total > TimeSpan.Zero ? Math.Clamp(processed / total, 0.0, 1.0) : 0.0;

    private static string Append(string? existing, string message) =>
        string.IsNullOrEmpty(existing) ? message : existing + " " + message;

    private static void TryDelete(string path)
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

    /// <summary>Forwards progress synchronously so reports keep their order.</summary>
    private sealed class ProgressAdapter<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public ProgressAdapter(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
