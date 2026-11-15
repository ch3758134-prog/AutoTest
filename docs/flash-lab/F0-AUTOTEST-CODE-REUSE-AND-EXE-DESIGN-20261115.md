# AutoTest FLASH F0｜现有 API 逐项复用、独立 Flash Lab 设计与可验收行为
日期：2026-11-15
基线：feature/flash-lab-20261114@18108e85e80318f3b684799c836029e801067226
结论：前端新建、后端唯一复用；仅已证明的物理 TX / 有限权限 / 独占缺口允许经审批最小补齐。NOT_BUILT / NOT_CONNECTED / NOT_FLASHED。

## 1. 实际回读的唯一后端及调用链
此表内容来自本轮对专项分支真实源码的直接回读（A 级证据）：

| 源码 | 现成接口、行号 | 可用事实/严格限制 |
|---|---|---|
| Devices/Usb/UsbSessionService.cs | ConnectAsync 52-73、RequestAsync 120-165、DisconnectAsync 167-196、TraceRecorded 23-43 | 真实 USB 请求/响应、SEQ、代际、超时、RawHex Trace；USB ACK 不是 ECU 接收 |
| Devices/Usb/WinUsbByteLink.cs | OpenCore 93-209、WriteCore 247-280、CloseCore 285-323 | 真实 WinUSB，WriteCore 257 必经 PhysicalCommandFence |
| Devices/Usb/WinUsbTransportNative.cs | Open 29-32 | CreateFileW 目前 FileShareRead | FileShareWrite，**没有证明跨进程独占** |
| Devices/Usb/PhysicalCommandFence.cs | AuthorizeSingleCanFrame 68-83、AuthorizePhysicalRun 24-48、AssertAllowedAndConsume 85-123 | 已有一次性精确帧或有期限的 ID 白名单测试授权；后者只匹配 CAN ID，不审 UDS SID，不能拿来作为 F1 只读诊断权限 |
| Devices/Can/CanFrameCodec.cs | ToUsbPayload 8-22、FromUsbPayload 23-37 | 15 字节 USB CAN payload；实体 port 1=DUT，port 0=KEY_SIM |
| Devices/Can/HardwareCanReceiveBridge.cs | OnAsyncResponse 57-64、PumpAsync 65-95 | 0x41 真实 CAN 接收，记录主机到达时间和 dropped；不是 ECU 采样时间；不发送 |
| Devices/Can/CanFrameHub.cs | 构造器 19、TransmitAsync 50-54、SubscribeAsync 37-49 | 构造时可注入物理发送委托；若空则 NO_TRANSMITTER |
| Devices/Can/CanTransmitGate.cs | AcquireAsync 12-24、Validate 25-29、Reset 11 | 进程内按总线 Lease + Generation；不等于物理授权与跨进程独占 |
| Desktop/Hardware/HardwareConnectionService.cs | ConnectAsync 38-95、SendCanOnceAsync 112-132、SendTestCanAsync 164-181 | 已完成实际 USB ping、单帧人工授权发送；在 ConnectAsync 第69行 new CanFrameHub() 未注入 TX，故 ConnectedCanHub 用于 UDS 时无法真实发帧 |
| Specialists/Uds/UdsSessionService.cs | StartSessionAsync 11-28 | 现成独占 Lease / CanHubIsoTpLink / IsoTpConversation / UdsClient，Lease 初始两分钟 |
| Specialists/Uds/CanHubIsoTpLink.cs | PumpAsync 29-51、SendAsync 52-61 | 按指定响应 ID 和代际/丢帧筛选；所有 TX 依赖 Hub 发送委托 |
| Specialists/Uds/IsoTpConversation.cs | SendPduAsync 48-80、ReceivePduAsync 81-107 | Classic CAN normal addressing 单帧/多帧、FC/BS/STmin；不另写 ISO-TP |
| Specialists/Uds/IsoTpFrameCodec.cs | MaxClassicPdu=4095、FrameKind/SF/FF/CF/FC | 正确校验 CF sequence、STmin 编码；特定 OEM 其他寻址方式不支持 |
| Specialists/Uds/UdsEndpointProfile.cs | ValidateAddressing 17-25 | DUT CAN 专用，Normal addressing，req/rsp 均11-bit或均29-bit；不支持 mixed |
| Specialists/Uds/UdsServiceCatalog.cs | 10-36 | 22 为 ReadOnly；10、11、27、31、34、36、37 为更高风险 |
| Specialists/Uds/UdsClient.cs | RequestAsync 33-85、Trace | 非 ReadOnly 风险直接拒绝(44-45)，按 NRC 78 有界等待，Trace 保存请求/应答；**直接挡住实体烧录** |
| Specialists/Flash/FirmwareImageParser.cs | ParseHex 25-63、ParseS19 64-108、ParseBin 109-115、ValidateSegments 116-131 | HEX/S19 记录 checksum、EOF、重叠/溢出拒绝；BIN 显式 baseAddress；MaxImageBytes=16MiB；不是 OEM Flash map 审核 |
| Specialists/Flash/ProgrammingProfile.cs | Validate 19-38、ValidateImage 40-46 | 有地址区间/身份/Revision 等模型，但 !MockOnly 会抛 NotSupportedException |
| Specialists/Flash/ProgrammingPreflight.cs | CheckAsync 21-51 | 环境里仍有 IsMock，真实环境返回 PHYSICAL_FLASH_NOT_AUTHORIZED；F1 不能拿旧 preflight 的模拟准备状态充当现场许可 |
| Specialists/Flash/FlashTransferScheduler.cs | WriteBlocksAsync 10-31 | 已实现 36 + BSC，需 76+BSC 才计确认字节；由于 UdsClient 风险拦截且缺上游 OEM Workflow，当前不能实刷 |
| Specialists/Flash/IProgrammingWorkflow.cs | BuildPlan、ExecuteAsync | 只是接口，非完整 CanTool 状态机 |

