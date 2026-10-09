# Architecture

字幕君 is a fully offline, CPU-only Windows desktop application that turns the
system playback (render) audio into live Chinese subtitles. This document describes the module
layout, data flow, threading model, persistence schema, and the public contracts.

## 1. Module map

```
                              +--------------------------------------------------+
                              |  LocalMeetingSubtitle.App  (WinExe, WPF)         |
                              |  MainWindow / SettingsWindow / FloatingSubtitle  |
                              |  MainViewModel / SettingsViewModel               |
                              |  TrayIconController / ShellService / DI root     |
                              +----------------------------+---------------------+
                                                           |  (interfaces only)
      +------------------+------------------+--------------+--------------+--------------------+
      |                  |                  |                             |                    |
+-----v-----+      +-----v-----+      +-----v-----+              +--------v-------+    +-------v------+
|   Audio   |      |    Asr    |      |   Core    |              |    Storage     |    |    Export    |
| NAudio    |      | sherpa-   |      | contracts |              | SQLite (WAL)   |    | TXT/SRT/MD   |
| WASAPI    |      | onnx      |      | + models  |              | repositories   |    | formatters   |
| loopback  |      | 1.13.8    |      | + pipeline|              +----------------+    +--------------+
+-----------+      +-----+-----+      +-----------+
                         |                  ^
                         |                  |
                   +-----v------------------+-----+
                   |  sherpa-onnx-c-api.dll (native) |
                   |  onnxruntime.dll               |
                   +--------------------------------+

  +---------------------+     +----------------------------+
  |    Diagnostics      |     |       ModelDownloads       |
  | ProcessPerformance  |     | HttpModelManager           |  <-- the ONLY assembly that
  | Monitor             |     | (+ Hugging Face download)  |      references System.Net.Http
  +---------------------+     +----------------------------+
```

| Project | TFM / notes | Responsibility |
| --- | --- | --- |
| `LocalMeetingSubtitle.App` | `net8.0-windows`, `x64`, `WinExe`, self-contained publish | WPF shell, DI composition root, view-models, tray icon, floating subtitle window |
| `LocalMeetingSubtitle.Core` | Class library | Contracts (interfaces) + domain models, audio math (downmix/resample), transcription pipeline, hotwords + text correction; **speaker diarization** contracts/models + `Core/Speakers/*` (`SpeakerDiarizationService`, `SpeakerAlignmentService`, `SpeakerColorPalette`) |
| `LocalMeetingSubtitle.Audio` | references NAudio 2.2.1 | `WasapiLoopbackCaptureService` — captures the render endpoint via WASAPI loopback; `NaudioAudioFileLoader` — decodes a local audio file (any NAudio/Media Foundation format) to the diarizer's mono 16 kHz samples |
| `LocalMeetingSubtitle.Asr` | references sherpa-onnx 1.13.8 | `SherpaOnnxAsrEngine` (streaming + offline), `AsrModelCatalog`, `AsrOptionsFactory`, `SherpaNativeProbe`, `MockAsrEngine` (tests only); **`SherpaOfflineSpeakerDiarizer`** (V0.4.0) + `DiarizationModelCatalog` |
| `LocalMeetingSubtitle.Storage` | references Microsoft.Data.Sqlite 8.0.31 | `SqliteDatabase` (WAL, migrations) + subtitle / hotword / settings / speaker / audio-asset repositories, `LocalDataPaths` |
| `LocalMeetingSubtitle.Export` | — | `SubtitleExportService` + TXT / SRT / Markdown formatters |
| `LocalMeetingSubtitle.Diagnostics` | references System.Diagnostics.PerformanceCounter 8.0.1 | `ProcessPerformanceMonitor` (CPU/memory sampling), `RollingAverage` |
| `LocalMeetingSubtitle.ModelDownloads` | references `System.Net.Http` | `HttpModelManager` — list/install/verify models from Hugging Face. **Isolated** (see §6) |

## 2. Data flow

