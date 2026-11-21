# AutoTest R1-SCAN S-08｜扫码枪、唯一 DUT 编号、产品配置与生产自动测试：全系统研究与产品设计 v0.3

> 时间：2026-11-21。任务：Issue #72。研究分支：`research/r1-scan-configuration-20261120`。
>
> **输入约束**：用户要的是“扫码得到编号，自动找到实际 ECU 应跑的工程、用例、DBC、TCF、按键、CAN 波特率、烧录配置并保存真实报告”，又明确**编码方案可以自己设计**。上一轮 S-00～S-07 已研究源代码、NI 和初步 UT1 草案；本文件将它们整合成**一个可逐步落实的产品逻辑**，纠正只谈 Regex、只谈逐编号表、只谈单一编码格式的局限。
>
> **当前状态**：`RESEARCHED / ARCHITECTURE_RECOMMENDED / NOT_APPROVED_FOR_PRODUCTION / NO_CODE_CHANGE / NO_PHYSICAL_VALIDATION`。2026-11-21 回读 `R1@8812847cd814a344e4c7ed4791fa59bf02304238`；本稿不改 R1、main、固件或生产 Schema。冻结/发布、发行方、出厂放行细节需用户/项目管理/RECORD 评审。

## 一、先解释“扫码配置”实际上包含六个问题

1. **输入设备**：扫码枪是什么接口、Windows 怎样得到完整数据；这是设备配置，不是产品编码。
2. **编号发行与标签**：是谁给实体 ECU 分配终身唯一号、何时贴标签、编码如何版本化与检错；AutoTest 当前只消费已发行编号，不暗自发号。
3. **产品身份**：扫描数据中能否明确得到型号、客户物料号、硬件版？得到的只是**标签声明**，不是 ECU DID 实体读回。
4. **生产任务与配置匹配**：如何用产品身份 + 当前任务/工单定位**唯一已发布**的测试方案；此关系必须独立、可追溯、可修改，不在扫码 Regex 中塞固件或 DBC。
5. **实测运行**：如何冻结测试项目与有序用例、TCF/DBC/KEY/TBC、ECU CAN 波特率及 Flash 完整配置，再受控调用 R1 唯一的真实 TEST/FLASH 入口。
6. **追溯/统计**：何为一次扫码、一次真实检测、一次复检、一次正式放行；如何让同一 DUT 的多用例 RunId 同属一检测会话，且不重复统计新出厂件数。

**关键产品答案**：扫码只启动“识别与准备测试方案”工作流，不在扫码完成时执行实体 IO/CAN/Flash；用户的工程师调试模式依旧无需扫码。一个成熟产品不用每次从条码格式重新选择 DBC/HEX，而是从已发布的产品配方中**唯一地恢复整个测试上下文**。

## 二、工业参考与对应逻辑（经过官方资料核对）

| 参考 | 外部产品真正做了什么 | 对 AutoTest 应借鉴的设计 | 不应照搬 |
| --- | --- | --- | --- |
| **NI TestStand Process Model / PreUUT / Test UUTs** | 独立处理 DUT 输入、测试前后共性动作、报告；测试序列专注产品测试；Single Pass 和连续 UUT 是不同运行入口 | 在 TEST 外有受控扫码/生产任务协调层，研发不强制扫 | 不造第二 TEST DSL；无需改成 NI 的 seq 文件格式 |
| **NI TestStand Property Loader** | 测试序列可从配置文件/数据库取得不同数值限值及属性 | 相同 TEST 程序可按产品版号引用不同冻结参数/限值；更改配置无需重贴 DUT 标签 | R1 未有完整该机制前，不假称已经可任意运行时覆盖全部参数 |
| **NI UUT_RESULT / SystemLink 产品追溯** | DUT 编号、PART NUMBER、工位、结果、运行记录分离，并可关联产品/测试活动 | 分离产品、任务、检测会话、每次 Run、出厂放行 | 当前用户单 PC 离线，不能因 NI 有云服务就引入强制 MES/云依赖 |
| **GS1 AI (01)/(21)/(10)** | 用结构化产品号、序列号、批号表达产品身份；GS1 DataMatrix/其他物理符号又是独立层 | 分离“实体唯一身份”“产品类型”“批次”，保留正式 GS1 接口的扩展可能 | 自定义 UT1 不是 GTIN/GS1，不能冒称 GS1；物理 2D 需要扫描器真支持 |
| **ISA-95 / OPC UA Job Order** | 区分生产工单/任务、设备/物料资源与实际执行事实 | 工作任务/工位允许范围约束与配置 Recipe 分离 | 不为首版上位机实现整套 MES、工单调度或多工位生产系统 |
| **Microsoft POS/Raw Input + Zebra USB 模式** | keyboard wedge 文本、HID POS Scanner 独占 claim、USB CDC 虚拟串口是不同接入方式 | 实机先确认 HID/COM/其他，分别适配，失败/离线状态可见 | 不能只凭 TextBox 里收到快速字符称“真实设备已验证” |

