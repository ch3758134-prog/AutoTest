# AutoTest R1-SCAN S-04｜NI TestStand / SystemLink 与扫码测试工作流全面对标研究

> 2026-11-21；Issue #72。定位：**第二轮研究，重新审查 S-01 的“逐编号精确绑定默认 P0”前提**；不是已批准生产合同。研究时远端 `R1@02f18c65e5ba6038da10067c6285450de6cce911`，SCAN 研究分支与 R1 分离；R1 的 `ProjectScanMatcher` 仍只返回 RecipeId/Identity/状态，不形成完整执行计划。
>
> 结论状态：`NI_PRIMARY_SOURCES_REVIEWED / MODELS_REASSESSED / DECISION_PENDING / NO_CODE_CHANGE / SCANNER_FIELD_NOT_VERIFIED`。本报告区分 NI 实际能力、NI 演示示例、AutoTest 自主设计建议。**不能用 NI 文档给 AutoTest 的具体生产政策背书**。

## 1. 用户真正希望解决的不是“扫个码”，而是测试上下文的可重复准备

用户已明确：由外部业务生成唯一 DUT 编号；AutoTest 只接收；扫码与配置关系独立管理；实际用途是自动选择或确认测试项目/有序用例、DBC/TCF/按键工位、ECU CAN 速率、Flash Profile/固件、报告与复检；研发生态不扫码亦能运行单步/用例/勾选范围。

完整问题至少分六个可分离的对象：

1. `ScannerDevice`：USB HID keyboard / Windows POS HID / verified Serial 等设备，决定字符输入方法、尾缀和来源可信度。
2. `DutIdentity`：唯一外部编号及可选规则字段；不能推定 ECU 型号或硬件版本。
3. `ProductVariant`：产品/型号/硬件版本等受控业务身份，可来自工单、产品码、外部编号档案或工位人工确认。
4. `TestAssignment`：产品/生产任务/精确编号→已发布测试方案（项目/程序/子集、参数、TCF/DBC、Flash）。
5. `TestExecution`：现有 R1 的真实测试、硬件授权、FLASH 安全边界；不是 SCAN 的新执行器。
6. `Traceability`：原始扫码、来源、配置决策、运行计划版本、真实各用例 RunId、真实结果、正式出厂放行事实。

**最重要的逻辑断点**：如果编号不携带产品信息、没有外部 SN→型号档案、也未选择当前产品/任务，那么仅凭一串随机 SN **无法知道**该 ECU 用什么 DBC/固件/测试范围。这不是正则或 UI 能解决的问题，必须声明“配置身份依据何在”。

## 2. NI TestStand 的真实产品架构和可借鉴点

### N-01：Process Model ≠ DUT 测试序列

NI 官方 `Process Models` 和 `Process Model Development and Customization` 明确将 UUT 识别、结果显示、报告和数据库日志放入独立过程模型；产品专属 `MainSequence` 负责测试逻辑。Process Model 还提供配置入口、回调与结果处理插件。

**AutoTest 对应**：`Scan/Production` 应作为 TEST 运行前/后的通用编排层，TEST 的准备/测试/清理和 `RealRunCoordinator` 不要塞进序列号管理代码。独立扫码匹配不应编辑/注入测试步骤以作到“看起来跑起来”。

来源：
- https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html
- https://www.ni.com/en/support/documentation/supplemental/06/a-guide-to-developing-effective--maintainable-teststand-sequence.html

### N-02：Test UUTs 与 Single Pass 各自有用

NI `Test UUTs` 是识别每只产品并连续执行的入口，`Single Pass` 可对一个 DUT 执行一次而不先弹 UUT 序列号确认。两者可复用同一 MainSequence/测试后端。

**AutoTest 对应**：两个显式入口——`Production/Scan-identified` 和 `Development/SingleStep-or-SelectedCases`——共享真实执行/证据，但**只有前者在成功的已发布业务身份和生产授权后计入生产数据**。不扫码研发是正统工业软件工作流，不是放宽安全的临时缺口。

来源：
- https://www.ni.com/en/shop/electronic-test-instrumentation/application-software-for-electronic-test-and-instrumentation-category/what-is-teststand/test-management-software-developers-guide.html
- https://www.ni.com/docs/en-US/bundle/teststand/page/teststand-process-models.html

### N-03：PreUUT 是身份采集切入点，可覆盖/定制

NI 的 `PreUUT` 回调常用于请求序列号，可换成条码读取；`UUT.AdditionalData` 可扩充型号等信息。**记录编号并不意味着它自动选择了哪份测试工程**；装配逻辑取决于定制过程模型/业务查表。

**AutoTest 对应**：可有 `BeforeUutStart` 风格的通用、可配置“识别→解析→解析方案→预检→显示→人工开始”，但不要假定规则匹配就是身份充分验证；不照搬 NI callback/sequence-file 文件格式。

