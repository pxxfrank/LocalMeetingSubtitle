# Codex Progress

## Current phase

**V0.4.0 released (HEAD `554c8eb`); V0.5 "offline file transcription + role-tagged dialogue" is in
progress — Phases 0–3 complete, Phase 4 not started.**

The V0.4 live-subtitle product builds, its tests pass on the development host, a real model decodes
Chinese, offline guarantees are verified, and a self-contained release artifact is produced. V0.5 adds
a bundled **LGPL FFmpeg** media-decode layer (import audio/video → 16 kHz mono PCM), **segmented
long-audio offline ASR** (VAD → per-segment decode → global timestamps, with a three-mode catalog) and
**role-tagged dialogue** (offline diarization of the decoded file + transcript/speaker alignment). The
remaining V0.5 work (job queue, editor UI, export, packaging) is **not started**, and the remaining work
overall is **acceptance on the real Windows 11 target hardware**.

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

## Update — V0.5 Phase 2（长音频离线 ASR，long-audio offline ASR）— DONE (2026-10-10)

**目标：** 在 Phase 0–1 的媒体解码层之上完成**长音频离线转写**：解码得到的 16 kHz 单声道 PCM →
VAD/能量分段 → 逐段 sherpa-onnx 离线解码 → 产出**带全局时间戳的句子**。**Phase 2 完成；Phase 3 及以后未开始。**
**实时字幕路径未改动**（无回归）。

### 新增 / 修改的代码（全部增量）
- **新增** `src/LocalMeetingSubtitle.Core/Audio/SpeechSegment.cs` —
  `readonly record struct SpeechSegment(float[] Samples, long StartSample, long EndSample, bool HasOverlapPrefix)`（含 `Length`）。
- **修改** `src/LocalMeetingSubtitle.Core/Audio/AudioSegmenter.cs` — 增加**绝对采样位置**记账；新增可选**末位**构造参数
  `double overlapSeconds = 0.0`（上限 `maxSegmentSamples/2`）；新增 `PushSegments` / `FlushSegment`（返回 `SpeechSegment`），
  `Push` / `Flush` 在其上重实现（**输出不变**）。**仅**在因达到最大长度而切割时，把尾部 `overlap` 采样保留进下一个缓冲区，
  并把该区域标记 `HasOverlapPrefix`（仅此情况）。
- **新增** `src/LocalMeetingSubtitle.Core/Models/OfflineTranscriptionModels.cs` —
  `enum TranscriptionMode { Fast, Standard, HighAccuracy }`；`sealed record OfflineTranscriptionOptions`（SampleRate、
  SilenceRms=0.010、MinSilenceSeconds=0.6、MaxSegmentSeconds=15.0、OverlapSeconds=0、MinSpeechSeconds=0.35、
  MinOverlapChars=3、ModelId、AppendTerminalPunctuation、TerminalPunctuation="。"）；
  `readonly record struct OfflineTranscriptSegment(TimeSpan Start, TimeSpan End, string Text, int SourceChunkId, string ModelId)`；
  `OfflineTranscriptionResult`（Segments / AudioDuration / Elapsed / Rtf / Completed / Cancelled / Error）；
  `readonly record struct OfflineTranscriptionProgress(TimeSpan Processed, TimeSpan Total, int SegmentsEmitted)`。
- **新增** `src/LocalMeetingSubtitle.Core/Transcription/OverlapTextDeduplicator.cs` —
  `Apply(previousText, currentText, minOverlapChars) -> Result(Text, TrimmedChars, Dropped)`。