权威资料：
- NI：https://www.ni.com/docs/en-US/bundle/teststand/page/process-model-architecture.html
- NI：https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html
- NI：https://www.ni.com/en/support/documentation/supplemental/06/a-guide-to-developing-effective--maintainable-teststand-sequence.html
- NI：https://www.ni.com/docs/en-US/bundle/teststand/page/uut-result-table-schema.html
- NI：https://www.ni.com/docs/en-US/bundle/systemlink/page/products-overview.html
- GS1：https://ref.gs1.org/ai/
- GS1：https://support.gs1.org/support/solutions/articles/43000734238-how-does-serialisation-differ-from-unique-identification-in-the-gs1-system-/
- ISA-95：https://reference.opcfoundation.org/specs/OPC-10031-4/4.1
- Windows：https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure
- Windows：https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input
- Zebra：https://docs.zebra.com/us/en/scanners/general/sm72-ig/c-sp72-param-desc-usb-interface/r-sp72-usb-parameter-defaults.html

## 三、先区分五种业务标识，禁止“一码包打天下”

| 概念 | 举例（虚构） | 生命周期/来源 | 能否作为 DUT 永久主键 |
| --- | --- | --- | --- |
| `DutSerial` | `000000123456` | 外部唯一编号发行方分配，每只 ECU 唯一、终身保留 | **是**：若发行范围确保全局唯一；否则 `Issuer+Serial` |
| `ProductCode` | `EPBA` | 产品代码注册表；区分客户、目标 ECU 型号/产品变型 | 否，多只 ECU 共用 |
| `HardwareCode` | `H02` | 实际硬件修订，受产品工程管理；变更必须审计 | 否 |
| `WorkOrder/Lot` | `WO-26-001` | 外部工单/批号或当前任务；可以换工艺或重测 | 否 |
| `RecipeId/Revision/SHA` | `EOL-EPBA@r3` | AutoTest 工程配置，可变更/发布，绝不可直接写进永久条码作为唯一配置内容 | 否 |

同一 Serial 对应不同 ProductCode/HW 的标签时，先提示身份冲突，不以 `Product+HW+Serial` 的复合编码换出新的“新 DUT”。重贴标签、返修、换硬件版、改变烧录软件时，**身份不应悄悄改变**。如果企业实际只有“每型号局部唯一”的 SN，那么编号发行合同必须改成 `Issuer+ProductScope+Serial`，并明确跨规格重标的身份迁移，不能假装全球唯一。

## 四、可自主设计的编码规范：不只研究格式，也研究发行责任和标签物理可读性

### 4.1 格式选择

| 方案 | 示例 | 适用 | 推荐 |
| --- | --- | --- | --- |
| 只有序列号 | `000000123456` | 已有客户码、固定工位/逐号档案 | 必须兼容，**不是新码默认** |
| **版本化结构化码** | `UT1-EPBA-H02-000000123456-4372` | 可自定义新标签、需要扫码识别产品/HW | **新产品推荐候选** |
| GS1 体系 | 合法 GTIN AI(01)+Serial AI(21)，可选批号 AI(10) | 供应链确实要求 GS1 且有合法产品标识时 | 有条件扩展，不能用假 GTIN |

