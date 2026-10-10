# Architecture — V0.5: offline file transcription + role-tagged dialogue

> **Status: work in progress.** **Phase 0** (FFmpeg tooling + licensing), **Phase 1** (media decode
> layer), **Phase 2** (segmented long-audio offline ASR), **Phase 3** (role-tagged dialogue:
> diarization of the decoded file + transcript/speaker alignment) and **Phase 4** (job queue +
> checkpoint/resume, migration 5) are implemented and verified;
> **Phases 5–8 are NOT_STARTED**. All target-hardware acceptance (Windows 11 + Core Ultra 7 155H) is
> **BLOCKED / NOT_TESTED** because that machine is not available — every fact below was measured on
> the Windows 10 dev host.
>
> This document is **new in V0.5** and sits beside the frozen V0.4 design in
> [`ARCHITECTURE.md`](ARCHITECTURE.md); it does not replace it. Where it is honest to say "planned",
> it says so explicitly rather than describing imagined code as if it existed.

## 1. Goal

Add an **offline file-transcription** workflow to 字幕君 **without changing the existing live
subtitle feature**:

- Import a local **audio or video file** (meeting recording, screen recording, etc.).
- Decode it locally and transcribe it **offline** with the existing sherpa-onnx engine.
- Run the V0.4 **offline speaker diarization** over the result.
- Align transcript and speakers into a **role-tagged dialogue** (who said what, when).
- Export the dialogue (TXT / Markdown / CSV / SRT / DOCX).

Everything is offline and CPU-only, exactly like the live path.

## 2. Two entries into the product

| Entry | Behaviour | Status |
| --- | --- | --- |
| **Live subtitles** (existing) | WASAPI loopback captures system playback → streaming ASR → live subtitles → SQLite. Captures *what is playing right now*. | **Unchanged** — V0.5 adds nothing to this path, and the V0.4 regression tests still pass (see §8). |
| **File transcription** (new) | User selects a local media file → FFmpeg decodes it to 16 kHz mono PCM → VAD segmentation → per-segment offline ASR → diarization → role-tagged dialogue → export. Works on *an already-recorded file*. | **Phase 0–4 done.** Media decode, segmented long-audio offline ASR (timestamped segments, three modes), role-tagged dialogue (diarization of the decoded file + transcript/speaker alignment) and the **resumable job queue** (migration 5) are done; the editor UI, dialog export and packaging are not started. |

The two entries are fully independent: the file path never touches the capture pipeline, and it is
designed (see §7) so it can never starve live ASR.

## 3. Module map

Existing V0.4 projects are unchanged. V0.5 adds one project (`LocalMeetingSubtitle.Media`) and
declares a small set of new `Core` contracts; the rest of `Core` is **reused**.

| Project / contract | Kind | Responsibility | Status |
| --- | --- | --- | --- |
| `LocalMeetingSubtitle.Core` | existing project | Contracts + domain model, audio math, live pipeline, hotwords/correction, diarization contracts + services | unchanged |
| `LocalMeetingSubtitle.Audio` | existing project | WASAPI loopback capture; `NaudioAudioFileLoader` | unchanged |
| `LocalMeetingSubtitle.Asr` | existing project | sherpa-onnx engine; `SherpaOfflineSpeakerDiarizer` (V0.4) | unchanged |
| `LocalMeetingSubtitle.Storage` | existing project | SQLite (WAL) + repositories (schema is at **migration 5** since Phase 4) | unchanged |
| `LocalMeetingSubtitle.Export` | existing project | TXT / SRT / Markdown / CSV formatters | unchanged |
| `LocalMeetingSubtitle.Diagnostics` | existing project | CPU / memory monitor | unchanged |
| `LocalMeetingSubtitle.ModelDownloads` | existing project | the only `System.Net.Http` assembly (isolated) | unchanged |
| **`LocalMeetingSubtitle.Media`** | **new project (V0.5)** | `FFmpegLocator`, `ProcessRunner`, `FFprobeMediaProbe`, `FFmpegMediaDecodeService` — probe a local file and stream its audio as 16 kHz mono float32 PCM | **DONE (Phase 1)** |

New `Core` contracts / models:

