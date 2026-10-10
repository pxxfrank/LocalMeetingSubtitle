# Model Selection

Status: **暂定 / tentative** — the chosen model is only provisional until it is benchmarked on the
target hardware (Windows 11 + Intel Core Ultra 7 155H). Candidates **A** and **C** have been downloaded
(C in V0.5 Phase 2, for the High-accuracy file-transcription mode) and decoding-benchmarked on the dev
host; **B** is declared in the catalog but was **NOT** downloaded or benchmarked. **None** has been
benchmarked on the target.

## Candidates

| ID | Candidate | Kind | License | Approx. size | Status |
| --- | --- | --- | --- | --- | --- |
| **A** | `csukuangfj/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23` | Streaming transducer | Apache-2.0 | ~24.5 MB on disk (INT8 encoder/decoder/joiner + tokens.txt) | **Downloaded & benchmarked** |
| **B** | `csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20` | Streaming transducer | Apache-2.0 | ~330 MB (declared) | Declared, **NOT downloaded / NOT benchmarked** → tentative |
| **C** | `csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17` | Offline (SenseVoice, INT8) | Apache-2.0 | ~250 MB (declared) | **Downloaded (V0.5 Phase 2)** — `sense-voice-small-int8`: `model.int8.onnx` **239,233,841 B** + `tokens.txt` (dir `models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17`; **sha256 not recorded**). Decoding **benchmarked** (dev host); hotword support **verified unsupported** (see below); accuracy not benchmarked → tentative |

The three candidates are declared in code in `src/LocalMeetingSubtitle.Asr/AsrModelCatalog.cs`
(`StreamingZipformerZh14M`, `StreamingZipformerBilingualZhEn`, `SenseVoiceSmall`). Candidate A is
the default (`AsrBenchmark`/`OfflineVerification` default `--model-id streaming-zipformer-zh-14M`).

## Verification log (Hugging Face API)

The three source repositories and their licenses were checked against the Hugging Face API:

| Repository | License (HF API) | Result |
| --- | --- | --- |
| `csukuangfj/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23` | `apache-2.0` | verified |
| `csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20` | `apache-2.0` | verified |
| `csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17` | `apache-2.0` | verified |

Candidate A int8 files (`encoder-epoch-99-avg-1.int8.onnx` + `decoder-epoch-99-avg-1.int8.onnx` +
`joiner-epoch-99-avg-1.int8.onnx` + `tokens.txt`) were downloaded and total **24.5 MB** on disk.

> The license strings above are those exposed by the Hugging Face API at verification time.

## Benchmark (candidate A — streaming-zipformer-zh-14M INT8)

Executed with `tools/AsrBenchmark`, `--threads 4`. Numbers are reported **verbatim** from the run.

| WAV | Load (ms) | Audio (s) | Inference (s) | RTF | Working set | Decoded text |
| --- | --- | --- | --- | --- | --- | --- |
| `test_wavs/0.wav` | 2741 | 5.612 | 0.262 | **0.0466** | 100 MB | `对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢` |
| `test_wavs/1.wav` | — | — | — | **0.0492** | — | `重点呢想谈三个问题首先呢就是这一轮全球金融动量的表现` |

Notes:

- RTF = inference seconds / audio seconds. Both runs are well below the ≤ 0.5 target **on the dev
  host** (a Xeon Gold 6230N, 36 cores/36 threads — far faster than the target laptop).
- **Target RTF / CPU / latency are ESTIMATES, not measured.** No target-hardware benchmark was run.

## Hotword compatibility

- Model-level hotwords require a **streaming transducer** model plus `modified_beam_search` decoding.
  Candidate A satisfies this; the recognizer switches automatically when a hotwords file is present
  (`SherpaOnnxAsrEngine.BuildOnline`: `config.DecodingMethod = hotwords ? "modified_beam_search" : …`,
  plus `HotwordsFile` / `HotwordsScore`).
- **Verified:** with a hotwords file supplied, the hypothesis changed and the engine CAPABILITIES
  string reported `decoding=modified_beam_search, hotwords=on`.
- **NOT_TESTED:** the *accuracy improvement* from hotwords was **not measured** (no targeted speech
  recording was available).