```
 WASAPI render endpoint (system playback)
        |  FramesAvailable (capture thread, float32 interleaved, e.g. 44100 Hz 2ch)
        v
 IAudioPreprocessor (DefaultAudioPreprocessor)
        |  ChannelConverter.ToMono  ->  StreamingResampler (windowed-sinc) -> 16 kHz mono
        v
 BoundedAudioQueue  (cap = 30 s; on overflow drops the INCOMING chunk and counts it)
        |  TryDequeue + SemaphoreSlim signal
        v
 ASR worker task  (AsrLoopAsync)
        |  streaming: AcceptWaveform -> Decode* -> GetResult (partial + endpoint)
        |  offline:   AudioSegmenter -> per-segment Decode -> text
        v
 SubtitleAccumulator
        |  partial vs final, consecutive-duplicate suppression, flush on pause/stop
        v
 System.Threading.Channels channel (unbounded, single reader)
        v
 SQLite writer task  (WriterLoopAsync)  ->  ISubtitleRepository.AppendSegmentAsync
                                                (final segments only; partials never persisted)
```

`TranscriptionPipeline` raises `TranscriptUpdated`, `StatusChanged`, `LevelChanged`, `Overload`
and `ErrorOccurred`. `MainViewModel` marshals every event onto the WPF dispatcher before touching
observable state, so the UI thread never blocks on capture, recognition, or disk.

### Partial vs final

- A non-endpoint hypothesis updates the **partial** shown to the user; it is never written to disk.
- An endpoint commits exactly one **final** segment (the endpoint text, or the last partial if the
  endpoint text is empty) and advances the 1-based sequence number.
- Consecutive identical finals (after whitespace/punctuation folding) within a short window are
  suppressed to avoid repeats; `Flush` commits a still-open partial on pause/stop so the last
  sentence is not lost.

### Speaker diarization (V0.4.0) — post-meeting, offline

Diarization is a **separate, on-demand path** that never touches the live capture path. It runs only
after a meeting (or over an already-saved session) and only when the user imports a recording:

```
 imported audio file (user picks it; %LOCALAPPDATA% or anywhere on disk)
        |  NaudioAudioFileLoader.LoadMono(file, 16 kHz)   -> downmix+resample (same preprocessor)
        v
 SpeakerDiarizationService.RunAsync  (dedicated BelowNormal thread, one run at a time)
        |  SherpaOfflineSpeakerDiarizer.Initialize  (pyannote segmentation + embedding models)
        |  Process(samples) -> raw intervals {Start, End, RawSpeakerIndex, Confidence}
        v
 anonymous Speaker rows  (one per distinct cluster, labeled "A", "B", ... + a palette colour)
        |  raw intervals persisted (SpeakerInterval) under a new DiarizationRun
        v
 SpeakerAlignmentService.Align(segments, intervals, speakerIdByRawIndex)   (pure, no I/O)
        |  overlap-weighted: at most one speaker per subtitle segment; ambiguous -> NeedsConfirmation
        v
 SpeakerAssignment rows  (one per segment; Source=Auto; original segment text is NEVER modified)
```

- The audio is segmented by the **pyannote** model, each segment is turned into a **voiceprint
  embedding** (3D-Speaker ERes2Net), and the embeddings are **agglomeratively clustered** (threshold
  `AppSettings.DiarizationClusteringThreshold`, default 0.5; or a fixed `NumClusters` when the user
  sets `AppSettings.DiarizationSpeakerCount != 0`). All three stages live inside the single native
  `OfflineSpeakerDiarization` object.
- Clustering only produces **anonymous labels** (A/B/…). There is no identity recognition: a label is
  a per-session voiceprint cluster the user may rename.
- `Align` is deliberately conservative: without word-level timestamps a segment cannot be split
  between speakers, so an undecidable overlap is assigned its best candidate and flagged rather than
  guessed. A segment with no usable overlap becomes "unknown speaker" (null id).
- Diarization is **purely additive**: it writes new speaker rows/assignments and leaves
  `segments.OriginalText` / `CorrectedText` untouched.

## 3. Threading model

