# G4 v0.2 · 并行方向 `DEVICE` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**26项**；新增工程/测试责任：**2项**；关联验证：**23项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Devices/Usb/IUsbByteLink.cs` — IUsbByteLink；关键方法：`OpenAsync/ReadAsync/WriteAsync/CloseAsync`；仅负责字节流与取消。
- `src/AutoTest.Devices/Usb/UsbWireCodec.cs` — UsbWireCodec；关键方法：`EncodeRequest/TryDecodeResponse/Crc16`；无IO字节帧。
- `src/AutoTest.Devices/Usb/UsbFrameDecoder.cs` — UsbFrameDecoder；关键方法：`Feed/Reset`；处理拆帧粘帧与异常前缀。
- `src/AutoTest.Devices/Usb/UsbDiscovery.cs` — UsbDiscovery；关键方法：`ListCandidatesAsync`；不把扫码枪枚举为主控。
- `src/AutoTest.Devices/Usb/PendingRequestRegistry.cs` — PendingRequestRegistry；关键方法：`Register/TryComplete/FailGeneration`；世代、序号、命令强关联。
- `src/AutoTest.Devices/Usb/UsbSessionService.cs` — UsbSessionService；关键方法：`ConnectAsync/RequestAsync/DisconnectAsync`；单会话所有者、后台读循环。
- `src/AutoTest.Devices/Usb/MainControllerCodec.cs` — MainControllerCodec；关键方法：`EncodePing/EncodeCan/EncodeIgn/EncodeSol`；业务命令映射按源码。
- `src/AutoTest.Devices/Usb/UsbDiagnosticsProjection.cs` — UsbDiagnosticsProjection；关键方法：`ObserveState/ObserveRawFrame`；只读原始报文与异常统计。
- `src/AutoTest.Devices/Station/StationProfile.cs` — StationProfile；关键方法：`Validate/DescribeBindings`；逻辑角色到设备通道映射。
- `src/AutoTest.Devices/Station/CapabilityRegistry.cs` — CapabilityRegistry；关键方法：`PublishVerified/GetAvailable`；只发布已证实能力。
- `src/AutoTest.Devices/Station/StationPreflight.cs` — StationPreflight；关键方法：`CheckAsync`；供电型号/接线/权限检查。
- `src/AutoTest.Devices/Station/StationControlService.cs` — StationControlService；关键方法：`SetIgnAsync/OperateFixtureAsync`；有副作用操作入口。
- `src/AutoTest.Devices/Station/KeypadCommandCodec.cs` — KeypadCommandCodec；关键方法：`EncodeMask/EncodeReleaseAll`；0x730 + 20位mask，8字节帧。
- `src/AutoTest.Devices/Station/KeypadControlService.cs` — KeypadControlService；关键方法：`SetMaskAsync/ReleaseAllAsync`；仅经 KEY_CAN 发送。
- `src/AutoTest.Devices/Station/OutputObservation.cs` — OutputObservation；关键方法：`ClassifyEvidence`；请求/ACK/电气/机械不同证据等级。
- `src/AutoTest.Devices/Station/FirmwareIdentityTracker.cs` — FirmwareIdentityTracker；关键方法：`ReadAndCompare/RecordIdentity`；主/按键固件版本及追踪。
- `src/AutoTest.Devices/Can/CanFrameHub.cs` — CanFrameHub；关键方法：`Publish/Subscribe/Unsubscribe`；统一扇出、每订阅独立限额。
- `src/AutoTest.Devices/Can/CanFrameCodec.cs` — CanFrameCodec；关键方法：`FromUsbPayload/ToUsbPayload`；15字节CAN字段校验。
- `src/AutoTest.Devices/Can/CanTransmitGate.cs` — CanTransmitGate；关键方法：`AcquireAsync/Release/TransmitAsync`；发送与诊断独占会话。
- `src/AutoTest.Devices/Can/CanSubscription.cs` — CanSubscription；关键方法：`Match/ReadAsync`；按BusRole/ID/IDE过滤。
- `src/AutoTest.Devices/Can/CanLossMonitor.cs` — CanLossMonitor；关键方法：`OnDrop/ReadSnapshot`；本地缓冲丢失可见。
- `src/AutoTest.Devices/Can/CanTimeAnnotator.cs` — CanTimeAnnotator；关键方法：`StampHostArrival/AnnotateReplay`；HOST到达时刻不冒充ECU采样时间。
- `src/AutoTest.Devices/Can/CanTraceProjection.cs` — CanTraceProjection；关键方法：`Append/Query/Export`；日志只读消费，不阻塞收包。
- `docs/integration/FirmwareCompatibility.md` — FirmwareCompatibility；关键方法：`DefineSupportedPairs`；双固件/主控协议/Station能力兼容范围。
- `src/AutoTest.Devices/Station/FirmwareCompatibilityChecker.cs` — FirmwareCompatibilityChecker；关键方法：`Validate/ExplainLimitations`；连接后检查版本能力，不自动刷主控。
- `verification/FirmwareChangeImpact.md` — FirmwareChangeImpact；关键方法：`TraceChangedCommands/SelectRegression`；修订触及接口时重测B1～B6必要项。

## 4. G4新增的工程/测试文件责任