来源：
- https://www.ni.com/en/support/documentation/supplemental/07/using-callbacks-in-ni-teststand.html
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html
- https://knowledge.ni.com/KnowledgeArticleDetails?id=kA03q000000YHmUCAW&l=en-US

### N-04：动态选择程序和加载参数，是两件不同的事

NI 2005 自动测试峰会官方演示有“读序列号的前缀→动态选择另一产品的测试序列”；还展示同一产品家族的**共享序列只加载不同参数/限值**。2026 NI 官方新材料亦明确按扫描产品码或配置文件改变同一系列测试的参数和流程。

**必须辨别**：2005 是**演示**（A 开头→产品 A，B 开头→产品 B，其余→产品 C），不意味着严格产线应把“不认识前缀”默认放行到产品 C。AutoTest 正式生产 **未知产品码或多命中必须拒绝**，不得采用这种 demo fallback。

**AutoTest 对应**：
- `TestScope` 决定哪个项目/用例/顺序；
- `ParameterProfile`（未来若需要）决定相同测试程序的规格限值/型号化参数；
- `SystemConfiguration` 决定 TCF/DBC/KEY/TBC；
- `FlashJob` 决定是否烧录、哪个固件/Profile/安全文件，依然使用原 Flash 授权。

来源（已读取 PDF 且截取演示原页截图，2005 演示不能当 2026 产品默认行为）：
- https://www.ni.com/pdf/testsummit/us/TestStand.pdf （第 11 页，印刷页 71）
- https://www.ni.com/en/shop/labview/labview-plus-suite/level-up-your-automation/test-automation-sequencing-ai-driven-world.html

### N-05：测试标识、产品编码、批号、操作员和实际运行结果必须分离

NI TestStand `UUT_RESULT` 表标准字段含 `UUT_SERIAL_NUMBER`、`PART_NUMBER`、`STATION_ID`、`BATCH_SERIAL_NUMBER`（Batch Process Model 用途）、`TEST_SOCKET_INDEX`、操作者/时间/UUT_STATUS；其 `SystemLink` 用 DUT 对象、产品、工作项及测试结果支持查询和关联。

**AutoTest 对应**：外部 SN、产品型号/版本、生产任务/批次、工位身份、测试项目版本、单次检测/用例 RunId、最终 release 各自独立。注意 NI `Batch Process Model` 的 batch 指**多 UUT 同组并行执行**；AutoTest 目前**单工位单 DUT**，绝不因此设计 NI Batch/Parallel 模型。

建议术语避免碰撞：`ProductionLotId`（外部生产批次）≠`InspectionSessionId`（本只 ECU 的本次检测会话）≠`CaseRunId`（R1 单用例实际 RunId）≠`ReleaseId`（独立放行）。现有 S-01 用 `BatchId` 的说法需加明确定义，不能误作 NI 多工位 Batch。

来源：
- https://www.ni.com/docs/en-US/bundle/teststand/page/uut-result-table-schema.html
- https://www.ni.com/en/shop/electronic-test-instrumentation/application-software-for-electronic-test-and-instrumentation-category/systemlink/select-edition.html

### N-06：开发/操作界面分离，测试追溯并不要求工程师总去扫码

NI 推荐测试开发者在 Sequence Editor 开发，而生产操作员使用 Operator Interface 运行和查看结果；不同角色可以获得不同入口与权限。

**AutoTest 对应**：用户已明确在统一 WPF 主工具中维护工程/编辑 TEST/工具/测试；可增加轻量 `研发/生产任务` 模式状态，而不是强迫所有用户从一个扫码页面进入、或为扫码独立启动一套上位机。

来源：
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-user-interface-development-best-practices.html

## 3. 四种互补而非互斥的关联模式

