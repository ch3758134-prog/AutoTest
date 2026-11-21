# AutoTest R1-SCAN S-03｜R1 最小真实扫码→配置准备→执行集成交接与验收合同 v0.1

> Issue #72；2026-11-21；基于显式回读 `R1@fa3b18e948043a9d1960adddb627658fd40c8159`。配套：`S-00_SOURCE_AUDIT_20261121.md`、`S-01_MATCHING_RUNPLAN_INTERFACE_CONTRACT_20261121.md`、`S-02_PRODUCTION_DEVELOPMENT_OPERATOR_FLOWS_20261121.md`。
>
> **状态：RESEARCH_AND_HANDOFF_DELIVERED / PRODUCTION_CODE_NOT_IMPLEMENTED / WINDOWS_AND_PHYSICAL_NOT_VERIFIED。** 任何生产功能须经 R1 负责人代码评审后进入 `R1`；本研究不修改 `R1`、`main`、固件和独立 UI 分支。

## 1. 实施目标和严格非目标

**最低可实测纵向切片**：使用真实客户外部编号与已发布编号规则→命中唯一独立绑定→获得版本化测试方案→解析当前工程中有序选定 TEST 项目条目（或一个测试程序）、TCF/DBC/Controller 按键/TBC、ECU CAN 目标波特率、可选单个真实 Flash 任务→显示给用户确认→确认设备与 0x43 读回→由**唯一 R1 TEST/FLASH/RECORD**执行并归档真实 RunId、Scan/Binding/Recipe/Plan 证明，防重和生产统计不能把复检视为“新增出厂”。

**本专项非目标**：不重构 TEST DSL / `RealRunCoordinator`，不另写 WinUSB/ISO-TP/UDS/Flash 实现，不在扫描时发送 CAN、KEY、IGN、夹具或写 Flash；不重新设计整套 WPF 工具窗口，不根据条码新建编号，也不默认自动发布出厂结果。

**必须回归的开发入口**：测试程序当前用例和单步、测试项目工程师勾选范围直接运行；`ScanMatchSettingsDocument` 不存在、扫描枪不接、扫描匹配配置有错误都不能阻止原有研发真实 TEST。开发测试依旧受原实体授权/实际 USB 约束；独立扫描并非它的预检项。

## 2. R1 实施批次与精确落点

