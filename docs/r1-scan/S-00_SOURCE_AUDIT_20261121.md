# AutoTest R1-SCAN S-00｜最新 R1 源码能力/实际断点调查

> Issue #72；审查时间 2026-11-21；审查对象 `R1@fa3b18e948043a9d1960adddb627658fd40c8159`（以调查时远端最新 R1 为准），研究分支起点 `94e82eddb03e69b428fd4c63c2093a570d979c4e`。该研究分支相对 R1 **落后 20 提交**；以下结论来自显式以 ref=R1 回读，不把研究分支旧源当作最新可交付实现。
>
> 状态：`STATIC_SOURCE_AUDITED / R1_INTEGRATION_NOT_IMPLEMENTED / WINDOWS_BUILD_NOT_RUN / SCANNER_AND_DUT_FIELD_NOT_VERIFIED`。本文件仅调查/设计，不改 R1、main、硬件固件。

## 1. 总体结论

当前存在两条未接通的链路：
- UI 输入：`MainWindow.ScannerEntry` → `MainWindowViewModel.FinishScannerInputAsync` → `ProjectScanMatcher.MatchAsync` → **返回字符串状态/RecipeId/DUT 身份**，没有 `RunPlan`、没有设置用例集合/应用 TCF/TBC/码率、没有调用 R1 的 TEST 或 RECORD。
- 生产应用层：`ProductionCommandHandler` → `ProductionSession` → `ProductionWorkflow(IProductionStageExecutor)` → `IRunFactSink`，有状态机和模拟合同测试，但在 Desktop 的 Prism 注册/现场入口中**没有真实 Production stage adapter 和 durable production batch 关联**。

R1 真实链路另外存在：`TestProjectWorkbenchViewModel.PrepareSelectedAsync/RunSelectedAsync` → `RealTestWorkbenchService.ExecuteAsync` → `RealRunCoordinator` + `RealTestRecordSession` → SQLite/Artifact/HTML。它支持开发选中的**有序多用例批次、每用例独立 RunId**，但当前冻结记录为 `Development` 或 `Diagnostic`，不是扫码验证的 `Production`。

**不得用“规则匹配成功”“TEST 有 RunId”推断生产闭环成立。**

## 2. 能力矩阵（P=已有可复用；G=缺口；V=未实机验证）

