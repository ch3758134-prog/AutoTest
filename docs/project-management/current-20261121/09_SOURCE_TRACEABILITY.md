# 09｜原始合同、专业研究与源码的追溯索引

**目的**：避免本次整理中的二手结论脱离原始审批、代码和实测文件。**导航本身不覆盖任何文档原文。** 以下每项对应已经回读的 GitHub 版本或用户明确要求，具体源文可能在历史研发分支，需以对应 SHA 链接查看；遇到冲突依 [01](01_AUTHORITY_AND_CURRENT_STATE.md)、[02](02_DECISION_SUPERSESSION_LEDGER.md) 判断地位。

## 1. 负责人已批准的基本产品/交付合同

| 类型 | 原始来源 | 当前地位 |
|---|---|---|
| 产品与阶段 | [P0-01～P0-08 / P0 Gate](../new-baseline-20261112/) 与 [已批准 P 阶段](../approved-20261113/) | 定位有效，历史执行序列由 2026-11-19 R1 决定更新 |
| G3 直接 KEY | [CR-KEY-DIRECT-STEP](../new-baseline-20261112/G3-SUPPLEMENTS-20261116/CR-KEY-DIRECT-STEP.md) | 有效产品语义，真实硬件/执行有独立门禁 |
| G3 公共流程 | [CR-PUBLIC-FLOW](../new-baseline-20261112/G3-SUPPLEMENTS-20261116/CR-PUBLIC-FLOW.md) | 有效资源与执行设计方向 |
| G3 双夹具 USB | [G3 主控补充](../new-baseline-20261112/G3-SUPPLEMENTS-20261116/CR-MAIN-CONTROL-USB-CLAMPS.md) | 独立夹具/USB 原则有效，但旧 IGN PIN 等已被新 HW 文档纠正 |
| R1 产品优先级 | [R1 真实 TEST + FLASH 方向](../new-baseline-20261112/research/R1-REAL-TEST-FLASH-PRIORITY-20261119.md) | 当前批准 |
| WAVE/SCAN 立项 | [R1 WAVE/SCAN 两专项](../new-baseline-20261112/research/R1-WAVE-SCAN-SPECIALISTS-20261120.md) | 已立项研究，具体新算法/生产 Schema 候审 |
| UI 方法原档 | [UI-UX-FOUNDATION v1.0 原文与审查](../new-baseline-20261112/research/UI-UX-FOUNDATION-20261119/README.md) | 研究方法已归档，产品 UI 未冻结 |

## 2. 实体硬件事实与接口

