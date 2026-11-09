# G2-05｜12 项 ICD：数据、行为、错误与验证（架构候审 v0.7）

## 用法

`tables/ICD_CONTRACTS.csv` 是本包唯一 ICD 登记表。每条包含生产者/消费者、必须表达的数据、不可违反的不变量、拟进行的验证与现场未知。

| 接口集 | 目的 | 必须共同拥有的边界 |
|---|---|---|
| ICD-01/02/03 | 设备 USB、双 CAN、IGN/夹具/按键 | 主控固件能力与设备请求不可凭 ACK 扩大物理证据 |
| ICD-04/05 | 扫码原始输入、编号解析/匹配 | 实物码获取与业务识别分离；运行身份不被新扫码覆盖 |
| ICD-06 | 工程、资源、DBC 与设备映射 | AssetRef/Revision/Hash 与能力端点引用可校验 |
| ICD-07/08 | 编辑文档/计划/Runtime/资源仲裁 | 文档可变，计划与运行快照不变；危险输出有独占路径 |
| ICD-09/10 | 烧录/生产协调/运行记录 | 安全前置与记录提交分离；失败恢复不可猜测 |
| ICD-11/12 | UI 命令与 CAN 多消费者 | ViewModel不直接持有USB，CAN单接收、多订阅并背压可见 |

## 共同消息语义（示例字段，非已编译类签名）

`OperationRequest`：OperationId、StationId、EndpointId、SessionGeneration、OperationKind、DeadlinePolicy、Arguments、SafetyAuthorization。

`OperationResult`：OperationId、TransportOutcome、DeviceAck、EvidenceRefs、EffectiveGeneration、ErrorCode/RecoveryHint。

`CanFrameObservation`：BusRole、Direction、FrameId、DLC、Payload、HostReceiveTime、TimeSource/Quality、LossCounter（若能取）。

`AssetRef`：AssetId、Revision、ContentHash、Kind。`RunSnapshot`：RunId、DutIdentity、ProjectVersion、TestDocumentVersion、AssetRefs、StationProfileVersion、FirmwareIdentity。

`EvidenceRef`：EvidenceId、Origin（USB_ACK/CAN_FRAME/DECODED_SIGNAL/PHYSICAL_MEASUREMENT/OPERATOR）、ObservedAt、ClockSource/Quality、PayloadRef。

以上是**领域数据合同候选**，不是已核实的固件 USB 帧字段和具体类签名。G3 再根据固件源码、协议和实测确定字节序、时间单位、Async 方法与线程调度。

## 统一错误域

`InvalidInput` / `AssetMissing` / `DeviceUnavailable` / `CapabilityUnavailable` / `PermissionRequired` / `ResourceBusy` / `TransportTimeout` / `TransportDisconnected` / `ObservationUnknown` / `PreflightRejected` / `ProtocolNegativeResponse` / `RecordCommitFailed` / `RecoveryRequired`。

每个错误返回：来源组件、关联 Operation/Run/Step（适用时）、是否存在物理副作用、可以重试与否、需要何种恢复证据。不因统一错误类型而给未测操作指定无限重试。

## 契约验证先于合并

1. 同一帧由 CAN Monitor 和 DBC/波形订阅者读取，不互相抢流，丢帧时可见。
2. USB 拔插后旧请求与旧响应不得匹配新会话。
3. 工程缺失 DBC/HEX 或资源版本变化时，编译/生产预检拒绝使用旧快照。
4. 运行中再次扫码，不改变已经冻结的 RunSnapshot。
5. 记录落盘失败，UI 显示未完整归档，统计不能默默把它当完整 PASS。
6. 诊断窗口与自动测试争用相同发送/控制资源时，有明确的 Grant/Wait/Reject 结果。

## G3 接收

每条 ICD 单独细化：DTO 和单位、调用签名、Async/线程、取消、安全前置、资源租约、状态转移、异常码、模拟样本和现场关闭条件；**不得自行添加跨模块共享可变状态**。

## 注册表C｜12项跨模块接口合同（唯一注册）