UT1 v0.1 是 **S-07 已计算校验向量的候选**：
- 固定协议标识 `UT1`；产品码 `[A-Z0-9]{4,8}`；硬件版码 `H[0-9]{2}`；独立 Serial `[A-Z0-9]{8,20}`；末尾四位大写 HEX CRC16。
- CRC 为 CRC-16/CCITT-FALSE，ASCII 范围从 `UT1` 到 Serial 的最后字符（包含中间连接符，不含最终 `-CRC`），poly=0x1021、init=0xFFFF、non-reflected、xorout=0。示例 `UT1-EPBA-H02-000000123456-4372`。
- **任何二维码图形只是该 ASCII 数据的承载方式**，不决定逻辑格式。实际可选 Code128（一维）或普通 DataMatrix/QR（二维）。UT1 这类较长负载使用 Code128 前要核对实际标签宽度、对比度、打印 dpi、扫距与读码器一维能力；优先以实物测试决定二维是否可用。
- CRC 只能防止常见输入错误，不防复制、伪造、错贴；若对真实性要求高，需要独立外部编号成员表/ECU DID 校核/发行凭证/工位防错，不能把 CRC 宣称为“防伪认证”。
- **标签内容不编码** DBC 名称、测试程序路径、固件文件、CAN 波特率、生产任务 ID、变动的测试限值或出厂结果；否则每次测试配置升级都要重印实体标签。
- 不建议为所有场景强制把生产日期/生产工单纳入固定标签，除非实际客户规则要求。保持短码与现场可读性。

### 4.2 发行与生命周期：仅“我们能设计”不足以产生唯一性

- `BarcodeSchema`：由 AutoTest/企业共同定义，发布 `ProtocolVersion/字段/CRC/兼容规则`；
- `DutSerialIssuer`：外部生产/标签发行方在打印/粘贴前分配唯一 Serial，维护不重用与已签发清单；AutoTest 当前**只消费，不发号、不生成标签**；
- `Label`：具体物理图形（Code128/DataMatrix/QR），包括必要的人眼可读产品代码与 Serial，不能因重印生成另一个 Serial；
- `ProductRegistry`：保存 ProductCode 与 HW 版的正式含义、批准状态，不能靠正则匹配任意新码为合法产品；
- `ScanParser`：扫描时按版本校验、得到原始标签声明；后续以独立绑定关系找到测试方案；
- `LegacyAdapter`：保留旧纯 SN，显式选择对应已发布任务/外部档案进行配置，不暗中拼新 UT1 或允许模糊匹配。

**研究仍需冻结**：Serial 的真实发行范围、实际标签一维/二维容量、二维码印制位置/尺寸、码符号前后缀与扫描器真实输出。如果以后用户希望软件发号/打印标签，则那是**独立的发行/标签工作流项目**，需要唯一性事务和打印重试审计，不应在 SCAN 首版偷偷加入。

## 五、最重要：定义 AutoTest 真正的“编号与实际测试配置对应”关系

**应维护三个核心业务配置对象（逻辑分层，可用一个成熟配置工作台编辑，不必强行画成三个彼此遮挡的窗口）：**

### 5.1 产品档案 ProductVariant

`VariantId, ProductCode, HardwareCode, CustomerPartNumber?, EcuFamily, ExpectedEcuIdentityHints?, Status, Revision, Sha256`。

用途：注册 `EPBA/H02` 是一个合法受控产品，而非收到任意字符串都能找到通配 Recipe。客户产品名、物料号、硬件版显示为人类能理解的文字。

### 5.2 测试方案 ProductionRecipe

`RecipeId/Revision/SHA` 统一引用以下原有工程资源：

| 方案内容 | AutoTest 实际资源/规则 | 必须冻结的内容 |
| --- | --- | --- |
| **工程** | Project Manifest | 项目 ID、当前实际资源安装位置、Project 修订/SHA |
| **测试任务范围** | TestProject、TestDocument | 正式项目 AssetId/Revision/SHA、稳定的有序 EntryId 列表和每个 TestDocument SHA；不能用研发界面临时勾选充当正式方案 |
| **测试系统 TCF** | TestSystemConfigurationDocument | DBC/20 Key Controller、其他测试读写所需源引用和 SHA |
| **工位台架 TBC** | TestBenchConfigurationDocument | 真实主控固件能力、通道物理角色和设备配置，禁止虚构重映射 |
| **CAN** | ECU CAN 软件目标波特率 | 每次运行前 `0x43` 在同一硬件会话读回；需要改变时明确由受控工位操作 `0x42`、再次读回；KEY CAN 固定 |
| **Flash** | 原 TEST 启用的 Flash 步骤与 ConfiguredRealTestFlashPort | 原 XML Profile、HEX、安全 DLL/Key、ECU target、SHA 和实际高危操作授权，不把扫到 `RequiresFlash=true` 当作可以写入 |
| **记录/判定** | TEST-RECORD | 正式用例清单、证据及报告策略、不完整/Unknown 不能当 PASS |

一个测试方案属于产品/硬件变型，但同一变型可能存在**多份不同工艺/工单下的方案**，必须靠下一层任务关系消解，而不能“自动选最新”。

