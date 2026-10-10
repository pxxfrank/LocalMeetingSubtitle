# File-Transcription Test Report (V0.5, Phase 0–2)

> **Scope note.** This report covers **what was actually executed** in the V0.5 Phase 0–2 work:
> provisioning FFmpeg, the `LocalMeetingSubtitle.Media` decode layer, the first end-to-end link
> *file → FFmpeg PCM → sherpa-onnx → Chinese text*, and **Phase 2 segmented long-audio offline ASR**
> (VAD segmentation → per-segment offline decode → global timestamps, across the three modes).
> **Phases 3–8 are not started**, so nothing about role-tagged dialogue, the job queue, resume, DOCX,
> or the dialogue editor is tested here. Every number is real; nothing is simulated, extrapolated, or
> invented.

## 1. Scope

| In scope (executed) | Out of scope (NOT_TESTED) |
| --- | --- |
| FFmpeg provisioning + LGPL verification | Role-tagged dialogue / alignment (Phase 3) |
| Real media **probe** (audio + video containers) | Job queue / checkpoint / resume (Phase 4) |
| Real media **decode** to 16 kHz mono float32 PCM | Dialogue editor UI (Phase 5) |
| **Offline ASR** over decoded audio → Chinese text | TXT/Markdown/CSV/SRT/DOCX export for file jobs (Phase 6) |
| **Phase 2: segmented long-audio offline ASR** (VAD → per-segment decode → global timestamps; three modes; overlap de-dup) | Long-audio accuracy (CER/WER); target hardware |
| Multi-track selection, no-audio, out-of-range, Unicode/space paths; no-regression of the live suites | Target-hardware acceptance (Win11); FFmpeg in the installer/ZIP (Phase 8) |

## 2. Environment

| Item | Value |
| --- | --- |
| Test host | **Windows 10 Pro** development host (am64) |
| **Target** (never available) | Windows 11 x64 · ThinkPad X1 Carbon Gen 12 · Core Ultra 7 155H · 32 GB → **all target-hardware acceptance is BLOCKED / NOT_TESTED** |
| .NET SDK | **8.0.425** |
| Shell | Windows PowerShell **5.1** |
| FFmpeg | `ffmpeg version N-127259-gb91a82d6dd-20261009`, LGPL v3 build (pinned) |
| Test media | `testmedia/` (gitignored), generated from the existing Chinese sample |
| ASR model (Fast / Standard) | `streaming-zipformer-zh-14M` INT8 (streaming) |
| ASR model (High accuracy) | `sense-voice-small-int8` (offline; `model.int8.onnx` **239,233,841 B** + `tokens.txt`, dir `models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17`; **sha256 not recorded**) |
| Long fixtures (Phase 2) | `testmedia/long-continuous.wav` (**56.115 s** unbroken speech) and `testmedia/long-gaps.wav` (**65.115 s** — the same clip split by 1 s silences), built by `tools/make-long-testmedia.ps1` |

No test in this report was run on the target laptop.

## 3. Exact commands run

```powershell
# 0) .NET 8 SDK (dev host: installed per-user, not on PATH)
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"

# 1) Provision the pinned LGPL FFmpeg into third_party/ffmpeg (verifies SHA-256 + LGPL config)
./tools/fetch-ffmpeg.ps1

# 2) Generate the test media from the existing Chinese sample (see DEVELOPMENT.md for the recipe)
#    -> testmedia/0.mp3 0.m4a 0.aac 0.flac 0.ogg 0.mkv 0.mov 0.avi 0.mp4 noaudio.mp4 two-tracks.mp4

# 3) Build
dotnet build LocalMeetingSubtitle.sln -c Release

# 4) Decode-layer tests (real media + real FFmpeg; skip when the samples/tools are absent)
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj `
    -c Debug --filter "FullyQualifiedName~MediaDecodeTests"

# 5) First end-to-end link: media -> FFmpeg PCM -> offline ASR -> Chinese
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj `
    -c Debug --filter "FullyQualifiedName~MediaToAsrEndToEndTests"

# 6) Full suites (regression check)
dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "Category=Performance"

# 7) Phase 2: segmented long-audio offline ASR (FileTranscribe CLI, three modes)
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode fast
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode standard
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode high
dotnet run --project tools/FileTranscribe -- --file testmedia/long-gaps.wav --mode high
dotnet run --project tools/FileTranscribe -- --file testmedia/long-continuous.wav --mode high

# 8) Build the long fixtures (gitignored testmedia/) from the bundled model's test_wavs/0.wav
./tools/make-long-testmedia.ps1
```

**Commands FFmpeg runs (from the code, not typed by hand):**

```text
probe : ffprobe -v error -print_format json -show_format -show_streams <file>
decode: ffmpeg -v error -i <file> -map 0:a:<N> -vn -sn -dn -f f32le -acodec pcm_f32le -ac 1 -ar 16000 pipe:1
```

