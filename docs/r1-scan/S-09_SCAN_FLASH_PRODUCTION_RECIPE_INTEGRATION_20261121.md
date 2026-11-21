# AutoTest R1 SCAN S-09｜扫码 × 自动烧录 × 自动 TEST 一体化配置与执行研究 v0.1


> **2026-11-21 用户明确变更／本 S-09 相关部分已被新要求取代**：正式 AutoTest 不采用“导入 CanTool Configuration.xml 作为烧录配置”的产品操作方式；必须在上位机原生编辑烧录参数并持久保存在当前 ECU 工程内的固定位置，正式执行不能引用/生成外部 XML；固件镜像和必要安全算法仍作为可受控复制到工程内部的资源。有关存储方案、源码接线、向后兼容和验收要求请以 [S-10｜原生烧录配置与工程固定保存位置](S-10_NATIVE_FLASH_CONFIGURATION_FIXED_PROJECT_STORAGE_20261121.md) 为新约束。S-09 关于“FLASH 作为 Recipe 一部分、独立授权、软件版本验证、真实 CanTool 后端不重写”的结论仍有效。

> 2026-11-21 / Issue #72 / 研究分支 `research/r1-scan-configuration-20261120`
> 
> 用户提出：**烧录配置如何与扫码方案配合，必须一起考虑**。本报告是对 S-07、S-08 的正式研究补充，单独考察真实 `ConfiguredRealTestFlashPort`、`CanToolFlashJob`、`CanToolFlashWorkflow`、`HardwareConnectionService.ProgramTestFlashAsync`、`RealTestWorkbenchService`、`TestProjectWorkbenchViewModel` 和 `FlashSettingsDocument`；本轮最新 `R1@8e6aaa556e70ce025a18c20abeb331fa812b055d`（各文件 SHA 详见最终回读）。**候审方案，没有更改 R1/主线/固件；未验证用户的实物设备或 ECU 结果**。

## 1. 核心结论（研究建议）

**一只 ECU 的正式生产测试方案不仅包含 TEST，还必须包含“软件目标 + 烧录策略 + 可执行 FLASH 包 + 烧录后身份/版本验收 + 记录策略”。** 条码只是定位这一方案的索引；不能在扫码枪正则规则中维护 HEX 路径，更不能“扫到型号就用当前目录最新固件”。

推荐决策链：

```text
ScannerInput (物理设备已验证) -> BarcodeSchema(例如 UT1) ->
DutSerial + ProductCode + HardwareCode [标签声明]
    + ActiveProductionTask / WorkOrder [明确生产上下文]
 -> ProductVariantRegistry（产品/HW 合法）与允许本任务的产品范围
 -> PublishedRoutingBinding（ProductVariant + TaskScope -> 唯一 RecipePin）
 -> ProductionRecipeVersion（统一版本化 TEST + FLASH + REPORT）
      ├─ TestProject + ordered TestCaseEntryId + TestProgram SHA
      ├─ TCF / DBC / 20KEY / TBC / ECU CAN目标速率
      ├─ FirmwareReleasePin（软件发布号/目标版本/映像SHA/适用硬件范围）
      ├─ FlashProfilePin（CanTool XML + 可选JSON + 安全DLL + 正确CAN APP/BOOT端点与擦写范围）
      ├─ FlashExecutionPolicy（禁止/本次必烧/已发布条件检查/仅研发）
      ├─ PostFlashVerification（软件版本/DID/APP恢复的证据策略）
      └─ FormalResultPolicy（测试合格与出厂放行分离）
 -> FrozenUutRunPlan / FlashJob SHA / 完整资产SHA / station epoch
 -> 先只读预检，再明确授权
 -> 单一 R1 TEST/CanTool Flash/RECORD 后端
 -> InspectionSession → 一个或多个实际 CaseRunId → Flash Evidence / Test Evidence / ReleaseDecision
```

