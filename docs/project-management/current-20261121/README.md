# AutoTest｜全系统现行基线与决策整理（2026-11-21）

> **2026-11-21 负责人已批准：重新全面整理并校正 AutoTest 系统资料。**
>
> **管理结果**：`SYSTEM_CONSOLIDATION_APPROVED / CURRENT_DECISIONS_RECONCILED / R1_SOURCE_CANDIDATE / PRODUCT_UI_NOT_FROZEN / WAVE_SCAN_DESIGNS_PENDING_REVIEW / WINDOWS_FIELD_GATE_PENDING`。
>
> 本目录**是当前阅读入口与决策索引**，不是全部历史文件的物理替代，不表示 Windows Build、Keil、CodeWarrior 或真实 ECU 验收已经通过。以精确分支 SHA、原批准合同和实际验证证据分别判定。

## 从这里开始（按阅读顺序）

| 文件 | 回答什么问题 |
|---|---|
| [01｜权威来源与当前真实状态](01_AUTHORITY_AND_CURRENT_STATE.md) | main、R1、UI、SCAN、WAVE、FW、PM 的版本及“源码/研究/验收”如何区分 |
| [02｜决策继承、取代与冲突台账](02_DECISION_SUPERSESSION_LEDGER.md) | 哪些旧要求保留、哪些被明确推翻、哪些只是研究建议、应找谁决定 |
| [03｜系统与领域完整合同](03_SYSTEM_PRODUCT_AND_DOMAIN_CONTRACT.md) | 产品使命、实体工位、工程、TEST、FLASH/UDS、RECORD、WAVE、SCAN、UI 的职责 |
| [04｜主控/按键板的硬件与通信真值](04_HARDWARE_FIRMWARE_BASELINE.md) | 引脚、CAN、USB、按键/IO 与硬件验证限制；纠正旧 IGN GPIO |
| [05｜R1 首版交付与整体验收计划](05_R1_RELEASE_AND_ACCEPTANCE.md) | R1-00～03 源码现状、Codex 门禁、首版现场验收；不再按包反复实测 |
| [06｜UI/UX 当前约束与后续研究](06_UI_UX_CURRENT_FRAMEWORK.md) | UX 基础研究的地位、R1 临时运行 UI 与长期全产品 UI 的区别 |
| [07｜WAVE/SCAN 新研究如何影响 R1](07_WAVE_SCAN_RECONCILIATION.md) | 2026-11-21 新草案价值、与 R1 冲突、哪些待评审/实施 |
| [08｜执行管理、清理与变更规则](08_GOVERNANCE_MIGRATION_AND_OPEN_ITEMS.md) | 怎样避免历史文件/PR/分支/研究结论混入现行合同 |
| [09｜来源与被覆盖档案索引](09_SOURCE_TRACEABILITY.md) | 回溯对应的原批准文件、专业设计、Git 分支 SHA 与历史错误状态 |
| [10｜2026-11-26 全产品用户语言与操作流程重构](10_R1_PRODUCT_LANGUAGE_AND_WORKFLOW_RESTRUCTURE_20261126.md) | 当轮批准的全系统 UI/操作整改、实际 R1 源码与未通过验证门禁 |
| [11｜2026-11-26 多工程生产库与扫码路由架构研究](11_R1_MULTI_PROJECT_PRODUCTION_ARCHITECTURE_RESEARCH_20261126.md) | 未来生产/工程双 EXE、当前单 EXE、多工程导入/版本/扫码选择的待实施研究方案 |

## 本次整理实际覆盖范围

- 项目管理 `P0/G0/G1/G2/G3` 的已批准语义及 G3 KEY、公共流程、主控增量；
- 2026-11-17～20 真实硬件、旧 WPF 实测、IO、CAN 动态波特率与 Git 基线；
- 旧 G4 HTML/UI 的历史接收与研发线退役、UI 基础理念 v1.0、UI 专项新研究；
- R1 最新源码候选及 `R1-00～R1-03` 整体一次性验收安排；
- TEST-RECORD PR #65 B-01/C-01 审查及 R1 接线后代码状态；
- Flash/UDS 专业候选；WAVE #71 的 W-00～W-03 及 SCAN #72 的 S-00～S-03；
- 各领域与项目管理合同对应关系、Git 分支权属与独立验收等级。

**审查口径**：更晚的用户明确批准决定可取代较早同一事项的批准内容；专业研究、PR 自述、代码提交或 UI 截图**不能自动推翻**正式业务/安全合同。旧原文不删除，只将其当前有效性写在本台账。不同专业成果需精确接口和新审批才能成为执行合同。

## 接手 AI 必须遵守

1. 首读本目录，确定用户要求处理的领域，再读该专业最新**原始**合同与 GitHub HEAD；不得把目录中列的历史 SHA 当作永远最新。
2. 对每条结论写清 `DECISION_APPROVED`、`RESEARCH_PROPOSED`、`SOURCE_EXISTS`、`BUILD_VERIFIED`、`FIELD_VERIFIED` 或 `BLOCKED`，不允许用一个“完成”覆盖多个层级。
3. R1 首版先交真实 TEST + 自动 Flash + 日志/报告；UI 全面研究并行。WAVE/SCAN 的未批合同不得因“报告已写完”自动合并。
4. 研发源码只在责任分支；项目管理事实在 `Project_Management`；未经批准不直接改 `main`、固件、现行 UI 设计源。除非另行受控授权，不删除历史成果或以清理名义覆盖新分支。

**适用有效期**：直到下一次经负责人批准的全系统替代基线。各专业独立持续更新，本目录不在后台自动同步。

> **2026-11-26 增量提醒：** R1 当前存在新的全系统用户语言/交互重构候选，见第 10 项。11-21 文档中与后来用户批准交互方案相冲突的早期 UI 表述，以本轮决策记录为准。此变更仅为 `SOURCE_EXISTS`，Windows/真机仍未准出。
