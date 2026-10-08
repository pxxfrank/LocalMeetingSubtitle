# Codex Progress

## Current phase

**Phase 10 complete — release candidate assembled, pending target-hardware acceptance.**
The product builds, tests pass on the development host, a real model decodes Chinese, offline
guarantees are verified, and a self-contained release artifact is produced. The remaining work is
**acceptance on the real Windows 11 target hardware**.

Status banner: **候选发布版本 — 待实机验收 / Release candidate — pending hardware acceptance.**

## Completed work by phase

| Phase | Scope | Outcome |
| --- | --- | --- |
| 0 | Solution scaffolding | `LocalMeetingSubtitle.sln`, `Directory.Build.props`, 7 product projects + `ModelDownloads`, 3 test projects, 4 tools |
| 1 | Core contracts & domain | Interfaces (`IAudioCaptureService`, `IAudioPreprocessor`, `IAsrEngine`, `IHotwordService`, repositories, `ISubtitleExportService`, `IPerformanceMonitor`, `IModelManager`), entities, primitives |
| 2 | Audio pipeline | `ChannelConverter`, `StreamingResampler`, `DefaultAudioPreprocessor`, `BoundedAudioQueue`, `AudioSegmenter`, `AudioMath` |
| 3 | WASAPI capture | `WasapiLoopbackCaptureService` (shared-mode loopback; never changes defaults/volume/mute/exclusive mode) |
| 4 | ASR integration | `SherpaOnnxAsrEngine` (streaming + offline), `AsrModelCatalog`, `AsrOptionsFactory`, `SherpaNativeProbe`, `MockAsrEngine`; sherpa-onnx C# API verified by reflection → `docs/SHERPA_CSHARP_API_DUMP.txt` |
| 5 | Transcription core | `TranscriptionPipeline` (capture→preprocess→queue→ASR→accumulator→persist), `SubtitleAccumulator` (partial/final, dedup, flush) |
| 6 | Persistence | `SqliteDatabase` (WAL, FK, migrations, `UNIQUE(SessionId,SequenceNumber)`), subtitle/hotword/settings repositories, `LocalDataPaths` |
| 7 | Hotwords & correction | `DefaultHotwordService`, `HotwordFileParser`, `HotwordValidator`, `ModelHotwordFile`, `TextCorrectionEngine`; transducer + `modified_beam_search` |
| 8 | Export & diagnostics | `SubtitleExportService` + TXT/SRT/Markdown formatters; `ProcessPerformanceMonitor` |
| 9 | UI / shell | WPF main window, settings window, floating subtitle overlay, tray icon, DI composition root, single-instance guard, file logger |
| 10 | Verification & release | Unit/integration/perf tests, real-model benchmark, WASAPI probe, offline verification, self-contained publish |

## Build result

- `dotnet build LocalMeetingSubtitle.sln` → **0 errors** (a few benign `CS0067` warnings in test fakes).
- SDK: .NET 8 SDK 8.0.425 (installed to `%USERPROFILE%\.dotnet`, not on `PATH`).

## Test results (development host)

| Project | Result |
| --- | --- |
| `UnitTests` | **112 passed / 0 failed** |
| `IntegrationTests` | **8 passed / 0 failed** |
| `PerformanceTests` | **3 passed / 1 skipped** (skipped = `ThreeHourSoak`, never executed) |

Additional evidence:

- Real ASR: `AsrBenchmark` (streaming-zipformer-zh-14M INT8, threads=4) → RTF 0.0466 (`0.wav`) and
  0.0492 (`1.wav`); load 2741 ms; working set 100 MB; Chinese text produced.
- WASAPI loopback: `AudioCaptureProbe capture` → 44100 Hz 2ch 32-bit float source → valid 16-bit WAV,
  RMS ≈ 0.345.
- Offline: `OfflineVerification` → **5/5 PASS** (native lib, no `System.Net.Http` reference,
  `%LOCALAPPDATA%` data dir, model present, offline decode produced text).
- Release: `dotnet publish -c Release -r win-x64 --self-contained true` →
  `dist/LocalMeetingSubtitle-win-x64/` (~184.7 MB, 495 files, incl. `onnxruntime.dll` 17.0 MB,
  `sherpa-onnx-c-api.dll` 4.4 MB). App smoke launch → process stayed alive (STILL_RUNNING) with
  successful startup logging (native=True 1.13.8, devices=1). The released ZIP bundles the model,
  and the app auto-detects it (see update below).

## Known issues summary

- **No open P0 defects.**
- Fixed: P1 (resampler emitted zero samples), P2 (mock engine could spin forever).
- Open P3 (minor): row-level selection; `EnableVadSegmenting` not wired; `AsrNumThreads` not live;
  simple hotword text box; floating-window resize not interactively verified.
- Blocked: full UI Start→transcribe→persist path; real 3-hour soak; code-signed installer.

See [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md).

## Next concrete tasks

1. **Target-hardware acceptance.** On the Windows 11 laptop (Core Ultra 7 155H, Arc, AI Boost):
   install and run, measure RTF / CPU / memory / first-and-final latency, and record results.
2. **Install the model into the app data directory and run a real meeting.** Copy/download the model
   into `%LOCALAPPDATA%\LocalMeetingSubtitle\models` and exercise the full UI path (Start → live
   subtitles → DB), closing BLOCKED-1.
3. **Run the 3-hour soak** (`ThreeHourSoak`) on a real meeting-like load; close BLOCKED-2.
4. **Benchmark candidate B/C** (bilingual zh-en; SenseVoice) on the target machine and finalise the
   (currently tentative) model choice.