| 接口ID | 接口对象 | 生产者/所有者 | 消费者/对端 | 必须表达的数据 | 禁止违反的不变量 | G3验证 | 待实测/待G3决定 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| ICD-01 | HOST USB↔主控固件 | A-SESSION | 主控 MCU | WireFrame、DeviceIdentity、RequestCorrelation/Generation | 旧回应不得串代；ACK≠物理完成 | 截断/未知命令/超时/拔插；B1 | 真实命令字节、固件回显能力B0/B1 |
| ICD-02 | 双 CAN 逻辑角色 | A-STATION/A-CAN | 主控 MCU / CAN 消费者 | BusRole、PhysicalEndpoint、CanFrame、TimeQuality | DUT_CAN与KEY_CAN独立；所有接收经单入口 | 双路映射/发送/丢帧；B2 | MCU CAN0/1 与物理接线的实际对应 |
| ICD-03 | IGN/夹具/按键控制 | A-STATION | 主控与按键固件 | OutputRequest、Ack、EvidenceRef、安全许可 | 软件应答不等于电气/机械事实 | B0/B3/B4 实物复验 | IGN 上电默认、电磁阀气路、KEY16 |
| ICD-04 | 扫码 USB 输入 | E-SCAN | Honeywell 扫码枪 | ScanCaptured(raw,source,time,mode,quality) | 输入事实不自动开始烧录；输入模式可换 | 记事本/HID/串口探测、重复焦点 | Windows 枚举方式/后缀/焦点 |
| ICD-05 | 编号解析与匹配 | E-SCAN | P-PRODUCTION/B-PROJECT | DutIdentity、MatchDecision、RuleRevision | 无匹配/歧义不得自动选第一项；合法复测≠冲突 | 样本编号+歧义/多次测试 | 编号格式/型号映射细节 |
| ICD-06 | 工程资源与引用 | B-PROJECT/B-DBC | C-AUTHOR/C-RUNTIME/E专业域 | ProjectId、AssetRef、AssetHash、Binding | 引用可验证；执行使用冻结资源版本 | 保存/重开/缺失/版本切换 | 磁盘格式与目录在G3定 |
| ICD-07 | 编辑、校验和计划 | C-AUTHOR | C-RUNTIME/D-SHELL | TestDocument、StepDescriptor、CompileDiagnostic、PreparedPlan | 可变文档不等于不可变执行计划 | 撤销、类型错误、运行中修改 | 首批Step目录与高级控制节拍 |
| ICD-08 | 运行与设备占用 | C-RUNTIME | A-STATION/A-CAN | Lease、OperationRequest/Result、CancelPolicy | 不可中断的动作不承诺立即暂停；租约可追踪 | 并发租约/取消/拔插 | 具体步骤的安全边界 |
| ICD-09 | 生产与烧录协调 | P-PRODUCTION | E-FLASH/C-RUNTIME | MatchDecision、Preflight、ProgrammingJob、RunSnapshot | 身份、电压、文件与许可核查；禁止无条件重刷 | 模拟刷写、错误目标负测 | 真实 ECU Bootloader、配置与恢复 |
| ICD-10 | 证据记录与统计 | C-RUNTIME/R-RECORD | UI/生产统计 | RunId、RunFact、EvidenceRef、CommitReceipt | 不同 RunId 不覆盖；写失败不报已归档 | 同DUT多次；写盘故障 | 原始CAN/波形长期留存策略 |
| ICD-11 | UI 应用命令与投影 | D-SHELL | 各 Application 服务 | Command、CanExecuteReason、Selection、RunProjection | UI 不直接发USB字节；选择不改变运行身份 | 真实WPF任务走查；U0-U3 | 布局/默认工作区需试用 |
| ICD-12 | 共享CAN与诊断/波形 | A-CAN | E-UDS/E-WAVE/E-FLASH | FrameObservation、Subscription、TxLease、TimeQuality | 单接收入口；异常/丢帧可见；危险会话发送有仲裁 | 回放/背压/非破坏UDS/波形 | 波形精度、吞吐阈值待实测 |
