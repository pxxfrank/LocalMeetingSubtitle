# File-Transcription Test Report (V0.5, Phase 0–3)

> **Scope note.** This report covers **what was actually executed** in the V0.5 Phase 0–3 work:
> provisioning FFmpeg, the `LocalMeetingSubtitle.Media` decode layer, the first end-to-end link
> *file → FFmpeg PCM → sherpa-onnx → Chinese text*, **Phase 2 segmented long-audio offline ASR**
> (VAD segmentation → per-segment offline decode → global timestamps, across the three modes), and
> **Phase 3 role-tagged dialogue** (offline diarization of the decoded file + transcript/speaker
> alignment into dialogue turns).
> **Phases 4–8 are not started**, so nothing about the job queue, resume, DOCX, or the dialogue editor
> is tested here. Every number is real; nothing is simulated, extrapolated, or invented.

## 1. Scope

| In scope (executed) | Out of scope (NOT_TESTED) |
| --- | --- |
| FFmpeg provisioning + LGPL verification | Job queue / checkpoint / resume (Phase 4) |
| Real media **probe** (audio + video containers) | Dialogue editor UI (Phase 5) |
| Real media **decode** to 16 kHz mono float32 PCM | TXT/Markdown/CSV/SRT/DOCX export for file jobs (Phase 6) |
| **Offline ASR** over decoded audio → Chinese text | Long-audio accuracy (CER/WER); target hardware |
| **Phase 2: segmented long-audio offline ASR** (VAD → per-segment decode → global timestamps; three modes; overlap de-dup) | Target-hardware acceptance (Win11); FFmpeg in the installer/ZIP (Phase 8) |
| **Phase 3: role-tagged dialogue** (diarization of the decoded file + transcript/speaker alignment → dialogue turns; video diarizable via the temp-WAV tee) | Target hardware (Win11); a diarized file > 14.1 s; long-audio accuracy |

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
| Two-speaker fixture (Phase 3) | `testmedia/two-speakers.wav` — **451,262 bytes ≈ 14.1 s** (speaker A ×2, gap, speaker B ×2), built by `tools/make-long-testmedia.ps1` from `models/_diar-eval/fangjun-sr-1.wav` + `leijun-sr-1.wav` |
| Diarization models (Phase 3) | `sherpa-onnx-pyannote-segmentation-3-0` + `3dspeaker-eres2net-base-zh-16k` under `models/` (V0.4 models, reused) |

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

# 8) Phase 3: role-tagged dialogue (diarize + align), against the prebuilt exe
tools\FileTranscribe\bin\Debug\net8.0-windows\FileTranscribe.exe --file testmedia\two-speakers.wav --mode high --diarize --models-root models

# 9) Build the long fixtures (gitignored testmedia/) from the bundled model's test_wavs/0.wav
#    (now also builds testmedia/two-speakers.wav)
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
| `UnitTests` | **208 passed / 0 failed** (190 at Phase 2; **+18** in Phase 3) |
| `IntegrationTests` | **43 passed / 0 failed** (41 at Phase 2; **+2** in Phase 3) |
| `PerformanceTests` | **3 passed / 1 skipped** (skipped = `ThreeHourSoak`, never executed) |
| `dotnet build LocalMeetingSubtitle.sln -c Release` | **0 errors** |

Phase 1 added 19 integration tests (`MediaDecodeTests` 15 + `MediaToAsrEndToEndTests` 4). Phase 2 added
31 unit tests (`OfflineSegmenterTimingTests`, `OverlapTextDeduplicatorTests`,
`OfflineTranscriptionEngineTests`, `TranscriptionModeCatalogTests`) and 8 integration tests
(`FileTranscriptionTests`: 4 theory + 4 fact). Phase 3 added 18 unit tests
(`TranscriptAlignmentServiceTests`, `FileTranscriptionServiceTests`, plus shared doubles in
`TestDoubles.cs`) and 2 integration tests (`FileTranscriptionDiarizationTests`). The live-subtitle
suites are unaffected (**no regression**).

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

### 4.6 Phase 3 — role-tagged dialogue

Executed with the built `FileTranscribe` CLI against the two-speaker fixture. The command (verbatim):

```powershell
tools\FileTranscribe\bin\Debug\net8.0-windows\FileTranscribe.exe --file testmedia\two-speakers.wav --mode high --diarize --models-root models
```

Output (verbatim, diary reduced):