**标识区分**：`DutSerial` 是外部唯一实体编号；`ProductCode+HardwareCode` 是标签声明；`FirmwareReleaseId` 是当班目标软件发布；`FlashJobFingerprint` 是具体执行字节+协议配置的 SHA；`CaseRunId` 是实际 R1 运行事实。任何一类都不能互相代替。

## 2. 为什么不能把烧录文件写进条码

- 同一产品/HW 的不同客户、生产任务、返修批次、出厂工艺可能要求不同软件发布；永久标签不能因为固件升级重印。
- 标签写 `RequiresFlash` 会把产品属性和工艺决策混淆：已写正确版本的返修机、空白机、验证失败的机型都不能仅凭标签决定是否擦除。
- 条码若直接写 `HEX 路径`、`Safety DLL`、CAN ID 等动态环境参数，既无法保证文件真实版本，又会在换电脑/调整编程包后失效。
- **正确匹配键**：`(已声明产品型号/HW + 工单/当前任务 + 已发布软件目标) -> 唯一版本化 Recipe`。已批准任务明确目标 FirmwareRelease；Recipe 引用文件实际 SHA；扫码只提供 DUT 身份，绝不自动从不受控目录按“最新时间”选 HEX。
- 例如 `UT1-EPBA-H02-000000123456-4372` 的 ProductVariant=EPBA/H02，在当班任务 `WO-A` 选择 `FW-R5`，在返修任务 `REPAIR-A` 可能选择 `FW-R6`。两者相同条码不改变物理身份，执行历史记录各自 Recipe/FlashJob 版本。

## 3. 研究后的真实源码能力矩阵（重要，切勿误称全部可配）

| 资产/入口 | 当前 R1 实际行为 | SCAN 设计缺口/风险 |
| --- | --- | --- |
| `ProjectConfigurationDocuments.cs/FlashSettingsDocument` | `SchemaVersion 1` 元数据：Target、ExpectedEcuIdentity、FirmwareAsset、UDS ID、P2/P2*、Security、Erase、Verify、MemoryRanges。明确注释 `Not a ProgrammingProfile and not a flash execution authorization`，且 `ApprovedByOperator=true` 被拒绝 | **不能**把它当已经被 TEST 可执行器自动消费的发布 FLASH Job。需明确定义可执行 `FlashProfileAsset`/BundlePin，并将旧配置作为草稿或迁移来源 |
| `ConfiguredRealTestFlashPort.FreezeJobs` | 从**现有启用的 TEST `Flash` 步骤**读取 `programmingProfile` (XML)、`firmwarePath`、`securityLibraryPath`、`targetLabel`、可选 `settingsPath`、`binBaseAddress`；准备成真实 `CanToolFlashJob` | Recipe 不能只引用 HEX 或 `RequiresFlash`，必须和真实 TEST 步骤绑定，检查二者 **ExactFingerprint** 一致；不能 SCAN 自己临时加或删物理步骤 |
| `CanToolBundleDiscovery` | “三文件”扫描实际为 **Configuration.xml + 固件 HEX/S19/BIN + SafetyFile.dll**；可在当前文件目录发现 | 用户若另外有独立“密钥文件”，不能擅自认作已实现的第四文件/通用秘钥 provider；先以真实版本和格式确定，再扩展配置 |
| `CanToolFlashJob.Freeze` | 冻结 APP/BOOT CAN ID、FlashStart/End、参考波特率、块大小、实际数据块、HEX/S19/BIN SHA、安全库 SHA、执行指纹；当前 XML 编程 BaudRate 只接受 250k/500k；只批准已明确核验的安全算法实现 | 必须逐个产品/HW/软件目标验证支持度。**主控 0x42/0x43 支持多速率不代表现有 CanToolFlashJob 支持任意速率或任意 ECU 安全算法** |
| `CanToolFlashJob` 针对 HydEPB 特定项目 | 与参考 CAN ID、内存范围和 0x200 块完全匹配时会启用受实测来源限定的尾部标记块 | 这是**特定 OEM 配置**，不许用 ProductCode 路由给其他 ECU 套用；变型绑定需核对内存布局及具体擦写边界 |
| `HardwareConnectionService.ProgramTestFlashAsync` | 复用当前主控 USB 和 ECU CAN，要求空闲，当前 ECU CAN 码率由 `0x43` 确认，且与 Flash XML 码率相等；验证 firmware SHA、安全 DLL SHA、独立完整 Consent，拦截未经授权写入 | 不能只因扫码匹配就直接切换 0x42/写 ECU；码率申请/确认在用户明确许可的工位准备阶段进行；FLASH 不单独开 USB 会话 |
| `ConfiguredRealTestFlashPort.ExecuteAsync` | 同一 TestDocument 的一个 Flash 步骤只能尝试一次（循环拒绝）；一个有序 TestProject 范围总体最多一次 Flash。开始前清理/释放周期 CAN/按键；成功后 APP 会话确认才允许重新授权 TEST 写入 | 匹配出来的 TestProject 应验证该 Flash 步骤位置、数量和执行次序；扫描后不能悄悄复制/插入 Flash 步骤 |
| `CanToolFlashWorkflow` | 对既有参考 ECU 通过 UDS Boot 进入→解锁→擦除→下载→验证→Reset。自动 TEST 场景补充 APP 10 03/50 03 会话恢复探测 | **APP 50 03 不是软件版本确认**；需要 TEST/UDS 对正确型号/软件版本/DID 的明确观测判定（若 ECU 支持） |
| `RealTestWorkbenchService` | 真测试、真实记录、文件证据和不同用例 RunId 可用；含 DUT 编号的普通运行仍记 `Development` | 需要审查一个经过扫码计划授权的 Production 上下文，将 Flash job/目标版本/读回证据绑定同一 InspectionSession，而非复制生产执行器 |
| `ProgrammingProfile.cs` 旧类型 | 当前文件明确 `MockOnly=true` 且真机模式 `NotSupported` | **不能**误把此旧 ProgrammingProfile 当作已支持实际烧录的版本化方案；真实集成必须绑定 `CanToolFlashJob` 及其已核验物理路径 |