### 5.3 关联配置 TestAssignment / RoutingPolicy

**统一流程不是 `SN→Recipe` 单表，而是 `可信产品/任务上下文→唯一已发布 Recipe`。**

| 实际模式 | 完整选择键 | 适用范围 | 处理冲突 |
| --- | --- | --- | --- |
| **新格式 + 固定当班产品** | `TaskId + ProductCode + HardwareCode` | R1 EPB 单工位首版**优先** | 扫码的 Product/HW 与任务期望不符立刻拒绝 |
| **新格式 + 已批准混线** | `ProductCode + HardwareCode (+ 可选 TaskScope)` | 同一工位不同 ECU 自动选择正式方案 | 多 Recipe/多个工程歧义，必须停下确认，不自动选“最新” |
| **外部工单** | `WorkOrderId + ProductVariant (+ 可选 SN 清单)` | 同一型号不同生产订单/软件目标版本 | 无 SN 成员清单时只能声明“本任务选了此产品”，不能证明 SN 属于该工单 |
| **严格逐号档案** | `Issuer + DutSerial`（或已确认全局 Serial） | 返修、混线严格归属与权威外部清单 | 未命中不能静默回退固定任务 |
| **旧纯编号 + 固定任务** | `TaskId + 纯 Serial` | 兼容历史标签/首版研发 | 只将 SN 归于人为指定的当前任务，不能声称“扫出产品型号” |

“固定工位”与“自动按码选择”不是相互排斥：**先选择本次任务限制允许的产品/工艺，扫码解析的产品码做二次核验，然后通过该任务的精确映射查 Recipe。** 如果用户将来批准混线，切换已发布路由策略即可自动按产品类型查找，而不改 TEST/Flash 逻辑。

**示例（虚构）**：
```text
生产任务：LT-EPB-A-2026-01，限定 EPBA/H02，绑定 Recipe R-EPBA-EOL@r3
扫码：UT1-EPBA-H02-000000123456-4372
  → 解析格式 UT1 / 校验 CRC / 读取 Serial=000000123456
  → 产品档案 EPBA/H02 合法
  → 任务限制 EPBA/H02 相符
  → 绑定找到 R-EPBA-EOL@r3（有 Rev/SHA；不是“最新工程”）
  → 冻结“当前 TestProject 的用例 1/3/5”、DBC、TCF/TBC、ECU CAN 速率
  → 仅显示方案预览和只读预检；等待明确开始
```

若产品/硬件版本与任务不符、关联歧义、资源已修改、缺文件/配置，就显示具体原因；**绝不能自动从错误产品降级使用“当前打开的测试程序”**。

## 六、跨工程方案必须有安装目录索引，但首版不必实现 MES

目前 R1 的 `ProjectScanMatcher` 只从**当前打开工程**检索扫码配置，不能在工位有多个工程时凭标签查找到尚未打开的工程。长期应有**工位侧受控安装目录 `StationProjectCatalog`**：

- `InstalledProjectId, CanonicalPath, ManifestRevision/SHA, ValidStatus, AllowedVariants, ApprovedRecipePins`；
- 只有明确批准并安装的工程能进入生产路由索引；
- 扫码命中其他工程时，运行必须先确认工位空闲、断开/释放旧会话、装载并校验新工程，再准备测试；不能运行中静默替换项目；
- 外部硬盘路径失效/工程被改/多候选时阻断，要求用户重新安装/发布，**不自动网上下载最新测试代码**。

**R1 最短可测路径**暂在同一个已经打开的工程内使用固定任务+合法产品码，随后扩展到同工位多工程目录。这样既满足最终“扫不同 ECU 自动配置”，也不让首版等待项目安装器/企业 MES 完成。

## 七、扫描枪应该怎样配置？（设备层不管理测试方案）

### 7.1 首次设备识别

现场先看 Windows **设备管理器中真实的枪型号和枚举方式**，并用真实标签/记事本或原始串口读取验证原始输出：
- **USB HID Keyboard**：需要已知焦点的专用输入区、正确键盘布局、结束符（Enter/CR/LF）、避免 Tab 把焦点跳走；普通输入框**无法证明字符来自枪**。
- **USB CDC / 传统 COM**：需要正确端口、可选设备稳定实例/VID/PID/序列、传输配置、CR/LF 或厂商字节分帧、短包/断线/复连；当前 `SerialBarcodeCapture` 只有 `IVerifiedBarcodeLineSource` 接口，**尚未写真实 SerialPort 驱动**。
- **HID POS Scanner**：在支持此模式的设备/Windows 上，可按系统 POS API 枚举和 claim 独占设备，更清楚地区分读码事件；不是所有 HID 键盘枪都会出现在该 API。

