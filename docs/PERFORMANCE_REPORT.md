# Performance Report (measurements only)

> **This file records only figures that were actually measured.** Nothing here is extrapolated to the
> target laptop. Every number was taken on the **development host** (Windows 10 Pro, **64 logical
> CPUs**, .NET 8.0.425, PowerShell 5.1). The **target** (Windows 11 + Core Ultra 7 155H + 32 GB) was
> **never available**, so all target-hardware CPU / memory / latency and the 3-hour soak are
> `NOT_TESTED` / `BLOCKED`. Where a value was not recorded, the row says so rather than estimating.

This report complements the live-path measurements in [`PERFORMANCE.md`](PERFORMANCE.md) and the
functionality results in [`FILE_TRANSCRIPTION_TEST_REPORT.md`](FILE_TRANSCRIPTION_TEST_REPORT.md).

## 1. ASR decode throughput (RTF) vs. thread count

Measured with `tools/AsrBenchmark` on the **same audio** and the **same model**
(`streaming-zipformer-zh-14M` INT8). The recognizer text was **identical** in every row.

| Threads | RTF | Text |
| --- | --- | --- |
| 2 | 0.0524 | `对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢` |
| 4 | **0.0481** | same |
| 8 | 0.0639 | same |
| 32 (old auto default) | **0.0999** | same |

**New default after the thread-cap fix:** the automatic thread count is capped at **4**
(`AsrThreadPolicy.MaxAutoThreads = 4`); the measured RTF with that default was **0.0396**.

Interpretation (as recorded at the time): on this 64-logical-CPU host the old default of 32 threads
was ~2× *slower* than 4 threads (0.0999 vs 0.0481) and also held 28 more cores. This is a throughput
observation on the dev host only.

## 2. Lexicon-boosting cost

Measured with `tools/AsrBenchmark` (same audio, 4 threads):

| Configuration | RTF |
| --- | --- |
| **116 hotwords** (built-in Huawei-domain lexicon) | 0.0594 |
| No hotwords | 0.0712 |

The lexicon did **not** slow decoding down here (it was faster, which is a different beam path, not a
guarantee). **The accuracy *gain* is not measured** — there is no domain test audio on the dev host
(see [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) P3-11).

## 3. Speaker diarization (V0.4, reused by V0.5)

Measured on a constructed **14.1 s, 2-speaker** clip (`fangjun-sr-1` ×2 + `leijun-sr-1` ×2) with the
**real** diarization models:

```
00:00.03 - 00:01.87  speaker 0  confidence 0.890
00:02.81 - 00:04.46  speaker 0  confidence 0.843
00:05.54 - 00:07.33  speaker 1  confidence 0.964
00:07.87 - 00:09.37  speaker 1  confidence 0.964
00:10.00 - 00:11.82  speaker 1  confidence 0.965
00:12.33 - 00:13.96  speaker 1  confidence 0.966
```

End-to-end service result: `success=True speakers=2 assigned=2 confirm=0`.

- **Recorded:** the intervals/confidences above and the 2-speaker split.
- **Not recorded:** a wall-clock RTF for the diarization step on this clip (the run was recorded as
  correct output, not as a timed throughput measurement) → *value: not recorded*.

## 4. File decode throughput (observed)

In `MediaToAsrEndToEndTests`, a **5.61 s** media file is **probed + decoded + transcribed** in
**~2 s** wall clock on the dev host (`0.mp4`; the decode and ASR steps are the ones this covers). This
is an observed **end-to-end wall time of the integration test**, not a per-file RTF on a long
recording.

- **Not measured:** decode/ASR throughput or memory on **long** (minutes-to-hours) files; the
  streaming design means the whole file is never buffered, but that was **not** benchmarked.

## 5. Phase 2 — long-audio offline file transcription (RTF)

Measured with the new **`tools/FileTranscribe`** CLI on the **development host** (Xeon, 64 logical
CPUs). `RTF = inference-seconds / audio-seconds`, printed by the tool.

