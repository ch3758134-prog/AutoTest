# AutoTest｜当前项目管理与产品决策入口

> **2026-11-15 HW/FW 新调查入口：** [主控/按键板硬件构型、MCU/连接器/模块引脚与 HOST 能力边界管理档案](new-baseline-20261112/HW-FW-BASELINE-20261115/README.md)。已保存两份校验过的原理图原件、63 条主控 MCU 信号映射、20 条 KEY_SIM 映射、模块及协议差异。**该档案是研究基线，不是现场 PCB、现役固件或 SOL 改线的验收通过**；HW/FW 专职 AI 单独调查，PM 审查，现有 G4 WPF/FLASH 不受阻。

> **当前入口（2026-11-14）。** 新项目管理 AI、G3 研究/设计 AI、UI 专项和代码实施 AI 应先从这里核对最新产品事实，不要从旧 `SAEF/`、历史 G4/V1 目录或未经批准的 HTML 原型推导当前产品范围。

> **2026-11-15 阶段最新状态：G3 已正式批准 PASS_WITH_LIMITATIONS**。审批记录：[G3-APPROVAL-20261115](new-baseline-20261112/G3-APPROVAL-20261115.md)；精确获批包 [G3-00～05](new-baseline-20261112/G3-R1-20261115/README.md)。**G4-UI-00～03 已获准由单一执行 AI 一次性完成**，执行指令 [G4-UI-ONE-SHOT](../delivery/g4/G4-UI-ONE-SHOT-EXECUTION-20261115.md)，专用分支 `feature/g4-html-parity-20261115`。允许自主编码并删除旧 UI，不能擅自合并 main 或操作未经许可的实体 ECU/Flash。其他 G4 功能与已知 G3-L01～10 限制继续受控。旧文件中“G3 候审/G4 未授权”均为审批前历史时点，不可覆盖本记录。

## 首先阅读

**第一优先纠偏：** [手动台架 / DUT 输入模拟勘误](new-baseline-20261112/PM-INPUT-SEMANTICS-20261114.md) —— 防止新 AI 将通用上位机错误缩为 EPB/按键工具，或将 Pulse/Hold/Release 当成全局主操作。

1. [现行业务与产品职责决策（PM-BIZ-20261114-01）](new-baseline-20261112/PM-BUSINESS-DECISIONS-20261114.md) —— 用户已经直接澄清的产品事实、工具职责、工程师与生产运行区别、手动台架、CAN 工具范围和剩余冲突；
2. [G1 已批准需求 v0.3](new-baseline-20261112/G1-REVIEW-v0.3.md) 和 [正式批准记录](new-baseline-20261112/G1-APPROVAL_RECORD.md) —— 保留已批准的历史版本；存在冲突时按上述业务决策追踪局部变更；
3. [G2 系统架构 v0.1](new-baseline-20261112/G2-GATE-REVIEW-v0.1.md) 与 [G2 批准记录](new-baseline-20261112/G2-APPROVAL_RECORD.md) —— G2 `PASS_WITH_LIMITATIONS`；
4. [G3 总体批准记录](new-baseline-20261112/G3-APPROVAL-20261115.md) —— G3 全包已准出，附 G3-L01～10；另 [G3 DEVICE/WAVE/TECH 专项原批准记录](new-baseline-20261112/G3-TECH-APPROVAL-20261114.md) 继续有效。

- [G3-UX v3 实测反馈与领域/接口复核（2026-11-14）](new-baseline-20261112/G3-UX-V3-PM-REVIEW-20261114.md) —— 专项六类问题、对应实际 Controller/TCF/TBC/WAVE/UDS 合同；v3 未获 UI 设计批准，G3 技术研究继续推进。

- [G3 自动测试 UI 兼容性审查与 G4 映射（2026-11-15）](new-baseline-20261112/G3-UX-AUTO-REVIEW-20261115.md) —— 对 `ui/ide-design@ec75a9fe` 精确 HTML 的自动测试操作方向作审查；允许按此推进 G3-A1/A2，明确 TEST FLOW、资源映射、选择性执行与真实设备缺口；非整体 G3/G4 批准。

## 本轮阶段与协作

- `main`：现行产品决策/阶段批准与经审查的集成版本；
- `local`：持续研发中的 HOST 候选版本，不等于 `main` 已验收；
- `Main_Control` / `Key_Sim`：当前固件分支；真实硬件能力以现场接线、实际烧录的固件和实测为准；
- `Project_Management`：项目管理工具的专用研发分支，可能留有历史档案，**不是另一份可自动覆盖本目录的新基线决策**；
- UI/UX 专项对话负责操作、视觉和工程师实际满意的体验；PM/技术设计负责系统业务语义、共享接口、实测条件和审批；
- GitHub 源码/工作包状态、Windows Build、真实 DUT 结果、产品验收要分别留证；不得用 Mock 或历史结果代替。

## 清理说明

2026-11-14 按项目负责人指令，从 `main` 移除旧 `SAEF/` 目录作为活跃工作树的入口，避免早期 Gate 和研发概念误导后续 AI。历史文件可通过 Git 历史以及单独 `Project_Management` 分支追溯，**但不能据此恢复其旧 Gate 结论为当前批准事实**。正式新基线文档仍保留在 `docs/project-management/new-baseline-20261112/`。

参见 [Issue #46｜用户确认业务决策](https://github.com/ch3758134-prog/AutoTest/issues/46) 与 [Issue #45｜技术协同](https://github.com/ch3758134-prog/AutoTest/issues/45)。
