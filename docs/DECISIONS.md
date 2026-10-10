# Architecture Decision Records (ADR)

Concise, ADR-style records. Each entry states the decision, the context, the rationale, and the
consequence/trade-off.

## D1 — .NET 8 + WPF for the desktop shell

- **Decision:** build the app as a WPF `WinExe` targeting `net8.0-windows`, `x64`.
- **Context:** the product is a Windows-only desktop tool needing a tray icon, a floating always-on-top
  overlay, and Windows audio interop.
- **Rationale:** WPF is mature and first-class on Windows 8+, gives XAML data-binding for the
  real-time transcript, and .NET 8 is LTS. Windows Forms (`UseWindowsForms`) is enabled only to reuse
  `NotifyIcon` for the tray, since WPF has no first-class tray support.
- **Consequence:** Windows-only; requires the .NET 8 desktop runtime (or a self-contained publish).

## D2 — sherpa-onnx 1.13.8 as the ASR engine

- **Decision:** use sherpa-onnx 1.13.8 (C# binding over the native `sherpa-onnx-c-api` /
  `onnxruntime` DLLs).
- **Context:** need offline, CPU-only streaming ASR with Chinese support and an embedding-friendly API.
- **Rationale:** sherpa-onnx provides native streaming transducers (partials + endpointing),
  offline models (SenseVoice), model-level hotwords, and a permissive Apache-2.0 license. The C# API
  was verified by reflection over the real assembly (see `docs/SHERPA_CSHARP_API_DUMP.txt`).
- **Consequence:** the app ships native x64 DLLs (`onnxruntime.dll`, `sherpa-onnx-c-api.dll`) and is
  therefore x64-only.

## D3 — CPU-only inference (no Arc / NPU acceleration)

- **Decision:** run inference on the CPU; set the provider to `"cpu"`.
- **Context:** the target is a laptop with Intel Arc iGPU and an Intel AI Boost NPU, but the
  development host has no Arc/NPU.
- **Rationale:** CPU execution is portable, avoids GPU/NPU driver and execution-provider complexity,
  and is sufficient for a 14M-parameter streaming model. The spec explicitly asks for CPU-only.
- **Consequence:** performance depends heavily on the target CPU; target RTF is an estimate until
  measured on the target hardware. GPU/NPU acceleration remains a possible future optimisation.

## D4 — WASAPI loopback in shared mode

- **Decision:** capture system playback via WASAPI loopback on the default (or selected) render
  endpoint, in **shared mode only**.
- **Context:** the tool must transcribe whatever the user is playing (a meeting) without a microphone
  or a virtual cable, and must not disturb the user's audio.
- **Rationale:** loopback captures exactly what is rendered; shared mode works without exclusive
  access. The service is contractually forbidden from changing device defaults, volume, mute, or
  exclusive mode (see `IAudioCaptureService`).
- **Consequence:** captures the whole system mix (all apps), and depends on the render endpoint being
  active. The source format (e.g. 44100 Hz 2ch float) is not assumed to be 16 kHz.

## D5 — SQLite with WAL journaling

- **Decision:** persist to a single SQLite database (`Microsoft.Data.Sqlite`) in WAL mode with
  `foreign_keys=ON`, `synchronous=NORMAL`, and versioned migrations.
- **Context:** need durable, single-file, per-user storage of sessions, segments, hotwords, settings,
  and metrics, with crash recovery.
- **Rationale:** SQLite is embedded, zero-configuration and offline; WAL gives concurrent
  reader/writer behaviour and resilience; migrations keep the schema evolvable. Connection pooling is
  disabled so the DB file (and `-wal`/`-shm`) is releasable/deletable.
- **Consequence:** writes go through a dedicated writer task; `UNIQUE(SessionId, SequenceNumber)`
  makes replayed final inserts idempotent.

## D6 — Bounded audio queue that drops the incoming chunk

- **Decision:** bound the capture→ASR queue (default 30 s of audio). On overflow, drop the
  **incoming** chunk (not the oldest buffered) and count the dropped seconds.
- **Context:** if recognition falls behind, memory and latency must not grow without bound, but an
  in-progress utterance should not be truncated mid-word.
- **Rationale:** dropping the newest audio keeps the audio already queued (an in-progress phrase)
  intact; the drop is surfaced to the UI via an overload warning rather than hidden.