| 模式 | 决策依据 | 操作示例（虚构） | 优点 | 局限/不可被推断的事实 | 是否推荐 R1 首期 |
| --- | --- | --- | --- | --- | --- |
| A 固定任务 | 当前工位人工选定且已批准的 `ActiveTestAssignment` | 工程师选“HydEPB A版出厂检测”→对外部编号逐个扫码→用同一任务测试 | 最短路径，无每只 SN 的预录入负担；支持研发转生产 | 只证明操作员将 SN 归入当前任务；**不能仅靠随机 SN 自动证明 ECU 实物型号**。需明确生产准入策略、物理人工校验/DID 辅助 | **建议 P0 最先打通，前提有已确认任务/产品** |
| B 编号解析产品码 | 已发布规则从编码提取 `ProductCode`，经独立 `ProductCode→Recipe` 映射 | `EPB-A-0000123`→ProductCode=EPB-A→绑定方案 A | 无逐 SN 表；支持少数产品混用 | 若前缀不真正受外部编号规范保证，不能依赖它；多命中/未知拒绝 | P1 / P0 有真实编码样例才做 |
| C 外部生产任务/工单 | `WorkOrderId/LotId→ProductVariant→Recipe`，每只 SN 只关联当前任务；可选 SN 名单校验 | 先载入 WO-01: Model A, HW r2→扫码 SN→检查归属→测试 | 批次一致，改配置只发布任务映射；正式连续生产更自然 | 无权威 SN 名单时只能保证“当前任务被选择”，不能证明某 SN 属于该任务 | **适合后续正式生产优先考虑** |
| D 精确编号档案 | 外部正式发布 `SerialNumber→Variant/WorkOrder/Recipe` 关系 | `SN000123→A/r2/Recipe-A` | 严格混线/返修追踪；精确身份 | 需要可靠导入、维护、更新/撤销，按只管理成本高；不是所有产线都能提供 | 可选严格场景，**不宜未经确认设为全部 P0 强制条件** |

**改进旧结论**：S-01 第 1 节“R1 P0 绑定粒度：逐个外部编号精确匹配”降为**研究候选，不再作为通用默认**。保留“规则和匹配关系分离”的已批准业务约束，但“匹配关系”的键可显式选择为 `CurrentAssignment / ProductCode / WorkOrder / ExternalDutKey`；任何一种都必须引用已批准 Recipe/资源，不能随意选最新。**模式 A 不等于“无需匹配关系”**：它的关系是 `ActiveAssignment→Recipe`，只是外部 SN 不作逐一授权查询。

### 4 个边界必须显式处理

1. **仅随机编号，没有生产任务/业务档案/产品信息**：不能决定测试程序，生产自动选配阻断；研发可自己选择测试程序并在报告中记录编号。
2. **扫码字段提供型号但没有独立映射**：仅得到 ProductCode，不自动决定 DBC/Flash；需要产品码→方案发布映射。
3. **一个当前任务对应多个产品修订**：不允许用当前工位上下文消解歧义；要添加明确变型选择或从权威档案查。
4. **同一编号发生返修或临时改工艺**：需显式新 InspectionSession/RetestReason/新 Recipe 版本引用，保留原历史，不把旧报告原地重写。

## 4. 扫码枪硬件配置 vs 上位机输入设置 vs 编号与测试业务配置

### 4.1 扫码枪/Windows 传输层

**首选 R1**：USB keyboard wedge + 专用扫码输入上下文。典型设备需核验 USB HID Keyboard 模式、前缀（通常关闭）、后缀 Enter/CRLF（推荐）、字符集/键盘布局、条码类型、字符大小写、键速、Tab 作为分隔符的行为；配置通常由设备提供的“扫描设置条码”或厂商程序执行，不能假设 AutoTest 可通用地控制枪固件。

**其他接口**：Windows HID POS Scanner 可使用系统条码设备接口；Raw Input 可区分不同键盘类 HID 源，但实现需谨慎，不应有全局 Hook 抢走工程编辑器输入；串口/COM 只有确认现场具体枪型号/协议后实现。`KeyboardWedge` 普通 WPF TextInput 自身不证明来源是实体扫码枪；生产若需要可靠设备身份，不能靠“输入很快”区分人手与设备。

来源：
- https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure
- https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input
- https://docs.zebra.com/us/en/scanners/general/ds4-series/ds4678-prg/c-ds4678-getting-started/r-ds4678-interfaces.html
- https://docs.zebra.com/us/en/scanners/general/sp72-prg/c-sp72-getting-started/c-sp72-host-interfaces.html
- https://knowledge.ni.com/KnowledgeArticleDetails?id=kA03q000000wzJKCAY&l=en-US （NI 焦点/Tab 分隔符案例）

### 4.2 AutoTest 输入预处理层

保存 `CaptureSource`（KeyboardFieldUnverified / HIDPOSVerified / SerialVerified / ManualTyped / Paste）、`RawText`、`RawHash`、时间、`ScanEventId` 和焦点。可按明确设备契约消去**传输尾缀** CR/LF，但不可静默改变条码实质字符（保留前导零、大小写；仅已发布规则允许归一化）。`Enter` 事件只产生扫码完成/解析任务，不进行实体控制；运行中扫入下一只立即拒绝、不排队，不允许丢失当前 DUT 归属。

### 4.3 编号规则和关联层

规范化身份、检查字符/长度/可选校验位、解析产品码/批号、碰撞冲突检测、查询 `ResolutionMode` 绑定，预览为何选中了某配方（例如“当前工位任务：A-v2”）。**不能把扫码设备后缀设置和产品编号正则放在同一张配置表，不能用正则捕获直接代替已发布配方映射。**

### 4.4 测试方案与硬件准备层