初始 Issue #72 写 HID 优先，历史调查评论提出可能是实际 USB/COM；**设备实物型号、设备管理器枚举、原始输入尚未拿到**，所以不能无证据就规定使用其中一种。R1 研发可先保留 keyboard wedge，真正现场接线按确认的输入设备实现。

### 7.2 AutoTest 扫码设备设置，建议仅这些字段

- 输入方式和当前绑定设备；真实连接/设备标识/最近成功读码；
- 数据框格式、CR/LF/Enter/STX-ETX 等**由设备手册与原始输出确认**的结束方式，最大长度/超时；
- 字符集与键盘布局、是否包含前/后缀、条码类型（支持 1D/2D）、是否启用厂商 symbology ID；
- 输入测试区：显示完整原文、HEX/可见字符、终止符、记录时间和来源，不做任何 DUT/IO 行为；
- 允许/拒绝策略：运行中、准备报告、上一个 DUT 未清台、输入不完整、设备掉线时拒绝新扫描，不在后台积压；
- 人工输入/粘贴（开发可用）；生产是否允许人工补录和操作员授权另立政策，不把手工键入伪称经验证的枪。

**扫码枪硬件自己的设置**（HID/CDC 模式、条码类型、蜂鸣器、结束符）可能需扫描厂商配置条码或使用厂商软件；**AutoTest 不能保证可以修改任意扫码枪固件**。扫描枪串口通信波特率与主控 **ECU CAN 20–1000 kbit/s** 是两套完全不同的设置，UI 不能混淆。

## 八、操作者任务流：不让用户每只 ECU 重配工程

### 8.1 一次性配置（工程师/配置员）

1. 在工程中维护**真实 TEST 程序、项目、有序用例、TCF/DBC/20 KEY/TBC、Flash**；确认当前设备、DBC、判定已可实测。
2. 创建 `ProductVariant`：产品代码、硬件版、客户/型号描述，发布编码规则和真实编号样例的解析测试。
3. 在“产品与测试方案”工作区选择**现有工程 TestProject 与用例子集**，配置需要的 ECU CAN 速率/Flash/记录目标，发布 `ProductionRecipe` 和全部资源 SHA。
4. 建立独立关联：`ProductVariant + 任务范围 → Recipe`；可选当前班次/工单，确定**允许的**产品集合。
5. 选择工位正在执行的已批准任务；只有“已发布”版本供生产扫码使用。**编辑器自动保存的是草稿，不等于自动发布生产授权**。
6. 在真实扫描枪连接之后读一只已知合法样本，用预览检查“匹配原因”与实际 TEST 选择是否正确。

### 8.2 每只 ECU 的正常运行

```text
工位空闲/当前任务有效
  → 操作者安装 DUT，扫码已存在的标签
  → 完整输入 + 版本/CRC/产品代码/HW/Serial 校验
  → 查旧记录：首次、已测未放行、已放行复检、重复扫码？
  → 当前任务与身份/产品关联无歧义
  → 显示本次方案（工程 / 有序用例 / DBC/TCF/KEY / ECU CAN / Flash）
  → 冻结 RunPlan；检查设备、CAN 码率读回、文件 SHA、安全约束
  → 明确确认本次测试开始（如包含 Flash，另有完整指纹授权）
  → 使用原 R1 TEST 执行一个或多个用例，产生各自真实 RunId
  → RECORD 归档真实观测、CAN/DBC/Flash/步骤证据、报告
  → 显示 TestVerdict 与 ReleaseDecision 两个状态
  → 操作者确认取走 DUT /工位恢复→准备下一只
```

**运行中再扫**：明确返回 `STATION_BUSY`，不更换当前 DUT，不排队下一只，不得将第二次扫描悄悄作为下一轮输入。异常/取消后留存本次事实，直到安全收尾和记录归档状态明确。

### 8.3 研发/独立工具

无需 Scanner 配置、不要求 Serial、不要求发布 ProductionRecipe，可以在 TEST 编辑器运行此步骤/此用例/本次勾选范围；UDS/Flash 工具仍按原设备与危险操作安全门禁；产生 Development/Diagnostic 记录但不进入正式生产首次放行计数。

