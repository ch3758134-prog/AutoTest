# AutoTest R1 SCAN｜S-14 最新系统澄清后的独立复审与实施验收基线

**日期：**2026-11-21（美国/项目报告日期）  
**归档：**`research/r1-scan-configuration-20261120`，Issue #72  
**产品决策来源：**`Project_Management@6d2a476de8eba752a227a02dc77b0fc5d30296aa` 的 `docs/project-management/current/00～07`。**绝不引用旧 `current-20261121` 中与新要求矛盾的生产流程。**  
**源码核查：**`R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f`（定点回读）；研究分支 S-11～S-13 已存在，本稿作**独立确认、风险裁决、可执行合同和测试门槛**，不再重新造旧多层 Recipe。  
**状态：**`USER_DECISIONS_INHERITED / SOURCE_GAPS_CONFIRMED / INTEGRATION_CONTRACT_PROPOSED / NO_R1_CODE_CHANGE / NO_BUILD_OR_FIELD_PROOF`。

## 1. 研究裁决：现在做的是型号工程识别+生产过程协调，不是“扫码选配方”

### 1.1 已确定且不得被研究弱化的真实产品合同

1. 一个 ECU 型号仅有一个当前已安装/当前有效工程；工程组织**唯一当前有效** DBC、自动 TEST 测试树、生产原生 Flash Profile、当前固件及必要资源。历史版本允许存档，不让产线每台选择。
2. 扫码枪从外部签发的标识中取 `ModelCode + DutSerial`；编码**可以与用户共同制定**，但 UT1/HW/CRC/长度仅是候审编码语法，不是全部生产客户硬编码。软件不自行签发 DUT 唯一序列号。
3. 当前已打开的是同型号工程：**不弹窗**。扫到另一个**已经登记且唯一可查**型号：只有此时要求**确认切换工程**，切换时完整更换 TEST/DBC/当前固件/原生 Flash；未知/多个工程冲突：拒绝，不猜、不按最近文件夹/名称近似匹配。
4. 生产单 DUT 固定业务顺序：`WAIT_SCAN→型号验证→夹具压紧请求→实物就绪证据→等待实体启动键新有效事件→工位/资源预检→本次完整 Flash→已证实完成才运行全部 Enabled TEST 用例→真实记录与单台报告→按实际已批准状态收尾/释放→结果保留等待下一次有效扫码`。**正常现场操作员不需逐台在 PC 点击开始、输入 SHA 或选择配方**。
5. **每一次正式完整生产检测、同号复检都必须重新真实烧录**；生产 Flash 是站点流程前置，不依赖工程师在 TEST 树手插 `Flash`。没有生产 NoFlash、ConditionalFlash 选项。
6. TEST 树中“目录”只有组织作用，不能执行；生产使用**整个有序深度展开的 Enabled 具体用例**，开发才使用单步/单用例/本次选择范围。普通 TEST FAIL 后继续/诊断/终止由 TEST 已保存控制流决定，不得代码一概立即停止全部用例；实体 FLASH 故障、STOP、失联仍有独立运行禁止门禁。
7. 专业工具（DBC、CAN/手动台架、UDS、独立 FLASH、WAVE）**无工程也能启动**；资源独立，按需使用工程资源。独立 FLASH 可有自己的编辑参数，不强制与生产原生配置同一用户操作状态，但必须共用经过验证的真实 UDS/Flash 后端。
8. 编号、FlashAttempt、每个 CaseRun/StepOccurrence、原始 CAN 底本、简洁单台报告、批量报告**同一记录真相源**；重复扫码不创建新实体，同号复检生成独立新 Inspection 和本次 Flash 证据；正式首次放行和批量统计口径仍待审。
9. 夹具机械到位、实体 START/STOP 上报与抖动、断电后的现场状态**没有真实闭环证据**，不得凭 USB ACK 伪称；此阶段产品要求已确定 ≠ 固件接口已实现。

### 1.2 原 S-01～S-10 的明确裁决矩阵