- **Consequence:** under sustained overload some audio is lost, but the system stays bounded and the
  condition is reported (RTF/queue/dropped metrics).

## D7 — `ModelDownloads` as an isolated assembly

- **Decision:** place `HttpModelManager` (the only `System.Net.Http` user) in a separate
  `LocalMeetingSubtitle.ModelDownloads` assembly, off the recognition path.
- **Context:** the product claims to be fully offline at runtime; such a claim must be provable.
- **Rationale:** isolating network code means `Core`/`Asr`/`Audio`/`Storage` provably contain no
  `System.Net.*` / `Http` references (verified by `OfflineVerification` check #2). Model download is a
  development/install-time concern only.
- **Consequence:** the App references `ModelDownloads` for install/status, but the recognition
  pipeline never calls into it; the offline guarantee is statically checkable.

## D8 — Streaming windowed-sinc resampler design

- **Decision:** implement `StreamingResampler` as a streaming windowed-sinc (Blackman) lowpass with
  unity DC gain, priming the buffer with `HalfTaps` zeros and starting the read position at `HalfTaps`.
- **Context:** capture formats vary (e.g. 44100 Hz) and must be converted to 16 kHz mono with phase
  continuity across block boundaries; anti-aliasing is required when downsampling.
- **Rationale:** the primed-buffer + offset-start design puts the first real sample on the kernel
  centre, avoids negative indices, and keeps continuity so no samples are lost or duplicated at
  boundaries. This is also the fix for defect P1 (the naive position warm-up guard emitted zero
  samples).
- **Consequence:** a small fixed latency (`2 × HalfTaps` source samples) and a per-block windowed-sinc
  cost, both well below real time (perf test PASS).

## D9 — Partial/final `SubtitleAccumulator`

- **Decision:** convert the ASR hypothesis stream into events via a single `SubtitleAccumulator`:
  non-endpoint updates the current partial; an endpoint commits exactly one final (endpoint text, or
  the last partial if empty); consecutive duplicates are suppressed; `Flush` commits an open partial on
  pause/stop.
- **Context:** streaming engines emit many hypotheses per utterance; the UI wants a live partial and
  a stable, persisted final; duplicates and lost last-sentences are unacceptable.
- **Rationale:** centralising the partial/final/dedup/sequence logic keeps the pipeline simple and
  testable, and guarantees **only finals are persisted** with monotonic 1-based sequence numbers.
- **Consequence:** partials are shown but never written to disk; final sequence numbers are stable and
  unique per session (enforced by the DB constraint).

## D10 — Apache-2.0 models only

- **Decision:** restrict the catalog to Apache-2.0 licensed models (streaming zipformer zh-14M,
  streaming zipformer bilingual zh-en, SenseVoice).
- **Context:** the product is redistributed; model licenses must permit use and redistribution.
- **Rationale:** all three candidate repositories expose an Apache-2.0 license (verified via the
  Hugging Face API), avoiding copyleft/non-commercial restrictions.
- **Consequence:** model selection is limited to permissively licensed models; the concrete model
  choice remains **tentative** until benchmarked on the target hardware (see
  [`MODEL_SELECTION.md`](MODEL_SELECTION.md)).

## D11 — Data-driven transcription-mode catalog that fails soft

- **Decision:** model the three file-transcription modes as a **data-driven catalog**
  (`Asr/TranscriptionModeCatalog.Resolve(mode, …)`) that returns a `ResolvedTranscriptionMode` carrying
  `IsAvailable` / `UnavailableReason`, rather than a `switch` that throws when a mode cannot run.
- **Context:** the three modes (Fast / Standard / HighAccuracy) differ in model **and** decoding **and**
  segmentation, and the High-accuracy mode depends on a large (≈239 MB) model that may not be installed.
- **Rationale:** a caller (CLI or UI) needs to **list** and **describe** all modes, know whether each is
  runnable, and degrade gracefully — a missing model must not crash the app or hide the mode. Returning
  availability plus a human-readable reason keeps the policy in one place and testable
  (`TranscriptionModeCatalogTests`).
- **Consequence:** the modes are declarative data, not branches; an unavailable mode is presented as
  such **with a reason** instead of throwing, and the caller decides what to do (fall back, or prompt to
  install the model).

## D12 — Overlap text de-duplication rule

- **Decision:** when consecutive offline segments overlap in audio (High-accuracy mode uses a **1.5 s**
  overlap), remove the repeated text with `OverlapTextDeduplicator.Apply(previousText, currentText,
  minOverlapChars)`: **fold both texts** (strip whitespace/punctuation/symbols, lowercase — reusing
  `SubtitleAccumulator.Fold`), find the **longest suffix-of-previous == prefix-of-current** run, and
  trim it from the **later** text when it reaches **`minOverlapChars` (default 3)** folded chars. If the
  whole current text repeats the previous, **drop** it.
- **Context:** an audio overlap makes the second region re-hear the tail of the first, so the ASR
  repeats it; the timeline must stay non-overlapping but the text must not be duplicated.
- **Rationale:** only the **later** text is ever modified, so earlier segments stay stable; trimming only
  a shared run (not arbitrary characters) avoids deleting legitimate repeats; the **≥ 3 folded chars**
  threshold avoids trimming a trivial one- or two-character coincidence. Crucially, the rule **can never
  empty a segment** — a full repetition becomes `Dropped`, not an empty line.
- **Consequence:** overlap de-dup is deterministic and testable (`OverlapTextDeduplicatorTests`); the
  `Dropped` outcome is reported rather than silently producing an empty segment.

## D13 — SQLite migration 5 deferred to Phase 4 (Phase 2 kept migration 4)

- **Decision (Phase 2):** do **not** add a schema migration in Phase 2. Keep the database at
  **migration 4** and defer the V0.5 job/queue/checkpoint schema to **Phase 4**.
- **Context:** Phase 2's `OfflineTranscriptionEngine` returns results **in memory**; there is no job
  queue, no per-chunk checkpoint, and no resume yet (that is Phase 4).
- **Rationale:** adding tables before their owning feature exists would freeze a schema that would then
  churn; migration 4 already stores what a transcript *is*. A file transcript could be persisted with
  **no schema change** — one `MeetingSession` plus one `segments` row per result (existing
  `SqliteSubtitleRepository`, `StartOffsetMs` / `EndOffsetMs`).
- **Outcome (superseded by Phase 4):** migration 5 was **added in Phase 4** (`file transcription job
  schema`; see D17). Phase 2 itself required **no migration**, and the schema stayed at migration 4
  until Phase 4. The tables this entry previously pre-declared (`MediaFile` / `TranscriptionJob` /
  `TranscriptionChunk` / `JobCheckpoint`) were **not** created verbatim — the real table set is D17.

## D14 — Temp-WAV tee + reuse `SpeakerDiarizationService` unchanged (Phase 3)

- **Decision:** for a file job, write the decoded audio to a **temporary 16 kHz mono WAV** (tee'd in the
  same decode pass, via the new **synchronous** `PcmWavWriter`) and run the V0.4
  `SpeakerDiarizationService` over that file **unchanged**, rather than streaming samples into the
  diarizer.
- **Context:** the V0.4 diarizer is a **whole-file** engine that takes a **file path** and loads it
  through `IAudioFileLoader` (NAudio). NAudio cannot read video containers (`.mp4/.mkv/.mov`), and the
  file job must support video. FFmpeg already decodes the file.
- **Rationale:** reusing the tested V0.4 service avoids touching tested code, avoids duplicating the
  run/speaker/interval/assignment persistence, supports video, and keeps the ASR path streaming. Two
  design notes make this safe:
  - **Fresh synchronous writer.** `PcmWavWriter` is deliberately **synchronous** (unlike the live-capture
    `WaveRecordingService`, which uses a bounded channel + **frame drops**). A file job is pull-based and
    a dropped frame would silently shift the whole diarization timeline, so the writer never drops — it
    blocks the decode pass instead.
  - **Drop guard / length check.** The service compares the written WAV's duration against the
    transcribed audio duration and **warns on divergence**, so a truncated or mis-encoded temp file is
    surfaced rather than silently mis-aligning speakers.
- **Rejected alternatives:** buffering the whole decoded file in memory (4 h @16 kHz float32 ≈ 920 MB),
  and adding a samples-based overload to `ISpeakerDiarizationService` (would touch tested V0.4 code).
- **Consequence:** the process holds a temp WAV in a **caller-supplied staging directory** (the app passes
  `LocalDataPaths.RecordingsDirectory`, so the existing 24 h orphan sweep covers a crash); it is deleted in
  `finally`. A crash mid-job leaves the WAV until the next app start cleans it (see
  [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md)).

## D15 — Assemble the dialogue from persisted `speaker_assignments` (Phase 3)

- **Decision:** build the role-tagged dialogue from the **persisted `speaker_assignments`** rows, **not**
  by re-running `ISpeakerAlignmentService` over the diarizer's raw intervals.
- **Context:** the diarizer's **raw-cluster-index → speaker-id** mapping is only built **transiently**
  inside `SpeakerDiarizationService` and is **not persisted**. The final per-segment decision is already
  in `speaker_assignments`, which holds the chosen speaker plus `NeedsConfirmation` / `Confidence` for
  each segment id.
- **Rationale:** reading the persisted assignments gives the **final** (post-clustering) speaker per
  segment without re-deriving the transient cluster mapping, keeps the assembler a pure function of stored
  state, and lets the Phase 5 editor re-assemble idempotently after a manual edit.
- **Consequence:** `ITranscriptAlignmentService.Align(sessionId, facts, assignments, speakers, options?)`
  depends only on repository state; a segment with no assignment (or a null `SpeakerId`) becomes an
  **unknown** turn rather than an error.

## D16 — Dialogue merge rule (Phase 3)

- **Decision:** `TranscriptAlignmentService` merges consecutive segments by the **same speaker** into one
  turn, and starts a **new turn** on (a) a **speaker change**, (b) a **gap** larger than
  `DialogueAssemblyOptions.MaxGap` (**default 2 s**), or (c) the turn's text exceeding
  `DialogueAssemblyOptions.MaxTurnChars` (**default 500**). A turn takes the **first `Start`**, the
  **last `End`**, the **concatenated text**, the constituent segment ids, and an **OR-propagated
  `NeedsConfirmation`**.
- **Unknown speakers:** a segment whose assignment is missing, or whose `SpeakerId` is null, becomes an
  unknown turn (`SpeakerId == null`, label **未知发言人**, grey `0xFF808080`) and is **excluded from
  `Participants`**.
- **Context:** diarization yields per-segment speaker ids but the product wants readable **dialogue
  turns**, and a later editor needs a deterministic, reproducible grouping.
- **Rationale:** a same-speaker + short-gap + bounded-length rule is simple, deterministic and
  **idempotent** (re-assembly after an edit produces the same turns), and the length cap prevents an
  unbounded turn when one speaker talks for a long time. Keeping unknown speakers out of `Participants`
  means the participant list only reflects speakers the diarizer actually identified.
- **Consequence:** `Participants` aggregate speaking time / turn count / segment count over **known**
  speakers, ordered by speaking time **descending**; the rule is unit-tested
  (`TranscriptAlignmentServiceTests`).

## D17 — Migration-5 table set, and the resume cursor derived from `segments` (Phase 4)

- **Decision:** migration 5 (`file transcription job schema`) adds exactly **two** tables —
  `media_files` and `transcription_jobs` (plus the `ix_transcription_jobs_queue (Status, QueuedAt)`
  index). It does **not** add `transcription_chunks`, `transcript_segments` or `job_checkpoints`. **The
  resume cursor is derived from the committed `segments` rows**
  (`MAX(EndOffsetMs)`, `MAX(SequenceNumber)+1`) and is **never stored in a separate checkpoint**.
- **Context:** the table list this project had previously pre-declared (`MediaFile` / `TranscriptionJob`
  / `TranscriptionChunk` / `TranscriptSegment` / `JobCheckpoint`, D13) assumed an upfront chunk plan and
  a stored checkpoint cursor.
- **Rationale:**
  - The **VAD is a streaming state machine** with no upfront chunk plan, so there is no
    `transcription_chunks` work-unit to persist.
  - The transcript **reuses the existing `segments` table** and the speaker reference reuses
    `speaker_assignments`, so a parallel `transcript_segments` table would duplicate them.
  - A stored cursor can **drift ahead** of the data if the process dies between "advance the cursor" and
    "commit the segment". Deriving the cursor from the committed `segments` means it **can never run
    ahead** of the data that actually exists.
- **Consequence:** `ProcessedMs` / `SegmentsEmitted` on the job row are only a **throttled (≥ 2 s)
  display snapshot** — they are **never** used to resume. The migration is purely additive in the
  migration-4 style (lower-case names, `CREATE TABLE IF NOT EXISTS`, no foreign keys).

## D18 — Resume at the last committed segment's end, with ffmpeg input seeking (Phase 4)

- **Decision:** a resume derives `resumeFrom = MAX(EndOffsetMs)` and
  `startSequence = MAX(SequenceNumber)+1` from the committed `segments`, seeks the decode to
  `resumeFrom`, and sets the session back to `Recording`. The seek is **input seeking**:
  `FFmpegMediaDecodeService` inserts `-accurate_seek` and `-ss <seconds>` **before** `-i`.
- **Context:** `FileTranscriptionService` must extend an interrupted job's transcript in place without
  re-transcribing or duplicating the prefix; `MediaDecodeRequest` gained a `TimeSpan StartOffset`.
- **Rationale:** `-ss` **before `-i`** seeks in the input container instead of decoding and discarding
  the prefix (**O(1)** rather than O(offset)), while `-accurate_seek` keeps the seek accurate. Every
  emitted `PcmBlock.Start` is offset by `StartOffset`, so **positions stay absolute** on the media
  timeline and the resumed segments line up with the pre-interrupt ones.
- **Consequence / limits:**
  - WAV/PCM seeking is **bit-exact** (verified: mean sample difference < 1e-6); **mp3 seeking is not
    sample-exact** (decoder delay, tens of ms). The emitted block positions stay absolute either way.
  - The first segment produced **immediately after a resume** can be transcribed slightly differently
    from an uninterrupted run (the VAD/recognizer restarts there with different leading context). Count,
    ordering, sequence numbers and timing are preserved and nothing is duplicated — the transcript is
    **structurally identical**, not textually identical (see [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) P3-20).

## D19 — Rebuild the whole-file staging WAV with an extra decode pass on a resumed job (Phase 4)

- **Decision:** the temporary diarization WAV is tee'd only when the run starts at offset zero. A
  **resumed** run **rebuilds the whole-file WAV first with one extra decode-only pass** before
  diarization.
- **Context:** the V0.4 diarizer (D14) is a **whole-file** engine, and NAudio cannot read a video
  container, so diarization runs over the temp WAV. A resumed run starts decoding at `resumeFrom`, so
  its tee would only cover the tail of the file, not the whole file.
- **Rationale:** diarization needs the whole file to cluster speakers correctly; the resume point only
  affects transcription, not the diarization input. Rebuilding the WAV (rather than diarizing a partial
  file) keeps the speakers correct at the cost of one decode-only pass.
- **Consequence:** a **resumed** job costs one extra decode-only pass over the file; a **fresh** job
  needs no extra pass (the tee and the ASR decode are the same pass). Recorded in
  [`PERFORMANCE_REPORT.md`](PERFORMANCE_REPORT.md).

## D20 — Cooperative cancel + re-queue; the composition root owns the recognizer (Phase 4)

- **Decision:** `ITranscriptionJobService.CancelAsync` is a **cooperative cancel** (a queued job is
  marked `Cancelled` immediately; a running job stops at the next cancellation check).
  `ResumeAsync` **re-queues** the job and the caller/composition root (the injected
  `TranscriptionJobResolver`) **owns the recognizer**: it creates a **fresh engine per attempt**, and
  the **job service disposes the engine it is given**.
- **Context:** a job must survive a restart and be cancellable without corrupting the committed data;
  the engine is an expensive, per-run resource that cannot be reused across attempts.
- **Rationale:** cooperative cancellation guarantees the run stops at a **segment boundary** (nothing
  half-written), and re-queueing re-runs the job through the same resolver seam so the request is
  rebuilt from the stored row every time. Because the resolver creates a fresh engine per attempt, the
  job service can safely own (and dispose) whatever the resolver returns.
- **Consequence:** `RecoverUnfinishedAsync` marks jobs left `Running` by a previous session as
  `Interrupted` (they can then be resumed); canceled jobs end as `Cancelled` with the session `Paused`;
  a decode failure ends as `Failed` with the session `Aborted`. Cancel/resume/recover are exercised by
  `TranscriptionJobServiceTests` and the `FileTranscribe --jobs` interrupt→resume run.
