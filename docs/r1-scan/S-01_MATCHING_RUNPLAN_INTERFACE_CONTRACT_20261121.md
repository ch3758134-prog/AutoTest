# AutoTest R1-SCAN S-01｜编号绑定→冻结运行计划→真实 TEST / RECORD 数据与接口合同 v0.1


> **2026-11-21 NI 对标修订提醒**：本文件的“R1 P0 逐外部编号精确绑定”只是第一轮**候审草案**，**不再作为通用 P0 默认模式**。依据 [S-04 NI 系统对标](S-04_NI_TESTSTAND_SYSTEM_RESEARCH_20261121.md) 与 [S-05 产品配置 v0.2](S-05_SCANNER_CONFIGURATION_AND_PRODUCT_UX_V02_20261121.md)，绑定关系可以基于当前工位任务、条码产品码、生产工单或逐个编号的权威档案，并明确各自验证级别。规则/关联/Recipe 分离、冻结版本、研发无需扫码等原则不变；**不得把本文件自行作为已批准出厂放行合同**。

**设计来源**：S-00 对 `R1@fa3b18e948043a9d1960adddb627658fd40c8159` 的静态回读；Issue #72。**状态：DESIGN_PROPOSED / REVIEW_REQUIRED / NOT_IMPLEMENTED**。本合同不把既有 `ScanRecipeDraft` 错当完整生产配方，不允许 SCAN 替换现有 `RealRunCoordinator`。

## 0. 决策与原则

1. **唯一 DUT 编号来自外部**。AutoTest 不派号、不生成条码、不根据扫描内容猜测硬件/BOM。保存原始扫描值和严格、可解释的归一化值；默认保留前导 0、大小写、所有有效字符，禁止悄悄 `Trim/ToUpper`；若允许规范化，需由已发布的规则明确声明并做归一化冲突检查。
2. **身份规则、独立 DUT 绑定表、生产配方是三个对象**，各自版本、发布状态和 SHA；扫码信息只用于定位绑定，不能通过 `model` 正则提取直接推定可执行配置。研发可完全绕过这三个生产对象。
3. **默认一个 ECU 工程对应受控的 ECU 型号/硬件版本范围**。禁止无审批的跨工程自动切换。当前打开的工程与绑定目标不符就拒绝并提示切换工程。
4. **一条匹配只允许得出一个、显式冻结的计划**。规则多命中、绑定冲突、配置资源失效、引用含糊、无预期用例、含未知烧录资源时 fail-closed；不得按文件排序选第一个。
5. **扫码/匹配没有任何真实 IO 权限**。应用波特率属于工位配置阶段的显式受控操作；TEST/FLASH 各自沿用真实授权路径，尤其 Flash 不可通过 `RequiresFlash=true` 获得任何特权。
6. 计划确认后任意变更编号/工程/规则/绑定/资源/选择/设备 epoch/码率必须废弃冻结计划重做预检。**研发临时勾选**和**生产冻结子集**互不修改。

## 1. 对象模型：四层职责与主键

| 层 | 建议工程资源/合同 | 持久键与冻结内容 | 负责什么 / 不负责什么 |
| --- | --- | --- | --- |
| 捕获 | `ScanCaptureEvidence` | `ScanEventId`、原始文本、来源、焦点状态、UTC+单调时钟、扫描器接入策略、输入哈希 | 采集，不推断可信型号，不调用 TEST。 |
| 编号规则 | `PublishedIdentityRuleSet`（既有 scan-rules 迁移或分离） | `RuleSetId`、`Revision`、`Sha256`、`RuleId`、命名字段/校验/归一化、发布时间 | 决定合法性及规范化的 `ExternalDutKey`；不得自造序列号。 |
| 独立编号→方案绑定 | **新增** `DutConfigBindingTable`（独立项目资产，不嵌入 Regex） | `TableId/Revision/SHA`、每个 `RowId`、`ExternalDutKey`、`RecipeId/RecipeRevision/RecipeSHA`、生效/停用状态、必要身份/硬件版本、审批人/时间 | 确定某个已被外部生成的 DUT 要使用哪个方案；不会执行。 |
| 版本化执行配方 | `ProductionRecipeV2`（改进既有 `ScanRecipeDraft`） | `RecipeId/Revision/SHA`、工程/项目/用例/TCF/TBC/控制资源及 FLASH 锁定引用、码率、报告政策 | 决定**运行哪些已保存 TEST**，不是第二套步骤程序。 |

