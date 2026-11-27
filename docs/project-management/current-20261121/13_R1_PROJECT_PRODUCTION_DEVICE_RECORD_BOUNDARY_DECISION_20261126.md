# 13｜R1 工程、生产过程、设备和记录四层职责重构决策

**状态：架构决定已确认（DECIDED）；源码重构待执行（NOT_IMPLEMENTED）；Windows 构建与现场实测未验证。**

- 日期：2026-11-26
- 依据：负责人明确“测试用例和烧录是工程内部的，扫码是外部的”，认可“检测工程 / 生产检测管理 / 工位设备服务 / 记录与报告”的职责划分并要求作出实施决策。
- 对照源码：R1 当前 HEAD `51445ed09bf7383eb454b9e0c55320c4a6fde467`（审核时）；只允许实施 R1，main 受控，管理决策仅在 Project_Management。
- 项目近期仍为单 PC / 单工位 / 一个 Desktop EXE；长期 Engineering.exe + Production.exe 共用业务与设备核心。
- 本文取代此前“扫码配置作为工程内部资源”“生产端再配置一份 TEST/FLASH 方案”“夹具扫码匹配”等互相冲突或过度设计。保留实际安全门禁，不进行伪兼容、伪设备回执或模拟正式 PASS。

## 一、四个权威、一个执行合同

| 领域 | 权威/持有内容 | 绝不能做的事 |
|---|---|---|
| PROJECT / Engineering | 一份检测工程的 TEST 有序实例 + 公共流程、原生 FLASH 工艺（含固件与安全算法）、DBC、WAVE、产品测试准备/清理、资源签名/引用与可导入包 | 保存生产扫码规则、扫描 COM、按编号统计产量、逐工位设置夹具、生成正式放行记录 |
| PRODUCTION | 本地已安装工程库、扫码编号至唯一工程版本的路由、检测会话与复检关系、开始/完成/异常的生产过程控制 | 重新编辑 TEST 步骤、独立重组另一份 FLASH 或用例清单、决定哪个 GPIO 是夹具输出 |
| DEVICE | 扫码枪原文输入和设备身份、主控 USB、ECU/KEY CAN、IGN、固定工位压紧/释放请求、传感与状态读回、设备独占 | 根据扫码猜测型号、自己选择固件或判定 TEST PASS |
| RECORD | 按完整扫码原文和 RunId 关联每次真正执行、烧录/用例/步骤/波形/报文证据、工程/固件精确版本、最终结果和审计追踪 | 修改原始测量、猜测未收到的硬件反馈、用程序检查替代真实执行证据 |

四层复用唯一工程资源合同和统一真实 TEST/FLASH 引擎；不得出现“工程测试执行器”和“生产测试执行器”两套测试规则。工程模式允许单步、部分用例、手动台架，**生产模式必须使用完整有序工程并执行每次必需前置 FLASH**。单次 Run 不变更工程包、身份和工位设备代次。

## 二、扫码方案（最近确认）

- 已决定：**固定标记 + 分隔符**。建议 `HYDEP-00001234`；先尝试统一 ASCII `-`，但具体字符长度/序列号规则在编码合同冻结时确定。**只能匹配完整开头标记加分隔符，不用字符串任意位置 Contains**。
- 扫码真实 COM 原文要立即显示并原样作为 ECU 实物身份保存；不强迫拆出单独序列号、客户型号、硬件字段。
- 生产工程管理一次性维护 `FixedMarker -> ActivePackageHash`；一标记唯一生效工程版本，一个工程内部已经包含固件、FLASH 与完整 TEST。
- 无码、无标记、非唯一命中、空后缀、缺失工程资源或工位占用时全部 fail closed；不得根据“最近打开工程”选择。重复扫描不是新增单位；真实复检有新 RunId 和历史关系。
- 产品更换只更新标记与工程版本的绑定；不在工程内部保存扫码规则。
- 旧的 Single / Fields / Registry + serial_registry / 硬件字段、产品码字段位置、编号登记表不是这个首版的业务要求。**直接删除并迁移到唯一匹配规则，不新增第四模式或保留未使用兼容分支**。
- 测试扫码预览是生产管理员的调试工具，不触发工位动作、FLASH 或正式记录。

## 三、统一生产过程

