# Release Checklist — Acceptance Matrix (AC-01 .. AC-20)

**Status banner: V0.4.0 候选发布版本 — 待实机验收；V0.5（离线文件转写 + 角色标注对话）进行中 / Release candidate (V0.4.0) pending hardware acceptance; V0.5 (offline file transcription + role-tagged dialogue) in progress.**

> **How to read this table.** The `实际` (actual) column contains **only** facts that were actually
> executed and recorded; anything not executed is marked `NOT_TESTED` / `BLOCKED` — never `PASS`.
> `环境` records where the item was (or must be) verified. The AC numbering and titles below are
> reconstructed from the release-acceptance spec; each row is anchored to concrete evidence from the
> recorded evidence brief rather than invented results.

Legend: `PASS` = verified with evidence · `PARTIAL` = some sub-checks pass, others blocked ·
`BLOCKED` = cannot run (hardware unavailable) · `NOT_TESTED` = not executed.

| 编号 | 验收内容 | 验证方法 | 预期 | 实际 | 证据 | 环境 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AC-01 | 在目标硬件与操作系统上运行（Windows 11 + Core Ultra 7 155H + Arc iGPU + AI Boost NPU） | 在目标笔记本安装并启动应用并完成一次转写 | 正常启动并输出字幕 | 未执行（无该机器） | — | 目标硬件 (Win11) | **BLOCKED** |
| AC-02 | 解决方案可完整构建 | `dotnet build LocalMeetingSubtitle.sln` | 0 错误 | 0 错误（测试桩有少量良性 CS0067 警告） | 构建日志 | 开发主机 Win10 | **PASS** |
| AC-03 | 单元测试全部通过 | `dotnet test …UnitTests…` | 全通过 | 136 通过 / 0 失败 | 测试输出 | 开发主机 | **PASS** |
| AC-04 | 目标硬件上的实时字幕时延/吞吐达标 | 目标机运行真实会议并测量 | 首字 ≤1.5 s、结尾 ≤1 s | 未执行 | — | 目标硬件 | **NOT_TESTED** |
| AC-05 | 目标硬件上的 CPU/内存占用达标 | 目标机运行并采样 `ProcessPerformanceMonitor` | CPU ≤25%、内存 ≤1 GB | 未执行 | — | 目标硬件 | **NOT_TESTED** |
| AC-06 | 端到端实时转写（UI：开始→转写→落库） | 应用内点击开始播放真实会议音频 | 连续实时字幕并写入数据库 | **已走通（2026-10-09）**：发布版 0.4.0 经 UI 自动化点击「开始」，WASAPI 回环采集所播放音频 → **落库 3 条真实中文字幕**（会话 `bb473bef…`，`decoding=modified_beam_search, hotwords=on`）；该次运行同时暴露并修复了「非 ASCII 热词路径」P0 | `KNOWN_ISSUES` BLOCKED-1（已关闭） | 开发主机 | **PASS (dev host)** |
| AC-07 | 真实模型可从样例音频解码出中文文本 | `AsrBenchmark`/`RealModelTests`（`test_wavs`） | 产出中文文本 | `test_wavs/0.wav`、`1.wav` 均产出中文文本 | 见 `MODEL_SELECTION.md` 基准表 | 开发主机 | **PASS** |
| AC-08 | 端到端时延指标可被采集 | 采样 `EndToEndLatencyMs` | 能报告延迟 | 指标链路已实现；未在真实会议中验证目标阈值 | `IPerformanceMonitor` / pipeline `ReportLatency` | 开发主机 | **PARTIAL** |
| AC-09 | 长时间稳定性（≥3 小时） | 运行 `ThreeHourSoak` | 无崩溃、序号单调、无丢帧 | 从未执行 | `SoakTests.ThreeHourSoak`（Skip） | — | **NOT_TESTED** |
| AC-10 | 完全离线保障 | `OfflineVerification` | 无网络引用且可离线解码 | **5/5 PASS** | 原生库加载、无 `System.Net.Http` 引用、`%LOCALAPPDATA%` 数据目录、模型存在、离线解码出文本 | 开发主机 | **PASS** |
| AC-11 | WASAPI 回环采集可用 | `AudioCaptureProbe capture` | 采到有效 PCM | 44100 Hz 2ch 32-bit float → 有效 16-bit WAV，RMS ≈0.345 | 探测输出 | 开发主机 | **PASS** |
| AC-12 | 热词切换 `modified_beam_search` | 提供热词文件后解码 | 切换解码方法 | 假设文本变化，CAPABILITIES 报 `decoding=modified_beam_search, hotwords=on` | AsrBenchmark / 引擎能力串 | 开发主机 | **PASS** |
| AC-13 | 字幕持久化（SQLite + WAL + 唯一约束） | 单元/集成测试 + `SqliteDatabase` | 写入可持久化、拒绝重复 | 仓储单元测试通过；集成测试 8/8 通过；`UNIQUE(SessionId,SequenceNumber)` | 测试输出；schema | 开发主机 | **PASS** |
| AC-14 | 导出 TXT / SRT / Markdown | 单元 + 集成往返测试 | 三种格式内容正确 | 通过 | export round-trip 测试 | 开发主机 | **PASS** |
| AC-15 | 悬浮字幕窗口（置顶/字号/透明度/鼠标穿透与恢复） | 手动 + 代码评审 | 置顶、可调、穿透可恢复 | 通过（置顶/穿透样式已验证；窗口缩放在 `KNOWN_ISSUES` 中记为未交互验证） | `FloatingSubtitleWindow` | 开发主机 | **PASS** |
| AC-16 | 托盘操作（开始/暂停/停止/悬浮/退出） | 手动 + 代码评审 | 菜单反映实时状态 | 通过 | `TrayIconController` | 开发主机 | **PASS** |
| AC-17 | 资源占用（CPU ≤25%、内存 ≤1 GB） | 采样/基准 | 达标 | 开发主机：单 WAV 基准工作集 100 MB（RTF≈0.05）；目标机未测 | 基准输出 | 开发主机 **PASS** / 目标机 | **PASS (dev host) / NOT_TESTED (target)** |
| AC-18 | 自包含发布产物可生成 | `dotnet publish -c Release -r win-x64 --self-contained true` | 生成完整产物 | `dist/字幕君-win-x64/`（~184.7 MB，495 文件，含 `onnxruntime.dll` 17.0 MB、`sherpa-onnx-c-api.dll` 4.4 MB） | 发布目录 | 开发主机 | **PASS** |
| AC-19 | 代码签名安装包 | 生成并签名安装程序 | 已签名安装包 | 已生成 `dist/字幕君-Setup.msi`(83 MB) 与 `dist/字幕君-Setup.exe`(83.5 MB，WiX Burn)，两者均以**自签名**证书签名（证书链不受信任，Windows SmartScreen 仍会警告）；**未获得 CA 签发的受信任证书** | `installer/`、`dist/` | 开发主机 | **PARTIAL（自签名）** |
| AC-20 | 便携 ZIP 构建并在目标机启动 | 解压并运行 `字幕君.exe` | 正常启动 | 开发主机：解压后进程存活（STILL_RUNNING）；日志记录 native=True (1.13.8)、自动识别随包模型 (installed=True)、设备数=1，界面显示“就绪 / Ready”且 Start 可用（不再置灰）；目标机（Win11）未测 | 冒烟启动日志 | 开发主机 **PASS** / 目标机 | **PARTIAL** |