## 九、UI 产品模型：一个主任务区 + 集中配置工作台

不能要求工程师操作一套陌生扫码微服务或十个悬浮弹窗。建议：

- **主窗口仍默认自动测试**。生产时在顶部选择“研发测试 / 生产任务”，生产操作区显著展示当前任务、允许产品、方案修订、扫码设备连接、当前 DUT 编号、状态；
- **“生产配置”用一个工作台**，左侧可按“产品/硬件变型”“编号规则”“方案关联”“生产任务”切换，右侧 Inspector 编辑当前项；不复制 DBC、TEST、Flash 编辑页面，只提供带资源引用的选择器及“打开原工具”；
- **设备设置**在工位设置/扫码设备工具中，有“一键接收测试”，以确认枪的真实 USB/COM 输入；
- **扫码后结果**：上部 DUT 编号与匹配来源，中部产品/工艺/用例范围，底部资源/硬件预检状态、历史重复/复检提醒，清晰的“重新扫 / 查看详情 / 确认开始”；
- **消息要业务可理解**：“扫到 EPBB/H01，与当前任务 EPBA/H02 不符。未执行测试。”而不是向操作者暴露 `CONFIG_MATCHED` 或原始代码异常；
- **生产交互不能打破桌面约束**：工程树保留、布局浅色、属性编辑尽量在常驻侧栏，关闭主程序时自动终止工具窗口；不出现每只 DUT 的重复工程编辑弹窗；
- 高级设置/修订审核可在配置员视图展开，但日常操作员只看到“当前任务已就绪／扫码／匹配／运行／结果”。

## 十、记录必须可靠：ScanEvent ≠ InspectionSession ≠ CaseRun ≠ FirstRelease

```text
CanonicalDutId（长期唯一，含 Issuer 仅在必要时）
  1 ──N ScanEvent（每次扫码）
  1 ──N InspectionSession（每次实际批准并开始的 DUT 检测/复检）
  InspectionSession 1 ──N ActualCaseRunLink（一个有序测试项目的每个真实 RunId）
  ActualCaseRunLink N ──1 RunRecord/StepOccurrence/证据/报告
  CanonicalDutId 1 ──N ReleaseDecisionEvent（独立的正式放行状态变迁）
```

- 扫码两次但不开始：可能两条 ScanEvent，**不增加检测次数和出厂量**；
- 同号失败后重测：独立的 InspectionSession 与每个 RunId，老报告不可改写；次数为复检；
- 同号 PASS 再测：依旧是复检，不因此产生第二个首次出厂件；
- 一组用例部分 PASS、部分 FAIL，不能因某用例 PASS 就整台 ECU 放行；
- `RecordRunMode.Production` 应当只从真实已发布路由/计划/模式进入，不由用户在研发输入 SN 后自行升级；
- 正式“首次放行”的**唯一性/幂等键与权限**属于需用户/RECORD 批准的生产合同，建议以稳定 DUT 身份和明确 ReleaseScope 为基础，不能简单执行 `COUNT(DISTINCT dut_id)`；
- R1 目前多用例有各自真实 RunId，抽象 `ProductionSession` 预造的 RunId 不能替代；需提供 InspectionSession 与真实 RunId 的关系，不再另造统计事实库；
- 离线工位至少要持久保存启动前计划 SHA、每用例结果、报告与“待归档/不确定”的状态，以避免程序断电后误报 PASS；扫描数据与附件可按工厂数据保留政策管控。

## 十一、源代码责任与未接线点（最新 R1 复查）

2026-11-21 对照 `R1@8812847cd814a344e4c7ed4791fa59bf02304238`：