| 旧结论 | 新结论 / 是否可复用 |
|---|---|
| 每只 SN 预绑定一个 Recipe | **不采用为默认**。一个 ModelCode→唯一工程，SN 归属检测历史 |
| ProductVariant→WorkOrder→Binding→Recipe→TestProject 多层配置 | **不作为产品常规入口**。工程自动确定当前 DBC、TEST/固件/Flash |
| 任意条码自动跨工程选多个变型/资源 | **仅通过唯一已登记型号工程索引**，异型号必须确认；不能用动态文件名猜 |
| UT1 含版本/型号/SN/可选 HW/CRC | **保留为候选 codec**。确切格式、外部发行方与实体枪需实测后冻结 |
| 扫码只显示 RunPlan，需要 PC 点击确认开始 | **生产流程不采用**。合法扫码可请求夹具，**等待实体 START** 才允许烧录 |
| NoFlash / FlashRequired / ConditionalFlash | **正式生产全部替换为每次必烧录**。研发/独立工具依自身操作不强制 |
| 把 Flash 加到 TEST 步骤树是生产必需 | **取代**。生产 Flash 属于前置站点阶段，TEST 内旧 Flash Step 保留技术兼容，但生产不能因此**双烧录** |
| 独立 FlashLab 强制使用项目生产 Profile | **取代**。工具可独立参数运行，显式加载工程资源；不可暗改工程配置 |
| CanTool XML 运行期输入和外部 config index | **取代**。工程内强类型原生参数、固定相对路径；HEX/必要算法依旧是资源 |
| 资源 SHA/真实 Job/安全门禁、APP 50 03≠软件版本、真机失败证据 | **继续有效**，任何新生产触发方式不得绕过 |

## 2. 五个真实最小数据合同，避免无用的新配方系统

| 最小合同（可为现有对象的投影，并非必须五张新数据库表） | 保存在哪里 | 关键字段 |
|---|---|---|
| `BarcodeDecoderSettings` | 工程生产配置或受控扫码规则资源 | CodecVersion、ModelCode 提取、Serial 提取、校验算法、样例/规则修订、是否接受真实扫描源 |
| `StationModelCatalog` | 本机安装工程索引（非烧录配置索引） | 唯一 ModelCode→ProjectId/CanonicalManifestPath、最近有效 ManifestSHA、冲突/路径失效状态 |
| `ProjectProductionAssets` | ECU 工程已有资产清单 + 固定生产配置 | 单一当前有效 DBC/TEST Tree/原生 Flash Profile/固件/Security、相互兼容和校验 SHA |
| `FrozenInspectionPlan` | Production 当前 Inspection/RECORD | ScanEventId、InspectionId、DutSerial、ModelCode、ProjectId+ManifestSHA、OrderedEnabledCases、NativeFlashProfileSHA、FirmwareSHA、JobSHA、StationEpoch、BarcodeRuleSHA |
| `InspectionRecord` | 真实 RECORD 同一持久库 | Scan/夹具/Start/FlashAttempt、每 CaseRunId/StepOccurrence、CAN/UDS/WAVE 原始证据、ReportId/状态、Result/ReleaseDecision（后者有授权政策才能使用） |

**不要重新增加** ProductVariantRegistry、Recipe 编辑器、ProductionJob 手工创建、批次强制启动 UI。站点索引只解决**跨工程型号查找**，不同于用户禁止的“外部烧录配置路径索引”。

### 2.1 必须给 ProjectDefinition 加正式型号身份，不能依赖工程名

最新 `HOST/src/AutoTest.Projects/Project/ProjectDefinition.cs` 只有 `Id, SchemaVersion, Revision, Name, Assets, Dependencies`，**没有独立 EcuModelCode 字段**。如果直接把项目显示名 `HydEPB` 当成生产条码正式匹配键，会造成用户改名、错拼、重名后匹配错误。建议在工程元数据中加入受控的 `EcuModelCode` 或等效正式工程资产；与显示名分离，唯一校验、显式旧工程迁移、模型注册冲突阻断。选择字段/SchemaVersion 的具体实现由 PROJECT 设计评审；这项缺口比继续扩大 ScanRecipe 更紧迫。

`StationModelCatalog` 的同型号索引要核验**工程内部自报 ModelCode**、ProjectId、路径与 manifest/SHA，不允许只凭目录名或扫描上一次临时文件列表注册。多个同型号工程只能有一个明确当前被批准用于生产；重复时阻断并让工程师解决（正常产线操作员不用处理复杂工程文件）。

### 2.2 输入身份和二维码的边界

