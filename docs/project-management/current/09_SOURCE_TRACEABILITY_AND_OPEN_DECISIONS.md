# AutoTest｜来源、现行决策、未决政策和研究缺口追溯
**2026-11-21｜以准确来源和当前 HEAD 重新验证；历史材料不可当最新状态。**

## 1. 本次重整来源的权威顺序
1. **本轮负责人逐项明确接受的后续产品要求**，汇总在 [00](00_DECISIONS_AND_SUPERSESSION.md)，对于相同问题优先于旧 P0/G3/R1/SCAN 业务主张。
2. 先前经正式批准且**未被后续决定取代**的硬件/USB、真实 TEST 判定/证据门禁、WPF 操作约束。
3. 最新实际 R1/HW/SCAN/WAVE 源码和代码接口；证明“是否已实施”，不能自行改变用户产品决定。
4. 专业研究/工业参考/PR 文档；证明“研究建议”，不是自动获得生产策略的批准。
5. 固定 HEAD 真实 Windows/固件构建及 ECU 工位记录；证明某个有限场景实际成立。

## 2. 现行与历史文档索引
| 主题 | 可追溯的原始资料 | 现行用法 |
|---|---|---|
| PM 上一轮总体整合 | [2026-11-21 原基线目录](../current-20261121/README.md) | 作为历史快照；其旧 Flash/SCAN/TEST 描述被本目录取代 |
| 产品/领域旧合同 | [旧03](../current-20261121/03_SYSTEM_PRODUCT_AND_DOMAIN_CONTRACT.md) | 底层技术仍参考，产品层级重新以 01/02/03 为准 |
| 旧批准决策 | [旧02](../current-20261121/02_DECISION_SUPERSESSION_LEDGER.md)、[P0/G3 资料](../new-baseline-20261112/) | 未取代部分仍有效；逐项裁决见 00 |
| 硬件与 FW | [旧04](../current-20261121/04_HARDWARE_FIRMWARE_BASELINE.md)、[Main_Control](https://github.com/ch3758134-prog/AutoTest/tree/Main_Control) | 硬件真值仍有效，见 05，不从用户业务讨论推断新引脚 |
| 原 R1 交付与 Issue | [旧05](../current-20261121/05_R1_RELEASE_AND_ACCEPTANCE.md)、[#70](https://github.com/ch3758134-prog/AutoTest/issues/70) | 源码/历史 Gate/实测回执保留，生产 Flash 在 TEST 内的旧业务要求被取代 |
| WPF/UI | [旧06](../current-20261121/06_UI_UX_CURRENT_FRAMEWORK.md)、[ui/ide-design](https://github.com/ch3758134-prog/AutoTest/tree/ui/ide-design) | 浅色/编辑/工具体验仍有效，新增独立生产区与工具无工程能力 |
| SCAN | [旧07](../current-20261121/07_WAVE_SCAN_RECONCILIATION.md)、[#72](https://github.com/ch3758134-prog/AutoTest/issues/72)、[S-08](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-08_END_TO_END_SCAN_PRODUCT_SYSTEM_RESEARCH_20261121.md)、[S-09](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-09_SCAN_FLASH_PRODUCTION_RECIPE_INTEGRATION_20261121.md)、[S-10](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-10_NATIVE_FLASH_CONFIGURATION_FIXED_PROJECT_STORAGE_20261121.md) | 原生配置技术继续研究；复杂多层 Recipe、NoFlash、人工 PC 开始等被用户新决定取代 |
| WAVE | [#71](https://github.com/ch3758134-prog/AutoTest/issues/71)、[WAVE 研究分支](https://github.com/ch3758134-prog/AutoTest/tree/research/r1-wave-judgement-20261120) | 波形真实采样、比较准确性、RECORD 证据问题仍待审查 |
| TEST/RECORD | [PR #65](https://github.com/ch3758134-prog/AutoTest/pull/65)、[R1](https://github.com/ch3758134-prog/AutoTest/tree/R1) | 静态修复/候选源码不等于 Windows/真机通过 |
| 历史 UI/G4/FLASH/UDS | [PR #61](https://github.com/ch3758134-prog/AutoTest/pull/61)、[#55](https://github.com/ch3758134-prog/AutoTest/pull/55)、[#60](https://github.com/ch3758134-prog/AutoTest/pull/60) | 分别按原现场、PR、集成状态，不一口气吞并分支 |
| Project Control 系统 | [tools/project-control-v2/README.md](../../../tools/project-control-v2/README.md) | 独立通用管理工具，其研发与 AutoTest 产品不能混用 |

## 3. 仓库状态快照（不要视为实时看板）
`main@ffb23f8c05ec4a64b82cd781db6357685d082c04`；`R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f`；此轮整理开始前 `Project_Management@4bc86df5e03a6e1cd251098e82d1f57255cfc98f`；`Main_Control@e06a820e0d54dc7719cc75af1ecae005f05e164f`；`Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a`。**任何后续实际提交/审批都需重新读取最新 HEAD**，不能将这些快照当作现行唯一 SHA。

## 4. 保留而不替用户决定的问题
| ID | 尚未确定的产品/技术事实 | 下一步 |
|---|---|---|
| U01 | R1 当前版本是否必须完整出厂站点闭环准出 | PM 提出证据驱动的阶段方案，再由用户决定 |
| U02 | 完整扫码条码的字段、校验规则、实物设备是 USB HID/CDC/COM/POS | SCAN 收集实际编码与扫码设备枚举后决定 |
| U03 | 实体 START/STOP 输入和 USB/FW 事件、夹具压紧反馈和安全状态 | Main_Control/DEVICE 实测 |
| U04 | 正常 FAIL 后是否同时保留夹具/IGN 物理现场 | TEST/DEVICE 实测场景和用户决定 |
| U05 | 同编号之前 PASS/FAIL 是否触发不同复测/人工确认 | SCAN/REPORT 研究，再决定 |
| U06 | 批次如何起止、唯一数量、首次良率/最终良率/复检次数、放行政策 | SCAN+REPORT 成熟产品/真实样例研究 |
| U07 | 单台报告具体最简字段、批量报告固定样式和日志索引如何展示 | RECORD/REPORT 交真实案例供用户看 |
| U08 | 原生 Flash Profile 存储 Schema/固定文件名/历史 XML 兼容、独立 Flash 与生产参数复用策略 | FLASH/PROJECT 技术评审 |
| U09 | Flash 中止/断电/停止实体键时的物理状态与恢复路径 | FLASH/DEVICE/实物现场 |
| U10 | 波形真实参考采集、时间质量/离散状态比较、证据类型、误判阈值 | WAVE/RECORD/真实 ECU |
| U11 | 已确认自动化产品流程如何适应不同夹具/工位而不新增过度配置 | UI/DEVICE/生产专项实测 |

**原则**：未定项不应作为某个新工作台的强制额外操作，也不应偷偷按旧 Recipe 默认补齐。需要研究的保留 `RESEARCH_PROPOSED`，用户明确选择后再变正式业务合同。

## 5. 哪些尚未完成
- 本轮 GitHub 管理基线落档不修改 R1、Main_Control、Key_Sim 或 SCAN/WAVE 研发分支的源码和文件；专业实际实现仍需交接与验证。
- `R1` 当前没有可靠的“最新 HEAD Windows Build + 所有功能现场 PASS”总体证据，不因管理已同步称 Release PASS。
- 项目管理内部的旧文档仍保留历史版本；旧 Issue/研究源码如有冲突由醒目修订指针标注，不能让下一个研发 AI 继续照旧制造复杂配方。
- 本文不意味着新的 USB/安全权限、批次统计政策或数据库 Schema 已经通过。