| 批次 | 交付内容和文件落点 | 必须提供的证据/完成定义 |
| --- | --- | --- |
| **SCAN-P0-A** 资源/业务合同 | `AutoTest.Specialists/Scan/` 新增纯数据绑定表 + 发布版配方/匹配器；`AutoTest.Desktop/Projects/ProjectConfigurationDocuments.cs`、`ProjectConfigurationCodec`、`ProjectWorkspaceService.cs` 扩展配置资源种类/原子保存，兼容旧 scan-rules 只读 | 对外部编号唯一键、绑定表冲突、停用/废止、规则/绑定/配方 Revision/SHA、非法发布和资源指纹变更的单元测试；**绝不据 model 正则直接选择生产配方** |
| **SCAN-P0-B** 完整项目运行计划 | `ProjectScanMatcher.cs` 由仅 `Code/RecipeId/Identity` 转 `ScanResolution`；新 `ScanRunPlanCompiler` 解析 `TestProjectDocument.Packages` + 固定 EntryItemIds/次序、每个 TEST 实际 `PlanCompiler.Prepare`、TCF/DBC/KEY/TBC pins、Flash 一次性约束，生成不可变 `FrozenScanRunPlan` | 两个已保存用例按顺序生成相同可复核 plan SHA；缺/删/旧资源、不一致 TCF、误匹配均阻断；**不改** `ProjectRunSelectionState.RunSelected` 或持久 `TestProjectEntry.Enabled` |
| **SCAN-P0-C** 工位准备与显式确认 | `HardwareConnectionService` 只由生产准备动作明确调用既有码率选择/0x42→0x43；使用 `ConnectedStationEpoch`、`ConfirmedEcuCanBitrateKbps`；`ConfiguredRealTestFlashPort.FreezeJobs` 实际预检 XML/HEX/Key DLL | 扫码 Enter 不引发硬件命令；未读回/速率不符/epoch 变化拒绝；Flash 目标与 XML 速率一致；多 Flash 批次阻断；码率切换被 busy gate 拒绝 |
| **SCAN-P0-D** 复用 R1 真 TEST | 新 `ScanProductionRunAdapter` 调用 `RealTestWorkbenchService`；生产模式单独传入显式验证过的 `RunContext`，冻结 TCF/TBC、计划和身份；跟踪每个用例真实 RunId，原有开发 `ExecuteAsync` 保持行为不变 | 真 TEST 执行/终止安全逻辑完全来自 `RealRunCoordinator`、`ConfiguredStationActionPort`、`ConfiguredRealTestFlashPort`；不同用例分别产生 RunId、归属同一 Attempt/Batch；手动完整 Flash SHA 授权仍在真实后端校验 |
| **SCAN-P0-E** RECORD、复检与 UI 最小交付 | `RunRecord` 增加生产身份/来源上下文或关联不可变 ScanPlan Artifact；`RecordDatabase` 提供批次/放行耐久关系和幂等唯一约束；`MainWindowViewModel` + 精简扫码入口提供匹配详情/预检/确认/停止；Production 状态与真实运行服务桥接 | 生产模式唯一真实来源；提交后可重启追溯：扫码、绑定、规则/配方、用例 RunId、报告/原始证据；重复 DUT 的生产放行不可重复新增统计；开发运行仍 `Development/Diagnostic` |

**合入依赖**：A→B→C→D→E（可分 PR 静态评审），P0-B/C 的计划可以先独立以真实扫码输入显示匹配和预检，**在 P0-D/E 完成前不可标“正式生产测试已接线”**。每次合入先复核最新 R1 HEAD；研究分支与 R1 存在落后提交，不使用研究分支旧源码强行覆盖 R1 新改动。

## 3. 三层接口接线图（唯一 TEST 后端）

```text
[专用扫描输入框 / verified source policy]
    -> KeyboardWedgeCapture / BarcodeInputRouter
    -> ScanConfigurationResolver
        -> PublishedIdentityRuleSet
        -> PublishedDutConfigBindingTable  (DUT external key -> RowId -> RecipeRef)
        -> PublishedProductionRecipeV2
    -> ScanRunPlanCompiler
        -> ProjectWorkspaceService / Project pinned manifest
        -> TestProjectDocument ordered ItemIds -> TEST documents
        -> PlanCompiler.Prepare + TestRunStepScope.RequiredMappings
        -> TCF/DBC/Controller KEY/TBC, ECU CAN baud, FlashJob pinned actual files
    -> FrozenScanRunPlan (read-only, canonical SHA) + operator preview
    -> ScanStationPreflight + explicit rate-apply/readback + history/retest check
    -> Operator explicitly confirms PREPARED plan
    -> ScanProductionRunAdapter  [NOT a new TEST engine]
        -> RealTestWorkbenchService.ExecuteAsync (production-scoped context)
            -> RealRunCoordinator + ConfiguredStationActionPort
            -> ConfiguredRealTestFlashPort (separate full consent)
            -> RealTestRecordSession -> actual RunId + evidence/report
    -> BatchAttemptLink + ProductionReleaseDecision in RECORD
    -> Production statistics projection
```

**现有独立 ProductionWorkflow** 是可借鉴/复用的编排状态合同，但它的 `IProductionStageExecutor` 不能在实现时又创建第二种测试执行逻辑。其 `ProductionSession.Confirm` 预造的 `RunId` 不等于真实 R1 每个用例的 `RunId`；必须调整为 production `BatchId/AttemptId`，以真实 `RealTestWorkbenchService` 返回的每个 `RunId` 作为唯一运行事实键。绝不把两个 RunId 同名伪关联。