All arguments are passed through `ProcessStartInfo.ArgumentList` (no shell).

## 4. Results — real media (development host)

### 4.1 Probe (audio containers)

`MediaDecodeTests.Probe_reports_audio_and_a_valid_duration` — every sample reports **≥1 audio stream**
and a **valid ~5.6 s duration**:

| File | Result |
| --- | --- |
| `0.mp3`, `0.m4a`, `0.aac`, `0.flac`, `0.ogg`, `0.mkv`, `0.mov`, `0.avi` | `HasAudio=true`, `AudioStreams ≥ 1`, duration within 5.0–6.5 s — **PASS** |

### 4.2 Decode + first end-to-end link (`MediaToAsrEndToEndTests`)

Real media → FFmpeg PCM → sherpa-onnx offline recognition → Chinese:

| File | Probe | Decoded | ASR text |
| --- | --- | --- | --- |
| `0.mp4` (video, mpeg4 + aac) | `kind=Video container=mov,mp4,m4a,3gp,3g2,mj2 duration=5.61s audioStreams=1` | **89 784 samples = 5.61 s** | `对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢` |
| `0.mkv` | container `matroska`, `5.63 s` | decodes → text | (same Chinese text) |
| `0.ogg` | container `ogg`, `5.63 s` | decodes → text | (same Chinese text) |
| `0.mp3` | container `mp3`, `5.64 s` | decodes → text | (same Chinese text) |

The video file's audio track is extracted from the **video** container — i.e. "extract video audio"
is proven, not assumed.

### 4.3 Edge cases

| Case | Input | Result | Status |
| --- | --- | --- | --- |
| Video with **no audio** | `noaudio.mp4` | `HasAudio=false`, `MediaKind=Video`; decode throws `MediaDecodeException(NoAudioTrack)` | **PASS** |
| **Second audio track** | `two-tracks.mp4` (2× aac), `AudioStreamIndex=1` | decodes to 16 kHz mono, 5–6.5 s | **PASS** |
| **Out-of-range** track | `two-tracks.mp4`, `AudioStreamIndex=9` | `MediaDecodeException(StreamIndexOutOfRange)` | **PASS** |
| **Unicode + space** path | `…\测试 folder\0 拷贝.mp3` | decodes to 16 kHz mono, 5–6.5 s | **PASS** |
| **Missing file** | non-existent path | `MediaDecodeException(FileNotFound)` | **PASS** |

### 4.4 Suite totals

| Project | Result |
| --- | --- |
| `UnitTests` | **190 passed / 0 failed** (159 at Phase 1; **+31** in Phase 2) |
| `IntegrationTests` | **41 passed / 0 failed** (33 at Phase 1; **+8** in Phase 2) |
| `PerformanceTests` | **3 passed / 1 skipped** (skipped = `ThreeHourSoak`, never executed) |
| `dotnet build LocalMeetingSubtitle.sln -c Release` | **0 errors** |

Phase 1 added 19 integration tests (`MediaDecodeTests` 15 + `MediaToAsrEndToEndTests` 4). Phase 2 added
31 unit tests (`OfflineSegmenterTimingTests`, `OverlapTextDeduplicatorTests`,
`OfflineTranscriptionEngineTests`, `TranscriptionModeCatalogTests`) and 8 integration tests
(`FileTranscriptionTests`: 4 theory + 4 fact). The live-subtitle suites are unaffected (**no
regression**).

### 4.5 Phase 2 — segmented long-audio offline ASR

Executed with the new `tools/FileTranscribe` CLI, which streams `IMediaDecodeService.DecodeAsync`
straight into `OfflineTranscriptionEngine` (it never buffers the file). Output line format:
`[hh:mm:ss.fff - hh:mm:ss.fff] (#chunk modelId) text`, followed by `SEGMENTS` / `AUDIO_SECONDS` /
`ELAPSED_SECONDS` / `RTF`.

**Three modes on the same 5.612 s clip (`testmedia/0.mp4`, FFmpeg-decoded from the video container):**

```powershell
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode fast
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode standard
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode high
```

| Mode | Segment (verbatim) | Segments | RTF |
| --- | --- | --- | --- |
| Fast | `[00:00:00.000 - 00:00:05.380] (#1 streaming-zipformer-zh-14M) 对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢。` | 1 | 0.0920 |
| Standard | `[00:00:00.000 - 00:00:05.380] (#1 streaming-zipformer-zh-14M) 对我做了介绍那么我想说的是呢大家如果对我的研究感兴趣呢。` | 1 | 0.0974 |
| High | `[00:00:00.000 - 00:00:05.380] (#1 sense-voice-small-int8) 对我做了介绍啊，那么我想说的是呢，大家如果对我的研究感兴趣呢。` | 1 | 0.1024 |

