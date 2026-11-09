# G4 v0.2 · 并行方向 `INTEGRATION` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**13项**；新增工程/测试责任：**43项**；关联验证：**5项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Core/Ids.cs` — StationId,EndpointId,ProjectId,AssetId,TestDocumentId,StepId,RunId；关键方法：`Create/TryParse/ToString`；不可混用的类型化ID。
- `src/AutoTest.Core/OperationResult.cs` — OperationResult<T>,OperationFailure；关键方法：`Accepted/Rejected/Uncertain`；请求接受、设备应答与真实观测不同层级。
- `src/AutoTest.Core/TimeEvidence.cs` — EvidenceRef,TimeQuality,TimePoint；关键方法：`FromHostArrival/FromReplay`；记录时源、质量、采集来源。
- `src/AutoTest.Core/VersionFingerprint.cs` — RevisionFingerprint；关键方法：`FromSha256/Matches`；资源与程序按内容识别。
- `src/AutoTest.Contracts/DeviceContracts.cs` — IStationSession,IStationControl；关键方法：`ConnectAsync/ExecuteAsync`；设备层统一入口。
- `src/AutoTest.Contracts/CanContracts.cs` — ICanFrameHub,ITransmitLeaseService；关键方法：`SubscribeAsync/TransmitAsync`；单接收流与租约。
- `src/AutoTest.Contracts/ProjectContracts.cs` — IProjectService,IAssetResolver；关键方法：`OpenAsync/Resolve/SaveAsync`；工程只读快照与资源验证。
- `src/AutoTest.Contracts/TestingContracts.cs` — ITestAuthoringService,IPlanCompiler,IRunController；关键方法：`Apply/PrepareAsync/StartAsync`；编辑与执行不互引UI。
- `src/AutoTest.Contracts/SpecialistContracts.cs` — IBarcodeInput,IUdsClient,IWaveformService,IProgrammingService；关键方法：`Capture/Execute/Compare/Program`；专业入口分开，共享CAN。
- `src/AutoTest.Contracts/RecordContracts.cs` — IRunFactSink,IRecordReader；关键方法：`AppendAsync/CommitAsync/QueryAsync`；追加事实与可验证提交。
- `src/AutoTest.Contracts/UiContracts.cs` — IApplicationCommandGateway,WorkbenchProjection；关键方法：`ExecuteAsync/Observe`；UI只用命令和只读投影。
- `src/AutoTest.Application/Composition/ApplicationComposition.cs` — ApplicationComposition；关键方法：`RegisterCore/RegisterMock/RegisterReal`；唯一装配根/防止双设备句柄。
- `src/AutoTest.Application/Commands/ApplicationCommandRouter.cs` — ApplicationCommandRouter；关键方法：`ExecuteAsync/GetCanExecuteReason`；跨模块命令与失败反馈。

## 4. G4新增的工程/测试文件责任