- **新增** `src/LocalMeetingSubtitle.Core/Transcription/OfflineTranscriptionEngine.cs` —
  `TranscribeAsync(IAsyncEnumerable<PcmBlock> blocks, IProgress<OfflineTranscriptionProgress>?, TimeSpan? totalDuration, CancellationToken)`
  → `OfflineTranscriptionResult`：**流式**处理块（**整文件从不缓冲**），跑 VAD，用**一个** `IAsrSession` 逐段解码
  （`Reset()` → `AcceptWaveform` → `InputFinished` → `while (IsReady()) Decode()` → `GetResult()`），应用可选纠正函数，
  **裁掉前导静音**（上报起点即语音起始），把**接续段的起点钳到前一段终点**（时间轴永不重叠），为不产标点的模型**追加句末标点**，
  流结束时**冲刷最后一段**。捕获 `OperationCanceledException` / `MediaDecodeException(Cancelled)` → `Cancelled=true`。
  绝对时间 = 第一个 `PcmBlock.Start` + 累计采样索引。
- **修改** `src/LocalMeetingSubtitle.Asr/AsrOptionsFactory.cs` — `FromDescriptor` 增加可选 `decodingMethod`、`language`、
  `useInverseTextNormalization`。
- **新增** `src/LocalMeetingSubtitle.Asr/TranscriptionModeCatalog.cs` —
  `Resolve(mode, IModelManager, hotwordsFile, numThreads)` → `ResolvedTranscriptionMode(Mode, DisplayName, Description,
  Descriptor, EngineOptions, TranscriptionOptions, IsAvailable, UnavailableReason)`。
- **修改** `src/LocalMeetingSubtitle.Asr/AsrModelCatalog.cs` — `SenseVoiceSmall.SupportsHotwords` 由 true 改为 **false**；说明同步更新。
- **修改** `src/LocalMeetingSubtitle.Asr/SherpaOnnxAsrEngine.cs` — 离线 `Capabilities.ModelLevelHotwords` 改为 **false**；
  `BuildOffline` 不再设置 `HotwordsFile` 并硬编码 `greedy_search`（已核实约束）；修复 `OfflineSession.IsReady()` 恒为 `true` 的缺陷。
- **修改** `src/LocalMeetingSubtitle.Core/Transcription/TranscriptionPipeline.cs` — `SwapEngine` 改用配置的
  `OfflineSilenceRms` / `OfflineMaxSegmentSeconds` 重建 `AudioSegmenter`（此前恒用默认值）。
- **新增工具** `tools/FileTranscribe/`（`FileTranscribe.csproj` + `Program.cs`，已加入解决方案）。
- **新增工具** `tools/make-long-testmedia.ps1`。
- **修改** `tools/AsrBenchmark/Program.cs` — 新增 `--decoding <method>`；离线路径改为**整段一次性** `AcceptWaveform` + 解码
  （此前按 100 ms 分块喂入，使离线模型孤立地解码碎片）。

### 三种模式（真实、互不相同的配置）
| 模式 | 标签 | 模型 | 解码 | 模型级热词 | 分段 |
| --- | --- | --- | --- | --- | --- |
| Fast | 快速 | `streaming-zipformer-zh-14M` | `greedy_search` | 关 | VAD，最大 15 s，overlap 0，补 `。` |
| Standard | 标准 | `streaming-zipformer-zh-14M` | `modified_beam_search` | 开（热词文件） | VAD，最大 20 s，overlap 0，补 `。` |
| HighAccuracy | 高精度 | `sense-voice-small-int8`（离线） | `greedy_search`（唯一选项） | **不支持** | VAD，最大 30 s，**overlap 1.5 s** + 去重，ITN 标点 |

### 实测证据（开发主机：Windows 10 Pro，Xeon 64 逻辑核，64 GB）
> 目标硬件（X1 Carbon Gen 12 / Core Ultra 7 155H / 32 GB / Win11）**不可用** —— 以下**未**在目标机验证。
> `sense-voice-small-int8` 已为验证下载（`model.int8.onnx` **239,233,841 B** + `tokens.txt`；目录
> `models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17`）；**sha256 未记录**。

`tools/FileTranscribe` 处理 `testmedia/0.mp4`（5.612 s 中文语音，FFmpeg 从视频容器解码）：