## 4. 生产/研发执行签名约束（建议新合同）

```csharp
// 明确区分扫描认证的身份与仅人工输入的开发编号。
public sealed record ProductionScanContext(
    Guid ScanEventId,
    string ExternalDutKey,
    string InputOrigin,
    string RuleSetSha256,
    string BindingTableSha256,
    Guid BindingRowId,
    string RecipeSha256,
    string FrozenRunPlanSha256,
    Guid BatchId,
    Guid AttemptId);

// 只用于生产入口；不允许通过字符串 DutIdentity 推断 Production。
public sealed record RunInvocationContext(
    RecordRunMode Mode,
    ProductionScanContext? Production,
    Guid? RequiredTcfAssetId,
    Guid? RequiredTbcAssetId,
    int RequiredEcuCanBitrateKbps);

// 仅当 Mode=Production 且 Production != null 且门禁已完成，才能归档 Production。
// 保留原 ExecuteAsync(TestDocument, confirmation, flashConfirmation, dutIdentity,...)
// 默认 Development/Diagnostic 行为，以避免破坏研发/单步路径。
public interface IScanRealTestBridge
{
    Task<ProductionCaseReceipt> ExecutePreparedCaseAsync(
        FrozenScanRunPlan plan,
        FrozenCase caseSnapshot,
        OperatorExecutionApproval testApproval,
        string? separateFlashApproval,
        CancellationToken ct);
}
// ProductionCaseReceipt 必须携带实际 TEST 返回的 RunId、正式 RECORD verdict、证据/报告状态。
```

- `RunInvocationContext` 不得让字符串来源、非生产扫码、已作废规则或复制粘贴自动升级为 Production；切换模式期间正在运行的上下文不可变。
- **实际授权**：`OperatorExecutionApproval` 为当前计划/操作者/设备 epoch 的明确批准；R1 当前 `TestProjectWorkbenchViewModel` 使用程序构造的 `TestConsent`，须由 TEST/安全负责者审查并给生产流程补真正可见的确认。Flash 仍必须使用现有完整 SHA 精确输入与硬件编程门禁。不得把发布配方或扫码成功本身作为批准。
- **TCF/TBC**：当前 `RealTestWorkbenchService` 从 `ProjectWorkspaceService.SelectedTestConfiguration` 读取可变 UI 选择。生产桥接须在每个真实 TEST 前验证期望 pinned TCF/TBC 与实际一致，理想改为显式 `RunInvocationContext` 的 scoped config，并由 TEST 工作区独占工位；**不可静默覆盖 UI 的研发选择**。
- **Plan freeze**：正向编排之前及每个用例前复核 manifest/file SHA、RuleSet/Binding/Recipe publication、Flash 原始文件 SHA、硬件 epoch、码率读回；任一失败丢弃计划并 BLOCKED，不能“试着跑”。
- **无 FLASH 任务**：Recipe `RequiresFlash=false` 时，启用步骤中的 Flash 必须为零；`true` 时全批恰好一项、与批准的 Firmware/Profile/Security 逐一相同。禁止扫码时向 TEST 动态插步骤。
- **异常状态**：TEST failure/fault/stop/未知实体副作用必保留实际既有 RunId 与原始证据；对应 Attempt 不得获得 release。无需运行后在 SCAN 自行“生成结果”来补洞。

## 5. 工程资源配置迁移