Standard differs from Fast (a `呢` is placed earlier) — so `modified_beam_search` is really active — and
High is the only **punctuated** output (SenseVoice ITN).

**Long fixtures (High-accuracy mode):**

```powershell
./tools/make-long-testmedia.ps1   # -> testmedia/long-continuous.wav (56.115 s), testmedia/long-gaps.wav (65.115 s)
dotnet run --project tools/FileTranscribe -- --file testmedia/long-gaps.wav --mode high
dotnet run --project tools/FileTranscribe -- --file testmedia/long-continuous.wav --mode high
```

`testmedia/long-gaps.wav` (**65.115 s**) → **10 segments**, all monotonic and non-overlapping; RTF **0.0508**:

| # | Start – End (s) |
| --- | --- |
| 1 | 0.000 – 5.380 |
| 2 | 5.980 – 11.980 |
| 3 | 12.580 – 18.600 |
| 4 | 19.200 – 25.220 |
| 5 | 25.820 – 31.820 |
| 6 | 32.420 – 38.440 |
| 7 | 39.040 – 45.040 |
| 8 | 45.640 – 51.659 |
| 9 | 52.260 – 58.280 |
| 10 | 58.880 – 64.880 |

`testmedia/long-continuous.wav` (**56.115 s**, unbroken speech) → **2 segments**; RTF **0.0587**:

| # | Start – End (s) | Note |
| --- | --- | --- |
| 1 | 0.000 – 30.000 | the 30 s cap fired |
| 2 | 28.500 – 55.880 | restarts at **28.500 s = 30.000 − 1.500 s** (the configured 1.5 s overlap) |

The second region genuinely restarts 1.5 s **before** the first ends — and the carried-over text is
**not** duplicated, i.e. the overlap de-duplication works.

**SenseVoice hotword A/B (`tools/AsrBenchmark --offline --model-id sense-voice-small-int8`):**

- With `--hotwords` and default decoding the native layer refuses to build the recognizer:
  `offline-recognizer.cc:Validate:88 Please use --decoding-method=modified_beam_search if you provide --hotwords-file. Given --decoding-method='greedy_search'`.
- With `--decoding modified_beam_search` it **also** refuses:
  `offline-recognizer-sense-voice-impl.h:82 Only greedy_search is supported at present. Given modified_beam_search`.
- **Conclusion (verified):** SenseVoice **cannot** use model-level hotwords in sherpa-onnx 1.13.8.
  Domain terms for the High-accuracy mode must go through `TextCorrectionEngine` (correction rules).

## 5. NOT covered / NOT_TESTED

The following are explicitly **not** tested in this report. Do not read them as "passing".

| Item | Why | Status |
| --- | --- | --- |
| **Long-audio accuracy (CER / WER)** | there is **no reference transcript** on the dev host, so accuracy cannot be measured | **NOT_TESTED** |
| **A file longer than ~1 minute** | the only long fixtures are **56 s / 65 s** (Phase 2); nothing longer was run | **NOT_TESTED** |
| **SenseVoice model-level hotwords (High-accuracy mode)** | **verified unsupported** in sherpa-onnx 1.13.8 (the native build refuses) — a hard limitation, not a test gap; domain terms go through `TextCorrectionEngine` instead | **NOT SUPPORTED (verified)** |
| **Role-tagged dialogue / alignment for file jobs** | Phase 3 not started | **NOT_TESTED** |
| **Job queue / checkpoint / resume** | Phase 4 not started | **NOT_TESTED** |
| **Dialogue editor UI** | Phase 5 not started | **NOT_TESTED** |
| **TXT / Markdown / CSV / SRT / DOCX export for file jobs** | Phase 6 not started | **NOT_TESTED** |
| **DOCX export** | no DOCX code exists | **NOT_TESTED** |
| **Resume after crash** | no checkpoint code exists | **NOT_TESTED** |
| **Decode / ASR throughput or memory on multi-hour files** | not measured | **NOT_TESTED** |
| **Live transcription + concurrent file job** | needs target hardware | **BLOCKED** |
| **Target hardware (Win11 + Core Ultra 7 155H): CPU / memory / latency** | target laptop not available | **BLOCKED / NOT_TESTED** |
| **FFmpeg inside the installer / ZIP** | packaging pending (Phase 8); the V0.4.0 installer exists but does **not** yet contain FFmpeg | **NOT_TESTED** |

## 6. Honest statement

Everything under "Results" was executed on the development host and is reproducible with the commands
in §3. Everything under §5 was **not** executed and is marked `NOT_TESTED` / `BLOCKED`. No result was
carried over from the target hardware, because that hardware was never available. The decode-level
evidence supports "the media layer and the first link work"; the Phase 2 evidence (§4.5) supports
"segmentation, per-segment decode and the three modes work on **56 s / 65 s** fixtures". Neither
supports any claim about **long-audio accuracy**, a **file longer than ~1 minute**, the dialogue
pipeline, or the target machine.
