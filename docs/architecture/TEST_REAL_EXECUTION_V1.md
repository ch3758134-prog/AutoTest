# AutoTest｜真实 ECU 测试组织与执行详细设计 V1

> 状态：local 实施候选；未经过 Windows .NET 构建、UI 验收或实机验收；不代表 main 已交付。
> 基本方法：参考 ecu.test 的 TBC／TCF／Mapping／Test Case 责任划分，复用 AutoTest 已有后端、工程模型、DBC 和波形模块。此文档描述当前提交的具体实现及明确未完成边界，不是重新定义产品。

## 1. 目标与责任边界

AutoTest 是离线 Windows ECU 测试与开发环境，面向不同被测对象，EPB 仅为当前验证样本。测试程序表达操作、获取响应、保存变量、计算、等待、判定和流程控制；结果取决于测试程序显式采用的判据，不把设备 ACK、接收成功或单一波形分析结果自动等同于 DUT Pass。

- **测试台架（Test Bench）**：主控板 WinUSB 会话；CAN1 DUT、CAN0 KEY_SIM；IGN 和其他实际设备命令；受控物理写授权；实际控制能力受设备固件与现场接线限制。
- **被测对象（Test Configuration）**：控制器、状态、功能操作、所需模拟电压和 DBC 的报文、信号描述。业务操作可以多于或少于物理 KEY_SIM 五路输出；只在绑定到具体硬件时检查物理能力。
- **Mapping**：把测试程序可复用名称映射到配置资源。绑定的资源包含 AssetId、资源修订、SHA256；DBC 含 BusRole、ArbitrationId、帧格式、信号名、单位；控制器含 ControllerId 和 StateId。一次 TEST 的 Mapping 保存在该 TEST JSON 中，未新建第二套 TEST 文件。
- **Test Case / 执行**：继续复用 TestDocument、StepNode、StepEditTransaction、PlanCompiler。完整 Run 采用测试计划指纹、物理操作入口、信号数据上下文、显式判断和清理。
- **Evidence**：原始 CAN、DBC 信号、设备操作和步骤事实同一运行号关联；波形实际样本作为独立证据文件保存并在执行事实中引用。

## 2. 真实数据与操作的严格语义

1. **Write** 是要求设备执行某项动作；收到主控 ACK 只能认定设备报告已收到命令，不能推导 ECU 功能正常。
2. **Read** 获取实际数据。它可以写入变量，但不隐式做测试判断；变量包含值、质量、时间和证据。变量 `x.status` 保留读取质量。
3. **WaitUntil** 持续观察实时信号，直到条件成立或截止，产生 `x.met` 和 `x.status` 供后续步骤使用。是否作为测试通过条件由后续 Expect 决定。
4. **Calculate** 对已取得的变量或常量进行明确的算术运算，无脚本 eval。
5. **If / Loop / Sequence / Wait / SetVariable** 继续作为统一 TEST 树的流程与数据操作；遇到缺失观测的 If 不自动走 else，以免把 ECU 没有响应误判为条件不成立。
6. **Expect** 是显式判断点；只有有效数据和明确判断才能形成有证据的 Pass/Fail。没有有效的判定时，整体运行不能自动标为 Pass。
7. **WaveStart / WaveStop / WaveAnalyze** 分别负责记录、结束记录和分析；分析结果作为数据写入变量，不直接决定整项 TEST 成败。

运行结果区分 Passed、Failed、Inconclusive、Error、Aborted；步骤同时保留 Completed、ConditionFalse、NoData、Timeout、InvalidData、DeviceError、Cancelled。采样缺失、数据过期、丢帧、DBC 无法解析和 USB 断开不能被当作合法数值 0。

## 3. 按键模拟组织

### 3.1 两层模型