待命（无活动检测） → 实际扫码观察（立即显示） → 路由唯一已安装工程版本 → 校验工程 TEST/FLASH 完整性、ECU CAN 参数、工位能力及当前设备代次 → 锁定待运行会话（完整编号、工程版本包指纹、扫码来源、运行许可快照） → 按固定工位业务动作自动请求夹具压紧并取得适用的可靠到位依据 → 等待真实实体 START 与互锁条件 → 每次完整 FLASH + APP 核验 → 完整有序 TEST → 按 RunId 持久化正式 RECORD/报告 → 按状态安全释放和恢复待命。

- 上述顺序继承原“扫码—装夹—实体 START—前置 FLASH—TEST—RECORD”的产品要求；**压紧和 START 的最终具体顺序仍以真实装夹/按钮接线及安全验收为准，不能无传感证据冒称到位**。
- 工程 TEST 的准备/测试/清理只负责产品特有测试行为，与工位夹具压紧/释放属于不同层级；产品特定 IGN/CAN 可以留在 TEST，固定夹具动作只能由 Station 过程所有。
- 故障中途断线、烧录失败、夹具失去反馈、RECORD 写入失败必须分别停机/保留不确定性，不能直接发布 PASS；夹具释放按已冻结的物理安全策略，不因某个失败码盲目反转输出。
- 正式生产结果只有可信物理动作、真正固件运行、严格 TEST/RECORD 完整证据可获得后才能 PASS；缺实体 START / record writer 时原有硬性阻断保持。

## 四、当前 R1 被查出的领域重复（必须针对性删除）

1. `HOST/src/AutoTest.Desktop/Projects/ProductionProjectCatalog.cs` 已管理 SQLite 工程库、扫码路由、安装版，但仍保留 Single/Fields/Registry、硬件字段和逐序号归属表；重构为唯一固定标记绑定，导入/版本功能继续保留。
2. `HOST/src/AutoTest.Desktop/Projects/ProductionIdentityContracts.cs`、`StationProductionConfigurationStore.cs` 还保留另一套 BarcodeRule、FixedProject、混线绑定、编号登记表及工程发布职责；新体系下这些不再是工程选择权威。经调用链复核后删除旧存储/解析/窗口分支，不写桥接兼容。
3. `ProductionPreparationService.cs` 将一个 Inspection 工位过程资产当作工程冻结资源，与 station-owned 固定夹具存在冲突。应将固定压紧/释放从工程资源移交 StationProcess；TEST 可以保留明确的产品级准备/清理动作。
4. `AutoTest.Application/Production/ProductionSession.cs`、`ProductionWorkflow.cs` 已定义会话和 FLASH→TEST→RECORD，桌面端 `AutomaticTestHomeViewModel.Run.cs` 仍包含完整运行编排和生产/工程混合状态。应以一个真实的生产过程服务为权威，并通过现有设备和 TEST/FLASH 端口调用，不复制业务规则。旧 Application 类型中的 `RecipeId`、`IsMock` 等是否仍有效必须核查真实调用链，物理生产不得允许 Mock。
5. `AutomaticTestHomeViewModel.Catalog.cs` 负责扫码路由、设备检查、冻结和生产准备，承担过多职责；拆为 BarcodeRecognition、PackageSelection、ProductionRunCoordinator，ViewModel 只展示状态与发起命令。
6. `ProductionProjectLibraryWindow.xaml[.cs]` 已包含大量第三类配置（字段索引、注册表、单模式与版本表），必须改成工程库 + 固定标记关联 + 实码预览，和低频 Station 设备设置分开。
7. `ProductionPreparationWindow.xaml[.cs]` 虽已减成设备设置，但仍向普通操作者展示夹具1/2、压紧顺序、反馈引脚等实现细节；日常生产屏绝无这些控件。设备联调在手动台架/工程诊断模式，工位硬件初始化一次维护。
8. `AutoTest.Records`、`AutoTest.Application/Production/ProductionResultCoordinator.cs` 现有独立证据边界必须继续保持；特别检查真实运行 RunId、扫码原文、工程包 SHA、FLASH 证据、TEST StepOccurrence 和报告行的一致性。

## 五、具体实施顺序（不是以 UI 美化代替重构）