- **Candidate C (SenseVoice, offline) does NOT support model-level hotwords** in sherpa-onnx 1.13.8 —
  **VERIFIED** by A/B with `tools/AsrBenchmark --offline --model-id sense-voice-small-int8`:
  - with `--hotwords` and default decoding the native layer refuses to build the recognizer:
    `offline-recognizer.cc:Validate:88 Please use --decoding-method=modified_beam_search if you provide --hotwords-file. Given --decoding-method='greedy_search'`;
  - with `--decoding modified_beam_search` it **also** refuses:
    `offline-recognizer-sense-voice-impl.h:82 Only greedy_search is supported at present. Given modified_beam_search`.
  So domain terms for the High-accuracy mode must go through `TextCorrectionEngine` (correction rules),
  **not** model-level hotwords. (This supersedes the earlier "not benchmarked" note about candidate C.)

## Three transcription modes (V0.5 file transcription)

`src/LocalMeetingSubtitle.Asr/TranscriptionModeCatalog.cs` maps each of the three file-transcription
modes to a **real, distinct configuration** (model + decoding + segmentation). Each resolves to a
`ResolvedTranscriptionMode` carrying `IsAvailable` / `UnavailableReason`, so a mode whose model is not
installed **fails soft** rather than throwing.

| Mode | Label | Model | Decoding | Model-level hotwords | Segmentation |
| --- | --- | --- | --- | --- | --- |
| `Fast` | 快速 | `streaming-zipformer-zh-14M` (candidate A) | `greedy_search` | off | VAD, max 15 s, overlap 0, appends `。` |
| `Standard` | 标准 | `streaming-zipformer-zh-14M` (candidate A) | `modified_beam_search` | on (hotwords file) | VAD, max 20 s, overlap 0, appends `。` |
| `HighAccuracy` | 高精度 | `sense-voice-small-int8` (candidate C, offline) | `greedy_search` (only option) | **not supported** | VAD, max 30 s, **overlap 1.5 s** + dedup, ITN punctuation |

- **Fast vs. Standard** differ only in decoding (`greedy_search` vs. `modified_beam_search`, plus the
  segment cap); on the same 5.612 s clip the **output differs**, confirming beam search is active.
- **High accuracy** is the only mode with **overlap** (1.5 s) — so the overlap de-duplicator runs — and
  the only one that emits **punctuation** (SenseVoice ITN), so it does not append a terminal `。`.

## Decision

Candidate **A** is selected as the default for the release candidate because it is small (~24.5 MB),
Apache-2.0, streaming (native partials + endpointing), and hotword-capable via
`modified_beam_search`, and it decoded Chinese correctly on the sample wavs.

This selection is **暂定 / tentative**: candidate B (bilingual zh-en) may be a better fit for mixed
Chinese/English technical meetings, and candidate C (SenseVoice) may offer higher accuracy at higher
latency. Candidate C has now been downloaded and its decoding benchmarked on the dev host (V0.5
Phase 2), but it is **still not benchmarked on the target hardware**; candidate B is still
undownloaded. **All must be benchmarked on the target before the model choice is finalised.** See
[`PERFORMANCE.md`](PERFORMANCE.md) and [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md).

## Diarization models (V0.4.0)

Post-meeting offline speaker diarization (V0.4.0) uses **two additional models** — a pyannote
segmentation model and a 3D-Speaker voiceprint embedding model. Neither is an ASR model, and neither
is a candidate for the live recognizer. They are declared in a separate catalog
(`src/LocalMeetingSubtitle.Asr/DiarizationModelCatalog.cs`) so a diarization descriptor can never be
selected as the recognizer.

| ID | Model | Kind | License | Size | Source | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `pyannote-segmentation-3-0` | `csukuangfj/sherpa-onnx-pyannote-segmentation-3-0` (`model.onnx`) | Speaker segmentation | MIT (pyannote / CNRS) | 5 992 913 bytes (~5.72 MB) | Hugging Face | **Downloaded & verified** |
| `3dspeaker-eres2net-base-zh-16k` | `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx` | Speaker embedding (voiceprint) | Apache-2.0 (3D-Speaker / ModelScope) | 39 593 761 bytes (~37.76 MB) | `k2-fsa/sherpa-onnx` release `speaker-recongition-models` | **Downloaded & verified** |

Exact source URLs:

- segmentation: `https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/main/model.onnx`
- embedding: `https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx`

The embedding model's size and **SHA-256 were verified against the downloaded file**:
`1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b`. The SHA-256 is pinned in the
catalog (`DiarizationModelCatalog.BuildEmbedding`), so `ModelManager` rejects a mismatched download.

Both models are downloaded on demand by `ModelManager` and are **not** bundled in the publish yet
(see [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) P3-9). No target-hardware benchmark of either model has
been run.