- **功能对象层**：测试工程师可以在 EcuControlProfile 中创建任意数量的 Controller 和 State；其中 State 是被测 ECU 的功能操作或输入状态，不是物理 KEY 编号。
- **台架实现层**：每个功能 State 可绑定具体电阻网络 KeyMask 和相应电压目标/窗口。KEY_SIM 采用 20 Key 电阻网络，合成最多五路模拟电压输出。20 Key 是当前硬件实现资源，不是 ECU 业务操作个数限制。
- TEST Mapping 引用已配置的状态，ButtonActionPlanner 才在操作时生成实际组合 mask。
- EcuControlProfile 可单独保存 KeyHardwarePolicy：当前设备可用 Key 掩码及禁止同时闭合的组合（例如 0x1+0x2）。老文件没有该策略仍可读取，但真实按键执行明确拒绝，必须由测试工程师检查台架资源并配置；不会默认放行全部 20 Key。策略是配置约束，不代表真实模拟电压已测量或验证。

### 3.2 时间与生命周期

固定 DelayBeforePressMs、DefaultPressDurationMs、DelayAfterReleaseMs；本版不引入随机延迟。操作模式：Pulse（保持指定时间后释放）、Hold（保持至显式释放）、Release（只释放指定已配置对象）。状态聚合保留其他已激活组合；互斥和 Key 冲突必须在转换为物理命令前检查。

- 对 Pulse，时间未配置且测试步骤未覆盖时拒绝执行；不能把零毫秒当作真实人手按下。
- 取消、异常结束要尝试清理；若设备操作超时或回执不确定，不谎报真实电气状态已释放。
- Device ACK、模拟电压实际输出、ECU 识别结果是三种不同的事实，最后一种仍需通过 ECU 响应和测试判据来观察。

当前实现使用既有 KeypadCommandCodec 和 CAN0，具体固件协议与真实硬件输出尚需现场验证；配置中的目标电压是期望/工程定义，不是上位机测得的实时电压。

## 4. CAN/DBC 统一数据流

```text
Main controller USB 0x41 CAN indications
  -> HardwareCanReceiveBridge
  -> CanFrameHub + HardwareConnectionService.FrameObserved
  -> LiveDbcSignalService (single bound stream)
  -> DecodedSignalPublisher / DbcSignalCodec (pinned DBC)
     +-> CAN Monitor: raw frame plus physical signal value
     +-> TEST Read and WaitUntil: TestSignalResult + variables
     +-> WaveCaptureService: continuous SignalObservation series
```

信号绑定明确要求匹配 AssetId、Revision 和 SHA256、CAN 角色、物理仲裁 ID、标准/扩展帧、DLC、信号与单位。现有解析器对无法正确识别的多路复用数据采取拒绝输出有效值的策略，不随意把 mux 分支解释为普通数值。

时间来源为主机到达时间（HostArrivalOnly），不能以此宣称 ECU 内部同步采样精度；跨 USB 重连必须清空旧缓存、终止旧采集。CAN 接收丢帧和消费队列溢出需要留下质量事实。

读取模式区分 Latest（取得未超龄的最近一次有效观测）与 Next（等待新报文）；后者使用固定超时；无数据、超时和断连是不同状态。

CAN 发送的报文 ID 与帧格式通过 DBC 绑定。写入值可使用 `HEX:001122...` 指定完整原始报文，或使用 `SignalA=12.5;SignalB=3` 对非多路复用消息的**全部** DBC 信号指定物理值，由 DbcSignalCodec 组帧。缺少任意一个信号、值超出 DBC 范围或不能精确编码均拒绝发送。多路复用报文暂不允许这种自动组帧方式，必须以完整 HEX 显式发送；不把其他未指定信号默认为零。此功能是编码能力，仍需实际 DUT 验证。

## 5. 显式的物理运行授权

旧 WinUSB 单帧人工确认门禁保留。新增物理 TEST 授权不是绕过门禁，而是由操作员针对准备好的 TEST 指纹输入一次完整确认：

`AUTHORIZE TEST RUN {SHA256}`

确认内容包含 TEST 内容和映射。下层 WinUSB PhysicalCommandFence 绑定当前物理连接代际、有限有效时间、最大写次数和允许的 CAN 端口/ID（IGN 单独授权）。无法凭空配置、自动连线或在 USB 重新连接后恢复物理权限。