- `docs/verification/BENCH_PREFLIGHT.md` — 现场安全协议 / V2；B0电气/固件身份/线束映射；工作包授权后执行。
- `tests/AutoTest.Devices.Tests/UsbCanProtocolTests.cs` — 模块测试代码 / V0；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.Integration.Usb/UsbProtocol.cs` → `src/AutoTest.Devices/Usb/UsbWireCodec.cs`；策略 `PORT_ALGORITHM`；必须验证：Frame encoding/CRC/decode; keep firmware vectors and malformed frames。
- `src/AutoTest.Integration.Usb/UsbSession.cs` → `src/AutoTest.Devices/Usb/UsbSessionService.cs`；策略 `ADAPT`；必须验证：Async read loop/request sequence/reconnect; add generation-bound late-response rejection。
- `src/AutoTest.Integration.Usb/LibUsbByteTransport.cs` → `src/AutoTest.Devices/Usb/IUsbByteLink.cs`；策略 `ADAPT`；必须验证：Native transport isolated behind link; actual driver choice preflight O01。
- `src/AutoTest.Integration.Usb/UsbTransportErrorClassifier.cs` → `src/AutoTest.Devices/Usb/UsbDiagnosticsProjection.cs`；策略 `ADAPT`；必须验证：Normalize hardware transport errors; no guessed device health。
- `src/AutoTest.Integration.Usb/UsbNativeRuntimePreflight.cs` → `src/AutoTest.Devices/Usb/UsbDiscovery.cs`；策略 `ADAPT`；必须验证：Platform package/driver/permissions need actual Windows proof。
- `src/AutoTest.Communication.Can.Usb/CanUsbPayloadCodec.cs` → `src/AutoTest.Devices/Can/CanFrameCodec.cs`；策略 `PORT_ALGORITHM`；必须验证：15B CAN payload, port/id/DLC; source/board role mapping still unproved。
- `src/AutoTest.Communication.Can.Usb/UsbCanAdapter.cs` → `src/AutoTest.Devices/Can/CanFrameHub.cs`；策略 `ADAPT`；必须验证：Receive/Send adapter; replace direct consumers with unique Hub。
- `src/AutoTest.Communication.Can/CanBus.cs` → `src/AutoTest.Devices/Can/CanFrameHub.cs`；策略 `ADAPT`；必须验证：Dispatch, waiters, periodic transmission; add leases/time/overflow attribution。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `A1-T01` USB黄金帧向量 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`UsbSessionContractTests.Verify_A1_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A1-T02` 随机拆包与粘包 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`UsbSessionContractTests.Verify_A1_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A1-T03` 非法CRC/垃圾字节 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`UsbSessionContractTests.Verify_A1_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A1-T04` SEQ0异步帧 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`UsbSessionContractTests.Verify_A1_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A1-T05` 请求超时及迟到回包 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`UsbSessionContractTests.Verify_A1_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A1-T06` 真实PING与多次断连 — `BENCH_MANUAL_OR_UI`；候选类/程序：`UsbSessionContractTests.Verify_A1_T06`；前置：B0+B1。
- `A2-T01` 逻辑CAN映射正反负测 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`StationControlContractTests.Verify_A2_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A2-T02` SET_IGN请求但缺物理证据保持Unknown — `AUTO_UNIT_OR_MOCK`；候选类/程序：`StationControlContractTests.Verify_A2_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A2-T03` 夹具操作无到位反馈 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`StationControlContractTests.Verify_A2_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A2-T04` 20bit按键帧黄金用例 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`StationControlContractTests.Verify_A2_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A2-T05` KEY16实测 — `BENCH_MANUAL_OR_UI`；候选类/程序：`StationControlContractTests.Verify_A2_T05`；前置：B0+B2+B4。
- `A2-T06` 上电默认输出核查 — `BENCH_MANUAL_OR_UI`；候选类/程序：`StationControlContractTests.Verify_A2_T06`；前置：B0。
- `A3-T01` 同帧同时交付CAN+DBC+UDS+Wave — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CanHubContractTests.Verify_A3_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A3-T02` 背压丢包标记 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CanHubContractTests.Verify_A3_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A3-T03` 两个写者争用 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CanHubContractTests.Verify_A3_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A3-T04` CanId与IDE检验 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CanHubContractTests.Verify_A3_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A3-T05` 断连订阅释放 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`CanHubContractTests.Verify_A3_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `A3-T06` 真实双路流实测 — `BENCH_MANUAL_OR_UI`；候选类/程序：`CanHubContractTests.Verify_A3_T06`；前置：B0+B1+B2。
- `FW1-T01` 主控源码参与编译清单 — `STATIC_REVIEW_AND_BUILD_ID`；候选类/程序：`FirmwareCompatibilityContractTests.Verify_FW1_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `FW1-T02` 按键固件目标选择 — `STATIC_REVIEW_AND_BUILD_ID`；候选类/程序：`FirmwareCompatibilityContractTests.Verify_FW1_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `FW1-T03` 双固件版本对照 — `STATIC_REVIEW_AND_BUILD_ID`；候选类/程序：`FirmwareCompatibilityContractTests.Verify_FW1_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `FW1-T04` 命令变更回归选择 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`FirmwareCompatibilityContractTests.Verify_FW1_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `FW1-T05` KEY16问题登记和复验 — `BENCH_MANUAL_OR_UI`；候选类/程序：`FirmwareCompatibilityContractTests.Verify_FW1_T05`；前置：B4/固件身份确认。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