## 4. “烧录配置”本身要分成三个对象，不要一张大表全混

### 4.1 固件发布 FirmwareRelease

```text
FirmwareRelease {
  FirmwareReleaseId, TargetProductVariantRange,
  SoftwareVersionLabel, ImageAssetId, ImageSha256,
  ImageFormat(HEX/S19/BIN), ReleaseStatus, Revision, ReviewAudit,
  ExpectedPostReadback? (DID+内容格式+匹配规则), CompatibilityConstraints,
  Source/Provenance, PublishedUtc
}
```

含义：**这版软件是什么、适用哪些硬件、工厂想写入哪一个经批准的版本**。软件版本标签和 SHA 要同时保存，标签用于人看，SHA 用于文件不可变核对。仅有 `FW-v3` 文本不足以证明目标固件。

### 4.2 烧录方法 FlashProfile/ProgrammingBundle

```text
FlashProfile {
  ProfileId, Revision, Published, ApprovedTargetProducts/HW,
  CanToolConfigXmlSha, RelatedJsonSha?, SecurityLibrarySha,
  AppTx/Rx, BootTx/Rx, ExtendedFrame,
  ConfiguredEcuCanKbps, FlashStart/End, BlockLength,
  SecurityProviderKind + TrustLevel, ProtocolWorkFlowKind,
  PostResetAppProbeConfiguration, CompatibleFirmwareRules,
  BundleFingerprint, OperatorReviewAudit
}
```

含义：**怎样给这类 ECU 安全地擦写**。保存当前 CanTool 可执行模式中实际使用的 XML/相关 JSON/SafetyFile 或安全提供器，不能把“KEY 文件”“SafetyFile”和 Seed/Key 算法简单等同；不要在审计 UI 或日志打印 Seed/Key。

