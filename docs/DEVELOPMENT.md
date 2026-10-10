# Development Guide

All commands are **Windows PowerShell 5.1** syntax (use `;` to chain commands, not `&&`).

## 1. Prerequisites

- **Windows 10/11 (x64)**.
- **.NET 8 SDK (8.0.x)**. On the development host it is installed *per-user* at
  `%USERPROFILE%\.dotnet` and is **not on `PATH`**; prefix your session with:

  ```powershell
  $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
  ```

  Verify with `dotnet --version` (expected 8.0.425 on the dev host).
- No internet is required to build, test, or run recognition once a model is present.

## 2. Restore / build / test / publish

```powershell
# Restore (usually implicit)
dotnet restore LocalMeetingSubtitle.sln

# Build
dotnet build LocalMeetingSubtitle.sln -c Release

# Tests
dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "Category=Performance"

# Real-model speaker-diarization validation (integration tests; they SKIP automatically when the two
# diarization models + the two-speaker eval wavs are not present under models/)
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~SpeakerDiarizationRealModelTests"

# Run the app (development)
dotnet run --project src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj -c Debug

# Self-contained release publish
dotnet publish src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj `
    -c Release -r win-x64 --self-contained true -o dist/字幕君-win-x64
```

## 3. Project structure

```
src/
  LocalMeetingSubtitle.App            WPF UI + DI composition root (exe: 字幕君.exe)
  LocalMeetingSubtitle.Core           contracts, models, audio math, pipeline, hotwords/correction
  LocalMeetingSubtitle.Audio          NAudio WASAPI loopback capture
  LocalMeetingSubtitle.Asr            sherpa-onnx engine, model catalog, native probe, mock engine
  LocalMeetingSubtitle.Storage        SQLite (WAL) + repositories + LocalDataPaths
  LocalMeetingSubtitle.Export         TXT / SRT / Markdown export
  LocalMeetingSubtitle.Diagnostics    process performance monitor
  LocalMeetingSubtitle.ModelDownloads HttpModelManager (the only System.Net.Http user; isolated)
tests/
  LocalMeetingSubtitle.UnitTests
  LocalMeetingSubtitle.IntegrationTests
  LocalMeetingSubtitle.PerformanceTests
tools/
  ModelManager                        list / install / verify models
  AudioCaptureProbe                   list / capture WASAPI loopback
  AsrBenchmark                        decode a WAV, report timing / RTF / memory
  OfflineVerification                 5 offline-capability checks
  FileTranscribe                      transcribe a local audio/video file (three modes)
```

Runtime data lives under `%LOCALAPPDATA%\SubtitleJun\` (`subtitles.db`, `logs\`, `models\`,
`exports\`). The repository's `models/` and `dist/` directories are git-ignored.

## 4. Working with models

Models are described declaratively in `src/LocalMeetingSubtitle.Asr/AsrModelCatalog.cs` and managed at
development/install time by the `ModelManager` tool. Recognition itself reads only the files on disk
under the configured models root.

```powershell
# List the catalog and which models are installed under the default models root
dotnet run --project tools/ModelManager -- list

# Install a model into the repository models/ root (default)
dotnet run --project tools/ModelManager -- install --id streaming-zipformer-zh-14M

# Install into the APPLICATION data dir so the app can use it
dotnet run --project tools/ModelManager -- install --id streaming-zipformer-zh-14M `
    --models-root "$env:LOCALAPPDATA\SubtitleJun\models"

# Verify presence (and list any missing files)
dotnet run --project tools/ModelManager -- verify --models-root "$env:LOCALAPPDATA\SubtitleJun\models"
```

### Diarization models (V0.4.0)

Offline speaker diarization needs **two additional models** — a pyannote segmentation model and a
3D-Speaker speaker-embedding model. They are declared in `DiarizationModelCatalog.cs` (deliberately
kept separate from `AsrModelCatalog.cs`) and are installed the same way:

```powershell
# Segmentation (pyannote) + embedding (3D-Speaker) into the repository models/ root
dotnet run --project tools/ModelManager -- install --id pyannote-segmentation-3-0
dotnet run --project tools/ModelManager -- install --id 3dspeaker-eres2net-base-zh-16k

# …or into the APPLICATION data dir so the app can diarize
dotnet run --project tools/ModelManager -- install --id pyannote-segmentation-3-0 `
    --models-root "$env:LOCALAPPDATA\SubtitleJun\models"
