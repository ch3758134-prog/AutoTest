# 04｜核心后端源码保留 / 重构 / 废弃审计（2026-11-09）

> **状态：READ-ONLY SOURCE AUDIT / PENDING WINDOWS VERIFICATION。** 对象：main @ `8852344f602cc2242b1fddfddef3bef08354f5ec` 的实际源码和 `.csproj` 依赖。此审计不等于全源码逐行安全证明、Windows Build、台架实测或完整功能验收。用户已经决定 UI 从零做，旧 G4/R1 专业 View、Shell 和 Lab 窗口无继承地位。

## 一、立即纠正原理解
“保留核心后端”不能按文件夹名字判断。现有 9 个正式程序集（Core/Contracts/Devices/Projects/Testing/Specialists/Records/Application/Desktop）+ 独立 DeviceLab 中，只有一些是可用于真实运行的通用核心；相当一部分 Application、Testing、Specialists/Flash 现有实现明确仅支持 Mock。需要先保全及验证可用逻辑，再把演示组装和前端依赖剥离，不要删除整个业务程序集。

## 二、组件审计矩阵

| 范围 | 已核实源码事实 | 初步处置 | 验证/改造要求 |
|---|---|---|---|
| `AutoTest.Core` | Strong IDs、OperationResult、时间/证据、RevisionFingerprint；无第三方项目依赖 | **KEEP / 基础** | 保留现行值语义并检验序列化、Equals、时间质量 |
| `AutoTest.Contracts` | CAN/Device/Project/Record/Specialist/Testing/UiContracts；仅引用 Core | **KEEP + REVIEW UI CONTRACT** | 以现有真实消费者核验接口；未来 UI 契约可局部替换，不重设整个核心 |
| `AutoTest.Devices` | WinUsbNative/TransportNative/UsbSessionService、序列/解析、CanFrameCodec/Hub、HardwareCanReceiveBridge、Trace、设备事实、Station Codec；`HardwareBenchController.ConnectAsync` 建 `new CanFrameHub()` 无 TX delegate | **KEEP / 真实硬件主干** | 当前 main 是只读 RX 能力，不能称 CAN1 实际 TX 已完成。隔离物理授权；核对现役固件字节、断连与世代 |
| `AutoTest.Projects` | ProjectRepository/Resource/Catalog/DBC codec/edit、Controller Config；存盘/读取与引用校验 | **KEEP / 业务核心** | 真实工程文件往返、编码位序、DBC 导出/引用失效、实际数据回归 |
| `AutoTest.Testing.Authoring/Preparation` | TestDocument/StepDescriptor/StepEditTransaction/StepTreeValidator/PlanCompiler；PreparedTestPlan 是序列化冻结快照 | **KEEP / 编辑与计划核心** | 不允许 UX 重新定义测试节点语义；增加真实设备步骤适配的契约设计 |
| `AutoTest.Testing.Runtime` | `RunCoordinator.StartAsync(PreparedTestPlan, MockStation)`、`MockRunReport/MockQuality`，源码标注 Mock-only；未见可直接用于真实 Station 的完整通用执行器 | **REWORK / 不能直接当正式 Runtime** | 保留逻辑与测试向量，设计基于同一节点语义的真实运行执行器；隔离 Mock |
| `AutoTest.Specialists.Uds` | ISO-TP 帧/会话、CanHubIsoTpLink、UDS 协议与工作台类；main 物理 Device CAN Hub 不具 TX | **KEEP PROTOCOL / PHYSICAL INTEGRATION PENDING** | UDS 物理只读候选须分别审查 Device 授权和 UDS Session；精确 ECU Req/Resp/时间/日志实测 |
| `AutoTest.Specialists.Wave` | 波形捕获/比较、参考与存储、多订阅接口；WaveComparator 检查 DBC 修订、时间质量与误差 | **KEEP ALGORITHM / LIVE PENDING** | 独立算法回归 + CAN1 真采集/时间质量/参考保存与比较，不继承旧曲线 UI |
| `AutoTest.Specialists.Scan` | KeyboardWedgeCapture、BarcodeInputRouter、IdentityRuleSet、Match/Scan；按键输入处理支持焦点/长度/时间限制 | **KEEP CORE / REAL DEVICE TEST PENDING** | 实际扫码枪 HID/串口模式、复扫、防覆盖历史与型号匹配真实验证 |
| `AutoTest.Specialists.Flash` | FirmwareImageParser 支持 HEX/S-record/BIN 并校验格式/范围；`ProgrammingJobCoordinator` 强依赖 `MockProgrammingWorkflow` 和 MockOnly 授权 | **KEEP PARSER/VALIDATORS; MOCK JOB IS TEST-ONLY** | 按 CanTool 实际配置/ECU 实测重新建立物理安全编程流程；不能沿用模拟种子算法 |
| `AutoTest.Records` | `RecordDatabase` 为 WAL、append-only、file-backed SQLite；RunRecordWriter/History/Artifact/Statistics；`RunReportExporter` 提供暂行 HTML、JSON/StepFacts | **KEEP DATA/EVIDENCE; REPORT FORMAT NOT FROZEN** | 数据往返、异常掉电/提交失败、统计去重、报告内容由真实用户样本确定，不以旧 HTML 为最终设计 |
| `AutoTest.Application` | `Composition.RegisterMock` 可用，`RegisterReal` 直接抛 NotSupported；`G4IntegrationHost` 只组装 MockBus/MockFlash/MockProduction，`V1Workspace` 为离线工程/MockRun。还有 Production/* 命令和状态 | **SPLIT / ONLY KEEP REAL BUSINESS SERVICES** | 将 Mock Compose/G4 演示从正式宿主隔离，重新设计唯一真实 Application Composition；保留经接口测试的协调和持久化逻辑 |
| `AutoTest.Desktop` | 现役旧 WPF Prism Shell、工具页和 ViewModel，主 UI 未经使用者批准 | **REPLACE ENTIRE PRESENTATION** | 新交互任务、DesignSystem、UiGallery、实际 Windows 截图、键盘/焦点与真实业务接线。旧 XAML/布局不能当验收基线 |
| `AutoTest.DeviceLab` | 独立 WPF UI + DeviceLabViewModel/AuditJournal；DeviceLab 用真实 Devices Service | **KEEP DEVICE DIAGNOSTIC EVIDENCE; REPLACE WINDOW** | 不误删 HardwareBenchController、协议测试、原始 Trace；重新做操作窗口 |

## 三、关键源码证据（非泛泛评价）
1. `src/AutoTest.Devices/Diagnostics/HardwareBenchController.cs` 明确 `_hub = new CanFrameHub(); // no physical Tx delegate = transmit impossible`，因此 main 真设备能力不包含可用 UDS 请求发送。
2. `src/AutoTest.Application/Composition/ApplicationComposition.cs` 的 `RegisterReal()` 直接抛 `NotSupportedException`，`RegisterMock()` 返回 `MockComposition`。
3. `src/AutoTest.Application/G4/G4IntegrationHost.cs` 构造 `MockCanHubRouter / MockFlashWorkbench / MockProductionWorkbench`，不能作为正式应用层架构。
4. `src/AutoTest.Testing/Runtime/RunCoordinator.cs` 接受 `MockStation`，输出 `MockRunReport`，不是对真设备适配完毕的自动测试引擎。
5. `src/AutoTest.Specialists/Flash/ProgrammingJobCoordinator.cs` 的 `MockProgrammingWorkflow` 依赖与 MockOnly 授权说明现役物理 Flash 未实现。
6. `src/AutoTest.Records/Storage/RecordDatabase.cs` 与 `RunReportExporter.cs` 证实数据持久化核心和暂行报告生成已存在。保留追溯数据模型，报告产品设计仍需实际反馈。

## 四、与旧候选代码的关系
- 旧 `p1/device-readonly-uds-can1-tx-20261109` 含受控 CAN1 TX 变更；旧 `uds/p1-readonly-issue17-pr16-20261109` 含只读物理 UDS 的上层接线契约；两者需要先合并兼容分析再开发真实 UDS。**两份候选都不是 main 的现有物理功能。**
- 旧 `work/r1-real-functional-labs-20261109` 有独立扫码、离线 Wave、镜像解析窗口，但这些 **UI 不继承**，可能只保留无副作用的业务适配/测试向量。
- 旧 `flash/physical-lab-20261109` 含 `CanToolFlashProfile`、协议 PDU 构造，值得单独审查其真实 CanTool 一致性；`FlashLabWindow` 不继承。
- 当前曾建立 `lab/priority-uds-wave-scan-flash-20261109` 实验分支，并从旧 Flash 候选复制三个文件，其中有旧 UI。**已取消其交付资格**，无 PR、未合入 main。不得继续以此分支开发新 UI 或实测。保留 Git 身份待整仓分支清理。

## 五、具体推进顺序（先后条件）
1. **验证 main 当前 SHA**：Windows Release 全量 Build/Test、非物理自动测试；从报错中区分 UI 编译与后端失败。此项未运行，状态 NOT_RUN。
2. **冻结后端边界**：保留 Core/Contracts、Devices、Projects、Records、UDS/Wave/Scan/Flash 核心逻辑；逐类剥离 G4 Mock Composition 和旧 Desktop，但不强行一次移动全部程序集。
3. **形成后端集成测试**：工程保存重开、CAN 订阅与 DBC、UDS ISO-TP、波形参考、扫码规则、SQLite Run/报告、镜像 parser、异常/取消；必须对已提交 SHA 产生证据。
4. **建立纯后端工作台契约**：真 Device Session 唯一拥有 USB/CAN；专业 UI 使用可注入的 Port/Service；新 UI 自己不拥有硬件句柄或业务测试引擎。
5. **从零设计专业 UI**：业务服务保持同源，先用单项工作台在 Windows 上实际运行/截图和真实交互；之后集成到一个新 Prism Shell。
6. **物理放行**：先实际 CAN1 只读 UDS；Wave 真采集；扫码真实录入；Flash 导入与安全检查；任何改写 ECU 前必须完成独立审批、恢复路径和授权。

## 六、尚不能断言的事项
- 未执行最新 main 物理或 Windows Build/Test；GitHub main 未见 CI run。
- 未逐行完审所有源码与测试矩阵，以上为首轮架构和关键类审计。
- UI 重做不代表能直接删除 Desktop 源码项目；先建立新宿主和编译/依赖替代，再由验证通过的分批提交移除。
- 真实 Flash 有风险，必须独立预检/恢复验证；USB ACK、CAN RX、模拟作业都不能升级为 ECU 完成证据。
