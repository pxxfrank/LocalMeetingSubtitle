# Release Checklist — Acceptance Matrix (AC-01 .. AC-20)

**Status banner: 候选发布版本 — 待实机验收 / Release candidate — pending hardware acceptance.**

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
| AC-06 | 端到端实时转写（UI：开始→转写→落库） | 应用内点击开始播放真实会议音频 | 连续实时字幕并写入数据库 | 解码层 PASS（`AsrBenchmark` 真实模型出文本）；UI 端到端未走通（无真实音频、应用数据目录内未装模型） | AsrBenchmark 输出；`MainViewModel` 逻辑 | 开发主机 | **PARTIAL/BLOCKED** |
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