**R1 P0 绑定粒度：逐个外部编号精确匹配**。绑定表可以从业务清单批量导入、可搜索/编辑/校验/发布；CSV 仅可作为一次性导入源，不是运行时依赖或“真实扫码验收”。未来若要“按外部批次/型号范围自动分配”，需单独审批范围、有效期、优先级、冲突测试；**不可默认退化为型号 Regex 选配方**。相同 ExternalDutKey 在单个当前有效发布快照内最多一个绑定；同一规则可能解析出多个字段，但唯一身份必须与外部编号一致且可全局查重。

## 2. PublishedIdentityRuleSet 与 BindingTable（必需字段）

`PublishedIdentityRuleSet`: `SchemaVersion`、`RuleSetId`、`Revision`、`Publication(Draft/Published/Retired)`、`Sha256`、`Rules[] {RuleId, Pattern, IdentityKeyContract, NamedFields, NormalizationPolicy, OptionalCheckDigit, Publication}`、`PublishedAtUtc`、`Approver`。正则必须完整匹配；多命中失败；不允许任意截取无法保证唯一性的字段当 DUT ID；结果同时保存解析值/原文/命中 RuleId。

`DutConfigBindingTable`: `SchemaVersion`、`TableId`、`Revision`、`Publication`、`Sha256`、`ProjectId/ExpectedProjectSha`、`Rows[]`、`PublishedAtUtc/Approver`。每行至少：
- `RowId`：稳定唯一值；`ExternalDutKey`：来自外部清单的规范化唯一编号；`RecipeRef {Id, Revision, SHA}`；
- `ExpectedModel`、`ExpectedHardwareRevision`（若业务已提供）、`ExpectedCustomer/ProductionOrder`（可选），`State=Active/Disabled/Revoked`；
- `EffectiveFromUtc/EffectiveToUtc` 可选，生效范围显式；`Reason/SourceDocumentRef/UpdatedBy` 为审计；`RowRevision` 增量修改；
- 只有一条**发布且有效**绑定匹配才能继续；重复、撤销、旧版、时间无效均明确拒绝。型号/硬件版本若来自条码，只作交叉检查，不能替代绑定表的已审批属性。

绑定表修改采用“草稿编辑→静态冲突检测→发布全量快照”的只读运行策略；发布之后不能原地修改已发行快照，形成新 Revision/SHA。已经确认的 RunPlan 永不追随“最新”。

## 3. ProductionRecipeV2 字段（当前缺失的核心合同）

| 字段 | P0 约束/来源 | 与 R1 当前代码关系 |
| --- | --- | --- |
| `ProjectId, ProjectRevision, ManifestSha256` | 等于当前工程；型号/硬件版本范围人工确认 | `OpenProjectContext` / `ProjectSnapshot` 已有主键/SHA |
| `TestScope` | **优先** `TestProjectAssetPin` + 精确、有序 `EntryItemIds[]`；可选 `SingleTestProgramPin`，二者恰有一个 | `TestProjectDocument.Packages` 有 `ItemId`、`PackageAssetId/Revision/SHA`；顺序按项目位置；不能借临时 `RunSelected` |
| `Cases[]` | 展开后逐条固定 `EntryItemId, ProgramAssetPin, PreparedPlanFingerprint`；空集合拒绝 | `PlanCompiler.Prepare`，每用例 RunId |
| `TcfAssetPin` | 必需；固定 ECU model/hardware + DBC/Button pins；不使用 UI 残留选择 | `TestSystemConfigurationDocument`；现有生产适配需增加 scoped context |
| `TbcAssetPin / StationContract` | 固定已批准 TBC 与设备型号/会话身份；不得更改 ECU/KEY 物理端口 | 单台架 `TestBenchConfigurationDocument` |
| `DbcPin / ControllerButtonPin` | 应与 TCF 和每个 TEST 引用全部一致；按键是20位 mask→5路模拟电压 | `TCF`、`RequiredMappings` 的版本检查已有 |
| `EcuCanBitrateKbps` | 必需且仅可为 20/50/100/125/250/500/800/1000；与 Flash XML 速率一致 | `HardwareConnectionService.SelectRequested.../ApplyRequested...` 同 epoch 0x43 读回 |
| `RequiresFlash` | 显式 true/false；若 true 则冻结测试用例中的**一个已启用 Flash 步骤**及相应文件；若 false 则禁止启用 Flash 步骤 | `ConfiguredRealTestFlashPort.FreezeJobs`：一次真实用例最多一个 Flash；批次最多一次 |
| `FlashJobPin` | `StepId`、`ProgrammingProfileXml` + `SettingsJson`（如有）+ `Firmware/HEX` + `SecurityLibrary/Key` 的完整路径身份、大小和 SHA、冻结 JobFingerprint、目标 ECU、APP/Boot IDs/速率 | 仅 `FirmwareAssetPin` 不够；`FlashSettingsDocument` 不等于真实 CanToolFlashJob |
| `ReportPolicyPin` | 生产允许判定的**必需用例/步骤/证据**、是否要求独立出厂复核、是否可复检、统计维度 | 由 RECORD/Production 定义，不得仅凭扫码或 Flash 完成 PASS |
| `RecipePublication` | 发布状态、`RecipeRevision/SHA`、发布者/时间 | 现有 `ProductionRecipe` 只携带较少字段且 bridge Revision 恒为1 |