| 模式 | 输出 | RTF |
| --- | --- | --- |
| Fast | `[00:00:00.000 - 00:00:05.380] (#1 streaming-zipformer-zh-14M) 对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢。` | 0.0920 |
| Standard | `[00:00:00.000 - 00:00:05.380] (#1 streaming-zipformer-zh-14M) 对我做了介绍那么我想说的是呢大家如果对我的研究感兴趣呢。` | 0.0974 |
| High | `[00:00:00.000 - 00:00:05.380] (#1 sense-voice-small-int8) 对我做了介绍啊，那么我想说的是呢，大家如果对我的研究感兴趣呢。` | 0.1024 |

标准模式输出与快速模式**不同** ⇒ 束搜索确实生效。

`testmedia/long-gaps.wav`（65.115 s）高精度 → **10 段**，时间戳单调、互不重叠：
`0.000-5.380`、`5.980-11.980`、`12.580-18.600`、`19.200-25.220`、`25.820-31.820`、`32.420-38.440`、
`39.040-45.040`、`45.640-51.659`、`52.260-58.280`、`58.880-64.880`；RTF **0.0508**。

`testmedia/long-continuous.wav`（56.115 s 不间断语音）高精度 → **2 段**：`[00:00:00.000 - 00:00:30.000]`
与 `[00:00:28.500 - 00:00:55.880]` —— 即 30 s 上限触发，第二段确实从 **28.500 s = 30.000 − 1.500 s** 重启；
重叠文本**未**被重复。RTF **0.0587**。

**sherpa-onnx 1.13.8 离线热词结论（A/B，`tools/AsrBenchmark --offline --model-id sense-voice-small-int8`）：**
- 带 `--hotwords` + 默认解码时，原生层拒绝构建识别器：
  `offline-recognizer.cc:Validate:88 Please use --decoding-method=modified_beam_search if you provide --hotwords-file. Given --decoding-method='greedy_search'`。
- 改用 `--decoding modified_beam_search` 仍拒绝：
  `offline-recognizer-sense-voice-impl.h:82 Only greedy_search is supported at present. Given modified_beam_search`。
- **结论：sherpa-onnx 1.13.8 中 SenseVoice 无法使用模型级热词。** 高精度模式的领域词只能走
  `TextCorrectionEngine`（纠正规则）。这纠正了代码中的两处过度声明。

### Phase 2 发现并修复的缺陷
1. **P1 `OfflineSession.IsReady()` 恒为 `true`**（sherpa-onnx 离线会话）—— `while (IsReady()) Decode();` 会无限自旋/挂起。
   因 `AsrBenchmark --offline` 挂起而发现。现在仅当「已接受音频但尚未解码」时为 true；`Decode()` 与 `Reset()` 会清除它。
   实时管线此前只调用一次 `Decode()`，故未受影响。
2. **P1 离线 ASR 从未用真实离线模型跑过。** 由此暴露并修复：工具把 100 ms 分块喂给「整段」模型（输出垃圾）、
   `BuildOffline` 传入会让识别器构建失败的热词文件。
3. **P2 SenseVoice 过度声明模型级热词**（`AsrModelCatalog.SenseVoiceSmall.SupportsHotwords` 与 `SherpaOnnxAsrEngine`
   离线 `ModelLevelHotwords`）—— 两者现均为 false，证据即上面的原生报错信息。
4. **P2 `TranscriptionPipeline.SwapEngine` 用默认值重建 `AudioSegmenter`**，每次引擎切换（热词重应用）都会静默丢弃
   配置的 `OfflineSilenceRms` / `OfflineMaxSegmentSeconds`。已修复。
5. **P2 实时离线路径仍产出零时长分段**（`StartOffset == EndOffset == 块结束`）—— Phase 2 未改动（Phase 2 面向文件转写）；
   **仍为 OPEN**。

### 持久化
**Phase 2 未新增数据库迁移。** 引擎在内存中返回结果；文件转写结果可立即通过创建 **1 个 `MeetingSession`** 并逐条追加
`segments` 行（既有 `SqliteSubtitleRepository`，`StartOffsetMs`/`EndOffsetMs`）持久化。V0.5 计划的 job/queue/checkpoint
表（`MediaFile`/`TranscriptionJob`/`TranscriptionChunk`/`JobCheckpoint`）**推迟到 Phase 4**；数据库仍为**迁移 4**。