- 扫码枪一般直接接 Windows，不经过 MK20 主控。HID keyboard、HID POS、USB CDC/COM 必须依据**实际设备枚举/原始输出**选择；`SerialBarcodeCapture` 目前只有 LineSource 接口，不等于 COM 驱动可用。
- 从原始 ScanEvent 剥离的是**已确认传输结束符**（CR/LF/厂商前后缀），不要 `Trim` SN、去掉前导 0、把 SN 转整数；CRC只检错，不识别真实 ECU 主板。
- 生产流程只在 `WAIT_SCAN + 物理工位允许接受新 DUT` 才接受完整新扫；忙时不缓存、不排队、不替换当前身份，也不自动换型号。
- 编码研究中 UT1 `UT1-EPBA-H02-000000123456-4372` 可作为一致性单测向量，不让 `H02` 自动增加 ProductVariant 层；真正业务必需字段是 **ModelCode + StableUniqueSerial**。
- 历史纯 SN 不携型号时不能宣称自动识别型号；需要明确已有工程上下文/历史兼容政策后才用于生产；用户已确认的“自动跨型号”不能靠猜测。

## 3. 生产工位状态机：高节拍但每一步事实必须真实

```text
WAIT_SCAN
  --完整扫码/型号已核对-->
PROJECT_READY
  --请求夹具压紧-->
CLAMP_REQUESTED
  --夹具真实到位证明/已批准等价工位确认-->
CLAMP_READY
  --新实体 START 有效边沿-->
START_ACCEPTED
  --最终只读预检/冻结当前 Profile+Firmware+DBC+Enabled Tree、0x43 码率-->
FLASH_PREPARED
  --当前 Inspection 唯一受控 Flash 执行许可-->
FLASH_ATTEMPT
  --真正协议完成、必要后验证满足本产品标准-->
FLASH_CONFIRMED
  --所有 Enabled 用例按树序使用唯一 TEST 引擎-->
TEST_RUNNING
  --事实持久化和固定单台报告-->
RECORD_FINALIZING
  --受控收尾，工位确认可重接 DUT-->
RESULT_HELD_WAIT_SCAN
```

**运行失败不意味着统一“释放夹具”“保持夹具”**：需要实际 STOP/FAIL 场景证据和用户最终决定。若 FLASH 中断/状态不明，`STATION_RECOVERY_REQUIRED`，不自动认为可新扫；UI 允许查看结果，但新运行许可必须严格区分。

**两种 START 故障负向案例**： 
- 在 `CLAMP_READY` 之前按下或一直按住 START→不能认为已有新的本次启动事件；需要显式释放后新的有效边沿和去抖；
- 夹具 ACK 但机械未真实压紧/反馈未知→不能靠软件倒计时把状态假设成实际到位，现场就绪判据需 HW/DEVICE 给实证。

**真实硬件证据边界**：主控夹具 USB 0x18/0x19 为请求，而非压紧反馈的充分证明；现有硬件文档未证明实体 START/STOP 输入口与 USB 事件实现。代码只能提供等待与设备未证实状态，不造假事件源。

## 4. FLASH：当前工程原生配置 + 生产前置阶段 + 工具独立模式

1. **原生生产配置**：当前 ECU 工程唯一当前有效 `NativeFlashProfile`，APP/Boot TX/RX、支持的 CAN bitrate、内存范围、块长、Security Provider（受审 ABI/SHA）、目标固件资源；导入固件/算法后复制至工程内，**正式运行不要求 XML/JSON 外部文件**。路径建议 `assets/flash/profile.json` 为未审批技术候选，不是用户指定的精确文件名。
2. **草稿和生产生效配置**：编辑自动保存原生草稿，不弹另存为；实体 START 后运行冻结已确认生效版本，正在编辑的内容不能中途替换固件；历史保留修订证据。
3. **工具 FlashLab**：无工程打开独立编辑/固件选择；用户可主动加载工程的现有参数作为副本或明确编辑工程，不能默默修改生产配置。共享的是真实 CanTool/ISO-TP/UDS/物理安全门禁，而不是同一份 ViewModel 状态。
4. **迁移必要接口**：当前 `ConfiguredRealTestFlashPort.FreezeJobs` 从 TEST Flash Step `programmingProfile` 载入 XML；`CanToolFlashJob.Freeze(CanToolXmlProfileReader,...)` 也依赖 XML Reader；`ScanRecipePreparationService` 仍依赖旧 TEST Flash Step。需新增 `NativeFlashJobBuilder` 直接冻结强类型 Profile+工程固件+受审安全算法，最终**生产前置 Stage**用同一 `CanToolFlashWorkflow`，保留在 R1 实测过的 APP/Boot 端点、特殊尾块、校验等逐字节等价测试。
5. **正视真实能力**：当前 CanTool Job 仅接受已确认的 250k/500k XML 模式、受限定的 Security Provider 和 OEM 特定尾块，不因用户在界面编辑任意参数就具有通用支持。主控 ECU CAN 0x42/0x43 的 8 个档位不等于烧录支持范围。
6. **生产 Flash 硬门禁**：禁止省掉 SHA/设备会话/一次性防重复策略；现有 `AUTHORIZE ECU FLASH <SHA>` 适合工程师，正式生产应研究“配置生效授权 + 当前型号/工程/站点资源锁定 + 本次实体 START 新事件→单次 FlashAttempt 授权”，在 FLASH/DEVICE 审核和真机证明后替换每台 PC 输入长文本。不能为了“无鼠标”直接删除底层权限。
7. **避免双烧录**：生产已在 TEST 前完整 Flash；若旧 TEST Enabled 树中仍有可达的 Flash Step，**在生产预检明确拒绝该冲突并要求工程师迁移/修正用例**，不要无提示忽略，也不要按 TEST 脚本额外再擦写一次。开发 TEST 内 Flash 的历史兼容另行处理，不拿它作为生产必须配置项。
8. **烧录完成证据**：Boot 复位 ACK、APP 50 03 会话恢复和 ECU 实际软件版本符合目标必须分成独立结果。无法读出目标版本时，不自行编造 DID；“软件版本未实证”应按已审批的正式质量门禁显示 Unknown/待核，不能冒称 PASS。

