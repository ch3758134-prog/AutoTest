# AutoTest Flash Lab｜F0 真实源码调查与独立上位机交付基线

**工作分支：** `feature/flash-lab-20261114`，起点 `local@a9a26ebfba1efcf032ff8ec8c435336a89469647`。  
**状态：F0_SOURCE_AUDIT_COMPLETE / G3_F1_DESIGN_CANDIDATE / G4_CODE_NOT_YET_AUTHORIZED。**
**2026-11-15 用户决定：** 单独烧录研究并实现**独立 Windows 上位机**；烧录链路研究和可运行工具先于独立 UDS 专业工具研究。自动测试 UI 在其他工作包并行按冻结 HTML 实施，F0 不修改其 XAML/TEST VM。

## 1. 现有真源码与缺失事实（已直接回读分支）

| 对象/路径 | 当前真实能力 | 阻断/下一步 |
|---|---|---|
| `src/AutoTest.Specialists/Flash/FirmwareImageParser.cs` | 读取 Intel HEX、S19、BIN（BIN 显式 baseAddress）、校验 checksum、段重叠和越界，最大 16MiB，生成来源指纹 | 尚不做 OEM Flash address range/ECU 型号适配；配置/镜像合法不能等于能烧录 |
| `Flash/ProgrammingProfile.cs` | 配置 ECU 身份、UDS Endpoint、允许区间、单块最大长度、电压窗口、Profile Revision | `Validate` 遇 `!MockOnly` 明确拒绝物理编程；**F1 保留此护栏** |
| `Flash/ProgrammingPreflight.cs` | 校验镜像、地址、身份、Revision/Hash、站点/电压等 | `ProgrammingEnvironment.IsMock` 为旧模型，不能用虚拟数据通过现场预检；F2 另设计真实前置事实 |
| `Flash/FlashTransferScheduler.cs` | 已有 `0x36` TransferData 分块并检查 `0x76` + 块序号的算法 | 非完整烧录工作流；没有经核实的 RequestDownload/擦除/校验/复位/恢复前后串接；当前风险请求还被 UdsClient 阻止 |
| `Uds/UdsSessionService.cs` | 持有一个 CAN 发射 Lease 并复用 UdsClient + ISO-TP | 只是进程内授权/关联；跨 Windows EXE USB 独占未完成 |
| `Uds/CanHubIsoTpLink.cs` | 按请求/响应 CAN ID 通过 Hub TX、RX 做 ISO-TP 帧交换；校验 SessionGeneration/丢帧 | `HardwareConnectionService` 构造的 Hub TX 尚未接真实物理发送路径，需按物理 Fence 受控接线 |
| `src/AutoTest.Devices/Usb/PhysicalCommandFence.cs` | 有限实体写入拦截 | 不能把“只读 DID”或“已经拿到 CAN Lease”当成自动绕过 Fence 的许可 |
| `src/AutoTest.Desktop/Hardware/HardwareConnectionService.cs` | 有 WinUSB/主控真实连接、CAN RX 和有限人工单帧 CAN TX | 位于 Desktop，不允许 FlashLab 新 EXE 直接依赖主 UI DLL；应抽出唯一设备会话适配层 |
| 独立 Windows FlashLab 项目 | 当前分支 `src/` 仅 `AutoTest.Desktop` 是 WPF EXE | **尚不存在**，建议 `src/AutoTest.FlashLab/` 新项目和明确程序集引用，独立启动/关闭/日志 |

以上是静态源码事实，不能被描述为“CAN1 DID 已读通”“真实刷写已成功”。

## 2. 研究依据与可核查性

