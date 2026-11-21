# AutoTest｜R1 源码与新业务基线差异、交付和真机验收
**回读快照 2026-11-21｜main@ffb23f8c05ec4a64b82cd781db6357685d082c04｜R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f**
**状态：SOURCE_CANDIDATE / WINDOWS_UNVERIFIED / FULL_PRODUCTION_FIELD_UNVERIFIED / RELEASE_NO-GO**

此 SHA 是检查时的快照，**不是动态最新值**。研发每次修改前先读 R1 当前 HEAD/PR/真实日志。现行用户业务决定见 [00](00_DECISIONS_AND_SUPERSESSION.md)，原 R1 Issue [#70](https://github.com/ch3758134-prog/AutoTest/issues/70) 的目标叙述已部分被取代。

## 1. 不要把“以前代码做过”当作“现在业务符合”
| 子系统 | R1 已有或已有局部真实证据 | 与新方向的差距 | 验证门槛 |
|---|---|---|---|
| HOST Shell/UI | WPF 工程树、TEST Inspector、台架/DBC/部分工具独立窗口、浅色局部改造源码 | 工程师/生产双工作区，专业工具无工程运行、生命周期/编辑恢复、全部操作无重复弹窗 | Windows Debug/Release、WPF 操作全路径与退出 |
| PROJECT | ProjectWorkspace/资源 Revision/SHA、部分自动保存/测试选择 | 一个型号唯一当前资源/TEST 树、分类目录、树序、模板、草稿恢复/复制迁移和无工程工具 | 文件创建/编辑/重启/故障恢复、树/引用测试 |
| TEST | RealRunCoordinator/RealTestWorkbenchService、Prepare/Test/Cleanup、StepOccurrence、单步/用例/勾选范围候选 | 生产全部 Enabled、失败后继续/停止由用例逻辑控制、未接线步骤能力显式限制、Flash 与生产前置解耦 | 正常、FAIL、超时、STOP/断连、真实 CAN/KEY/DBC |
| FLASH/UDS | HydEPB 特定独立 FlashLab 真实烧录曾成功；R1 ConfiguredRealTestFlashPort/CanToolFlashJob 有源码 | 原生工程 Profile 不依赖 XML；生产每次必烧/前置独立阶段；独立工具参数与生产配置区分；软件版号后验 | CanTool 字段完整映射、Windows、真实多轮正常/失败烧录 |
| SCAN/Production | R1 ScanRecipeDraft/UT1/正则、扫描匹配、应用 TEST 用例选择等候选 | 多层 Recipe 与新业务冲突；真实扫码型号核对/跨工程、夹具、等待实体键、自动 Flash+TEST、自动归档未闭环 | 真扫描枪 + 工位 + ECU，全链不同型号/连扫/失败 |
| RECORD | RealTestRecordSession、SQLite、HTML/JSON/CSV、原始证据与同 Occurrence 判定代码 | CAN 完整底本与质量、TEST 关键数据提取、简洁单台报告、批量统计/报告、失败可追溯 | 真运行 RunId→Evidence→报告回读、失败/丢帧/断电 |
| WAVE | CAN/DBC 时间序列、比较器及部分 TEST/RECORD 接入 | 多信号真实采集/参考准入、离散信号算法误判、入库证据类型冲突、实际时间质量 | 同一真实信号 PASS/FAIL/缺失，参考/源 SHA 与质量门禁 |
| DEVICE/HW | 现有主控 USB/CAN/IGN/夹具接口，0x42/0x43 候选 | 真实工位启动/停止按键和机械到位证据，异常状态、设备实际 FW 版本 | 原理图、Keil/CodeWarrior、实物输入/输出测试 |

### 已知 R1 文档不能被误读
- [R1 产品准出复审](https://github.com/ch3758134-prog/AutoTest/blob/R1/docs/r1/R1_06_PRODUCT_RELEASE_READINESS_REVIEW_20261121.md) 结论 **NO-GO** 对应其检查 SHA；后续代码修复不自动改变该报告的当时事实。
- [R1 全系统整改源码候选](https://github.com/ch3758134-prog/AutoTest/blob/R1/docs/r1/R1_07_SYSTEM_WIDE_PRODUCT_REMEDIATION_20261121.md) 有源码提交，但没有相同 SHA 的 Windows/WPF/真实 ECU 完整证明。
- [R1 SCAN×FLASH 接入](https://github.com/ch3758134-prog/AutoTest/blob/R1/docs/r1-scan/R1_SCAN_FLASH_CONFIG_INTEGRATION_20261121.md) 仍使用 ScanRecipe 选择资源和 TEST Flash Step 的 JobFingerprint；其实现不能自动代表新生产流程。
- PR #65 的 TEST-RECORD B-01/C-01 静态复审部分通过，但仍为 Draft；R1 有相关接线候选，不等于 PR 正式合并/现场通过。
- 实物 FlashLab 对特定 HydEPB 成功，不等于迁移后的生产 Flash 或该型号全部版本验证通过。

## 2. 源码迁移的原则与最小改动顺序
**A. 先做差异、保留价值：** 罗列 `ScanRecipePreparationService`、`ProjectScanMatcher`、`ConfiguredRealTestFlashPort.FreezeJobs`、`RealTestWorkbenchService`、`TestProjectWorkbenchViewModel`、`ProductionWorkflow`、`RealTestRecordSession` 等代码真实调用关系。标记可复用后端、与业务冲突的入口、兼容旧工程/数据的转换点。避免直接清空此前 FLASH 真实成果。

**B. PROJECT/TEST 先稳定：** 分类目录→唯一有序 TEST 树、编辑草稿与自动保存、直接 KEY/CAN/DBC/公共流程、失败后的可编辑控制、真实证据。开发调试无需依赖扫描枪/Production。

**C. FLASH/SCAN/工位新增生产编排：** 产品上不要再将 Flash 配成普通 TEST Step 必需项；生产先读取型号工程的一份原生 Flash Profile/固件，真实夹具/按钮条件满足后调用共用 Flash 后端，成功再调用同一 TEST 执行器。停止和失败收尾显式、安全可验证。旧程序/Report 外键引用迁移需保留历史。

**D. REPORT/WAVE 补闭环：** Run/Inspection/FlashAttempt/CaseAttempt/StepOccurrence/CAN 原始附件/WAVE 证据同源；先证成实际单台报告，再研究批量统计。WaveComparison 证据类型冲突及时间质量必须审查，不可伪 PASS。

**E. UI 工具真正独立：** 无工程可启动、独立文件与当前工程资源按工具各自需求使用；生产无鼠标主路径，扫码/按钮可见状态，工具退出和 USB 释放统一。

## 3. R1 交付范围暂不替用户选择
负责人没有确认“R1 必须立即做到完整扫码-夹具-实体按钮-烧录-TEST-批量报告”还是“先验工程师完整 TEST/FLASH/RECORD 技术底座再增量生产”作为当前版本准出要求。**不得把本管理文档的业务目标擅自改成 R1 首版版本承诺。** R1 仍需推进已有真实 TEST/FLASH/RECORD 实测，而不是等待 SCAN/REPORT 全量研究才修基础运行缺陷。建议对每个能力分别标记工程师可用、生产就绪、正式验收等级。

## 4. 四类证据门禁
1. **SOURCE_EXISTS**：文件/入口/实际调用存在，精确 SHA；静态审查不得直接写测试通过。
2. **BUILD_VERIFIED**：同一 HEAD 的 Windows Restore、Debug+Release Build、全量相关测试、WPF 运行；FW 需另给 Keil/CodeWarrior 结果。
3. **FIELD_VERIFIED_EXACT_CASE**：真实 ECU、主控/KEY 固件、扫码枪/夹具/按钮、CAN TX/RX、烧录日志、测量值、报告路径和明确 RunId 共同证明对应具体场景。
4. **PRODUCT_RELEASE_PASS**：必须相应工作区/功能验收满足、异常与恢复受测，且用户明确认可；一个历史真实 Flash 成功不构成整个 R1 准出。

## 5. 最短闭环场景建议（按现有证据推进而非空想）
- **工程师场景**：新建/打开 ECU 工程→修改 DBC/TEST 自动保存→独立手动台架发 CAN/KEY→实际 DBC 信号正反判定→保存步骤日志/原始 CAN/单次结果→关闭再重开完全恢复。
- **Flash 场景**：配置 CanTool XML 参数的原生界面→选当前固件/安全算法→独立工具正常烧录并记录进度/CAN/UDS→与生产 Profile 的关系清楚，真实 ECU 结果可查。
- **生产场景（待现场条件）**：放 DUT→扫码型号核对→夹具压紧→实体启动→本次 Flash→TEST 全部已启用用例→记录/报告→结果保持/下一扫码；必须覆盖异型号、正在运行中误扫、烧录失败、STOP、重复编号。
- **报告场景**：一台 PASS、一台功能 FAIL、一台烧录 FAIL、同号复测，逐条证明 CAN 底本/关键测量/单次报告与批量统计不混数。

## 6. 必须保持的现实风险
真实设备 USB ACK 不等于 ECU 响应；0x43 读回不等于测速；夹具 GPIO 值不等于机械夹紧；APP 50 03 不等于目标软件版本；CAN/DBC/WAVE 数据丢失不得记正式 PASS。用户先前纠正过“把所有 UI 错误都等同电气危险”这类过度设计：设备保护必须依据**真实物理状态与动作权限**分级，不能为管理上的“安全”编造硬件事实。

## 7. 新业务对 Issue 的影响
[#70](https://github.com/ch3758134-prog/AutoTest/issues/70) 旧文仍以 TEST Flash 步骤及人工启动为中心；[#72](https://github.com/ch3758134-prog/AutoTest/issues/72) 旧文偏向多层扫描配方和扫码后确认。必须从本管理基线读取最新用户决定；旧工作单代码候选仍可保留，**实施顺序和业务验收须做迁移评审**。