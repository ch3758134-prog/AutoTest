# AutoTest R1｜P0 生产配置体系重构与编码匹配闭环实施记录

**研究/实现基线**：R1@20b03d5b7a2eae71275784223343bd3465193bf5  
**实施分支**：仅 R1，不改 main，不新开分支  
**状态**：源码已提交、人工静态检查；Windows 编译/自动化测试/现场硬件未运行，严禁称为验收 PASS。  
**正式生产**：仍 BLOCKED（可信实体 START、夹具机械反馈、互锁及正式 Inspection 写入/完成链尚未连接）。

## 一、核心领域关系（冻结）

原始扫码 → 已发布多形制规则 → 唯一 DUT 编号 → 可信型号/可选硬件版本 → 唯一已发布 ECU 工程 → 工程内已发布原生 FLASH + 完整有序 TEST + 其 DBC/波形资源 → 活动工位设备预检。

- 生产身份与发行物：StationProductionConfiguration / ProductionBarcodeRule / ProductionIdentityRow / ProductionProductBinding / PublishedStationProductionConfiguration。
- ECU 工程仍为 ProjectDefinition + AutomaticTestResourceContract + NativeFlashPublicationStore；不得用条码引用 TEST、固件或 DBC。
- 物理工位：ProductionFixtureConfiguration 代表单个 SLOT-01、两路可选控制输出（不是两个 DUT）、输入来源、超时和固定安全释放要求。真正机械反馈/START/STOP/互锁仍须 MCU 与独立实物验证，文本输入不能冒充接线或信号。
- 运行：原 ProductionPreparationService 负责工程唯一完整资源冻结，正式实体 START 和 Inspection 仍不予软件绕行。工程实测继续使用真实前置 FLASH 和连续 TEST 项目会话，结果永不计生产数量。
- 工作区：新 ProductionPreparationWindow 通过一条“生产准备”一级入口呈现五阶段，不建立第二套 TEST/FLASH 配方或解释器。

## 二、正式生产身份三种来源

A. FixedStation：当前工位发布固定型号 + 可选硬件变型；整条条码可只有 DUT 编号。  
B. BarcodeModel：已发布编码形制的字段/前缀或受控 UT1 校验提供可信型号，硬件版本可选；严格匹配一个发行工程。  
C. RegistryModel：CSV 受控编号绑定表提供唯一编号到型号、硬件版本的映射。重复、过期、缺失及产品不存在均拒绝；不猜测编号外观，不回退最近编辑工程。

形制支持 Serial 整串、Delimited 分隔字段、Fixed 固定位数、Regex 完整正则命名字段、UT1 既有真实校验格式。每条有规则 ID、版本、启用状态、前缀、长度/字符检查、解析参数、正反样例。规则发布是工位快照中的正式状态，独立草稿不参与生产。Regex 设有限时，不接受未锚定的部分匹配。

多个规则可匹配同一条码且解释完全一致，按稳定 RuleId 选取展示来源；若编号、型号、硬件、批次或发行工程不一致，拒绝 BARCODE_RULE_CONFLICT。即使采样规则通过，也要再次校验工程原始清单、TEST、DBC/波形和已发布 FLASH/固件指纹。

## 三、生产准备操作路径

开发与调试 → 生产准备（一级入口，浅色独立工作区）。

1. **产品与工程**：输入型号/硬件变型，绑定当前工程或选择正式工程清单，查看各型号对应 FLASH、固件 SHA、完整 TEST 用例序列和当前发布版本。
2. **编码形制**：新增/复制/删除多条规则、设置串号/型号/硬件/批次字段、固定长度/分隔/正则/UT1，配置正反样例，检查多规则冲突。
3. **身份匹配**：选择固定型号/编码型号/编号表，编辑或 UTF-8 CSV 导入最多 100000 条编号，检查重复、失效/过期和型号-工程不一致。
4. **工位与夹具**：复用真实 SerialScannerSetupView 配置 COM 波特率、绑定 USB PnP 扫码枪；显式设置单 ECU 位置、两路夹具输出使能、可信反馈及 START/STOP/互锁输入来源与动作超时。未验证输入会在页面显示阻断，不能装作安全有效。
5. **验证与发布**：输入多条样例或使用真实串口扫码枪试扫；显示原始码、规则 ID/版本、唯一编号、型号/硬件、判定来源、工程路径与 SHA、原生 FLASH、固件 SHA、TEST 顺序与工位要求。仅进行离线预览。发布当前 ECU 工程/发布站级映射均会显式检查并保存快照。试扫不得驱动夹具/UDS/FLASH/TEST。

自动保存采用防抖与编辑代次保护，不在数据未修改时无故抬升修订。新规则/编号表草稿可保存但只有全校验通过并审核发布后才参与正式扫码。

## 四、发布文件与旧路径

- 工位产品/编码草稿：%LOCALAPPDATA%/AutoTest/production-preparation/station-draft.json
- 工位已发布映射：%LOCALAPPDATA%/AutoTest/production-preparation/station-published.json
- 当前激活的固定工程发布指针：%LOCALAPPDATA%/AutoTest/station-deployment.json
- ECU 工程：工程清单 + 已发布原生 FLASH 和唯一完整 TEST，内部资源路径由现有项目/FLASH 存储管理。