## 5. TEST 与 RECORD 的专业职责别再复制到扫码里

- **TEST**：有序树、Enabled/Selected 两种不同状态、真实源 CAN/DBC/WAVE 观察、脚本定义失败路径和关键测量抽取。扫码/生产不得因为工程师上次只勾了 1 个用例就漏测其余 Enabled。若正常功能 FAIL，根据已保存 TEST 控制流是否继续后续用例；最终全台结果保留所有 FAIL，不能被后一个 PASS 覆盖。
- **Production**：启动工位过程阶段，不拥有第二种 TEST DSL；FLASH 完成才能调用已有真实 RealRunCoordinator/RealTestWorkbenchService。
- **RECORD**：一次接受的生产检查要有持久 `InspectionId`，包含可能未启动的 Flash/TEST 状态；每次真实 FlashAttempt、CaseRunId、步骤 Occurrence、完整 CAN 底本与阶段事实都能追溯；Flash 失败后 TEST 要记录为`NOT_EXECUTED`，不能虚构 TEST FAIL。
- **单台报告**：清楚列 DUT/型号/工程及固件版本、本次烧录和功能 TEST 总结、失败原因/时间、证据链接；不能把原始几千行 CAN 逐条塞正文。
- **批量报告**：固定样式、基于真实单台 Inspection 再计算；扫码事件、完成检测尝试数、唯一 ECU 数、PASS/FAIL/STOP、复检数与首次正式放行/首次良率**分别统计**；后两项规则、批次边界尚未批准，不得作为默认发布门禁。

## 6. R1@5f7a2cf 的代码审查结果：新产品不是当前源代码已经达成