```text
MODE=HighAccuracy DISPLAY=高精度 MODEL=sense-voice-small-int8 INSTALLED=True
FILE=…\testmedia\two-speakers.wav  KIND=Audio CONTAINER=wav DURATION=14.100s AUDIO_STREAMS=1
INFO  Diarization run d31ad9cf136c4742b9257ce35c2d0505: 225592 samples (00:00:14.0995000) from C:\Users\huawei\AppData\Local\Temp\dijob-7c91e0475a1b4fd9b1419f9f4b5a48d3.wav
INFO  Diarization run d31ad9cf136c4742b9257ce35c2d0505 succeeded: 2 speakers, 6/6 assigned, 0 need confirmation
INFO  File transcription finished: 6 segment(s), 2 turn(s), diarized=True, elapsed=3.2s.

[00:00:00.260 - 00:00:04.540] A: 今天是星期二。今天是星期二。
[00:00:05.580 - 00:00:13.820] B: 这是我第四次。办年度演讲。这是我第四次。办年度演讲。

PARTICIPANT B (11514447cd124912939705c37ae57500) speaking=8.2s turns=1 segments=4
PARTICIPANT A (e3e1918e142b41d98f82558f0de00cb0) speaking=4.3s turns=1 segments=2
PARTICIPANTS=2 TURNS=2
SEGMENTS=6  AUDIO_SECONDS=14.100  ELAPSED_SECONDS=3.216  RTF=0.2281
DIARIZED=True COMPLETED=True CANCELLED=False
```

The fixture's first half (speaker A, two segments) merged into **one A turn** and the second half
(speaker B, four segments) into **one B turn** — exactly **2 speakers and 2 turns**. The diarization ran
over a **temporary 16 kHz mono WAV** (`dijob-…wav` in `%TEMP%`) written by the tee of the decode pass —
the same path that makes a video diarizable.

**Cleanup (verified):** after the runs, **no** `dijob-*.wav`, **no** `ft-*.db`, `-wal` or `-shm`
remained in `%TEMP%`, and **no** leftover WAV remained in the staging directory.

**Coverage:** the Phase 3 integration tests are `FileTranscriptionDiarizationTests` — (a) a real
two-speaker file producing a 2-participant dialogue, and (b) a video file (`testmedia/two-tracks.mp4`,
audio track 1) proving the temp-WAV tee makes video diarizable. Unit tests are
`TranscriptAlignmentServiceTests` and `FileTranscriptionServiceTests` (with shared doubles in
`TestDoubles.cs`).

**Honest caveats for Phase 3:**

- The dialogue was exercised **only** on the **14.1 s** `two-speakers.wav` fixture. Diarization is a
  **whole-file** operation (V0.4 engine, cap **4 h**); a long diarized file was **not** run — see
  [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) P3-17.
- No **reference dialogue** exists, so the correctness of the speaker→turn assignment was checked by
  inspection of the output above, **not** against a ground-truth labelled transcript.
- `DiarizationProgress.Fraction` was `0.0` throughout (a **pre-existing V0.4 cosmetic** issue, not
  introduced by Phase 3) — see [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) P3-19.
- **Dev host only** (Windows 10 Pro; target Windows 11 / Core Ultra 7 155H **not** available) — nothing
  here is verified on the target laptop.
- **No UI** is wired for file transcription yet (Phase 5); this is the CLI path only.

## 5. NOT covered / NOT_TESTED

The following are explicitly **not** tested in this report. Do not read them as "passing".

| Item | Why | Status |
| --- | --- | --- |
| **Long-audio accuracy (CER / WER)** | there is **no reference transcript** on the dev host, so accuracy cannot be measured | **NOT_TESTED** |
| **A file longer than ~1 minute** | the only long fixtures are **56 s / 65 s** (Phase 2); nothing longer was run | **NOT_TESTED** |
| **SenseVoice model-level hotwords (High-accuracy mode)** | **verified unsupported** in sherpa-onnx 1.13.8 (the native build refuses) — a hard limitation, not a test gap; domain terms go through `TextCorrectionEngine` instead | **NOT SUPPORTED (verified)** |
| **Diarization of a long (> 14.1 s) or > 2 h file** | only the **14.1 s** `two-speakers.wav` fixture was diarized; diarization is whole-file (V0.4, cap 4 h) | **NOT_TESTED** |
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
"segmentation, per-segment decode and the three modes work on **56 s / 65 s** fixtures"; the Phase 3
evidence (§4.6) supports "diarization + alignment produce a 2-speaker, 2-turn dialogue for the
**14.1 s** two-speaker fixture, and the temp-WAV tee makes a video file diarizable". None of this
supports any claim about **long-audio accuracy**, a **file longer than ~1 minute**, **diarizing a long
file**, or the **target machine**.