| 门 | 目标实现 | 删除/保留 | 准出条件 |
|---|---|---|---|
| A：合同收敛 | 明确 ProjectPackage/FullBarcode/FixedMarker/RunId/StationSession/FrozenExecutionContext 唯一数据合同；追踪所有现行调用方 | 清理重复 Matcher/Recipe、旧发布工程决策权威 | 编译和数据合同单测，确认只有一处实际工程选择 |
| B：工程包与路由 | 安装/查询多个自包含包，`前缀+分隔符 -> 唯一生效包` | 保留 SHA 校验和只读版本；删除 Fields/Registry 三模式 | 两真实包路由、错码/冲突/缺包阻断，原文完整 |
| C：单一执行总控 | 将运行过程从 WPF ViewModel 抽出，调用真实 FLASH + TEST + Device/RECORD 端口 | 只保留一个执行权威；工程调试使用共同执行层 | 执行顺序/停机/失败收尾一致，生产不接受部分运行 |
| D：固定工位动作 | 工位 owned 的压紧/释放、反馈/安全互锁接入，正常生产不出现配置夹具 UI | 删除工程里的固定夹具配置和扫码对夹具映射 | 实机压紧/到位/START/释放与故障安全实测 |
| E：记录闭环 | 固化完整码、RunId、工程版本、FLASH 和 TEST 证据及复检链 | 不另生成“摘要式 PASS”绕开 RECORD | 真实记录可追溯，断电/失败/复检证据完整 |
| F：界面与后续双 EXE | 当前单 EXE 仅工程/生产两个顶层工作区；生产 UI 极简、工程完整专业工具；提前把包/生产服务从 Desktop 抽离 | 不建立第二套引擎、不加多余导航 | 单 EXE WPF 全流程实测；未来单独打包两 EXE 无业务重写 |

### 安全与验收原则

- 每一阶段必须用其真实完成的内容报告，不以“源码改了”声称真机合格。
- Windows Release 编译成功只是代码准入，USB/COM/IGN/夹具/实际 ECU FLASH/TEST/RECORD 通过后才可正式生产准出。
- 禁止无实测依据的固定 9/10；按业务闭环、交互效率、可追溯、硬件安全、异常恢复逐项评分。
- 用户的本地 Codex 只拉取编译运行；R1 源码需由实施方实际修改并提交。管理文档只写 Project_Management。
- 不更改 main、不新建分支、不自行启动 CI。


## 七｜R1 第一批破坏性重构实际施工记录（2026-11-26）

**此节优先于本文件此前“尚未实施”口径。** 用户明确下达“破坏性重构吧，直接开始”。实施仅在 R1，原基线 `51445ed09bf7383eb454b9e0c55320c4a6fde467`，本次报告 HEAD `d19aef685249913691b7a02005b58179db941046`。

### 已经真实写入 GitHub R1 的源码

1. **唯一扫码合同：** 在 `AutoTest.Application/Production/FixedMarkerBarcode.cs` 新增独立识别器，仅按完整编号开头 `Marker + '-'` 精确识别，保存完整扫码原文；不拆序列号、不做关键词全文包含，不自动推断型号或夹具。标记长度与字符集是当前首版实现约束，可随未来实际标签合同受控调整。
2. **唯一生产工程路由：** `ProductionProjectCatalog` 从三模式/登记表改为 SQLite `marker_routes`，标记唯一关联一个安装包版本，重绑增加规则修订。保留完整工程包导入与资源验证。废弃的 `routes/settings/serial_registry` 不再有运行权威；首次删除旧表前会生成数据库备份，备份失败就阻止切换。
3. **管理窗口重建：** `ProductionProjectLibraryWindow.xaml[.cs]` 移除单工程模式、分隔字段输入、硬件码、逐编号注册及 CSV 匹配；只提供工程导入/查看、固定标记关联及只读扫码预览。生产扫描真实 COM 时根据唯一标记选包，锁定规则版本和包哈希；扫描本身不发 START/夹具/FLASH。
4. **工程不再定义固定工位动作：** `ProductionPreparationService` 删除工程夹具冻结合同；`StationDeploymentStore` 删除 InspectionProcedure 字段、部署格式提升 v3；`AutomaticTestHomeViewModel.Run` 删除工程 BeforeStart/AfterFinish 工位动作执行，固定夹具仅由工位设备计划控制；工程中的旧 Inspection 资源会在包审查时报错要求移除，产品特定动作应改写成 TEST 步骤。
5. **源代码级删除而非隐藏：** 删除旧 `InspectionStationProcedureEditorView.xaml[.cs]`、`InspectionStationProcedureEditorViewModel.cs`，删除工程 UI 的新增/打开路径。
6. **设备配置纯化：** 新 `StationDeviceConfiguration` 和 `StationDeviceConfigurationStore`（SchemaVersion=3）独立保存工位设备/夹具配置，相关 UI 和扫描/调试调用已切换；删除 `ProductionIdentityContracts.cs`、`StationProductionConfigurationStore.cs` 及其三模式相关测试。新的设备配置路径为 `%LOCALAPPDATA%/AutoTest/station-device/station-device.json`；旧混合草稿不会被当作新设备配置静默加载，需现场核对接线。
7. **测试源码调整：** 更新纯数据扫码合同/路由测试，新增设备配置隔离与安全阻断测试。**测试文件存在≠测试已经运行**。

