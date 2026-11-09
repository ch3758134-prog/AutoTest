# AutoTest G4 · DEVICE B0/B1/B2 真机 USB/CAN 只读现场规程

状态：**工具与现场步骤准备完成，真实 Windows 编译/设备测量/USB-CAN 物理联机均 NOT_RUN。**

## 0. 不可变边界

- 现场主控固件仅认 `EPB_Test_Keil.zip/Project`；按键板仅认 `KEY_SIM.zip/WS/EPB_EOL`，**不更新两板固件、不发送 Flash/UDS/按键/IGN/阀/供电/CAN Tx 命令**。
- 本次 Console 和 `HardwareBenchController` 完全只读；可发送 `PING F0`、`GET_VERSION F1`、`GET_ECU_PWR 11`、`GET_IGN 13`、`GET_KEYPWR 15`、`GET_SOL 17`、`GET_ALL_PWR 1F`，均为空 payload；其他命令在 WinUSB 写入前被拒绝。
- **USB 断开≠关闭 IGN/ECU/电磁阀/外部供电**。任何物理变化需要人工验收，不得靠 ACK/读回 GPIO 推断实际电压、气路方向或夹具动作。
- B0 电气隔离/测量、线束连接、USB 接线等只由用户执行；Codex 仅在未连接真实设备的 Windows 环境构建/跑 Mock；不让 Codex执行实物输出。
- 工具不自动发 PING，也不自动发 GET_IGN，操作员必须输入具体命令。

## 1. 源码确定的硬件/协议事实

| 项目 | 现役 Project 源码值 | 源码证据 |
|---|---|---|
| Windows USB | WinUSB Vendor Specific；VID `1183` PID `5850`，iSerialNumber=0 | `USB/USB_App/usb_descriptor.c` |
| 类/端点 | 0xFF vendor interface 0；Bulk IN `0x82`, OUT `0x03`，64B | `usb_descriptor.h`, `usb_descriptor.c`, `USB_Config.h` |
| 设备接口 GUID | `{4da6d065-1e3e-47e6-8cc3-c6977351defb}`，MS OS 1.0 扩展属性 | `BSW/EAL/USB/EAL_USB.c` |
| USB 协议 | `A5 [SEQ 2LE] CMD LEN PAYLOAD CRC16LE 5A`; response多 `STATUS`；CRC16-Modbus / poly A001 | `BSW/CDD/USB/CDD_USB.{h,c}` |
| PING / 版本 | `F0` 返回 ASCII `PONG`; `F1` 返回 `EPB_TEST_CTRL v0.3` | `ASW/UsbSvc/ASW_UsbSvc.c` |
| 控制/只读 | SET ECU/IGN/KEYPWR/SOL = `10/12/14/16`; GET = `11/13/15/17`; GET_ALL_PWR=`1F` | `CDD_USB.h`, `ASW/PwrCtrl/ASW_PwrCtrl.c` |
| CAN | `40` 是 **发送**（B0/B1/B2 禁用）；`41` 是 **CAN RX async**，SEQ=0，15B | `ASW/BusProxy/ASW_BusProxy.c` |
| CAN 15B | `{port,ide,id32LE,dlc,data8}`；port 0=CAN0=按键板、1=CAN1=ECU | `ASW/BusProxy/ASW_BusProxy.c`, `main.c` |
| CAN 波特率 | CAN0 和 CAN1 500kbps | `Project/source/main.c` |
| 按键固件 | 仅 CAN 标准帧 ID `0x730`、DLC=8；字节0 `01`+20 bit mask 或 `80`释放；未见只读版本查询 | `KEY_SIM/WS/EPB_EOL/Sources/APP/KeySim.c`, `main.c` |
| USB 满 64B | 主控固件给长度为 64B 整数倍的 IN 帧补 1 个 `00`，HOST 帧扫描器忽略该多余字节 | `BSW/EAL/USB/EAL_USB.c` |

