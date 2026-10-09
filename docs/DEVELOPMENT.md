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

## 6. Conventions

- `Directory.Build.props` sets `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`,
  `Version=0.1.0`. Warnings are not errors (`TreatWarningsAsErrors=false`).
- Comments are used only where the logic is non-obvious; the code favours small, single-purpose types.
- Only add error handling at true boundaries (I/O, native interop, cross-thread hand-off).
- Do not modify `.cs` / `.csproj` / `.sln` / `.xaml` when only documentation is in scope (see
  `README.md` for the doc set).