### 核对与证据边界

- GitHub Compare：相对 `51445ed09bf7383eb454b9e0c55320c4a6fde467`，27 个提交，25 文件变化，新增 795 行 / 删除 2406 行，其中删除 6 个旧源文件/测试文件。
- 静态回读全部现存 Desktop C# 130 文件与 Desktop.Tests C# 39 文件，未发现对已删除 `ProductionBarcodeRule`、`StationProductionConfigurationStore`、`ProductionIdentityResolver`、工程工位动作编辑器等旧类型的引用。两处 WPF 管理窗口 XAML 标签、名称与事件映射静态检查无明显错误。
- 当前工具环境没有 Windows .NET SDK/dotnet，**未进行 Release 编译或单测执行，尚不能声称无编译错误**。不主动触发 CI，也不能用静态审查声称真实 ECU 通过。
- **未完成 P0：** 目前 `AutoTest.Application/Production` 的正式会话/流程与 Desktop 生产 Run 编排尚未完全统一；实体 START、夹具可靠到位反馈、正式 InspectionWriter 到 TEST StepOccurrence 的生产证据链仍不具备闭环；硬件门禁与正式 PASS 阻断保持。
- 开发调试已有真实执行能力的源码继续保留；没有用 mock 填充正式生产阶段或将工程调试报告冒充生产检测报告。下一阶段应以单一生产过程控制器 + 真实记录适配的完整运行证据为主，不应返回旧的扫码发布/工位脚本架构。

### 首次运行注意

- 因为是破坏性数据合同切换，旧扫码映射不再生效，生产管理员需要按标记重新关联已导入的工程包。
- 旧工位混合配置不会自动迁移或授权夹具；工位设备设置需要根据真实接线重新确认。首次实测应先验证扫码原文和工程匹配，然后核对主控、CAN 速率、夹具安全，再运行真实开发调试链。正式生产门禁仍应阻断。

当前阶段标志：**SOURCE_REFACTOR_APPLIED / BUILD_NOT_VERIFIED / FIELD_NOT_VERIFIED / FORMAL_PRODUCTION_BLOCKED**。


## 八｜第二批破坏性施工：真正生产过程合同与记录边界（2026-11-26）

**覆盖此前待施工口径：** 本节记录自 R1 `d19aef685249913691b7a02005b58179db941046` 起已写入 GitHub 的第二批源码，当前核对 HEAD `06fcf6947d4f7be7bce448d126aef0928188cf2d`。本批次 47 次提交、17 文件、+790/-695；**仅源码及静态检查，不代表编译或真机验收**。

### 执行过程已经修改的事实