dotnet run --project tools/ModelManager -- install --id 3dspeaker-eres2net-base-zh-16k `
    --models-root "$env:LOCALAPPDATA\SubtitleJun\models"
```

`list` / `verify` / `install` see the diarization models too: the tool defaults to a combined
catalog (`--catalog all`); pass `--catalog asr` or `--catalog diarization` to restrict it:

```powershell
dotnet run --project tools/ModelManager -- list --catalog all
dotnet run --project tools/ModelManager -- verify --catalog diarization `
    --models-root "$env:LOCALAPPDATA\SubtitleJun\models"
```

- **Default models root:** the tool walks up from its own directory to find the solution
  (`*.sln`) and uses `models/` beside it; override with `--models-root <path>`.
- **App models root:** the application always uses `%LOCALAPPDATA%\SubtitleJun\models`.
  To let the app start, install the model there (see above).
- **Adding a *new* model** (one not already in the catalog) requires adding a `ModelDescriptor` to
  `AsrModelCatalog.cs` (file names, URLs, license, hotword support) — this is a **code change**, so it
  is intentionally outside the scope of the documentation-only work in this repository state. Once a
  descriptor exists, the tool downloads/verifies it with no further code.

The candidate model repositories are documented in [`MODEL_SELECTION.md`](MODEL_SELECTION.md).

## 5. Useful development tools

```powershell
# Inspect render devices
dotnet run --project tools/AudioCaptureProbe -- list
# Capture 5 s of system playback to a WAV
dotnet run --project tools/AudioCaptureProbe -- capture --seconds 5

# Benchmark the real model on a sample WAV
dotnet run --project tools/AsrBenchmark -- --wav models/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23/test_wavs/0.wav --threads 4

# Prove the offline claim (native lib, no network refs, data dir, model, offline decode)
dotnet run --project tools/OfflineVerification -- --wav models/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23/test_wavs/0.wav
```

## 6. File transcription / FFmpeg (V0.5)

V0.5 decodes local audio/video through a **separately distributed LGPL FFmpeg** (see
[`LICENSES.md`](LICENSES.md)). FFmpeg is **not** committed; it is fetched on demand.

### 6.1 Fetch FFmpeg

```powershell
# Download + verify (SHA-256) + extract the pinned LGPL build into third_party/ffmpeg (gitignored)
./tools/fetch-ffmpeg.ps1
./tools/fetch-ffmpeg.ps1 -Force   # re-download even if present
```

The script pins the artifact by **URL + SHA-256** (`85e26d3d…58a55`), extracts only the runtime files
(`ffmpeg.exe`, `ffprobe.exe`, the `av*` / `sw*` DLLs, `LICENSE.txt`), then runs `ffmpeg -version` and
**refuses** a GPL/nonfree build. At runtime `FFmpegLocator` searches, in order: an explicit directory,
`<app>\ffmpeg\bin` (the published layout), the dev tree `third_party\ffmpeg\bin` (walking up from the
app), then `PATH`. A clone **without** FFmpeg still builds; only file transcription is unavailable
(`MediaErrorKind.ToolMissing`).

### 6.2 Generate the test media

`testmedia/` is **gitignored**. Generate it from the existing Chinese sample with the fetched FFmpeg —
every file is ~5.6 s (the length of the source `0.wav`):

```powershell
$ff  = "third_party/ffmpeg/bin/ffmpeg.exe"
$src = "models/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23/test_wavs/0.wav"
New-Item -ItemType Directory -Force -Path testmedia | Out-Null

# audio-only containers
& $ff -y -i $src -ac 1 testmedia/0.mp3
& $ff -y -i $src -ac 1 testmedia/0.m4a
& $ff -y -i $src -ac 1 -c:a aac testmedia/0.aac
& $ff -y -i $src -ac 1 testmedia/0.flac
& $ff -y -i $src -ac 1 testmedia/0.ogg
& $ff -y -i $src -ac 1 testmedia/0.mkv
& $ff -y -i $src -ac 1 testmedia/0.mov
& $ff -y -i $src -ac 1 testmedia/0.avi