| Thread / task | Owner | Work |
| --- | --- | --- |
| Capture thread | WASAPI / NAudio | raises `FramesAvailable`; only enqueues into `BoundedAudioQueue` (never blocks) |
| ASR worker task | `TranscriptionPipeline.AsrLoopAsync` | dequeues audio, runs preprocess+decode, feeds the accumulator; a single thread per stream (sherpa session is not thread-safe) |
| SQLite writer task | `TranscriptionPipeline.WriterLoopAsync` | drains the segment channel and persists finals with retry (3 attempts); a slow disk can never stall recognition |
| Performance sampler | `ProcessPerformanceMonitor` timer (1 s) | samples CPU/memory; pipeline reports RTF/queue/latency |
| UI thread | WPF dispatcher | all observable state updates; engine init and persistence are `async` so UI never blocks |
| Diarization thread | `SpeakerDiarizationService.RunAsync` | one whole-file post-meeting run (decode → native segmentation+embedding+clustering → label → align → persist) on a dedicated `Thread` with `Priority = BelowNormal` (name `speaker-diarization`). A `SemaphoreSlim(1, 1)` allows only one run at a time, and cancellation is cooperative via the native progress callback |

Engine hot-swap (`RequestEngineSwap`) runs on the inference thread: the current utterance is
flushed and persisted first, then the engine/session are replaced — capture is never interrupted.
`AsrNumThreads` therefore applies at the next Start / engine swap, not live (see KNOWN_ISSUES P3).

Because the diarization thread runs at **below-normal** priority and is capped to a small CPU-thread
count (`SherpaOfflineSpeakerDiarizer`, default `min(ProcessorCount/4, 4)`), the OS scheduler preempts
diarization for the ASR worker — so a diarization run cannot starve live recognition. It reads the
audio file and the persisted segments directly and never touches the capture path.

## 4. Persistence (SQLite)

- File: `%LOCALAPPDATA%\SubtitleJun\subtitles.db`; PRAGMA `journal_mode=WAL`,
  `foreign_keys=ON`, `synchronous=NORMAL`. Connection pooling disabled so the file (and `-wal`/`-shm`)
  is deletable after close.
- Versioned migrations (`schema_version` table) run each in its own transaction.

### Tables

| Table | Key columns | Notes |
| --- | --- | --- |
| `sessions` | `SessionId` (PK), `Title`, `StartTime`, `EndTime`, `Status`, `ModelId`, `AudioDeviceId`, `AudioDeviceName` | one row per meeting |
| `segments` | `SegmentId` (PK autoinc), `SessionId`, `SequenceNumber`, `StartOffsetMs`, `EndOffsetMs`, `OriginalText`, `CorrectedText`, `IsEdited`, `CreatedAt` | **`UNIQUE (SessionId, SequenceNumber)`** rejects duplicate inserts; index `ix_segments_session` |
| `hotwords` | `Id` (PK), `Text`, `Enabled`, `GroupId`, `Score` | model-level boosting entries |
| `hotword_groups` | `GroupId` (PK), `Name`, `Enabled`, `SortOrder` | grouping for hotwords |
| `correction_rules` | `Id` (PK), `Pattern`, `Replacement`, `Enabled`, `IsRegex`, `Priority`, `WholeTokenOnly`, `CaseSensitive` | deterministic text post-correction (not blind global replace) |
| `settings` | `Key` (PK), `Value` | single-row-ish key/value settings store |
| `metrics` | `Id` (PK), `SessionId`, `Timestamp`, `CpuPercent`, `WorkingSetMb`, `Rtf`, `QueueLength`, `MaxQueueLength`, `DroppedAudioSeconds`, `EndToEndLatencyMs` | index `ix_metrics_session` |
| `diarization_runs` | `RunId` (PK), `SessionId`, `AudioAssetId`, `CreatedAt`, `Status`, `RequestedSpeakerCount`, `ResolvedSpeakerCount`, `ClusteringThreshold`, `MinDurationOn`, `MinDurationOff`, `SegmentationModelId`, `EmbeddingModelId`, `DurationMs`, `ErrorMessage` | one row per diarization execution (enables re-analysis + history); index `ix_diaruns_session` |
| `speakers` | `SpeakerId` (PK), `SessionId`, `Label`, `DisplayName`, `ColorArgb`, `SortOrder`, `CreatedAt`, `IsMerged`, `MergedIntoSpeakerId` | anonymous per-session speakers (`A`, `B`, …); `DisplayName` / `IsMerged` back future rename/merge (V0.4.2); index `ix_speakers_session` |
| `speaker_intervals` | `SpeakerSegmentId` (PK autoinc), `RunId`, `SessionId`, `StartMs`, `EndMs`, `RawSpeakerIndex`, `Confidence` | raw engine intervals for a run (replaced on re-analysis); index `ix_speaker_intervals_run` |
| `speaker_assignments` | `AssignmentId` (PK autoinc), `SessionId`, `SegmentId`, `RunId`, `SpeakerId`, `Source`, `Confidence`, `NeedsConfirmation`, `UpdatedAt` | one active row per segment; **`UNIQUE (SegmentId)`**; `Source` distinguishes auto vs manual (a manual row survives re-analysis); index `ix_speaker_assignments_session` |
| `audio_assets` | `AudioAssetId` (PK), `SessionId`, `Path`, `Kind`, `SampleRate`, `Channels`, `DurationMs`, `SizeBytes`, `CreatedAt`, `DeleteAfterUtc`, `IsTemporary` | imported (and future recorded) audio files + retention; index `ix_audio_assets_expiry` |