1. 没有再创建第三套运行器：直接改造现有 `AutoTest.Application/Production/ProductionWorkflow`、`ProductionCommandHandler`、`ProductionSession` 和 `ProductionResultCoordinator`，与桌面端工程实测流程分开。桌面工程实测仍保留已有真实 FLASH/TEST 路径；正式生产尚未接入 MCU 实体 START/证据写入端口。
2. 正式执行对象从旧 `ProductionConfiguration(RecipeId,RecipeVersion)` 改成 `ProductionProjectExecution(ProjectId,ProjectRevision,PackageFingerprint,RequiresFlash,ExpectedSupply,Assets)`，杜绝生产层第二份测试配方。预检严格检查工程版本与依赖闭包。
3. 正式生产彻底删除 `IsMock`/模拟流程分支，`ProductionCommandHandler` 删除 `StartAsync` 软件 START 和对外 `PhysicalStartSignalAsync(bool)` 入口。正式扫码仅准备项目，`BeginStationCycleAsync` 只启动工位过程，不等同 FLASH 许可；真实接线适配不存在时返回 `PHYSICAL_STATION_ADAPTER_NOT_CONNECTED`，不碰硬件、不创建假 PASS。
4. 新增 `IProductionPhysicalStation` 与夹具/按钮结构化回执。受控顺序：持久化运行范围 → 持久化夹具输出意图 → 请求压紧并验证实际机械到位及联锁 → 等待可信实体 START 新边沿 → 记录已验证证据 → FLASH 输入意图持久化 → 真实 FLASH/APP → TEST → 正式 RECORD。任何证据缺失、写盘失败、模拟阶段声明都 fail closed，发生潜在实物副作用后保持 UNKNOWN/ABORTED，不能盲目复位/释放。
5. 在 `ProductionStartGate` 中删除软件抢占入口。新一台必须先明确 Disarm、观察按钮释放，再允许不同 RunId 的新按下；保持按钮与重复上升沿不会再启动两次。**此纯模型不是传感器适配**。
6. 增加 MCU 控制会话代次 `ControllerEpoch`，并要求夹具证据和 START 回执同 RunId、StationId 和 Epoch。重连后旧回执不得授权擦写 ECU。
7. 正式 RunFact 首条 `PRODUCTION_SCOPE` 以 JSON 冻结完整扫码编号、工程 ProjectId、工程修订、不可变包 SHA、扫码规则 SHA、扫码设备来源、StationId 和 MCU Epoch。每次运行独立 RunId，完整扫码身份不会被 matcher 改写。
8. `ProductionResultCoordinator` 去掉模拟记录提交分支，唯一正式 PASS 前提为 `IVerifiedProductionCommitSink` 真正核验并持久化与同一 RunId 关联的真实证据；普通 `IRunFactSink` 即便 durable=true 仍仅返回 UNKNOWN，`PhysicalReleaseAllowed` 始终为 false（现场受控释放尚未接入）。
9. 桌面端扫码冻结数据合同 `FrozenScanIdentity` 已精简为工程调试或生产固定标记两种明确来源；删除已废弃的产品硬件字段、工位扫码发布版本、独立序列号及人工映射字段。生产入站新增机械到位与安全联锁门禁（共 13 项），目前实体 START、机械证据、正式记录适配均仍为 false。

### 验证与剩余 P0

- 替换了过去模拟生产/配方主导的应用层单测，覆盖完整扫码不可改写、无硬件适配时零物理命令、夹具意图落盘先于输出、机械到位/联锁、START 可信来源、主控代次错配、FLASH 证据、正式结果写盘、复检独立 RunId 等；测试替身仅存在于 `HOST/tests`，**测试未执行**。
- 对 18 个直接变动的应用层、桌面层与单测源码文件进行了静态括号/字符串结构检查，未发现明显不匹配；搜索旧 `IsMock`、`RecipeId`、`ProductionConfiguration` 等标识（应用层范围）无业务引用。**这不是 .NET/WPF 编译结果**。
- 正式生产设备接口 `IProductionPhysicalStation` 当前没有真实实现和依赖注入；测试套件中的 `TestPhysicalStation` 仅为纯接口契约验证，绝不能被 Desktop 或发布包注册为正式硬件。
- 桌面端仍未将 `ProductionCommandHandler` 接入正式硬件扫码/夹具/START/FLASH/TEST/RECORD 的全链生产路径；真实的 `InspectionWriter` 还缺与 StepOccurrence/原始测量/波形/实际 FLASH 同一 RunId 的可验收适配。所有 P0 真机门禁保持。
- 当前无 Windows .NET SDK 环境可编译，用户本地 Codex 负责拉取构建/运行；不能凭本节文字宣称发布准备完成。下一步必须处理编译反馈、连接真实物理输入与设备动作，再进行 HydEPB 整机台架验收。

状态：**SOURCE_CONTRACT_RESTRUCTURED / STATIC_CHECK_ONLY / BUILD_NOT_VERIFIED / PHYSICAL_START_NOT_CONNECTED / FORMAL_RECORD_NOT_CONNECTED / PRODUCTION_PASS_BLOCKED**。