| Contract / model | Kind | Purpose | Status |
| --- | --- | --- | --- |
| `Core/Models/MediaModels.cs` | models | `MediaKind`, `AudioStreamInfo`, `MediaInfo`, `PcmBlock`, `MediaDecodeRequest` | **DONE (Phase 1)** |
| `Core/Abstractions/MediaAbstractions.cs` → `IMediaDecodeService` | contract | probe + streaming decode of a local file | **DONE (Phase 1)** |
| `MediaDecodeException` / `MediaErrorKind` / `MediaToolPaths` | types | machine-readable decode failure + resolved tool paths | **DONE (Phase 1)** |
| `ITranscriptionJobService` | contract | own a file-transcription **job**: queue, progress, cancel, checkpoint/resume, status | **DONE (Phase 4)** |
| `ITranscriptAlignmentService` | contract | align the offline transcript's segments with diarization intervals into a role-tagged dialogue | **DONE (Phase 3)** |
| `IFileTranscriptionService` | contract | run one file end-to-end: admit → probe → decode (tee'd) → ASR → persist → diarize → align | **DONE (Phase 3)** |
| `ISpeakerDiarizationService` (from V0.4) | **reused** | run offline speaker diarization over decoded audio | reuse; file-job wiring **DONE (Phase 3)** |

> `ISpeakerDiarizationService` and `ISpeakerAlignmentService` already exist from V0.4 and are
> exercised by the speaker-diarization tests. V0.5 will **reuse** them for file jobs rather than add
> a parallel diarization stack. The new `ITranscriptAlignmentService` is the file-job-specific
> seam (segment/VAD boundaries + speakers → dialogue turns).

New **Phase 2** types (segmented long-audio offline ASR; detailed in §6):

| Type | Kind | Purpose | Status |
| --- | --- | --- | --- |
| `Core/Audio/SpeechSegment.cs` | model | `readonly record struct SpeechSegment(float[] Samples, long StartSample, long EndSample, bool HasOverlapPrefix)` (with `Length`) — one VAD region carrying **absolute** sample bounds | **DONE (Phase 2)** |
| `Core/Audio/AudioSegmenter.cs` (modified) | component | absolute-sample bookkeeping; optional `double overlapSeconds = 0.0` (last ctor arg, clamped to `maxSegmentSamples/2`); new `PushSegments` / `FlushSegment` return `SpeechSegment` | **DONE (Phase 2)** |
| `Core/Models/OfflineTranscriptionModels.cs` | models | `TranscriptionMode`, `OfflineTranscriptionOptions`, `OfflineTranscriptSegment`, `OfflineTranscriptionResult`, `OfflineTranscriptionProgress` | **DONE (Phase 2)** |
| `Core/Transcription/OfflineTranscriptionEngine.cs` | component | stream PCM blocks → VAD → per-region offline decode → timestamped segments (in memory) | **DONE (Phase 2)** |
| `Core/Transcription/OverlapTextDeduplicator.cs` | component | fold both texts and trim the longest shared suffix/prefix run (overlap de-duplication) | **DONE (Phase 2)** |
| `Asr/TranscriptionModeCatalog.cs` | component | `Resolve(mode, …)` → `ResolvedTranscriptionMode(…, IsAvailable, UnavailableReason)` — the data-driven three-mode catalog | **DONE (Phase 2)** |
| `Asr/AsrOptionsFactory.cs` (modified) | component | `FromDescriptor` gained optional `decodingMethod`, `language`, `useInverseTextNormalization` | **DONE (Phase 2)** |

New **Phase 3** types (role-tagged dialogue; detailed in §6.5):

| Type | Kind | Purpose | Status |
| --- | --- | --- | --- |
| `Core/Models/DialogueModels.cs` | models | `TranscriptSegmentFact`, `DialogueTurn`, `DialogueParticipant`, `DialogueTranscript`, `DialogueAssemblyOptions` — the role-tagged dialogue shape | **DONE (Phase 3)** |
| `Core/Abstractions/TranscriptAlignmentAbstractions.cs` → `ITranscriptAlignmentService` | contract | `Align(sessionId, segments, assignments, speakers, options?)` → `DialogueTranscript` | **DONE (Phase 3)** |
| `Core/Speakers/TranscriptAlignmentService.cs` | component (pure) | merge consecutive same-speaker segments into turns; aggregate `Participants`; unknown-speaker turns | **DONE (Phase 3)** |
| `Core/Abstractions/FileTranscriptionAbstractions.cs` | contract + models | `IFileTranscriptionService`, `FileTranscriptionPhase`, `FileTranscriptionProgress`, `FileTranscriptionRequest`, `FileTranscriptionResult` | **DONE (Phase 3)** |
| `Core/Transcription/FileTranscriptionService.cs` | component | run one file end-to-end (admission → probe → tee decode → ASR → persist → diarize → align) | **DONE (Phase 3)** |
| `Core/Audio/PcmWavWriter.cs` | component | **synchronous** incremental PCM16 WAV writer (patches RIFF/`data` sizes on `Dispose`) for the diarizer's temp file | **DONE (Phase 3)** |

New **Phase 4** types (job queue + checkpoint/resume; detailed in §6.6):

| Type | Kind | Purpose | Status |
| --- | --- | --- | --- |
| `Core/Models/JobModels.cs` | models | `enum TranscriptionJobStatus { Queued, Running, Paused, Succeeded, Failed, Cancelled, Interrupted }`; `MediaFileRecord`; `TranscriptionJob` (all job columns + `Progress`, `IsFinished`, `IsRunnable`); `sealed record TranscriptionJobRequest(InputPath, TranscriptionMode Mode, string ModelId, int? AudioStreamIndex, bool RunDiarization, string? Title, SpeakerCountMode DiarizationCountMode, int ManualSpeakerCount, double ClusteringThreshold)` | **DONE (Phase 4)** |
| `Core/Abstractions/JobAbstractions.cs` | contracts | `IMediaFileRepository`, `ITranscriptionJobRepository`, `delegate Task<FileTranscriptionRequest> TranscriptionJobResolver(TranscriptionJob job, CancellationToken ct)`, and `ITranscriptionJobService` (queue/progress/cancel/resume/recover/drain; also `IAsyncDisposable`) | **DONE (Phase 4)** |
| `Storage/SqliteMediaFileRepository.cs`, `Storage/SqliteTranscriptionJobRepository.cs` | components | hand-written repositories over the migration-5 tables, same style as `SqliteAudioAssetRepository` | **DONE (Phase 4)** |
| `Core/Transcription/TranscriptionJobService.cs` | component | one FIFO worker on a dedicated below-normal-priority thread; enqueue, cooperative cancel, resume/recover, drain | **DONE (Phase 4)** |
| `Core/Transcription/OfflineTranscriptionEngine.cs` (modified) | component | optional awaited `Func<OfflineTranscriptSegment, CancellationToken, Task>? onSegment` so a caller persists each segment before more audio is consumed (`null` is byte-identical to Phase 2) | **DONE (Phase 4)** |
| `Core/Transcription/FileTranscriptionService.cs` (modified) | component | **incremental persistence** (create/reuse the session, append a `segments` row per segment) and **resume** from the committed `segments` rows | **DONE (Phase 4)** |
| `Core/Models/MediaModels.cs` (modified) | models | `MediaDecodeRequest.StartOffset` (`TimeSpan`) | **DONE (Phase 4)** |
| `Media/FFmpegMediaDecodeService.cs` (modified) | component | input seeking (`-accurate_seek` + `-ss <seconds>` **before** `-i`); emitted `PcmBlock.Start` stays absolute | **DONE (Phase 4)** |
| `App/App.xaml.cs` (modified) | composition root | registers `IMediaFileRepository`, `ITranscriptionJobRepository`, `ITranscriptionJobService`; `ResolveJobRequestAsync` builds a fresh recognizer per attempt; `RecoverUnfinishedAsync()` at start | **DONE (Phase 4)** |
| `tools/FileTranscribe` (modified) | tool | `--jobs [--file] [--db] [--interrupt-after N] [--resume-job <id>] [--list-jobs]` | **DONE (Phase 4)** |

## 4. Media data flow (Phase 1, implemented)

```
 local media file (audio or video; any path, incl. Unicode + spaces)
        |  IMediaDecodeService.ProbeAsync  ->  ffprobe -v error -print_format json -show_format -show_streams <file>
        |                                     -> MediaInfo { Kind, ContainerFormat, Duration, AudioStreams[] }
        |  (a file with no audio stream -> MediaDecodeException(NoAudioTrack))
        v
 IMediaDecodeService.DecodeAsync(MediaDecodeRequest{Path, AudioStreamIndex, TargetSampleRate})
        |  ffmpeg -v error -i <file> -map 0:a:<N> -vn -sn -dn -f f32le -acodec pcm_f32le -ac 1 -ar 16000 pipe:1
        |  all arguments passed via ProcessStartInfo.ArgumentList (no shell)
        v
 streamed stdout  ->  reassembled into 1-second (16 000-sample) PcmBlocks with absolute Start times
        |  (the whole file is never buffered; a bounded channel provides back-pressure; cancel kills the tree)
        v
 PcmBlock stream  ->  [Phase 2 DONE: segment / VAD -> OfflineTranscriptionEngine -> timestamped segments]
                  ->  [Phase 3 DONE: diarization -> alignment]  ->  [role-tagged dialogue]  ->  [Phase 6: export]
```

Decode details confirmed in the code and in the integration tests:

- **Probe:** `ffprobe … -print_format json -show_format -show_streams`. Cover-art streams (a "video"
  stream with `attached_pic`) are excluded from `MediaKind.Video` detection.
- **Decode:** `-map 0:a:<N>` selects the ordinal among the file's audio streams; `-vn -sn -dn`
  drop video/subtitle/data; `-f f32le -acodec pcm_f32le -ac 1 -ar 16000` produces the recognizer's
  exact input. `AudioStreamIndex` out of range → `MediaDecodeException(StreamIndexOutOfRange)`.
- **Streaming:** output blocks are one second (`BlockSamples = 16000`) with absolute start offsets;
  reading uses a 64 KiB buffer and carries any partial float32 to the next pass. The whole file is
  never buffered.
- **Injection safety:** `ProcessRunner` uses only `ProcessStartInfo.ArgumentList` with
  `UseShellExecute=false` — never a shell, never string concatenation — so Unicode, spaces and long
  paths are safe (verified by a Unicode-with-space decode test).

## 5. SQLite migration 5 (Phase 4 — DONE)

The database schema is now at **migration 5** (`file transcription job schema`), purely additive in
the same style as migration 4 (lower-case table names, `CREATE TABLE IF NOT EXISTS`, **no foreign
keys**). The transcript still **reuses** the existing `segments` table and the speaker reference still
reuses `speaker_assignments`; migration 5 only adds the file/job/orchestration layer that had no V0.4
equivalent.

| Table | Columns | Purpose |
| --- | --- | --- |
| `media_files` | `MediaFileId` TEXT PK, `Path`, `FileName`, `Kind` INTEGER, `ContainerFormat`, `DurationMs` INTEGER, `SizeBytes` INTEGER, `AudioStreamIndex` INTEGER NULL, `CreatedAt` TEXT | one imported media file: path, kind, container, duration, size, audio-stream selection, probe result |
| `transcription_jobs` | `JobId` TEXT PK, `MediaFileId`, `SessionId` TEXT NULL, `Title`, `Mode` INTEGER, `ModelId`, `AudioStreamIndex` INTEGER NULL, `RunDiarization` INTEGER, `DiarizationCountMode` INTEGER, `ManualSpeakerCount` INTEGER, `ClusteringThreshold` REAL, `Status` INTEGER, `Phase` INTEGER, `ProcessedMs` INTEGER, `TotalMs` INTEGER, `SegmentsEmitted` INTEGER, `QueuedAt`, `StartedAt` NULL, `FinishedAt` NULL, `Attempts` INTEGER, `ResumeCount` INTEGER, `Error` NULL, `Warning` NULL | one file-transcription job: state, phase, progress snapshot, model/options, timings, attempts/resumes, error |

Plus an index `ix_transcription_jobs_queue (Status, QueuedAt)` — the queue is read in `Status` then
`QueuedAt` order.

**Deliberately NOT created** (diverging from the table list this section previously pre-declared):

- `transcription_chunks` — the VAD is a **streaming state machine** with no upfront chunk plan, so
  there is no chunk work-unit to persist.
- `transcript_segments` — the transcript **reuses the existing `segments` table**, and the speaker
  reference reuses `speaker_assignments` (no parallel table).
- `job_checkpoints` — **the resume cursor is derived from the committed `segments` rows**
  (`MAX(EndOffsetMs)`, `MAX(SequenceNumber)+1`), so it can never drift ahead of the data that actually
  exists. `ProcessedMs` / `SegmentsEmitted` on the job row are only a **throttled display snapshot**
  (written at most every 2 s) and are **never** used to resume.

> See [`DECISIONS.md`](DECISIONS.md) D17 for why the cursor is derived rather than stored.

## 6. Segmented offline ASR (Phase 2 — DONE) → role-tagged dialogue (Phase 3 — DONE)

```
 decoded 16 kHz PCM (Phase 1, done)
        |  Phase 2 (DONE): VAD / energy segmentation  ->  SpeechSegment (absolute start/end samples)
        v
 Phase 2 (DONE): OfflineTranscriptionEngine — one IAsrSession per region:
        |    Reset() -> AcceptWaveform -> InputFinished -> while (IsReady()) Decode() -> GetResult()
        |    + optional correction function
        |    + trim leading silence (reported start = speech onset)
        |    + clamp a carried-over start to the previous segment's end (timeline never overlaps)
        |    + append terminal punctuation for models that emit none
        |    + flush the final partial segment at end of stream
        v
 OfflineTranscriptionResult  (segments with global Start/End + Text, AudioDuration, Elapsed, Rtf, …)
        |  Phase 3 (DONE): reuse ISpeakerDiarizationService (V0.4)  ->  raw speaker intervals + assignments
        v
 Phase 3 (DONE): ITranscriptAlignmentService.Align(segments, assignments, speakers)  ->  one speaker per segment
        v
 role-tagged dialogue  (turn = {speaker, start, end, text})  ->  Phase 5 editor  ->  Phase 6 export
```

This mirrors the V0.4 post-meeting diarization flow in [`ARCHITECTURE.md` §2](ARCHITECTURE.md)
(`SpeakerDiarizationService` → `SpeakerAlignmentService`), differing only in that the ASR runs over a
decoded file instead of the live capture.

### 6.1 `OfflineTranscriptionEngine` (new, `Core/Transcription/`)

`TranscribeAsync(IAsyncEnumerable<PcmBlock> blocks, IProgress<OfflineTranscriptionProgress>?, TimeSpan? totalDuration, CancellationToken)` → `OfflineTranscriptionResult`.

- **Streams**, never buffers a whole file: it consumes the `PcmBlock` stream directly (the Phase 1 decoder already yields 1-second blocks).
- Runs the **VAD/segmenter** over the samples and decodes **each region with one `IAsrSession`**: `Reset()` → `AcceptWaveform` → `InputFinished` → `while (IsReady()) Decode()` → `GetResult()`.
- Applies an **optional correction function** to the recognised text.
- **Timestamps:** trims the leading silence so the reported `Start` is the speech onset; clamps a carried-over start to the **previous segment's `End`** so the timeline never overlaps; absolute times come from the **first `PcmBlock.Start` plus the cumulative sample index**.
- Appends **terminal punctuation** for models that emit none (`OfflineTranscriptionOptions.AppendTerminalPunctuation` / `TerminalPunctuation`); flushes the **final partial segment** at end of stream.
- Catches `OperationCanceledException` / `MediaDecodeException(Cancelled)` and returns `Cancelled=true`.
- Returns everything **in memory** — no persistence in the engine (see §5).

### 6.2 Segmentation — `SpeechSegment` + `AudioSegmenter` changes

- New `Core/Audio/SpeechSegment.cs`: `readonly record struct SpeechSegment(float[] Samples, long StartSample, long EndSample, bool HasOverlapPrefix)` (plus `Length`). The bounds are **absolute** sample indices into the whole stream, not per-buffer.
- `AudioSegmenter` gained **absolute-sample bookkeeping** and an optional **last** constructor parameter `double overlapSeconds = 0.0` (clamped to `maxSegmentSamples/2`).
- New `PushSegments` / `FlushSegment` return `SpeechSegment`; the old `Push` / `Flush` are **re-implemented on top of them with unchanged output** (so the live path is untouched).
- **Overlap is introduced only on a max-length cut** (and only then): the trailing `overlap` samples are retained into the next buffer, and that next region is flagged `HasOverlapPrefix`.

### 6.3 Three modes — `TranscriptionModeCatalog` (new, `Asr/`)

`TranscriptionModeCatalog.Resolve(mode, IModelManager, hotwordsFile, numThreads)` → `ResolvedTranscriptionMode(Mode, DisplayName, Description, Descriptor, EngineOptions, TranscriptionOptions, IsAvailable, UnavailableReason)`. The three modes are **real, distinct configurations**:

| Mode | Label | Model | Decoding | Model-level hotwords | Segmentation |
| --- | --- | --- | --- | --- | --- |
| `Fast` | 快速 | `streaming-zipformer-zh-14M` | `greedy_search` | off | VAD, max 15 s, overlap 0, appends `。` |
| `Standard` | 标准 | `streaming-zipformer-zh-14M` | `modified_beam_search` | on (hotwords file) | VAD, max 20 s, overlap 0, appends `。` |
| `HighAccuracy` | 高精度 | `sense-voice-small-int8` (offline) | `greedy_search` (only option) | **not supported** | VAD, max 30 s, **overlap 1.5 s** + dedup, ITN punctuation |

The catalog is **data-driven and fails soft**: it never throws for an unavailable mode — it sets `IsAvailable=false` and fills `UnavailableReason` (e.g. the SenseVoice model is not installed) so a caller can degrade gracefully (see [`DECISIONS.md`](DECISIONS.md) D11).

### 6.4 Overlap de-duplication — `OverlapTextDeduplicator` (new)

When consecutive regions overlap in audio (High-accuracy mode uses a 1.5 s overlap), their text may repeat. `OverlapTextDeduplicator.Apply(previousText, currentText, minOverlapChars)` → `Result(Text, TrimmedChars, Dropped)`:

- **Folds both texts first** (reuses `SubtitleAccumulator.Fold`: strips whitespace/punctuation/symbols and lowercases) before comparing.
- Finds the **longest suffix-of-previous == prefix-of-current** run and trims it from the **later** text when it reaches `minOverlapChars` (default 3, `OfflineTranscriptionOptions.MinOverlapChars`).
- If the **whole** current text repeats the previous one, it is **dropped** instead.
- **Only the later text is ever modified, and it can never be emptied** — a full repetition becomes `Dropped`, not an empty segment.

### 6.5 Role-tagged dialogue (Phase 3 — DONE)

The file job runs one file **end to end** through `FileTranscriptionService` (`IFileTranscriptionService`). Its
order is deliberate and strict:

1. **Admission** — a `SemaphoreSlim(1, 1)` admits a single job; a second concurrent job is rejected with
   an error result (no queuing yet — that is Phase 4).
2. **Probe** — `IMediaDecodeService.ProbeAsync` reads the container/streams.
3. **Decode + tee** — the streamed decode (`IMediaDecodeService.DecodeAsync`) is **tee'd in the same pass**
   into a temporary 16 kHz mono WAV via `PcmWavWriter`. The ASR still streams — the file is never buffered
   in memory. A length check compares the written WAV duration against the transcribed audio duration and
   warns on divergence.
4. **ASR** — the decoded blocks go straight into the Phase 2 `OfflineTranscriptionEngine`.
5. **Persist (one session, one `segments` row per transcript segment)** — one `MeetingSession` plus one
   `segments` row per segment, capturing the assigned **`SegmentId` at append time** (never trusting
   `SourceChunkId`, which skips empty chunks).
6. **Diarize** — `SpeakerDiarizationService.RunAsync` (V0.4) runs over the temporary WAV, **reused
   unchanged** (see [`DECISIONS.md`](DECISIONS.md) D14).
7. **Read back assignments** — `GetAssignmentsAsync` + `GetSpeakersAsync` from the repository.
8. **Align** — `ITranscriptAlignmentService.Align(sessionId, facts, assignments, speakers, options?)`
   → `DialogueTranscript`.

**Why a temporary WAV.** The V0.4 diarizer is a **whole-file** engine that takes a **file path** and loads
it through `IAudioFileLoader` (NAudio). NAudio cannot read video containers (`.mp4/.mkv/.mov`), and the file
job must support video. Since FFmpeg already decodes the file, the job writes its own 16 kHz mono WAV and
reuses `SpeakerDiarizationService` unchanged. This avoids touching tested V0.4 code, avoids duplicating the
run/speaker/interval/assignment persistence, supports video, and keeps the ASR path streaming. Rejected
alternatives: buffering the whole decoded file in memory (4 h @16 kHz float32 ≈ 920 MB) and adding a
samples-based overload to `ISpeakerDiarizationService`.

The temp WAV lives in a **caller-supplied staging directory** (the app passes
`LocalDataPaths.RecordingsDirectory`, so the existing 24 h orphan sweep covers a crash) and is deleted in
`finally`. The caller owns `request.Engine` (never disposed). Diarization is a **soft failure**: busy or
failed → a `Warning`, `Diarized=false`, an all-unknown dialogue, and **never an exception**.

**Dialogue assembly is from the persisted `speaker_assignments`, not a second alignment pass.** The
diarizer's raw-cluster-index → speaker-id mapping is only built transiently inside
`SpeakerDiarizationService` and is not persisted, whereas `speaker_assignments` already holds the final
chosen speaker (plus `NeedsConfirmation` / `Confidence`) per segment id (see
[`DECISIONS.md`](DECISIONS.md) D15).

**The pure assembler — `TranscriptAlignmentService`.** It is pure and **idempotent** (so the Phase 5
editor can re-assemble). It merges consecutive segments by the same speaker into one turn and starts a
**new turn** on:

- a **speaker change**,
- a **gap** between consecutive segments greater than `DialogueAssemblyOptions.MaxGap` (**default 2 s**), or
- the turn's text exceeding `DialogueAssemblyOptions.MaxTurnChars` (**default 500**).

A turn is the **first `Start`**, the **last `End`**, the **concatenated text**, the **constituent segment
ids**, and an **OR-propagated `NeedsConfirmation`**. Segments whose assignment is missing or whose
`SpeakerId` is null become an **unknown turn** (`SpeakerId == null`, label `未知发言人`, grey
`0xFF808080`) and are **excluded from `Participants`**. `Participants` aggregate speaking time / turn
count / segment count over **known** speakers, ordered by speaking time descending (see
[`DECISIONS.md`](DECISIONS.md) D16).

### 6.6 Job queue + checkpoint/resume (Phase 4 — DONE)

A file transcription now runs as a **background job** through `TranscriptionJobService`
(`Core/Transcription/`), so several files can be queued and an interrupted job can be resumed.

```
enqueue (TranscriptionJobRequest + MediaInfo)
        |  writes media_files + transcription_jobs rows; status = Queued   (enqueuing only writes rows)
        v
one FIFO worker on a dedicated Thread { IsBackground, Priority = BelowNormal, Name = "file-transcription-worker" }
        |  dequeues the oldest runnable job (Status, QueuedAt) -> status = Running
        v
TranscriptionJobResolver(job, ct)  ->  a fresh FileTranscriptionRequest  (rebuilt from the stored row)
        |  a fresh recognizer per attempt; the job service disposes the engine it is given
        v
FileTranscriptionService.RunAsync(request)  ->  incremental persistence
        |  probe -> create (or reuse request.SessionId) the MeetingSession
        |  decode -> ASR, persisting one segments row per produced segment BEFORE more audio is consumed
        v
success -> Succeeded / session Completed;  cancel -> Cancelled / session Paused;
decode failure -> Failed / session Aborted
```

**Enqueuing only writes rows.** Every run rebuilds its `FileTranscriptionRequest` from the stored job
through the injected `TranscriptionJobResolver`, so a job **survives a restart** — the resolver
resolves the mode from the job row (via `TranscriptionModeCatalog`), creates and initializes a
**fresh recognizer per attempt** (the job service disposes the engine it is given), and passes the
job's `SessionId`. `App.xaml.cs` wires the same seam (`ResolveJobRequestAsync`), and
`StartInitializationAsync` calls `RecoverUnfinishedAsync()`.

**Incremental persistence (the resume seam).** `OfflineTranscriptionEngine.TranscribeAsync` gained an
optional **awaited** `Func<OfflineTranscriptSegment, CancellationToken, Task>? onSegment` so the
caller can persist each segment **before more audio is consumed** (passing `null` is byte-identical to
the Phase 2 behaviour). `FileTranscriptionService` uses it to append **one `segments` row per produced
segment**; after a successful probe it creates the `MeetingSession`, or, when
`FileTranscriptionRequest.SessionId` is set, **reuses** it.

**The resume cursor is derived, never stored.** On a resume the service derives
`resumeFrom = MAX(EndOffsetMs)` and `startSequence = MAX(SequenceNumber)+1` from the **committed
`segments` rows**, seeks the decode there, and sets the session back to `Recording`. Because the cursor
is computed from the data that actually exists, it can never run ahead of it. `ProcessedMs` /
`SegmentsEmitted` on the job row are a throttled (≥ 2 s) **display** snapshot only — **never** the
cursor (see [`DECISIONS.md`](DECISIONS.md) D17/D18).

**Seeking — `-ss` before `-i`.** `MediaDecodeRequest` gained `TimeSpan StartOffset`; when it is
non-zero, `FFmpegMediaDecodeService` inserts `-accurate_seek` and `-ss <seconds>` **before** `-i`
(*input* seeking: O(1) instead of decoding and discarding the prefix) and offsets every emitted
`PcmBlock.Start` by it, so positions stay **absolute** on the media timeline. The temporary staging WAV
for diarization is only tee'd when the run starts at offset zero; a **resumed** run rebuilds the
whole-file WAV first with one extra decode-only pass (see D19).

**Cancel / resume / recover / drain.** `CancelAsync` is **cooperative** (a queued job is marked
`Cancelled` immediately; the caller/composition root owns the recognizer). `ResumeAsync` re-queues the
job — the file service resumes from committed data automatically. `RecoverUnfinishedAsync` marks jobs
left `Running` by a previous session as `Interrupted`. `DrainAsync` processes the queue
**synchronously** (tests/CLI). Queue order is FIFO on `QueuedAt`, made **strictly increasing** on
enqueue (`NextQueuedAt`) so several same-tick enqueues still order deterministically. Phase 4 ships
**no UI** (Phase 5 owns the dialogue editor); the queue is exercised through `tools/FileTranscribe
--jobs` and the tests.

## 7. Threading / resource rules

The prime rule: **file transcription must never starve live ASR.**

- **One queue, one worker thread, below-normal priority.** `TranscriptionJobService` owns a single
  **FIFO queue** and runs **one job at a time** on a dedicated
  `Thread { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "file-transcription-worker" }`,
  created lazily by `StartAsync` — the same pattern V0.4 uses for diarization. A job enqueued while
  another runs waits in the queue rather than running concurrently.
- **Thread-continuation caveat (honest).** The worker thread is created with
  `Priority = BelowNormal`, but **after the first real `await` inside a run the continuations execute
  on the thread pool**, so the priority hint mainly covers process start-up; the queued work is **not**
  guaranteed to stay at below-normal priority end-to-end. The real isolation from live ASR is
  structural — **one job at a time**, low thread caps, and **separate ffmpeg child processes**. Treat
  the "below-normal priority" claim as **aspirational**, not a measured guarantee.
- **Low thread cap.** Decode children (ffmpeg/ffprobe) and any offline inference use a small,
  bounded CPU-thread budget (the V0.4 diarizer already clamps to `min(ProcessorCount/4, 4)`; the ASR
  auto-thread policy caps at 4). The OS scheduler therefore preempts a file job in favour of live
  recognition.
- **Never on the live path.** The file path reads a file and calls FFmpeg directly; it never
  enqueues into the live `BoundedAudioQueue` and never touches the capture buffer. A slow file job
  cannot grow the live queue.
- **Bounded memory.** Decode streams 1-second blocks through a bounded channel (capacity 4); the
  whole file is never buffered, so a multi-hour recording does not blow up the working set.
- **Cancellation kills the process tree.** `ProcessRunner` registers the cancellation token and
  kills the entire child process tree, so an abandoned ffmpeg does not linger.

> The concurrency of a live transcription and a file job **has not been measured** — the design above
> is the isolation mechanism; the measurement is BLOCKED on target hardware.

## 8. Phase plan

| Phase | Scope | Status |
| --- | --- | --- |
| **0** | FFmpeg tooling + licensing (`tools/fetch-ffmpeg.ps1`, LGPL build pinned by SHA-256; see [`LICENSES.md`](LICENSES.md)) | **DONE** |
| **1** | Media decode layer: `IMediaDecodeService`, `LocalMeetingSubtitle.Media` (probe + streaming PCM), DI registration, integration tests | **DONE** |
| **2** | Segmented long-audio offline ASR (VAD / energy segmentation + per-segment sherpa-onnx offline decode + global timestamps + three-mode catalog; see §6) | **DONE** |
| 3 | Role-tagged dialogue: wire `ISpeakerDiarizationService` + new `ITranscriptAlignmentService` for file jobs (`IFileTranscriptionService` / `FileTranscriptionService`, `PcmWavWriter`, `DialogueModels`; see §6.5) | **DONE** |
| 4 | Job queue + checkpoints/resume (`ITranscriptionJobService`, migration 5) | **DONE** |
| 5 | Dialogue editor UI (rename/merge/reassign turns) | **NOT_STARTED** |
| 6 | Export for file jobs: TXT / Markdown / CSV / SRT / **DOCX** | **NOT_STARTED** |
| 7 | UX / performance polish | **NOT_STARTED** |
| 8 | Regression + release (bundle FFmpeg into the installer/ZIP) | **NOT_STARTED** |

## 9. What is verified vs. not

**Verified on the dev host (Phase 0–4):**

- FFmpeg provisioned as an **LGPL v3** build, pinned by URL + SHA-256, redistributable as a separate
  program (see [`LICENSES.md`](LICENSES.md)).
- Real media **probes correctly**: `0.mp3/m4a/aac/flac/ogg/mkv/mov/avi` all report ≥1 audio stream
  and a valid ~5.6 s duration; `noaudio.mp4` reports `HasAudio=false` and `MediaKind.Video`.
- Real media **decodes to 16 kHz mono float32** and the audio transcodes to real Chinese:
  `0.mp4` → `89784` samples = `5.61 s` → `对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢`
  (same shape for `0.mkv` 5.63 s, `0.ogg` 5.63 s, `0.mp3` 5.64 s).
- **Multi-track** selection works (`two-tracks.mp4` index 1 decodes; index 9 →
  `StreamIndexOutOfRange`), a **video-only** file errors cleanly (`NoAudioTrack`), and a
  **Unicode-with-space** path decodes.
- **Segmented long-audio offline ASR works (Phase 2):** `tools/FileTranscribe` on a 5.612 s clip
  produces a timestamped segment in all three modes (Fast / Standard / High); High-accuracy mode on
  the **65.115 s** clip yields **10 monotonic, non-overlapping segments**, and on the **56.115 s
  continuous** clip yields **2 segments** where the 30 s cap fired and the second region genuinely
  restarted at `28.500 s` (= `30.000 − 1.500`), with the carried-over text **not** duplicated.
- **Three modes / engine / de-dup** are covered by new tests (`OfflineSegmenterTimingTests`,
  `OverlapTextDeduplicatorTests`, `OfflineTranscriptionEngineTests`, `TranscriptionModeCatalogTests`
  in unit; `FileTranscriptionTests` in integration).
- **Role-tagged dialogue works (Phase 3):** `tools/FileTranscribe --diarize` on
  `testmedia/two-speakers.wav` (**451,262 bytes ≈ 14.1 s**; speaker A ×2, gap, speaker B ×2) produced
  **2 speakers, 6/6 assigned, 0 need confirmation**, **6 segments → 2 turns** (`A` merged its two
  segments; `B` merged its four), with `PARTICIPANTS=2 TURNS=2`, `RTF=0.2281`, `DIARIZED=True`.
  Diarization ran over the temp 16 kHz mono WAV (the same tee path that makes video diarizable); after
  the run no `dijob-*.wav`, no `ft-*.db`/`-wal`/`-shm`, and no leftover staging WAV remained.
- **Phase 3 tests:** unit **+18** (`TranscriptAlignmentServiceTests`, `FileTranscriptionServiceTests`,
  plus shared doubles in `TestDoubles.cs`); integration **+2** (`FileTranscriptionDiarizationTests` —
  a real two-speaker file producing a 2-participant dialogue, and a video file `testmedia/two-tracks.mp4`
  audio track 1 proving the temp-WAV tee makes video diarizable).
- **Resumable job queue works (Phase 4):** `tools/FileTranscribe --jobs` on `testmedia/two-speakers.wav`
  (High + `--diarize`) with a persistent `--db` was run **twice**: run 1 (`--interrupt-after 2`)
  enqueued the job and deliberately cancelled it after **2 persisted segments**
  (`STATUS=Queued` → `INTENTIONAL-INTERRUPT after 2 persisted segment(s)` →
  `Cancelled segments=2 processed=0.0/14.1s attempts=1 resumes=0`); run 2 (`--resume-job <id>`) resumed
  the **same session** — `Resuming session … at 4.540s (sequence 2)`, diarization ran over the whole
  14.1 s file (`225592 samples`), and the job finished
  `Succeeded segments=4 processed=14.1/14.1s attempts=2 resumes=1`. The session ended with **6
  contiguous segments (0..5)** whose timeline matches an uninterrupted run (see §6.6 and the caveat
  below).
- **Phase 4 tests:** unit **208 → 222** (+14: `SqliteTranscriptionJobRepositoryTests`,
  `TranscriptionJobServiceTests`, a resume test in `FileTranscriptionServiceTests`, plus shared
  doubles); integration **43 → 46** (+3: `FileTranscriptionResumeTests` interrupt→resume, and
  `MediaDecodeOffsetTests` for the seek).
- **No regression**: unit **222**, integration **46**, performance 3 (+1 skipped), build 0 errors —
  the existing live-subtitle suites are unaffected; `MainViewModel` is untouched.
- Full detail and commands: [`FILE_TRANSCRIPTION_TEST_REPORT.md`](FILE_TRANSCRIPTION_TEST_REPORT.md).

**NOT verified / NOT_STARTED (do not treat as done):**

- **Phases 5–8 do not exist.** There is no DOCX export, no dialogue editor, no drag-drop UI, no player,
  and no progress UI. (Phases 0–4 exist — the job queue and migration 5 are built — but the file job is
  still **not wired to any UI**; it is exercised only through `tools/FileTranscribe` and the tests.)
- **Phase 4 caveats (verified, but limited):**
  - **Boundary segment text can differ (OPEN, P2).** The first segment produced immediately after a
    resume can be transcribed slightly differently from an uninterrupted run, because the VAD/recognizer
    restarts there with different leading context. Observed on the two-speaker fixture: the uninterrupted
    run produced `四次班年度演。` for that window while the resumed run produced `年度演。`. Segment
    **count, ordering, sequence numbers and timing are preserved** and nothing is duplicated — the
    transcript is **structurally identical**, but **not** textually identical (every segment except the
    one at the resume boundary matches exactly).
  - **mp3 seeking is not sample-exact** (decoder delay, tens of ms). WAV/PCM seeking is bit-exact
    (verified: mean sample difference < 1e-6); the emitted block positions stay absolute either way.
  - **The below-normal thread priority is aspirational** (see §7): after the first `await`, continuations
    run on the thread pool; the real isolation is one-job-at-a-time + low thread caps + separate ffmpeg
    child processes.
  - **A resumed job performs one extra decode-only pass** to rebuild the whole-file staging WAV for
    diarization (a fresh job needs no extra pass).
  - **`ProcessedMs` is a throttled (≥ 2 s) display snapshot**; the resume cursor is always the committed
    `segments` data, never that field.
  - **No UI** for file transcription yet (Phase 5 owns the dialogue editor);
    `AppSettings.EnableVadSegmenting` remains unwired (P3-2). The isolation of a **concurrent** live + file
    run is still **not measured** (BLOCKED on target hardware).
- **Long-audio accuracy is unmeasured** — there is **no reference transcript**, so CER/WER is
  `NOT_TESTED`; only 56 s and 65 s fixtures exist (no > 1 h file).
- **Diarization is a whole-file operation.** The V0.4 diarizer caps at **4 h** and
  `NaudioAudioFileLoader.LoadMono` builds a `List<float>` then `.ToArray()` (~1.8 GB transient at 4 h),
  so files **> 2 h** are treated as risky — **not** tested beyond the 14.1 s two-speaker fixture
  (see [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md)).
- **No UI entry.** There is still no UI entry for file transcription, and `AppSettings.EnableVadSegmenting`
  remains unwired. `SubtitleSegment.SpeakerName` stays transient; Phase 3 changes no persistence and no
  export formatter (Phase 6 owns dialogue export).
- **File decode performance** (throughput on long files, memory on multi-hour audio) is unmeasured.
- **Target-hardware acceptance** (Windows 11 + Core Ultra 7 155H: CPU, memory, latency, and the live
  transcript **plus** a concurrent file job) is `BLOCKED` / `NOT_TESTED` — the target machine is not
  available.
- **FFmpeg is not bundled** into the installer or portable ZIP yet (Phase 8).

See [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) (matrix FT-01 .. FT-25) for the per-item status.