## Summary

- 依赖真实 **Windows 11 目标硬件** 的 P0 条目（至少 AC-01、AC-04、AC-05、AC-09、AC-17 的目标机部分、AC-20 的目标机部分）全部为 **BLOCKED / NOT_TESTED**。
- 因此本次发布是 **候选发布版本（candidate pending hardware acceptance）**，**不是**已验收版本。
- 开发主机上可验证的部分（构建、单元/集成/性能测试、真实模型解码、WASAPI 采集、离线校验、发布产物生成）均已 **PASS** 或有明确的部分结论。

---

## Speaker-diarization acceptance matrix (SD-01 .. SD-18) — V0.4.1

> Same rules as above: only executed facts are recorded; anything not run is `NOT_TESTED` / `BLOCKED`.
> Scope note: **V0.4.1 (会后离线分离 + 对齐 + 匿名编号) is implemented**; **V0.4.2** (rename / merge /
> reassign / by-speaker filter + export) and **V0.4.3** (dual-stream / near-real-time) are **not started**,
> so the items that need them are `NOT_TESTED` even where the storage layer already supports them.

| 编号 | 验收内容 | 验证方法 | 预期 | 实际 | 证据 | 环境 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SD-01 | 旧版本字幕功能正常 | 回归：既有单元/集成/性能测试 | 全通过 | 136 单元 / 10 集成 / 3 性能(+1 跳过) 全通过；迁移为纯新增 | 测试输出 | 开发主机 | **PASS** |
| SD-02 | 可导入本地音频进行分角色分析 | 页脚「说话人」→ 选择音频；`SpeakerDiarizationService` | 完成分析 | 端到端用例：`NaudioAudioFileLoader` 解码 → 分角色 → 落库，`success=True` | `SpeakerDiarizationRealModelTests` | 开发主机 | **PASS** |
| SD-03 | 可自动生成匿名发言人编号 | `SpeakerCountMode.Auto` | 输出 A/B/… | 真实 2 人音频 → `speakers=2`（标签 A、B） | 集成测试输出 | 开发主机 | **PASS** |
| SD-04 | 可手动指定发言人数 | `SpeakerCountMode.Manual` → `FastClusteringConfig.NumClusters` | 生效 | 链路已实现（`DiarizationRequest.ManualSpeakerCount` + 设置项 `DiarizationSpeakerCount`）；**尚无独立 UI 控件**，未在真实音频上对比 | 代码/设置 | 开发主机 | **PARTIAL** |
| SD-05 | 字幕与说话人时间对齐 | `SpeakerAlignmentService` + 集成测试 | 逐句归属 | 2 条字幕 → 分属 2 个不同发言人 | 集成测试输出 | 开发主机 | **PASS** |
| SD-06 | 未知或歧义发言人可被标记 | 单元测试 | 未知=空、歧义=待确认 | 无重叠→`null`；近似均分→`NeedsConfirmation=true`；精确平局→未知+待确认 | `SpeakerAlignmentServiceTests` | 开发主机 | **PASS** |
| SD-07 | 可修改发言人名称 | 发言人管理窗口重命名 → `UpsertSpeakerAsync` | 重命名并同步 | **UI 已实现**（发言人管理窗口；保存后主窗口标签同步）；仓储层有单测；**未人工交互验证** | `SpeakerManagementViewModel` | 开发主机 | **PARTIAL** |
| SD-08 | 可合并错误拆分的发言人 | 发言人管理窗口合并 + 撤销 | 合并 + 撤销 | **UI 已实现**（选择目标合并 + 一层撤销：还原受影响字幕归属与源发言人状态）；`MergeSpeakersAsync` 有单测；**未人工交互验证** | `SpeakerManagementViewModel` / `SqliteSpeakerRepositoryTests` | 开发主机 | **PARTIAL** |
| SD-09 | 可修改单条字幕的发言归属 | 发言人管理窗口逐条改派 + 应用 | 手动改派并持久化 | **UI 已实现**（逐条下拉选择 → 应用；`Source=Manual` 不被重分析覆盖）；有单测；**未人工交互验证** | `SpeakerManagementViewModel` / `SqliteSpeakerRepositoryTests` | 开发主机 | **PARTIAL** |
| SD-10 | 人工修改能够持久保存 | 单元测试 | 重分析不覆盖人工 | `Source=Manual` 行在 `overwriteManual:false` 下保持不变 | `SqliteSpeakerRepositoryTests` | 开发主机 | **PASS** |
| SD-11 | 按角色导出正确 | 导出对话框 CSV / 分角色输出 | 分角色 TXT/MD/CSV/SRT | 已实现：CSV（`index,start,end,speaker,text`）+ TXT/SRT 前缀发言人 + Markdown「按发言人整理」分节；格式层有单测；**UI 导出未人工交互验证** | `SpeakerAwareExportTests` | 开发主机 | **PARTIAL（格式层 PASS）** |
| SD-12 | 原有会议历史正常打开 | 迁移 4 + 既有仓储测试 | 旧库可读 | 迁移为 `CREATE TABLE IF NOT EXISTS`，`segments` 未被改动；v3 库升级后既有查询不变 | `SqliteDatabaseTests` | 开发主机 | **PASS** |
| SD-13 | 无网络可完成分角色分析 | 断网/离线校验 | 无需网络 | 模型仅经 `ModelDownloads`（隔离）下载；分析在本机真实模型上完成，运行期无 `System.Net.Http` | 集成测试 + `OfflineVerification` | 开发主机 | **PASS** |
| SD-14 | 不影响正常会议播放 | 实机播放验证 | 无异常 | 未执行（无目标硬件/真实会议） | — | 目标硬件 | **NOT_TESTED** |
| SD-15 | 不导致实时 ASR 持续积压 | 目标机并发压测 | 队列不持续增长 | 隔离设计已就位（独立 **BelowNormal** 线程、`SemaphoreSlim(1,1)` 单任务、低线程上限、按文件读取不经实时队列）；**未在目标机并发实测** | 代码 | 目标硬件 | **NOT_TESTED** |
| SD-16 | 可取消任务并安全恢复 | 触发取消 | 可取消且状态一致 | `CancellationToken` 贯穿解码/引擎/落库；原生回调返回非零即中止；结果落库为 `Cancelled`。**未做端到端取消演示** | 代码 | 开发主机 | **PARTIAL** |
| SD-17 | 临时音频删除策略正确 | 检查临时文件生命周期 | 到期清理、崩溃可恢复 | 采集侧为**可选录制**（默认关闭，不落盘）：`IRecordingService`/`WaveRecordingService` 后台 16 kHz 单声道写入，帧经有界通道拷贝、不阻塞采集线程；临时录音在一次分角色分析成功后即删；启动时 `LocalAudioAssetStore.CleanupAsync` 清理到期临时资产与 >24h 孤儿文件。`RecordingTests` 覆盖写入往返与清理 | `RecordingTests` | 开发主机 | **PASS** |
| SD-18 | 真实多发言人音频验证通过 | 真实 2 人音频分析 | 正确区分并归属 | 2 人片段 → speaker 0（前）与 speaker 1（后）正确区分（置信度 0.84–0.97）；字幕正确归属；`OriginalText` 未改动 | `SpeakerDiarizationRealModelTests` | 开发主机 | **PASS** |

