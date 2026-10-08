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