> **Caveat:** on the 5.6 s clip the RTF is **dominated by model load**, not by inference (each run
> loads the model once), so 0.09–0.10 is *not* the marginal cost of decoding audio. It drops to
> ~0.05 on the longer fixtures, consistent with that amortisation.

### 5.1 5.612 s clip (`testmedia/0.mp4`) — one segment per mode

| Mode | Model | RTF |
| --- | --- | --- |
| Fast | `streaming-zipformer-zh-14M` | 0.0920 |
| Standard | `streaming-zipformer-zh-14M` (`modified_beam_search`) | 0.0974 |
| High | `sense-voice-small-int8` (offline) | 0.1024 |

### 5.2 Long fixtures (High-accuracy mode)

| Fixture | Audio | Segments | RTF |
| --- | --- | --- | --- |
| `testmedia/long-gaps.wav` | 65.115 s | 10 | 0.0508 |
| `testmedia/long-continuous.wav` | 56.115 s | 2 (30 s cap + 1.5 s overlap) | 0.0587 |

- **Measured:** the five RTF values above and the segment counts.
- **Not measured:** throughput / memory on a **multi-hour** file (no such fixture exists — the longest
  is 65 s), and the **target-hardware** RTF (the laptop is not available).

## 6. Methodology

- **RTF** = inference-seconds / audio-seconds; produced by `tools/AsrBenchmark` (it prints
  `RTF=<inference>/<audio>`). Thread counts were passed with `--threads <N>`.
- **File transcription (Phase 2):** `tools/FileTranscribe --file <path> --mode fast|standard|high`
  streams the FFmpeg decode straight into `OfflineTranscriptionEngine` and prints one line per segment
  plus `SEGMENTS` / `AUDIO_SECONDS` / `ELAPSED_SECONDS` / `RTF`.
- **File decode / first link**: `tests/LocalMeetingSubtitle.IntegrationTests` —
  `MediaDecodeTests` (probe + decode of real media) and `MediaToAsrEndToEndTests`
  (real media → FFmpeg PCM → sherpa-onnx → Chinese text).
- **Diarization**: the V0.4 integration tests over a constructed 2-speaker clip with the real
  pyannote + 3D-Speaker models.
- All runs were on the development host; **no number was extrapolated to the target**.

## 7. Targets vs. measured

Targets come from the product spec. "Measured" records only what was actually run.

| Metric | Target | Measured (dev host) | Target hardware |
| --- | --- | --- | --- |
| ASR RTF (live / offline decode) | ≤ 0.5 | **0.0396** (4-thread default) / 0.0481 (4 threads) | `NOT_TESTED` |
| Long-audio file transcription RTF | — | **0.0508** (65 s) / **0.0587** (56 s) / 0.0920–0.1024 (5.6 s, load-dominated) — High/Fast/Standard, dev host | `NOT_TESTED` |
| Diarization wall time | — | not recorded (correct output only) | `NOT_TESTED` |
| CPU | ≤ 25 % | `NOT_TESTED` | `NOT_TESTED` |
| Working set | ≤ 1 GB | not measured for file jobs | `NOT_TESTED` |
| 3-hour soak | ≥ 3 h | `NOT_TESTED` (`ThreeHourSoak` never executed) | `NOT_TESTED` / **BLOCKED** |

## 8. Explicit statement

**No figure in this report was measured on the target hardware, and none was extrapolated to it.**
The dev-host RTF comfortably beats the ≤ 0.5 target, but the target laptop's CPU is substantially
slower and this project makes **no claim** that the target targets are met. Target-hardware CPU,
memory, latency, long-audio throughput, and the 3-hour soak are all `NOT_TESTED` (the soak and the
live+file concurrency additionally `BLOCKED`). The Phase 2 file-transcription RTFs (§5) are
dev-host values on 5.6 s / 56 s / 65 s fixtures — **not** a multi-hour throughput claim.
