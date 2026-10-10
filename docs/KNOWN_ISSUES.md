# Known Issues

This file lists defects found and fixed during development, open (non-blocking) limitations, and
items that could not be verified on the available hardware.

**There are no open P0 defects.** Everything below is either already fixed, or a P3 (minor /
documentation-level) limitation, or an item blocked purely by the absence of the target hardware.

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
| P3-14 | File transcription Phases 2–8 not implemented | Low | Open |
| P3-15 | No `AGENTS.md` in the repo (the V0.5 spec's session-startup ritual references it) | Low | Open |
| P3-16 | No reference transcript → file-transcription CER/WER not measured | Low | Open |
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
ASR → diarization → role-tagged dialogue). **Only Phase 0–1 (FFmpeg tooling + media decode layer) is
implemented**; the following are known and accepted for this state.

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

#### P3-14 — File transcription Phases 2–8 not implemented

- **Impact:** only the media-decode layer exists. There is **no** segment-level/VAD pipeline, **no**
  `ITranscriptionJobService`, **no** `ITranscriptAlignmentService`, **no** migration-5 tables
  (`MediaFile`/`TranscriptionJob`/`TranscriptionChunk`/`TranscriptSegment`/`JobCheckpoint`), **no**
  DOCX export, **no** dialogue editor UI, and **no** player/drag-drop. The database schema is still at
  **migration 4**.
- **Workaround:** none — the feature is not wired into the app; this is in-progress work.
- **Planned:** Phases 2–8 (see [`ARCHITECTURE_V05.md`](ARCHITECTURE_V05.md)); per-item status in
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