**关键：`EAL_GPIO.c` 的 `s_sigInitLevel` 数组五项全部为 `1U`（RL_DRV、KEY_PWR_DRV、ECU_PWR_DRV、IGN、SOL），被 `EAL_GPIO_Init()` 实际用于 `MCAL_GPIO_Init`。** `main.c` 注释“初始低电平”、`EAL_GPIO.c` 注释“四路电源初始低”与实际数组冲突；**以执行代码为准，不能把 IGN 上电当成 OFF**。

`GET_IGN=1` 表示 MCU 引脚为高、固件 `SET_IGN=1` 意味 IGN ON。主控 GPIO→外部高边驱动→实际线束受外部供电及硬件状态影响，读回引脚不是 Vbb 实测。`GET_ALL_PWR` 只反映 GPIO：bit0 ECU_PWR，bit1 IGN，bit2 KEY_PWR。若自复位后未被任何命令更改，源码推导预期 `GET_IGN=01`、`GET_ALL_PWR=07`。**这是源码推导值，不是已测结果，也不是电气安全保证。** `SOL=1` 固件读回动作码 0（升起）亦不保证气路实际位置。

主控 USB 描述符无序列号，Windows DeviceInstanceId 不是不可伪造的单板身份；插拔同类另一台主控也可能需要重复确认物理铭牌/实例。PING/版本证实协议通路而非主控固件烧录校验或按键板固件版本。

## 2. B0：接线与输出初态（先于任何真机 USB 命令）

1. **物理断开 DUT、ECU IGN 线、按键输出负载、气路阀的致动路径；台架与电源状态由用户确认。** 确认外部 12/24V 母线、USB 与主控板供电方式。禁止把“仅插 USB”认作保证输出无电。
2. 用原理图/线号确认 **USB 主控板**、CAN0=按键支路、CAN1=ECU 支路；特别确认实际连接器标识与固件 `0/1` 不混用（实物可能标称 CAN1/CAN2）。未核对线束不进入 B2。
3. 在 DUT 彻底隔离条件下，用万用表核对上电/复位前后 **MCU IGN 控制、高边驱动输出、ECU IGN 接插件以及供电母线**，同时检查继电器、KEY_PWR、ECU_PWR、SOL。若存在不可控的负载上电/阀动作风险立即断电并 STOP。
4. Windows 设备管理器确认 WinUSB 设备 `VID_1183&PID_5850`，驱动为 `WinUSB`，接口 GUID 正确。不更改扫码枪的 HID/COM 驱动；扫描枪不在本工具可选列表。WinUSB 无法枚举时 **STOP**，先处理驱动/权限，不改固件、不改协议。
5. `devices` → 人工选择该主控 `connect INDEX` → 输入 `B0 ISOLATED`；**该命令只建立 USB 端点，不发送 USB 业务指令**。手工输入 `send 13`、`send 1F`（条件允许可 `send 11`、`send 15`、`send 17`），记录 MCU 读回与万用表电压，明确来源不同。

**B0 停止条件：** 未隔离 DUT/执行器、实测 IGN/负载带电但现场未授权、供电来源不明、接线标识冲突、线束与固件 CAN 映射冲突、设备不以预期 WinUSB 端点出现、读回与实测严重矛盾且无法解释。**绝不发送 `SET_IGN=0`/`SET_SOL` 去消除风险；先物理断电/隔离。**

## 3. B1：最小 USB 联机与断连恢复（B0 合格后）