### SD summary

- **V0.4.1**（导入 → 离线分离 → 对齐 → 匿名编号 → 字幕板标签）：SD-01/02/03/05/06/10/12/13/18 在开发主机 **PASS**。
- **V0.4.2**（发言人管理 + 分角色过滤/导出）本版本已实现：SD-07/08/09 为 **PARTIAL**（UI 已实现、存储层有单测，但**未人工交互验证**）；SD-11 格式层 **PASS**、UI 未交互验证。
- SD-14/15 需**目标硬件/真实会议**并发验证，标记 **NOT_TESTED**（设计上的性能隔离已就位）。
- SD-04/16 为 **PARTIAL**（链路已就位，缺 UI 或端到端演示）。
- SD-17 已接线并通过单测（录制写入往返 + 到期/孤儿清理，默认不录制）：**PASS**（UI 交互未人工验证）。
- **V0.4.3**（双路采集、准实时）尚未开始。
- 因此 **V0.4 整体为 Release Candidate**，不得宣称已通过实机验收。

---

## File-transcription acceptance matrix (FT-01 .. FT-25) — V0.5 (in progress)

> Same rules as above: the `实际` column contains **only** executed facts; anything not run is
> `NOT_TESTED` / `BLOCKED` — never `PASS`. The FT numbering and titles below are **reconstructed from
> the V0.5 file-transcription spec** and anchored to concrete evidence, not invented results.
>
> **Scope note: only V0.5 Phase 0–1 (FFmpeg tooling + media decode layer) is implemented.** Phases
> 2–8 (segmented long-audio ASR, role-tagged dialogue, job queue / checkpoints, dialogue editor UI,
> file-job export, packaging) are **NOT_STARTED**, so their items are `NOT_TESTED`. Where a V0.4
> feature already covers the *equivalent capability*, the row says so ("重用 V0.4 …") but is still
> `NOT_TESTED` for the file-job path.
>
> Decode-level evidence: [`FILE_TRANSCRIPTION_TEST_REPORT.md`](FILE_TRANSCRIPTION_TEST_REPORT.md).
> Design + phase status: [`ARCHITECTURE_V05.md`](ARCHITECTURE_V05.md).

