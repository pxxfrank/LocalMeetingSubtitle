# Model Selection

Status: **暂定 / tentative** — the chosen model is only provisional until it is benchmarked on the
target hardware (Windows 11 + Intel Core Ultra 7 155H). Only candidate **A** has been downloaded and
benchmarked; **B** and **C** are declared in the catalog but were **NOT** downloaded or benchmarked.

## Candidates

| ID | Candidate | Kind | License | Approx. size | Status |
| --- | --- | --- | --- | --- | --- |
| **A** | `csukuangfj/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23` | Streaming transducer | Apache-2.0 | ~24.5 MB on disk (INT8 encoder/decoder/joiner + tokens.txt) | **Downloaded & benchmarked** |
| **B** | `csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20` | Streaming transducer | Apache-2.0 | ~330 MB (declared) | Declared, **NOT downloaded / NOT benchmarked** → tentative |
| **C** | `csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17` | Offline (SenseVoice) | Apache-2.0 | ~250 MB (declared) | Declared, **NOT downloaded / NOT benchmarked** → tentative |

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
- Candidate C (SenseVoice, offline) exposes hotwords through the offline recognizer but uses
  `greedy_search`; its hotword behaviour was not benchmarked.

## Decision

Candidate **A** is selected as the default for the release candidate because it is small (~24.5 MB),
Apache-2.0, streaming (native partials + endpointing), and hotword-capable via
`modified_beam_search`, and it decoded Chinese correctly on the sample wavs.

This selection is **暂定 / tentative**: candidate B (bilingual zh-en) may be a better fit for mixed
Chinese/English technical meetings, and candidate C (SenseVoice) may offer higher accuracy at higher
latency. **Both must be downloaded and benchmarked on the target hardware before the model choice is
finalised.** See [`PERFORMANCE.md`](PERFORMANCE.md) and [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md).

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