### 测试
- 单元 **159 → 190**（+31：新增 `OfflineSegmenterTimingTests`、`OverlapTextDeduplicatorTests`、
  `OfflineTranscriptionEngineTests`、`TranscriptionModeCatalogTests`）。
- 集成 **33 → 41**（+8，含 `FileTranscriptionTests`：4 例 theory + 4 例 fact）。
- **无实时路径回归**；构建 0 错误。

### 验证命令
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build LocalMeetingSubtitle.sln -c Release
dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug

# 长音频测试素材（gitignore 的 testmedia/；由随包模型的 test_wavs/0.wav 生成）
./tools/make-long-testmedia.ps1

# 文件转写 CLI（三模式）
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode fast
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode standard
dotnet run --project tools/FileTranscribe -- --file testmedia/0.mp4 --mode high
dotnet run --project tools/FileTranscribe -- --file testmedia/long-continuous.wav --mode high
dotnet run --project tools/FileTranscribe -- --file testmedia/long-gaps.wav --mode high
```

### 断点续跑信息（下一会话）
- **当前阶段：** V0.5 Phase 2 完成；**Phase 3 未开始**。
- **已完成：** Phase 0 FFmpeg 工具链；Phase 1 媒体解码层；Phase 2 长音频离线 ASR（VAD 分段 + 逐段离线解码 +
  全局时间戳 + 三模式目录）。
- **下一步（Phase 3）：** **角色标注对话（role-tagged dialogue via diarization）** —— 复用 V0.4
  `ISpeakerDiarizationService` 对解码音频做说话人分离，再以新的 `ITranscriptAlignmentService` 把离线转写分段与
  说话人区间对齐成对话轮次。
- **下一步要改的文件：** 新增 `Core/Abstractions/` 下的 `ITranscriptAlignmentService`（对齐契约）、`Core/` 下的对齐服务
  （区间重叠 → 每句最多一个发言人），并接线文件作业路径；测试在 `tests/LocalMeetingSubtitle.IntegrationTests/` 下新增。
- **下一验收目标：** `RELEASE_CHECKLIST` 的 **FT-09..FT-14**（音轨选择、任务级错误处理、断点续跑、分角色标注/对齐）。
- **诚实边界：** 全部目标机（Win11 + Core Ultra 7 155H）验收仍 **BLOCKED/NOT_TESTED**；
  **长音频 CER/WER 因无参考文本未测**；仅 56 s / 65 s 素材，**无 >1 h 文件**；
  SenseVoice 高精度模式**不支持模型级热词**（已核实）。

## Update — V0.5 Phase 3（角色标注对话，role-tagged dialogue）— DONE (2026-10-10)

**目标：** 在 Phase 2 的离线转写之上，对**同一个文件**做**离线说话人分离**，再把转写分段与说话人对齐成
**角色标注对话轮次**（「谁在什么时候说了什么」）。**Phase 3 完成；Phase 4 及以后未开始。**
**实时字幕路径未改动**（无回归）。

### 新增 / 修改的代码（全部增量）
- **新增** `src/LocalMeetingSubtitle.Core/Audio/PcmWavWriter.cs` — **同步**增量 PCM16 WAV 写入器
  （`Write(ReadOnlySpan<float>)`、`FrameCount`、`Duration`，`Dispose` 时回填 RIFF/`data` 大小）。**刻意同步**：
  与实时采集的 `WaveRecordingService`（有界通道 + **丢帧**）不同，文件任务为拉取式，丢一帧就会**静默平移**整条
  分离时间轴。
- **新增** `src/LocalMeetingSubtitle.Core/Models/DialogueModels.cs` —
  `readonly record struct TranscriptSegmentFact(long SegmentId, TimeSpan Start, TimeSpan End, string Text)`；
  `DialogueTurn { string? SpeakerId; SpeakerName; SpeakerColorArgb; Start; End; Text; IReadOnlyList<long> SegmentIds; NeedsConfirmation; Duration; IsUnknownSpeaker }`；
  `DialogueParticipant(SpeakerId, Name, ColorArgb, SpeakingTime, TurnCount, SegmentCount)`；
  `DialogueTranscript { SessionId, Turns, Participants }`；
  `sealed record DialogueAssemblyOptions { TimeSpan MaxGap = 2 s; int MaxTurnChars = 500; string UnknownSpeakerLabel = "未知发言人"; int UnknownSpeakerColorArgb = 0xFF808080 }`。
- **新增** `src/LocalMeetingSubtitle.Core/Abstractions/TranscriptAlignmentAbstractions.cs` →
  `ITranscriptAlignmentService.Align(sessionId, IReadOnlyList<TranscriptSegmentFact>, IReadOnlyDictionary<long, SpeakerAssignment>, IReadOnlyDictionary<string, Speaker>, DialogueAssemblyOptions?)` → `DialogueTranscript`。
- **新增** `src/LocalMeetingSubtitle.Core/Speakers/TranscriptAlignmentService.cs` — **纯函数**。
- **新增** `src/LocalMeetingSubtitle.Core/Abstractions/FileTranscriptionAbstractions.cs` —
  `enum FileTranscriptionPhase { Decode, Transcribe, Diarize, Assemble }`；
  `readonly record struct FileTranscriptionProgress(Phase, double Fraction, TimeSpan Processed, TimeSpan Total, int SegmentsEmitted)`；
  `sealed record FileTranscriptionRequest(InputPath, IAsrEngine Engine, OfflineTranscriptionOptions, int? AudioStreamIndex, bool RunDiarization = true, SpeakerCountMode, int ManualSpeakerCount, double ClusteringThreshold, DialogueAssemblyOptions?, string? Title)`；
  `FileTranscriptionResult { SessionId, Segments, Dialogue, AudioDuration, Elapsed, Rtf, Completed, Cancelled, Diarized, Error, Warning }`；
  `IFileTranscriptionService { bool IsBusy; Task<FileTranscriptionResult> RunAsync(request, IProgress<FileTranscriptionProgress>?, CancellationToken) }`。
- **新增** `src/LocalMeetingSubtitle.Core/Transcription/FileTranscriptionService.cs` — 端到端跑一个文件，
  顺序严格：`SemaphoreSlim(1,1)` 准入（第二个并发任务被拒绝并返回错误结果）→ 探针 → **同一次解码中 tee** 到临时
  16 kHz 单声道 WAV（ASR 仍流式，**整文件从不进内存**）→ Phase 2 `OfflineTranscriptionEngine` → 持久化 1 个
  `MeetingSession` + 每个转写段 1 行 `segments`（**追加时就捕获分配的 `SegmentId`**，绝不信任会跳过空块的
  `SourceChunkId`）→ V0.4 `SpeakerDiarizationService.RunAsync`（**未改动复用**）→ 读 `GetAssignmentsAsync` +
  `GetSpeakersAsync` → `ITranscriptAlignmentService.Align` → 结果。临时 WAV 放在**调用方指定的暂存目录**
  （App 传入 `LocalDataPaths.RecordingsDirectory`，故既有的 24 h 孤儿清扫可覆盖崩溃），`finally` 删除。
  分离为**软失败**：忙/失败 → `Warning`、`Diarized=false`、全未知对话，**绝不抛异常**。调用方持有
  `request.Engine`（**从不释放**）。另有长度校验：比对写入 WAV 的时长与转写音频时长，不一致则告警。
- **修改** `src/LocalMeetingSubtitle.App/App.xaml.cs` — 注册 `ITranscriptAlignmentService` 与
  `IFileTranscriptionService`（暂存目录 = `LocalDataPaths.RecordingsDirectory`）并加 `using LocalMeetingSubtitle.Core.Transcription;`。
  **未改 `MainViewModel` / UI**（编辑器归 Phase 5）。
- **修改** `tools/FileTranscribe/Program.cs` + `.csproj`（新增 `Audio` + `Storage` 引用）— 新增 `--diarize` 与
  `--out <path>`。带 `--diarize` 时构建临时 SQLite 库 + 由 `DiarizationModelCatalog` 建 V0.4
  `SpeakerDiarizationService`，跑完整 `FileTranscriptionService`，打印 `[hh:mm:ss.fff - hh:mm:ss.fff] Speaker: text`
  轮次、`PARTICIPANT …` 行与 `PARTICIPANTS=/TURNS=`；`--json` 现在也输出 `participants`/`turns`。两条路径都在
  `finally` 删除临时产物（DB + `-wal`/`-shm`、暂存 WAV）。
- **修改** `tools/make-long-testmedia.ps1` — 现在还会生成 `testmedia/two-speakers.wav`（说话人 A ×2、间隔、
  说话人 B ×2，来自 `models/_diar-eval/fangjun-sr-1.wav` + `leijun-sr-1.wav`；**451,262 字节 ≈ 14.1 s**）。

### 关键决策（详见 `docs/DECISIONS.md` D14–D16）
1. **临时 WAV（tee）+ 未改动复用 `SpeakerDiarizationService`（D14）。** V0.4 分离器是**整文件**引擎，吃**文件路径**、
  经 `IAudioFileLoader`（NAudio）加载；NAudio 读不了视频容器（`.mp4/.mkv/.mov`），而文件任务必须支持视频。
   既然 FFmpeg 已解码，任务就自己写 16 kHz 单声道 WAV，**未改动复用** V0.4 服务——不碰已测 V0.4 代码、不重复
   run/speaker/interval/assignment 持久化、支持视频、ASR 仍流式。**被否决的替代方案：** 把整个解码文件缓存进内存
   （4 h @16 kHz float32 ≈ **920 MB**）、给 `ISpeakerDiarizationService` 加基于采样的重载。
   两个安全设计：**全新的同步写入器**（文件任务拉取式，丢帧会平移时间轴，故不丢帧、阻塞解码代）+ **丢帧/长度校验**
   （比对写入 WAV 时长与转写音频时长，不一致告警）。
2. **对话由持久化的 `speaker_assignments` 组装（D15），而不是再跑一次 `ISpeakerAlignmentService`。** 分离器的
   **原始聚类索引 → speaker-id** 映射只在 `SpeakerDiarizationService` 内**临时**构建、**不持久化**；而
   `speaker_assignments` 已按 segment id 存了**最终选定**的发言人（含 `NeedsConfirmation`/`Confidence`）。
3. **对话合并规则（D16）。** `TranscriptAlignmentService` 把**同一说话人**的连续分段合并成一轮；在**换说话人**、
   **间隔 > `MaxGap`（默认 2 s）**、或文本将超过 `MaxTurnChars`（默认 500）时**开新轮**。轮次取**首个 `Start`**、
   **末个 `End`**、**拼接文本**、组成它的 segment id，以及 **OR 传播的 `NeedsConfirmation`**。归属缺失或
   `SpeakerId` 为 null 的分段成为**未知轮**（`SpeakerId == null`、标签 `未知发言人`、灰 `0xFF808080`），
   **不计入 `Participants`**；`Participants` 按已知识别说话人聚合说话时长/轮数/段数，**按时长降序**。**幂等**，
   故 Phase 5 编辑器可重组。

### 实测证据（开发主机：Windows 10 Pro，Xeon 64 逻辑核，64 GB）
> 目标硬件（X1 Carbon Gen 12 / Core Ultra 7 155H）**不可用** —— 以下**未**在目标机验证。

命令：`tools\FileTranscribe\bin\Debug\net8.0-windows\FileTranscribe.exe --file testmedia\two-speakers.wav --mode high --diarize --models-root models`

结果（逐字，日记简化）：
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
即素材前半（说话人 A，2 段）合并成一个 A 轮、后半（说话人 B，4 段）合并成一个 B 轮——正好 **2 个说话人、2 个轮次**。
运行结束后，`%TEMP%` 中**没有**遗留 `dijob-*.wav`、`ft-*.db`/`-wal`/`-shm`，暂存目录中也**没有**遗留 WAV。

### 已知限制（诚实记录）
1. **分离是整文件操作：** V0.4 服务上限 **4 h**，且 `NaudioAudioFileLoader.LoadMono` 先建 `List<float>` 再
   `.ToArray()`（4 h 约 **1.8 GB** 瞬态）。**> 2 h** 的文件视为高风险；记为开放限制（`KNOWN_ISSUES` P3-17）。
2. **进程中途死亡**时临时 WAV 会留在 recordings 目录，直到**下次应用启动**清扫（24 h 孤儿清扫）；CLI 在 `finally`
   删除（P3-18）。
3. `DiarizationProgress.Fraction` 全程渲染 `0.0`（sherpa 回调报告未知总数）。这是**既有 V0.4 外观问题，非 Phase 3
   引入**，且 `IProgress` 上报仍会到达（P3-19）。
4. **文件转写仍无 UI 入口**（Phase 5 是对话编辑器）；`AppSettings.EnableVadSegmenting` 仍未接线（P3-2）。
5. `SubtitleSegment.SpeakerName` 仍为瞬态；Phase 3 **不改持久化、不改导出格式化器**（Phase 6 负责对话导出）。

### 测试
- 单元 **190 → 208**（**+18**：新增 `TranscriptAlignmentServiceTests`、`FileTranscriptionServiceTests`，以及
  `TestDoubles.cs` 中的共享测试替身）。
- 集成 **41 → 43**（**+2**：新增 `FileTranscriptionDiarizationTests`——真实两人文件产出双人对话；以及视频文件
  `testmedia/two-tracks.mp4` 音轨 1，证明临时 WAV tee 使视频可分离）。
- **无实时路径回归**；`MainViewModel` 与实时管线未改动；构建 0 错误。

### 验证命令
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build LocalMeetingSubtitle.sln -c Release
dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug

# 两人测试素材（gitignore 的 testmedia/）
./tools/make-long-testmedia.ps1

# 角色标注对话（分离 + 对齐）
tools\FileTranscribe\bin\Debug\net8.0-windows\FileTranscribe.exe --file testmedia\two-speakers.wav --mode high --diarize --models-root models
```