| 准确源位置 | R1 目前实际代码事实 | 迁移/验收要求 |
|---|---|---|
| `AutoTest.Projects/Project/ProjectDefinition.cs` | 有工程唯一 Id/名称和资源，**没有正式 ModelCode 字段** | 增稳定 ECU 型号与跨工程唯一索引，显式迁移旧工程 |
| `AutoTest.Desktop/Projects/ProjectScanMatcher.cs` | UT1+Regex 匹配当前打开工程中的 Recipe，选择输出 RecipeId/Preview | 改为扫码型号→已安装工程唯一映射；同型无提示，异型确认，未知拒绝 |
| `AutoTest.Desktop/Shell/MainWindowViewModel.cs` | 扫描后 `ApplyScannedRecipeAsync` 更新工程师临时选择，**并不自动跑生产状态机** | 生产独立页面和状态机，研发入口不动 |
| `AutoTest.Specialists/Scan/SerialBarcodeCapture.cs` | `IVerifiedBarcodeLineSource` 抽象、未真正打开 COM 口 | 验证实际 HID/COM/POS，补齐真输入源、连续扫码、断线和设备绑定 |
| `AutoTest.Desktop/Projects/ScanRecipePreparationService.cs` | 从 TestProject 枚举已启用用例，依 TEST Flash 步骤，支持 `NoFlash/FlashRequired`，冻结 XML FlashJob | 不把这套旧 Recipe 逻辑继续扩张为正式生产，迁移当前工程资产直接冻结 |
| `AutoTest.Desktop/Projects/ProjectConfigurationDocuments.cs` | `FlashSettingsDocument` 只是字段元数据而非当前物理 Job；扫码有 FlashPolicy 及多 Recipe | 原生单工程生产 Profile、当前固件与有限扫码规则；旧数据迁移无暗授权 |
| `AutoTest.Desktop/Testing/ConfiguredRealTestFlashPort.cs` | 旧 TEST Flash Step 读取 XML/HEX/DLL 生成真实 Job | 共用底层技术保留，生产原生 Profile→前置 Flash 后端，禁止双擦写 |
| `AutoTest.Application/Production/ProductionWorkflow.cs` | 有顺序编排轮廓，但 Flash 正结果仍可能标 `RunFactKind.MockObservation` | 不使用 Mock 事实证明正式实体烧录，真实事实归档 |
| `AutoTest.Desktop/Testing/RealTestWorkbenchService.cs` | 非空 DUT 编号普通运行仍记 `Development`；单个 TestDocument 真实执行 | 正式生产上下文归类 Production，多个 CaseRunId 归本次 Inspection，复用真实后端 |
| `AutoTest.Desktop/Projects/ProjectWorkspaceService.cs` | 工程 Manifest 固定名，Flash 配置存 `assets/flash/{GUID}.settings.json` | 一个当前 Profile 的稳定工程相对位置、原子自动保存/发布、固定资源身份与文件 SHA |
| `AutoTest.Desktop/Testing/TestProjectWorkbenchViewModel.cs` | 目前工程师 `RunSelected`、通过勾选执行，已冻结若干用例 | 生产不能跟随选中；应按未来唯一 TEST 树全部 Enabled 且目录不执行 |

以上内容是**静态源码证据**：没有在本研究分支进行 Windows Debug/Release Build、真实扫码枪/夹具/实体键/ECU 烧录、完整 TEST/报告验收。不要用“代码里有类”代替现场能工作。

## 7. 一组严谨的生产验收矩阵（建议交给 R1/QA）

| ID | 场景/触发 | 预期真实结果/证据 |
|---|---|---|
| S01 | 工位空闲，扫描当前同型号合法码 | 无弹窗；绑定真实 ModelCode/Serial；不会提前烧录 |
| S02 | 已知异型号扫码 | 仅一次确认；工程、DBC、TEST/当前固件/Flash 同时正确切换；无候选歧义 |
| S03 | 未知型号、双工程同码、配置路径丢失 | 明确拒绝；不夹紧/不烧录 |
| S04 | 错误 CRC、短包、CRLF 双终止、人工粘贴冒充设备 | 只产生合法的一次解析结果或明示拒绝；SN 原文保留前导零 |
| S05 | 同合法编号连续扫、正等待实体 START 时又扫 | 不重建本次 DUT、不排队、不触发双夹具/双 Flash |
| S06 | 夹具 USB ACK 但无机械到位证据 | UI 不得显示“实物夹紧已证明”；不得凭假反馈开放真实 START |
| S07 | START 被按住、先于 CLAMP_READY 按、抖动、两次边沿 | 未获得新有效触发不能启动；一次 Inspection 最多一次烧录 |
| S08 | 同一 SN 第一次完整检测与复检 | **两次都各自真实烧录**、两份 Inspection/Report，但唯一 DUT 数仍为一 |
| S09 | 目标固件/算法/原生 Profile 缺失、SHA 被换、码率不符 | 预检阻断；不使用 XML/外部文件路径替代 |
| S10 | 旧 TEST 里可达 Flash Step，生产又设前置必烧 | 预检报“重复烧录动作”并阻断，工程师显式修复 |
| S11 | FLASH 传输中断/断连/ECU 复位未确认 | 保留阶段与 Unknown；不进入正式 TEST，不自发重试 |
| S12 | FLASH 全部 ACK 但 APP 未恢复/软件版本不可证明 | 分别标明协议/APP/版本状态，不伪造软件版 PASS |
| S13 | FLASH 成功、工程师当前仅勾选一个测试用例 | 正式生产依序运行全部 Enabled；研发勾选不改变范围 |
| S14 | 正常 TEST 第一个用例 FAIL 后继续执行后续用例 | 根据已保存 TEST 逻辑继续/终止；先前 FAIL 不被后续 PASS 覆盖 |
| S15 | 运行中再次扫码或切换工程、切换主控会话 | 拒绝并保持原 DUT/配置，计划或授权会话更改阻断后续写入 |
| S16 | 完整 PASS / 功能 FAIL / FLASH FAIL / STOP | 各自 Inspection/原始 CAN/关键测试日志/单台报告可回查；TEST 未执行如实标识 |
| S17 | 结束显示结果、正常进入下一 DUT | 上次结果保持至下一合法扫描；安全/机械未知则不声称已经可接新 DUT |
| S18 | 断电/存储失败/报告生成失败 | 保留尽可能多的持久事实及不完整状态，不自动补造 PASS/出厂数量 |
| S19 | 无 ECU 工程的独立 FLASH、UDS、DBC 工具 | 按工具已有能力可运行；没有工程也不被扫描配置阻断 |
| S20 | 导入固件后删除来源目录、重启工程/拷另一台 PC | 原生 Profile 与固件在工程内，固定路径与 SHA 可恢复；运行不引用旧外部 XML |

