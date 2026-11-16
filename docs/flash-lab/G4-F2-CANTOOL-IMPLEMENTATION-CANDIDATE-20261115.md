# AutoTest FLASH F2｜参考 CanTool 完整烧录源码候选、验证门禁与差异（2026-11-15）

状态：**F2_SOURCE_IMPLEMENTED_NOT_BUILT / NOT_ECU_VALIDATED**。开发者已收到项目负责人“完整参考 CanTool，先能走完烧录再优化”的实现指令；现场执行危险命令仍需独立一次性任务 SHA256 明确许可。**不宣称真实 ECU 烧录成功。**

分支：feature/flash-lab-20261114。PR #55 Draft，不自动合并 main。

## 1. 业务实现范围（真实设备，不是模拟器）
独立 WPF 项目 src/AutoTest.FlashLab，引用 AutoTest.Devices 和 AutoTest.Specialists，只有一个真实主控 WinUSB 会话，CAN1=DUT，CAN0=KEY_SIM。不创建第二套 CAN/ISO-TP 协议栈；使用既有 HardwareCanReceiveBridge、CanHubIsoTpLink、IsoTpConversation、CanTransmitGate。

CanTool 参考状态机：
1. 导入原 CanTool Configuration.xml（包括中文 FLASH起始地址、FLASH结束地址、波特率、BootFeature/item/APPFeature/item 原样保留），导入 firmware HEX/S19/BIN，镜像 checksum/重叠/范围全部严格验证；显示原始 SHA256。
2. 手工选择原生 x64 Seed/Key DLL，静态 PE 架构与 SHA 检查；先生成冻结 FlashJob，再由现场人员核对设备、目标 ECU、整片擦除地址、固件 SHA、算法库 SHA，手工输入 AUTHORIZE ECU FLASH + SHA256 并在 UI 二次确认。
3. Boot 请求 ID 发送 10 03 探测；无法确认则尝试 APP 10 03 → APP 11 01 → Boot 10 02。APP 和 Boot 的 11/29 bit CAN IDs 各用 XML 所设值，不要混用。
4. Boot SecurityAccess 27 01 取得 Seed → 原生 ZLGKey Cdecl level=0/variant=null 算 Key → 27 02，要求 67 02。**不静默使用 LFSR、演示算法或错误 DLL 兜底。**
5. 31 01 FF00 + FlashStart4 BE + (EndInclusive-Start+1)4 BE，必须得到 71 01 FF00（此 RID 是参考 ECU 特有的，当前目标 ECU 未实证）。
6. 每个按 BlockLength 对齐的镜像块：34 00 44 + addr4 BE + len4 BE，严格按 74 的高半字节解析 maxNumberOfBlockLength；完整 36 UDS PDU <= min(ECU max, ISO-TP 4095)。36 + BSC + data，须收到相同 BSC 的 76；37 必须收到 77；31 01 FF01 addr+len，要求明确 checksum 并与本机 16bit ones-complement 结果比对。
7. Boot 11 01 须有 51 01；等待后 APP 10 03 须有 50 03。**仅报告 REFERENCE_FLOW_COMPLETED_APP_SESSION_OBSERVED_VERSION_UNVERIFIED；不把 APP 回应等同固件版本/身份匹配。**

参考 CanTool 会强制在 FlashEndInclusive-3 写 12 34 56 78。该行为是 OEM 专用、可能覆盖原镜像数据：实现为明确 opt-in 复刻开关，纳入冻结任务哈希，**默认不写**。未经确认不要启用。

失败/取消/USB超时后统一记录 ECU 可能已经发生副作用，禁止自动重擦、自动重写或断点续传。日志对 0x27 Seed/Key 通讯窗口进行原始字节脱敏，仍记录各阶段的请求 SID/NRC/确认与 USB/CAN 执行证据。USB 0x40 ACK 不证明 ECU 收到 CAN；36 的 76 只确认一个序号；FF01 checksum 与 APP 返回分别评定。

