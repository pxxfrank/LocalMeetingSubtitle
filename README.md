# LocalMeetingSubtitle 本地会议字幕

> **状态 / Status: 候选发布版本 — 待实机验收 / Release candidate — pending hardware acceptance**
>
> 在开发主机（Windows 10 Pro 19045，Xeon Gold 6230N，64 GB）上构建、单元/集成/性能测试、真实模型解码、WASAPI 采集与离线校验均已执行并通过；**所有依赖真实目标硬件（Windows 11 + Intel Core Ultra 7 155H + Intel Arc + AI Boost NPU）的验收项均未执行（BLOCKED / NOT_TESTED）**。详见 [`docs/RELEASE_CHECKLIST.md`](docs/RELEASE_CHECKLIST.md)。

> **候选发布 ≠ 已验收。** 本版本可交付评审与实机试用，但在目标笔记本上完成验收前，不应宣称已满足产品指标（延迟、CPU、内存、3 小时稳定性）。

---

## 这是什么 / What it is

LocalMeetingSubtitle 是一个**完全离线、纯 CPU** 的 Windows 桌面会议实时字幕工具：

- 通过 **WASAPI loopback** 捕获系统播放（扬声器/耳机）声音，无需麦克风、无需虚拟声卡。
- 使用 **sherpa-onnx**（流式 zipformer / 离线 SenseVoice）在本地进行语音识别，输出中文实时字幕。
- 字幕**只保存在本机**（`%LOCALAPPDATA%\LocalMeetingSubtitle\`），提供置顶悬浮字幕、搜索、热词、文本纠正与 TXT/SRT/Markdown 导出。
- 运行期**不发起任何网络请求**：唯一引用 `System.Net.Http` 的 `ModelDownloads` 程序集与识别链路隔离，可静态验证（见“离线保证”）。

技术栈：.NET 8 / WPF（`net8.0-windows`，x64，**Claude 风格极简界面**：自绘主题 + 线性图标，支持**明/暗主题实时切换**，不依赖任何第三方 UI 框架）、NAudio 2.2.1、sherpa-onnx 1.13.8、Microsoft.Data.Sqlite 8.0.31（WAL）。

## 快速开始 / Quick start

> 以下命令为 **Windows PowerShell 5.1** 语法（用 `;` 连接，不要用 `&&`）。

```powershell
# 0) 若 .NET 8 SDK 未加入 PATH（开发主机装于 %USERPROFILE%\.dotnet）：
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"

# 1) 构建
dotnet build LocalMeetingSubtitle.sln -c Release

# 2) 测试
dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug
dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "Category=Performance"

# 3) 安装模型到应用数据目录（应用从 %LOCALAPPDATA% 读取模型）
dotnet run --project tools/ModelManager -- install --id streaming-zipformer-zh-14M `
    --models-root "$env:LOCALAPPDATA\LocalMeetingSubtitle\models"

# 4) 运行（开发态）
dotnet run --project src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj -c Debug

# 5) 生成自包含发布产物
dotnet publish src/LocalMeetingSubtitle.App/LocalMeetingSubtitle.App.csproj `
    -c Release -r win-x64 --self-contained true -o dist/LocalMeetingSubtitle-win-x64
