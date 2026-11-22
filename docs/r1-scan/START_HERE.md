# AutoTest｜R1 SCAN 扫码枪与自动 TEST 配置匹配研究接管入口

> **2026-11-21 晚间独立复审补充（优先读）：** [S-14｜按现行产品决策逐项审查 SCAN/FLASH/TEST/RECORD 及 R1 真实缺口](S-14_LATEST_SYSTEM_CLARIFICATION_INDEPENDENT_REVIEW_20261121.md)。包括新发现的 `ProjectDefinition` 无正式 `ModelCode` 字段、产线旧 TEST Flash Step 可能造成**双烧录**、实体 START 与机械夹具反馈未实测、20 项正式验收矩阵。与 S-11～S-13 并用；不另起多层 Recipe。
>
> **历史区阅读警示：** 本页下方「关键业务约束」「第一阶段必须提交」「S-01～S-10」均是过去研究原文及当时假设；凡出现 **“不允许扫码触发夹具”/“扫码后必须由 PC 确认开始”/“生产可 NoFlash”/“用 TEST Flash 步骤触发每次生产烧录”/“逐号或多级 Recipe 配置作为常规入口”/“独立工具强制共用工程烧录 Profile”**，已经明确被当前 `Project_Management/docs/project-management/current/00~03` 决策取代。**当前任务绝不能照抄历史候选段落实施**。
>

> **2026-11-21 最新现行业务基线已取代原 S-01～S-10 中部分生产设计。** 当前产品合同以 `Project_Management/docs/project-management/current/README.md` 和 `00_DECISIONS_AND_SUPERSESSION.md` 为先。**一个型号一个工程、扫码后夹紧等待实体 START、每次完整出厂必烧录、生产 Flash 在 TEST 之前、原生工程内固定烧录配置、独立工具无工程可用**已获用户确认。先前“复杂 Recipe+NoFlash/TEST Flash Step+PC 开始确认”不得继续当正式默认生产合同。该变更不删除旧研究原文，也不表示当前 R1 已实现。
>
> **本分支最新独立全量复核（先读这三份）：**
> - [S-11｜真实扫码设备、编码、型号工程路由与生产工位](S-11_CURRENT_PRODUCT_SCAN_HARDWARE_ROUTE_AND_PRODUCTION_RESEARCH_20261121.md)
> - [S-12｜烧录配置字段逐项接线、原生 Profile 与 CanTool 真实后端迁移](S-12_NATIVE_FLASH_CONFIGURATION_EXECUTION_AND_MIGRATION_RESEARCH_20261121.md)
> - [S-13｜SCAN×FLASH×TEST×RECORD 系统合同、R1 源码差异及验收门禁](S-13_SCAN_FLASH_PRODUCTION_INTEGRATION_CONTRACT_AND_GAPS_20261121.md)
>
> **状态：`RESEARCH_COMPLETE_FOR_THIS_ROUND / R1_SOURCE_NOT_MIGRATED / WINDOWS_AND_FIELD_NOT_VERIFIED`**。S-07 的 UT1 编码/CRC 可作候审技术资产；S-09/S-10 的参数追踪与原生配置研究保留，但多层生产 Recipe、NoFlash、TEST Flash 步骤强绑定、独立工具与生产配置强共享等假设按现行 PM 被取代。