**实机配置前置**：具体 ECU 的 APP/Boot ID 必须来自已核对配置；用户原 HydEPB Boot 请求 `18DA50F1`、应答 `18DAF150` 只适用于其真实 profile，不可转发所有其他 ECU。关于擦除/下载/签名尾块等 OEM 行为，都应与 Profile 和产品兼容表严格绑定。

### 4.3 烧录执行策略 FlashPolicy（最关键）

| 策略 | 什么时候选 | 实际执行方式与风险 |
| --- | --- | --- |
| **NoFlash** | 只做测试/出厂复检，不允许本次编程 | 冻结计划中**不得**包含可达的启用 Flash 步骤；如果存在则预检拒绝 |
| **FlashRequired** | 生产指定固件必须写入 | 计划中恰有一次正式 Flash 步骤，独立安全授权，执行后验证 APP 与目标软件，再功能测试 |
| **VerifyThenConditionalFlash**（后续） | 当前 ECU 可能已经写了正确固件 | 必须有**可信、已实现且已批准的只读版本校验**和两份**预先发布**的可选 TEST 范围（Flash/NoFlash）；不能仅用“上次同编号测过”或 APP 50 03 就跳过 |
| **EngineerFlashOnly** | 研发/维修手工烧录 | 人工使用独立 Flash 工具或明确 TEST Flash 步骤；记录为研发/维修，不能当正式生产放行 |

**重要的现场建议**：首版只做已验证 `NoFlash` 与 `FlashRequired` 两种明确模式；`VerifyThenConditionalFlash` 的“可跳过”判据、旧 ECU 状态恢复、真实 DID 读取没有证据前**不可默认启用**。不要通过“SCAN 匹配到此机已测过”跳过 Flash。

### 4.4 三者如何合成执行配置

```text
ProductVariant + WorkOrder/ActiveTask
  -> ProductionRecipe
      FirmwareReleasePin = ReleaseId@Revision/SHA
      FlashProfilePin = ProfileId@Revision/SHA
      FlashPolicy = Required / Forbidden / ...
      TestProjectPin + TestCaseEntryIds
      ExpectedVersion/Identity Readback Policy
  -> FlashJob = CanToolFlashJob.Freeze(...)
      Assert ImageSHA == FirmwareRelease.ImageSHA
      Assert APP/BOOT endpoints, baud, address range, DLL
      Assert job fingerprint == enabled TestDocument Flash step's frozen job
  -> append FlashJobFingerprint to FrozenUutRunPlan
```

**不能把 FlashPolicy 和 FlashStep 的存在当成两套会各自做动作的配置**：FlashPolicy 表示本次生产任务希望做什么；TEST `Flash` 步骤是实现该动作的**唯一可执行入口**；预检必须证明二者一致。无 Flash 时不能隐藏执行计划里已有的 Flash 步骤；需要烧录时不能凭配置“Required”让 SCAN 在 TEST 外另发擦写指令。

## 5. 烧录前、烧录中、烧录后的正式质量闭环

### 5.1 扫码后，只读/无破坏预检

1. 扫描 UT1 读取 `DutSerial, ProductCode, HardwareCode`，解释这是**标签声明**。
2. 确认操作员当前生产任务、工单允许的产品、Recipe/固件发布版本、实际安装的工程及 TestProject 用例集合。
3. 检查当前 DUT 的历史记录是否已检测/烧录或已放行；历史事实只作防重复与风险提示，**不替代真实软件版本读回**。
4. 如 ECU 支持安全只读诊断，按特定已验证 `DID` / 物理信号核查硬件身份、Boot/App 软件版本；不猜测 DID、软件版本字符串或无证据的生产号。**做不到就显示“实物身份未核实”，需要按生产政策拒绝或人工复核**。
5. 完整核验 Image/Config XML/JSON/Safety provider 的 SHA、适用产品/HW、内存范围、编程 CAN 地址与 Main_Control 当前设备会话状态。
6. 检查 `0x43` 同会话读回的 ECU CAN 码率，是否和 Flash XML 要求相等；需要更改 `0x42` 时只能在工位空闲、显式批准的准备阶段应用和再读回。
7. 计划包含 Flash 时确认单批单次、顺序正确、未授权前不产生 USB/ECU 危险写入；冻结完整 `FrozenUutRunPlan` 和 `CanToolFlashJob.Fingerprint`。
8. 显示：`DUT + Product/HW + 当前目标 FW Release/Version + Firmware SHA + Flash Profile + APP/BOOT ID + 码率 + 测试范围 + 如有身份未验证风险`，等待明确确认与 Flash 特别授权。