当前已证实不存在 src/AutoTest.FlashLab 项目。AutoTest.Devices / Specialists csproj 已有公共 Contracts 依赖；正式 Desktop 单独是 WPF WinExe。无需复制/依赖 Desktop 主 UI 组件，也无需重新实现协议库。

## 2. F1 真实只读调用图
独立 EXE 负责：配置/文件/工作台、操作权限请求、日志与审计。

设备数据通路（现成模块串接）：
Windows WinUSB (WinUsbByteLink)
 → UsbSessionService (SEQ, Generation, ACK, Trace)
 → 0x41 HardwareCanReceiveBridge
 → CanFrameHub SubscribeAsync
 → CanHubIsoTpLink (CAN1, RX ID filter, Generation)
 → IsoTpConversation
 → UdsClient/UdsSessionService
 → 0x22 DID 结果/NRC/超时

发送方向：
UdsClient ReadOnly 0x22
 → IsoTpConversation（请求 SF，响应 FF 时回发必要 FC）
 → CanHubIsoTpLink.SendAsync
 → CanFrameHub.TransmitAsync（**当前需实例注入受控 TX 委托**）
 → CanTransmitGate Validate + ReadOnlyDiagnosticScope Validate
 → CanFrameCodec.ToUsbPayload(role=DutCan, physicalPort=1)
 → UsbSessionService.RequestAsync(0x40)
 → WinUsbByteLink.WriteCore / PhysicalCommandFence
 → 主控 ACK（只代表主控已受理）
 → ECU CAN RX（真实 0x41，经 ISO-TP/UDS 判定）

需明确：使用现成组件组合独立进程会话，而不是复制 WinUSB/ISO-TP 实现。正式 HOST 仍调用原 Devices；FlashLab 不引用 Desktop。是否将少量通用 composition 提取至 Devices，以 F1 实施审查/Windows 实测决定，**不得预设整个共享会话框架重构**。