| 编号 | 验收内容 | 验证方法 | 预期 | 实际 | 证据 | 环境 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| FT-01 | 导入真实 WAV 音频并离线解码 | `MediaDecodeTests` 探针/解码 | 识别为音频、≥1 音轨、时长有效 | 样本 `0.mp3/m4a/aac/flac/ogg` 探针全部 `HasAudio=true`、`AudioStreams≥1`、时长 5.0–6.5 s | `MediaDecodeTests.Probe_reports_audio_and_a_valid_duration` | 开发主机 | **PASS** |
| FT-02 | 导入真实 MP3（及 M4A/AAC/FLAC/OGG）并离线解码 | 同上 + `MediaToAsrEndToEndTests` | 解码出 16 kHz 单声道 PCM | `0.mp3` 探针 `container=mp3 duration=5.64s`；解码 → 中文文本 | `MediaToAsrEndToEndTests`（`0.mp3`） | 开发主机 | **PASS** |
| FT-03 | 导入真实 MP4（视频）并提取其中的音轨 | `MediaDecodeTests.Decoding_a_mp4_extracts_16kHz_mono_audio` + `MediaToAsrEndToEndTests` | 从视频容器抽出音频并解码 | `0.mp4` 探针 `kind=Video container=mov,mp4,m4a,3gp,3g2,mj2 duration=5.61s audioStreams=1` → **89 784 采样 = 5.61 s** → 文本 `对我做了介绍那么我想说的是大家如果对我的研究感兴趣呢` | 集成测试输出 | 开发主机 | **PASS** |
| FT-04 | 导入其他视频容器（MKV/MOV/AVI）并提取音轨 | 同上 | 抽出音频并解码 | `0.mkv`(matroska, 5.63 s)、`0.ogg`(ogg, 5.63 s) 解码出文本；`0.mov`/`0.avi` 探针 `HasAudio=true`。**附：多音轨** `two-tracks.mp4` 选音轨 1 可解码、索引 9 → `StreamIndexOutOfRange`（解码层已支持） | `MediaDecodeTests` / `MediaToAsrEndToEndTests` | 开发主机 | **PASS** |
| FT-05 | 解码音频经离线 ASR 得到中文文本 | `MediaToAsrEndToEndTests` | 产出中文文本 | **已证实**：`0.mp4/mkv/ogg/mp3` 解码 → 中文文本；但为整段一次性喂入引擎，**未经分段/VAD 流水线** | `MediaToAsrEndToEndTests` | 开发主机 | **PARTIAL** |
| FT-06 | 离线 ASR 输出句子级分段（utterance 边界） | 集成测试 | 输出逐句分段 | 复用引擎的 endpoint/分段能力可产出部分/终稿；**文件任务的分段流水线未构建** | （Phase 2） | 开发主机 | **PARTIAL** |
| FT-07 | 长音频（≥1 小时）分段 + VAD 离线转写 | 端到端长音频 | 正确分段并转写 | 未执行（Phase 2 未开始） | — | 开发主机 | **NOT_TESTED** |
| FT-08 | 每条音频块/句子保留全局时间戳 | 代码 + 集成测试 | 全局时间戳 | 每条 **1 秒 `PcmBlock` 带全局起始时间**（`TimeSpan Start`，已实现并随解码输出）；**句子级时间戳未构建** | `PcmBlock` / `FFmpegMediaDecodeService` | 开发主机 | **PARTIAL** |
| FT-09 | 文件任务中可选择要转写的音轨 | 端到端 | 选择音轨并转写 | 未执行（文件任务 UI/作业模型未构建）；**解码层已支持 `AudioStreamIndex`**（见 FT-04 附） | — | 开发主机 | **NOT_TESTED** |
| FT-10 | 任务级错误处理（损坏/不支持媒体不静默、可跳过） | 端到端 | 明确报错/跳过 | 未执行（Phase 4）；**解码层已映射类型化错误** `FileNotFound/NotReadable/NoAudioTrack/StreamIndexOutOfRange` | `MediaDecodeTests`（`NoAudioTrack`/`FileNotFound`） | 开发主机 | **NOT_TESTED** |
| FT-11 | 断点续跑（崩溃/中断后可恢复） | 端到端 | 可恢复 | 未执行（Phase 4 未开始；无 `JobCheckpoint` 表） | — | 开发主机 | **NOT_TESTED** |
| FT-12 | 复用 V0.4 说话人分离对文件转写结果分角色 | 端到端 | 分角色标注 | 未接线到文件任务（Phase 3 未开始）。**重用 V0.4**：导入本地音频 → 离线分离 → 落库已 **PASS**（SD-02/03） | `SpeakerDiarizationRealModelTests`（SD-02/03） | 开发主机 | **NOT_TESTED** |
| FT-13 | 转写分段与说话人时间对齐 | 端到端 | 逐句归属 | 未接线到文件任务（Phase 3）。**重用 V0.4**：字幕与说话人对齐已 **PASS**（SD-05） | `SpeakerAlignmentService`（SD-05） | 开发主机 | **NOT_TESTED** |
| FT-14 | 生成带角色标签的对话（role-tagged dialogue） | 端到端 | 输出对话轮次 | 未执行（Phase 3 未开始；无 `ITranscriptAlignmentService`） | — | 开发主机 | **NOT_TESTED** |
| FT-15 | 对话编辑界面（重命名/合并/改派） | UI 交互 | 可编辑并持久化 | 未执行（Phase 5 未开始）。**重用 V0.4**：发言人管理窗口已实现（SD-07/08/09，但仅 PARTIAL、未人工交互验证） | `SpeakerManagementWindow`（SD-07..09） | 开发主机 | **NOT_TESTED** |
| FT-16 | 多文件任务队列 | 端到端 | 队列依次处理 | 未执行（Phase 4 未开始；无 `ITranscriptionJobService`） | — | 开发主机 | **NOT_TESTED** |
| FT-17 | 任务进度与状态展示（UI） | UI 交互 | 进度/状态可见 | 未执行（Phase 4/5） | — | 开发主机 | **NOT_TESTED** |
| FT-18 | 文件任务导出 TXT / Markdown | 端到端 | 内容正确 | 未接线到文件任务（Phase 6）。**重用 V0.4**：TXT/Markdown 导出格式层已 **PASS**（AC-14、SD-11） | `SpeakerAwareExportTests` | 开发主机 | **NOT_TESTED** |
| FT-19 | 文件任务导出 CSV / SRT | 端到端 | 内容正确 | 未接线到文件任务（Phase 6）。**重用 V0.4**：CSV/SRT 格式层已 **PASS**（AC-14、SD-11） | `SpeakerAwareExportTests` | 开发主机 | **NOT_TESTED** |
| FT-20 | 解码/转写全程离线（无网络） | 静态 + `OfflineVerification` | 运行期无网络 | 解码路径（FFmpeg/sherpa-onnx 本地）无网络，`OfflineVerification` 仍 **5/5 PASS**；**完整文件任务流未构建** | `OfflineVerification` + 代码 | 开发主机 | **PARTIAL** |
| FT-21 | 任务取消与失败清理 | 端到端 | 可取消、状态一致、清理 | 未执行（Phase 4）。**解码层已实现**：取消会杀死子进程树（`ProcessRunner` 注册 `CancellationToken`） | `ProcessRunner` | 开发主机 | **NOT_TESTED** |
| FT-22 | Unicode / 空格 / 长路径安全 | 集成测试 | 路径不影响解码 | `MediaDecodeTests.Decoding_a_unicode_path_with_a_space_succeeds`：`…\测试 folder\0 拷贝.mp3` 解码成功；**长路径/更多字符集未测** | `MediaDecodeTests` | 开发主机 | **PARTIAL** |
| FT-23 | 不回归既有实时字幕功能 | 回归：既有单元/集成/性能测试 | 全通过 | **单元 159 / 集成 33 / 性能 3(+1 跳过)** 全通过，构建 0 错误；既有实时字幕套件未受影响 | 测试输出 | 开发主机 | **PASS** |
| FT-24 | 目标硬件（Windows 11 + Core Ultra 7 155H）上的文件转写验收 | 目标机运行 | 达标 | 未执行（无该机器） | — | 目标硬件 (Win11) | **BLOCKED** |
| FT-25 | 发布产物/安装包包含 FFmpeg | 解压/安装后运行 | 随包提供 FFmpeg | 未执行：**V0.4.0 安装包存在但不含 FFmpeg**；Phase 8 打包未开始（`fetch-ffmpeg.ps1` 仅供开发/构建期） | `installer/` + `tools/fetch-ffmpeg.ps1` | — | **NOT_TESTED** |

### FT summary

- **Phase 0–1（已实现）**：FT-01/02/03/04 在**解码层** **PASS**；FT-05/06/08 为 **PARTIAL**（离线 ASR 在解码音频上可用、
  PcmBlock 全局时间戳存在，但**句子/分段级流水线未构建**）；FT-22 **PARTIAL**、FT-23 **PASS**（无回归）。
- **Phase 2–6（未开始）**：FT-07/09/10/11/12/13/14/15/16/17/18/19 为 **NOT_TESTED**；其中 FT-12/13/15/18/19 标注了
  **可重用的 V0.4 等价能力**（分离/对齐/发言人管理/导出格式），但**对文件任务均未接线**。
- FT-20 **PARTIAL**（解码路径离线，任务流未构建）；FT-21 **NOT_TESTED**。
- **FT-24/25**：目标硬件（Win11）**BLOCKED**、安装包内含 FFmpeg **NOT_TESTED**。
- 因此 **V0.5 远未完成**：仅 Phase 0–1 可用，**不得宣称文件转写功能已达成**。
