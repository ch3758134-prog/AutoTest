# AutoTest｜G3-04 工程/DBC/DEVICE/WAVE/SCAN/RECORD 与独立 FLASH 协同 v0.1

**状态：G3 候审，工程真实事实优先；本轮不批准实体输出和烧录。**
**本轮用户顺序：** 自动测试新 WPF 严格按冻结 HTML 实现并尽快在 Windows 实测；**独立 Flash Lab** 由专用分支同步研究和实现；**独立 UDS 专业 UI** 放在烧录研究完成之后；波形分析和判定在自动测试实测中进一步优化，当前已批技术合同先保留。

## 1. 模块角色与共用数据

| 工作域 | 当前继承基础（`local`） | G3 对 G4 的接口约束 | 不允许 |
|---|---|---|---|
| PROJECT/DBC | `ProjectWorkspaceService`、`DbcDocumentStore`、`DbcCanMessageEncoder`、`TestTargetMappings` | 测试工程按 ECU 型号/硬件版独立；资源 ID/Revision/SHA，DBC 报文/信号是 TEST 编辑器选择的数据源；保存资源后失效引用明确定位 | 用 Demo 数据或临时字符串替代真实资源 |
| Controller/外部输入 | `EcuControlProfile`、`ControllerDefinition/State`、`ButtonActionPlanner` | 输入条件、状态及名称只来自当前 DUT 工程配置；KEY_SIM 20 位组合成 5 路电阻网络输出条件，CAN0 控制；不得硬编码 EPB 业务 | 20 Key 作为通用功能键、CAN ACK=ECU 识别 |
| DEVICE | `WinUsbByteLink`、`UsbSessionService`、`CanFrameHub`、`PhysicalCommandFence`、`ConfiguredStationActionPort` | 单工位设备会话、USB generation、实体授权与实际 ACK/RX/UNKNOWN；CAN1 DUT/CAN0 KEY_SIM；IGN/SOL 手动与自动共用权限 | 主控固件历史电源命令映射为 HOST 控制 DUT 主电源 |
| WAVE | `WaveCaptureService`、`WaveComparator`、`WaveReferenceRepository`、`ConfiguredWavePort` | 已批准 G3-WAVE-01 的参考身份、采集来源/覆盖/时间质量、`Pass/Fail/Inconclusive` 比较事实保持；测试判定通过正式 DSL 归约 | 采样缺失/丢帧当 PASS；以界面图形当数据 |
| SCAN | `BarcodeInputRouter`、`ProjectScanMatcher` 等 | 生产扫码匹配+预检通过自动按生产工序运行；工程师调试选择不污染生产默认；重复码/复测分离 | 无真实项目 Run 却先生成“已通过生产测试” |
| RECORD | `RunRecord`、`RecordDatabase`、`RunReportExporter` | 每次 Run 写入可追踪的项目/用例/StepOccurrence、资源修订、观测来源、物理状态/异常/报告，复测不覆盖 | Mock/模拟日志作为当次真实结果 |
| FLASH | `UdsClient`、`CanHubIsoTpLink`、`FirmwareImageParser`、`ProgrammingProfile` | **独立 Windows EXE**，与 HOST 共用协议/设备核心，但不是主 IDE 必须内嵌的正式工具；先真实只读识别、配置/镜像/日志；再按 ECU 证据分阶段刷写 | 因 CanTool 工程有 DLL 就默认算法可刷/解锁物理 Fence |
| UDS 独立工具 | 已有 ISO-TP/UDS 后端基础 | **独立 UDS 专业界面研究排在 Flash Lab 之后**；Flash Lab 内部必需 UDS 操作仍先做，不重复创建一套新协议栈 | 把“后置 UDS 工具研究”理解成烧录不需要 UDS |

## 2. 真实设备唯一性/接口冲突

- **真接线**：一台 PC、一块主控、一套台架、一台 DUT；主控 USB 与上位机通信，CAN0 到 KEY_SIM，CAN1 到 DUT，IGN 与夹具按主控已核实命令。
- **同进程/跨 EXE**：HOST、手动台架、Flash Lab 不可独立抢占同一设备物理输出；以实际设备身份为键提供跨进程排他连接/会话保护，避免仅用 `CanTransmitGate` 的进程内 lease 当全系统互斥。
- **物理授权**：收到用户的“读取 DID”或“烧录”意图不是 USB 自发授权；命令、CAN 帧、ISO-TP FC 都要受统一可追溯边界；现有 `PhysicalCommandFence` 不得从旁路绕开。
- **观察质量**：CAN 收发 RX、主控 ACK、UDS 响应、DUT 业务变化和外部测量是不同层证据；UNKNOWN 不被归为已恢复。

