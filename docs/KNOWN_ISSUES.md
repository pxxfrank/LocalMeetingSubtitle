# Known Issues

This file lists defects found and fixed during development, open (non-blocking) limitations, and
items that could not be verified on the available hardware.

**There are no open P0 defects.** Everything below is either already fixed, or a minor /
documentation-level limitation (mostly P3), or an item blocked purely by the absence of the target
hardware. **V0.5 Phase 2 added four fixed defects (two P1, two P2) and one P2 that is still OPEN.**
**V0.5 Phase 3 (role-tagged dialogue) added no code defect; it introduces three open limitations
(P3-17 duration/memory of whole-file diarization, P3-18 a stale temp WAV after a crash, and P3-19 a
pre-existing V0.4 cosmetic progress item).**
**V0.5 Phase 4 (job queue + checkpoint/resume) added no new code defect; it introduces one open
**P2** (P3-20, a segment at a resume boundary whose text can differ from an uninterrupted run) and
six open P3 items (P3-21 .. P3-26), and it supersedes P3-14 (Phase 4 is now implemented).**

## Summary

| ID | Kind | Severity | Status |
| --- | --- | --- | --- |
| P1 | `StreamingResampler` emitted zero samples | High (was P1) | **FIXED** + regression tests |
| P2 | `MockAsrEngine` streaming could spin forever | Medium (test-only) | **FIXED** |
| P1 | Installer flattened the folder tree (`models\` lost, 238 files silently dropped) | High (release) | **FIXED** + re-verified 519/519 files |
| P2 | `MaterialDesignVerticalSeparator` does not exist in MaterialDesignThemes 5.3.2 | Medium (UI) | **FIXED** |
| P1 | Signed Burn `Setup.exe` could not install (0x80070002 — container lost) | High (release) | **FIXED** (bundle intentionally unsigned; MSI + app exe stay signed) |
| P0 | `.gitignore`'s unanchored `models/` rule also ignored `src/...Core/Models/` (git is case-insensitive on Windows), so the 4 Core model files were **absent from the public repo** | Critical (repo would not build from a clone) | **FIXED** — rule anchored to `/models/`, files committed |
| P1 | `Controls.xaml` used `{StaticResource IconChevronDown}` before `Icons.xaml` was merged → main window failed to load | High (UI) | **FIXED** (`DynamicResource`) |
| P2 | Dark theme: stock Aero2 ComboBox/TextBox templates paint a hardcoded white background and ignore `Background`/`SystemColors`, making the text invisible | Medium (UI) | **FIXED** (explicit themed templates) |
| P0 | The built-in lexicon made hotwords always active, and the hotwords file was written to a **Chinese** temp path (`%TEMP%\字幕君\`) that sherpa-onnx cannot read → the recognizer never became ready and **no subtitles appeared at all** | Critical (silent, total failure of the core flow) | **FIXED** — ASCII temp path + an explicit non-ASCII-path guard in the engine |
| P1 | Release packaging: the repository `models/` folder was **flattened** into the publish (the ASR model files landed directly under `models\` instead of `models\<model>\`), so the shipped app reported `installed=False` and **Start was disabled** | High (release-breaking) | **FIXED** — pre-create `models\` and copy each model **directory** to its own sub-path; procedure documented |
| P1 | sherpa-onnx offline `OfflineSession.IsReady()` always returned `true`, so any `while (IsReady()) Decode();` loop **spun/hung forever** (found via `AsrBenchmark --offline`) | High (offline ASR) | **FIXED** — `IsReady()` is true only while audio is accepted-but-undecoded; `Decode()`/`Reset()` clear it |
| P1 | The offline ASR path had never been exercised with a real offline model: the tool fed **100 ms chunks** to a whole-utterance model (garbage), and `BuildOffline` passed a hotwords file that made the recognizer fail to build | High (correctness) | **FIXED** — whole-utterance feed; `BuildOffline` no longer passes hotwords (also fixes the `AsrBenchmark --offline` path) |
| P2 | SenseVoice over-claimed model-level hotwords (`AsrModelCatalog.SenseVoiceSmall.SupportsHotwords` and offline `SherpaOnnxAsrEngine.ModelLevelHotwords`) | Medium (correctness) | **FIXED** — both now `false`, with the verified native error messages as evidence |
| P2 | `TranscriptionPipeline.SwapEngine` rebuilt the `AudioSegmenter` with **defaults**, silently dropping the configured `OfflineSilenceRms`/`OfflineMaxSegmentSeconds` on every engine swap | Medium (latent) | **FIXED** — rebuild from the configured values |
| P2 | The **live offline path** still emits zero-duration segments (`StartOffset == EndOffset == chunk-end`) | Medium | **OPEN** — unchanged by Phase 2 (Phase 2 targets file transcription) |
| P3-1 | Subtitle selection is row-level, not character-level | Low | Open |
| P3-2 | `AppSettings.EnableVadSegmenting` persisted but not wired | Low | Open |
| P3-3 | `AsrNumThreads` applies at next Start / engine swap, not live | Low | Open |
| P3-4 | Hotword editing UI is a one-line-per-hotword text box | Low | Open |
| P3-5 | Floating-window resize not interactively verified | Low | Open |
| P3-6 | No speaker-management UI yet (rename / merge / reassign) | Low | Open → V0.4.2 |
| P3-7 | Manual speaker count only via settings; no dedicated control | Low | Open |
| P3-8 | Post-meeting recording capture not wired (import only) | Low | Open |
| P3-9 | Diarization models are downloaded, not bundled in the publish | Low | Open |
| P3-10 | Real-time + diarization concurrency not measured on target hardware | Low | Open |
| P3-11 | Built-in lexicon: the bundled zh-14M model cannot encode several lexicon terms | Low | Open |
| P3-12 | Bundled FFmpeg is LGPL (decode-only): no libx264, so H.264 **encoding** is unavailable | Low | Open (harmless — 字幕君 only decodes) |
| P3-13 | FFmpeg not yet bundled into the installer / portable ZIP | Low | Open → V0.5 Phase 8 |
| P3-14 | File transcription Phases 5–8 not implemented (Phase 4 is now done) | Low | Open |
| P3-15 | No `AGENTS.md` in the repo (the V0.5 spec's session-startup ritual references it) | Low | Open |
| P3-16 | No reference transcript → file-transcription CER/WER not measured | Low | Open |
| P3-17 | Whole-file diarization: 4 h cap + ~1.8 GB transient at 4 h; files > 2 h risky | Medium | Open |
| P3-18 | A crash mid-job leaves the temporary diarization WAV until the next app start | Low | Open |
| P3-19 | `DiarizationProgress.Fraction` renders `0.0` throughout a run (pre-existing V0.4 cosmetic) | Low | Open |
| P3-20 | A segment produced at a **resume boundary** can have different text from an uninterrupted run (structure/count/timing identical) | **P2** | Open |
| P3-21 | mp3 seeking is not sample-exact (decoder delay, tens of ms); WAV/PCM seeking is bit-exact | Low | Open |
| P3-22 | The worker's `BelowNormal` priority is **aspirational** (continuations run on the thread pool after the first `await`) | Low | Open |
| P3-23 | A **resumed** job performs one extra decode-only pass to rebuild the whole-file diarization WAV | Low | Open |
| P3-24 | The job row's `ProcessedMs` is a throttled (≥ 2 s) **display** snapshot, never the resume cursor | Low | Open |
| P3-25 | Whole-file diarization is unchanged from Phase 3 (same as P3-17) | Low | Open |
| P3-26 | Still no UI for file transcription (Phase 5); `EnableVadSegmenting` unwired (same as P3-2) | Low | Open |
| BLOCKED-1 | Full Start→transcribe→persist UI path | P0 | **CLOSED (2026-10-09, dev host)** — a UI-driven Start on the published build produced and persisted real subtitles |
| BLOCKED-2 | No real-meeting 3-hour stability run | P0 (target) | Blocked (`ThreeHourSoak` never executed) |
| BLOCKED-3 | Installer signature is self-signed / untrusted | P1 (release) | Partial (MSI + Setup.exe produced & signed; no CA-issued certificate) |

## Fixed defects

### P1 — Installer flattened the folder tree *(FIXED)*

- **Symptom:** `字幕君-Setup.exe` / `.msi` reported success but installed a broken
  app: the `models\` folder was gone (so the app could not find its ASR model) and 238 files —
  the localized resource folders (`cs\`, `de\`, `ja\`, …) and `docs\` — were silently missing.
- **Root cause:** the WiX source generator emitted every `<Component>` with `Directory="INSTALLFOLDER"`,
  flattening the tree. Same-named files from the 12 language folders collided and overwrote each
  other. A second attempt emitted one `<Directory>` per leaf path only, so multi-level folders such
  as `models\<name>\` lost their parent.
- **Fix:** the generator now builds the **complete nested directory tree** (every ancestor prefix)
  and places each component in the directory that mirrors its folder.
- **Verification:** install then diff against the publish output → **519 / 519 files, 0 missing**;
  `models\…\encoder…onnx`, `docs\USER_GUIDE.md`, `ja\…resources.dll` all present; the installed app
  logs `model=streaming-zipformer-zh-14M installed=True` and a Start-menu shortcut is created.
- The portable **ZIP was never affected** (it preserves the tree by construction) — only the MSI/Bundle.

### P1 — Signed Burn `Setup.exe` failed to install *(FIXED)*

- **Symptom:** double-clicking `字幕君-Setup.exe` did nothing useful; the Burn log showed
  `Error 0x80070002: Failed to acquire container: WixAttachedContainer` → `exit code 0x2`, nothing installed.
- **Root cause:** I applied an Authenticode signature to the **burn bundle after building it**.
  Authenticode appends the signature at the end of the PE file, which invalidates the location Burn uses
  to find its attached (embedded) container — so the bundle could not find its own MSI payload.
  Signing the MSI is safe; signing the bundle this way is not.
- **Fix:** `installer/build-installer.ps1` no longer signs `Setup.exe` (documented in the script). The
  app exe and the MSI remain signed. Because the certificate is self-signed, the signature was never
  trusted anyway, so nothing is lost.
- **Verification:** `Setup.exe /quiet` → **exit 0, 519/519 files, model present, Start-menu shortcut**.
- A correct signed bundle would require the WiX engine-signing workflow (sign the engine before it is
  attached), which was out of scope given the certificate cannot be trusted regardless.

### P2 — `MaterialDesignVerticalSeparator` style does not exist *(FIXED)*

- **Symptom:** would have thrown at window load (unresolvable `StaticResource`) after the Material
  Design restyle.
- **Root cause:** the resource key was guessed rather than verified.
- **Fix:** the key list was checked against the actual `MaterialDesignThemes.Wpf` assembly resources
  (the correct key is `MaterialDesignSeparator`); the separators were replaced with a plain divider.
- **Verification:** the app launches and renders; all 123 tests still pass.

### P1 — `StreamingResampler` emitted zero samples *(FIXED)*

- **Symptom:** the windowed-sinc resampler produced no output samples, which would have meant silent
  recognition. Found by the unit/perf tests (the resampler test also asserts a non-zero sample count:
  "a resampler that produces no samples is not ‘fast', it is broken").
- **Root cause:** the read position warm-up guard rejected the first outputs (the kernel requires
  samples on both sides of the centre, so the very first blocks had no valid centre).
- **Fix:** prime the internal buffer with `HalfTaps` zero samples and start the read position at
  `HalfTaps`, so the first real sample lands on the kernel centre and no negative index is required.
- **Verification:** regression tests now pass (`StreamingResamplerTests`, `ResamplerThroughputTests`).

### P2 — `MockAsrEngine` streaming could spin forever *(FIXED)*

- **Symptom:** the streaming scripted test double reported `IsReady()` as always true, so the pipeline
  loop could decode without bound (a test-infrastructure hang).
- **Fix:** the mock now consumes exactly one scripted hypothesis per accepted buffer
  (`AcceptWaveform` sets ready once; `Decode` clears it), mimicking a real streaming engine.
- **Verification:** pipeline long-run tests complete deterministically.

#### P0 — Non-ASCII hotwords path silently disabled recognition entirely *(FIXED)*

- **Symptom:** after adding the built-in lexicon, starting transcription produced **no subtitles at
  all** — audio was captured (160k+ samples accepted) but the recognizer never became ready
  (`IsReady=False`), and the bounded self-heal rebuilt the session without effect. Identical signature
  to the earlier non-ASCII *model* path defect.
- **Root cause:** `MainViewModel.BuildHotwordFile()` wrote the hotwords file to
  `%TEMP%\字幕君\hotwords.txt` — a **Chinese** directory. sherpa-onnx opens that file from native code and
  cannot read a non-ASCII path; the C# wrapper does not surface the failure. Previously the app never
  produced a hotwords file (0 hotwords → `hotwords=off`), so the latent bug was never triggered; the
  lexicon made hotwords **always** active, which activated it.
- **Proof (A/B, `tools/AsrBenchmark`, same audio/model):** the same hotwords file under an **ASCII**
  path → `RTF=0.0588`, correct text; under a **Chinese** path → `RTF=0.0002`, **empty text**.
- **Fix:** `ModelHotwordFile.DefaultTempPath` (ASCII, `%TEMP%\SubtitleJun\hotwords.txt`) is now the only
  hotwords path; and `SherpaOnnxAsrEngine.InitializeAsync` **rejects** a non-ASCII model directory or
  hotwords path with an explicit error, so this class of failure is loud instead of silent.
- **Verification:** the published 0.4.0 build, started through the UI, captured loopback audio and
  **persisted 3 real Chinese subtitle segments** (see BLOCKED-1).
- **Regression guards:** `ModelHotwordFileTempPathTests` (unit) and `NonAsciiPathGuardTests` (integration).

#### P0 — Release packaging flattened the bundled `models/` tree *(FIXED)*

- **Symptom:** after a clean republish, the shipped app reported
  `Startup check complete: … model=streaming-zipformer-zh-14M installed=False` and the **Start button
  was disabled** — the app could not transcribe at all.
- **Root cause:** the release procedure copied the models with
  `Copy-Item -Recurse -Force models\<dir> <publish>\models` **without pre-creating** `<publish>\models`.
  With a non-existent destination, PowerShell creates it as a copy of the *first* source directory, so
  the 14M model's files landed directly in `models\` instead of `models\<model-dir>\`. The app looks for
  `models\<model-dir>\encoder-….int8.onnx`, did not find it, and correctly disabled Start.
- **Fix (procedure):** create the directory first and copy each model **to its own sub-path**:
  ```powershell
  $dest = "dist/SubtitleJun-win-x64/models"
  Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  foreach ($m in @("sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23",
                   "sherpa-onnx-pyannote-segmentation-3-0",
                   "3dspeaker-eres2net-base-zh-16k")) {
      Copy-Item -Recurse -Force -Path "models/$m" -Destination (Join-Path $dest $m)
  }
  ```
- **Verification:** after the fix the published build reports `installed=True`, Start is enabled, and a
  UI-driven run persisted real subtitles (see BLOCKED-1).
- **Lesson:** always launch the *packaged* app once before shipping — a compile + unit test cannot catch
  a broken model layout.

### P1 — sherpa-onnx offline `OfflineSession.IsReady()` always returned `true` *(FIXED)*

- **Symptom:** with a real offline model, any `while (IsReady()) Decode();` loop **spun/hung forever**.
  Found because `tools/AsrBenchmark --offline` hung.
- **Root cause:** the sherpa-onnx offline session's `IsReady()` was implemented to return `true`
  unconditionally once audio had been accepted, so the decode loop never terminated.
- **Fix:** `OfflineSession.IsReady()` now returns `true` **only while audio is accepted but not yet
  decoded**, and both `Decode()` and `Reset()` clear it.
- **Why the live pipeline was unaffected:** it called `Decode()` **exactly once** and never looped on
  `IsReady()`, so the bug stayed latent there.

### P1 — the offline ASR path had never been exercised with a real offline model *(FIXED)*

- **Symptom / root cause:** offline ASR (SenseVoice) had only ever been *declared*, never run. Running
  it exposed two real bugs:
  1. `tools/AsrBenchmark` fed the offline model **100 ms chunks**, so it decoded fragments in isolation
     (garbage output) instead of the whole utterance.
  2. `SherpaOnnxAsrEngine.BuildOffline` passed a **hotwords file** that makes the recognizer **fail to
     construct**.
- **Fix:** the offline benchmark path now accepts the **whole utterance in one `AcceptWaveform` +
  decode**; `BuildOffline` no longer sets `HotwordsFile` and hardcodes `greedy_search`.
- **Note:** the **`AsrBenchmark` offline path was fixed** as part of this.

### P2 — SenseVoice over-claimed model-level hotwords *(FIXED)*

- **Symptom:** `AsrModelCatalog.SenseVoiceSmall.SupportsHotwords` and the offline
  `SherpaOnnxAsrEngine` capability `ModelLevelHotwords` claimed hotword support for SenseVoice.
- **Proof (VERIFIED, `tools/AsrBenchmark --offline --model-id sense-voice-small-int8`):**
  - with `--hotwords` + default decoding the native layer refuses to build:
    `offline-recognizer.cc:Validate:88 Please use --decoding-method=modified_beam_search if you provide --hotwords-file. Given --decoding-method='greedy_search'`;
  - with `--decoding modified_beam_search` it **also** refuses:
    `offline-recognizer-sense-voice-impl.h:82 Only greedy_search is supported at present. Given modified_beam_search`.
- **Fix:** both flags are now **false**. Domain terms for the High-accuracy mode must go through
  `TextCorrectionEngine` (correction rules), not model-level hotwords.

### P2 — `TranscriptionPipeline.SwapEngine` rebuilt the `AudioSegmenter` with defaults *(FIXED)*

- **Symptom:** on every engine swap (the hotword re-apply path), the `AudioSegmenter` was rebuilt with
  **default** `OfflineSilenceRms` / `OfflineMaxSegmentSeconds`, silently discarding the configured
  values.
- **Fix:** `SwapEngine` now rebuilds the `AudioSegmenter` from the configured
  `OfflineSilenceRms` / `OfflineMaxSegmentSeconds`.

## Open limitations (P3 — minor)

### P3-1 — Subtitle selection is row-level, not character-level

- **Impact:** the transcript is a `ListBox`; users can select whole rows (`SelectionMode="Extended"`)
  and copy them, but cannot select individual characters/words within a line.
- **Workaround:** copy the whole row (Copy button) or export to TXT/SRT/Markdown.

### P3-2 — `AppSettings.EnableVadSegmenting` is persisted but not wired

- **Impact:** the setting round-trips through the settings store and UI but does not currently change
  segmentation behaviour (offline segmentation uses `AudioSegmenter` defaults).
- **Workaround:** none needed; it has no effect on streaming models.

### P3-3 — `AsrNumThreads` applies at next Start / engine swap, not live

- **Impact:** changing the ASR thread count while transcribing does not take effect until the next
  Start or hotword-driven engine swap.
- **Workaround:** Stop then Start, or re-apply hotwords, to pick up the new value.

### P3-4 — Hotword editing UI is a simple one-line-per-hotword text box

- **Impact:** hotwords are edited as plain lines (one hotword per line) with a fixed score; there is
  no dedicated hotword manager (groups / per-entry enable / per-entry score UI).
- **Workaround:** edit the text box; the underlying `hotwords` / `hotword_groups` schema supports
  richer data for future UI.

### P3-5 — Floating-window resize not interactively verified

- **Impact:** the floating subtitle window's always-on-top, click-through and restore behaviour are
  implemented and code-verified, but interactive resize was not manually verified on-screen.
- **Workaround:** adjust font size / opacity from Settings.

### V0.4.0 — open limitations (P3)

V0.4.0 adds offline speaker diarization (import a recording → anonymous speaker labels A/B/…). The
following limitations are known and accepted for this version.

#### P3-6 — No speaker-management UI yet (rename / merge / reassign)

- **Impact:** a session can be diarized and each subtitle tagged with an anonymous speaker
  (`A`/`B`/…), but there is **no UI** to rename a speaker, merge two speakers, or reassign an
  individual segment's speaker. The data model is already prepared — `Speaker.DisplayName`,
  `Speaker.IsMerged`, `SpeakerAssignment.Source` and `ISpeakerRepository.MergeSpeakersAsync` /
  `SetAssignmentSpeakerAsync` exist — so only the UI is missing.
- **Workaround:** none; the anonymous labels are still usable to read who spoke when.
- **Planned:** V0.4.2 (rename / merge / reassign / by-speaker filter + export). V0.4.3
  (dual-stream / near-real-time) is likewise not started.

#### P3-7 — Manual speaker count only via settings; no dedicated control

- **Impact:** the expected speaker count is taken from `AppSettings.DiarizationSpeakerCount`
  (0 = automatic) together with `AppSettings.DiarizationClusteringThreshold` (default 0.5). There is
  no per-run control in the diarization flow itself ("analyze with exactly N speakers").
- **Workaround:** set the values in Settings before running diarization; the default (0, auto) needs
  no change.

#### P3-8 — Post-meeting recording capture not wired (import only)

- **Impact:** diarization works only on an **imported** audio file
  (`IShellService.PickAudioFile()` → `NaudioAudioFileLoader`). The post-meeting *recording capture*
  side — capturing the meeting to a file so it can be analysed without a separate recording — is
  **not** wired. The `AudioAssetKind.TempRecording` / `RetainedRecording` values exist but are not
  produced by any capture path yet.
- **Workaround:** record the meeting with any recorder, then import the file and diarize it.

#### P3-9 — Diarization models are downloaded, not bundled in the publish

- **Impact:** the release publish/installer has **not** been rebuilt for 0.4.0, and the two
  diarization models (`pyannote-segmentation-3-0`, `3dspeaker-eres2net-base-zh-16k`) are **not yet
  bundled** into the publish. They must be fetched on demand.
- **Workaround:** install them with `ModelManager` before diarizing (see
  [`DEVELOPMENT.md`](DEVELOPMENT.md)):
  `dotnet run --project tools/ModelManager -- install --id pyannote-segmentation-3-0` and
  `--id 3dspeaker-eres2net-base-zh-16k` (add `--models-root "$env:LOCALAPPDATA\SubtitleJun\models"`
  for the app).

#### P3-10 — Real-time + diarization concurrency not measured on target hardware

- **Impact:** by design diarization runs on a dedicated **below-normal-priority** thread and is
  CPU-thread-capped (`SherpaOfflineSpeakerDiarizer`, default `min(ProcessorCount/4, 4)`), so it
  cannot starve the recognizer. However the **combined** load of live transcription **and** a
  concurrent diarization run was **not measured** on the target hardware.
- **Workaround:** run diarization *after* the meeting (the intended flow) rather than during live
  transcription.

#### P3-11 — Built-in lexicon: the bundled zh-14M model cannot encode several lexicon terms

- **Impact (verified by loading the lexicon as a hotwords file):** the bundled **Chinese-only** model's
  vocabulary has **no entry** for some lexicon terms, so sherpa-onnx logs
  `Failed to encode some hotwords, skip them` and simply skips them (no crash, recognition continues):
  - pure-Latin acronyms — `Atlas`, `CANN`, `MDC`, `HCSO`, `openEuler`, `GaussDB`, `ArkTS`, `ArkUI`,
    `ModelArts`, `MindSpore`, `IPD`, `LTC`, `ITR`, `OTN`, `eSIM`, `5G`, `5.5G`, `6G`, `Massive MIMO`;
  - the **rare character 昇** — so `昇腾`, `昇思`, `毕昇`, `昇腾AI` cannot be boosted either.
- **Mitigation already in place:** such terms are still handled by the **text-correction rules**
  (e.g. `升腾 → 昇腾`, `毕升 → 毕昇`), and the acronyms will boost normally with an **English-capable**
  model (the catalog's bilingual `streaming-zipformer-bilingual-zh-en`); the
  `Failed to encode …` lines are native `stderr` output and are not surfaced in the app (WinExe discards stderr).
- **Not measured:** there is **no domain test audio** on the dev host, so the lexicon's *accuracy gain*
  is **not measured** — only its cost is (measured RTF 0.059 with 116 hotwords vs 0.071 without, i.e. no
  regression; output on unrelated audio changes only by a different beam path).
- **Workaround:** extend the hotword list with your own terms and/or select the bilingual model.

### V0.5 — open limitations (P3)

V0.5 adds an **offline file-transcription** workflow (import audio/video → FFmpeg decode → offline
ASR → diarization → role-tagged dialogue). **Phase 0–3 (FFmpeg tooling + media decode layer + segmented
long-audio offline ASR + role-tagged dialogue) are implemented**; the following are known and accepted
for this state.

#### P3-12 — The bundled FFmpeg is LGPL and cannot encode H.264

- **Impact:** the pinned FFmpeg build is **LGPL v3** (by design — so it is redistributable as a
  separate program; see [`LICENSES.md`](LICENSES.md)). Because it has **no libx264** (which is GPL),
  **H.264 *encoding* is unavailable**.
- **Why this is harmless:** 字幕君 only ever **decodes** media; H.264 **decoding** is supported by the
  native FFmpeg decoders. The build's other encoders (libmp3lame, aac, libvorbis, libopus) are present
  for the test media.
- **Workaround:** none needed.

#### P3-13 — FFmpeg is not yet bundled into the installer / portable ZIP

- **Impact:** FFmpeg is provisioned for development/build by `tools/fetch-ffmpeg.ps1` into
  `third_party/ffmpeg/` (gitignored); the V0.4.0 installer and portable ZIP **do not contain FFmpeg**
  yet, so file transcription would fail with `MediaErrorKind.ToolMissing` on such a build.
- **Workaround (dev):** run `./tools/fetch-ffmpeg.ps1` before building; the app's `.csproj` copies
  `third_party/ffmpeg` next to the app **when present**.
- **Planned:** V0.5 Phase 8 (bundle FFmpeg into the release artifacts).

#### P3-14 — File transcription Phases 5–8 not implemented

- **Impact:** Phases 0–4 exist (media decode, segmented offline ASR, role-tagged dialogue, and the
  job queue + checkpoint/resume with migration 5). There is still **no** DOCX export, **no** dialogue
  editor UI, and **no** player/drag-drop. The database schema is now at **migration 5**
  (`media_files` / `transcription_jobs`; see [`ARCHITECTURE_V05.md`](ARCHITECTURE_V05.md) §5 and
  [`DECISIONS.md`](DECISIONS.md) D17).
- **Workaround:** none — the file job is still **not** wired into the app UI (only `tools/FileTranscribe`).
- **Planned:** Phases 5–8 (see [`ARCHITECTURE_V05.md`](ARCHITECTURE_V05.md)); per-item status in
  [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) (FT-01 .. FT-25).

#### P3-15 — No `AGENTS.md` in the repository

- **Impact:** the V0.5 spec's session-startup ritual references an `AGENTS.md` at the repo root, which
  **does not exist**. There is therefore no `AGENTS.md` for an agent to read at session start.
- **Mitigation in place:** [`CODEX_PROGRESS.md`](CODEX_PROGRESS.md) (phase progress, build/test
  results, **resume block**, important commands) plus [`DEVELOPMENT.md`](DEVELOPMENT.md) (environment
  setup, build/test/publish, model + FFmpeg tooling) already serve that role. The V0.5 update entry in
  `CODEX_PROGRESS.md` contains an explicit resume block for the next session.
- **Proposed:** add a short root `AGENTS.md` that points at `CODEX_PROGRESS.md` + `DEVELOPMENT.md`
  and restates the doc-only/`.cs`-frozen conventions — **not yet created**.

#### P3-16 — No reference transcript → file-transcription CER/WER not measured

- **Impact:** there is **no reference transcript** on the dev host, so the accuracy of file
  transcription (**CER / WER**) is **not measured** — only that decoded media produces plausible
  Chinese text. No accuracy claim is made.
- **Workaround:** none; accuracy measurement needs a reference transcript, which is not available.

#### P3-17 — Whole-file diarization: duration/memory limit (files > 2 h risky) *(OPEN, medium)*

- **Impact:** the V0.4 diarizer reused for file jobs is a **whole-file** operation. It caps at **4 h**,
  and `NaudioAudioFileLoader.LoadMono` builds a `List<float>` and then calls `.ToArray()` — roughly
  **~1.8 GB of transient memory at 4 h** of 16 kHz audio. Files **longer than ~2 h** should therefore be
  treated as risky.
- **Measured evidence:** only the **14.1 s** two-speaker fixture (`testmedia/two-speakers.wav`) was
  diarized; **no long file was diarized**, so the limit is documented as an open design constraint, not
  a measured failure.
- **Workaround:** none yet — split very long recordings before diarizing, or wait for a streaming
  diarization path (not planned for V0.5).
- **Not fixed:** this is a property of the reused V0.4 engine; Phase 3 deliberately did not modify it
  (see [`DECISIONS.md`](DECISIONS.md) D14).

#### P3-18 — A crash mid-job leaves the temporary diarization WAV until the next start *(OPEN, low)*

- **Impact:** the file job writes a temporary 16 kHz mono WAV into a caller-supplied staging directory
  (the app passes `LocalDataPaths.RecordingsDirectory`). If the process dies mid-job, that WAV is left
  behind until the **existing 24 h orphan sweep** removes it on the next app start. The
  `tools/FileTranscribe` CLI deletes its temp artefacts in `finally` (WAV, SQLite DB, `-wal`/`-shm`).
- **Measured evidence:** after the verified runs, **no** `dijob-*.wav`, **no** `ft-*.db`/`-wal`/`-shm`,
  and **no** leftover staging WAV remained in `%TEMP%`.
- **Workaround:** none needed — the daily sweep covers it; do not delete files under
  `%LOCALAPPDATA%\SubtitleJun\recordings\` manually while a job runs.

#### P3-19 — `DiarizationProgress.Fraction` renders `0.0` during a run *(OPEN, low — pre-existing V0.4)*

- **Impact:** the diarization progress **fraction** is reported as `0.0` for the whole run (the sherpa
  callback reports an **unknown total**, so no fraction can be computed). `IProgress` reports still
  arrive; only the fraction is unusable.
- **Status:** this is a **pre-existing V0.4 cosmetic issue, NOT introduced by Phase 3** — Phase 3 reused
  the V0.4 diarization service unchanged.
- **Workaround:** show indeterminate progress (a busy indicator) rather than a percentage.
- **Not fixed:** explicitly out of scope for Phase 3.

### V0.5 Phase 4 — open limitations

Phase 4 adds the **job queue + checkpoint/resume** (migration 5). It introduces no new code defect; the
following are known and accepted for this state.

#### P3-20 — A segment at a resume boundary can differ from an uninterrupted run *(OPEN, P2 — medium)*

- **Impact:** the first segment produced **immediately after a resume** can be transcribed slightly
  differently from an uninterrupted run, because the VAD/recognizer restarts there with different
  leading context. Observed on the two-speaker fixture: the uninterrupted run produced `四次班年度演。`
  for that window while the resumed run produced `年度演。`.
- **What is *not* affected:** segment **count, ordering, sequence numbers and timing are preserved**,
  and nothing is duplicated. The transcript is **structurally identical**; it is **not** textually
  identical — every segment **except the one at the resume boundary** matches exactly. Do **not** read
  this as "the resumed transcript is identical to the original".
- **Workaround:** none; the resumed transcript is usable and the timeline stays correct. This is a
  property of restarting the VAD/recognizer at an arbitrary offset.
- **Not fixed:** inherent to resuming a streaming recognizer at a non-zero offset.

#### P3-21 — mp3 seeking is not sample-exact *(OPEN, low)*

- **Impact:** the resume seek uses ffmpeg input seeking (`-ss` before `-i`, `-accurate_seek`). For
  **WAV/PCM** this is **bit-exact** (verified: mean sample difference < 1e-6); for **mp3** it is **not**
  sample-exact (decoder delay, tens of ms).
- **Why it is harmless:** the emitted `PcmBlock.Start` positions stay **absolute** on the media
  timeline either way, so segment timestamps remain correct.
- **Workaround:** none needed.

#### P3-22 — The worker's below-normal thread priority is aspirational *(OPEN, low)*

- **Impact:** `TranscriptionJobService` creates its worker with
  `Thread { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "file-transcription-worker" }`,
  but **after the first real `await` inside a run the continuations execute on the thread pool** — the
  priority hint mainly covers process start-up, so the queued work is **not** guaranteed to stay at
  below-normal priority end-to-end.
- **Real isolation (unchanged):** **one job at a time**, low thread caps, and **separate ffmpeg child
  processes**. The "below-normal priority" claim is **aspirational**, not a measured guarantee (see
  [`ARCHITECTURE_V05.md`](ARCHITECTURE_V05.md) §7).

#### P3-23 — A resumed job performs one extra decode-only pass *(OPEN, low)*

- **Impact:** the whole-file staging WAV for diarization is tee'd only when the run starts at offset
  zero. A **resumed** job **rebuilds** the whole-file WAV first with **one extra decode-only pass** (the
  V0.4 diarizer is whole-file and NAudio cannot read video). A **fresh** job needs no extra pass.
- **Workaround:** none needed (one extra pass over the file).
- **See also:** [`DECISIONS.md`](DECISIONS.md) D19 and [`PERFORMANCE_REPORT.md`](PERFORMANCE_REPORT.md).

#### P3-24 — `ProcessedMs` is a throttled display snapshot, never the resume cursor *(OPEN, low)*

- **Impact:** the job row's `ProcessedMs` / `SegmentsEmitted` are written at most every **2 s** as a
  **display** snapshot. They are **never** used to resume — the resume cursor is always derived from the
  committed `segments` (`MAX(EndOffsetMs)`, `MAX(SequenceNumber)+1`; see [`DECISIONS.md`](DECISIONS.md)
  D17). A progress bar driven by `ProcessedMs` can therefore lag the true committed state by up to ~2 s.
- **Workaround:** treat the committed `segments` as the source of truth for resume/progress.

#### P3-25 — Whole-file diarization is unchanged from Phase 3 *(OPEN, medium — same as P3-17)*

- **Impact:** a file job still runs the **whole-file** V0.4 diarizer (cap **4 h**; `LoadMono` builds a
  `List<float>` then `.ToArray()`, ≈ **1.8 GB** transient at 4 h; files **> 2 h** risky). Phase 4 did
  **not** change this.
- **Measured evidence:** Phase 4 exercised diarization only on the **14.1 s** two-speaker fixture (it ran
  over the whole file — `225592 samples` — proving the WAV rebuild, but that is a short file); no long
  file was diarized.
- **Workaround:** see P3-17 (split very long recordings before diarizing).

#### P3-26 — Still no UI for file transcription *(OPEN, low — same as P3-2 / P3-14)*

- **Impact:** Phase 4 runs the queue through `tools/FileTranscribe --jobs` and the tests only; there is
  **no UI** (the dialogue editor is Phase 5) and `AppSettings.EnableVadSegmenting` remains **unwired**
  (P3-2). There is no progress/status UI for a queued or running job.
- **Workaround:** use `tools/FileTranscribe --jobs [--list-jobs]` on the CLI; see
  [`DEVELOPMENT.md`](DEVELOPMENT.md).

## Open defect (P2 — still open)

### P2 — the live offline path still emits zero-duration segments *(OPEN)*

- **Symptom:** on the **live** offline path a persisted segment can have
  `StartOffsetMs == EndOffsetMs == (chunk end)` — i.e. a zero-length span.
- **Status:** **unchanged by Phase 2** (Phase 2 targets file transcription), so this is still **OPEN**.
  The file-transcription path (`OfflineTranscriptionEngine`) does **not** have this problem — it reports
  the speech onset and clamps a carried-over start to the previous segment's end, so its timeline never
  overlaps or collapses.
- **Workaround:** none; file transcription is unaffected.

## Blocked / NOT_TESTED items

### BLOCKED-1 — Full Start → transcribe → persist path — **CLOSED (2026-10-09, dev host)**

- **Previously blocked because:** no real meeting audio was available on the dev host.
- **Now exercised:** the **published 0.4.0 build** was launched, the **Start** button invoked through UI
  Automation, and `test_wavs/0.wav` played through the default render endpoint so WASAPI loopback
  captured it. Result: session `bb473befdf724f8b820c5bbadd1a9ad9` with **3 persisted segments**
  (e.g. `#1 [759 ms..11959 ms] 对我做了介绍那么我想说的是…`), `decoding=modified_beam_search, hotwords=on`.
- **Status:** the integrated UI path now works on the dev host. **Target-hardware acceptance (latency /
  CPU / 3-hour soak) is still NOT_TESTED** — this closes the "does the app work end-to-end" question, not
  the performance-acceptance one.
- **Note:** this run is also what exposed the P0 non-ASCII hotwords-path defect above.

### BLOCKED-2 — No real-meeting 3-hour stability run

- **Why:** `ThreeHourSoak` requires ~3 hours of wall-clock time and was never executed.
- **Status:** **NOT_TESTED**. The 10-minute scripted pipeline run passed, but that is not a substitute.

### BLOCKED-3 — No code-signed installer

- **Why:** only the portable, self-contained ZIP (`dist/字幕君-win-x64/`) was produced.
- **Status:** **NOT_TESTED**. **Do not claim the release is signed.** Code signing is optional and
  would need a certificate, if desired.