```

首次启动后：选择音频设备 → 点击 **开始 / Start** → 播放任意会议声音即可看到实时字幕。
发布包运行：解压 `dist/LocalMeetingSubtitle-win-x64`，双击 `LocalMeetingSubtitle.exe`。

## 项目结构 / Project layout

```
13-meetingsubtitle/
├─ src/                          # 8 个产品工程（7 个 + ModelDownloads）
│  ├─ LocalMeetingSubtitle.App         # WPF UI、DI 组合根、托盘、悬浮字幕（可执行）
│  ├─ LocalMeetingSubtitle.Core        # 契约(接口)+模型、音频数学、流水线、热词/纠正
│  ├─ LocalMeetingSubtitle.Audio       # NAudio WASAPI 回环采集
│  ├─ LocalMeetingSubtitle.Asr         # sherpa-onnx 引擎 + 模型目录 + 原生库探测
│  ├─ LocalMeetingSubtitle.Storage     # SQLite(Microsoft.Data.Sqlite, WAL) 仓储
│  ├─ LocalMeetingSubtitle.Export      # TXT / SRT / Markdown 导出
│  ├─ LocalMeetingSubtitle.Diagnostics # CPU/内存/流水线性能监控
│  └─ LocalMeetingSubtitle.ModelDownloads # 唯一引用 System.Net.Http 的程序集（隔离）
├─ tests/                        # 3 个测试工程（xunit 2.5.3）
│  ├─ LocalMeetingSubtitle.UnitTests
│  ├─ LocalMeetingSubtitle.IntegrationTests
│  └─ LocalMeetingSubtitle.PerformanceTests
├─ tools/                        # 4 个开发/安装期工具
│  ├─ ModelManager               # 列出/安装/校验模型
│  ├─ AudioCaptureProbe          # WASAPI 回环探测与录音
│  ├─ AsrBenchmark               # 真实模型解码计时/RTF/内存
│  └─ OfflineVerification        # 离线能力 5 项校验
├─ docs/                         # 本套文档（见下）
├─ models/                       # 开发期下载的模型（不入库）
└─ dist/                         # 发布产物（不入库）
```

## 文档索引 / Documentation

| 文档 | 语言 | 内容 |
| --- | --- | --- |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | EN | 模块划分、数据流、线程模型、SQLite 表结构、接口清单、`ModelDownloads` 隔离原因 |
| [`docs/MODEL_SELECTION.md`](docs/MODEL_SELECTION.md) | EN | 模型候选 A/B/C、HF 校验日志、基准结果、热词兼容性（暂定） |
| [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md) | EN | 测试分类、命令、`--filter` 用法、`ThreeHourSoak` 手动命令、最近一次结果 |
| [`docs/PERFORMANCE.md`](docs/PERFORMANCE.md) | EN | 实测数据、方法学、目标 vs 实际、目标机未测量的说明 |
| [`docs/RELEASE_CHECKLIST.md`](docs/RELEASE_CHECKLIST.md) | EN | AC-01..AC-20 验收矩阵与状态 |
| [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md) | EN | 已修复缺陷(P1/P2)、未决限制(P3)、阻塞项 |
| [`docs/CODEX_PROGRESS.md`](docs/CODEX_PROGRESS.md) | EN | 阶段进度、构建/测试结果、后续任务、阻塞项、重要命令 |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | EN | ADR 风格架构决策 D1–D10 |
| [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) | EN | 开发环境搭建、构建/测试/发布命令、如何添加模型 |
| [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md) | **中文** | 面向用户的使用与排查指南 |
| [`LICENSE`](LICENSE) | — | 项目许可证（MIT） |
| [`docs/THIRD-PARTY-NOTICES.md`](docs/THIRD-PARTY-NOTICES.md) | EN | 第三方组件与模型许可说明 |
| [`docs/SHERPA_CSHARP_API_DUMP.txt`](docs/SHERPA_CSHARP_API_DUMP.txt) | — | sherpa-onnx C# 程序集反射导出的原始 API 清单 |

## 离线保证 / Offline guarantee

- 运行期识别链路（`Core` / `Asr` / `Audio` / `Storage` 程序集）**不引用** `System.Net.Http` 或任何 `System.Net.*`；网络能力被隔离在 `ModelDownloads` 程序集中，仅用于开发/安装期下载模型。
- `tools/OfflineVerification` 校验 **5/5 PASS**：原生库加载、无网络引用、数据目录位于 `%LOCALAPPDATA%`、模型文件存在、离线解码产出文本。
- 用户数据（数据库、日志、模型、导出）全部写入 `%LOCALAPPDATA%\LocalMeetingSubtitle\`，不写 Program Files。

## 三个必须诚实说明的限制 / Honest caveats

1. **未在目标硬件上验收。** 开发主机（Xeon 6230N）远快于目标笔记本；目标机的 RTF / CPU / 延迟均为**估计值，非实测**。
2. **UI 端到端实时链路未在本机走通。** 无真实会议播放音频，故“开始→转写→落库”的完整 UI 路径为 BLOCKED（各分层已分别测试；发布 ZIP 已内置模型并被自动识别）。
3. **无真实会议 3 小时稳定性运行。** `ThreeHourSoak` 从未执行。
4. **安装包为自签名**（`LocalMeetingSubtitle-Setup.exe` / `Setup.msi`，未获 CA 证书，SmartScreen 仍会警告）；未制作/签名受信任的正式安装包。

更多细节见 [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md) 与 [`docs/RELEASE_CHECKLIST.md`](docs/RELEASE_CHECKLIST.md)。
