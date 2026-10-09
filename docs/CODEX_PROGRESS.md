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
  `dist/字幕君-win-x64/` (~184.7 MB, 495 files, incl. `onnxruntime.dll` 17.0 MB,
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
   into `%LOCALAPPDATA%\字幕君\models` and exercise the full UI path (Start → live
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
dotnet run --project tools/ModelManager -- install --id streaming-zipformer-zh-14M --models-root "$env:LOCALAPPDATA\字幕君\models"
dotnet run --project tools/ModelManager -- verify --models-root "$env:LOCALAPPDATA\字幕君\models"

# Real-model benchmark
dotnet run --project tools/AsrBenchmark -- --wav models/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23/test_wavs/0.wav --threads 4

# WASAPI loopback probe
dotnet run --project tools/AudioCaptureProbe -- list
dotnet run --project tools/AudioCaptureProbe -- capture --seconds 5

# Offline verification
dotnet run --project tools/OfflineVerification -- --wav models/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23/test_wavs/0.wav

# Run the app / publish
dotnet run --project src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj -c Debug
dotnet publish src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj -c Release -r win-x64 --self-contained true -o dist/字幕君-win-x64
```

## Update — portable model bundling + acceptance re-run (2026-10-08)

- **Defect found & fixed (P2, usability):** the app only looked for models in
  `%LOCALAPPDATA%\字幕君\models`, so the model bundled inside the release ZIP was not
  detected. `ResolveModelsRoot()` now prefers `<app>\models` when it exists, otherwise the per-user
  directory. Verified: after extracting `dist/字幕君-win-x64.zip` to a clean folder and
  running `字幕君.exe`, the log shows:
  `Using portable models directory: …\models` and
  `Startup check complete: native=True (1.13.8), model=streaming-zipformer-zh-14M installed=True, devices=1.`
  The UI shows “就绪 / Ready” with the model marked “已安装 / installed” and **Start enabled**.
- **Release ZIP rebuilt:** `dist/字幕君-win-x64.zip` (96.5 MB, 516 entries, forward-slash
  paths) containing `字幕君.exe`, the native sherpa-onnx/onnxruntime DLLs, the bundled
  model, docs, and a launcher.
- This materially strengthens **AC-20** (extract-and-run) and partially exercises the previously
  BLOCKED integrated path (engine + device selection + readiness), though a full live-meeting
  transcription run still requires real playback audio and is not yet done.

## Update — installer + signing (2026-10-08)

- Added `installer/` (WiX v4): `Product.wxs`, `generate-files-wxs.ps1` (emits one component per file),
  `Bundle.wxs` (Burn bootstrapper), and `build-installer.ps1` (end-to-end, reproducible).
- Produced and **signed** (self-signed cert, CurrentUser\My; the trust store was NOT modified):
  - `dist/字幕君-Setup.msi` — per-user MSI (no admin), installs to
    `%LOCALAPPDATA%\Programs\字幕君`, Start-menu shortcut, does not touch user data.
  - `dist/字幕君-Setup.exe` — Burn bundle chaining the MSI.
  - `dist/字幕君-win-x64/字幕君.exe` — the app exe.
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

## Update — installer defect fixed; v0.2.0 assets replaced (2026-10-08)

- **P1 (release-blocking) found and fixed:** the MSI/Burn installer reported success but installed a
  broken app — `models\` was lost (so the ASR model was never found) and 238 files (the 12 localized
  resource folders + subfolders) were silently dropped, because the WiX source generator flattened
  every file into `INSTALLFOLDER`.
- Fixed by rebuilding `generator/…`: `installer/generate-files-wxs.ps1` now emits the **complete nested
  directory tree** (all ancestor prefixes) and places each component in its real folder. The MSI is also
  built with `-arch x64` now (it was being produced as a 32-bit package).
- **Re-verified end-to-end:** silent install → diff vs publish → **519 / 519 files, 0 missing**;
  `models\…\encoder…onnx`, `docs\USER_GUIDE.md`, `ja\…resources.dll`, `Run-字幕君.cmd`
  all present; Start-menu shortcut created; the installed app logs
  `model=streaming-zipformer-zh-14M installed=True, devices=1`.
- The portable **ZIP was never affected**; only the MSI/Bundle were broken — both have been rebuilt, and
  the GitHub release assets were replaced.
- **Second P1 found while verifying the bundle:** signing the Burn `Setup.exe` after build broke it
  (`0x80070002 Failed to acquire container: WixAttachedContainer`, exit 0x2, nothing installed) because
  Authenticode appends the signature at the end of the PE file and corrupts Burn's container location.
  `build-installer.ps1` no longer signs the bundle (the MSI and app exe stay signed). Re-verified:
  `Setup.exe /quiet` → exit 0, 519/519 files, model found, Start-menu shortcut created.

## Update — Claude-style UI, dark mode, icons; repo-completeness fix (2026-10-09)

- **UI rebuilt to a minimal "Claude (Anthropic)" style** and **MaterialDesignThemes removed entirely**
  (no third-party UI framework). New `src/LocalMeetingSubtitle.App/Theme/`:
  `Claude.Light.xaml` / `Claude.Dark.xaml` (warm neutrals + coral `#C96442` / `#D97757` accent),
  `Controls.xaml` (flat implicit styles), `Icons.xaml` (16 line-icon geometries, Lucide-derived),
  plus `Infrastructure/ThemeManager.cs` for runtime switching. `AppSettings.Theme` persists the choice.
- **Simplified layout:** the tall Material app bar is gone; the main window is now a slim 2-row header
  (title, status pill, theme toggle, settings), a single toolbar (device + transport controls), the
  subtitle list as the visual focus, and a slim footer. The duplicated model caption was removed.
- **Dark mode** verified by sampling rendered pixels (input interior luminance ~48 with text pixels at 255).
- **P0 repository defect found and fixed:** `.gitignore` had an unanchored `models/` rule. Because git is
  case-insensitive on Windows, that also ignored `src/LocalMeetingSubtitle.Core/Models/` — the four Core
  model files (`Primitives.cs`, `Entities.cs`, `AsrModels.cs`, `TranscriptModels.cs`) were **never
  committed**, so a fresh clone of the public repo could not compile. The rule is now anchored (`/models/`)
  and the files are committed.
- **Two UI defects found and fixed while verifying dark mode:**
  1. `Controls.xaml` referenced `{StaticResource IconChevronDown}` while `Icons.xaml` had not yet been
     merged → `StaticResourceHolder` exception → the main window failed to create. Changed to
     `{DynamicResource}`.
  2. The stock WPF (Aero2) ComboBox/TextBox templates paint a **hardcoded white** background and ignore
     `Background`/`SystemColors`, so dark mode showed light text on white. Replaced with explicit,
     fully themed templates.
- Version bumped to **0.3.0**; installers and the portable ZIP rebuilt and re-signed (bundle intentionally
  unsigned, per the note above).

## Update — renamed to 字幕君 (v0.3.1) (2026-10-09)

- The application is now named **字幕君** everywhere user-visible: window / header / tray titles, the floating
  subtitle window (`字幕君 悬浮字幕`), all message boxes, the executable (`字幕君.exe`), the installer product
  name and install directory (`%LOCALAPPDATA%\Programs\字幕君`), the Start-menu folder and shortcut, the
  uninstall registry key, and the release artifacts (`字幕君-Setup.*`, `字幕君-win-x64.zip`).
- The **user data directory** moved to `%LOCALAPPDATA%\字幕君\` (database / logs / models / exports).
  There is **no automatic migration** — existing data must be moved manually.
- Internal identifiers (assembly names `LocalMeetingSubtitle.*`, namespaces, project folders, `.sln`) were
  intentionally left unchanged; users never see them.
- Files that contain Chinese are saved as **UTF-8 with BOM**: the dev host ANSI codepage is 936 (GBK), so
  PowerShell 5.1 would otherwise mis-decode the `.ps1` scripts.
- **Startup-crash defect introduced by the rename and fixed:** an HTTP `User-Agent` header value must be
  ASCII, so `ParseAdd("字幕君/0.1")` threw `FormatException` and the app exited on launch. Now ASCII
  (`SubtitleJun/0.1`). Every other use of the name (mutex, message boxes, temp dir, registry) is verified
  to be non-ASCII-safe.
- Version bumped to **0.3.1**; MSI + Burn bundle + portable ZIP rebuilt with the ASR model bundled
  (parity with 0.3.0) and re-signed (bundle intentionally unsigned).
