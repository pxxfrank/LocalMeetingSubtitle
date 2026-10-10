# File-Transcription Test Report (V0.5, Phase 0–1)

> **Scope note.** This report covers **what was actually executed** in the V0.5 Phase 0–1 work:
> provisioning FFmpeg and the `LocalMeetingSubtitle.Media` decode layer, plus the first end-to-end
> link *file → FFmpeg PCM → sherpa-onnx → Chinese text*. **Phases 2–8 are not started**, so nothing
> about segmented long-audio ASR, role-tagged dialogue, the job queue, resume, DOCX, or the dialogue
> editor is tested here. Every number is real; nothing is simulated, extrapolated, or invented.

## 1. Scope

| In scope (executed) | Out of scope (NOT_TESTED) |
| --- | --- |
| FFmpeg provisioning + LGPL verification | Segmented long-audio offline ASR (Phase 2) |
| Real media **probe** (audio + video containers) | Role-tagged dialogue / alignment (Phase 3) |
| Real media **decode** to 16 kHz mono float32 PCM | Job queue / checkpoint / resume (Phase 4) |
| **Offline ASR** over decoded audio → Chinese text | Dialogue editor UI (Phase 5) |
| Multi-track selection, no-audio, out-of-range, Unicode/space paths | TXT/Markdown/CSV/SRT/DOCX export for file jobs (Phase 6) |
| No-regression check of the live-subtitle suites | Long-audio accuracy (CER/WER), target hardware |

## 2. Environment

| Item | Value |
| --- | --- |
| Test host | **Windows 10 Pro** development host (am64) |
| **Target** (never available) | Windows 11 x64 · ThinkPad X1 Carbon Gen 12 · Core Ultra 7 155H · 32 GB → **all target-hardware acceptance is BLOCKED / NOT_TESTED** |
| .NET SDK | **8.0.425** |
| Shell | Windows PowerShell **5.1** |
| FFmpeg | `ffmpeg version N-127259-gb91a82d6dd-20261009`, LGPL v3 build (pinned) |
| Test media | `testmedia/` (gitignored), generated from the existing Chinese sample |
| ASR model | `streaming-zipformer-zh-14M` INT8 (streaming) |

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
| `UnitTests` | **159 passed / 0 failed** |
| `IntegrationTests` | **33 passed / 0 failed** |
| `PerformanceTests` | **3 passed / 1 skipped** (skipped = `ThreeHourSoak`, never executed) |
| `dotnet build LocalMeetingSubtitle.sln -c Release` | **0 errors** |

The 19 new integration tests (Phase 1) are `MediaDecodeTests` (15) + `MediaToAsrEndToEndTests` (4);
the live-subtitle suites are unaffected (**no regression**).

## 5. NOT covered / NOT_TESTED

The following are explicitly **not** tested in this report. Do not read them as "passing".

| Item | Why | Status |
| --- | --- | --- |
| **Long-audio accuracy (CER / WER)** | there is **no reference transcript** on the dev host, so accuracy cannot be measured | **NOT_TESTED** |
| **Segmented long-audio offline ASR** | Phase 2 not started | **NOT_TESTED** |
| **Role-tagged dialogue / alignment for file jobs** | Phase 3 not started | **NOT_TESTED** |
| **Job queue / checkpoint / resume** | Phase 4 not started | **NOT_TESTED** |
| **Dialogue editor UI** | Phase 5 not started | **NOT_TESTED** |
| **TXT / Markdown / CSV / SRT / DOCX export for file jobs** | Phase 6 not started | **NOT_TESTED** |
| **DOCX export** | no DOCX code exists | **NOT_TESTED** |
| **Resume after crash** | no checkpoint code exists | **NOT_TESTED** |
| **Decode throughput on long / multi-hour files; memory** | not measured | **NOT_TESTED** |
| **Live transcription + concurrent file job** | needs target hardware | **BLOCKED** |
| **Target hardware (Win11 + Core Ultra 7 155H): CPU / memory / latency** | target laptop not available | **BLOCKED / NOT_TESTED** |
| **FFmpeg inside the installer / ZIP** | packaging pending (Phase 8); the V0.4.0 installer exists but does **not** yet contain FFmpeg | **NOT_TESTED** |

## 6. Honest statement

Everything under "Results" was executed on the development host and is reproducible with the commands
in §3. Everything under §5 was **not** executed and is marked `NOT_TESTED` / `BLOCKED`. No result was
carried over from the target hardware, because that hardware was never available. The decode-level
evidence supports "the media layer and the first link work"; it does **not** support any claim about
long-audio accuracy, the dialogue pipeline, or the target machine.
