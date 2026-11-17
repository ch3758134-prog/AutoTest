# AutoTest｜上位机执行 AI：主控 USB/双夹具整改交接合同

**交付身份**：项目管理 G3 已批产品增量与接口对齐任务；供 HOST/DEVICE/G4 WPF 执行 AI 按批准的范围实施，不能自行修改用户既定主工作区。  
**固件最新源代码**：`Main_Control@16abba7fec94dc4b7fbdc05b91ee209b5c7efa12`。**已经实现新 USB 指令的源码，尚未证实 Keil Build、烧录到实体板卡或台架操作通过。**  
**源码接口**：`docs/interfaces/USB_HOST_CONTROL_INTERFACE.md` v0.2；[精确版本](https://github.com/ch3758134-prog/AutoTest/blob/16abba7fec94dc4b7fbdc05b91ee209b5c7efa12/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)。  
**当前 HOST 目标分支**：`feature/g4-html-parity-20261115`，开始时查询实际 HEAD 与当前运行 EXE 来源。**不写 main，不把 UI-only Preview 当做真实 USB 程序**。

## A. 必须准确实现的现行 USB / GPIO 合同

| 能力 | 源代码已实现的命令与格式 | 上位机要求 |
|---|---|---|
| WinUSB | VID 0x1183 / PID 0x5850；GUID `4da6d065-1e3e-47e6-8cc3-c6977351defb`；Bulk OUT 0x03 / IN 0x82 | 复用现有发现与实际设备会话，保留连接阶段详细错误 |
| 应用帧 | 0xA5/0x5A，SEQ/响应STATUS/CRC16 MODBUS，CAN0/1转发 | 不重写协议，不把 ACK 当成 DUT 或夹具机械确认 |
| PING / 版本 | F0 PONG；F1 当前源代码返回 `EPB_TEST_CTRL v0.4-dev` | GET_VERSION + 真实已刷固件身份核验；不可仅凭 VID/PID/F1 字符串推断新接口现场可用 |
| IGN | 0x12 设置 1B 0/1；0x13 查询空请求→1B | GPIO 语义，非 ECU 点火证明 |
| **夹具1** | **0x18 SET_CLAMP，请求 2B `[1, state=0|1]`** | 只改夹具1 / PTB1 / J1 B5；1压紧、0释放 |
| **夹具2** | **0x18 SET_CLAMP，请求 2B `[2, state=0|1]`** | 只改夹具2 / PTC2 / J1 B4；1压紧、0释放 |
| 夹具状态 | 0x19 GET_CLAMPS 空请求→1B bit0/bit1 | 仅报告 MCU GPIO 电平，不等于机械到位 |
| 废弃功能 | 0x10/11、0x14/15、0x16/17、0x1F 在新固件已删除路由 | 移除新 HOST 用户入口和调用；不要暴露 RL_DRV |

最新 `ASW_PwrCtrl.c` 已实现双夹具独立 GPIO，`ASW_UsbSvc.c` 已移除旧供电/SOL 命令路由，`EAL_GPIO.c` 只初始化 Air1/2 LOW、IGN HIGH、RL_DRV HIGH。旧固件 v0.3 的 0x18 **一字节互斥协议不兼容新版**。绝对不要自动回退或“猜格式”发送。

**硬件安全约束**：RL_DRV 属于内部 PCB 功率链，不是 HOST 操作；不能关掉它模拟废弃电源按钮。当前固件在 USB 枚举丢失且曾压紧时自动将两夹具 LOW，IGN 上电 HIGH；这些尚未通过真实安全验收，HOST 不得自行设定断连后的必然安全结果。请求、USB ACK、MCU GPIO、J1 电平、夹具机械位置是不同证据层。

## 可用于单元测试的固件协议请求帧

下列示例源自本次固件 v0.2 接口文档，包含实际 CRC16/MODBUS，**只是协议测试向量，不得当成现场发送授权**：

| 请求 | 完整 HEX |
|---|---|
| 夹具1压紧，SEQ=5，0x18 [1,1] | `A5 05 00 18 02 01 01 67 7E 5A` |
| 夹具2压紧，SEQ=6，0x18 [2,1] | `A5 06 00 18 02 02 01 67 BD 5A` |
| 夹具2释放，SEQ=7，0x18 [2,0] | `A5 07 00 18 02 02 00 A7 AC 5A` |
| 查询夹具，SEQ=8，0x19 | `A5 08 00 19 00 09 D4 5A` |

## B. 要检查并定点修改的现有 HOST 代码

1. `src/AutoTest.Devices/Usb/MainControllerCodec.cs`：新增严格的 `SET_CLAMP 0x18` 两字节请求与 `GET_CLAMPS 0x19` 一字节响应解析，拒绝非法 clamp/state、错误长度及非预期响应。现有 PING/CAN/IGN 编码必须保留。
2. `src/AutoTest.Devices/Usb/UsbSessionService.cs`、`PhysicalCommandFence.cs`：接入夹具物理操作授权、同一会话/设备所有权、超时/取消/断线 UNKNOWN。不得放宽 Fence。
3. `src/AutoTest.Desktop/Hardware/HardwareConnectionService.cs`：单实例 USB 设备会话、连接分阶段诊断（枚举→Open→管道→F0→F1）；新增夹具1/2设置/查询与错误审计，不为重建 UI 另开一套主控连接。
4. `src/AutoTest.Devices/Station/StationControlService.cs`：两个独立夹具输出语义，状态分开保存/展示，不复用旧 SOL、互斥模式或覆盖另一夹具。
5. `src/AutoTest.Desktop/Testing/ConfiguredStationActionPort.cs`、`RealTestWorkbenchService.cs`、TEST 步骤/Mapping：夹具1/夹具2可以作为正式 TEST 操作，支持测试用例和公共流程调用（公共流程执行器未接通前应明确阻断）。
6. 手动台架及原生 WPF Inspector：分别提供夹具1/2“压紧/释放”，清楚显示固件版本、设备可用性及 REQUEST/ACK/UNKNOWN；移除旧 ECU/KEY 供电、原 SOL 的用户入口。保留冻结 IDE 主布局。
7. 专项验证：两字节 Payload / 帧与 CRC；00、01、10、11；单独修改夹具保持另一路；非法值 ERR_FMT；旧版 0x18 禁止降级；旧供电/SOL 不再调用；断线、超时与重新连接；原 USB 和 CAN0/1 功能不回归。

以上为**文件级责任调查清单**，不授权为了快捷而重写整个设备/TEST 后端。现有真正可用的 WinUSB/CAN/IGN 路径必须保留。若跨专业共享接口修改有冲突，应报告 PM/专业负责人定位决定。

## C. 必须按顺序推进

1. **固定源版本**：核对实际 HOST HEAD、`Main_Control@16abba7f` 及其接口文档，明确现场主控板是否实际烧录 v0.4-dev 或其他可验证等价协议。此前用户遇到 USB 连接失败，应保留完整诊断日志。
2. **可先编码但不可冒险发送**：实现新设备 API、TEST/手动台架 UI、编辑/保存/预检，先完成本地 Windows 构建测试；在未证明实际板卡具备新版夹具协议和现场条件之前，夹具真实操作显示 `FIRMWARE_UNVERIFIED / BLOCKED`，**不发送 0x18**。
3. **固件专业构建与识别**：取得实际 Keil 编译、HEX/镜像身份与烧录回执，确认从真实设备 `F1` 回读身份，并将当前设备与受审固件版本/接口对应。固定 `v0.4-dev` 字符串本身不具备自动校验 SHA 能力，需要额外证据。
4. **现场分级验证**：经负责人按现场安全前提明确许可后先测 USB/PING、CAN0/1、IGN，再独立测试夹具 00/01/10/11 与一次只改一路；分别记录 MCU ACK、GPIO 读回、J1 电压、实际气路/机械位置，随后验证断线/上电/重新连接。
5. **真实 TEST 闭环**：手动台架独立夹具控制 → TEST 夹具步骤 → 同一资源/运行证据 → 公共流程（待流程解释器真实支持后），不允许 Mock PASS 或无证据的“真实联调完成”。

## D. 不得执行

- 不对未确认版本的现场板卡发送新 2B 0x18；不退回旧版 1B 互斥 0x18；不使用版本字符串、PING/ACK 冒充实体夹具到位。
- 不新增已取消的 ECU_PWR/KEY_PWR/旧 SOL/RL_DRV UI，不通过改 HOST 实现板级内部功率功能。
- 不把 GPIO 初始 LOW/HIGH 或 USB 枚举丢失后的释放，自动当作已经批准的安全策略；断线后 UNKNOWN 不可静默变成 PASS。
- 不改 Main_Control/Key_Sim 固件，不动 Flash Lab/冻结 HTML/其他专业私有代码；不直接修改 main。
- 不用 Mock、随机状态、假的设备连接或 UI Preview 来验收真实能力。
- 本地 Codex 只按精确指令做 Git/Build/Test/原始日志，不擅自修改业务代码或盲猜编译错误。

## E. 执行 AI 回报的最低证据

精确 HOST/固件/现场 HEX 版本；文件与接口差异；Windows Build/Test/真实 WPF 启动；连接失败所处 USB 阶段；现场板卡协议兼容判定；两夹具各四组合和断线实测（没有证据一律 NOT_VERIFIED）；已知尚未验证的硬件动作与物理风险。明确区分 `SOURCE_UPDATED / BUILD_OK / USB_CONNECTED / GPIO_VERIFIED / MECHANICALLY_VERIFIED` 五个层级。

**管理合同：** [G3 主控接口变更](CR-MAIN-CONTROL-USB-CLAMPS.md)；[公共流程](CR-PUBLIC-FLOW.md)；[直接 KEY](CR-KEY-DIRECT-STEP.md)。