**禁止的实现**：生产匹配过程中修改已保存 TEST 程序的 Flash 步骤属性、隐式插入烧录步骤、修改项目 Enabled、未经验证将 `ScanRecipeDraft` 旧数据静默转换成可生产执行的新合同。旧文档可保留用于研发；正式生产发布必须补齐 v2 后显式升级审核。

## 4. FrozenScanRunPlan（仅内存运行对象 + 不可变审计内容）

建议字段层次（抽象合同，不是已实现的 C# 类型）：

```text
FrozenScanRunPlan {
  PlanId, PlanSchemaVersion, CanonicalPlanSha256, CreatedUtc, ExpiresUtc?
  Intent = Production
  Capture { ScanEventId, RawTextOrProtectedRef, Source, Focus, CapturedUtc, CaptureEvidence }
  Identity { ExternalDutKey, ParsedFields, RuleId, RuleSetId/Revision/SHA, IdentityVerifiedByPolicy }
  Binding { TableId/Revision/SHA, RowId/RowRevision, EffectiveState }
  Recipe { RecipeId/Revision/SHA, PublicationEvidence }
  Project { ProjectId/Revision/ManifestSHA, EcuModel, HardwareRevision }
  Test { TestProjectAssetPin?, OrderedEntryItemIds[], FrozenCases[
           EntryItemId, TestProgramAssetId/Revision/SHA, PreparedPlanSHA, HasFlash ] }
  Config { TcfPin, TbcPin, DbcPin, ButtonControllerPin, OtherUsedPins[] }
  Station { ConnectedControllerIdentity, StationEpoch, RequiredEcuCanKbps,
            AppliedAndReadbackKbps, ReadbackEpoch, FixedKeyCanKbps=500 }
  Flash { Required, StepId?, ProfileXML/SHA?, SettingsJSON/SHA?,
          Firmware/SHA?, SecurityProvider/SHA?, TargetLabel?, FrozenFlashJobSHA? }
  Recording { ReportPolicyPin, AttemptId, BatchId, RetestReason?, ParentBatchId? }
  Preflight { Diagnostics[], PreparedAt, ChecksFingerprint, BlockingCodes[] }
}
```

- 项目/测试资源的 pins 一律 `AssetId + Revision + SHA256`；外部 XML/DLL/Key/HEX 必须以实际**字节 SHA**封存，不把路径、文件名当版本证明。Seed/Key 内容及算法机密不可写入公开日志。
- `CanonicalPlanSha256`：对固定字段进行确定性序列化（有序数组，稳定键顺序、统一字符编码、UTC），SHA256。扫描原始密文/隐私字段可以通过安全引用+哈希进入指纹，但显示中须脱敏。**不包含可变操作员授权文本**。
- 编号可以在测试开始前换成另一个，只能丢弃旧计划重建；准备后 `StationEpoch`、项目 manifest/依赖 hash、TCF/TBC、当前 0x43 速率、Flash Job、扫描确认操作者任何变化均作废。每个单用例执行前复核实体状态、文件内容和 hash。
- `BatchId`（此次扫码批次）与 `AttemptId`（对某 DUT 的一次生产复/首检尝试）只是编排相关键；**真正的步骤和测量属于每个用例各自唯一的 `RunId`**。需要 `BatchId -> ordered [CaseEntryId, RunId, Verdict, ReportArtifact]` 的不可变关联证明。不得让 `ProductionSession` 自己先造一个 RunId 而后与实际 `RealTestWorkbenchService` 的 RunId 脱节。