- `AutoTest.sln` — 解决方案 / V0；九个生产程序集与按波次建立的测试工程；使用Windows构建证明。
- `global.json` — SDK身份 / V0；仅在目标Windows执行dotnet --info后冻结精确SDK版本。
- `Directory.Build.props` — 统一编译属性 / V0；Nullable、ImplicitUsings、LangVersion、Windows TFMs；禁止其他方向覆盖。
- `Directory.Packages.props` — 集中依赖 / V0；NuGet依赖与版本单一真源；验证Prism许可及实际包还原。
- `NuGet.Config` — 包源与恢复策略 / V0；目标机器按需生成，不能隐含不可信源和用户密码。
- `.editorconfig` — 代码格式 / V0；C#分析器与命名/空值约束统一。
- `.gitignore` — 源码仓库规则 / V0；排除bin/obj/.vs/密钥/本地日志及大型固件拷贝。
- `README.md` — 构建说明 / V0；分开列生产工程和SAEF文档仓库；不得替代审查报告。
- `docs/engineering/BUILD_ENVIRONMENT.md` — 工具链证据 / V0；Windows SDK、Prism许可、NuGet还原与Build/Run真命令。
- `docs/engineering/CONTRACT_REVISION.md` — 合同快照 / V0；23 CT + DTO定义的受控版本/签名哈希、依赖图。
- `docs/engineering/INTEGRATION_LOG.md` — 集成账本 / V0；逐包BASE_COMMIT、文件SHA、命令结果、最终提交。
- `docs/engineering/KNOWN_LIMITATIONS.md` — 缺陷限制 / V0；未验证的实机能力与安全限制；禁止默认为PASS。
- `docs/engineering/SOURCE_REUSE_DECISIONS.md` — 迁移审查 / V0；引用OLD_HOST_REUSE_DECISIONS和差异测试证据。
- `docs/engineering/G3_BASELINE.json` — 受控输入 / V0；G3文档HEAD/hash、版本、限制；不包含源代码Git伪造身份。
- `docs/verification/TEST_MANIFEST.json` — 测试身份 / V0；测试ID->类方法/人工脚本->证据输出; 自动化由程序生成/核验。
- `src/AutoTest.Core/AutoTest.Core.csproj` — 生产项目文件 / V0；AutoTest.Core项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Contracts/AutoTest.Contracts.csproj` — 生产项目文件 / V0；AutoTest.Contracts项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Devices/AutoTest.Devices.csproj` — 生产项目文件 / V0；AutoTest.Devices项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Projects/AutoTest.Projects.csproj` — 生产项目文件 / V0；AutoTest.Projects项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Testing/AutoTest.Testing.csproj` — 生产项目文件 / V0；AutoTest.Testing项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Specialists/AutoTest.Specialists.csproj` — 生产项目文件 / V0；AutoTest.Specialists项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Records/AutoTest.Records.csproj` — 生产项目文件 / V0；AutoTest.Records项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Application/AutoTest.Application.csproj` — 生产项目文件 / V0；AutoTest.Application项目引用由集成唯一维护，不允许各聊天另建副本。
- `src/AutoTest.Desktop/AutoTest.Desktop.csproj` — 生产项目文件 / V0；AutoTest.Desktop项目引用由集成唯一维护，不允许各聊天另建副本。
- `tests/AutoTest.Contracts.Tests/AutoTest.Contracts.Tests.csproj` — 测试项目文件 / V0；公共类型/CT签名/负编译用例；项目/包引用集中维护。
- `tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj` — 测试项目文件 / V0；USB/CAN/Keypad/Firmware协议及故障注入；项目/包引用集中维护。
- `tests/AutoTest.Projects.Tests/AutoTest.Projects.Tests.csproj` — 测试项目文件 / V1；资源CRUD/DBC边界/文件原子保存；项目/包引用集中维护。
- `tests/AutoTest.Testing.Tests/AutoTest.Testing.Tests.csproj` — 测试项目文件 / V1；事务、Plan、控制流、裁决及停止；项目/包引用集中维护。
- `tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj` — 测试项目文件 / V0→V1；VM/命令与WPF人工操作清单；需Windows；项目/包引用集中维护。
- `tests/AutoTest.Specialists.Tests/AutoTest.Specialists.Tests.csproj` — 测试项目文件 / V4；UDS/波形/烧录/扫码纯逻辑Mock；项目/包引用集中维护。
- `tests/AutoTest.Records.Tests/AutoTest.Records.Tests.csproj` — 测试项目文件 / V1→V3；追加写入、恢复及统计；项目/包引用集中维护。
- `tests/AutoTest.Application.Tests/AutoTest.Application.Tests.csproj` — 测试项目文件 / V3→V5；生产编排及阻断负测；项目/包引用集中维护。
- `tests/AutoTest.Integration.Tests/AutoTest.Integration.Tests.csproj` — 测试项目文件 / V0→V5；跨模块Fake端到端和真实能力标签；项目/包引用集中维护。
- `tests/AutoTest.TestFixtures/AutoTest.TestFixtures.csproj` — 测试项目文件 / V0；FakeStation/FakeUsb/ReplayCAN/Clock/Bootloader/fault injection；项目/包引用集中维护。
- `tests/AutoTest.TestFixtures/FakeStation.cs` — 测试代码/Fixture / V0；无真机副作用且有可注入的ACK/Unknown。
- `tests/AutoTest.TestFixtures/FakeUsbByteLink.cs` — 测试代码/Fixture / V0；分片、乱序、超时、断连注入。
- `tests/AutoTest.TestFixtures/ReplayCanStream.cs` — 测试代码/Fixture / V0；时间质量与丢帧可配置的CAN回放。
- `tests/AutoTest.TestFixtures/FakeClock.cs` — 测试代码/Fixture / V0；单调时钟和UTC关系可控。
- `tests/AutoTest.TestFixtures/MockBootloader.cs` — 测试代码/Fixture / V4；仅仿真，绝不发送实际Flash命令。
- `tests/AutoTest.Integration.Tests/MockRunSmokeTests.cs` — 测试代码/Fixture / V0；最小真进程Mock闭环与操作事实。
- `tests/AutoTest.Integration.Tests/WorkbenchEndToEndTests.cs` — 测试代码/Fixture / V1；工程→编辑→运行→记录→界面状态。
- `tests/AutoTest.Integration.Tests/DeviceToEvidenceTests.cs` — 测试代码/Fixture / V2；USB→CAN Hub→DBC→StepJudge→Facts，先Mock。
- `tests/AutoTest.Integration.Tests/ProductionFlowTests.cs` — 测试代码/Fixture / V5；扫码→绑定→运行/FlashMock→历史与统计。

## 5. 旧源码可利用的候选实现（全部未迁移）

此方向暂未指定可直接复用的旧源码；须遵循G3和当前公共合同，不凭记忆假设旧代码。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `G3-0-T01` 强类型误传必须编译拒绝 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CoreContractTests.CompileNegative_G3_0_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `G3-0-T02` 假会话重连拒绝旧响应 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CoreContractTests.Verify_G3_0_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `G3-0-T03` 未授权的副作用操作一律拒绝 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CoreContractTests.Verify_G3_0_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `G3-0-T04` 运行快照修改防护 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CoreContractTests.Verify_G3_0_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `G3-0-T05` 时间源质量不得虚构 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CoreContractTests.Verify_G3_0_T05`；前置：无实物许可；先Mock，现场行为依G3-08。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
