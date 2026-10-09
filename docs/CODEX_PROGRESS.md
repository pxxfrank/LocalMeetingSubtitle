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