| 路径 | 已验证的当前事实 | 需要的最小受控增量 |
| --- | --- | --- |
| `Specialists/Scan/IdentityRuleSet.cs` | 有全串 Regex、命名捕获，身份键是任意指定字段按长度拼接 | 新版本扫码协议解析/CRC、规范化与稳定 DutSerial 主键；不能旧 `model+serial` 拼接绕过身份冲突 |
| `Specialists/Scan/ConfigMatchService.cs` | 从 Regex 字段 model/customer 筛 Recipe；不能按 ProductVariant/HW/任务绑定 | 独立版本化 `ProductVariantRegistry + PublishedRouteBinding`，冲突拒绝与来源解释 |
| `Desktop/Projects/ProjectScanMatcher.cs` | 只在**当前工程**中查一份已发布 scan-rules，输出 `Code/RecipeId/Identity`，部分 TEST/DBC/Firmware 指纹校验 | `FrozenScanRunPlan`：TestProject/EntryId/TCF/KEY/TBC/码率/Flash实际作业/报告；跨工程后续 StationCatalog |
| `Specialists/Scan/SerialBarcodeCapture.cs` | `IVerifiedBarcodeLineSource` 抽象，未真正打开 COM Port；现有 WPF Shell 主要是 keyboard wedge | 真实设备确证后的相应 COM/HID 适配、输入证据、断线/重复/忙状态 |
| `Desktop/Testing/TestProjectWorkbenchViewModel.cs` | 真正有序多用例执行与每用例 RunId、Flash 指纹授权已存在 | 允许生产计划传入已冻结的 case scope/TCF/TBC 而不依赖工程师 UI 临时勾选 |
| `Desktop/Testing/RealTestWorkbenchService.cs` | 真正真实 USB、TEST/Flash、record；有 DUT 编号仍为 Development，Diagnostic 另用 | 经授权的独立 Production 运行上下文与 InspectionSession→真实 RunId 关联，保留原实际安全门禁 |
| `Application/Production/ProductionWorkflow.cs` / `Records/Queries/ProductionStatistics.cs` | 生产抽象与统计存在，未和实际 R1 生产执行/首次放行事实完整闭环 | 不重复建立生产引擎；区分生产检查/正式放行与 distinct DUT 数 |

**旧配置的迁移风险**：`ScanMatchSettingsDocument` 旧 `SchemaVersion=1` 混合了 Regex Rules+Recipe，且 Recipe 尚未持有工程级有序项目、全部 TCF/TBC/波特率/Flash pins。应保留旧文件可读取，在**显式迁移与资源复核后**产生新草稿；不得静默升级为已发布生产方案，不能强行覆盖活跃 R1。

## 十二、关键风险、反例及应出现的用户提示

| 风险/场景 | 必须行为 |
| --- | --- |
| 新 UT1 通过 CRC，但 ProductCode 未在注册表发布 | **拒绝生产路由**，提示未注册产品 |
| 旧纯 SN 且没有当前任务/权威编号清单 | 生产无从判断型号：拒绝自动配方；开发可以人工选测试 |
| 同一 Serial 被贴成另一型号或硬件版本 | 查询历史返回身份冲突，不另建“新 DUT” |
| 标签型号/HW 与当前当班任务不一致 | 明示不符、禁止测试，而非偷偷选择新 Recipe |
| 同型号多种已发布 Recipe，缺少 TaskScope/WorkOrder | 拒绝歧义，不能选最新或第一条 |
| 扫码枪掉线、HID 输入失焦、串口半包/拼包 | 失败要显示原因；不能拿历史缓冲再执行 |
| 资源文件/DBC/TestProject/HEX 已修改但引用 SHA 未同步发布 | 计划失效，明确哪些资源待重新发布 |
| 未确认 ECU CAN 目标速率 | 运行前读回/明确应用；扫码本身不写主控 |
| 未确认标签/真实 ECU 版本一致 | 若生产策略强制 DID/夹具核验则阻断；不能把 CRC 当实体证明 |
| 已扫码等待预检时修改工程或工位连接掉线 | 旧 RunPlan 失效，需要重新准备 |
| 真实 Flash XML/HEX/安全文件不完整或额外 Flash 未授权 | 阻断 Flash/生产执行，不插入新危险步骤 |
| 同一 DUT 反复扫码/失败复测/通过复测 | 展示历史、区分 ScanEvent/InspectionSession/FirstRelease；绝不重复计新增 |
| 测试中再次扫描或主窗口关闭 | 不覆盖当前 DUT、不排队；已有 TEST 正常停止与清理、报告未决显式保留 |
| 完成测试但证据未归档或结果 Unknown | 不得生产放行、不得计首次出厂 |
| 当前工程未打开、扫到另一个工程的码 | R1 首版明确要求先打开任务工程；后续只从已批准 StationCatalog 切换，不猜路径 |
| 手工/粘贴在测试工具里输入一个合法 UT1 | 可以测试解析；如果源不符合生产政策，不冒充来自真实扫码枪 |

## 十三、建议分阶段实施，直达真实 ECU 而非提前造生产管理平台