**身份**：SCAN 扫码/编号/配置匹配与 R1 自动测试启动准备专业 AI。  
**正式任务**：[Issue #72](https://github.com/ch3758134-prog/AutoTest/issues/72)。  
**研究分支**：`research/r1-scan-configuration-20261120`；初始基于 `R1@94e82eddb03e69b428fd4c63c2093a570d979c4e`。  
**状态**：`NI_BENCHMARKED / MULTI_MODE_DESIGN_PROPOSED / R1_INTEGRATION_PENDING / FIELD_NOT_VERIFIED`（2026-11-21 第一阶段研究落档；最新 R1 回读基线 `fa3b18e948043a9d1960adddb627658fd40c8159`）。

## 必须先调查的实际后端

`HOST/src/AutoTest.Specialists/Scan/`、`HOST/src/AutoTest.Desktop/Projects/ProjectScanMatcher.cs`、`ProjectConfigurationDocuments.cs` 中的 `ScanMatchSettingsDocument`、`HOST/src/AutoTest.Application/Production/`、`HOST/src/AutoTest.Desktop/Testing/RealTestWorkbenchService.cs`、`ProjectRunSelectionState.cs`、`HOST/src/AutoTest.Records/`。

**当前确认**：扫描匹配类已有，但只返回 RecipeId/Identity/状态，**不会直接配置 TEST 用例运行选择或自动启动真实测试**。研究不能把“条码规则匹配成功”描述成“整条生产自动测试链路已实现”。

## 关键业务约束

- 条码**由外部业务产生**；本软件只采集唯一编号，不自行生成编号。编号规则可编辑/版本化/发布；不同 ECU 型号和硬件版本不默认混在一个工程。
- 独立资源/绑定表解决“唯一编号→配置方案”；扫描字符内提取到的字段不等于得到完整 ECU 配置，必须对明确资源映射做版本验证。
- 匹配到实际 TEST 运行方案：测试程序/测试项目用例集合及顺序、TCF/DBC、KEY/工位配置、ECU CAN 码率、是否烧录及固件/Flash Profile、报告/出厂规则；明确冻结 SHA 和审核状态。
- 扫描应显示具体匹配结果供确认，不能在输入完编号的瞬间绕过安全门禁直接烧录或控制物理 IO；不允许同一个编号重复统计为“新增出厂”。
- **研发/手动运行不依赖扫码枪**：使用者直接选择用例或单步执行，不因未配置条码规则被拦截。生产模式与调试模式不能共用未经区分的出厂计数。
- 键盘模拟枪优先、专用输入区；失焦、CR/LF、连续扫描和意外粘贴都要处理。禁止全局 Hook 抢占编辑器输入；串口枪需真实型号和设备验证。
- 必须把未命中、歧义、旧资源修订、重复扫描、条码不合法、设备断连、运行中重扫等反馈转成可理解的错误/恢复动作。对重复 DUT 允许按审核规则复测，但报告 RunId 不得混同。
- SCAN 本身不做实体动作；最后调用唯一 R1 测试执行/FLASH 安全接口与 TEST-RECORD 报告，不另造运行器或统计库。

## 第一阶段必须提交

1. 源码能力矩阵，特别是扫码配置到真实 TEST 入口的断点；
2. 编号规则+独立绑定表+项目资源冻结+运行计划数据合同，列出当前缺失字段；
3. 研发与生产两套实际使用路径、异常和重复/计数策略、UI 必需状态字段；
4. 一个最小扫码到已预检 TestRun 选择的可实测路径设计；必要实现通过专业和 R1 审查后再进入 R1 集成。不能用 CSV/Mock 代替实物扫码验收。

**边界**：本专项不重画 UI、不自改硬件协议、不改 TEST/Flash 执行器、不在生产模式静默出厂放行。具体 WPF 交互只提出用户任务与字段要求，UI 设计在 `ui/ide-design`。

## 2026-11-21｜第一轮研究成果与 R1 实施交接

- [S-00｜R1 源码能力矩阵与真实接线断点](S-00_SOURCE_AUDIT_20261121.md)：逐类核实 SCAN、PROJECT、TEST、Production、RECORD 和 CAN/Flash 能力，确认扫码匹配当前未接实际 TEST，也未产生 Production RunRecord。
- [S-01｜唯一编号绑定与冻结 RunPlan 详细数据/接口合同](S-01_MATCHING_RUNPLAN_INTERFACE_CONTRACT_20261121.md)：外部 DUT 编号规则、独立发布版绑定表、版本化 Recipe、有序用例项目子集、TCF/DBC/KEY/TBC/波特率、Flash XML/HEX/Security SHA、报告/复检与 Release 幂等。
- [S-02｜生产/研发实际操作流程与错误、重复/统计策略](S-02_PRODUCTION_DEVELOPMENT_OPERATOR_FLOWS_20261121.md)：明确生产扫码才启动匹配流程，开发测试和单步**无需扫码即可运行**，匹配预览及确认、输入焦点/粘贴/CRLF、运行中再扫、复检/新增出厂计数。
- [S-03｜R1 最小实施切片与验收接口合同](S-03_R1_IMPLEMENTATION_HANDOFF_20261121.md)：P0-A～E 分阶段落点、唯一 R1 TEST 执行/Flash 授权/RECORD 桥接、测试清单、真实扫码枪+ECU 现场验证。

**研判中最重要的工程事实**：R1 最新实测入口 `RealTestWorkbenchService` 目前将有 DUT 编号的运行写为 `Development`（无编号/单步为 `Diagnostic`），并且测试项目中各用例有独立实际 `RunId`。新的扫码生产流程必须用 `BatchId/AttemptId` 关联这些实际 RunId，不能让旧 `ProductionSession` 预造的 RunId 或 `COUNT(DISTINCT dut_id)` 充当出厂批次/新增放行事实。

**下一步（需 R1 审查后开展）**：按 S-03 的 P0-A→E 接入，优先给出真实扫码→版本绑定→冻结两个用例→预检的可操作候选，然后受控调用唯一 R1 TEST + Flash/RECORD。此阶段**未实现生产接线、未运行 Windows Release Build/Tests、未完成真实扫码枪/ECU 实测**，不得宣布生产功能 PASS；研究与源代码交付不写入 `main`、`R1` 或固件。


## 2026-11-21｜第二轮产品层系统研究（S-04）

- [S-04｜扫码设备、外部唯一编号与生产方案路由的系统研究 v0.2](S-04_PRODUCT_ROUTING_AND_SCANNER_SYSTEM_RESEARCH_20261121.md)：对照 NI TestStand、GS1、ISA-95 和 Zebra CDC/HID 厂商文档，说明为何扫码的原始 ID 不能凭空推导型号/DBC/TEST/烧录，明确将设备接入、编号规则、业务绑定/路由、生产 Recipe、冻结 RunPlan 与 RECORD 拆为不同对象。
- **新增路线选项（待审查）**：在原“混线逐编号精确绑定”之外，提出“已批准单工位固定生产任务 + 扫码只绑定 DUT 身份”的轻量首版方式；两个模式必须显式单选，禁止精确绑定失败时静默回退，亦不把固定任务误称“扫码自动识别型号”。
- **重要硬件差异**：用户此前实际 USB/COM 串口扫码枪，不应只按原 R1 KeyboardWedge 入口推进；现有 `SerialBarcodeCapture` 只有 `IVerifiedBarcodeLineSource` 接口，正式 Windows COM 枚举、设备绑定、分帧、异常恢复尚需现场验证。软件层允许将 HID 作为另一个支持模式，但不能取代现有真实枪类型。
- 新回读活跃 R1：`02f18c65e5ba6038da10067c6285450de6cce911`；比先前 S-00 快照前进 28 提交，比较结果未显示 SCAN/Production/R1 真实服务接线源变更。**所有新增决策为研究候选，尚未改 HOST 或通过真实扫描/ECU 验收。**

## 2026-11-21｜第二轮 NI TestStand 系统对标与产品方案修正

- [S-04｜NI TestStand/SystemLink 与 SCAN 全面系统对标](S-04_NI_TESTSTAND_SYSTEM_RESEARCH_20261121.md)：真实 `Process Model / PreUUT / Test UUTs / Single Pass / UUT_RESULT` 架构；区别 NI 前缀选序列的**示例**和产品默认行为；研究四种配置来源——当前任务、产品编码、工单、逐号档案。
- [S-05｜扫码枪设备设置 + 业务配置 + 测试方案的实际操作设计 v0.2](S-05_SCANNER_CONFIGURATION_AND_PRODUCT_UX_V02_20261121.md)：系统设置/编号规则/Recipe/独立绑定/当前工位任务、真实枪验收、模式选择、UI 必需状态、两种入口和 R1 最小切片。

**重要取代关系**：S-01/S-03 的“P0 每只 DUT 必须逐号精确绑定”的提议属于未审批假设，现被第二轮 NI 研究**修正为四种候审绑定依据**；仍遵守用户已确认的“外部唯一 DUT 编号、编号与配置映射独立、重复不计新增、Development 无扫码可运行”。最先建议在用户当前单工位做 `ActiveTask→Recipe` 的**显式任务关联**并扫码验证编号；不以该模式证明 DUT 实体型号，正式出厂放行/复检规则需另行审批，严禁静默烧录。

**同步决策源**：项目管理 [07_WAVE_SCAN_RECONCILIATION.md](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current-20261121/07_WAVE_SCAN_RECONCILIATION.md) 和 [02_DECISION_SUPERSESSION_LEDGER.md](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current-20261121/02_DECISION_SUPERSESSION_LEDGER.md) 已将第一轮 S-00～03 标为**候审而非生产已批准**。第二轮亦保持候审；不得将研究结论当作现有 R1 实现/Windows/真实扫码枪验收。

**第二轮调研基线**：`R1@02f18c65e5ba6038da10067c6285450de6cce911`（2026-11-21 调研时读取；后续实施需重新回读最新 HEAD）。研究分支文档提交未修改 R1/main/固件；未修改 TEST/Flash/Record 执行源码。

## 2026-11-21｜并行 S-04 文档交叉审查

- [另一份 S-04｜产品路由、GS1/ISA-95 与设备真实接口研究](S-04_PRODUCT_ROUTING_AND_SCANNER_SYSTEM_RESEARCH_20261121.md)：同分支同期研究，对固定任务/逐号/工单/标签属性四种路由提出佐证；其中“设备实际是否 USB/COM”属于**未验证的现场事实**。
- [S-06｜两份 S-04 交叉审查与冲突收敛](S-06_NI_PRODUCT_RESEARCH_RECONCILIATION_20261121.md)：统一模式术语、标明研究/审批边界，避免把未经确认的 USB/COM 推测写死在 R1；明确 NI 对标中哪些原则可复用，哪些是演示而非生产门禁。

**审查提示**：本分支存在两份名称以 S-04 开头的研究报告，分别聚焦 NI 框架与产品配置路由；请以 S-06 交叉审查确认共同结论/开放问题。实施时须对照 R1 最新源码，不使用旧分支覆盖持续迭代成果。

## 2026-11-21｜新增用户条件：编码规范可由我们设计（S-07）

用户明确补充“**编码可以我们自己来设计**”。此前所有“外部编号是没有产品语义的随机串，因此无法靠标签选择产品方案”的推论**只能适用于历史纯 SN / 客户已有编码**，不能再作为新条码规范的默认前提。原“外部编号由业务生成”责任边界仍先保留：**自主定义标签格式 ≠ 已获授权由 AutoTest 发号**。

- [S-07｜自定义 DUT 条码结构 UT1 v0.1 与产品变型配置映射](S-07_CUSTOM_DUT_BARCODE_ENCODING_V01_20261121.md)：候选 `UT1-产品代码-硬件版-外部唯一 SN-CRC`，发行规则/校验示例、字段主键、产品→Recipe 发布映射、历史兼容、Code128/DataMatrix/GS1 区分、真实扫码与 R1 最小验收。
- **重要新路由建议（候审）**：对采用新 UT1 的产品，优先研究 `ProductCode+HwCode→发布 Recipe` 的显式映射；已批准工位任务仍应限定允许产品并提示版本，逐 SN/工单仍作为明确可选严格模式，不自动 fallback。
- **不能改变**：条码文本和 CRC 不证明实物 ECU 身份；版本/工程资产冻结与原 R1 测试执行器/Flash 授权保持不变；研发不扫码可以操作；同一 Serial 不因产品字段更换而计第二个新 DUT。
- 本次 S-07 是**编码设计建议、未实施、未实物验证、未审批正式出厂政策**；旧 S-04/S-05/S-06 是旧业务输入前提下的调查，阅读时先看 S-07 的新条件。

## 2026-11-21｜第三轮全系统研究与最新 R1 差距（S-08）

- [S-08｜扫码枪、唯一编号、产品配置、生产任务与测试/记录的全链路系统研究 v0.3](S-08_END_TO_END_SCAN_PRODUCT_SYSTEM_RESEARCH_20261121.md)：在用户确认“编码可以自己设计”后，将 NI TestStand、GS1、ISA-95、Microsoft/Zebra 设备模式与 R1 **实际代码**统一复核；特别明确 ProductVariant、Recipe、Task/Route、FrozenRunPlan、InspectionSession/CaseRun/Release 独立语义及跨工程 StationCatalog 的未来边界。
- **架构建议**：新标签使用 S-07 的 UT1 候选编码（Protocol + Product + HW + 全局 Serial + CRC）；在已批准任务的产品范围内，用正式 `ProductVariant(+TaskScope)→RecipePin` 选择已保存 TestProject、TCF/DBC/KEY/TBC、ECU CAN、Flash/报告；保留旧纯 SN+任务、严格逐号档案和工单模式，不允许匹配失败静默 fallback。
- **源码差距**：最新复查 `R1@8812847cd814a344e4c7ed4791fa59bf02304238`：`IdentityRuleSet` 是纯 Regex、`ProjectScanMatcher` 只返回 RecipeId、`SerialBarcodeCapture` 没有真实 COM 连接、`TestProjectWorkbenchViewModel` 的有序用例是真执行但没有生产计划输入、`RealTestWorkbenchService` 有 DUT 编号仍记录 Development。不能声称扫码生产已接线。
- **候审**：编码发行归属、扫码枪真实 USB 枚举/读码器符号、当前产品是否混线、ECU DID 实物校核、正式出厂首次放行政策；本轮没有修改 HOST/R1/main/硬件，也没有真实枪/ECU 物理验收。

## 2026-11-21｜烧录配置和扫码的一体化研究（S-09）

- [S-09｜扫码 + 固件发布 + 烧录方法 + 烧录策略 + TEST/RECORD 全闭环](S-09_SCAN_FLASH_PRODUCTION_RECIPE_INTEGRATION_20261121.md)：用户明确要求**烧录配置与扫码方案一起考虑**。报告回读 R1 `ConfiguredRealTestFlashPort`、`CanToolFlashJob`、`CanToolFlashWorkflow`、`HardwareConnectionService`、`RealTestWorkbenchService`；指出旧 `FlashSettingsDocument` 只是元数据，不会自动成为真实 CanTool Job，老 `ProgrammingProfile` 仍是 MockOnly 且真机不支持。
- **候审结构**：UT1 产品/HW + 当前任务→唯一发布 Recipe，Recipe 独立引用 `FirmwareRelease`、`FlashProfile`、`FlashPolicy`、软件版本读回规则，检查与 TEST 内**唯一启用 Flash 步骤**的冻结 Job/Consent 完全一致；码率需与 XML 和 0x43 实际读回一致，FLASH 仍沿 R1 的 CanTool 和独立授权。
- **已证实的实际限制**：当前 CanTool XML 只允许 250k/500k，安全算法提供器对真机仅启用核验过的资料；一次项目运行范围最多一次 Flash，尝试后不自动重试。APP 50 03 恢复并不证明目标软件版本；协议、软件版、功能 TEST、正式放行分开判。
- **生产政策未批准**：NoFlash/FlashRequired 建议先作为显式模式；依据 DID 识别后条件跳过、一次任务授权连续烧多台以及 Release/复检政策需要先由 R1/FLASH/RECORD 与用户共同批准。研究不修改 R1/main/固件，未物理测试。

## 2026-11-21｜用户正式明确：FLASH 原生配置，不导入外部 XML（S-10）

- [S-10｜原生烧录参数直接编辑 + 当前 ECU 工程固定位置保存 + 运行时无外部配置依赖](S-10_NATIVE_FLASH_CONFIGURATION_FIXED_PROJECT_STORAGE_20261121.md)：用户明确指出 CanTool 的“配置文件+固件+安全算法”只作阶段性研究参考；正式 AutoTest 烧录设置**手动在软件内编辑、自动保存到工程固定位置**，不要求导入 Configuration.xml，不长期引用外部配置路径。固件与安全算法本身仍可作为受控工程资源。
- **严格修订关系**：S-09 内的“FlashProfile 继续以外部 CanTool XML/JSON 包作为正式执行配置”被本次用户要求**明确取代**；但其配方/固件发布/FLASH 策略/真实安全授权/烧录后核验结论保持有效。S-07、S-08 的扫码产品/任务选方案不变。
- **最新 R1 实际断点**：`ProjectWorkspaceService` 当前 flash-settings 虽在工程内部保存却使用 GUID 文件名而非唯一固定文件；`FlashSettingsDocument` 不是可执行 Flash Job；`ConfiguredRealTestFlashPort.FreezeJobs` 必须读取 XML；近期新增 `ScanRecipePreparationService` 也沿旧路径读取。这些均需代码级改造，不能仅删除 XML 选择按钮。初步建议草稿固定 `assets/flash/profile.json`、发布不可变 snapshots、固件/必要算法复制为工程内部受控资源；保存路径/Schema 需 PROJECT/FLASH 复审。
- **研究状态**：用户要求明确；研究分支已归档设计。未修改 R1/main/固件；未做 Windows 编译或真机 FLASH 验收。后续提交必须以最新 R1 HEAD 对比。