## 3. WAVE 本轮先保留 G3 技术合同

用户计划在新的自动测试 WPF 实测中调整波形采集与判定。G3-WAVE-01 v0.1 已批准，**本轮不以模拟曲线或未做实测为由制定额外固定容差**。G4 首次 UI 切片优先保证 TEST 可编辑波形步骤、参考资源身份和清楚的尚未连接状态。实测后按真实波形与时间质量更新分析逻辑/比较设置，版本化记录，不能为赶进度删除 G1 的波形能力。

## 4. FLASH 独立研发次序（研究在专用分支展开）

**分支（复用既有）**：`feature/flash-lab-20261114`。该分支本轮研究基线 `a9a26ebf...`，独立于主 UI 分支；下游真正应用目标建议新项目 `src/AutoTest.FlashLab/AutoTest.FlashLab.csproj`（WPF 独立 EXE），仅引用合适的 `Devices/Specialists/Projects/Core`，**不引用 `AutoTest.Desktop`**。
1. **F0 事实研究**：核对 CanTool XML、APP/Boot CAN ID、HEX/S19/BIN 格式和基址、CRC/校验、实际 Bootloader 序列、Seed/Key DLL 函数签名、刷写成功后的校验及断电恢复事实；无实证项明确标为未知。源资料归档及配置字段追溯。
2. **F1 独立软件**：可在 Windows 启动、USB 主控真实连接/设备身份、CAN1 原始 RX 和受限读取 ECU 身份（0x22 DID）、XML/镜像离线检查、参数编辑、日志导出；多帧 ISO-TP，NRC/超时/ACK 证据，断连取消、与 HOST 设备独占。**不发编程/擦除/SeedKey。**
3. **F2 实体刷写详细合同**：只有 OEM 目标资料、协议参数、算法 DLL ABI、正确镜像/地址、恢复保障、用户许可与真实工位前置条件完整后设计/实施。可复用 F1 的 EXE，保留准确阶段进度和失败证据。当前无权移除 `MockOnly` 安全限制后直接刷写。
4. **F3 一体化验证**：独立烧录工具经现场验证后再为自动 TEST 提供安全的程序化烧录服务；独立 UDS 专业工具研究在这条真实通信链成熟后开展。

**这个顺序并不阻断自动测试 WPF 的先行实测**。G4 自动 TEST 可先接无危险的工程编辑、DBC/KEY/IGN 等已明确的接口，烧录暂显示明确可用性/缺失原因。

## 5. 接口开放与验收边界

- Flash 与 HOST 共用相同设备协议和物理授权语义，但独立前端；不是两个不同 USB 实现、两个不同 ISO-TP；
- 专业 DBC/UDS/WAVE/Flash 可脱离自动测试使用，但**独立 CAN 工具**不是产品要求；手动台架集成人工 CAN TX/RX；
- 不因为独立 Flash/UDS 后置就阻塞 TEST 下 CAN/波形步骤的协议定义；
- G4 中各项真实行为分证据级别报告：`DESIGN_ONLY / WINDOWS_UI / OFFLINE_RESOURCE / USB_CONNECTED / CAN_TX_ACK / ECU_RESPONSE / REAL_RESULT_ACCEPTED`，不得跨级宣称完成。

## 6. 仍需真实现场确认的限制

- CAN1 实际可用总线参数、UDS DID/地址与 ECU 正响应，以及 CanTool 的真实操作序列；
- KEY_SIM 具体信号与按键配置对应电压范围；工位夹具/IGN 的 ACK 与真实物理表现；
- HEX/S19 镜像可用性、安全算法 DLL 匹配与可恢复启动方式；
- WAVE 的真实时间基、缺帧、比较阈值与故障曲线的可判定性。

**本轮不要求用户提前回答所有未知**。这些信息应由对应研发/现场任务调查，不能硬编码模拟结论。若缺少会破坏实体安全的必要事实，F2 不能开始。