| 阶段 | 工作 | 用户实测验收证据 | 明确不做 |
| --- | --- | --- | --- |
| **P0-1 设备通路** | 真实枪枚举/连接、完整文本/分帧、取消/失焦/连扫/输入来源 | 扫真实标签，记录设备模式和收包原文；无任何 ECU 动作 | 不凭 Mock 宣称物理扫码 PASS |
| **P0-2 编号+产品+关联** | 可编辑且版本化 UT1 解析、CRC、产品注册、任务范围与 `ProductVariant→Recipe` 独立映射；旧 SN 兼容 | 合法/非法码、不同 HW/型号、歧义/错任务/版本失效都可预览 | 不逐只强制建绑定、不提前实现 MES |
| **P0-3 冻结计划** | 当前工程 TestProject 有序 Case、TCF/DBC/KEY/TBC/ECU CAN/Flash 真实 SHA+唯一 RunPlan；只读预检/明确授权 | 用用户真实 EPB 项目验证三例：正确、配方缺失、资源修改 | 扫完不直接点火/夹具/烧录 |
| **P0-4 TEST/RECORD 纵向联调** | 用原 R1 真执行器运行无 Flash 的真实 EPB 计划，归档真实 CAN/DBC/步骤证据、统一 InspectionSession/RunId | 真设备、真 ECU、关闭/失败/复测/报告证据 | 不造第二 runner 或录入假的生产数据 |
| **P0-5 生产与 Flash 受控** | 单独 Flash SHA 授权和全链路真实试验、正式复检/放行/计数政策、配置发布权限 | 现场有/无 Flash、同 SN 多次检测与首次出厂唯一性验收 | 不因 TEST PASS 自动发布出厂放行 |
| **P1 扩展** | 多工程 StationCatalog、混线自动选工程、工单/严格逐号档案、GS1、ECU DID 校核和参数 Profile | 多型号及不同生产工艺版本的真案例 | 无真实需求不开发 MES/云/多工位并行 |

**批次策略**：首版每步都必须落在能实际试验的 Windows + ECU 路径上；合同测试可以用输入样本验证解析，但无法替代真枪/真主控/真 ECU 的现场判定。UI 产品工作台遵循独立 UI 分支的浅色/一体布局标准，SCAN 只交业务与字段，不重绘所有 WPF。

## 十四、需要冻结的仅四项外部事实，而不是让用户重新设计软件

1. **编码发行责任**：我们拟定 UT1 格式后，外部系统/标签打印流程能否在贴标签前签发**永不重复**的 Serial？如果不行，后续需独立研究由谁发号，不在扫码解析器中自动生成。
2. **真实工位扫码枪**：具体型号/枚举 HID vs COM，以及能否扫描 2D；这个决定实际设备驱动与物理条码符号。
3. **初期使用场景**：首版是单型号连续测、多个型号交替，还是按工单/客户不同工艺切换？影响先做固定任务约束还是跨工程目录。
4. **实体 ECU 版本识别**：是否有安全只读 UDS DID 或其他被验证的物理型号/软件版本识别手段，以及能否作为正式出厂门禁？没有则提示来源是标签声明/人工工单。

其余主要技术设计都可在这些约束下先做候审方案，不以用户必须先知道 TestStand/数据库/条码协议名为前提。

## 十五、研究主结论与对既有文档的取代关系

- **可自拟编码**使新产品能在标签中声明 ProductCode+HW+Serial；所以不再把“固定工位模式是首版唯一能实现的匹配”当成必要前提，更不强制逐号事先导入。
- **条码永不携带完整测试配置**：编号/型号与测试配方可改；正式选择依据是受控 ProductVariant/任务/发布绑定、资源 SHA 与物理预检。
- **生产任务约束仍非常重要**：知道“这是什么产品”不等于知道“今天应该烧录哪个版本/执行哪份出厂检查工艺”，工单/任务消除同产品不同工艺版本歧义。
- **S-01 逐号绑定强制 P0 已候审撤销**；S-04/S-05 多模式、S-07 UT1/CRC 是研究输入；本 S-08 给完整逻辑，不擅称得到用户生产政策正式批准。
- **可以先走最简试点**：新 UT1 标签 + 当前已打开工程/当班任务 + 单 ProductVariant→Recipe 映射 + 不 Flash 真测试；然后从真实客户/产线反馈迭代标签/扫码和 Recipe 的复杂度。
- **绝不因扫码 Enter 自动点火、夹具动作或危险烧录**；单 PC 单 DUT，研发 TEST 继续不依赖扫码。