| 对象 | 真正应核对的来源 | 状态 |
|---|---|---|
| 主控 CAN / 0x42 0x43 / 固件 USB | [Main_Control: USB_HOST_CONTROL_INTERFACE.md](https://github.com/ch3758134-prog/AutoTest/blob/e06a820e0d54dc7719cc75af1ecae005f05e164f/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md) | 候选源码，Keil/实物尚未认证 |
| IGN/LED/夹具/HOUT/RL_DRV | [Main_Control: BOARD_FIXED_IO_MAP.md](https://github.com/ch3758134-prog/AutoTest/blob/e06a820e0d54dc7719cc75af1ecae005f05e164f/docs/interfaces/BOARD_FIXED_IO_MAP.md) + [PM 2026-11-18 IO 更新](../new-baseline-20261112/research/MAIN-CONTROL-IO-USB-SOURCE-UPDATE-20261118.md) | 新 GPIO/PIN 定义纠正旧 G3 错误 |
| ECU CAN 动态波特率 PM 审查 | [2026-11-20 固件静态接收](../new-baseline-20261112/research/MAIN-CONTROL-ECU-CAN-BAUD-20261120.md) | SOURCE_ACCEPTED_WITH_LIMITATIONS |
| KEY_SIM 20 Key 与物理风险 | [KEY_SIM 硬件事实及遗留问题](../new-baseline-20261112/research/KEY-SIM-HARDWARE-FACT-INTAKE-20261117.md) | K19/20 与供电/热风险仍未消除 |
| 仓库结构 | [三工程平级更正](../new-baseline-20261112/research/PEER-PROJECT-DIRECTORY-BASELINE-20261119.md) / [main 结构](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/architecture/REPOSITORY_STRUCTURE_20261119.md) | 当前源码路径事实 |

## 3. TEST/FLASH/RECORD 和 R1

| 专业 | 来源 | 当前地位 |
|---|---|---|
| R1 真 TEST 源码 | [R1@fa3b18e](https://github.com/ch3758134-prog/AutoTest/tree/fa3b18e948043a9d1960adddb627658fd40c8159/HOST) / [R1-00～03 统一候选](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/docs/r1/R1_00_03_UNIFIED_SOURCE_CANDIDATE_20261120.md) | 有源码，Windows/ECU 全面验收待实证 |
| R1 真实验证 | [R1-04 Windows/ECU 交接](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/docs/r1/R1_04_WINDOWS_FIELD_VALIDATION.md) | 验收程序，不是验收回执 |
| TEST-RECORD | [PR #65](https://github.com/ch3758134-prog/AutoTest/pull/65)、[B-01 首审](../new-baseline-20261112/research/TEST-RECORD-PR65-TECHNICAL-REVIEW-20261119.md)、[B-01 定点复审](../new-baseline-20261112/research/TEST-RECORD-PR65-B01-REVIEW-20261119.md)、[C-01 验收](../new-baseline-20261112/research/TEST-RECORD-PR65-C01-CLEARANCE-20261119.md) | B-01/C-01 已静态认可；R1 已接线，但不是 HOST PR 合并或真机 PASS |
| Flash 独立历史实测 | [Flash 实测事实接收](../new-baseline-20261112/research/FLASH-REFERENCE-VALIDATION-INTAKE-20261118.md)、[PR #55](https://github.com/ch3758134-prog/AutoTest/pull/55) | 有限参考，不替代 R1 自动烧录现场验收 |
| UDS 工具独立研究 | [UDS 专业分支/实测合同](../new-baseline-20261112/research/UDS-DEDICATED-BRANCH-AND-REAL-LAB-20261118.md)、[PR #60](https://github.com/ch3758134-prog/AutoTest/pull/60) | 独立工具与 TEST 的区别仍有效 |

## 4. 当前研究输入与尚未批准的方案

| 专项 | 直接读哪些原始文件 | 权限 |
|---|---|---|
| WAVE #71 | [W-00](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-00_SOURCE_CAPABILITY_AUDIT_20261121.md)、[W-01](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-01_INDUSTRIAL_COMPARISON_AND_FALSE_JUDGEMENT_20261121.md)、[W-02](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-02_BACKEND_INTERFACE_AND_RECORD_CONTRACT_20261121.md)、[W-03](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-03_REAL_ECU_ACCEPTANCE_AND_ROLLOUT_20261121.md) | `RESEARCH_PROPOSED`，需要新技术合同及 R1 实测 |
| SCAN #72 | [S-00](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-00_SOURCE_AUDIT_20261121.md)、[S-01](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-01_MATCHING_RUNPLAN_INTERFACE_CONTRACT_20261121.md)、[S-02](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-02_PRODUCTION_DEVELOPMENT_OPERATOR_FLOWS_20261121.md)、[S-03](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-03_R1_IMPLEMENTATION_HANDOFF_20261121.md) | `RESEARCH_PROPOSED`，生产绑定和批次策略待审批 |
| 活动 UI/UX | [UI research R06](https://github.com/ch3758134-prog/AutoTest/blob/ui/ide-design/docs/ui/research/R06_FULL_PRODUCT_UX_IMPLEMENTATION_READY_20261119.md)、[V03-02 UI 交互计划](https://github.com/ch3758134-prog/AutoTest/blob/ui/ide-design/docs/ui/visual-research/V03-02/AUTOTEST_FULL_PRODUCT_UI_INTERACTION_PLAN_20261119.md)、[UI 决策档案](https://github.com/ch3758134-prog/AutoTest/blob/ui/ide-design/docs/ui/UX_DECISIONS.md) | 后续全产品设计与用户操作验收，不应取消 R1 当前首版 |

## 5. 识别“历史状态文字已过时”的典型例子

- [G4 PR #61 初审](../new-baseline-20261112/research/G4-PR61-BASELINE-ACCEPTANCE-20261119.md) 写“尚未合并”，但后续 [G4 实际合并](../new-baseline-20261112/research/G4-PR61-MAIN-MERGED-20261119.md) 与 main SHA 证明已经接收。
- [TEST-RECORD 最早技术否决](../new-baseline-20261112/research/TEST-RECORD-PR65-TECHNICAL-REVIEW-20261119.md) 写 B-01，后续 [B-01/C-01 修复审查](../new-baseline-20261112/research/TEST-RECORD-PR65-C01-CLEARANCE-20261119.md) 已更新为静态认可；但 Windows/现场仍未完成。
- [R1 首建规划](../new-baseline-20261112/research/R1-REAL-TEST-FLASH-PRIORITY-20261119.md) 的“尚无源码”不能用于判断 [2026-11-20 R1 最新源码](https://github.com/ch3758134-prog/AutoTest/tree/fa3b18e948043a9d1960adddb627658fd40c8159/HOST)；源码已实施一部分，现场运行仍待验证。
- [最初硬件 G3](../new-baseline-20261112/G3-SUPPLEMENTS-20261116/CR-MAIN-CONTROL-USB-CLAMPS.md) 的 IGN 映射被新的 [Main_Control 硬件接口](https://github.com/ch3758134-prog/AutoTest/blob/Main_Control/docs/interfaces/BOARD_FIXED_IO_MAP.md) 修正；旧文档必须作为历史查阅。

**后续每一项新批准应追加精准变更记录**，不回头直接改旧原文。这个索引是 2026-11-21 的快照，不能替代以后持续回读最新 R1 和专项 Head。