当前只批准测试程序显式包含的真实写入能力，不能据此开启未知固件输出、自动刷写或其他需要单独授权的物理命令。

## 6. TEST 文件与运行器

### 6.1 原有文档兼容

TestDocument 和 StepNode 不替换。TEST 文档新增可选 Mapping 列表；TestDocumentCodec 用明确的原始 GUID、修订及枚举字符串序列化；没有 Mapping 字段的旧 TEST 文档仍可加载。变更通过 StepEditTransaction 记录撤销历史。修改 DBC/控制器后，绑定指纹失效必须重新确认，不能静默替换。

### 6.2 步骤与参数

| 步骤 | 输入 | 输出 | 是否直接判定 |
|---|---|---|---|
| Write | target，动作值 | 设备操作事实、ACK/错误 | 否 |
| Read | target，模式，超时，新鲜度，变量 | 物理值、质量、时间及证据 | 否 |
| WaitUntil | target，表达式，超时，变量 | 条件成立、数据质量 | 否 |
| Calculate | 左/右操作数，算符，变量 | 计算数据 | 否 |
| Expect | 左值、比较算符、右值 | 期望检查事实 | 是 |
| WaveStart | 信号、采集键、最大时长 | 采集会话 | 否 |
| WaveStop | 采集键，变量 | 持久波形证据和样本数 | 否 |
| WaveAnalyze | 采集键、参考名、容差、覆盖率、变量 | 比较分析数据与证据 | 否，需 Expect 决定 |

流程顺序为 Prepare → Test → Cleanup。正常完成、异常或中止都进入 Cleanup，之后撤销运行授权；不能用 MockObserve 替代 ECU 实际响应。当前真正执行器独立于旧 Mock RunCoordinator，但使用同一正式 TEST 文档和编译器，并未建立第二套测试程序。

### 6.3 WPF

测试编辑器加入资源浏览、TEST Mapping 创建/删除、目标参数选择、运行确认、开始/停止、步骤结果。CAN 监视同时显示原始接收帧和通过 DBC 转换的信号值。仍保留现有主界面布局与项目资源管理能力，不单独创建重复的工程与 DBC 编辑器。

## 7. 波形录制与分析

波形记录从 LiveDbcSignalService 中订阅与 TEST Read **同一来源**的 SignalObservation，保留样本值、时间、单位、证据和数据质量。WaveStop 将波形采样证据写入工程文件夹；WaveAnalyze 使用命名参考波形、容差和覆盖率产生分析结果数据，并允许通过 Expect 显式采用该结果。

参考波形需要从有效采集数据经操作员明确确认后建立；不应将一次失败的 DUT 测量自动提升为新参考。比较值的工程适用性必须在实际波形采集后确定；高精度时序不能由 HostArrivalOnly 证据冒充。

## 8. 代码落位与质量验证

- `src/AutoTest.Testing/Authoring/`：Mapping 类型、序列化与 TEST 编辑事务
- `src/AutoTest.Testing/Runtime/`：真实读/写/波形端口、变量、步骤事实与统一运行器
- `src/AutoTest.Projects/ControllerConfig/`：配置状态、固定按键时间、动作规划
- `src/AutoTest.Projects/Dbc/`：统一 DecodedSignalPublisher
- `src/AutoTest.Desktop/Hardware/`：唯一共享实际 CAN/DBC 信号服务
- `src/AutoTest.Desktop/Testing/`：真实台架操作、波形、运行协调、WPF 操作、证据落盘
- `src/AutoTest.Devices/Usb/`：物理写安全门禁
- `tests/AutoTest.Testing.Tests/`、`tests/AutoTest.Projects.Tests/`、`tests/AutoTest.Devices.Tests/`：各层契约与测试用例

回归必须覆盖旧 TEST 读写兼容、映射版本过期、DBC 解析、标准/扩展帧、设备断连/恢复、读取数据和独立 Expect、Pulse/Hold/Release、USB 超时后的状态未知、取消清理、波形质量和独立分析、报告持久化。