## 3. 三个当前精确缺口与最小方案
### A. Hub 缺 TX 委托
- 事实：Desktop/HardwareConnectionService.cs:69 用 new CanFrameHub()；CanFrameHub.cs:50-54 无委托即 NO_TRANSMITTER。FlashLab 自身构造 Hub 时应提供注入委托；不必为此修改 Desktop 的大规模 UI。
- 候选：在 Devices/Can 增薄的 Can1ReadOnlyDiagnosticTransmitter，依次检查 role、lease generation/owner/expiry、Scope、frame kind/SID、USB session、证据，然后调用既有 UsbSessionService.RequestAsync(0x40)。不实现另一个 CAN Codec。
- Scope 必须限定一个设备/会话、固定 CAN1 请求/响应 ID、11/29bit、精确 0x22 DID、时间及 TX 帧数上限。请求 0x22 SID 在单帧内校验，响应 FF 关联后才允许 FC，禁止 FF/CF 隐含任意其他 UDS 请求。
### B. 物理 Fence 目前不支持有界多帧只读交换
- 事实：一次性帧只能放行确切 15B；测试 Run 白名单仅查 CAN ID，不能仅凭合法 ID 允许任意 27/31/34/36/37。
- 候选：最小扩展 PhysicalCommandFence / WinUsbByteLink 的显式 ReadOnlyDidExchange grant，由物理 raw 0x40 写入口再次检查该 generation 的允许请求 SF / 关联 FF 后必要 FC / 数量 / 过期。任何直接 UsbSessionService.RequestAsync 绕过 UI 的请求仍必须失败。绝不把原测试 Run 白名单当作 F1 授权。
- 对于 F1 非编程通信，不得调用 AuthorizePhysicalTestRun 自动放行全部同 ID 帧。
### C. 同物理主控两个 EXE 的独占未知
- 事实：WinUsbTransportNative.cs:29-32 当前 CreateFileW 的 share mode = READ | WRITE；CanTransmitGate 只在单进程。无法证明主 HOST 与 FlashLab 同开安全。
- 候选首选最小保护：在共享 WinUsbByteLink 的 Open 生命周期对精确 DeviceInstanceId 取操作系统级独占，或经 Windows 真实验证改为 share-none 打开；选择一种就够，不能两套新设备框架；异常退出/重连均使旧 Scope/Lease 失效。
- HOST/FlashLab 交叉打开试验必须验证一方连接时另一方明确提示占用；旧 Host USB 连接/监视不回归。Windows 行为未测，不在此预先宣称实现有效。

## 4. 独立 Windows EXE 交互设计
名称：AutoTest Flash Lab；新建 src/AutoTest.FlashLab/，Windows WPF WinExe，浅色中文界面、默认合适尺寸且可最大化，独立启动与关闭；不嵌主 IDE，也不复制主 Shell。
建议单窗布局：
- 顶部：主控“连接/断开”、设备身份、CAN1 角色、连接 generation、当前操作“仅只读”状态；外部供电状态只显示实证/UNKNOWN，不假装拥有控制权。
- 左侧（约1/3）：配置源与编辑/保存、APP/Boot Req/Rsp ID、11/29bit、波特率的 requested/effective/unknown；固件选择 HEX/S19/BIN、BIN baseAddress、SHA256、镜像区间；每字段可查看来源和错误。
- 中间（约1/3）：本次操作步骤（配置→镜像→连接→身份/DID→预检）；每一步清楚展示允许执行、BLOCKED、UNKNOWN 和理由；F1 擦除/下载/烧录按钮根本不存在可执行命令。
- 右侧（约1/3 或可调宽）：USB/CAN/UDS 真实时间线，过滤、复制、错误/NRC/响应字节、CorrelationId、设备 generation、主控 ACK/ECU 应答严格区分；日志不得在切页时丢失；异常可一键导出。
- 文件编辑采用就地表单和字段验证，不把高频参数都藏进新窗口；操作者点击“读取 ECU 身份”前可看到实际将发送的 CAN1 请求 ID、DID、配置/状态。
- 单实例操作一个 DUT；断连/取消/超时后的结果显示“未确认/不可继续”，不能假装刷写成功。

## 5. 配置导入的可落地合同
FlashLab/Configuration/CanToolXmlProfileReader：读原字段及别名；区别 XML、settings.json 和代码默认来源。未在历史 Loader 中消费的 XML 字段原样保留或列表标记 UNMAPPED；支持检查、就地编辑、另存为而不悄然覆盖原文件。必须处理合法 hex、地址溢出、Req/Rsp 值和类型、FlashStart/End 的 inclusive 计算、未知字段、缺字段、保存后重新读取一致性。
UI 必须显示两组诊断寻址：APP 11bit、Boot 29bit 的历史样本可以作为“仅供查看的参考样本”，禁止自动套入当前 DUT。CAN bitrate 未由主控证实时标明 UNKNOWN。
镜像在 F1 使用当前 FirmwareImageParser，不复刻 CanTool 容错跳过/强制填 FF/末端写 12345678 的危险行为；BIN 必须输入 baseAddress；显示原始文件 SHA、段地址、endExclusive、总字节、重叠/格式拒绝原因，不调用旧 MockOnly ProgrammingPreflight 得出实体许可。