`Recipe` 明确冻结 `TestProject`/有序 EntryId/或 TestProgram、TCF/DBC/KEY/TBC、CAN 波特率、是否烧录/真实 Flash job、报告政策。编辑来源独立于扫码；预检检查工程 asset SHA、设备 epoch、0x43 读回、所有启用步骤/Flash 文件。扫码只是选择计划，不是计划本身，也不授予实体授权。

## 5. 面向 AutoTest 工程的配置组合建议

| 资源/状态对象 | 归属 | 是否需要正式发布 | 是否跟具体编号一一对应 |
| --- | --- | --- | --- |
| `ScannerDeviceProfile` | 工位本机设备配置 | 工位确认即可，变更审计；非 ECU 工程版本 | 否 |
| `IdentityRuleSet` | 工程/客户编码规则 | 生产要发布、版本化 | 否 |
| `ProductVariant` | 型号+HW r + 客户/可选 BOM | 生产要受控 | 否 |
| `ProductionRecipe` | TEST Scope + TCF/DBC/KEY/TBC + CAN rate + Flash/Report | 生产要发布、版本化、可冻结 | 否，多个 DUT 共用一份 |
| `TestAssignment` | 当前任务、产品编码、工单或逐 SN 的绑定 | 生产要明确作用域和 revision | **视模式不同**；不是全部逐号 |
| `InspectionSession` | 一只 DUT 在一次检查中的识别/计划/预检/结果归档 | 固定的运行事实 | 是，本次尝试一只 |
| `RunRecord` | R1 TEST/RECORD | 每个实际执行用例持续耐久存储 | 同只 DUT 可有很多 Run |
| `ReleaseDecision` | 生产政策/RECORD | 独立可审计 | 与特定生产放行作用域对应，不能凭 PASS 自动发出 |

系统应允许不同 ECU 工程使用不同的 `ResolutionMode`；生产模式一次只选择**一个已批准当前任务/策略**，不能随每次扫码以无序优先级在四种模式间“猜最匹配”。一旦绑定选择因多个来源冲突，必须人工审查后重新发布。

## 6. 对前轮 S-01～03 的明确修正

- **保留**：编号来源外部、SCAN/PROJECT/TEST 分层、全资源 SHA/Revision、不扫码研发、二次扫码阻断、实体 Flash 单独授权、正式 RECORD 区分生产与开发、重复不算新增。
- **修正 1**：`DutConfigBindingTable` 不能被视为所有生产模式必有、所有编号必有一行。升级为通用独立 `TestAssignment`（或 `ConfigurationBinding`）概念，包含 `SourceKind+ScopeKey+RecipePin`；逐编号档案是其中一种具体表，必要时保持独立可编辑资源。
- **修正 2**：不能自动假定 `ReleaseScope`、首次放行和复检审批政策已经获用户批准；先以 `TestResult` 与 `ProductionReleaseStatus=NotConfigured/NotReleased` 分开，不在 P0 一开始就建立完整 MES。
- **修正 3**：`ProductionBatchId`/NI Batch 概念可能混淆。明确外部 `ProductionLotId`、本次 `InspectionSessionId`、真实每用例 `RunId`；对多用例同 DUT 批量执行，统一在 `InspectionSessionId` 下关联。
- **修正 4**：若无可信外部型号对应关系，不得宣称 `CurrentAssignment` 自动验证了被测实体型号；应显示 `IdentityAssurance=OperatorDeclared/ExternallyMapped/EcuReadbackCorroborated` 等置信来源，而不是一个布尔 Verified。
- **修正 5**：先做最小可用模式 A（当前任务 + 真实扫码编号 + 现有 TEST）、不因绑定表未建阻断研发；收到真实业务编码样例/工单模式后再按需求扩展模式 B/C/D，而不是一次性设计所有 UI/数据库为 P0。

## 7. 外部资料和研究置信边界

- NI 官方产品用户手册、官方最佳实践、NI TestStand/Test Management 开发指南、NI 2005 Demo、2026 现代配置驱动案例、NI SystemLink 官方说明、Microsoft Windows POS/Raw Input 文档、Zebra 扫描器接口配置资料。上述均附原始链接。
- NI 2005 演示不代表今天所有产线必须“凭条码前缀决定测试”。SystemLink 的 DUT/Work Items 提供产品思路，但用户 AutoTest 当前是本地离线单工位，不建议照搬云平台/MES/多 socket 调度。
- 未取得现场扫码枪实际型号/后缀、外部条码样例、现有业务编号清单格式、ECU 实际 DID/硬件版本读取合同，不能假称设备身份已验证；不得虚构真实产品编码格式。
- 以上为候审推荐，用户或项目管理审批前不对 R1 生产 Schema/发布/放行做无条件迁移。