必须为每个真实验收场景记录 `R1 HEAD / Windows Build / 工位硬件+FW版本 / 扫描原文+来源 / FlashAttemptId+JobSHA / CaseRunId / CAN证据 / 报告路径`。现场没有实物证据就标 `NOT_FIELD_VERIFIED`。

## 8. 技术交接顺序与跨专业依赖，不要求用户再解释已明确的需求

1. **PROJECT/TEST**：正式 ECU ModelCode + 单一 Enabled 测试树+分类目录、真实资源 Revision/SHA、自动保存+恢复；开发手动运行不变。
2. **FLASH/PROJECT**：原生 Profile 固定工程存储+当前固件+受审 Security Provider、真正 NativeFlashJobBuilder；逐字节对齐成功 HydEPB 实测流程。独立 Flash 工具先验证配置改造不倒退。
3. **SCAN/PROJECT**：实际扫码枪模式与条码 Codec 真实样本、跨工程唯一索引/完整切换、忙时丢弃和不误记录。
4. **DEVICE/FW**：夹具到位证据、实体 START/STOP 真实 USB/IO 输入和去抖/失联动作；没有验证不解锁生产状态机。
5. **Production**：独立业务状态机只调 DEVICE/FLASH/TEST/RECORD 现有技术服务；实体 START 导致每台一次真正 FlashAttempt，再 TEST 全量 Enabled；不复制执行器。
6. **RECORD/REPORT**：Inspection+FlashAttempt+CaseRun+原始 CAN、单台固定报告；批量/首次合格政策最后按用户实际审查样例冻结。
7. **全系统验收**：先 Linux/静态相关测试与 Windows CI/启动（不能代替真实 WPF/USB），再真枪/夹具/按钮/ECU 集中测试正反案例；验收后才报告具体通过范围。

**明确不擅定**：UT1 字符位数/CRC是否强制、扫描枪真实 HID vs COM、主控实体 START/STOP 的固件上报、夹具真实到位传感器、Flash 生产一次授权合同、FAIL 后夹具/IGN 状态、批次统计和正式放行政策、R1 当前版本准出是否强制完整产线。后续用户实物实测可逐项冻结；不要回问用户已批准的“每台必烧录”等业务决策。

## 9. 独立外部研究核对（为何是这个分层）

- NI TestStand Process Model 将 UUT 标识、前后工序和报告等过程从产品 TEST 序列中分开；其 `Test UUTs` 和 `Single Pass` 体现连续生产与工程师单次测试使用不同入口，但可以共享底层测试：
  https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
  https://www.ni.com/docs/en-US/bundle/teststand/page/process-model-architecture.html
- Windows 扫码输入模式不是“一根 USB 线”就固定的：普通 keyboard wedge 和 HID POS 不同，`SerialPort.DataReceived` 不保证按每一字节触发；需要真实设备确定驱动与分帧：
  https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure
  https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived

**最终结论**：SCAN 的正式职责是“从可验证的实体输入得出型号/终身唯一编号，并把合法身份交给生产站点”；PROJECT 唯一确定生产资源，FLASH 是每台前置必做，TEST 是全部 Enabled 真功能运行，RECORD 是每次独立事实。S-11～S-13 已说明该架构，本 S-14 进一步确认源码没有正式 ModelCode 字段、旧 TEST Flash 可能导致双刷、实体 START/夹具就绪证据不足、任务权限需从研发 SHA 迁移到可审生产实体事件。**业务已确认，源码和现场尚未完成；不重返旧多 Recipe / NoFlash / PC逐台确认路径。**