1. 保留原 `scan-rules` 文档（兼容旧资产/早期研发），新增 `DutConfigBindingTable` 独立资产 `scan-bindings` 和配方 V2 `scan-recipe-v2`；均使用已有项目资源版本/内容 SHA 原子保存能力。**不要**在原 `ScanRecipeDraft` 上默默塞默认 TCF/码率/用例子集使其看起来“发布可生产”。
2. 显式迁移向导读取旧 `ScanMatchSettingsDocument`，按已知 TestProgram/DBC/Firmware 填入候选草稿；缺 TestProject/TCF/TBC/FlashJob/绑定表/报告政策均标为 `NeedsOperatorReview`。不经人工确认不能发布 V2。
3. 独立绑定表可通过人工录入或业务编号清单导入，然后**编辑和发布为工程资源**，发布时检测全表唯一 DUT、RowId、RecipeRef SHA、状态和时间冲突；不把 CSV/Excel 作为现场依赖或测试验收依据。
4. 每份正式发布资源有 `Id/Revision/SHA`、发布者/发布时间/取消版本说明；历史引用保留旧快照或内容寻址归档，不能删除旧版后导致历史 Run 无法复盘。
5. 明确并测试**外部编号归一化合同**：带前导零的编号不可误变成数字、条码中的大小写/空格不可自动更正而与另一件 DUT 混淆；如出现统一规范化需求必须经过独立规则版本更新并验证无身份碰撞。

## 6. 首批应自动执行的合约/集成测试（不替代实测）

| ID | 测试条件 | 必须结果 |
| --- | --- | --- |
| S-A01 | 两条规则命中同一个原始码 | `IDENTITY_AMBIGUOUS`，无 RunPlan |
| S-A02 | 外部编号合法，但无绑定 | `BINDING_NOT_FOUND`，不能默认按型号选 |
| S-A03 | 同一 DUT 同时存在两个有效绑定 | `BINDING_AMBIGUOUS`，拒绝发布/运行 |
| S-A04 | 前导 0、大小写和截断编号 | 严格按发布身份规则，不发生静默串号 |
| S-A05 | 测试项目含三个有序 EntryId，配方只选 1、3 | 只冻 1、3，保留项目顺序和各自 TestProgram SHA；不修改工程师勾选 |
| S-A06 | 发布后 DBC/Controller/TEST/TCF/TBC 被修改 | 原 RunPlan 失效且输出定位字段 |
| S-A07 | Flash=true 但缺 XML/Security或启用两个 Flash；或 Flash=false 但有启用 Flash | 编排/预检 fail-closed，不进入设备动作 |
| S-A08 | 目标 ECU CAN 速率与工位读回不一致 | 需要显式应用/读回、`Ready=false`；扫描本身无设备调用 |
| S-A09 | 计划准备后更换 USB epoch，或生产运行中再次扫码 | 立即拒绝，不延迟执行，不替换 DUT |
| S-A10 | 同一 DUT 两次真实批次模拟记录行为（合同层） | 新 BatchId/AttemptId、每用例真实 RunId 全不同、历史可查询 |
| S-A11 | 同一 DUT 已被首次放行，再次成功复检 | 复检报告新增；首次放行件数不变，唯一键并发冲突安全 |
| S-A12 | 一批两用例第二个 FAIL/UNKNOWN/归档失败 | 没有 release，保留第一用例实际已提交证据、第三用例不启动 |
| S-A13 | SCAN 规则为空/扫描器未连接，工程师单用例、单步、勾选范围调试 | 原 TEST 路径依旧可使用，记录 Development/Diagnostic，非 Production |
| S-A14 | 人工输入/粘贴被伪作 scanner、或读取来源未验证 | 按生产来源策略拒绝/特殊审核，绝不隐式升级正式认证 |
| S-A15 | Flash 同一 Run 重试、Flash 授权缺失或不一致 | 由原 Flash 门禁阻断，不复用旧 consent，无二次擦写 |
| S-A16 | 扫码规则/绑定/配方与 TEST/RECORD 的 SHA 关联查询 | 根据真实 RunId 可反查唯一外部 DUT、规则、绑定、配方、项目/用例和报告 |

以上单元/集成测试可以用内存事实仓库覆盖纯算法/事务，但不得用 Fake TEST/模拟扫描枪对外声明现场 PASS。每个测试须在 CI/Windows Release Build 中运行；特别是 WPF 模式回归需要实际 Windows 启动。

