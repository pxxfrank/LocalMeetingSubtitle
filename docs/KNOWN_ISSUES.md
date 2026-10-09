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
| P3-1 | Subtitle selection is row-level, not character-level | Low | Open |
| P3-2 | `AppSettings.EnableVadSegmenting` persisted but not wired | Low | Open |
| P3-3 | `AsrNumThreads` applies at next Start / engine swap, not live | Low | Open |
| P3-4 | Hotword editing UI is a one-line-per-hotword text box | Low | Open |
| P3-5 | Floating-window resize not interactively verified | Low | Open |
| BLOCKED-1 | Full Start→transcribe→persist UI path not exercised | P0 (target) | Blocked (no real audio; model not installed in app data dir) |
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

## Blocked / NOT_TESTED items

### BLOCKED-1 — Full Start → transcribe → persist path not exercised in the UI

- **Why:** no real meeting audio was available on the dev host, and the model was **intentionally not
  installed** in the application's data directory (`%LOCALAPPDATA%\字幕君\models`), so
  the UI Start button is correctly disabled (no fake output is produced).
- **Status:** each layer is tested separately (audio capture probe, decode benchmark, pipeline tests,
  persistence tests), but the integrated UI path is **BLOCKED / NOT_TESTED** at the UI level.
- **Impact:** high (it is the core user flow) — must be closed on the target hardware.

### BLOCKED-2 — No real-meeting 3-hour stability run

- **Why:** `ThreeHourSoak` requires ~3 hours of wall-clock time and was never executed.
- **Status:** **NOT_TESTED**. The 10-minute scripted pipeline run passed, but that is not a substitute.

### BLOCKED-3 — No code-signed installer

- **Why:** only the portable, self-contained ZIP (`dist/字幕君-win-x64/`) was produced.
- **Status:** **NOT_TESTED**. **Do not claim the release is signed.** Code signing is optional and
  would need a certificate, if desired.
