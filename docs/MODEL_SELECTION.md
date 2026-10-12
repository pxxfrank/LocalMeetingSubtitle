# Model Selection

Status: **暂定 / tentative** — the chosen default is only provisional until it is benchmarked on the
target hardware (Windows 11 + Intel Core Ultra 7 155H). All three candidates have now been downloaded
and decoding-benchmarked on the **dev host**; **none** has been benchmarked on the target. The two
streaming candidates are **user-selectable** at runtime in `设置 → 识别 / Settings → Recognition`:
**A** stays the default, and **B** is offered as an optional 中英 (zh-en) model that is **downloaded on
demand** (it is not bundled in the installer).

## Candidates

| ID | Candidate | Kind | License | Approx. size | Status |
| --- | --- | --- | --- | --- | --- |
| **A** | `csukuangfj/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23` | Streaming transducer | Apache-2.0 | ~24.5 MB on disk (INT8 encoder/decoder/joiner + tokens.txt) | **Downloaded & benchmarked** |
| **B** | `csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20` | Streaming transducer | Apache-2.0 | **198,270,793 B** on disk (INT8 encoder/decoder/joiner + `tokens.txt`) | **Downloaded & benchmarked (dev host)** — selectable in Settings as the 中英 model; **no model-level hotwords** (measured ineffective — see below), so its descriptor sets `SupportsHotwords = false` |
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
- **Candidate B (bilingual zh-en) does NOT support model-level hotwords in practice** — measured:
  byte-identical output at hotwords score 1.5 / 4.0 / 20 while the same file measurably steered A.
  Its descriptor therefore sets `SupportsHotwords = false`; see
  [Candidate B](#candidate-b--bilingual-zh-en-measured-on-the-dev-host).
- **Candidate C (SenseVoice, offline) does NOT support model-level hotwords** in sherpa-onnx 1.13.8 —
  **VERIFIED** by A/B with `tools/AsrBenchmark --offline --model-id sense-voice-small-int8`:
  - with `--hotwords` and default decoding the native layer refuses to build the recognizer:
    `offline-recognizer.cc:Validate:88 Please use --decoding-method=modified_beam_search if you provide --hotwords-file. Given --decoding-method='greedy_search'`;
  - with `--decoding modified_beam_search` it **also** refuses:
    `offline-recognizer-sense-voice-impl.h:82 Only greedy_search is supported at present. Given modified_beam_search`.
  So domain terms for the High-accuracy mode must go through `TextCorrectionEngine` (correction rules),
  **not** model-level hotwords. (This supersedes the earlier "not benchmarked" note about candidate C.)

## Candidate B — bilingual zh-en (measured on the dev host)

Downloaded with `tools/ModelManager install --id streaming-zipformer-bilingual-zh-en` —
**198,270,793 B** on disk (encoder 181,895,032 + decoder 13,091,040 + joiner 3,228,404 + tokens
56,317; all sizes are pinned in the catalog so download progress and installed-file verification are
real rather than decorative).

**Accuracy — a large, unambiguous win for English and for code-switching.** `tools/AsrBenchmark
--threads 4`, greedy decoding, identical audio:

| Audio | A (zh-14M) | B (bilingual) |
| --- | --- | --- |
| English TTS, 16 kHz | `在国儿地位上字儿` | `THE QUARTERLY REVIEW MEETING IS ON THURSDAY AT THREE OCLOCK IN THE AFTERNOON` |
| B's `test_wavs/0.wav` | `昨天是妈呗 她等意八 他们是星期三` | `昨天是 MONDAY TODAY IS LIBR THE DAY AFTER TOMORROW是星期三` |
| B's `test_wavs/2.wav` | `是平凡呢真是接下来富裕困地频繁` | `这是不是平凡的啊不认识你记下来 FREQUENTLY频繁的` |
| B's `test_wavs/3.wav` | `一只手有什么时代加了意思是一般现 在街上比` | `第一句是个什么时态在加了 ES是一般现在时对吧…` |

**Chinese is *not* strictly better.** On clean synthetic and 8 kHz audio B repeats characters that A
renders cleanly (`升升藤`, `教教`, `说说说`, `正正在`, `上上`) and on `test_wavs/8k.wav` it dropped the
opening clause; greedy decoding measured at least as stable as `modified_beam_search` on the same
material. The claim *"accuracy does not decrease"* therefore **cannot** be made for pure-Chinese
meetings — that is why A stays the default and the choice must still be A/B'd on real meeting
recordings.

**Cost** (dev host, 4 threads): disk 198.3 MB vs 24.5 MB; load ≈ 2.9–3.4 s vs 1.6–1.9 s; working set
≈ 280 MB vs ≈ 100 MB; RTF ≈ 0.09–0.23 vs 0.04–0.09 (still well below real time).

**Model-level hotwords do NOT work on B — VERIFIED.** With an identical `modified_beam_search`
configuration and the same hotwords file, A is measurably steered while B is not:

| Audio (TTS) | Model | no hotwords | hotwords @1.5 | hotwords @4.0 |
| --- | --- | --- | --- | --- |
| `昇腾训练集群部署在鲲鹏服务器上` | A | `生疼训练集群部署在昆鹏鹏服务器上` | same | `生腾训练集群部署在昆仑服务器上` |
| same | B | `升藤愉训练级集群部署在鲲鲲鹏鹏鹏鹏服务器上上` | **identical** | only more repetition |
| `CANN是昇腾的异构计算架构` | A | `是生疼的异构计算架构` | `是升腾的异构计算架构` | `是升腾的异构计算架构` |
| same | B | `是升升藤LY易易易购计算架架构` | **identical** | **identical** (also identical at score 20) |

The context graph *is* loaded (`hotwords=on` reports in CAPABILITIES, and score 4.0 perturbs the
output), and deleting `bpe.model`/`bpe.vocab` from the model directory changed nothing — so this is a
property of the model, not of the file layout. Two structural facts behind it:

- B's `tokens.txt` (6,257 entries) contains **no whole-word English tokens**: English is BPE subwords
  (`▁TH`, `DAY`, `ING`; `CANN`, `IEEE`, `TENSOR`, `▁FRI` are all **missing**). Chinese is
  single-character tokens (`昇` 5846, `腾` 1481, `鲲` 5537, `鹏` 2477 — all present).
- Consequence: **ASCII/English domain terms cannot be hotword-boosted on *any* model**, because
  `ModelHotwordFile` emits a Latin run as one glued token.

So candidate B's descriptor sets `SupportsHotwords = false`, which makes
`DefaultHotwordService.Mode == HotwordMode.NotSupported`, `WriteModelHotwordFile` return `null`, and
the engine stay on `greedy_search`. Domain terminology on B flows through the **deterministic
correction layer** (`TextCorrectionEngine` + `BuiltInLexicon.Rules`, e.g. `升腾 → 昇腾`,
`昆鹏 → 鲲鹏`), which is model-independent — and is equally the only mechanism that can fix acronyms
such as `CANN` on candidate A.

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

Candidate **A** remains the **default** because it is small (~24.5 MB), Apache-2.0, streaming (native
partials + endpointing), hotword-capable via `modified_beam_search`, and decodes Chinese correctly.
It stays the right choice for pure-Chinese meetings.

Candidate **B** is now offered as an **optional, on-demand 中英 (zh-en) model**, selectable at runtime
in `设置 → 识别 / Settings → Recognition`. It is a clear win whenever English or zh-en code-switching
is spoken, but it must **not** become the default: its Chinese repeats characters on some material,
it costs ~8× the disk and ~3× the working set, and model-level hotwords do not work on it (so
terminology there relies on the correction rules). It is **not** bundled in the installer — a ~198 MB
optional download would nearly double the artifact — so it is fetched in-app on demand.

Candidate **C** (SenseVoice) stays reachable only through the V0.5 file-transcription High-accuracy
mode, because the live offline path still emits zero-duration segments.

This selection is **暂定 / tentative**: none of the candidates has been benchmarked on the target
hardware, and the zh-en choice must be validated on real meeting recordings before it is trusted. See
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