| 环节 | 现存具体源码 | 已有能力 | 缺口/风险 |
| --- | --- | --- | --- |
| 输入 | `Specialists/Scan/KeyboardWedgeCapture.cs`、`BarcodeInputRouter.cs`、`BarcodeScanEvent.cs` | 专用焦点、CR/LF、超时清空、最大长度、无全局 Hook、有界队列32、忙拒绝策略接口 | UI 注入门禁只检查 `_hardwareBusy`；尚未与真实 TEST/Production busy 完整关联；未确认实物枪 Enter/焦点行为。 |
| Serial | `SerialBarcodeCapture.cs` | 仅验证来源的接口桩和逐行路由 | 未开实际 COM 端口，也未认定实际型号；不能称串口支持现场可用。 |
| 规则 | `IdentityRuleSet.cs` | 正则全串匹配、命名字段、已发布筛选、同一输入多命中拒绝、超时拒绝；规则集 fingerprint | 解析字段**不是可信 ECU BOM/型号证明**；IdentityKey 长度前缀串不等于外部唯一编号的业务合同；无独立编号绑定数据库。 |
| 配方引擎 | `ConfigMatchService.cs` | `model/customer` 候选过滤；多配方拒绝；资产 Id/Revision/SHA 基础校验 | 仅凭解析字段选 Recipe；没有唯一 DUT→配置绑定表，缺明确优先级、客户/硬件版本和生效/废止策略；`ProductionRecipe.Revision` 在桥接处硬编码为1。 |
| 工程扫码资源 | `Projects/ProjectConfigurationDocuments.cs` | `ScanMatchSettingsDocument`、规则及配方的持久化/发布/测试程序/DBC/固件 pins | `ScanRecipeDraft` 仅 TestProgram/DBC/Firmware + `RequiresFlash`；**无 TestProject/EntryId 有序子集、TCF、TBC/Controller、波特率、Flash XML/Key/Seed、报告规则**。 |
| 实际扫码匹配 | `Projects/ProjectScanMatcher.cs` | 检查唯一已发布配置、资产修订、项目 revision 后返回状态 | 只返回 `Code/RecipeId/Identity/Explanation`；不含全部 pins/plan；中途异常部分以 throw 表示；没有真实执行 bridge。 |
| WPF 入口 | `Shell/MainWindow.xaml(.cs)`、`MainWindowViewModel.cs` | 底栏专用输入框、输入后 Enter 匹配和状态反馈 | `CaptureScannerText` 在 WPF 专用框触发且硬编码 focus=true，未认证源头是否真是扫描枪；队列 reader 仅 drain；无匹配详情、确认、保存 RunPlan/复检上下文。 |
| 测试项目 | `Testing/Authoring/TestProjectDocument.cs` | 有序 `Packages`、每条 `ItemId/PackageAssetId/Revision/SHA/Enabled`；可以重复引用同一用例 | 没有供生产独立引用的“冻结执行子集”资产；`Enabled` 是工程默认值，不应作为生产批次运行临时勾选。 |
| 运行勾选 | `Projects/ProjectRunSelectionState.cs` | 工程师 UI 临时选择与项目持久化 Enabled 分离 | 状态仅内存；扫码不能直接写入并借用该 UI 状态充当可追溯的生产计划。 |
| 真实 TEST | `Testing/RealTestWorkbenchService.cs`、`TestProjectWorkbenchViewModel.cs` | 唯一真实 TEST 入口；用例逐一冻结/执行/记录；不支持 Mock fallback；断连/版本变化拒绝 | `ExecuteAsync` 的 identity 字符串来源未经过生产 SCAN 合同；`FreezeActualRunIdentity` 固定 `Development/Diagnostic`，没有 rule/binding/plan hash 与生产事务的可追溯挂接。 |
| TCF/TBC | `Projects/TestSystemConfigurationDocuments.cs`、`ProjectWorkspaceService.cs` | TCF 引用 DBC/Controller Button 的 Revision/SHA；TBC 固定台架声明；当前 UI 选择 TCF/TBC | 两者由**全局可变 UI 选择**提供，不接受扫码冻结资源显式参数；TBC 不支持任意物理端口重映射，这是正确边界。 |
| CAN 码率 | `Hardware/HardwareConnectionService.cs` | ECU 20/50/100/125/250/500/800/1000 kbit/s；USB 0x42 设置、0x43 同会话读回；TEST/FLASH 预检校验；KEY CAN 不变 | 扫码配方没有目标速率；切换速率是设备实体配置操作，应仅在空闲、明确确认、读回后执行，不能扫码瞬间无感更改。 |
| FLASH | `Testing/ConfiguredRealTestFlashPort.cs` + `HardwareConnectionService.ProgramTestFlashAsync` | 从启用的 Flash 步骤冻结 XML/profile/firmware/security；单测试最大一次实体烧录；同 USB 会话；完整 SHA 授权；APP 会话后验 | `FlashSettingsDocument` 不是可直接执行的 Profile；扫码仅有固件 pin，**不足以运行 Flash**；批次最多一个 Flash，自动测试中不可无批准静默注入/改写步骤。 |
| Production | `Application/Production/{ProductionSession,ProductionPreflight,ProductionCommandHandler,ProductionWorkflow,ProductionResultCoordinator,ProductionStatisticsProjection}.cs` | 扫码→Match→Ready→Running 状态模型，Mock 不出厂，未 durable 不放行，繁忙拒绝 | 目前与 WPF、`RealTestWorkbenchService`、真实 RECORD 尚未装配；`PhysicalReleaseAllowed` 最终仍 false；无出厂审批真正落账。 |
| RECORD | `Records/Model/RunRecord.cs`、`Storage/RecordDatabase.cs`、`Queries/ProductionStatistics.cs` | 明确 `Production/Development/Diagnostic`；SQLite append/commit、steps、证据；按 mode 聚合 | `DistinctDutCount` 是所有该模式运行的 `COUNT(DISTINCT dut_id)`，**含 Fail/Interrupted，绝不可作为新增出厂数量**；缺唯一出厂放行登记/幂等约束和扫码匹配证明；同一项目批次各用例 RunId 独立。 |
| 证据报告 | `Testing/RealRunEvidenceRecorder.cs`、`RecordedRealRunEvidenceFinalizer.cs`、`Application/TestRecord/RealTestRecordSession.cs` | CAN/DBC/FLASH/WAVE 实际证据、缺失/丢帧保护、独立 RunId 的 HTML 与归档 | 未将原始扫码、规则/绑定决策、ProductionBatchId/AttemptId/Report aggregate 与真实 RunId 连通。 |

