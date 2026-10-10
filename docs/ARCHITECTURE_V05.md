# Architecture — V0.5: offline file transcription + role-tagged dialogue

> **Status: work in progress.** Only **Phase 0** (FFmpeg tooling + licensing) and **Phase 1**
> (media decode layer) are implemented and verified; **Phases 2–8 are NOT_STARTED**. All
> target-hardware acceptance (Windows 11 + Core Ultra 7 155H) is **BLOCKED / NOT_TESTED** because
> that machine is not available — every fact below was measured on the Windows 10 dev host.
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
| **File transcription** (new) | User selects a local media file → FFmpeg decodes it to 16 kHz mono PCM → offline ASR → diarization → role-tagged dialogue → export. Works on *an already-recorded file*. | **Phase 0–1 only.** Media decode is done; everything downstream is not started. |

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
 PcmBlock stream  ->  [Phase 2: segment / VAD -> offline ASR]  ->  [Phase 3: diarization -> alignment]
                  ->  [role-tagged dialogue]  ->  [Phase 6: export]
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

## 5. Planned SQLite migration 5 (NOT_STARTED)

The database schema is **still at migration 4** (V0.4 speaker diarization). V0.5 will add a
**migration 5**, purely additive in the same style as migration 4, for the file-transcription job
model. **None of these tables exist yet.**

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

## 6. Planned role-tagged dialogue pipeline (Phase 2–3, NOT_STARTED)

```
 decoded 16 kHz PCM (Phase 1, done)
        |  Phase 2: energy/VAD segmentation of long audio  ->  utterance segments
        v
 Phase 2: offline ASR per segment (sherpa-onnx offline model)  ->  segment text + global timestamps
        |  Phase 3: reuse ISpeakerDiarizationService (V0.4)     ->  raw speaker intervals
        v
 Phase 3: ITranscriptAlignmentService.Align(segments, intervals)  ->  one speaker per segment
        v
 role-tagged dialogue  (turn = {speaker, start, end, text})  ->  Phase 5 editor  ->  Phase 6 export
```

This mirrors the V0.4 post-meeting diarization flow in [`ARCHITECTURE.md` §2](ARCHITECTURE.md)
(`SpeakerDiarizationService` → `SpeakerAlignmentService`), differing only in that the ASR runs over a
decoded file instead of the live capture.

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
| 2 | Segmented long-audio offline ASR (VAD / energy segmentation + per-segment sherpa-onnx offline decode) | **NOT_STARTED** |
| 3 | Role-tagged dialogue: wire `ISpeakerDiarizationService` + new `ITranscriptAlignmentService` for file jobs | **NOT_STARTED** |
| 4 | Job queue + checkpoints/resume (`ITranscriptionJobService`, migration 5) | **NOT_STARTED** |
| 5 | Dialogue editor UI (rename/merge/reassign turns) | **NOT_STARTED** |
| 6 | Export for file jobs: TXT / Markdown / CSV / SRT / **DOCX** | **NOT_STARTED** |
| 7 | UX / performance polish | **NOT_STARTED** |
| 8 | Regression + release (bundle FFmpeg into the installer/ZIP) | **NOT_STARTED** |

## 9. What is verified vs. not

**Verified on the dev host (Phase 0–1):**

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
- **No regression**: unit 159, integration 33, performance 3 (+1 skipped), build 0 errors — the
  existing live-subtitle suites are unaffected.
- Full detail and commands: [`FILE_TRANSCRIPTION_TEST_REPORT.md`](FILE_TRANSCRIPTION_TEST_REPORT.md).

**NOT verified / NOT_STARTED (do not treat as done):**

- **Phases 2–8 do not exist.** There is no segment-level/VAD pipeline, no `ITranscriptionJobService`,
  no `ITranscriptAlignmentService`, no migration-5 tables, no DOCX, no dialogue editor, no drag-drop
  UI, no player.
- **Long-audio accuracy is unmeasured** — there is **no reference transcript**, so CER/WER is
  `NOT_TESTED`.
- **File decode performance** (throughput on long files, memory on multi-hour audio) is unmeasured.
- **Target-hardware acceptance** (Windows 11 + Core Ultra 7 155H: CPU, memory, latency, and the live
  transcript **plus** a concurrent file job) is `BLOCKED` / `NOT_TESTED` — the target machine is not
  available.
- **FFmpeg is not bundled** into the installer or portable ZIP yet (Phase 8).

See [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) (matrix FT-01 .. FT-25) for the per-item status.
