# Performance

> **Caveat up front:** every number below was measured on the **development host**
> (Windows 10 Pro 19045, Intel Xeon Gold 6230N, 36 cores/36 threads, 64 GB RAM — a server CPU far
> faster than the target laptop). **No performance measurement was performed on the target hardware**
> (Windows 11 + Intel Core Ultra 7 155H + Intel Arc + Intel AI Boost NPU). Target RTF / CPU / latency
> figures are **ESTIMATES, not measured**.

## 1. Measured evidence (development host)

### 1.1 Real ASR decode — `tools/AsrBenchmark`

Model: `streaming-zipformer-zh-14M` INT8, `--threads 4`. Values verbatim from the run.

| WAV | Load (ms) | Audio (s) | Inference (s) | RTF | Working set | Decoded text |
| --- | --- | --- | --- | --- | --- | --- |
| `test_wavs/0.wav` | 2741 | 5.612 | 0.262 | **0.0466** | 100 MB | `对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢` |
| `test_wavs/1.wav` | — | — | — | **0.0492** | — | `重点呢想谈三个问题首先呢就是这一轮全球金融动量的表现` |

Interpretation: at RTF ≈ 0.05 the recognizer needs ~5 % of real time to keep up **on this CPU**.
Load time (~2.7 s) is a one-off engine-initialisation cost, not per-utterance.

### 1.2 WASAPI loopback capture — `tools/AudioCaptureProbe capture`

- Source format: **44100 Hz, 2 ch, 32-bit float** (default render endpoint).
- Saved a valid **16-bit PCM WAV**; RMS ≈ **0.345** while a 440 Hz tone was playing.
- Shared mode only; the probe does not change default device / volume / mute.

### 1.3 Resampler throughput — `ResamplerThroughputTests`

- Scenario: 60 s of stereo 48 kHz → 16 kHz mono through `DefaultAudioPreprocessor`.
- Result: **PASS** — the test asserts RTF < 0.5 (far below real time) and that the output sample
  count is within ±5 % of the expected 960 000 samples.
- The exact measured RTF of this test was **not recorded in the evidence brief** (only that it
  passed); no number is invented here. → *value: not recorded / PASS*.

### 1.4 Pipeline long-run — `PipelineLongRunTests`

- Scenario: 10-minute scripted run (fake capture + mock engine); asserts zero errors, bounded queue
  high-water mark ≤ capacity, monotonic 1-based sequence numbers, no duplicate finals, and every
  final persisted. Result: **PASS**.

### 1.5 PerformanceTests summary

`3 passed / 1 skipped` (skipped = `ThreeHourSoak`, never executed).

## 2. Methodology

- **RTF** = `inference seconds / audio seconds`, accumulated over the WAV and computed by
  `AsrBenchmark` (`RTF={inferenceSeconds}/{audioSeconds}`).
- **Working set** = `Process.GetCurrentProcess().WorkingSet64`.
- **Load ms** = wall time of `SherpaOnnxAsrEngine.InitializeAsync`.
- **CPU %** = `ProcessPerformanceMonitor` reads the `Processor Information / % Processor Utility`
  counter when present, otherwise falls back to process CPU-time deltas divided by wall time and
  logical-processor count — keeping the monitor usable in VMs/containers.
- **Latency** = capture backlog (queue samples / 16 kHz) + last inference duration, reported by the
  pipeline on each emitted subtitle.
- **End-to-end capture** used a played 440 Hz tone through the default render device.

## 3. Targets vs actual

Targets are taken from the product spec. "Actual" records only **measured** results; anything not
measured is marked NOT_TESTED.

| Metric | Target | Actual (dev host, measured) | Target hardware |
| --- | --- | --- | --- |
| First partial subtitle latency | ≤ 1.5 s | NOT_TESTED (UI E2E not exercised; decode itself runs at RTF ≈ 0.05) | ESTIMATE only |
| Final subtitle latency | ≤ 1 s | NOT_TESTED | ESTIMATE only |
| RTF | ≤ 0.5 | **0.0466 / 0.0492** (PASS on dev host) | ESTIMATE only |
| CPU | ≤ 25 % | NOT_TESTED (monitor implemented; not asserted against target) | ESTIMATE only |
| Working set | ≤ 1 GB | 100 MB observed during benchmark (single WAV) | ESTIMATE only |
| Stability | ≥ 3 h | NOT_TESTED (`ThreeHourSoak` never executed) | NOT_TESTED |

## 4. Explicit statement

All target-hardware numbers (RTF, CPU %, first/final latency, memory, 3-hour stability) are
**unmeasured on the target machine**. The dev-host RTF comfortably beats the ≤ 0.5 target, but the
target laptop's CPU is substantially slower and this project makes **no claim** that the target
targets are met until the model is benchmarked and the full UI path is exercised there.
See [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) (AC-01, AC-04, AC-05, AC-08, AC-09, AC-17).
