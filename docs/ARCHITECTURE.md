# Architecture

LocalMeetingSubtitle is a fully offline, CPU-only Windows desktop application that turns the
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
| `LocalMeetingSubtitle.Core` | Class library | Contracts (interfaces) + domain models, audio math (downmix/resample), transcription pipeline, hotwords + text correction |
| `LocalMeetingSubtitle.Audio` | references NAudio 2.2.1 | `WasapiLoopbackCaptureService` — captures the render endpoint via WASAPI loopback |
| `LocalMeetingSubtitle.Asr` | references sherpa-onnx 1.13.8 | `SherpaOnnxAsrEngine` (streaming + offline), `AsrModelCatalog`, `AsrOptionsFactory`, `SherpaNativeProbe`, `MockAsrEngine` (tests only) |
| `LocalMeetingSubtitle.Storage` | references Microsoft.Data.Sqlite 8.0.31 | `SqliteDatabase` (WAL, migrations) + subtitle / hotword / settings repositories, `LocalDataPaths` |
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

## 3. Threading model

| Thread / task | Owner | Work |
| --- | --- | --- |
| Capture thread | WASAPI / NAudio | raises `FramesAvailable`; only enqueues into `BoundedAudioQueue` (never blocks) |
| ASR worker task | `TranscriptionPipeline.AsrLoopAsync` | dequeues audio, runs preprocess+decode, feeds the accumulator; a single thread per stream (sherpa session is not thread-safe) |
| SQLite writer task | `TranscriptionPipeline.WriterLoopAsync` | drains the segment channel and persists finals with retry (3 attempts); a slow disk can never stall recognition |
| Performance sampler | `ProcessPerformanceMonitor` timer (1 s) | samples CPU/memory; pipeline reports RTF/queue/latency |
| UI thread | WPF dispatcher | all observable state updates; engine init and persistence are `async` so UI never blocks |

Engine hot-swap (`RequestEngineSwap`) runs on the inference thread: the current utterance is
flushed and persisted first, then the engine/session are replaced — capture is never interrupted.
`AsrNumThreads` therefore applies at the next Start / engine swap, not live (see KNOWN_ISSUES P3).

## 4. Persistence (SQLite)

- File: `%LOCALAPPDATA%\LocalMeetingSubtitle\subtitles.db`; PRAGMA `journal_mode=WAL`,
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
