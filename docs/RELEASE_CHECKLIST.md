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
| AC-03 | 单元测试全部通过 | `dotnet test …UnitTests…` | 全通过 | 112 通过 / 0 失败 | 测试输出 | 开发主机 | **PASS** |
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
