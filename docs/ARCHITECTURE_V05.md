# Architecture — V0.5: offline file transcription + role-tagged dialogue

> **Status: work in progress.** **Phase 0** (FFmpeg tooling + licensing), **Phase 1** (media decode
> layer) and **Phase 2** (segmented long-audio offline ASR) are implemented and verified;
> **Phases 3–8 are NOT_STARTED**. All target-hardware acceptance (Windows 11 + Core Ultra 7 155H) is
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
| **File transcription** (new) | User selects a local media file → FFmpeg decodes it to 16 kHz mono PCM → VAD segmentation → per-segment offline ASR → diarization → role-tagged dialogue → export. Works on *an already-recorded file*. | **Phase 0–2 done.** Media decode and segmented long-audio offline ASR (timestamped segments, three modes) are done; diarization/alignment and everything downstream are not started. |

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
| `LocalMeetingSubtitle.Storage` | existing project | SQLite (WAL) + repositories (schema is still at **migration 4**) | unchanged |
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
| `ITranscriptionJobService` | contract (planned) | own a file-transcription **job**: queue, progress, cancel, checkpoint/resume, status | **NOT_STARTED (Phase 4)** |
| `ITranscriptAlignmentService` | contract (planned) | align the offline transcript's segments with diarization intervals into a role-tagged dialogue | **NOT_STARTED (Phase 3)** |
| `ISpeakerDiarizationService` (from V0.4) | **reused** | run offline speaker diarization over decoded audio | reuse; file-job wiring **NOT_STARTED (Phase 3)** |

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
                  ->  [Phase 3: diarization -> alignment]  ->  [role-tagged dialogue]  ->  [Phase 6: export]
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

## 5. SQLite migration 5 (deferred to Phase 4 — NOT_STARTED)

The database schema is **still at migration 4** (V0.4 speaker diarization). **Phase 2 changed no
schema:** `OfflineTranscriptionEngine` returns results **in memory**, and a file transcript can
already be persisted today by creating **one `MeetingSession`** and appending **one `segments` row per
result** through the existing `SqliteSubtitleRepository` (`StartOffsetMs` / `EndOffsetMs`). V0.5 will
add a **migration 5**, purely additive in the same style as migration 4, for the file-transcription
job/queue/checkpoint model — **deferred to Phase 4**. **None of these tables exist yet.**

| Table (planned) | Purpose |
| --- | --- |
| `MediaFile` | one imported media file: path, kind, container, duration, size, audio-stream selection, probe result |
| `TranscriptionJob` | one file-transcription job: state (queued/running/done/failed/cancelled), progress, timings, model/options, error |
| `TranscriptionChunk` | per-chunk decode/ASR work unit (offset, sample range, status) — the basis for checkpoint/resume |
| `TranscriptSegment` | one transcribed segment with global timestamps (start/end), text, speaker reference |
| `JobCheckpoint` | resumable state of a job (last completed chunk, offsets) for restart-after-crash |

> The file-job transcript is expected to **reuse** the existing `segments`/`speakers`/
> `speaker_assignments` tables where the data is the same shape; the migration-5 tables above are the
> job/orchestration layer that has no V0.4 equivalent. The exact split is a Phase 4 design decision
> and is not fixed here.

## 6. Segmented offline ASR (Phase 2 — DONE) → role-tagged dialogue (Phase 3 — NOT_STARTED)

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
        |  Phase 3 (NOT_STARTED): reuse ISpeakerDiarizationService (V0.4)  ->  raw speaker intervals
        v
 Phase 3 (NOT_STARTED): ITranscriptAlignmentService.Align(segments, intervals)  ->  one speaker per segment
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

## 7. Threading / resource rules

The prime rule: **file transcription must never starve live ASR.**

- **Separate thread, below-normal priority.** The whole file job (probe → decode → ASR →
  diarization) runs off the UI thread and off the capture/ASR worker threads, on a dedicated thread
  with `Priority = BelowNormal` — the same pattern V0.4 uses for diarization.
- **One job at a time.** A `SemaphoreSlim(1, 1)` admits a single file job; jobs queue (Phase 4)
  rather than run concurrently.
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
| 3 | Role-tagged dialogue: wire `ISpeakerDiarizationService` + new `ITranscriptAlignmentService` for file jobs | **NOT_STARTED** |
| 4 | Job queue + checkpoints/resume (`ITranscriptionJobService`, migration 5) | **NOT_STARTED** |
| 5 | Dialogue editor UI (rename/merge/reassign turns) | **NOT_STARTED** |
| 6 | Export for file jobs: TXT / Markdown / CSV / SRT / **DOCX** | **NOT_STARTED** |
| 7 | UX / performance polish | **NOT_STARTED** |
| 8 | Regression + release (bundle FFmpeg into the installer/ZIP) | **NOT_STARTED** |

## 9. What is verified vs. not

**Verified on the dev host (Phase 0–2):**

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
- **No regression**: unit **190**, integration **41**, performance 3 (+1 skipped), build 0 errors —
  the existing live-subtitle suites are unaffected.
- Full detail and commands: [`FILE_TRANSCRIPTION_TEST_REPORT.md`](FILE_TRANSCRIPTION_TEST_REPORT.md).

**NOT verified / NOT_STARTED (do not treat as done):**

- **Phases 3–8 do not exist.** There is no `ITranscriptAlignmentService`, no role-tagged dialogue, no
  `ITranscriptionJobService`, no migration-5 tables, no DOCX, no dialogue editor, no drag-drop UI, no
  player. (Phase 2 exists but returns results **in memory only** — no job/queue persistence.)
- **Long-audio accuracy is unmeasured** — there is **no reference transcript**, so CER/WER is
  `NOT_TESTED`; only 56 s and 65 s fixtures exist (no > 1 h file).
- **File decode performance** (throughput on long files, memory on multi-hour audio) is unmeasured.
- **Target-hardware acceptance** (Windows 11 + Core Ultra 7 155H: CPU, memory, latency, and the live
  transcript **plus** a concurrent file job) is `BLOCKED` / `NOT_TESTED` — the target machine is not
  available.
- **FFmpeg is not bundled** into the installer or portable ZIP yet (Phase 8).

See [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) (matrix FT-01 .. FT-25) for the per-item status.
