# AutoTest FLASH｜G4-F1 独立 WPF Flash Lab 一次性实施授权候选
日期：2026-11-15
身份：FLASH 研究及独立工具研发负责人提交给项目管理/项目负责人审批
状态：CANDIDATE / NOT_APPROVED / NO_FLASH_SOURCE_CHANGES
目标分支：feature/flash-lab-20261114
审查基准：18108e85e80318f3b684799c836029e801067226
关联 Issue：#25、#44、#49；F0 文档另见同目录。

## 1. 申请的唯一工作包
申请一次性实施独立 AutoTest Flash Lab 的 **F1：配置 + 镜像 + 真实主控连接 + 有限 0x22 只读 DID + 原始日志**。不是申请编写第二套 UDS/ISO-TP/WinUSB，也不是申请执行实体 Flash。获批后一次性交付 Windows 可拉取可编译的候选，而不要求每做完一个小窗口就审批一次。
F0 提供的 CanTool 样本值仅做参考与校验示例，不能成为实体 ECU 默认值。F1 的设备物理发报须在用户现场许可范围内，源码实施授权不等于现场发送授权。

## 2. 精确文件责任（拟新增）
| 文件 | 责任 |
|---|---|
| src/AutoTest.FlashLab/AutoTest.FlashLab.csproj | 独立 net*-windows WPF WinExe，引用现有 Devices、Specialists、Contracts（按根 Directory.Build.props 的真实 TFM），不引用 Desktop |
| src/AutoTest.FlashLab/App.xaml、App.xaml.cs | 独立进程入口、异常报告、会话安全关闭 |
| src/AutoTest.FlashLab/Shell/FlashLabWindow.xaml、.xaml.cs | 中文浅色单窗，配置/文件、阶段、真实时间线，直观可最大化 |
| src/AutoTest.FlashLab/Presentation/FlashLabViewModel.cs | 操作命令、控件状态、超时取消、失败处理、进度语义 |
| src/AutoTest.FlashLab/Configuration/CanToolXmlProfileReader.cs | 参考字段、来源标签、未知字段、编辑保存重开、有效值快照 |
| src/AutoTest.FlashLab/Firmware/FirmwareSelectionService.cs | 文件选择、BIN 必填基址、现有 FirmwareImageParser、段/哈希展示 |
| src/AutoTest.FlashLab/Hardware/FlashLabDeviceSession.cs | 只做现有 WinUsbByteLink / UsbSessionService / CanFrameHub / HardwareCanReceiveBridge / CanTransmitGate 的生命周期组合，固定 CAN1；不是新驱动或 ISO-TP |
| src/AutoTest.FlashLab/Diagnostics/ReadOnlyDidController.cs | 调用现有 UdsSessionService / UdsClient；精确一个 DID 操作，不允许任意 PDU |
| src/AutoTest.FlashLab/Evidence/FlashLabEvidenceExporter.cs | 聚合 USB Raw、CAN、UDS、窗口操作记录，导出脱敏 ZIP/JSONL |
| tests/AutoTest.Specialists.Tests/Flash/CanToolXmlProfileReaderTests.cs 或 F1 项目独立纯配置测试 | 配置解析/范围/来源、镜像格式的确定性测试；不得用模拟 ECU 证明实体通过 |
| tests/AutoTest.Devices.Tests/ReadOnlyDidAuthorizationTests.cs | 物理 SID/FC/ID/generation/过期/数量和绕过拒绝的反例；只证明软件门禁 |

上表为最小候选；命名可因真实项目引用契约调整，但不得默默扩展产品范围。无需为独立启动强制修改 main、Desktop Shell 或 AutoTest.sln；可先用项目 csproj 单独 Build/Run。