## 6. 证据合同与终态
每条事件至少有 UTC、host monotonic ticks（若已有源支持）、session generation、CorrelationId、阶段、层级 USB/CAN/ISO-TP/UDS、方向、物理端口、Req/Rsp CAN ID、原始帧/PDU 引用、USB ACK / ECU POSITIVE / ECU NRC / TIMEOUT / REJECTED / CANCELLED、错误码及原始异常。不要把主控 ACK 显示为 ECU 成功。
原始 USB TraceRecorded、HardwareCanReceiveBridge.Trace、UdsClient.Trace 和操作日志汇总为同一 ZIP（JSONL + 配置/镜像脱敏摘要 + 版本/文件 SHA），含不可删改的出错上下文、丢帧计数、取消/断连日志。Seed/Key/敏感识别信息后续应脱敏。
终态区分 OFFLINE_VALIDATED、USB_CONNECTED、CAN1_OBSERVED、DID_CONFIRMED、DID_NRC、DID_TIMEOUT、DIAGNOSTIC_UNCERTAIN、PRECHECK_BLOCKED。F1 不存在 FLASH_SUCCEEDED。

## 7. F1 验收（代码/Windows/设备分开）
1. 目标 WinExe 可独立 dotnet build / run，未运行 Windows 编译不得报告 BUILD_PASS；启动不要求主 IDE。
2. 导入 XML，编辑保存重开字段与来源、未知字段；非法 ID、范围、缺字段给中文错误。
3. 解析真实 HEX/S19/BIN，BIN 地址必填，显示 SHA、段、范围和格式错误；不能把任何测试样本当现役 ECU 固件。
4. 真实 USB 主控能连接、识别、断开、再次连接，ConnectionGeneration 和故障可见；用户日志保留。
5. 同物理设备在 HOST 与 FlashLab 不能同时占有；拒绝时不抢占、不静默重试；有 Windows 双进程试验证据。
6. 独立 Exe 通过真实 CAN1 对**经许可的**精确 0x22 DID 发起一次请求，允许合法 ISO-TP FF/CF/FC，记录原始数据、NRC、超时；主控 ACK 与 ECU 回复分开。没有实机回包不能标记 READ_PASS。
7. 业务 + PhysicalCommandFence 双层拒绝 10/11/27/31/34/36/37、非法 CAN0、错 ID、错 DID、旧 generation、过期/过量、未批准 FC 和直接 USB 旁路；Windows 断连与取消验证 fail-closed。
8. 文件操作和 UI 不执行任何自动诊断发送。无 mock/replay 通信与假成功替代真实测试。静态/单元边界测试只能证明算法约束，不能证明真实 ECU。
9. 原始日志导出可由另一台机器离线追查失败发生于哪一 USB/CAN/UDS 阶段；包含源 SHA、版本与时间。
10. 未经 PM 批准不得改实体通道安全限制；F1 不含 SecurityAccess、Erase、RequestDownload、TransferData、Exit 和复位。

## 8. F2 与自动测试关系
F2 实体写入不在本阶段，届时在同一个 EXE 上引入经实证的 OEM Session→Security→Erase→RequestDownload→TransferData→TransferExit→Verify→Reset→APP Verify，沿用当前 UDS/ISO-TP/USB/CAN 与 FlashTransferScheduler，并新增必要 OEM Workflow 和真实授权路径；必须逐段显示已 ACK、已校验与最终 APP 复验。
仅当指定 ECU 的实体刷写经授权真实验证后，才提议把同一个非 UI 的编程服务给自动测试步骤调用。独立 UDS 工具后置，不在本 F0/F1 实现。

参考：Issue #25/#44/#49；G3-APPROVAL-20261115；本分支 AGENTS.md；F0-CANTOOL-CONFIG-AND-FLASH-TRACE-20261115.md。