1. 继续保持 B0 隔离：在 `hw> ` 输入 `devices`，确认精确 InstanceId 后 `connect INDEX` 和 `B0 ISOLATED`。`status` 显示 SelectedInstanceId、`Connected`、Generation、Trace JSONL 路径；无自动发送。
2. `ping` 连续 3 次；每次必须满足：请求 `CMD=F0`、非零 SEQ；接收同 SEQ `CMD=F0`、`STATUS=00`、payload ASCII `PONG`。3 次 SEQ 可不同；CRC16 必须正确。
3. `version` 一次：同一 SEQ 匹配 `F1`、`STATUS=00`，ASCII 逐字节等于 `EPB_TEST_CTRL v0.3`；不符即 STOP 并记录原始响应。可选读 `send 13` / `send 1F` 对照 B0 电气结果。
4. `disconnect`，核对状态和日志尾部；USB 断开不等于输出关闭。重新确认 B0 隔离后 `reconnect`，输入 `B0 ISOLATED`，再 `ping`、`version`。至少 2 次断连/重连以观察驱动、超时、旧 SEQ 拒绝和世代变更。
5. 每轮保存自动生成的独立 `.jsonl`；每条含 UTC HOST 到达时间、单机 Stopwatch tick、Generation、TX/RX 原始十六进制、SEQ、CMD、STATUS、超时/错误。出厂测试结果仍为 **NOT_RUN**。

**帧举例（SEQ 仅示例，真实每次不同）：**

- `PING SEQ=1 TX A50100F00045D85A`；`RX A50100F00004504F4E4753EA5A`（`PONG`）。
- `GET_VERSION SEQ=2 TX A50200F100440C5A`；`RX A50200F100124550425F544553545F4354524C2076302E33D2A35A`。
- `GET_IGN SEQ=3 TX A5030013000D505A`；**若 GPIO 为高** `RX A5030013000101C4FC5A`。
- `GET_ALL_PWR SEQ=4 TX A504001F0009245A`；**若源代码默认 0x07 未变化** `RX A504001F00010746195A`。

**B1 停止条件：** PONG/版本/CRC/SEQ 任何不符、同次会话响应错配、超时或断连错误无法恢复、非预期 GPIO 读回、Trace 创建/写入失败、设备实例身份变化而未复核。不得按“设备已连接”就认定 GPIO/供电安全。

## 4. B2：双路 CAN 被动监听（B0/B1 合格、物理接线确认后）

1. 由用户确认 CAN0/按键板和 CAN1/ECU 的实际线束、终端电阻、500kbit/s、DUT/按键板上电与 IGN/气路安全状态。**B2 不发送 CAN Tx、不触发按键、不给 DUT 烧录。**
2. 已完成 B1 的 `connect` 后，在连接状态保持被动等待 CAN RX：若总线上存在真实报文，主控 USB 将报告 `CMD=41`、`SEQ=0`、`STATUS=00`，15B payload，第一字节 00=KeypadCan，01=DutCan。
3. 用 `status` 查看 `CAN published / ingressDropped / malformed / hubDropped`，并保留 JSONL 中完整 15B 原始 payload + 主机到达 UTC/Stopwatch 时间。下游 CAN Hub 广播角色依据为 `KeypadCan(0)` / `DutCan(1)`；时间质量只能 `HostArrivalOnly`，不能标成 ECU 采样时间。
4. 若某支路没有自发 CAN 报文，**0 帧只记 `NO_TRAFFIC_OBSERVED`，不等于线路失败**。证明双支路需要用户在安全可控条件下提供具有已知 ID 的实际 CAN 流量与外部总线测量记录；B2 工具本身不主动发帧。
5. 用户手动断开 USB 后，旧 CAN 订阅释放；按 B0 重新确认电气隔离再 `reconnect`，后续收到的帧必须属于新 Generation。

**B2 停止条件：** 输出/负载被意外驱动、通道角色反向、帧字段与已知真实总线数据不一致、Host 日志持续丢帧/异常、USB 掉线、CAN 总线异常。暂停现场，不通过尝试发送 `CAN_SEND=40` 解决。

## 5. 本轮证据状态

- `Project` Keil 源码已核对；**不代表主控实际烧录物理版本有可验证 build hash**。GET_VERSION 文本只能对照源码硬编码版本字符串。
- `WS/EPB_EOL` KeySim 源码已核对；**按键板当前固件没有已确认的只读版本身份返回机制**，不能从 MCU PING 或 CAN 0x730 ACK 推断按键板版本。
- WinUSB 真实连接/超时/驱动兼容、GPIO 电压、CAN0/CAN1 物理通路均需用户现场完成 B0/B1/B2 才能进入实测 PASS。