### 断点续跑信息（下一会话）
- **当前阶段：** V0.5 Phase 3 完成；**Phase 4 未开始**。
- **已完成：** Phase 0 FFmpeg 工具链；Phase 1 媒体解码层；Phase 2 长音频离线 ASR；Phase 3 角色标注对话
  （临时 WAV tee + V0.4 分离复用 + 纯对齐器 `TranscriptAlignmentService`）。
- **下一步（Phase 4）：** **作业队列 + 检查点/续跑**（`ITranscriptionJobService`）与 **迁移 5**
  （`MediaFile`/`TranscriptionJob`/`TranscriptionChunk`/`TranscriptSegment`/`JobCheckpoint`）；数据库当前仍为
  **迁移 4**，`FileTranscriptionService` 目前以 `SemaphoreSlim(1,1)` 单任务准入（无排队）。
- **下一步要改的文件：** 新增 `Core/Abstractions/` 下的 `ITranscriptionJobService`、`Core/` 下作业/队列编排与检查点、
  `Storage/` 的**迁移 5** 表与仓储；App DI 注册作业服务；测试在 `tests/` 下新增。
- **下一验收目标：** `RELEASE_CHECKLIST` 的 **FT-09..FT-11**（音轨选择、任务级错误处理、断点续跑）。
- **诚实边界：** 全部目标机（Win11 + Core Ultra 7 155H）验收仍 **BLOCKED/NOT_TESTED**；对话仅在 **14.1 s**
  两人素材上验证，**未分离长文件**；**无参考对话**，未做对齐正确率核对；Phase 3 的限制见 `KNOWN_ISSUES`
  P3-17/P3-18/P3-19。
