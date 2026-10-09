# Third-Party Notices

字幕君 bundles and/or depends on the following third-party components. Each entry
lists the component, the version used, its license, and where it is used.

| Component | Version | License | Used for |
| --- | --- | --- | --- |
| **sherpa-onnx** (`org.k2fsa.sherpa.onnx`) | 1.13.8 | Apache-2.0 | Offline speech recognition engine (streaming transducer + offline models); ships the native `sherpa-onnx-c-api.dll` |
| **ONNX Runtime** (`onnxruntime.dll`, distributed with sherpa-onnx) | 1.17.x (bundled) | MIT | Native inference backend used by sherpa-onnx |
| **NAudio** | 2.2.1 | MIT | WASAPI loopback audio capture |
| **Microsoft.Data.Sqlite** | 8.0.31 | MIT | Embedded database provider |
| **SQLite** (native library bundled with Microsoft.Data.Sqlite) | bundled | Public Domain | Database engine (WAL mode) |
| **Microsoft.Extensions.DependencyInjection** | 8.0.1 | MIT | Dependency-injection container in the App |
| **System.Diagnostics.PerformanceCounter** | 8.0.1 | MIT | CPU performance-counter sampling |
| **.NET 8 / .NET Runtime** | 8.0.x | MIT | Runtime / WPF platform |
| **Lucide icon paths** (derived) | — | MIT | The thin line-icon geometry set in `src/LocalMeetingSubtitle.App/Theme/Icons.xaml` is derived from the Lucide icon set. The Claude-style theme itself is original and uses **no third-party UI framework**. |
| **xunit** + `xunit.runner.visualstudio` | 2.5.3 | Apache-2.0 | Test framework (test projects only) |
| **Microsoft.NET.Test.Sdk** | 17.8.0 | MIT | Test host (test projects only) |
| **coverlet.collector** | 6.0.0 | MIT | Code-coverage collector (test projects only) |

## Models

The ASR models are **not** bundled with the source; they are downloaded on demand by `ModelManager`
from Hugging Face. All models in the catalog are licensed **Apache-2.0**:

| Model | Repository | License |
| --- | --- | --- |
| Streaming Zipformer 中文 14M (INT8) | `csukuangfj/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23` | Apache-2.0 |
| Streaming Zipformer 中英双语 (INT8) | `csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20` | Apache-2.0 |
| SenseVoice Small (INT8, offline) | `csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17` | Apache-2.0 (FunAudioLLM/SenseVoice; sherpa-onnx conversion) |

Licenses were verified against the Hugging Face API; see [`MODEL_SELECTION.md`](MODEL_SELECTION.md).

### Diarization models (V0.4.0)

Offline speaker diarization uses two further models. They are **not** bundled with the source and are
downloaded on demand by `ModelManager`; **neither is an ASR model**. They are declared separately in
`src/LocalMeetingSubtitle.Asr/DiarizationModelCatalog.cs`:

| Model | File | Size | License | Source URL |
| --- | --- | --- | --- | --- |
| `pyannote-segmentation-3-0` (speaker segmentation) | `model.onnx` | 5 992 913 bytes (~5.72 MB) | MIT (pyannote / CNRS) | `https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/main/model.onnx` |
| `3dspeaker-eres2net-base-zh-16k` (speaker embedding) | `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx` | 39 593 761 bytes (~37.76 MB) | Apache-2.0 (3D-Speaker / ModelScope) | `https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx` |

The embedding model's SHA-256 is pinned as
`1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b` (verified against the
download). See [`MODEL_SELECTION.md`](MODEL_SELECTION.md) for the verification log.

## Project license

字幕君 itself is released under the **MIT License** (see the root `LICENSE` file).
The third-party components listed above remain governed by their own licenses.
