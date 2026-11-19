# AutoTest｜G4 ECU/KEY CAN、主控 LED 与烧录专业工具接线记录

**实施分支**：`feature/g4-html-parity-20261115`  
**对照来源**：`Main_Control/docs/interfaces/BOARD_FIXED_IO_MAP.md`、
`Main_Control/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md`、
`feature/flash-lab-20261114/src/AutoTest.FlashLab`。  
**交付状态**：已提交研发候选，尚未完成 Windows Build、WinUSB 接口验证或本轮台架实测。**禁止把源码检查认定为实物通过。**

## 一、硬件固定事实

| 业务接口 | 主控外端子 | MCU CAN | USB 0x40/0x41 通道 |
| --- | --- | --- | --- |
| ECU CAN | A12 H、A1 L | FlexCAN1 | **1** |
| KEY_SIM CAN | B11 H、B1 L | FlexCAN0 | **0** |

`StationCanWiring` 为唯一业务映射来源。CAN 帧发送、接收、KEY_SIM 物理授权以及 FlashLab APP/Boot 均保持此映射。不允许因原理图上 CAN 网名与 MCU 序号不同，再次倒置 USB 通道。

IGN：物理输出由原 HOUT5 改为 **HOUT1 / PTC1 / J1 C10**；USB 仍为 `0x12 SET_IGN` 与 `0x13 GET_IGN`。上位机没有直接控制 MCU 引脚号码。

## 二、新增六路独立 LED

| LED | HOUT | J1 |
| --- | --- | --- |
| LED1 | HOUT2 | C11 |
| LED2 | HOUT3 | C25 |
| LED3 | HOUT4 | C26 |
| LED4 | HOUT5 | C6 |
| LED5 | HOUT6 | C7 |
| LED6 | HOUT7 | C8 |

- `0x1A`：两字节 `[index=1..6, level=0|1]`，只改变对应 LED。
- `0x1B`：空请求，成功应答 1 字节 GPIO 位图（bit0..bit5）。
- `0x1C`：固件支持六路整体位图，但**本轮手动台架不提供整体写入，也不给自动/手动 CAN 授权打开此命令**。
- `RL_DRV`：板内硬件初始化，不允许通过 USB 业务层控制。
- 新版固件 `GET_VERSION` 自报文本不保证唯一性，故先 `GET_LEDS` 检测功能，合法回读后开放按钮；旧固件 `ERR_CMD` 显示错误，不伪造六路 OFF。
- 每次主控写入经物理命令 Fence 的精准一次性授权；USB 断开、超时或回读异常时显示 `UNKNOWN`。
- `ACK` 及 `GET_LEDS` 只表明 MCU 接收命令及 GPIO 回读，**不是 J1 电压、LED 亮度或负载结果的独立测量**。
- 自动测试执行期间封锁手动 LED，连接不会自动写入任何 LED 状态。

## 三、烧录专业工具

`feature/flash-lab-20261114` 已实测成功的 CanTool 参考程序，原样引入其配置、固件解析、种子密钥、APP/Boot、编程流程、收发监控及证据导出模块。它是 **AutoTest.Desktop 直接引用的 WPF 专业工具库**，不创建另一个自动测试执行器。

- 主 IDE 使用一个 USB 会话；打开 FlashLab 前要求停止手动台架、自动测试和后台发送任务。
- 如果主会话存在，先取得人工确认，再 **await** 断开、释放主控 WinUSB；专业工具随后以独占模式打开同一设备。
- FlashLab 窗口为主界面 Owner 的模态专业工具窗口；关闭后先取消编程并释放 USB/CAN/记录，再返回主 IDE。主 IDE 不自动重连，消除旧授权继承。
- WinUSB Native `CreateFile` 采用 `FILE_SHARE_NONE`，防止同一主控的第二连接。
- FlashJob 保留完整配置/HEX/DLL 的冻结计划指纹、APP/Boot 允许发送 ID、逐 CAN 帧精确物理授权、传输阶段日志和 Seed/Key 脱敏。
- 保留 Flash 专项实测过的 ISO-TP 短 STmin 优化及 APP 会话恢复记录。USB ACK 并非 ECU 诊断肯定应答，烧录成功也不等于做过 APP 内容校验。
- **不接入自动测试步骤**；UDS 工具的自动测试接线、波形参考判定留待各专项审查。

## 四、Codex 本地机械验收顺序

1. 精确拉取本分支 HEAD；`dotnet build AutoTest.sln -c Debug`；所有编译错误原样报回，不自行改项目代码。
2. `dotnet test tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj -c Debug --no-restore`；检查 `LedUsbContractTests`、`PhysicalCan1FlashFenceTests`、CAN 收发角色。
3. 在**不发送 CAN**的前提下连接主控。ECU 插 A12/A1 时检查 RX 应显示 ECU CAN；KEY 插 B11/B1 时显示 KEY CAN。
4. 打开手动台架，先 `读取 LED GPIO`。逐路 LED1~6 测 ON/OFF，检查 `0x1A` 参数、`0x1B` 回读和实体端子电压，断线显示 UNKNOWN。IGN 的端子为 J1 C10。
5. 关闭手动台架及周期任务，点击工具→ECU 烧录工具，确认 USB 移交。先以只读方式检查已有 CanTool 配置，**真实刷写必须现场另外批准**。核查烧录完整日志、退出后的 USB 释放以及重新连接行为。
6. 烧录专项不能通过“只打开窗口”判定为功能验收；本次移植必须使用用户现有实物与参数复核 UDS/Seed/Key/擦写数据。

## 五、限制

本次接入实现代码结构及静态 XML/依赖检查，尚无本地构建、USB/CAN 报文、GPIO 测量或 Flash 复核证据。未获得这些证据前，保持候选状态，不合入主线。
