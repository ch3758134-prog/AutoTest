# CanTool 独立 ECU Flash 联调工作台

本工作台 **不属于 R1 正式交付 UI**，只用于单个真实 ECU 的 CanTool 协议与 CAN 链路排障。

**唯一替换范围：** CanTool 的物理 CAN 设备访问改为 AutoTest 主控 WinUSB → CAN1 (DUT)。APP/Boot 两组请求和响应 CAN ID 来自真实 `Configuration.xml`；不使用模拟 ECU、模拟 Seed/Key 或模拟烧录结果。

## Windows 开始使用

要求 Windows 11、.NET 10 SDK（工程 `global.json` 指定 10.0.400）、已正确安装的主控 WinUSB 驱动、独占的真实主控和安全供电的目标 ECU。先关闭任何占有同一 WinUSB 主控板的 AutoTest 或诊断窗口。

在现有 AutoTest Git 工作副本执行（建议单独 worktree，避免覆盖本地改动）：

```powershell
git fetch origin feature/cantool-flash-lab-20261110
git worktree add ..\AutoTest_CanTool_Flash_Lab origin/feature/cantool-flash-lab-20261110
cd ..\AutoTest_CanTool_Flash_Lab
dotnet restore tools/AutoTest.FlashDiag/AutoTest.FlashDiag.csproj
dotnet build tools/AutoTest.FlashDiag/AutoTest.FlashDiag.csproj -c Release --no-restore
dotnet run --project tools/AutoTest.FlashDiag/AutoTest.FlashDiag.csproj -c Release --no-build
```

**两阶段实测**：

1. 无破坏通信确认：导入参考 CanTool 的 `Configuration.xml` → 主控 WinUSB 连接 → 点击「实际 CAN1 探测 Boot / APP（仅 10 03）」→ 查看 UDS `50 03` 或超时/NRC 与完整原始 RX 日志。此操作可改变 ECU 诊断会话，但**不**触发擦除、解锁、下载或复位。保存 USB/CAN JSONL 路径及导出 TXT。
2. 仅当真正核实 ECU 型号、外部供电、CAN1 实际比特率、Flash 范围、固件镜像和可用于本 ECU 的原生 Seed/Key DLL，且具备现场恢复措施之后，才尝试真实烧录。必须先生成并人工复核每次授权短语，再二次确认。失败/取消/USB 丢失/ACK 不明均停止；不自动重试，不能当作 ECU 保持原状。

## CanTool 流程

| 阶段 | 请求与验证 |
|---|---|
| Boot / APP 探测 | `10 03` → 校验 `50 03`；先 Boot 再 APP |
| 切入 Boot | APP `11 01`、Boot `10 02` → `50 02` |
| Security | `27 01` → `67 01` Seed；经 **原生** DLL `ZLGKey` → `27 02` → `67 02` |
| Erase | `31 01 FF00` + 32-bit BE 起始地址和长度 → `71 01 FF00` |
| Download | 每个对齐固件块 `34 00 44` → 解析 `74` 允许长度 → `36` 序号确认 → `37` |
| Verify | `31 01 FF01` + 32-bit BE 块范围，比较 ECU 16-bit checksum 与 host complement-sum |
| Reset | `11 01`；**仅确认发送与 ECU 响应，重启后 APP 版本未自动证明** |

**关键警告：** 目前本工具按参考研究实现了原生 `ZLGKey` 调用约定，但原 DLL 与目标 ECU 的 Seed/Key ABI 兼容性尚未通过真实配套验证。样例 `SafetyFile.dll` 为托管 DLL，无法作为原生 `ZLGKey` 导出使用；`sa_demo.dll` 不应作为真实 ECU 默认安全算法。若未确认原生库及调用约定，**不要启动擦除**。运行代码不保证固件恢复能力。

UI 中 XML BaudRate 是配置的期望值，导入 XML **不会**更改主控的 CAN1 物理速率。窗口展示 USB ACK 和实际 ECU UDS 响应，二者不可混淆。

## 取证

主控原始 JSONL：`%LOCALAPPDATA%\AutoTest\CanToolFlashLab\Sessions\`；按钮「导出全部日志…」保存阶段、NRC、地址、确认字节、CAN1 RX 和 USB 底层错误。任何真实写入前后，记录 ECU 当前状态、配置 XML SHA、固件 SHA、设备 Generation 和具体失败阶段。

GitHub Windows CI 仅验证独立 WPF 编译、发布和不接设备的单元测试；**不等于 ECU 实际烧录成功**。
