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
/// The temporary WAV exists because the V0.4 diarizer is a whole-file engine that loads its input
/// through <c>IAudioFileLoader</c> (NAudio), which cannot read a video container. We already decode
/// with FFmpeg, so writing our own WAV makes video files diarizable and keeps the ASR path streaming:
/// the file is never buffered in memory. The WAV lives under the caller's staging directory (the
/// app passes its recordings folder, so the existing orphan sweep cleans up after a crash).
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

            // ---- decode (tee'd to the temp WAV) + transcribe --------------------
            OfflineTranscriptionResult transcript;
            TimeSpan written;
            using (var wav = new PcmWavWriter(tempWav, request.TranscriptionOptions.SampleRate))
            {
                var engine = new OfflineTranscriptionEngine(
                    request.Engine, request.TranscriptionOptions, correct: null, _log);

                var decoded = _media.DecodeAsync(
                    new MediaDecodeRequest(request.InputPath, request.AudioStreamIndex, request.TranscriptionOptions.SampleRate),
                    cancellationToken);

                transcript = await engine.TranscribeAsync(
                    Tee(decoded, wav, cancellationToken),
                    new ProgressAdapter<OfflineTranscriptionProgress>(p => progress?.Report(new FileTranscriptionProgress(
                        FileTranscriptionPhase.Transcribe,
                        Fraction(p.Processed, p.Total),
                        p.Processed,
                        p.Total,
                        p.SegmentsEmitted))),
                    info.Duration,
                    cancellationToken).ConfigureAwait(false);

                written = wav.Duration;
            }

            if (transcript.Cancelled)
            {
                return new FileTranscriptionResult { Cancelled = true, AudioDuration = transcript.AudioDuration };
            }

            if (transcript.Segments.Count == 0)
            {
                // A silent file is not a failure: report an empty transcript and create no session.
                return new FileTranscriptionResult
                {
                    AudioDuration = transcript.AudioDuration,
                    Elapsed = stopwatch.Elapsed,
                    Completed = true,
                    Dialogue = new DialogueTranscript(),
                    Warning = "No speech was recognized in the file."
                };
            }

            // A dropped tee frame would shift the whole diarization timeline, so verify the copy.
            if ((written - transcript.AudioDuration).Duration() > TimeSpan.FromMilliseconds(100))
            {
                warning = $"Audio copy is {written.TotalSeconds:F2}s but {transcript.AudioDuration.TotalSeconds:F2}s was decoded; "
                    + "speaker timings may be off.";
                _log.Warn(warning);
            }

            // ---- persist the transcript ----------------------------------------
            var session = new MeetingSession
            {
                Title = request.Title ?? Path.GetFileNameWithoutExtension(request.InputPath),
                StartTime = DateTimeOffset.Now,
                EndTime = DateTimeOffset.Now.Add(transcript.AudioDuration),
                Status = SessionStatus.Completed,
                ModelId = request.TranscriptionOptions.ModelId
            };
            await _subtitles.CreateSessionAsync(session, cancellationToken).ConfigureAwait(false);

            var facts = new List<TranscriptSegmentFact>(transcript.Segments.Count);
            for (int i = 0; i < transcript.Segments.Count; i++)
            {
                var source = transcript.Segments[i];
                var segment = new SubtitleSegment
                {
                    SessionId = session.SessionId,
                    SequenceNumber = i,
                    StartOffset = source.Start,
                    EndOffset = source.End,
                    OriginalText = source.Text,
                    CorrectedText = source.Text,
                    CreatedAt = DateTimeOffset.Now
                };
                await _subtitles.AppendSegmentAsync(segment, cancellationToken).ConfigureAwait(false);

                if (segment.SegmentId == 0)
                {
                    _log.Warn($"Segment {i} was not inserted (duplicate sequence number).");
                    continue;
                }

                facts.Add(new TranscriptSegmentFact(segment.SegmentId, source.Start, source.End, source.Text));
            }

            // ---- diarize (soft failure: the transcript is still valuable) --------
            bool diarized = false;
            if (request.RunDiarization)
            {
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
                                session.SessionId,
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
                        return new FileTranscriptionResult
                        {
                            SessionId = session.SessionId,
                            Segments = transcript.Segments,
                            AudioDuration = transcript.AudioDuration,
                            Elapsed = stopwatch.Elapsed,
                            Cancelled = true
                        };
                    }
                }
            }

            // ---- assemble the role-tagged dialogue ------------------------------
            progress?.Report(new FileTranscriptionProgress(
                FileTranscriptionPhase.Assemble, 1.0, transcript.AudioDuration, info.Duration, facts.Count));

            var assignments = await _speakers.GetAssignmentsAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            var speakers = await _speakers.GetSpeakersAsync(session.SessionId, cancellationToken).ConfigureAwait(false);

            var dialogue = _alignment.Align(
                session.SessionId,
                facts,
                assignments.ToDictionary(a => a.SegmentId),
                speakers.Where(s => !s.IsMerged).ToDictionary(s => s.SpeakerId, StringComparer.Ordinal),
                request.DialogueOptions);

            stopwatch.Stop();
            _log.Info($"File transcription finished: {facts.Count} segment(s), {dialogue.Turns.Count} turn(s), "
                + $"diarized={diarized}, elapsed={stopwatch.Elapsed.TotalSeconds:F1}s.");

            return new FileTranscriptionResult
            {
                SessionId = session.SessionId,
                Segments = transcript.Segments,
                Dialogue = dialogue,
                AudioDuration = transcript.AudioDuration,
                Elapsed = stopwatch.Elapsed,
                Completed = true,
                Diarized = diarized,
                Warning = warning
            };

            FileTranscriptionResult Failure(MediaInfo? info, string message)
            {
                _log.Warn($"File transcription failed: {message}");
                return new FileTranscriptionResult
                {
                    AudioDuration = info?.Duration ?? TimeSpan.Zero,
                    Elapsed = stopwatch.Elapsed,
                    Error = message
                };
            }
        }
        catch (OperationCanceledException)
        {
            return new FileTranscriptionResult { Cancelled = true, Elapsed = stopwatch.Elapsed };
        }
        catch (MediaDecodeException ex)
        {
            return new FileTranscriptionResult { Elapsed = stopwatch.Elapsed, Error = ex.Message };
        }
        finally
        {
            TryDelete(tempWav);
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