- 既有 [CanTool F0 调查 Issue #25](https://github.com/ch3758134-prog/AutoTest/issues/25) 包含 FLASH-FACT 研究任务；[Flash 实施协调 Issue #44](https://github.com/ch3758134-prog/AutoTest/issues/44) 给出已有调查进展；[G3-F1 接口候审 #49](https://github.com/ch3758134-prog/AutoTest/issues/49) 给出 CAN1 TX、ISO-TP、双层安全边界。
- **历史研究记录不是本分支重新读取过原始 `Configuration.xml` 或全部 CanTool 源码的证明。** 准备实施 F1 导入界面前须拿到实际文件，完成原始字段、合法值、地址和默认值的对照清单；取不到的明确 UNKNOWN。
- 旧研究称 `SafetyFile.dll` 为 managed，`sa_demo.dll` 带 `ZLGKey` demo ABI；此为旧研究陈述，**并不证明目标 ECU Seed/Key 有效，也不能由 DLL 名称推导算法**。

## 3. 独立 EXE 目标工程结构（待 G4 精确授权创建）

```
src/AutoTest.FlashLab/
  AutoTest.FlashLab.csproj             # WPF WinExe，独立入口
  App.xaml / App.xaml.cs
  Shell/FlashLabWindow.xaml(.cs)       # 独立窗
  Presentation/FlashLabViewModel.cs    # 输入/命令/状态/日志
  Configuration/CanToolXmlImport.cs    # 来源/字段/校验/映射
  Evidence/FlashLabEvidenceExporter.cs# 原始错误与事件
```

**复用公共模块而非调用主 UI：** `AutoTest.Projects` 工程数据、`AutoTest.Specialists` FirmwareImageParser/UDS 真实算法、`AutoTest.Devices` USB/CAN/实体 Fence；必要共享 WinUSB 会话服务抽至 `AutoTest.Devices`/经审核的 Application，禁止复制驱动。独立 EXE 与正式 HOST **不同时占有一个物理主控**；连接时由 OS/设备实例互斥与 generation 检查 fail-closed。

## 4. 执行序列：先解决 F1，后由实证决定 F2

| 阶段 | 用户任务 | 输出证据 | 不允许 |
|---|---|---|---|
| F0-A | 查清 CanTool XML → 阶段、每字段来源及合法范围；核对真实样例/版本 | 配置矩阵、原文件 SHA/URI、Unknown 项 | 把研究推断填成当前 ECU 实测参数 |
| F0-B | 当前 USB/CAN1 接口复用评审与物理发送权限 | 服务对象/Owner/CanLease/ISO-TP/FrameFence 图 | 复制 WinUSB 适配或在 UI 直接发任意 CAN1 |
| F1-A | 独立 Windows EXE，配置和 HEX/S19/BIN 导入、镜像段/地址/Hash 校验 | Windows 启动/文件导入/错误日志/证据导出 | 未连接 ECU 却显示烧录完成 |
| F1-B | 真实主控身份、CAN1 监视、指定只读 DID 0x22 与完整 ISO-TP | USB 请求、CAN TX ACK、完整 ECU RX 或明确 NRC/Timeout | 不批准就发 0x27、0x31、0x34、0x36、0x37 |
| F1-C | 用户独立工具实测、断连与错误修复 | 用户实测结果、精确 Commit、回滚说明 | 把合成/回放当真机读 DID |
| F2 | 已确认目标 ECU 的 APP/BOOT 进入、SecurityAccess、Erase/Download/Verify/Reset/恢复 | 真实完整烧录证据、逐阶段错误和恢复、用户许可 | 目标未知时默认刷写和放开 MockOnly 保护 |

## 5. F2 进入条件：需研究完成，不让用户代替研究

1. APP/Boot 诊断地址、11/29-bit、波特率、会话时序、P2/P2*，均有适用 ECU 的文件或实机证据。
2. Firmware HEX/S19/BIN 的地址/长度/填充/受允区域、块传输长度（区分 0x74 响应字段与实际 UDS payload）由真实目标校验。
3. Seed/Key DLL 实际函数签名/调用约定/位数/提供者和 OEM 匹配证据；失败不可静默使用 demo 算法。
4. 完整 UDS 阶段进入/成功/失败/NRC、擦除 RID、校验 RID、Reset/Boot→APP 复验顺序与可恢复性；不能仅按通用 UDS 标准推定私有流程。
5. 独立外部电源/IGN 当前事实及操作许可，设备断连/未知 Flash 状态优先阻断。
6. 用户明确批准对应 ECU 实体写入试验后才发危险请求。**研究与 Windows EXE 的授权不等于烧录授权。**

## 6. 与自动测试主线接口

烧录研究先成为**独立、真实工作的可诊断工具**。只有 F2 对指定 ECU 的真实安全性、校验与失败恢复验证成功后，才将同一 `IProgrammingWorkflow` 服务作为自动测试步骤调用的候选，不嵌第二套核心协议，也不逼用户手工把 Flash Lab UI 嵌进正式自动测试主区。

**独立 UDS 工具**的完整使用界面在烧录研究成功后开始；Flash 内必须实现的 0x22/UDS/ISO-TP 会话属于必要底层技术，不算违反此顺序。

**下一步研究事实输出**：原始 CanTool XML 字段表与当前代码映射、F1 EXE 的入口与文件职责、真实 CAN1 只读诊断 TX 候选补丁；按受控阶段批准后 G4 编码。本记录只确立研究事实和设计，不执行设备写入。