### 5.2 真正执行（使用 R1 原来的 TEST 顺序）

推荐的逻辑顺序：

```text
工位就绪/软件目标已冻结
   -> 【准备】确认 IGN、夹具与基本通信条件
   -> 【可选只读身份/当前版本检查】硬件身份符合计划，记录读回来源
   -> 【如果 FlashRequired】TEST 中唯一 Flash 步骤
       -> CanTool 容量/端点/安全授权
       -> Boot → SecurityAccess → Erase → Download/Transfer/Exit → Verify → Reset
       -> APP 会话恢复必须确认
   -> 【烧录后必须另验】ECU 当前软件版本 / 目标 FW 标识或应用层特征（按 ECU 实测支持）
   -> 【功能检测】CAN/DBC、KEY 模拟、状态波形/UDS 断言等有序真实步骤
   -> 【清理】安全释放输出、关闭夹具；归档原始 CAN/Flash/StepOccurrence
   -> 【结果】ProtocolCompleted、AppRecovered、VersionMatched、
       FunctionalVerdict、RecordIntegrity、ReleaseDecision 分列
```

**注意**：不是所有 ECU 都支持某个指定 DID；`VersionMatched` 是否可验证、怎样比较由某 ECU 的已发布验收配置决定。未验证不能用“协议传输全部 ACK + APP 50 03”替代，如果是正式产线的强制版号证明则 Result=Unknown/blocked 而不是 PASS。

### 5.3 Flash 失败后的恢复而不是静默重试

- 擦除已经开始但写入中断、CAN 超时、设备断连或电压状态未知：**不能因为新的扫码重复触发擦写**；冻结当前 DUT 为 `FLASH_UNKNOWN / RECOVERY_REQUIRED`，保存最后一条真实证据与 RunId/任务 SHA，工位不能直接放下一台。
- 提供“查看烧录阶段/证据、人工确认状态、复位或重烧前独立审查”的维修操作路径。**不自动重试 erase/download**；旧授权不可循环复用。
- 即便只收到 Boot 51 01/APP 50 03，也必须区分 `protocol` 与 `verified software`；失败时不能去运行下游可能控制 KEY/IGN 的 TEST 步骤。
- 初始计划已进入一次烧录尝试后，无论失败还是取消，下次再次擦写都需新 `InspectionSession/FlashAttemptId/PlanSHA/Consent`，保留旧事务证据。

## 6. UI 具体怎么配合（成熟产品而非扫码绑定页塞三个浏览按钮）

### 配置员：“产品与生产配置”工作台

- 左列表为 `产品型号/硬件变型` 与 `生产任务`，右侧绑定现有 **TestProject + 配置资源**；烧录区可选：
  - 本次策略：`不烧录` / `必须烧录`（可条件烧录待后续实机审核）。
  - 软件目标：`FirmwareRelease` 列表（显示人类版本标签、HEX SHA、适用 ProductVariant/生命周期）；**不要默认最近修改时间**。
  - 烧录方法：`FlashProfile`，从现有 CanTool 三文件包导入，经核对自动填 XML/HEX/Safety；**可预览** APP/Boot ID、ECU CAN 码率、地址范围、块大小、Provider、受支持/不受支持状态。
  - 烧录后检验：已审核的 `ECU软件版本/DID 判据` 与功能测试用例范围；不能配置未知 DID 为“已验证”。
  - TEST 引用：显示已保存的 TEST 中唯一启用 Flash 步骤；配置项冲突红色阻断，“打开 TEST 编辑”允许正确修改步骤，但发布前必须再次核对。
