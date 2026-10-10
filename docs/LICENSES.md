# Third-Party Licenses

This file is the **consolidated license summary for the V0.5 file-transcription feature** and the
licenses that V0.5 touches. It focuses on the **new** dependency (FFmpeg) and on the components that
the file path reuses; the **pre-existing** component/model list lives in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) and is **not duplicated verbatim** here.

Legend: the license column states the governing license; "separate program" and "linked" describe
*how* the component is used, which is what decides the redistribution obligation.

| Component | Version | License | How it is used |
| --- | --- | --- | --- |
| **FFmpeg** (`ffmpeg.exe`, `ffprobe.exe` + shared DLLs) | see below | **LGPL v3** | **separate program** — invoked as a child process; never linked into 字幕君 |
| **sherpa-onnx** | 1.13.8 | Apache-2.0 | linked (native DLL) |
| **pyannote segmentation 3.0** (diarization model) | — | MIT (pyannote / CNRS) | model file, loaded by sherpa-onnx |
| **3D-Speaker ERes2Net** (speaker-embedding model) | — | Apache-2.0 (3D-Speaker / ModelScope) | model file, loaded by sherpa-onnx |
| **NAudio** | 2.2.1 | MIT | linked |
| **Microsoft.Data.Sqlite** | 8.0.31 | MIT | linked |
| **.NET 8 / .NET Runtime (BCL)** | 8.0.x | MIT | linked (runtime / WPF) |

The ASR models, the other NuGet packages, the SQLite native library, the Lucide-derived icons and the
test-only packages are listed in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## FFmpeg (LGPL v3) — the V0.5 addition

### What is downloaded

`tools/fetch-ffmpeg.ps1` downloads **BtbN/FFmpeg-Builds** release asset `latest` →
`ffmpeg-master-latest-win64-lgpl-shared.zip` (**75.8 MB**) and extracts **only the runtime files**
into `third_party/ffmpeg/` (gitignored) plus `LICENSE.txt`:

- `bin/ffmpeg.exe`, `bin/ffprobe.exe`;
- the shared libraries the CLIs link: `avutil-61`, `avcodec-63`, `avformat-63`, `avdevice-63`,
  `avfilter-12`, `swresample-7`, `swscale-10`;
- `LICENSE.txt`.

Provisioned size: **153.7 MB** (`avcodec-63.dll` 87.65 MB, `avfilter-12.dll` 30.2 MB,
`avformat-63.dll` 24.52 MB, `avdevice` 4.72 MB, `avutil` 2.9 MB, `swscale` 2.28 MB, `swresample`
0.71 MB, `ffmpeg.exe` 0.53 MB, `ffprobe.exe` 0.22 MB).

### Why this is LGPL, not GPL (verified, not assumed)

`tools/fetch-ffmpeg.ps1` records the pin and the verification in one place — this is the **source of
truth** for the version and the config:

- **Pinned URL:** `https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip`
- **Pinned SHA-256:** `85e26d3d77c17393e56e49132fda0905a3ced925942e1d8d7d2ebe0a28d58a55`
  (the script computes `Get-FileHash` and **refuses to continue on mismatch**).
- **Config check:** after extracting, the script runs `ffmpeg -version` and **throws** if the config
  contains `--enable-gpl` or `--enable-nonfree`; it then prints the version line and the license head.

Installed version (from the script's `ffmpeg -version`):

```
ffmpeg version N-127259-gb91a82d6dd-20261009
  build: Latest Auto-Build (2026-10-09 14:16)
  configuration: ... --enable-version3 --enable-shared ...   (no --enable-gpl, no --enable-nonfree)
```

Because the configuration contains **`--enable-version3 --enable-shared`** and **no `--enable-gpl` /
`--enable-nonfree`**, the build is **LGPL v3**.

### Why it is redistributable as a "separate program"

字幕君 does **not** link FFmpeg. It ships `ffmpeg.exe` / `ffprobe.exe` and their shared DLLs
**alongside** the application and runs them as **child processes** through
`ProcessStartInfo.ArgumentList` (see `LocalMeetingSubtitle.Media/ProcessRunner.cs`). That is exactly
the "separate program" case the LGPL permits: a work that merely *invokes* an LGPL program is not a
derivative of it. The obligations this leaves are the LGPL's own — **keep the license with the
binaries** (shipped as `LICENSE.txt`) and **make the corresponding source available** (the build is a
public BtbN/FFmpeg-Builds artifact; FFmpeg source is publicly available). The app's own license (MIT)
is unaffected.

### Known limitation: no H.264 *encoding*

This LGPL build has **no libx264** (x264 is GPL). Therefore **H.264 encoding is unavailable**. This is
**harmless** for the product: 字幕君 only ever **decodes** media, and H.264 **decoding** is supported
by the native FFmpeg decoders. The build also has OpenCL / AMF / nvenc hooks compiled in (unused;
decode is CPU-only), and the usual encoders (libmp3lame, aac, libvorbis, libopus) for the test media.

## Diarization models (reused from V0.4)

| Model | License | Notes |
| --- | --- | --- |
| `pyannote-segmentation-3-0` (`model.onnx`) | **MIT** (pyannote / CNRS) | speaker segmentation |
| `3dspeaker-eres2net-base-zh-16k` | **Apache-2.0** (3D-Speaker / ModelScope) | speaker embedding; SHA-256 pinned in the catalog and in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) |

Both are downloaded on demand by `ModelManager`; they are **not** bundled with the source.

## Project license

字幕君 itself is **MIT** (root `LICENSE`). Third-party components remain governed by their own
licenses. See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the pre-existing list.
