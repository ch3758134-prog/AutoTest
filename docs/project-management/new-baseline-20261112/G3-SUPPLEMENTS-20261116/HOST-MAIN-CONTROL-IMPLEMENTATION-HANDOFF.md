# AutoTest｜上位机执行 AI：主控 USB/双夹具整改交接合同

**性质**：由项目管理已批准产品决定形成的执行边界；实际编码以最新可用 HOST 与固件 SHA 复核，仍需遵守原有专业开发/提交权限。**本文件不表示主控固件已经更新，也不开放未确认的夹具实体输出。**

## 目标

保留现有真实 WinUSB/CAN1/KEY_SIM/IGN 连接链；新增**夹具1、夹具2独立可见、可编辑、可授权执行**的上位机能力；彻底移除已取消的供电和旧 SOL 的用户入口。给程序编辑、手动台架和自动 TEST 复用**同一 DEVICE 端口**，严禁 UI 单独编码 USB 帧或另造硬件执行路径。

## 源版本和接口

- HOST：`ch3758134-prog/AutoTest`；目标 WPF 分支 `feature/g4-html-parity-20261115`，开始前重新锁定最新 HEAD，并调查与 `main/local` 的相应源码差异；不整体合并其他分支。
- 固件调查来源 `Main_Control@5d2ad5b7f3ae54c6b3e08e363229a25349f4f8be`；文档 `docs/interfaces/USB_HOST_CONTROL_INTERFACE.md`（源自调查，不是新固件验证）。
- USB 发现：VID/PID 1183/5850、GUID `4da6d065-1e3e-47e6-8cc3-c6977351defb`、Bulk IN=0x82/OUT=0x03；应用帧 0xA5/0x5A、CRC16 MODBUS，PING F0/版本 F1。协议底层不得随意重写。
- 双夹具目标：PTB1/J1 B5、PTC2/J1 B4；**独立**高=压紧、低=释放。
- 新命令字段按管理变更文件：目标 0x18 `[clamp=1|2, state=0|1]`；目标 0x19 返回 bit0/bit1。**现有固件只有旧互斥的一字节 0x18**，在固件能力未确证前不允许执行夹具写。
- IGN 0x12/0x13 保留；0x10/0x11、0x14/0x15、0x16/0x17、0x1F 不作为新业务 API/UI；RL_DRV 不暴露。
- 用户近期实际报过 USB 连接失败，不能因为界面重建而回归或用模拟成功掩盖，需保留完整阶段诊断。

## 请执行 AI 审查/修改的真实代码入口

1. `src/AutoTest.Devices/Usb/MainControllerCodec.cs`：新增独立夹具请求编码与严格响应解析，拒绝非法 clamp/state/长度；不再面向新代码暴露旧电源/SOL 功能。**编码新协议不等于现在可以对现役固件发送。**
2. `src/AutoTest.Devices/Usb/UsbSessionService.cs`、`PhysicalCommandFence.cs`：固件协议身份/能力预检、会话失效、串行实体授权、失败及 UNKNOWN。不能放宽物理 Fence。
3. `src/AutoTest.Desktop/Hardware/HardwareConnectionService.cs`：共享主控会话、独立夹具调用与状态读取；保留原有 WinUSB 发现/连接/断开/PING/CAN/IGN 路径。日志明确失败阶段、错误码、实际固件版本。
4. `src/AutoTest.Devices/Station/StationControlService.cs`：按真实可用能力抽象两个独立输出及报告，不能按旧 SOL 或“一次操作两个互斥输出”实现。
5. `src/AutoTest.Desktop/Testing/ConfiguredStationActionPort.cs`、`RealTestWorkbenchService.cs`、TEST 步骤类型与映射：夹具1/2 各自作为真实步骤/目标，复用 TEST 授权与运行事件；不能因为夹具正在编辑而自动发送 USB。
6. 手动台架和原生 WPF Inspector：两夹具按钮及状态；取消废弃供电/旧 SOL 的可操作入口；状态未知、未支持、固件不匹配时清楚禁用。
7. 对应测试：编码字节及帧解析、独立 00/01/10/11、单一路改变另一路保持、旧固件阻断、超时/断线/重连/UNKNOWN、UI 真实服务接线及 TEST 文件保存/重开。

以上是**排查和改动责任边界，不是授权全量覆盖每个文件**；现有实现必须先阅读并定点修正，不动 Flash Lab/KEY_SIM 固件/冻结 HTML/其他专项私有代码。

## 严格实施顺序（不得跳步）

A. 锁定 HOST 与 Main_Control 实际 HEAD，核对源代码/文档/实体固件标识；确认上位机实际入口为 `AutoTest.Desktop` 而非无后端 UiPreview。

B. 先完成不影响 USB 连接的**模型/Codec/Inspector/TEST 保存和能力阻断**。此阶段夹具操作 UI 必须显示“固件能力未确认，禁止实体操作”，不可发 0x18。

C. 固件专业完成目标接口实现、能力/版本判定与 Keil 编译；给出固件 SHA、HEX、实际板卡身份。HOST 与固件共同完成协议联调；如果双方接口字段不同，停止并走定点合同修订。

D. 在得到**实际现场授权和安全前提**后，测试 USB 发现→Open→端点→F0 PING→F1 VERSION，CAN0/1 双通道；随后依次测试夹具 00、01、10、11、单夹具变化、IGN、重连、故障/断连与恢复；记录请求/ACK/GPIO/J1/机械结果不同层级证据。

E. 完成真实 Windows WPF 工作链：手动夹具1/2 → TEST 连续步骤 → 公共流程调用（只有公共流程执行器已支持后） → 运行证据。未实现的步骤预检阻断，不允许 UI 假 PASS。

## 禁止事项

- 不以 `F1` 固定版本字符串、设备管理器出现 EPB_Host、USB 连接成功或状态 0x00 来推断两夹具已可用。
- 不向旧固件发送新两字节 0x18，不使用旧一字节互斥 0x18 作为所谓兼容降级。
- 不调用旧 ECU_PWR、KEY_PWR、SOL、GET_ALL_PWR，也不暴露 RL_DRV。
- 不自选“上电压紧/释放”“断线释放/保持”“取消时强制归零”的全局安全默认策略。
- 不使用 Mock、旧测试 UI 或绕过安全门禁来证明真实功能。
- 不直接写 main、不合并分支；按专业文件责任提交可审查的准确 SHA，Codex 只按精确机械指令 Build/Test/收集日志。

## 执行 AI 必须交回的内容

- 精确 HOST/固件/配置 SHA；改动文件、接口及不兼容旧协议清单；
- Windows Build/Test 结果和真实 EXE 启动证据，特别是 USB 连接失败阶段；
- 新夹具接口能力检查的结果（未获固件证明时为 BLOCKED）；
- 两路夹具真实测试按 00/01/10/11 的分级证据，未实测时标 UNKNOWN/NOT_VERIFIED；
- 对 G3 已批公共流程/KEY 变更的兼容影响；用户仍未决的具体技术/现场策略。

**交付标准：源码完成、测试构建完成、USB 连接完成、夹具 GPIO 验证、夹具机械验证是五个独立状态。任何一项没有证据，不得标 PASS。**