## 9. 验收与未完成边界

本次 local 分支交付的是六项能力对应的**首个贯通实施候选**，而不是经验证的 R1 完成证明：

1. Windows .NET SDK 构建尚未运行；本环境缺少 dotnet，不能虚报编译/单测成功。2026-11-12 的 `local` 新增了完整 DBC 物理信号编码和明确的 Key 硬件策略绑定，仍待本地编译验证。
2. WinUSB、CAN0 Keypad 电阻组合输出、IGN 等物理动作必须现场测量和确认；设备 ACK 不能代替实测。
3. ECU DBC 特别是复杂多路复用、周期 CAN 发送、实际信号刷新率和时间基准需要原始报文实测。
4. 波形参考管理的图形化建立/修改、波形判据校准仍需依据实际采样继续完善。
5. UDS、Flash、扫码出厂自动流转不包含在这一条最小真实测试链内，应按照相同运行和数据原则逐步接入，而非虚构完成。
6. 本轮仅修改 `local`，未合入 `main`，未运行 GitHub CI。

最低现场验收：打开工程和 DBC → 配置 ECU 功能状态到 KEY_SIM → 将功能对象和 CAN1 信号映射到 TEST → 显式授权运行 → 执行固定延迟按键动作 → 从真实 CAN1 得到 DBC 信号数据 → 利用 IF/Calculate/Expect 使用响应 → 完成 Cleanup → 查阅原始 CAN、信号和步骤证据；再用同一信号完成一次波形记录和独立分析。

**任何一步失败，均应记录原始错误和证据，停止声称该链路已通过验收。**

## 10. 工程化分阶段验收矩阵（未验证前不得标 PASS）

| 阶段 | 验收内容 | 当前源码事实 | 必须取得的结果 |
|---|---|---|---|
| 1 契约 | 原 TEST 兼容读取/保存，Mapping 身份+修订+SHA，明确 Read 与 Expect 分离 | 已有源代码与单元测试 | 本机 Build/Test，旧项目与新项目回归 |
| 2 DBC 数据链 | WinUSB CAN1 → Hub → DBC → Read/监视/波形同源，区分缺帧/过期/丢帧/解码失败/断连 | 已有共享服务；物理时间为主机到达 | 真实报文日志与解码值逐项核对 |
| 3 真实刺激 | KEY_SIM 功能状态映射到 KeyMask，按固定延迟 Pulse/Hold/Release，IGN 和 CAN 写入 | 已有物理门禁、配置策略、受控动作端口 | CAN0 的设备响应、示波器/电压表与 DUT 实测 |
| 4 真实执行 | Prepare/Test/Cleanup，Write/Read/WaitUntil/Calculate/Expect/IF/Loop，数据与判定分离 | 已有 RealRunCoordinator；无 Mock 回退 | 完整的一次实机 Run，证据、错误和清理记录齐全 |
| 5 WPF | TEST 编辑器映射对象、编辑步骤、显式运行授权和步骤结果 | 已有界面入口 | Windows 本地 UI 逐项操作且流程无阻塞 |
| 6 波形 | 同源 DBC 观测、Start/Stop/Analyze、结果交由 Expect 决定 | 已有 WaveCaptureService 及分析端口 | 实际样本时间质量、参考波形和判据的现场复核 |

### 当前明确限制

- 配置中的电压窗口是工程要求，不是当前 KEY_SIM 已经测得的电压事实。硬件策略需要工程师配置及现场验证。
- 对 CAN DBC 发送，完整报文物理组帧只支持无多路复用且列出全部信号的情况；其它情况拒绝或显式使用完整 HEX。
- 持续 CAN 周期发送、复杂波形参考 UI、完整 UDS/Flash 测试步骤、扫码生产自动运行、硬件同步时间不属于本轮已验证内容。
- 未经 Windows Build/Tests、实机验证及用户验收，不得合并 main 或把候选标为 R1 完成。