ProjectBarcodeExpectation/ProductionBarcodePreflight 属于旧工程条码期望，已从正式扫码路径及工位部署必要性检查移除。旧界面在工程总览中停用；ProjectScanMatcher、ScanRecipePreparationService、ScanMatchSettingsDocument 和 StationModelCatalog 保留历史/诊断数据访问，但**不得**决定正式生产用例、固件或工程。扫码设备的诊断显示也改为读取新的发布解析器，避免和正式入口给出相互矛盾的型号解释。

已有旧数据不静默迁移/合并：可在新生产准备中明确复制编码规则、登记表和完整型号工程；无法确定身份来源、固定版本或原生 FLASH 关联的旧配置直接标记不可用于生产。旧工位工程内的 BeforeStart/AfterFinish Clamp1/2 动作如与标准夹紧流程重复，在新产品发布时直接阻断，要求工程师明确清理/重新审查。可明确保留特殊 IGN 或纯等待步骤，不允许隐性重复压紧/释放。

## 五、硬件及变型切换

只有工位空闲才允许切换当前活动发行型号：检查没有 TEST、FLASH、单行 CAN、批量 CAN、KEY、硬件控制及码率修改任务；验证候选的完整发布指纹，再保存并切换工程、重新部署。主控已连接时经既有 USB 0x42/0x43 真实设定并读回 ECU CAN 码率，不无条件断开 USB。扫描到另一型号且活动工程未切换时，正式扫码明确拒绝，绝不将当前 TEST 改为另一组。

正式扫码还必须来自已绑定的真实 USB PnP 扫码枪/COM，冻结原始码、规则 ID/版本、发布 ID/完整指纹、DUT、型号判定来源、工位 SLOT-01、扫描设备身份。发布变更、COM/USB 连接代次改变、资源变化后必须重新扫码。工程真实执行前复核发布指纹和 USB 扫码枪 PnP 设备身份。正式生产状态仍未取得实体 START 与可验证 Inspection 报告，因此准出阻断。

## 六、自主静态评分迭代（产品设计/源码结构，不是实测）

| 维度 | 满分 | 当前自评 | 扣分依据 |
|---|---:|---:|---|
| 产品配置架构与职责边界 | 25 | 23 | 旧读取代码仍保留作诊断，尚未完成全部历史迁移清单 |
| 编码形制与身份匹配 | 25 | 23 | 正例/反例、字段、冲突与 CSV 已覆盖；极端条码仍需 Windows 交互实测 |
| 工程/FLASH/TEST 唯一资源绑定 | 20 | 19 | 真实烧录方法目前只对 HydEPB 有已审来源，其它型号不能套用 |
| 工位与夹具配置路径 | 15 | 13 | 工位输入和机械反馈需要确认真实接线、协议、互锁；旧动作迁移需工程师审核 |
| UI 操作效率与信息清晰度 | 10 | 9 | 五阶段工作区已实现，键盘/高 DPI/长表性能未实测 |
| 异常、版本及追溯完整性 | 5 | 4 | 正式生产跨 InspectionAttempt 记录链仍未接入 |
| **总计** | **100** | **91/100** | 达到源码/方案静态目标，绝不是完整生产验收或运行证明 |

**各轮评审记录**：
- 轮 0（旧 R1 现场）：旧单工程期待条码字段，混合旧扫码配方与多个工位索引，无单一完整生产准备入口，不能视为成熟配置产品。
- 轮 1：确定唯一领域资源关系、三种型号来源及五种条码解析，冻结发行的工程原生 FLASH/TEST。
- 轮 2：新增生产准备五阶段 UI、批量试扫、CSV、串口设备复用与发布入口；隔离旧工程条码编辑器。
- 轮 3：复审并修复发布/草稿修订、重复夹具隐性动作、相同扫码规则歧义、外部变更、新扫码枪绑定、USB CAN 码率切换时工位占用保护、二维码样例校验及发布故障诊断，静态自评 91。

## 七、纯合同回归源码 / 未运行

ProductionIdentityConfigTests 包含：纯唯一编号固定型号、两种形制同型号、条码含型号和 HW 变型、编号表按型号选工程、无匹配和缺型号、不同匹配身份冲突、批次字段冲突、固定位置字段、正则命名字段、重复/过期编号表、无真实机械反馈时不能宣称到位、正反样例发布阻断。

要求的实际 Windows 用例还包括真实 COM 扫码/无设备、USB 重连、出版配置失效、独立 CAN 发送期间禁止切换型号、工程外部修改、两型号 CAN 波特率切换和恢复、自动保存/发布错误恢复，以及完整 FLASH/TEST 证据核验。这些从未在本轮运行。

## 八、验收事实与未完成项

- 源码实现并提交 GitHub R1：YES。
- 本轮代码人工静态复审：已进行，**不等于编译**。
- Windows MSBuild：NOT_RUN（依指令）。
- 自动化单元/集成测试执行：NOT_RUN（只提交测试源码）。
- 真扫码枪 COM/USB PnP：NOT_TESTED。
- 夹具机械反馈、实体 START/STOP、安全互锁：NOT_CONNECTED / NOT_TESTED。
- 真实 ECU 原生 FLASH 与完整连续 TEST：NOT_TESTED。
- 正式 InspectionAttempt/报告：尚未接入，正式生产准出 **BLOCKED**。

工程配置能够离线建立与预览，不等于真实工位已经可以安全开展量产检测。不得以发布配置、USB ACK、GPIO 回读、扫码成功或代码提交冒充 ECU 功能 PASS。