- 已发布生产配置需要审核产品/HW 兼容、映像SHA与安全库SHA/真实文件存在、Flash Profile 受支持、Case顺序与运行前检查；**自动保存草稿并不自动发布**。
- 老 `FlashSettingsDocument` 只作为原有配置编辑/迁移输入；不能因为它有 `ApprovedByOperator` 字段就把文件值当真实 Flash 安全同意。

### 操作者：生产扫码主工作流

扫后显示一张清楚的任务预览，内容示例：

```text
DUT: 000000123456             产品: EPBA/H02 [来自标签]
当前生产任务: HydEPB 软件写入 + 功能检测 [已发布]
目标固件: Release R5 / HEX SHA256 abcd... (示例)
烧录策略: 必须烧录        FlashProfile: CanTool-HydEPB v3 [SHA...]
当前软件版本: 未核实（不得当作匹配）
APP/Boot CAN: 本配置下端点 / Boot 18DA50F1,18DAF150
ECU CAN: XML 500k / 主控 0x43 读回 500k
TEST范围: 烧录前预检 → Flash一次 → APP恢复/软件版本检查 → EPB功能用例
资源: 全部SHA匹配     FLASH安全: 等待独立授权    启动: 等待用户确认
[重新扫码] [查看详细计划] [运行前检查] [确认开始] [FLASH 单独授权]
```

- 运行时显示阶段进度：等待、Boot、Security、擦除、数据传输、校验、复位、APP恢复、软件版本断言、后续功能测试；右侧 CAN 报文与可导出的阶段证据；Seed/Key 和敏感运算内容必须脱敏。
- 最终显示分别的五项：`FLASH 传输完成?`、`APP恢复?`、`软件版本匹配?`、`功能TEST结果`、`生产放行状态`。不合并成一个“烧录成功/产品通过”提示。

## 7. 新建议的最小数据合同 / 接口（待 TEST/FLASH/RECORD 评审）

```csharp
record ProductFirmwareTarget(
    string FirmwareReleaseId,
    string FirmwareReleaseSha256,
    string ExpectedSoftwareVersion,
    string[] AllowedProductVariantIds);

enum FlashPolicy { NoFlash, FlashRequired, VerifyThenConditionalFlash, EngineerOnly }

record FlashBundlePin(
    string FlashProfileId, string XmlSha256, string? JsonSha256,
    string FirmwareImageSha256, string SecurityProviderSha256,
    int EcuCanKbps, string BootEndpointFingerprint,
    string AppEndpointFingerprint, string FlashJobFingerprint);

record ProductionRecipeFlashSection(
    FlashPolicy Policy, string? FirmwareReleaseId,
    string? FlashProfileId, string? ExpectedPostFlashVersionRuleId,
    string? ExecutableFlashStepId, string? FlashJobFingerprint);

record FrozenUutRunPlanFlashSection(
    string PlanSha256, string InspectionSessionId, string DutSerial,
    string ProductVariantId, string ProductionTaskId,
    FlashPolicy Policy, string? FirmwareReleaseSha256,
    FlashBundlePin? Bundle, string? FlashStepId,
    string[] RequiredVerificationStepIds,
    string StationEpochFingerprint, bool FlashAuthorizationRequired);

record FlashEvidenceLink(
    string InspectionSessionId, string ActualCaseRunId,
    string StepOccurrenceId, string FlashJobFingerprint,
    string FirmwareImageSha256, string SecurityProviderSha256,
    string TransportEvidenceArtifactId, string? VersionObservationArtifactId,
    string ProtocolOutcome, string AppRecoveryOutcome, string VersionVerdict);
```