## 2. 物理权限独立接线
新增 PhysicalCommandFence.AuthorizeProgrammingJob/PrepareProgrammingCanFrame/TryConsumeProgramming。授权必须精确匹配冻结 Plan SHA、当前 WinUSB generation、APP/Boot CAN1 IDs、最大帧数/40分钟；每一帧提前登记完整15B USB CAN Payload，在 native write 之前消费。**不复用旧物理 TEST RUN 的 CAN ID 白名单；未授权时 0x10/27/31/34/36/37/11 发送全拒绝。** 断连与异常清空。

## 3. 真实主控波特率的源码证据
用户上传 EPB_Test_Keil.zip 的 Project/source/main.c:33-34：EAL_CAN_Open(EAL_CAN0,EAL_CAN_BAUD_500K) 和 EAL_CAN_Open(EAL_CAN1,EAL_CAN_BAUD_500K)；MCAL_CAN.c 的 CAN1 500K 定时预分频存在。该上传源码未显示给 WinUSB 的有效动态切速率命令。因此 F2 计划对 250K **拒绝执行**；真实 F2 连接时先用固件只读 GET_VERSION 与目前基线 EPB_TEST_CTRL v0.3 比较，再要求现场确认真实 CAN1 速率；仍不能从一个版本字符串证明电气速率已测量。

## 4. 已知尚未经过实体证明的 CanTool / ECU 依赖
- 原始 CanTool.rar 在本轮运行环境无法再次取得；根据 Issue #25 的源码调查、归档 CanToolProgrammingProtocol.cs 和此次真实 XML/DLL 静态证据实现。没有原版 WinForms Designer/界面截图，不可声称 UI 1:1 外观还原。
- 用户上传 SafetyFile.dll：托管 .NET、无原生 ZLGKey，**不能直接通过 CanTool 原生接口调用**；sa_demo.dll 有 x64 ZLGKey 导出，但属演示算法，其密钥是否被真实 ECU 认可未知。**必须选择与目标 ECU 匹配的算法 DLL**。
- 31 FF00 擦除约定、31 FF01 checksum 字节序/范围、0x74 数据格式、0x27 level/seed 长度、0x34 下载参数、原版末端签名需求，只有参考代码依据，未由真实 ECU 证明。
- 实体供电/IGN/台架电源稳定、目标 ECU 身份/Bootloader 版本、编程前供电闭环、APP 最终版本 DID 未提供独立实证；不宣称设备已经安全具备现场烧录条件。
- IsoTpConversation 使用现有 classic CAN normal addressing；若目标 ECU 要求固定 DLC 填充、扩展寻址或不同 FC STmin，需要以原始 CAN 证据修订，不猜。

## 5. Windows Codex 验证（**尚未在本次 F2 源码上运行**）
项目负责人自己指定本地工作目录。Codex 只按精确 HEAD 检出源码，执行：
1. dotnet build src/AutoTest.FlashLab/AutoTest.FlashLab.csproj -c Debug
2. dotnet test tests/AutoTest.FlashLab.Tests/AutoTest.FlashLab.Tests.csproj -c Debug
3. dotnet test tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj -c Debug
4. dotnet build src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug
5. dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Debug
6. **实际启动 FlashLab EXE，确认窗口出现且不会因 XAML Binding 崩溃**；仅做 XML/DLL/镜像离线导入、冻结任务和授权显示检查，不连 ECU、不执行任何 F2 实体发送。
也可调用 tools/flash-lab/verify-windows.ps1 -AllHostTests。发生编译/启动错误只交原始日志，不许 Codex 自行修复、不得启动 CI 或改 main。

## 6. 后续现场验证（与以上离线验证不同）
首次应先确认主控唯一独占、真实 Version/PING/CAN1 500K、实际 ECU TX/RX 是否可通信；在负责人与台架操作人员批准、镜像来源/ECU 身份/DLL 算法匹配及安全供电都确认后，才以本次冻结任务 SHA 独立许可执行。逐阶段保存原始证据；发生 NRC、超时、断连立即停下收集日志，不自动重试实体擦写。

**验收语义：源码提交 ≠ Windows Build PASS ≠ 真实 ECU烧录成功。**
