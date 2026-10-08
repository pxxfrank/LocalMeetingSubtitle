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