这些是**说明性接口草案**，不可直接假设与当前 C# 的所有现有类型匹配。真正实现必须选择最小增量和旧 Schema 的显式迁移；`InspectionSessionId` 是一个 ECU 的一次生产检测，R1 单个用例会有各自 RunId；`FlashAttemptId`（若引入）需保证失败后也留下审计事实。

**职责分工**：
- SCAN/Project：解码、产品档案、任务/绑定解析、Recipe 选择、冻结 FlashBundle 元数据/哈希，显示为何选用此目标。
- FLASH 专项：真实已支持的 CanTool 能力、端点/算法/地址/镜像，已授权实体工作流与 post-reset/软件版验证接口；不得给另一个 ECU 套用 HydEPB OEM 尾块。
- TEST/R1：有序执行项目，Flash 作为唯一可执行步骤、单工位安全租约/操作者危险写入授权、TEST 运行/清理/停止。
- RECORD/Production：真实 RunId/StepOccurrence 与 Flash evidence 归档、DUT/软件历史、复检与首次放行幂等事实。
- UI：集中可编辑配置、任务预览、阶段进度和可理解阻断/恢复，不拥有物理编程接口。

## 8. 必须现场验收的正反案例

| 案例 | 必须行为 |
| --- | --- |
| 同一 EPBA/H02，任务 A 目标 R5、任务 B 目标 R6 | 仍同一 DutSerial；两份明确任务绑定不同固件发布，保留旧次结果 |
| 扫到 EPBA/H03，但 FlashProfile 仅批准 EPBA/H02 | **未授权产品/HW，拒绝 Flash 预检** |
| 新标签 Product/HW 合法，但无已发布固件目标 | `FlashRequired` 阻断；不找目录里“最新 HEX” |
| FlashPolicy=NoFlash 但测试用例中有启用 Flash | 阻断配置冲突，不静默跳过/执行 |
| FlashPolicy=Required 但测试项目无可达 Flash 或多于一个 | 预检阻断 |
| 同一项目多个用例里包含两个 Flash | R1 单批只许一个，拒绝；测试方案审批时应拦住而非运行中才报错 |
| XML 与工位目标 ECU CAN 波特率不一致 | 不能擦除；需要显式调整工位并 0x43 读回 |
| XML 波特率是 125k 或未知安全提供器 | 当前真实 CanToolFlashJob 不能证明支持；拒绝而不是擅自兼容 |
| XML 地址范围不适合固件/HW，固件 SHA 错、SafetyFile SHA 错 | 预检阻断；不得擦除 |
| 替换 XML、JSON、HEX 或 SafetyFile 后不发布新版本 | 计划作废，重新冻结审核 |
| 扫码/任务标称版号 R5，但实物读回身份不符 | 若身份校验属于正式前置门禁则阻断、保留诊断；不能用标签当硬件证明 |
| 当前版本已是 R5，条件烧录策略启用 | 仅在可信读回/已发布条件判定存在且获批准时执行预审的 NoFlash 子计划；不可用“历史此SN烧过”跳过 |
| 刷写到 Boot 51 01、APP 50 03，但新软件版本未验证 | FlashProtocol=Complete、AppRecovery=Confirmed、SoftwareVersion=Unknown；不能自动计正式版号合格 |
| 中途断线/取消/供电异常/Flash 擦除已经发生 | 持久记录 Unknown/RecoveryRequired；不得自动重试，不自动进入下一 DUT |
| 扫码同一编号后重复尝试 Flash | 独立新 Attempt/计划/授权/记录，不能绕过已尝试一次的锁 |
| 研发工具单独烧录 | 仍可按原 FlashLab 安全门禁使用，无生产扫码设置要求；不自动升级成出厂已放行 |