## 3. 需要 PM 先批准的共享接口修改（仅这些）
| 现有文件/新增薄适配 | 已验证原因 | 最小改动与回归影响 |
|---|---|---|
| 新增 src/AutoTest.Devices/Can/Can1ReadOnlyDiagnosticTransmitter.cs | CanFrameHub 的 _transmit 支持注入但当前 HOST 构造时为空 | 在 FlashLab 的 Hub 注入使用，验证 Lease、scope、物理 CAN1 和请求/响应 ID；用现有 CanFrameCodec+UsbSessionService 发 0x40；HOST 主 UI 不变 |
| 修改 src/AutoTest.Devices/Usb/PhysicalCommandFence.cs | 现有精确单帧只能授权一次、Run ID 白名单无法判断 UDS SID | 新增物理层严格的单次 0x22 DID 范围许可，确认 request SF / 相应 FC、数量与 generation；维持 raw USB 写入口 fail-closed，不触动现有两种授权的旧语义 |
| 修改 src/AutoTest.Devices/Usb/WinUsbByteLink.cs | 新 grant 必须下沉到现有写入口并在断连撤销 | 为受限读 DID 提供内部授权入口；不开放任意原始发送；Disconnect/Reset 撤销 |
| 修改 src/AutoTest.Devices/Usb/WinUsbTransportNative.cs 或在 WinUsbByteLink.cs 做一处最小独占约束（二选一，依据 Windows 真实试验） | CreateFileW 当前 FileShareRead+FileShareWrite，两个 EXE 未证实互斥 | 首选验证 share-none 的 OS 独占，若受硬件/驱动限制则在双方共用 WinUsbByteLink 生命周期使用按设备身份命名的跨进程互斥；不创建新 USB 会话框架，若不能独占则 F1 实体发送阻断 |
| src/AutoTest.Specialists/Uds/* | 当前 ISO-TP/UDS 能处理 0x22 请求及多帧；暂无确证需要修改 | **默认不改**；如 Windows 证据证明具体缺陷，先追加可核查变更影响报 PM，不能自行重构或开放非 ReadOnly SID |
| src/AutoTest.Specialists/Flash/* | 当前 FirmwareImageParser 可用，真实编程仍被 MockOnly 拦截 | **F1 不改**，不移除 MockOnly、不接实体烧录 |

并行冲突约束：不可修改 AutoTest.Desktop/Testing/Projects/Shell 的主 IDE/测试编辑器、main/local/g4-html-parity/ui-design、主控及 KEY_SIM 固件。更改 Devices 物理 Fence/WinUSB 会影响 HOST，需 PM 审核并在用户本地检验旧 HOST 连接和已有人工发报不回归。若 PM 拒绝共享变更，可先交付完全离线配置/镜像 UI，但不得冒称只读联机 F1 已完成。

## 4. 授权后的一次性交付流程
- 先固定审批通过的 HEAD / 文件清单，不从分支名隐式取更高风险权限。
- 实施独立 WPF、配置/镜像/时间线，复用旧设备/诊断代码；只做确有必要的 F1 薄适配。
- 完成精确静态风险检查与可重复参数校验；本地 Windows 编译/启动、无设备故障提示、真实 USB/双进程独占、真实 CAN1 的受控只读 DID 由用户现场验证。
- 实测前/后提交唯一完整候选 commit SHA、变更文件、启动命令、原始日志/错误、已完成和未知项；不运行 GitHub CI、不自行合并 main。
- 编译未跑则 BUILD_UNVERIFIED；未收到 ECU 应答则 ECU_UNVERIFIED；不能把 Mock/TestFixture 当实测。

## 5. 必须验证的反例
- CAN0 发送被拒；合法 CAN1 ID 上的 UDS 27/31/34/36/37 被 USB 物理层阻断。
- 没有 Scope、旧 generation、错误 Req/Rsp ID、错误 DID、过量 FC、超时、取消、断开后发送全部拒绝。
- 主 HOST 与 FlashLab 同时打开相同 DeviceInstanceId 时第二个清楚拒绝，不能双收发造成 ACK/SEQ 交错。
- 0x22 请求已被主控 ACK 但 ECU 无回应 → 超时/未确认；真实 NRC → 明确状态而非成功；0x22 多帧严格按照已有 ISO-TP 处理。
- 缺配置、错 Flash end、HEX/S19 checksum 错、BIN 无 baseAddress、镜像重叠 → 禁止进入下一阶段并保留错误。
- 无任何自动擦除、SecurityAccess、下载、重启、IGN、按键动作；日志无编程成功假事实。

## 6. F2 单独请求，绝不包含在本次 F1
指定 ECU 的真实身份、适用配置和固件 SHA/Revision、Seed/Key DLL 真实 API/ABI 和 ECU 兼容、真实 Boot/APP 地址、Session、OEM RID/擦除/校验、0x74/块长、供电 IGN/断连恢复和最终 APP 复验全部需要可核验的证据。即使 F1 获批，用户也需 **另行、针对具体 ECU/固件明确批准实体写入试验**。届时在同一个 Flash Lab EXE 与现有后端上增加最小 OEM Workflow 和专用物理授权，不开发第二套 Flash/UDS。

## 7. 请求 PM 裁决（明确批准/退回）
- 是否批准以上 **G4-F1 实施一次性委托**（仅限精确文件及行为）？
- 是否同意仅对 Devices/USB 中真实证实的 TX/只读物理安全/单设备独占作受控最小改动，并由 PM 协调与 HOST 的回归？
- 对已缺的原始 CanTool.rar / Configuration.xml / DLL / 指定 ECU 资料：请确认继续保持 UNKNOWN，**不把补材料前的静态复原当作刷写已验证**。

**目前仅申请审批，不视为已经获批。F0 文档提交本身不授权 F1 源码或实体 ECU 操作。**