## 5. 建议服务接口（R1 实施时按实际类型调整）

```csharp
// SCAN 只做数据；必须是纯匹配，不得调用设备或执行器。
public interface IScanConfigurationResolver {
    Task<ScanResolution> ResolveAsync(
        BarcodeScanEvent capture, ProjectSnapshot expectedProject,
        CancellationToken ct);
}
// 生成明确定义、顺序稳定、按内容固定的 TEST 资源引用。
public interface IScanRunPlanCompiler {
    Task<PlanPreparation> PrepareAsync(
        ScanResolution resolved, CancellationToken ct);
}
// 融合 R1 实体设备状态：只预检；若需要应用码率，应作为独立的受控用户动作。
public interface IScanStationPreflight {
    Task<PreflightReport> CheckAsync(
        FrozenScanRunPlan plan, CancellationToken ct);
}
// 只在明确确认后复用同一个 R1 TEST 服务、Flash安全门禁及 RECORD。
public interface IScanProductionRunner {
    Task<ProductionBatchReceipt> RunPreparedAsync(
        FrozenScanRunPlan plan, OperatorExecutionApproval approval,
        CancellationToken ct);
    void RequestStop();
}
// 真实 TEST/RECORD 返回的 RunId 是权威来源；不得再做内存成绩库。
public interface IScanProductionTraceability {
    Task AttachPlanEvidenceAsync(
        RunId actualRunId, FrozenScanRunPlan plan, Guid caseEntryId,
        CancellationToken ct);
    Task<ProductionReleaseDecision> EvaluateReleaseAsync(
        ProductionBatchReceipt batch, CancellationToken ct);
}
```

`ScanResolution` 应包含 `Code/Explanation/ParsedIdentity/BindingRow/RecipeRef/CandidateIds/Conflicts`，仅在唯一命中时 Allowed。 `PlanPreparation` 包含阻断项和待用户确认项；生产模式不允许把 Unknown 当警告继续运行。

**最小实现接入点**：
- `Specialists.Scan` 拓展纯规则、绑定表匹配与测试，无硬件接口；
- `Desktop.Projects` 新建绑定表类型/Codec/编辑存储，严守 `ProjectWorkspaceService` 现有 SHA、Revision 和原子保存；
- `Desktop.Testing` 新建 plan resolver/runner adapter，复用 `TestProjectDocument`、`PlanCompiler`、`TestRunStepScope`、`RealTestWorkbenchService`；后者新增显式绑定生产上下文/选定 TCF/TBC 而**不修改现有研发调用默认行为**；
- `Application.Production` 管理确认/重试/工位状态，不建立第二 TEST interpreter；
- `Records` 以 `RunRecord` + `RunStepFact` + ArtifactRepository 为事实源记录扫描证明和批次/放行，不能仅用 `ProductionStatistics.DistinctDutCount` 代表新增出厂。

## 6. 预检不变量与拒绝码（必须结构化而非文字匹配）

| Gate | 必须检查 | 推荐代码 |
| --- | --- | --- |
| Capture | 非空、长度、控制字符、焦点、来源、忙态/连续扫 | `SCAN_EMPTY/LENGTH/FOCUS/SOURCE/STATION_BUSY` |
| Identity | 规则集已发布且 SHA 正确、全串唯一命中、外部编号不变 | `SCAN_RULE_UNPUBLISHED/IDENTITY_AMBIGUOUS/IDENTITY_INVALID` |
| Binding | 有且仅有一个有效 Row、身份字段一致、绑定资源已发布 | `BINDING_NOT_FOUND/AMBIGUOUS/REVOKED/STALE` |
| Recipe | 唯一、已批准、包含完整 TEST/TCF/TBC/Flash/Report pins | `RECIPE_NOT_PUBLISHED/RECIPE_INCOMPLETE` |
| Scope | 项目 SHA、EntryId+程序 SHA、有序非空、所有用例可编译 | `PROJECT_STALE/CASE_STALE/CASE_SCOPE_EMPTY` |
| Config | TCF↔DBC/Controller↔实际 TEST mappings；硬件版本符合 | `TCF_DBC_MISMATCH/KEY_CONFIG_MISMATCH/ECU_VERSION_UNKNOWN` |
| Station | 设备接通/未占用/安全状态、同 epoch 0x43 读回码率 | `STATION_BUSY/ECU_CAN_BAUD_UNCONFIRMED/STATION_CHANGED` |
| Flash | 需要与启用步骤一致、一次性约束、XML/HEX/Security/目标/速率 SHA 一致 | `FLASH_POLICY_MISMATCH/FLASH_JOB_STALE/FLASH_CONSENT_REQUIRED` |
| Record/retest | 历史同编号检索、运行次数/放行、复测理由与权限、归档可写 | `RETEST_APPROVAL_REQUIRED/RELEASE_ALREADY_EXISTS/RECORD_UNAVAILABLE` |
| Execution | 计划 hash/项目 hash/设备 epoch/用例 hash 复检、真实授权在场 | `PLAN_INVALIDATED/OPERATOR_CONFIRM_REQUIRED` |