**与 NI 的对应**：NI 的 Process Model 和 Sequence 负责不同层，Property Loader 和部署依赖管理支持可控的项目变体；但 NI 并不为本 ECU 的 Boot/SeedKey/目标固件提供工厂自动正确性的保证，必须由 AutoTest 项目自己的冻结和实物证据证明。

资料：
- https://www.ni.com/docs/en-US/bundle/teststand/page/process-model-architecture.html
- https://www.ni.com/en/support/documentation/supplemental/06/a-guide-to-developing-effective--maintainable-teststand-sequence.html
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-system-deployment-best-practices.html
- https://diadrom.com/insights/ecu-flash-programming-uds/

## 9. 建议实施节奏（先把真正可工作的 Flash 不破坏，再联入扫码）

**F0 源码合同对齐**：确认所有现有可用 TEST Flash 步骤属性键、CanTool 3 件套路径解析、真正受支持的 ECU profile、CurrentStation CAN baud、原 FlashConsent、R1 CaseRunId/RECORD。同步 Flash 专项审核，不改实际编程传输算法。

**F1 可编辑但不会误授权的产品软件配置**：给 ProductVariant/ProductionTask/Recipe 增加目标 FirmwareRelease、FlashProfile、FlashPolicy、软件目标验证规则；从测试项目中选择现有 Flash 步骤，显示配置一致性/受支持状态。老 FlashSettings 资源只做显式迁移候选。

**F2 冻结与预检**：绑定已批准 `FlashBundleSha + FirmwareReleaseSha + FlashJobFingerprint` 与 TestProject SHA、TCF/TBC/ECU Baud；计划变更即时失效；实现“扫描→解释→完整预览→不触发物理写入”的工作台。最先跑 NoFlash。

**F3 受控实体 FLASH**：在 R1 已通过的真实测试运行执行器基础上，用一份被批准的 HydEPB Flash Job 验证一次写入、APP 恢复以及后续固件版本/功能检查；不可因 UDS ACK 直接产生出厂 PASS。上传/复制 CAN 与阶段日志，敏感安全信息脱敏。

**F4 RECORD 和正式生产政策**：用户/RECORD 认可生产模式的 ID、DUT 软件目标、每次实体 FLASH 尝试、VersionMatched 与正式 Release 后，再接正式首次放行/复检，检查同 DUT 重测不重复计新增。

**F5 扩展**：多个软件目标/任务、其他硬件版本安全算法/内存布局、可靠版本识别后条件跳过、跨工程自动切换/工单导入。真实型号/烧录项目不支持时明确“不支持”，不接受“功能有 Flash 按钮所以能刷所有 ECU”。

## 10. 用户真正需要决定的少量业务策略

1. **在正式生产首批，扫码后应当“每台必须烧录”，还是“根据可靠 ECU 软件版本判定是否跳过”，还是“只检测不烧录”**？建议首批固定任务直接采用 NoFlash 或 FlashRequired，别默认条件跳过。
2. **烧录后能否经真实 ECU DID/固定应用响应核对准确软件版本与硬件兼容性**？若尚无，完整版本核验作为正式产线待核，不以 APP 50 03 替代。
3. **固定当前生产任务还是允许扫码跨客户/硬件自动切换已批准 Flash 方案**？建议当前单工位首批只用一个任务的受控已发布软件目标，避免混线错刷。
4. **安全授权是否采用当前完整 SHA 人工确认机制**？现有 R1 是每个真实 Job 的独立 Consent；如要生产工位授权一次后连续刷多个 DUT，涉及持久权限和恢复边界，必须独立设计、审查与批准，**不能仅为省点击绕过 R1 现有门禁**。

**最终工程建议**：编程方案属于正式 Recipe 的组成部分，`FirmwareRelease + FlashProfile + FlashPolicy + PostFlashVerification` 分层维护。条码只识别 DUT，任务决定目标软件，测试项目中的 Flash 步骤是唯一可执行物理动作，R1 真实 CanTool 后端和独立授权不可被 SCAN 改写。
