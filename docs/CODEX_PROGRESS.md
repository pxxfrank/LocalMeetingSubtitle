# Codex Progress

## Current phase

**V0.4.0 released (HEAD `554c8eb`); V0.5 "offline file transcription + role-tagged dialogue" is in
progress — Phases 0–1 complete, Phase 2 not started.**

The V0.4 live-subtitle product builds, its tests pass on the development host, a real model decodes
Chinese, offline guarantees are verified, and a self-contained release artifact is produced. V0.5 adds
a bundled **LGPL FFmpeg** media-decode layer (import audio/video → 16 kHz mono PCM). The remaining V0.5
work (segmented offline ASR, role-tagged dialogue, job queue, editor UI, export, packaging) is **not
started**, and the remaining work overall is **acceptance on the real Windows 11 target hardware**.

Status banner: **候选发布版本 — 待实机验收；V0.5（离线文件转写）进行中 / Release candidate (V0.4.0) — pending hardware acceptance; V0.5 (offline file transcription) in progress.**

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
   into `%LOCALAPPDATA%\SubtitleJun\models` and exercise the full UI path (Start → live
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
dotnet run --project tools/ModelManager -- install --id streaming-zipformer-zh-14M --models-root "$env:LOCALAPPDATA\SubtitleJun\models"
dotnet run --project tools/ModelManager -- verify --models-root "$env:LOCALAPPDATA\SubtitleJun\models"

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
  `%LOCALAPPDATA%\SubtitleJun\models`, so the model bundled inside the release ZIP was not
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
- The **user data directory** moved to `%LOCALAPPDATA%\SubtitleJun\` (database / logs / models / exports).
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

## Update — release artifacts + auto-migration of the data folder (v0.3.2) (2026-10-09)

- **Release asset names must be ASCII:** GitHub silently drops non-ASCII characters from release asset
  file names (`字幕君-Setup.exe` was published as `-Setup.exe`). The distribution artifacts are therefore
  `SubtitleJun-Setup.exe` / `SubtitleJun-Setup.msi` / `SubtitleJun-win-x64.zip`; the app's own display name
  and its executable (`字幕君.exe`) stay Chinese.
- **One-time automatic migration of the user data folder.** Because 0.3.1 moved the data folder to
  `%LOCALAPPDATA%\SubtitleJun\`, `LocalDataPaths.MigrateLegacyFolderIfNeeded` now moves anything left in
  `%LOCALAPPDATA%\LocalMeetingSubtitle\` into the new folder on first launch. It only runs when the new
  folder is absent or empty (so it never merges over live data), moves each top-level entry separately so a
  single locked file cannot abort it, and deletes the legacy folder once it is empty. Covered by four unit
  tests (`LocalDataPathsMigrationTests`); unit suite now 116 tests.
- Version bumped to **0.3.2**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — "Recognition error" NullReferenceException hardening (v0.3.3) (2026-10-09)

- **Reported:** recognition occasionally fails with `Recognition error: Object reference not set to an
  instance of an object` (the message produced by the decode catch in `TranscriptionPipeline`).
- **Could not reproduce** on the dev host: real WASAPI loopback + the real zh-14M model transcribes
  correctly, with and without model-level hotwords (`modified_beam_search`), and the new
  `RealPipelineTests` (real engine through the real pipeline, no scripting) passes.
- **Root cause found by review — use-after-dispose of the ASR session:**
  1. `StopAsync` waits at most 5 s for the ASR loop; `DisposeAsync` then freed `_asrSession` (and the
     native `OnlineStream` handle) even if the loop was still decoding with it. Decoding a freed native
     stream throws `NullReferenceException`, which the decode catch reports as a "Recognition error".
     `DisposeAsync` now cancels the token and awaits the ASR task before disposing the session.
  2. `SwapEngine` (engine hot-swap when hotwords are applied) disposed the old engine and session
     *before* creating the new session, so a failure left a disposed session in use. It now creates the
     replacement session first and only then tears the old one down.
- **Diagnostics:** `FileLogger` now appends the exception `StackTrace` to error entries. Previously only
  the type and message were written, which made a NullReferenceException here undiagnosable from the log.
- **New coverage:** `RealPipelineTests` — the first tests that drive the real engine through the real
  pipeline (plain decode + model-level hotwords). Integration suite 8 → 10.
- Version bumped to **0.3.3**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — real stack for the "Recognition error" NRE; guard at the sherpa-onnx boundary (v0.3.4) (2026-10-09)

- **The v0.3.3 teardown fixes were not the trigger.** The failure was finally reproduced in a live
  session (Start + real WASAPI loopback), giving the actual stack:

  ```
  NullReferenceException
    at SherpaOnnx.OnlineRecognizerResult..ctor(IntPtr handle)
    at SherpaOnnx.OnlineRecognizer.GetResult(OnlineStream stream)
    at LocalMeetingSubtitle.Asr.SherpaOnnxAsrEngine.OnlineSession.GetResult()   (SherpaOnnxAsrEngine.cs:196)
    at ...TranscriptionPipeline.DecodeStreaming(...)
    at ...TranscriptionPipeline.AsrLoopAsync(...)
  ```

  So the exception is thrown **inside the sherpa-onnx C# wrapper**: `OnlineRecognizer.GetResult`
  occasionally yields no native result, and `OnlineRecognizerResult(IntPtr)` dereferences it. It is not
  in our decode logic and not a use-after-dispose (the v0.3.3 fixes remain useful but were not the cause).
- **Fix:** `OnlineSession.GetResult()` now treats "the library returned no result" as *no hypothesis for
  this chunk* (empty result) and continues, instead of letting the exception tear down recognition. The
  first occurrence logs a WARN with the `IsReady` state; later ones drop to DEBUG.
- **Instrumentation:** the decode-failure log now includes `chunk=<length>` and `nonFinite=<count>`, to
  tell whether bad (NaN/Inf) audio data is involved.
- **Not deterministically reproducible:** many live runs decode correctly (plain and with
  `modified_beam_search`); the failure is intermittent, so the fix is a defensive guard at the library
  boundary rather than a proven root-cause removal. If it recurs, the log now carries the full stack
  plus the new diagnostic fields.
- Probes written while investigating (`GetResult` with no audio, tiny chunks, 44.1 kHz stereo through the
  real preprocessor, post-`Reset` reads, empty `AcceptWaveform`, speech+silence endpointing) all pass and
  were removed.
- Version bumped to **0.3.4**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — "Recognition error" root cause: reading a result that was never computed (v0.3.5) (2026-10-09)

- **Live evidence obtained.** Running the installed build with real capture logged:

  ```
  [WARN] sherpa-onnx GetResult returned no result (IsReady=False, accepted=155 samples)
  ```

  i.e. the stream had **not decoded once** (`IsReady()==false`, so the `while (IsReady()) Decode()` loop
  never ran) and `GetResult()` was then called anyway. The sherpa-onnx C# wrapper has no result to read in
  that state and throws `NullReferenceException` from `OnlineRecognizerResult(IntPtr)`.
- **Fix:** `OnlineSession` now only reads a result **after at least one `Decode()`** (`_hasDecoded`, cleared
  by `Reset()`); until then it returns an empty hypothesis without calling the throwing API. The first
  occurrence logs a WARN with the accepted-sample count and `IsReady`; it no longer floods the log.
- **Ruled out small blocks as the cause:** probes feeding 155 / 320 / 800 / 1600-sample blocks all reached
  `IsReady()==true` after ~0.42 s of audio. The trigger is reading a result before the stream is ready.
- **Still not deterministically reproducible** end-to-end: most live runs transcribe correctly. The log now
  makes the next occurrence self-diagnosing (`accepted=N sample(s)` distinguishes "no capture data" from
  "recognizer never ready").
- Version bumped to **0.3.5**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — "video plays but nothing is transcribed": floating-window XAML crash + resampler overflow (v0.3.6) (2026-10-09)

- **Reported:** continuous video playback produced no subtitles at all.
- **Diagnosis (live):** the level meter moved and the recognizer accepted audio in real time
  (`accepted=192635 sample(s)` in ~12 s), so capture and decoding were fine. The log was flooded with

  ```
  XamlParseException: 无法对“SubtitleLineViewModel”类型的只读属性“Timestamp”进行 TwoWay 或 OneWayToSource 绑定
  ```

  Root cause: `FloatingSubtitleWindow.xaml` bound `Run.Text` to the read-only `Timestamp`, and **`Run.Text`
  is `BindsTwoWayByDefault`** in WPF. Every rendered subtitle row threw. `OnDispatcherUnhandledException`
  shows a **modal MessageBox per exception** and marks it handled, so the error/modal loop wedged the UI
  and the subtitle list never displayed anything.
  **Fix:** `Mode=OneWay` on both `Run` bindings.
- **Second defect found while diagnosing:** `StreamingResampler.Process` sized its output list with
  32-bit arithmetic (`input.Length * _outRate / _inRate`), which overflows for blocks longer than
  ~134k samples (~3 s at 44.1 kHz) and threw `ArgumentOutOfRangeException`. Now 64-bit.
- **Noise fix:** the v0.3.5 "stream has not produced a result yet" warning also fired during healthy
  transcription (between utterances there is nothing new to read). It now only warns if the stream has
  **never** decoded.
- **Verified live:** with real WASAPI loopback the subtitle list now renders (`[00:00:00]`, `[00:00:20]`, …)
  and the `XamlParseException` count is **0**. The captured video audio decodes to Chinese with the real
  model (RTF ≈ 0.096) via `AsrBenchmark`.
- Version bumped to **0.3.6**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — in-app self-diagnosis in the footer (v0.3.7) (2026-10-09)

- **Motivation:** "sound plays but nothing is transcribed" was only diagnosable from the log. On a
  second machine the user had no way to tell whether capture, the recognizer, or the model was at fault.
- **Added** a live diagnostic chip in the footer (next to the level meter), shown in red when it reports
  a problem:
  - `就绪（未开始）/ ready`
  - `未采集到声音 — 请检查播放设备 / no audio captured` (frames stopped arriving — usually the wrong
    playback device; only the Windows default render endpoint is captured via WASAPI loopback)
  - `已采集 N.Ns 音频，识别器未就绪 / N.Ns captured, not ready yet` (audio flowing, no decode yet)
  - `识别中 / recognizing`
  - `模型未安装` / `未配置模型` / `原生库缺失`
- **Implementation:** `TranscriptionPipeline.GetDiagnostics()` returns a `PipelineDiagnostics` snapshot
  (`FramesReceived`, `SamplesAccepted`, `Decodes`, `SecondsSinceLastFrame`, `QueueSamples`); counters use
  `Interlocked` because the capture thread and the ASR thread both update them. `MainViewModel` refreshes
  the text once a second via a `DispatcherTimer` started in `InitializeAsync`. Only one small text row was
  added to the footer; no other layout changed.
- **Verified live:** with nothing playing it correctly reports "no audio captured" in red; before Start it
  shows "ready"; while transcribing it shows "recognizing".
- Unit **116** + integration **10** pass.
- Version bumped to **0.3.7**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — bounded self-heal for "captured but the recognizer never decodes" (v0.3.8) (2026-10-09)

- **Reported (second machine):** the v0.3.7 footer chip shows "已采集 N.Ns 音频，识别器未就绪" —
  i.e. audio reaches the pipeline (`FramesReceived` advancing) but `Decodes == 0`, so nothing is emitted.
- **Ruled out by measurement (probes, all removed afterwards):**
  - sample rate fed to the recognizer is 16000 (app log `rate=16000`);
  - the audio is normal speech (rms 0.02–0.08, max 0.07–0.25);
  - block size is irrelevant — offline decoding of the same audio at 10/60/1000 ms all produce text
    (RTF ≈ 0.078);
  - real-time vs back-to-back feeding is irrelevant — both start decoding after **0.42 s** of audio;
  - thread count is irrelevant — 4 / 8 / 18 / 36 / default all behave identically.
  So the recognizer instance itself must be in a state where its stream never becomes ready.
- **Mitigation:** if 12 s of audio have been accepted with `Decodes == 0`, `DecodeStreaming` rebuilds the
  recognition session (`_engine.CreateSession()`, old one disposed) once per session and logs a WARN:
  `No decode after 12.0s of audio (IsReady=False); rebuilding the recognition session.` If the rebuild
  throws (e.g. the engine was disposed), it logs an ERROR with the stack.
- **UI:** the chip now reads `已采集 N.Ns 音频，识别器未就绪（正在自动重建会话）`.
- Unit **116** + integration **10** pass; the healthy path still starts decoding within 0.42 s.
- Version bumped to **0.3.8**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — ROOT CAUSE of "sound but no subtitles": non-ASCII model path (v0.3.9) (2026-10-09)

- **Proven with a controlled experiment.** The same audio and the same model files, only the path differs:

  | model dir | result |
  | --- | --- |
  | `G:\...\models\sherpa-onnx-...` (ASCII) | `TEXT=对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢` |
  | `C:\...\Programs\字幕君\models\sherpa-onnx-...` | native prints `c-api.cc:SherpaOnnxCreateOnlineRecognizer:214 Errors in config!`, `TEXT=` (empty) |

  **sherpa-onnx's native layer cannot open model files under a non-ASCII path.** The C# wrapper does
  **not** surface the failure (`AsrInitStatus.Success`), so the app happily starts capturing while the
  recognizer is permanently unusable (`IsReady()` never true) — exactly the reported symptom.
- **Fix:** every on-disk path is ASCII again while the product name stays 字幕君:
  - install dir `%LOCALAPPDATA%\Programs\字幕君` → `%LOCALAPPDATA%\Programs\SubtitleJun`;
  - data dir `%LOCALAPPDATA%\字幕君\` → `%LOCALAPPDATA%\SubtitleJun\`;
  - the Start-menu folder and shortcut still *display* 字幕君 (shell names only — native code never reads
    them), and the executable is still `字幕君.exe` (native code never reads it either).
  - `MigrateLegacyFolderIfNeeded` now moves data out of **either** legacy folder (`字幕君`,
    `LocalMeetingSubtitle`) so upgrades keep history.
- **Guard:** if the models path still contains non-ASCII characters (e.g. a Chinese Windows user name in
  `%LOCALAPPDATA%`), the footer chip now says so explicitly instead of silently producing nothing.
- Tests: unit suite 116 → **118** (migration from both legacy names, plus an assertion that the current
  folder name is ASCII); integration 10.
- Version bumped to **0.3.9**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — application icon (v0.3.10) (2026-10-09)

- The installed app showed the **default WPF icon** (a window). Added a real icon matching the in-app
  logo: a coral rounded tile with a white caption card, rendered from the same `IconCaptions` geometry.
- `src/LocalMeetingSubtitle.App/Assets/app.ico` — multi-size (16/24/32/48/64/128/256), generated by
  `dist/_makeicon.py` (Pillow), which scales the app's own 24×24 logo path.
- Wired in:
  - `<ApplicationIcon>` → the exe icon (Explorer, taskbar, Start-menu shortcut, window icon);
  - `<Resource>` + `TrayIconController.LoadAppIcon()` → the **tray** icon, which previously used
    `SystemIcons.Application` (the generic system icon), with a fallback if loading fails.
- Unit **118** + integration **10** pass.
- Version bumped to **0.3.10**; MSI + Burn bundle + portable ZIP rebuilt and re-signed.

## Update — 离线说话人分离 V0.4.1 / offline speaker diarization (v0.4.0) (2026-10-09)

**目标:** 在**不破坏**现有实时字幕的前提下，增加**会后离线说话人分离**——导入本地会议录音 →
自动/手动设定发言人数 → 声纹聚类得到发言区间 → 与已有 ASR 字幕按统一音频时间轴对齐 →
在字幕板显示「发言人 A/B/…」。匿名编号，不宣称身份识别。

### 技术选型（已核实，未升级依赖）
- 复用现有 **sherpa-onnx 1.13.8**：其 C# API 已包含完整离线说话人分离栈
  （`OfflineSpeakerDiarization` + `OfflineSpeakerSegmentationModelConfig`(pyannote) +
  `SpeakerEmbeddingExtractorConfig` + `FastClusteringConfig` + `OfflineSpeakerDiarizationSegment`）。
  **无需版本升级 → 现有 ASR 无回归风险**。
- 模型（`Asr/DiarizationModelCatalog.cs`，与 `AsrModelCatalog` **分离**，避免被选作实时识别模型）：

  | 角色 | 模型 | 大小 | 许可证 | 来源 |
  | --- | --- | --- | --- | --- |
  | 说话人分段 | `sherpa-onnx-pyannote-segmentation-3-0` (`model.onnx`) | 5.72 MB | **MIT** (pyannote/CNRS) | HF `csukuangfj/...` |
  | 说话人声纹 | `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx` | 37.76 MB | **Apache-2.0** (3D-Speaker) | GH `k2-fsa/sherpa-onnx` 发布 |

  声纹模型 SHA-256 `1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b`
  （本机下载已逐字节校验一致）。下载仅在安装/开发期经 `ModelDownloads`（隔离程序集）进行；
  运行期识别链路仍无网络引用。

### 新增模块（全部增量，未改动既有类型的语义）
- 契约/模型：`Core/Models/SpeakerModels.cs`、`Core/Abstractions/SpeakerAbstractions.cs`
  （`ISpeakerDiarizationEngine`、`ISpeakerDiarizationService`、`ISpeakerAlignmentService`、
  `ISpeakerRepository`、`IAudioAssetRepository`、`IAudioFileLoader`）。
- 纯算法：`Core/Speakers/SpeakerAlignmentService.cs`（区间重叠 → 每句最多一个发言人；歧义标记待确认；
  无重叠 => 未知发言人，绝不强配；**绝不修改 `OriginalText`**）、`Core/Speakers/SpeakerColorPalette.cs`。
- 编排：`Core/Speakers/SpeakerDiarizationService.cs`（**独立后台线程 + `BelowNormal` 优先级**；
  `SemaphoreSlim(1,1)` 单任务；协作式取消；进度回调；失败仅落库为 `DiarizationRun.Status=Failed`，不致命）。
- 引擎：`Asr/SherpaOfflineSpeakerDiarizer.cs`（线程数上限低，默认 `clamp(ProcessorCount/4,1,4)`；
  校验 16 kHz；**仅当模型提供置信度时才记录，绝不伪造**）。
- 文件解码：`Audio/NaudioAudioFileLoader.cs`（`AudioFileReader` + 现有 `DefaultAudioPreprocessor` 下混/重采样到 16 kHz）。
- 存储：迁移 **4**（`diarization_runs`、`speakers`、`speaker_intervals`、`speaker_assignments`、`audio_assets`）
  ——**纯新增表，旧库照常打开**；`Storage/SqliteSpeakerRepository.cs`、`Storage/SqliteAudioAssetRepository.cs`。
  人工修改的行（`Source=Manual`）在重新分析时**不会被覆盖**（`ON CONFLICT ... WHERE Source=0`）。
- 界面：字幕行新增**发言人彩色标签**（`MainViewModel`/`SubtitleLineViewModel`）；页脚新增
  「说话人 / Speakers」按钮（导入录音并分析）与进度条；**悬浮字幕窗口未改动**；14 英寸布局不受挤压。

### 实测证据（开发主机，真实模型 + 真实音频）
构造 2 人片段（fangjun-sr-1 ×2 + leijun-sr-1 ×2，约 14.1 s）→ 引擎输出：

```
00:00.03 - 00:01.87  speaker 0  confidence 0.890
00:02.81 - 00:04.46  speaker 0  confidence 0.843
00:05.54 - 00:07.33  speaker 1  confidence 0.964
00:07.87 - 00:09.37  speaker 1  confidence 0.964
00:10.00 - 00:11.82  speaker 1  confidence 0.965
00:12.33 - 00:13.96  speaker 1  confidence 0.966
```

端到端服务：`success=True speakers=2 assigned=2 confirm=0`；两条字幕分别归属不同发言人；
`OriginalText` 未被改动。两个用例在缺模型时自动跳过（干净克隆仍绿）。

### 测试
- 单元 **118 → 136**（对齐算法 10 例；仓储/迁移 8 例）。
- 集成 **10 → 12**（真实模型说话人分离 + 端到端对齐落库）。
- 性能：**3 通过 + 1 跳过**（`ThreeHourSoak` 仍未执行）。
- **未回归**：既有 118 单元 / 10 集成 / 3 性能全部保持通过。

### 版本
- 版本升至 **0.4.0**（`Directory.Build.props`、`installer/Product.wxs`、`installer/Bundle.wxs`）。

### 未完成 / 阻塞（详见 RELEASE_CHECKLIST SD-01..SD-18）
- **V0.4.2**（发言人重命名 / 合并 / 单条重分配 / 分角色过滤与导出）与 **V0.4.3**（双路采集、准实时）**尚未开始**。
- 会后**录音采集侧**尚未接入（当前仅支持**导入**本地音频文件）；临时音频生命周期管理表已就位但未接线。
- 无目标硬件 → 实时+会后并发的真实性能对比、3 小时 soak 仍未执行。

## Update — 发言人管理与分角色导出 V0.4.2 (2026-10-09)

在 V0.4.1 基础上补齐**角色校正与分角色输出**（`SD-07..SD-11`）：

- **发言人管理窗口**（`SpeakerManagementWindow.xaml` + `ViewModels/SpeakerManagementViewModel.cs`，Claude 主题，独立窗口）：
  - **重命名**：编辑名称 → `UpsertSpeakerAsync`，主窗口标签即时同步。
  - **合并 + 撤销**：选择目标合并（`MergeSpeakersAsync` 返回受影响字幕 id）→ **一层撤销**（把受影响字幕还原到原发言人、并复原 `IsMerged`/`MergedIntoSpeakerId`）。
  - **单条重分配**：逐条下拉选择发言人 → 应用（`SetAssignmentSpeakerAsync(..., Manual)`），人工结果不被重分析覆盖。
  - 统计：每位发言人的**字幕条数**（精确）与**发言时长**——后者由分离区间与已归属字幕的重叠估算，UI 标注“≈”，因“原始区间 → 发言人”映射未持久化（已在代码中说明）。
- **分角色过滤**：主窗口新增发言人筛选（全部 / 各发言人）；`SubtitleLineViewModel.IsSpeakerVisible` 控制行可见性。
- **分角色导出**：`ExportFormat.Csv` 新增；`CsvTranscriptFormatter`（`index,start,end,speaker,text`，RFC4180 转义）；TXT/SRT 前缀发言人；Markdown 增加「参与发言人」与「按发言人整理」分节。**无发言人信息时输出与旧版逐字节一致**（向后兼容）。
- 新增单测 `SpeakerAwareExportTests`（CSV 表头/引号转义、TXT/SRT 前缀、Markdown 分节、无发言人时不变）。

### 测试
- 单元 **136 → 143**；集成 **12**；性能 3 通过 + 1 跳过。构建 0 警告 / 0 错误。

### 未完成
- **V0.4.3**（双路采集、准实时）尚未开始；会后**录音采集侧**仍未接线；0.4.x 发布产物未重建。
- SD-07/08/09/11 的 UI **未人工交互验证**（存储层与格式层有单测）。

## Update — 会后录音采集与清理 V0.4.2b (2026-10-09)

补齐 **SD-17** 与「会中录音 → 会后分离 → 自动清理」闭环。**默认不录音；除非用户显式启用，绝不持久化任何音频。**

- **`IRecordingService`**（`Core/Abstractions/RecordingAbstractions.cs`）+ 实现
  **`Audio/WaveRecordingService.cs`**：捕获帧经**有界 Channel** 入队（`Write` 克隆后 `TryWrite`，
  **绝不阻塞采集线程**），单一后台写线程用 `DefaultAudioPreprocessor(16000)` 下混/重采样并写
  16 kHz 单声道 16-bit WAV。写线程落后时丢弃并计数（不阻塞、不丢识别）。
- **`RecordingMode { None, Temporary, Retain }`**（`Core/Models`），持久化为 `AppSettings.RecordingMode`（默认 `None`，JSON 字段，无需迁移）。
- **`LocalDataPaths.RecordingsDirectory`** = `%LOCALAPPDATA%\SubtitleJun\recordings`（ASCII 路径）。
- **`Storage/LocalAudioAssetStore.CleanupAsync`**：删除到期的临时资产（文件+行），再清除
  `recordings/` 下**未被资产行引用且超过 24 小时**的孤儿文件（文件名即 `AudioAssetId`）；锁定文件不抛异常。
- **接线**（`MainViewModel`）：开始会议且 `RecordingMode != None` 时启动录音、订阅 `capture.FramesAvailable`
  写入、登记 `AudioAsset`（临时/保留）；停止时退订并回写时长/大小；`InitializeAsync` 启动时执行一次
  残留清理（崩溃恢复）；`ImportAndDiarizeAsync` **优先使用本会话的录音**（无需再选文件），
  分离成功后若为临时录音则**删除文件与资产行**。录音失败仅记日志+提示，绝不影响转写。
- **设置界面**：「会后录音」下拉 + 明确隐私说明（本机保存、用于一次会后分析、分析后或下次启动自动清除、不上传）。
- **测试**：`RecordingTests`（WAV 往返：1 s 440 Hz → 时长≈1 s、RMS>0.1；清理：到期临时删除、保留留存、25 h 孤儿删除、新孤儿保留）。

### 测试
- 单元 **143 → 145**；集成 12；性能 3 通过 + 1 跳过；构建 0 错误。
- **SD-17 更新为 PASS**（开发主机：写入/清理有单测；录音 UI 未人工交互验证）。

## Update — 转写卡顿：ASR 线程数上限 (2026-10-09)

**问题（用户报告）：** 一旦开始转写系统即卡顿。

**根因（本次实测确认）：** `SherpaOnnxAsrEngine` 在未指定线程数时沿用 sherpa-onnx 的 `ProcessorCount/2`——
开发主机（64 逻辑核）因此开 **32 个推理线程**。对 14M 小模型而言，线程数远超所需，**既拖慢识别又长期占用大量 CPU**。

**实测（`tools/AsrBenchmark`，同一音频、同一模型）：**

| 线程 | RTF | 文本 |
| --- | --- | --- |
| 2 | 0.0524 | 对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢 |
| **4** | **0.0481** | 同上 |
| 8 | 0.0639 | 同上 |
| 32（旧默认） | **0.0999** | 同上 |

即：**4 线程比 32 线程快约 2 倍**（文本逐字一致），同时把 28 个核留给界面。

**修复：** 新增 `Core/Models/AsrModels.cs` → `AsrThreadPolicy`（`MaxAutoThreads = 4`；`Resolve(configured)` =
显式值优先，否则 `clamp(ProcessorCount/2, 1, 4)`）。`AsrOptionsFactory.FromDescriptor` 与
`SherpaOnnxAsrEngine`（在线 + 离线两处）统一走该策略。用户仍可在设置里显式指定更多线程。

**修复后默认（4 线程）实测 RTF = 0.0396**（较旧默认 0.0999 改善约 2.5×）。

> 诚实说明：这只消除了「识别自身占用过多 CPU」这一确定因素；**未在用户机器上复现**，若卡顿另有来源
> （如性能计数器采样或界面刷新频率），仍需在目标机进一步定位。

### 测试
- 单元 **145 → 148**（`AsrThreadPolicyTests`：显式值透传、自动值下限/上限、大核数不超上限）。
- 集成 12；性能 3 + 1 跳过；构建 0 错误。

## Update — 华为领域专业词表 (2026-10-09)

**目标（用户请求）：** 把华为全部领域的专业名词纳入识别，提升转写准确率。

- 新增 **`Core/Hotwords/BuiltInLexicon.cs`**：公开资料整理的**分领域术语表**（8 个领域：
  芯片与算力 / 操作系统与基础软件 / 云计算与数据 / 网络与联接 / 终端与消费 / 智能汽车 /
  数字能源 / 流程与组织；共 **116 条**）+ **15 条保守的文本纠正规则**
  （`升腾→昇腾`、`鲲朋/昆鹏→鲲鹏`、`鸿盟/红盟→鸿蒙`、`升思→昇思`、`毕升→毕昇` 等）。
- **`IHotwordService.UseBuiltInLexicon`**（默认 true）+ `DefaultHotwordService` 合并逻辑：
  内置术语与用户热词/规则**合并**（**用户同名条目优先**，缓存 + 失效重建）；可用设置关闭。
  `AppSettings.UseBuiltInLexicon`（JSON 字段，无需迁移）。
- **设置界面**：热词区新增勾选框 + 说明（含内置条数，`Run.Text` 用 `Mode=OneWay` 绑定以规避已知的
  TwoWay 崩溃缺陷）。
- **纠正规则使用子串模式（`WholeTokenOnly=false`）**：因为整词边界基于 `\p{L}`，而中日韩字符本身属于
  `\p{L}`，整词 CJK 规则**在句内永远不会命中**（已由既有 `TextCorrectionEngineTests` 印证）。
  代价是含错误写法的合法词（如「经济升腾」）也会被改写；该权衡已写入代码注释与 `KNOWN_ISSUES` P3-11。

### 实测（`tools/AsrBenchmark`）
- **性能无回归**：带 116 条热词 `RTF=0.0594`，不带 `RTF=0.0712`（同一音频、4 线程）。
- **语言无系统性退化**：无关音频上的差异仅为不同束搜索路径（wav0 多一个「呢」，wav1 反而少一个「呢」）。
- **重要发现（P3-11）**：随包的**中文 14M 模型词表缺少**部分术语——纯英文缩写（`CANN`/`IPD`/`HCSO`/`Atlas`…）
  与**生僻字「昇」**（故 `昇腾`/`昇思`/`毕昇` 无法参与模型加权）。这些词只能靠**纠正规则**或改用
  中英双语模型。sherpa 打印 `Failed to encode some hotwords, skip them` 后继续，不致命。
- **未能测量准确率提升**：开发主机**没有含华为术语的真实音频**，故只测了成本、未测增益（已如实记录）。

### 测试
- 新增 `BuiltInLexiconTests` 7 例（词表非空/去重、规则可在句内生效、默认合并、关闭后清空、
  用户同名优先、仅凭词表即可生成热词文件、纠正规则命中）。
- 单元 **148 → 155**；集成 12；性能 3 + 1 跳过；构建 0 错误。

## Update — 真实端到端冒烟：发现并修复非 ASCII 热词路径 P0 (2026-10-09)

**这是本项目第一次真正跑通「开始 → 采集 → 转写 → 落库」的完整 UI 路径**（此前 BLOCKED-1 一直未执行）。

**方法：** 启动**发布版 0.4.0** → 用 UI Automation 点击「开始」→ 通过默认播放设备循环播放
`test_wavs/0.wav` → WASAPI 回环采集 → 检查日志与数据库。

**首次运行即暴露 P0 回归（词表引入的）：**
- 现象：`hotwords=on`，采集到 160k+ 采样，但识别器**始终 `IsReady=False`**，**一条字幕都没有**。
- 根因：`MainViewModel.BuildHotwordFile()` 把热词文件写到 `%TEMP%\字幕君\hotwords.txt`——**中文目录**。
  sherpa-onnx 原生层无法读取非 ASCII 路径，且 C# 包装层不报错。此前热词为空 → `hotwords=off` → 潜伏未触发；
  本次内置词表使热词**恒为开启**，于是引爆。
- **A/B 实证**（同音频同模型）：ASCII 路径 `RTF=0.0588` 且出文本；中文路径 `RTF=0.0002`、**文本为空**。
- **修复**：热词文件固定写 ASCII 路径 `%TEMP%\SubtitleJun\hotwords.txt`（`ModelHotwordFile.DefaultTempPath`）；
  并在 `SherpaOnnxAsrEngine.InitializeAsync` **显式拒绝**非 ASCII 的模型目录/热词路径（把静默失败变为明确报错）。
- **回归防线**：`ModelHotwordFileTempPathTests`（单元）、`NonAsciiPathGuardTests`（集成）。

**修复后复跑（发布版）：**
- 日志：`decoding=modified_beam_search, hotwords=on`；热词文件 116 行。
- **数据库落库 3 条真实中文字幕**（会话 `bb473befdf724f8b820c5bbadd1a9ad9`）：
  `#1 [0.8–12.0s] 对我做了介绍那么我想说的是…`、`#2 [12.3–23.1s] …`、`#3 [23.4–34.4s] …`。
- → **BLOCKED-1（P0）关闭**；AC-06 由 PARTIAL/BLOCKED 改为 **PASS (dev host)**。

### 测试
- 单元 **155 → 157**；集成 **12 → 14**；性能 3 + 1 跳过；构建 0 错误。

### 打包缺陷（同步修复）
- 清理重发布后发现随包 `models/` 被**拍平**（ASR 模型文件直接落在 `models\` 下，而非 `models\<模型目录>\`），
  导致发布版启动日志报 `installed=False`、**「开始」按钮置灰、完全无法转写**。
  根因：`Copy-Item -Recurse` 目标目录未预先创建时，会把第一个源目录**当作目标目录本身**复制。
- 修正流程（先建目录，再逐个复制到各自子路径）：见 `KNOWN_ISSUES.md`「Release packaging flattened the
  bundled models tree（FIXED）」。
- 修正后复跑**最终发布产物**：`installed=True`、Start 可用、UI 驱动一次运行**落库 2 条真实中文字幕**
  （会话 `97e832ca…`）。
- **教训：发布前一定要真正启动一次打包后的程序**——编译 + 单元测试无法发现模型目录结构错误。

## Update — 界面时间戳统一为分钟精度 (2026-10-09)

- 应要求，**显示**位置的句子时间戳由 `[HH:MM:SS]` 改为 **`[HH:MM]`**：
  - 主窗口字幕列表（`SubtitleLineViewModel.Timestamp`）；
  - 悬浮字幕窗口（同一绑定）；
  - 发言人管理窗口的字幕列表（`SegmentRowViewModel.TimeText`）——同一句字幕在两个窗口应显示一致，故一并统一；
    如只需要前两处，回退这一处即可。
- **导出格式保持不变**：TXT / Markdown / CSV 仍为 `HH:MM:SS`，SRT 仍为 `HH:MM:SS,mmm`（格式规范要求）。
- 无测试断言显示格式（既有 `[00:00:00]` 断言都在**导出**测试中，未受影响）；构建 0 错误，
  单元 157 / 集成 14 / 性能 3（+1 跳过）全通过。

## Update — 离线文件转写 V0.5 Phase 0–1（FFmpeg 解码层）(2026-10-10)

**目标：** 在**不破坏**现有实时字幕的前提下，新增**离线文件转写**：导入本地音频/视频 → 离线解码 →
离线 ASR → 说话人分离 → 角色标注对话 → 导出。**当前仅 Phase 0–1 完成，Phase 2 及以后未开始。**

### 依赖决策：FFmpeg（LGPL v3，许可已核实、非假定）
- 新增 **`tools/fetch-ffmpeg.ps1`**：下载 BtbN/FFmpeg-Builds `latest` 资产
  `ffmpeg-master-latest-win64-lgpl-shared.zip`（**75.8 MB**），按 **URL + SHA-256**
  （`85e26d3d77c17393e56e49132fda0905a3ced925942e1d8d7d2ebe0a28d58a55`）双重固定；校验哈希后
  **仅解出运行期文件**到 `third_party/ffmpeg/`（gitignore）+ `LICENSE.txt`。
- 该构建为 **LGPL v3**：`ffmpeg -version` 的 config 含 **`--enable-version3 --enable-shared`**，
  **无 `--enable-gpl` / `--enable-nonfree`**；脚本在解包后运行 `ffmpeg -version` 并**拒绝** GPL/nonfree 构建。
  已安装版本 `ffmpeg version N-127259-gb91a82d6dd-20261009`（build `Latest Auto-Build (2026-10-09 14:16)`）；
  预置体积 **153.7 MB**（`avcodec-63.dll` 87.65 MB、`avfilter-12.dll` 30.2 MB、`avformat-63.dll` 24.52 MB 等）。
- **作为独立程序**随包分发（仅以子进程调用，绝不链接）→ 合规；完整说明见 **`docs/LICENSES.md`**。
- 注意：LGPL 构建**无 libx264** → **不能 H.264 编码**（我们**只解码**，H.264 解码受支持）；内置
  OpenCL/AMF/nvenc 钩子（未用，纯 CPU 解码）。

### Phase 0–1 交付（全部增量）
- 契约/模型：`Core/Models/MediaModels.cs`（`MediaKind`/`AudioStreamInfo`/`MediaInfo`/`PcmBlock`/`MediaDecodeRequest`）、
  `Core/Abstractions/MediaAbstractions.cs`（`IMediaDecodeService`/`MediaDecodeException`/`MediaErrorKind`/`MediaToolPaths`）。
- 新工程 **`src/LocalMeetingSubtitle.Media`**：
  - `FFmpegLocator`（应用目录 `ffmpeg\bin` → 开发树 `third_party\ffmpeg\bin` → `PATH`）；
  - `ProcessRunner`（**仅用 `ProcessStartInfo.ArgumentList`**，无 shell → 注入安全、Unicode/空格/长路径安全）；
  - `FFprobeMediaProbe`（`ffprobe -print_format json -show_format -show_streams` → `MediaInfo`，可识别无音轨/attached_pic）；
  - `FFmpegMediaDecodeService`（`ffmpeg -map 0:a:<N> -vn -sn -dn -f f32le -acodec pcm_f32le -ac 1 -ar 16000 pipe:1`，
    按 **1 秒 `PcmBlock`** 流式输出，**整文件从不缓冲**；取消杀进程树）。
- App：DI 注册 `IMediaDecodeService`（**延迟解析**，缺 FFmpeg 仍能启动）；csproj 仅在 `third_party/ffmpeg`
  存在时随包拷贝。
- 测试：`MediaDecodeTests`（15 例）+ `MediaToAsrEndToEndTests`（4 例）。

### 实测证据（开发主机）
- `0.mp4`（视频）→ 探针 `kind=Video container=mov,mp4,m4a,3gp,3g2,mj2 duration=5.61s audioStreams=1`
  → 解码 **89 784 采样 = 5.61 s** → ASR 文本 `对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢`。
  `0.mkv`（matroska, 5.63 s）、`0.ogg`（ogg, 5.63 s）、`0.mp3`（mp3, 5.64 s）同形状。
- `noaudio.mp4` → `HasAudio=false` → `MediaDecodeException(NoAudioTrack)`；
  `two-tracks.mp4` 音轨 1 可解码、索引 9 → `StreamIndexOutOfRange`；含**中文+空格**的路径也能解码。
- 完整报告：`docs/FILE_TRANSCRIPTION_TEST_REPORT.md`。

### 测试
- 单元 **157 → 159**；集成 **14 → 33**（`MediaDecodeTests` 15 + `MediaToAsrEndToEndTests` 4）；
  性能 **3（+1 跳过：`ThreeHourSoak`）**；构建 **0 错误**。
- **无回归**：既有实时字幕套件全部保持通过。

### 未完成（Phase 2–8 均未开始）
Phase 2 分段长音频离线 ASR（VAD）；Phase 3 角色标注对话（`ITranscriptAlignmentService` + 复用 V0.4
`ISpeakerDiarizationService`）；Phase 4 作业队列 + 检查点/续跑（`ITranscriptionJobService`）；Phase 5 对话编辑 UI；
Phase 6 TXT/Markdown/CSV/SRT/**DOCX** 导出；Phase 7 UX/性能；Phase 8 回归 + 发布（把 FFmpeg 打进安装包）。
数据库仍为**迁移 4**；无 `MediaFile`/`TranscriptionJob`/`TranscriptionChunk`/`TranscriptSegment`/`JobCheckpoint` 表。
设计/状态见 `docs/ARCHITECTURE_V05.md`；逐项状态见 `docs/RELEASE_CHECKLIST.md`（FT-01..FT-25）。

### 断点续跑信息（下一会话）
- **当前阶段：** V0.5 Phase 1 完成；**Phase 2 未开始**。
- **已完成：** FFmpeg 工具链（Phase 0）+ 媒体解码层（Phase 1），含契约、新工程、DI、测试。
- **下一步（Phase 2）：** 在已解码的 16 kHz PCM 上做**分段长音频离线 ASR**（VAD/能量分段 + 逐段
  sherpa-onnx 离线解码），产出**带全局时间戳的句子**。
- **构建/测试命令：**
  ```powershell
  $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
  dotnet build LocalMeetingSubtitle.sln -c Release
  dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug
  dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~MediaDecodeTests"
  dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~MediaToAsrEndToEndTests"
  ```
- **取 FFmpeg：** `./tools/fetch-ffmpeg.ps1`（自动校验 SHA-256 + LGPL）。
- **生成测试媒体：** 见 `docs/DEVELOPMENT.md` 的「File transcription / FFmpeg」；`testmedia/` 与 `third_party/`
  均为 gitignore。
- **下一步要改的文件：** 新增分段/编排契约（如 `Core/Abstractions/` 下的分段/离线解码接口）、`Core/Media/*`
  分段器与离线 ASR 编排、`Asr/*` 的逐段离线解码接线；测试在 `tests/LocalMeetingSubtitle.IntegrationTests/`
  下新增分段转写用例。
- **下一验收目标：** `RELEASE_CHECKLIST` 的 **FT-05..FT-08**（离线 ASR 文本/分段、全局时间戳、长音频 VAD 分段）。
- **诚实边界：** 全部目标机（Win11 + Core Ultra 7 155H）验收仍 **BLOCKED/NOT_TESTED**；长音频 **CER/WER**
  因**无参考文本**未测。