## 3. 必须正视的具体运行事实

1. `ProjectScanMatcher` 仅以 `RequiredModel/RequiredCustomer` 筛选配方，在一个工程中多个相同型号配方会返回 `CONFIG_AMBIGUOUS`；若缺真实编号绑定，不能靠“选第一个”解决。
2. 现有 `ScanMatchSettingsDocument.Published` 可以保存和发布，保存的工程资源具独立 SHA/revision；**发布文件不等于冻结整套执行所需依赖**，特别是外部 Flash XML、DLL、安全文件和有效生产配置。RuleSet 的 hash 基于 Rules JSON，不包含绑定表和配方完整语义。
3. `MainWindowViewModel` 当前把用户在焦点输入框输入的任何字符都当作 keyboard 来源；设备身份无法被该 WPF 行为认证。R1 首版应区分 `KeyboardWedgeUnverified` 和 `OperatorTyped/ManualPaste`；只有允许的模式才能作正式生产身份输入，并记录来源。
4. `TestProjectWorkbenchViewModel` 每个用例调用 `ExecuteAsync` 另起 RunId；必须增设生产 `BatchId` + `AttemptId`，不能假设单 RunId 覆盖整批。对每个用例完整证据分别归档，批次/放行政策在全部用例结束且 durable 后评估。
5. `RealTestWorkbenchService` 静态 TEST 预检和运行时再次检查是可复用基线；但它把编辑器选中的 TCF/TBC、波特率作为当前状态，扫码计划须保留**独立冻结来源**并在运行前确认实际状态等于预期；绝不能仅在 UI 点选用例后声称已按扫码配置执行。
6. `RunRecord.CanonicalSnapshotJson()` 有 project/station/plan/asset SHA 与 SessionIdentity，但当前缺 ScanEvidenceRef/RulePublicationHash/BindingRowId/BatchId/RecipeRevision。建议扩展正式 RECORD 事实/不可变关联附件，而不是 SCAN 创建第二张“成绩真相表”。
7. 当前 R1 的 `TestProjectWorkbenchViewModel` 会自动传入基于测试 plan SHA 生成的 `TestConsent`；生产模式必须审查并补上**明确操作员动作与安全许可的绑定**，不能把程序拼好的 consent 视为扫码后的实体操作授权。Flash 使用单独人工完整指纹授权，必须保留。
8. 现有 `ProductionPreflight` 里 `VerifiedSupply` 和 `AuthorizationScope` 是合同字段，不代表 Desktop 已有真实可用供电测量或授权界面；不能声称真正测得电源。

## 4. 当前测试证据边界

- `HOST/tests/AutoTest.Specialists.Tests/BarcodeRuleTests.cs` 验证焦点、短码、规则歧义、Revision、测试性重复输入、CRLF；**未验证实物扫描枪**。
- `HOST/tests/AutoTest.Application.Tests/ProductionOrchestrationTests.cs` 用 `FakeMatcher/FakeSink/FakeStageExecutor` 验证状态机、RunId、忙拒绝与不 durable 的安全路径；**未证明 Production 与 R1 实体 TEST 接线**。
- `HOST/samples/SCAN_PRODUCTION_MOCK_CASES.csv` 不是验收凭据；本专项不使用 Mock 冒充真实 DUT 证据。
- R1 文档 `docs/r1/R1_04_WINDOWS_FIELD_VALIDATION.md` 仍声明 Release Build / Windows WPF / 实体 ECU 未验证，严禁夸大。

## 5. 研究冻结边界

- 确定不新增 TEST DSL、不重做编译器/执行器、不更改 CAN 实体端口关系、不改固件、不更改 `main` 或 `R1`。
- 本研究将提出 **可独立版本化的规则/绑定/执行配方**、`FrozenScanRunPlan`、生产/研发隔离入口、正式 RECORD 归档与增量出厂事实的接口合同。具体见 S-01～S-03。
- 来源：上述精确源码路径在 GitHub `R1` 分支；再次实施时须锁定最新 HEAD 与重新比较修订，特别是本文件所用 `R1@fa3b18e`。