5. **Code-sign the release if desired** (produce a signed installer; close BLOCKED-3).

## Blockers

- **No Windows 11 target hardware available** → all target-hardware items BLOCKED / NOT_TESTED.
- **No real meeting audio captured on the dev host** and the model was intentionally not installed in
  the app data dir → integrated UI path not exercised.

## Important commands

```powershell
# (If .NET 8 SDK is not on PATH)
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"

# Build
dotnet build LocalMeetingSubtitle.sln -c Release

# Tests
dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "Category=Performance"

# Model management (install into the app data dir)
dotnet run --project tools/ModelManager -- list
dotnet run --project tools/ModelManager -- install --id streaming-zipformer-zh-14M --models-root "$env:LOCALAPPDATA\LocalMeetingSubtitle\models"
dotnet run --project tools/ModelManager -- verify --models-root "$env:LOCALAPPDATA\LocalMeetingSubtitle\models"

# Real-model benchmark
dotnet run --project tools/AsrBenchmark -- --wav models/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23/test_wavs/0.wav --threads 4

# WASAPI loopback probe
dotnet run --project tools/AudioCaptureProbe -- list
dotnet run --project tools/AudioCaptureProbe -- capture --seconds 5

# Offline verification
dotnet run --project tools/OfflineVerification -- --wav models/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23/test_wavs/0.wav

# Run the app / publish
dotnet run --project src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj -c Debug
dotnet publish src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj -c Release -r win-x64 --self-contained true -o dist/LocalMeetingSubtitle-win-x64
```

## Update — portable model bundling + acceptance re-run (2026-10-08)

- **Defect found & fixed (P2, usability):** the app only looked for models in
  `%LOCALAPPDATA%\LocalMeetingSubtitle\models`, so the model bundled inside the release ZIP was not
  detected. `ResolveModelsRoot()` now prefers `<app>\models` when it exists, otherwise the per-user
  directory. Verified: after extracting `dist/LocalMeetingSubtitle-win-x64.zip` to a clean folder and
  running `LocalMeetingSubtitle.exe`, the log shows:
  `Using portable models directory: …\models` and
  `Startup check complete: native=True (1.13.8), model=streaming-zipformer-zh-14M installed=True, devices=1.`
  The UI shows “就绪 / Ready” with the model marked “已安装 / installed” and **Start enabled**.
- **Release ZIP rebuilt:** `dist/LocalMeetingSubtitle-win-x64.zip` (96.5 MB, 516 entries, forward-slash
  paths) containing `LocalMeetingSubtitle.exe`, the native sherpa-onnx/onnxruntime DLLs, the bundled
  model, docs, and a launcher.
- This materially strengthens **AC-20** (extract-and-run) and partially exercises the previously
  BLOCKED integrated path (engine + device selection + readiness), though a full live-meeting
  transcription run still requires real playback audio and is not yet done.

## Update — installer + signing (2026-10-08)

- Added `installer/` (WiX v4): `Product.wxs`, `generate-files-wxs.ps1` (emits one component per file),
  `Bundle.wxs` (Burn bootstrapper), and `build-installer.ps1` (end-to-end, reproducible).
- Produced and **signed** (self-signed cert, CurrentUser\My; the trust store was NOT modified):
  - `dist/LocalMeetingSubtitle-Setup.msi` — per-user MSI (no admin), installs to
    `%LOCALAPPDATA%\Programs\LocalMeetingSubtitle`, Start-menu shortcut, does not touch user data.
  - `dist/LocalMeetingSubtitle-Setup.exe` — Burn bundle chaining the MSI.
  - `dist/LocalMeetingSubtitle-win-x64/LocalMeetingSubtitle.exe` — the app exe.
- Signature status is **untrusted** (self-signed) → SmartScreen will still warn. A trusted
  signature requires a CA-issued OV/EV code-signing certificate, which was not available.
- WiX v7 was rejected: it requires accepting the Open Source Maintenance Fee EULA, which must not be
  accepted on the owner's behalf. WiX v4.0.6 (free) is used instead.

## Update — Material Design 3 UI (2026-10-08)

- The WPF UI was restyled to **Google Material Design 3** using `MaterialDesignThemes` 5.3.2
  (MIT) merged in `App.xaml` via `BundledTheme` + `MaterialDesign3.Defaults.xaml`
  (PrimaryColor=Blue, SecondaryColor=Teal).
- Main window: Material top app bar (`ColorZone` + elevation), status card with a floating-label
  combo box and a hotword chip, outlined cards for the subtitle stream, and an elevated action bar
  with icon buttons (`AppIconButton`, `AppPrimaryButton`, `AppActionButton` in `App.xaml`).
- Settings window: card-based sections (was GroupBox), floating-hint fields, Material sliders and
  check boxes. Floating subtitle overlay: rounded dark Material surface with a LIVE chip.
- **No functional change**: every binding, command, `x:Name` and event handler was preserved;
  all 123 automated tests still pass, and `OfflineVerification` is still 5/5 (runtime assemblies
  remain free of `System.Net.Http`); the theme library is UI-only.
- **Startup:** enabled `PublishReadyToRun` because the first cold launch of the published build was
  dominated by JIT/AV scan (measured >40 s before, ~14 s cold and ~2.3 s warm after).
- One UI bug found and fixed during the restyle: `MaterialDesignVerticalSeparator` does not exist in
  5.3.2 (would have thrown at window load) — replaced with a plain divider.
- Third-party notices updated with the two new MIT-licensed UI packages.