## 7. 现场验证步骤（真实扫码枪 + 真实 R1 工位）

**先做无 Flash**：在真实扫码设备上读取**外部已存在**的 DUT 条码，验证捕获的字节序列/Enter/焦点，查看命中的绑定、项目、两个用例及配置，预检 ECU CAN 实际码率，用真实设备跑合法无 Flash 测试；每个用例完整 RunId、CAN/DBC/工位日志、RunRecord/报告与批次关联都能在软件重启后查看。紧接着验证无扫码直接选一个用例及单步骤运行不受 SCAN 影响。

**再做异常与复检**：真实枪重复扫描、运行中连扫、未知编号、资源发布后变更、同号复检、第二用例故意不满足判定、断开扫码枪/设备、报告写入异常，均应有可解释反馈与无新增出厂计数。涉及真实 ECU 破坏性测试/拔电/擦写错误注入必须由现场制定安全预案，不可自动进行。

**最后单独验证 Flash**：在现场明确批准、准确 HEX/XML/SafetyFile/目标 ECU、独立 SHA 文字授权的条件下，真实扫码将计划定为需 Flash；码率与 Flash Profile 一致，执行既有 TEST 内唯一 Flash 步骤，在 UDS/ISO-TP/Reset/APP 后验/后续 DBC 判定/正式归档上收集真实证据。Boot 完成/APP Session 成功均不自动认定软件版本正确或出厂 PASS。未批准授权时必须证明无实体擦写。

**现场记录必填**：Git HEAD、真实 scanner 型号/配置、扫码源/编号（报告可脱敏）、Rule/Binding/Recipe/Plan SHA、工位身份和 0x43 读回、项目/TCF/DBC/KEY/Flash SHA、实际 BatchId/AttemptId/RunId、步骤原始证据/报告、PASS/FAIL/UNKNOWN 与人工观察。**没有这些证据不能称现场 PASS。**

## 8. 与 R1、RECORD、UI 的接口审查点

1. **R1 负责人**：负责批准 SCAN 生产适配与 `RealTestWorkbenchService` 的显式 `RunInvocationContext` 以及计划/码率/TCF 会话门禁，绝不让 SCAN 自造运行器。
2. **TEST/RECORD 负责人**：批准 `RunRecord` 的 Production 身份证明与 BatchId/AttemptId/真实 RunId 关系、持久化事务、正式出厂放行记录和统计幂等约束；旧 `ProductionStatistics.DistinctDutCount` 不改含义，以“新增出厂”另建 release projection。
3. **FLASH 负责人**：批准真实 Profile/安全文件/HEX SHA 关联方式及每批最大一次 Flash、人工完整授权的不可跨用例复用策略。
4. **UI 负责人**：按照 S-02 的用户任务/状态设计轻量扫描操作入口、已发布配置管理、匹配预览/拒绝理由/复检确认；不得阻断现有工具/测试编辑工作区。
5. **项目管理**：确认生产“外部编号源”“按编号精确绑定”“出厂放行审批范围”“复检权限/原因”“是否允许人工输入代扫”等业务政策。未经确认采取 S-01/S-02 的**保守默认：精确绑定、人工确认、不自动放行、手工来源不得冒充实物扫码、复检不重复计新增**。

## 9. 交付结论

- 研究侧完成 **S-00 源码调查、S-01 完整数据/接口合同、S-02 用户流程与异常/统计策略、S-03 R1 实施交接与验收计划**；下一阶段需 R1 按此契约实施独立代码与现场实测。
- `SOURCE_REVIEWED` 不代表 `BUILD_PASS`，`MODEL_DESIGNED` 不代表 `R1_CONNECTED`，`SCAN_MATCHED` 不代表 `TEST_PASS`，`TEST_PASS` 不代表 `RELEASED`。