# video container (mpeg4 video + aac audio): proves audio extraction from a video
& $ff -y -i $src -f lavfi -i color=c=black:s=320x240 -shortest -c:v mpeg4 -c:a aac testmedia/0.mp4

# video only (no audio): must raise NoAudioTrack
& $ff -y -f lavfi -i color=c=black:s=320x240 -t 5.6 -c:v mpeg4 -an testmedia/noaudio.mp4

# two audio tracks: track selection / out-of-range
& $ff -y -i $src -i $src -map 0:a -map 1:a -c:a aac testmedia/two-tracks.mp4
```

> The exact durations follow the source `0.wav` (~5.6 s); the tests assert a 5.0–6.5 s window rather
> than a fixed value.

### 6.3 Tests

```powershell
# Decode layer: probe + decode of real media (real FFmpeg)
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj `
    -c Debug --filter "FullyQualifiedName~MediaDecodeTests"

# First end-to-end link: media -> FFmpeg PCM -> sherpa-onnx -> Chinese text
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj `
    -c Debug --filter "FullyQualifiedName~MediaToAsrEndToEndTests"

# Phase 2: segmented long-audio offline ASR (VAD -> per-segment decode -> timestamps; three modes)
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj `
    -c Debug --filter "FullyQualifiedName~FileTranscriptionTests"
```

Both suites **skip** (they never fake a result) when `testmedia/`, the bundled FFmpeg, or the ASR
model are absent, so a clean clone stays green.

### 6.4 Transcribe a file offline (`tools/FileTranscribe`)

`tools/FileTranscribe` is a CLI that streams `IMediaDecodeService.DecodeAsync` straight into
`OfflineTranscriptionEngine` (it never buffers the file) and prints one line per segment followed by
`SEGMENTS` / `AUDIO_SECONDS` / `ELAPSED_SECONDS` / `RTF` (or JSON with `--json`).

```powershell
# The three modes (Fast / Standard / High-accuracy)
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode fast
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode standard
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode high

# With options
dotnet run --project tools/FileTranscribe -- --file <path> --mode high --models-root models --track 0 --json
```

Flags: `--file <path>` (required), `--mode fast|standard|high`, `--models-root <path>`,
`--track N` (audio-stream ordinal), `--hotwords <file>`, `--lexicon`, `--json`.

Output line format: `[hh:mm:ss.fff - hh:mm:ss.fff] (#chunk modelId) text`.

> The **High-accuracy** mode needs the offline `sense-voice-small-int8` model installed — it is **not**
> bundled by default. Install it with
> `dotnet run --project tools/ModelManager -- install --id sense-voice-small-int8`. The other two modes
> use the bundled streaming `streaming-zipformer-zh-14M`. Per the verified constraint, the High-accuracy
> mode does **not** use model-level hotwords (see [`MODEL_SELECTION.md`](MODEL_SELECTION.md)).

### 6.5 Long test fixtures (`tools/make-long-testmedia.ps1`)

`tools/make-long-testmedia.ps1` builds two longer fixtures from the bundled model's `test_wavs/0.wav`
using the fetched FFmpeg:

- `testmedia/long-continuous.wav` — **~56 s of unbroken speech** (the clip joined back-to-back);
- `testmedia/long-gaps.wav` — **~65 s** of the same clip separated by **1-second silences**.

```powershell
./tools/make-long-testmedia.ps1
dotnet run --project tools/FileTranscribe -- --file testmedia/long-continuous.wav --mode high
dotnet run --project tools/FileTranscribe -- --file testmedia/long-gaps.wav --mode high
```

`testmedia/` is **gitignored** (as is `third_party/`), so these fixtures are **never committed** —
regenerate them locally before running long-audio work. They are the fixtures behind the Phase 2
evidence in [`FILE_TRANSCRIPTION_TEST_REPORT.md`](FILE_TRANSCRIPTION_TEST_REPORT.md) (§4.5).

## 7. Conventions

- `Directory.Build.props` sets `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`,
  `Version=0.1.0`. Warnings are not errors (`TreatWarningsAsErrors=false`).
- Comments are used only where the logic is non-obvious; the code favours small, single-purpose types.
- Only add error handling at true boundaries (I/O, native interop, cross-thread hand-off).
- Do not modify `.cs` / `.csproj` / `.sln` / `.xaml` when only documentation is in scope (see
  `README.md` for the doc set).