Migration **4** ("speaker diarization schema") adds `diarization_runs`, `speakers`,
`speaker_intervals`, `speaker_assignments` and `audio_assets`. It is **purely additive** (only
`CREATE TABLE` / `CREATE INDEX`), so a database from an earlier version keeps opening unchanged.

Crash recovery: `ISubtitleRepository.RecoverAbortedSessionsAsync` marks sessions still in
`Recording`/`Paused` as `Aborted`.

## 5. Public contracts

| Interface | Assembly | Purpose |
| --- | --- | --- |
| `IAudioCaptureService` | Core | capture render audio via WASAPI loopback; MUST NOT change device defaults, volume, mute, or exclusive mode |
| `IAudioPreprocessor` | Core | downmix + resample captured PCM to mono float32 at the target rate (16 kHz) |
| `IAsrEngine` / `IAsrSession` | Core | replaceable recognition engine; streaming exposes incremental partials, offline returns after `InputFinished` |
| `IHotwordService` | Core | owns hotwords + correction rules; writes the model-level hotwords file; applies corrections; reports `HotwordMode` |
| `ISubtitleRepository` | Core | sessions + segments + metrics; durable, idempotent writes |
| `IHotwordRepository` | Core | hotwords, groups, correction rules CRUD |
| `ISettingsRepository` | Core | load/save `AppSettings` |
| `ISubtitleExportService` | Core | export TXT/SRT/Markdown |
| `IPerformanceMonitor` | Core | CPU/memory + RTF/queue/latency snapshot |
| `IModelManager` | Core | model catalog, installed check, missing files, install |
| `ISpeakerDiarizationEngine` | Core | native offline diarization engine wrapper (one blocking whole-file call); returns intervals, never throws from `Dispose` |
| `ISpeakerDiarizationService` | Core | orchestrates a post-meeting run on a below-normal thread; one run at a time (`IsBusy`) |
| `ISpeakerAlignmentService` | Core | pure mapping of diarization intervals onto subtitle segments (no I/O, no text edits) |
| `IAudioFileLoader` | Core | decode + downmix + resample a local audio file to the engine's mono samples |
| `ISpeakerRepository` | Core | runs, speakers, raw intervals, per-segment assignments; manual rows survive re-analysis |
| `IAudioAssetRepository` | Core | registry + retention of local audio assets (imported / recorded) |

Supporting types: `ITranscriptFormatter` (per-format formatter), `IAppLogger` / `NullLogger`.

## 6. Why `ModelDownloads` is a separate assembly

The offline guarantee must be **provable**, not merely asserted. If the model downloader lived in
`Asr` (or any assembly on the recognition path), that assembly would transitively reference
`System.Net.Http` and the claim "the runtime never touches the network" would be unverifiable.

By isolating `HttpModelManager` into `LocalMeetingSubtitle.ModelDownloads`:

- `Core`, `Asr`, `Audio` and `Storage` contain **no** `System.Net.*` / `Http` reference
  (verified by `tools/OfflineVerification`, check #2).
- Model download/installation is a development/install-time concern only; once a model is present
  under the models root, recognition needs no network.

The App project still references `ModelDownloads` (to report installed-model status and, at
install time, to fetch models), but the recognition pipeline itself never calls into it.