**应用速率**：匹配得出 `RequiredEcuCanKbps` → 显示目标及当前读回 → 在工位空闲由操作员明确“应用/读回” → `HardwareConnectionService` 执行 0x42/0x43 → 与 plan/设备 epoch 一致才可 Ready。扫码输入本身绝不触发 0x42。

## 7. 正式统计与重复出厂的最小一致性

- **ScanEvent** 可以重复；**AttemptId/BatchId/RunId** 每次实际执行都新建；同一 `ExternalDutKey` 跨多次运行保持业务同一身份。运行一批多个用例各自 RunId，均必须挂同一 BatchId/AttemptId。
- `Production` 模式只在规则/绑定/工程有效、全部必须用例真实 PASS、证据全部 durable、批准的生产放行策略复核后，才能登记独立 `ReleaseDecision`。这里不能把 FLASH PASS、单用例 PASS 或一条 `RunRecord` 当作自动出厂。
- `ReleaseDecision` 幂等键建议为 `Project/ProductionOrder/ExternalDutKey/ReleaseScope`（生产范围需业务批准）；同一键第二次测试只能追加 `RetestAttempt`，**不可再次增加首检出厂件数**。需要复放行时保留独立变更审批/引用原 ReleaseId，不创造第二个“新增”。
- 统计最少区分：扫描次数、有效唯一身份数、测试批次数、测试 Run 数、首次出厂放行 DUT 数、复检批次数、FAIL、INCONCLUSIVE、ABORTED、待人工复核。所有计数基于耐久的 RECORD 与 ReleaseDecision 而非 UI 本地累加。
- `ProductionStatistics.GetSummaryAsync` 现有 `COUNT(DISTINCT dut_id)` 包括失败 DUT；R1 禁止将它直接显示为“新增出厂数量”。

## 8. 与 mature products 的职责对齐（参考而非照搬）

NI TestStand 区分 UUT 序列号、测试执行与结果/报告日志（见 NI 官方 UUT_RESULT 与 Process Model 文档）；Siemens Opcenter 的序列化追溯强调工艺、设备、产品、试验记录和返工关系；这些支持本方案把识别/计划/结果分别建模。AutoTest 只做一个本地离线单工位工站，无需引入完整 MES/跨产线调度。参考：
- https://www.ni.com/docs/en-US/bundle/teststand/page/uut-result-table-schema.html
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html
- https://www.siemens.com/en-us/technology/lot-traceability/

## 9. 需要 R1/管理审查而不是猜测的项目项

1. 首版外部编号绑定是否采用“逐编号精确绑定”（本专项**建议确定为 P0 默认**）；如量产需要范围级自动绑定，必须补客户的权威版本/工单边界及批准。
2. 出厂批准是否独立于测试 PASS（**默认分离，绝不静默自动放行**）；ReleaseScope/复检授权人需生产业务提供。
3. 生产需要的 XML/安全 DLL/固件是否全部工程化存储；在此之前按外部文件实际 SHA 固定并强制重验。
4. `ECU 型号/硬件版本` 的真实核验渠道（仅外部映射还是 ECU DID/标签人工复核），不假定扫码内容自动证明实物。
5. `RealTestWorkbenchService` 生产记录模式和 batch association 的签名改动，由 R1/RECORD 审查；不能以本专项文档等同源码已实现。
