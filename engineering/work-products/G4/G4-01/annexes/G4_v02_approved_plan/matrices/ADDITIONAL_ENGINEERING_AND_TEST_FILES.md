# matrices/ADDITIONAL_ENGINEERING_AND_TEST_FILES.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`6e8eaf071f6fb5e40b725fe6b65cd8562acc9e2e45279d7ebb03b29aa0ed6fbe`

原始行数（含表头）：55

| 拟定相对路径 | 文件类别 | 唯一写入者 | 最早建立 | 实施约束/作用 | 状态 |
| --- | --- | --- | --- | --- | --- |
| AutoTest.sln | 解决方案 | INTEGRATION | V0 | 九个生产程序集与按波次建立的测试工程；使用Windows构建证明 | PROPOSED_NOT_CREATED |
| global.json | SDK身份 | INTEGRATION | V0 | 仅在目标Windows执行dotnet --info后冻结精确SDK版本 | PROPOSED_NOT_CREATED |
| Directory.Build.props | 统一编译属性 | INTEGRATION | V0 | Nullable、ImplicitUsings、LangVersion、Windows TFMs；禁止其他方向覆盖 | PROPOSED_NOT_CREATED |
| Directory.Packages.props | 集中依赖 | INTEGRATION | V0 | NuGet依赖与版本单一真源；验证Prism许可及实际包还原 | PROPOSED_NOT_CREATED |
| NuGet.Config | 包源与恢复策略 | INTEGRATION | V0 | 目标机器按需生成，不能隐含不可信源和用户密码 | PROPOSED_NOT_CREATED |
| .editorconfig | 代码格式 | INTEGRATION | V0 | C#分析器与命名/空值约束统一 | PROPOSED_NOT_CREATED |
| .gitignore | 源码仓库规则 | INTEGRATION | V0 | 排除bin/obj/.vs/密钥/本地日志及大型固件拷贝 | PROPOSED_NOT_CREATED |
| README.md | 构建说明 | INTEGRATION | V0 | 分开列生产工程和SAEF文档仓库；不得替代审查报告 | PROPOSED_NOT_CREATED |
| docs/engineering/BUILD_ENVIRONMENT.md | 工具链证据 | INTEGRATION | V0 | Windows SDK、Prism许可、NuGet还原与Build/Run真命令 | PROPOSED_NOT_CREATED |
| docs/engineering/CONTRACT_REVISION.md | 合同快照 | INTEGRATION | V0 | 23 CT + DTO定义的受控版本/签名哈希、依赖图 | PROPOSED_NOT_CREATED |
| docs/engineering/INTEGRATION_LOG.md | 集成账本 | INTEGRATION | V0 | 逐包BASE_COMMIT、文件SHA、命令结果、最终提交 | PROPOSED_NOT_CREATED |
| docs/engineering/KNOWN_LIMITATIONS.md | 缺陷限制 | INTEGRATION | V0 | 未验证的实机能力与安全限制；禁止默认为PASS | PROPOSED_NOT_CREATED |
| docs/engineering/SOURCE_REUSE_DECISIONS.md | 迁移审查 | INTEGRATION | V0 | 引用OLD_HOST_REUSE_DECISIONS和差异测试证据 | PROPOSED_NOT_CREATED |
| docs/engineering/G3_BASELINE.json | 受控输入 | INTEGRATION | V0 | G3文档HEAD/hash、版本、限制；不包含源代码Git伪造身份 | PROPOSED_NOT_CREATED |
| docs/verification/TEST_MANIFEST.json | 测试身份 | INTEGRATION | V0 | 测试ID->类方法/人工脚本->证据输出; 自动化由程序生成/核验 | PROPOSED_NOT_CREATED |
| docs/verification/BENCH_PREFLIGHT.md | 现场安全协议 | DEVICE | V2 | B0电气/固件身份/线束映射；工作包授权后执行 | PROPOSED_NOT_CREATED |
| src/AutoTest.Core/AutoTest.Core.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Core项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Contracts/AutoTest.Contracts.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Contracts项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Devices/AutoTest.Devices.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Devices项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Projects/AutoTest.Projects.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Projects项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Testing/AutoTest.Testing.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Testing项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Specialists/AutoTest.Specialists.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Specialists项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Records/AutoTest.Records.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Records项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Application/AutoTest.Application.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Application项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| src/AutoTest.Desktop/AutoTest.Desktop.csproj | 生产项目文件 | INTEGRATION | V0 | AutoTest.Desktop项目引用由集成唯一维护，不允许各聊天另建副本 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Contracts.Tests/AutoTest.Contracts.Tests.csproj | 测试项目文件 | INTEGRATION | V0 | 公共类型/CT签名/负编译用例；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj | 测试项目文件 | INTEGRATION | V0 | USB/CAN/Keypad/Firmware协议及故障注入；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Projects.Tests/AutoTest.Projects.Tests.csproj | 测试项目文件 | INTEGRATION | V1 | 资源CRUD/DBC边界/文件原子保存；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Testing.Tests/AutoTest.Testing.Tests.csproj | 测试项目文件 | INTEGRATION | V1 | 事务、Plan、控制流、裁决及停止；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj | 测试项目文件 | INTEGRATION | V0→V1 | VM/命令与WPF人工操作清单；需Windows；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Specialists.Tests/AutoTest.Specialists.Tests.csproj | 测试项目文件 | INTEGRATION | V4 | UDS/波形/烧录/扫码纯逻辑Mock；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Records.Tests/AutoTest.Records.Tests.csproj | 测试项目文件 | INTEGRATION | V1→V3 | 追加写入、恢复及统计；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Application.Tests/AutoTest.Application.Tests.csproj | 测试项目文件 | INTEGRATION | V3→V5 | 生产编排及阻断负测；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Integration.Tests/AutoTest.Integration.Tests.csproj | 测试项目文件 | INTEGRATION | V0→V5 | 跨模块Fake端到端和真实能力标签；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.TestFixtures/AutoTest.TestFixtures.csproj | 测试项目文件 | INTEGRATION | V0 | FakeStation/FakeUsb/ReplayCAN/Clock/Bootloader/fault injection；项目/包引用集中维护 | PROPOSED_NOT_CREATED |
| tests/AutoTest.TestFixtures/FakeStation.cs | 测试代码/Fixture | INTEGRATION | V0 | 无真机副作用且有可注入的ACK/Unknown | PROPOSED_NOT_CREATED |
| tests/AutoTest.TestFixtures/FakeUsbByteLink.cs | 测试代码/Fixture | INTEGRATION | V0 | 分片、乱序、超时、断连注入 | PROPOSED_NOT_CREATED |
| tests/AutoTest.TestFixtures/ReplayCanStream.cs | 测试代码/Fixture | INTEGRATION | V0 | 时间质量与丢帧可配置的CAN回放 | PROPOSED_NOT_CREATED |
| tests/AutoTest.TestFixtures/FakeClock.cs | 测试代码/Fixture | INTEGRATION | V0 | 单调时钟和UTC关系可控 | PROPOSED_NOT_CREATED |
| tests/AutoTest.TestFixtures/MockBootloader.cs | 测试代码/Fixture | INTEGRATION | V4 | 仅仿真，绝不发送实际Flash命令 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Integration.Tests/MockRunSmokeTests.cs | 测试代码/Fixture | INTEGRATION | V0 | 最小真进程Mock闭环与操作事实 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Integration.Tests/WorkbenchEndToEndTests.cs | 测试代码/Fixture | INTEGRATION | V1 | 工程→编辑→运行→记录→界面状态 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Integration.Tests/DeviceToEvidenceTests.cs | 测试代码/Fixture | INTEGRATION | V2 | USB→CAN Hub→DBC→StepJudge→Facts，先Mock | PROPOSED_NOT_CREATED |
| tests/AutoTest.Integration.Tests/ProductionFlowTests.cs | 测试代码/Fixture | INTEGRATION | V5 | 扫码→绑定→运行/FlashMock→历史与统计 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Devices.Tests/UsbCanProtocolTests.cs | 模块测试代码 | DEVICE | V0 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Projects.Tests/ProjectDbcRoundTripTests.cs | 模块测试代码 | PROJECT | V1 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Testing.Tests/AuthoringAndRuntimeTests.cs | 模块测试代码 | TEST | V1 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Desktop.Tests/WorkbenchViewModelTests.cs | 模块测试代码 | UI | V1 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Specialists.Tests/IsoTpUdsTests.cs | 模块测试代码 | UDS | V4 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Specialists.Tests/WaveComparatorTests.cs | 模块测试代码 | WAVE | V4 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Specialists.Tests/FirmwareImageAndFlashMockTests.cs | 模块测试代码 | FLASH | V5 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Specialists.Tests/BarcodeRuleTests.cs | 模块测试代码 | SCAN | V4 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Records.Tests/RecordAppendTests.cs | 模块测试代码 | RECORD | V3 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
| tests/AutoTest.Application.Tests/ProductionOrchestrationTests.cs | 模块测试代码 | PRODUCTION | V5 | 该方向源码和测试同步交付；精确测试ID映射另见101清单 | PROPOSED_NOT_CREATED |
