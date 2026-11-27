# AutoTest 主控板 ↔ Windows 上位机 USB 外部接口说明

> **文档类型：主控板对外通信接口 / 上位机开发交接**  
> **接口修订：2026-11-26（新增实体 KEY1～KEY3 双边沿中断和只读 USB 0x44/0x45；待 Keil 编译与实物验证）**  
> **源码基线：ch3758134-prog/AutoTest，Main_Control ECU CAN 波特率配置候选**  
> **约束：本文描述源码当前行为，不等同于实物验证通过；“目标需求/建议”不得冒充已实现协议。**

## 1. 对外边界与状态

主控板使用 WinUSB Bulk 与 Windows 上位机通信，传输层上传送 0xA5 私有应用协议。**不是 COM 串口，也不直接把 CAN 帧写入 USB 端点。** 当前源码的已知功能与业务范围：

| 项目 | 当前固件源码 | 说明 |
| --- | --- | --- |
| USB PING/版本查询 | 已实现 | 上电设备识别仍需实测 |
| KEY CAN / ECU CAN 透传 | 已实现，两路启动均为500kbit/s | KEY CAN=J1 B11/B1（MCU CAN0），固定500k；ECU CAN=J1 A12/A1（MCU CAN1），支持 USB 0x42 调码率/0x43 查询 |
| ECU CAN 周期广播 | 旧功能仍存在 0x50~0x53 | 是否长期保留作为业务功能未另行批准 |
| 实体 KEY1～KEY3 输入 | 0x44 异步事件、0x45 查询 | PTC7/PTC6/PTC5、PORTC 双边沿中断及20ms消抖；仅 MCU GPIO 电平，不是 KEY_SIM K1～K20 |
| IGN/HOUT1/PTC1/J1 C10 | 0x12/0x13设置/查询 | 保留，查询为MCU GPIO电平 |
| LED1~LED6/HOUT2~HOUT7 | 0x1A单路设置、0x1B六路查询、0x1C位图设置 | 新增；GPIO上电默认LOW |
| 夹具1/PTB1、J1 B5 | 0x18/0x19，独立控制 | 1=压紧，0=释放 |
| 夹具2/PTC2、J1 B4 | 0x18/0x19，独立控制 | 1=压紧，0=释放 |
| ECU/按键板供电控制 | 已从USB命令和业务代码删除 | 不对上位机提供接口 |
| 原SOL控制 | 已从USB命令和业务代码删除 | 不对上位机提供接口 |
| RL_DRV | 板级初始化 HIGH | 内部高边供电电路使能；ASW/USB 不控制、不读写该 GPIO |

**状态边界：** 源码已修改，不等于Keil编译、USB通信、J1实际电压或夹具机械到位验证通过。固件若未刷入本版，上位机必须按设备实际协议版本处理。

## 2. WinUSB 设备发现与管道

以下值来自 Project/source/USB/USB_App/usb_descriptor.c、usb_descriptor.h、Project/source/BSW/EAL/USB/EAL_USB.c：

| 字段 | 实际值 |
| --- | --- |
| VID | 0x1183 |
| PID | 0x5850 |
| USB 产品字符串 | EPB_Host |
| bcdDevice | 0x0003（现源码为调试枚举缓存修改过） |
| 接口号、接口类 | 0 / vendor-specific 0xFF |
| 传输接口 | WinUSB，Bulk；不是 CDC 虚拟串口 |
| 设备接口 GUID | {4da6d065-1e3e-47e6-8cc3-c6977351defb} |
| Bulk OUT（主机 → 板） | 0x03 |
| Bulk IN（板 → 主机） | 0x82 |
| 端点最大包长 | 64 bytes |
| WCID | MS-OS 1.0；字符串索引 0xEE；WINUSB 兼容 ID / 扩展属性 DeviceInterfaceGUID |

**Windows 连接顺序：**
1. 使用上述设备接口 GUID 枚举设备接口路径；VID/PID 和产品字符串用于交叉检查，不要把产品字符串当作设备唯一标识。描述符目前不提供 USB 序列号。
2. 通过 Windows 设备接口路径打开设备句柄（CreateFile），然后 WinUsb_Initialize。
3. 使用 WinUsb_QueryInterfaceSettings、WinUsb_QueryPipe 校验接口0中的 Bulk OUT=0x03、Bulk IN=0x82。优先使用实际查询结果，不自行想象另外的管道。
4. 启动后台持续读取 Bulk IN 并执行**字节流**解帧；写请求到 Bulk OUT。不要以“一次 ReadPipe == 一帧”或“一次 USB 包 == 一帧”为前提。
5. 使用 PING 验证应用层，再 GET_VERSION；读写均应有超时、断线重连、单条请求 SEQ 关联。
6. 枚举失败应分别检查设备识别、WinUSB 驱动绑定、GUID 枚举、打开句柄、管道读写、PING 响应；“设备管理器看到 EPB_Host”不等于应用层已通信。

注意：WCID 免驱枚举代码是固件现有设计，不保证所有 Windows 机器的驱动绑定都成功。具体是否需人工安装驱动，以现场实际状态为准。

## 3. 0xA5 应用帧格式（当前已实现）

### 3.1 上位机请求帧

| 偏移 | 字段 | 长度 | 含义 |
| --- | --- | --- | --- |
| 0 | SOF | 1 | 固定 0xA5 |
| 1~2 | SEQ | 2 | uint16，小端（低字节在前） |
| 3 | CMD | 1 | 命令码 |
| 4 | LEN | 1 | PAYLOAD 长度 0~255 |
| 5~(4+LEN) | PAYLOAD | LEN | 命令参数 |
| 5+LEN~6+LEN | CRC16 | 2 | 小端 |
| 7+LEN | EOF | 1 | 固定 0x5A |

请求长度 = 8 + LEN 字节。

### 3.2 主控板响应 / 主动上报帧

| 偏移 | 字段 | 长度 | 含义 |
| --- | --- | --- | --- |
| 0 | SOF | 1 | 0xA5 |
| 1~2 | SEQ | 2 | 请求应答回显 SEQ；主动 CAN 上报为 0 |
| 3 | CMD | 1 | 对应命令或主动事件类型 |
| 4 | STATUS | 1 | 结果状态 |
| 5 | LEN | 1 | PAYLOAD 长度 |
| 6~(5+LEN) | PAYLOAD | LEN | 返回数据 |
| 6+LEN~7+LEN | CRC16 | 2 | 小端 |
| 8+LEN | EOF | 1 | 0x5A |

响应长度 = 9 + LEN 字节。

### 3.3 CRC16、SEQ、消息分发

- CRC16/MODBUS：初值 0xFFFF、反射多项式 0xA001、无额外终值异或。
- **CRC 计算范围从 SEQ 开始，到 PAYLOAD 结束**；不包括 SOF、CRC 自身和 EOF。CRC 两字节先低后高。
- 上位机请求使用非零 SEQ（建议 1~65535，回绕跳过0）；应答按 SEQ + CMD 关联。**SEQ=0、CMD=0x41 是主控板主动 CAN 接收事件，不参与请求应答配对。**
- 同一 Bulk 读取可能含有半帧、多帧或垃圾字节；必须按 SOF→LEN→总长→EOF→CRC 增量解析，错误后继续寻找下一个 SOF。
- 固件发送的完整响应帧如恰好为64字节的整数倍，EAL_USB 会补一个 0x00 以制造 short packet；该 0x00 **不属于应用帧**，接收解析器必须能跳过。
- 主机应先启用连续读取，再发送请求；不可只在发送完某条命令后读取一次，因为主动 0x41 可能与请求响应交错到达。

**状态码：** 0x00 OK；0x02 ERR_FMT（参数/格式错误）；0x03 ERR_CMD（不支持命令）；0x05 ERR_BUSY（CAN 繁忙）；0x06 ERR_HW（固件报硬件/操作错误）。此外 USB 无响应、CRC 异常、超时是**上位机检测的通信失败**，不可伪装成设备返回某一 STATUS。

## 4. 系统与 CAN 命令（当前源码）

| CMD | 方向 | 请求 PAYLOAD | 应答 PAYLOAD | 说明 |
| --- | --- | --- | --- | --- |
| 0xF0 PING | 主机→板 | 空 | ASCII "PONG"（4字节） | STATUS=0x00 |
| 0xF1 GET_VERSION | 主机→板 | 空 | ASCII "EPB_TEST_CTRL v0.4-dev" | 固件当前常量，并非 Keil 构建自动版本 |
| 0x40 CAN_SEND | 主机→板 | 固定15字节，见下 | 空 | OK 表示驱动接受发送请求，**不代表 DUT 已收到/应答** |
| 0x41 CAN_RX_IND | 板→主机（主动） | 无请求 | 固定15字节，见下 | SEQ=0；可随时与普通应答交错 |
| 0x42 SET_ECU_CAN_BAUD | 主机→板 | 1字节 baud_code，0..7 | 空 | 仅 ECU CAN 运行时切换；忙时拒绝 |
| 0x43 GET_ECU_CAN_BAUD | 主机→板 | 空 | 1字节 baud_code | 查询最近一次成功配置的 ECU CAN 码率 |
| 0x44 KEY_INPUT_EVENT | 板→主机（主动） | 无请求 | 5字节，见下 | SEQ=0，外部实体 KEY 稳定电平变化 |
| 0x45 GET_KEY_INPUTS | 主机→板 | 空 | 3字节，见下 | 三路 KEY 当前消抖电平、事件序号 |
| 0x50 SET_BROADCAST | 主机→板 | 1+16*N 字节，见下 | 空 | 装填 ECU CAN 周期发送表 |
| 0x51 BROADCAST_START | 主机→板 | 空 | 空 | 启动发送表；无帧表时当前实现也可能返回 OK |
| 0x52 BROADCAST_STOP | 主机→板 | 空 | 空 | 停止发送，保留帧表 |
| 0x53 BROADCAST_STATUS | 主机→板 | 空 | 两字节：[running, count] | running 为 0/1 |

### 4.1 CAN_SEND (0x40) 与 CAN_RX_IND (0x41)

两者的15字节 PAYLOAD 布局相同：

| 偏移 | 字段 | 长度 | 约束 |
| --- | --- | --- | --- |
| 0 | channel | 1 | 0=固件 CAN0/按键板；1=固件 CAN1/DUT |
| 1 | ide | 1 | 0=11-bit 标准帧；1=29-bit 扩展帧 |
| 2~5 | id | 4 | uint32 小端；主机应检查11/29-bit合法范围 |
| 6 | dlc | 1 | 0~8 |
| 7~14 | data | 8 | 固定占8字节，不足 DLC 的尾部由上位机补零 |

固件 main.c 初始化两路 CAN 均为 500 kbit/s；**只允许通过 USB 0x42 修改 ECU CAN 的运行时码率，KEY CAN 固定为 500 kbit/s。** 主控板仍按通道透传 CAN，不解析按键板动作、ACK、DBC 或 UDS。USB 的 CAN0/1 是 EAL 软件通道名，不能与原理图 CAN1/CAN2 按数字等同。

当前 CAN_SEND 校验 channel、DLC、长度，但未在 ASW_BusProxy 层完整检查 ide、ID 取值；上位机仍应主动校验。CAN 接收上报为低优先级，队列拥堵时存在丢弃风险，不能把 USB 日志当作无损总线记录。

### 4.2 ECU CAN 波特率设置与查询（0x42 / 0x43）

本指令**固定只控制** `ECU_CAN_PORT`（MK20 FlexCAN1 / J1 A12 CAN_H、A1 CAN_L；在 `0x40/0x41` 中为 `channel=1`），Payload **不包含 channel 字节**，绝不更改 KEY CAN（FlexCAN0 / J1 B11/B1）。

| CMD | 请求 Payload | 成功应答 Payload | 说明 |
|---|---|---|---|
| `0x42 SET_ECU_CAN_BAUD` | `[baud_code]`，1字节 | 空，STATUS=0x00 | 修改 ECU CAN 时序，成功后立即生效 |
| `0x43 GET_ECU_CAN_BAUD` | 空 | `[baud_code]`，1字节 | 返回固件最近一次成功配置的速率，不是总线测速 |

`baud_code` 与现行 `EAL_CAN_BaudType` 枚举完全一致：

| code (hex) | 波特率 |
|---|---|
| `00` | 20 kbit/s |
| `01` | 50 kbit/s |
| `02` | 100 kbit/s |
| `03` | 125 kbit/s |
| `04` | 250 kbit/s |
| `05` | 500 kbit/s |
| `06` | 800 kbit/s |
| `07` | 1 Mbit/s |

**参数及状态：**

- `0x42` 请求长度必须为1，代码范围0..7；`0x43` 必须为空。否则返回 `0x02 ERR_FMT`，不修改波特率。
- 如果 ECU CAN 的 `0x50~0x53` 周期广播仍在运行，返回 `0x05 ERR_BUSY`；上位机需先 `0x52 BROADCAST_STOP`。
- 如果 ECU CAN 硬件 TX 邮箱尚有待发帧，返回 `0x05 ERR_BUSY`，上位机应停止发送并重试；不强制清除/丢弃该 TX 邮箱。
- 读回 CAN 时序寄存器与请求值不匹配时返回 `0x06 ERR_HW`；即使返回 OK，也只证明软件时序参数已写入，**不证明 ECU 与主控板已经通信成功**。
- 相同的 `baud_code` 重复设置是幂等操作，但仍需先停止本固件的周期广播。
- 更改仅在本次主控板运行期间有效；MCU 复位恢复 ECU CAN=500 kbit/s，KEY CAN 始终初始化500 kbit/s。不写 Flash，不修改 USB 帧格式。
- 推荐上位机启动测试前按 DUT 工程配置执行 `0x42`，再用 `0x43` 确认，并在重启/重连后重新读取、按需设置。正在进行 UDS/烧录/周期发送时不应修改码率。
- 底层利用现有 `FLEXCAN_SetTimingConfig` 的 Freeze→寄存器更新→退出流程，不重新初始化整个 CAN 模块；Keil 编译和不同速率的实际通讯/忙状态仍需实测。

### 4.3 实体 KEY1～KEY3 输入事件与查询（0x44 / 0x45）

三路外部自复位按钮仅接主控板输入：KEY1=PTC7/J1 C17，KEY2=PTC6/J1 C18，KEY3=PTC5/J1 C16。与 KEY_SIM 的20个按键模拟通道完全独立；本功能不发送 CAN、不驱动夹具/IGN、不启动测试。

| CMD | 方向 | Payload | 约束 |
|---|---|---|---|
| `0x44 KEY_INPUT_EVENT` | MCU→Host，`SEQ=0` | 5B `[key,level,levels,seq_lo,seq_hi]` | key=1..3；level=0/1；levels 的 bit0..2 分别代表 KEY1..3 消抖稳定电平 |
| `0x45 GET_KEY_INPUTS` | Host→MCU，非零 SEQ | 请求为空，响应3B `[levels,seq_lo,seq_hi]` | 请求长度非0返回 `0x02 ERR_FMT` |

- KEY1～3 设置为 PORTC GPIO 输入、双边沿外部中断；ISR 仅记录变化标志和 MCU 时刻。主循环等待该输入连续稳定20ms后接受变化，并由 `0x44` 上报。
- `sequence` 是主控 MCU 启动以来每次被确认的稳定电平变化计数（16bit，低字节先发，自然回绕）；主机可比较序号间隔发现事件跳跃。USB 异步队列仍可能丢事件，不可将其宣称为无损。
- 刚连接时应请求 `0x45` 获取初始电平并设置会话基线。0x45 返回**消抖稳定电平**，不是外部输入端子的实测电压，也不证明哪种电平代表按下；实际按钮极性在现场验证前未定。
- 每个按下并自动弹起的完整动作，期望产生两次稳定电平变化；按住不反复上报，抖动不产生有效事件。短于消抖时间的脉冲可能被忽略。
- 上电、断连后重新连接的会话只能重新读回当前 GPIO；断连不能解释成按钮松开。旧固件可能对 `0x45` 返回 `0x03 ERR_CMD`，上位机应明确显示不支持，禁止模拟假事件。
- 上报只走 USB 0xA5 帧，不进入 `0x40/0x41` CAN 通道；实际物理输入通路、触点极性与消抖稳定性尚待真实按键实测。

### 4.4 ECU CAN 周期广播（0x50~0x53）

0x50 请求：
- PAYLOAD[0] = count，范围 1~8。
- 后面 count 个帧，每帧固定16字节：ide(1) + id(4 LE) + period_ms(2 LE) + dlc(1) + data(8)。
- period_ms 5~60000；dlc 0~8；所有帧**固定从 ECU CAN（MCU FlexCAN1）**发送。
- 发送新的 0x50 成功时，会先停止旧广播、替换帧表，不自动启动；再发 0x51。
- 0x52 停止但保留帧表；0x53 返回 [running,count]。
- 当 USB 枚举状态丢失时固件会停止广播；主机重连应明确重新检查状态。USB 断连检测是按枚举状态，不等价于应用层心跳。
- 本组是当前源码已有功能，是否作为目标主控板业务基线的必需交付项，待需求审查确认。

## 5. GPIO 控制命令（本次已修订源码）

### 5.1 IGN

| CMD | 请求PAYLOAD | 成功应答PAYLOAD | 实际行为 |
| --- | --- | --- | --- |
| 0x12 SET_IGN | 1B：0关闭/1开启 | 空 | 设置PTC1 GPIO（HOUT1） |
| 0x13 GET_IGN | 空 | 1B：0/1 | 读取PTC1 GPIO电平（HOUT1） |

物理路径 PTC1→SOL8IN→U5 CH1→HOUT1→J1 C10。旧HOUT5现为 LED_RED2。查询仅表示MCU电平，不表示J1实测电压或DUT点火反馈。固件初始化时 IGN 默认为 LOW/OFF（2026-11-18 已批准的源码行为）；显式 SET_IGN=1 才开启对应 GPIO，不改变内部 RL_DRV。

### 5.2 两路独立夹具

| 夹具 | 硬件原理图网名 | MCU | J1 | 0 | 1 |
| --- | --- | --- | --- | --- | --- |
| 夹具1 | Air_ALVE1 / U23 CH4 | PTB1 | B5 | 释放（低电平） | 压紧（高电平） |
| 夹具2 | Air_ALVE2 / U23 CH3 | PTC2 | B4 | 释放（低电平） | 压紧（高电平） |

**设置：CMD=0x18 SET_CLAMP**

- 请求PAYLOAD **必须为2字节**：[clamp, state]。
- clamp=1控制夹具1；clamp=2控制夹具2；state=0请求释放，state=1请求压紧。
- 例如[1,1]只让夹具1压紧；[2,0]只让夹具2释放，另一路不改变。
- 缺少字节、非法夹具号、非法状态值，返回STATUS=0x02 ERR_FMT。
- 命令成功时返回STATUS=0x00、空PAYLOAD。只能说明MCU发出了GPIO请求，不保证气压、负载供电或夹具实际到位。

**查询：CMD=0x19 GET_CLAMPS**

- 请求PAYLOAD为空。
- 返回1字节位图：bit0=夹具1 MCU GPIO电平；bit1=夹具2 MCU GPIO电平，其它位为0。
- 两只夹具可以存在00、01、10、11四种独立组合；不做二选一、互锁或自动切换。

**当前上电/断连源码行为：** 两路夹具初始化LOW（请求释放）；若此前已收到压紧请求，USB枚举丢失时固件会将两路输出置LOW。这是保留上一版逻辑，不是新的业务安全策略。**该断连释放行为仍须现场检查，尤其注意夹具突然释放的风险。** 不能把USB枚举状态当成应用层心跳。

### 5.3 LED1~LED6（HOUT2~HOUT7）

| CMD | 请求PAYLOAD | 成功应答PAYLOAD | 说明 |
|---|---|---|---|
| 0x1A SET_LED | 2B: [index=1..6, level=0/1] | 空 | 单路GPIO设置，不影响其他五路 |
| 0x1B GET_LEDS | 空 | 1B: bit0..5 = LED1..LED6 MCU GPIO电平 | bit6/7=0，不是负载端电压反馈 |
| 0x1C SET_LEDS | 1B: [bitmap=0x00..0x3F] | 空 | 一次要求六路状态，按 GPIO 依次写入 |

物理对应：LED_RED1=HOUT2/PTB19/J1 C11；LED_YELLOW1=HOUT3/PTB18/J1 C25；LED_GREEN1=HOUT4/PTC4/J1 C26；LED_RED2=HOUT5/PTC10/J1 C6；LED_YELLOW2=HOUT6/PTC9/J1 C7；LED_GREEN2=HOUT7/PTC8/J1 C8。

| USB index | 位图 bit | 业务宏 | HOUT |
|---|---|---|---|
| 1 | 0 | LED_RED1 | HOUT2 |
| 2 | 1 | LED_YELLOW1 | HOUT3 |
| 3 | 2 | LED_GREEN1 | HOUT4 |
| 4 | 3 | LED_RED2 | HOUT5 |
| 5 | 4 | LED_YELLOW2 | HOUT6 |
| 6 | 5 | LED_GREEN2 | HOUT7 |

以上只是 GPIO 语义命名变化；原有 USB 0x1A/0x1B/0x1C 的 index、bit 位次及帧格式不变。

- 上电六路初始化为 LOW；HIGH 请求高边输出导通，依赖 SOL24IN，共用高边电源。GPIO HIGH 与 USB OK 不等于 J1 端子已输出电压、更不等于 LED 实际点亮。
- 不合法 index、level、长度以及 bitmap 的 bit6/bit7 非零，返回 ERR_FMT，不执行 GPIO 操作。
- USB 断连不会自动清零 LED；仍保持最后一次 GPIO 输出请求，MCU 重新初始化时恢复LOW。
- 0x1C 六路逐个写入，不保证严格同时翻转。全部命令继续使用现有 0xA5/SEQ/CRC 协议。


### 5.4 外部业务 IO 初始全关闭、RL_DRV 仅属硬件内部电路

- EAL 初始化 IGN/HOUT1、LED1~6/HOUT2~HOUT7、Air_ALVE1/2、HOUT9/HOUT10 及 U22 CH4（SOL12IN）未分配控制输入为 LOW。外部受控输出的上电请求状态都是 OFF。
- 在以上输出均初始化 LOW 之后，EAL 将 **RL_DRV/PTC0 初始化 HIGH**，用于板内 Q4→U2→SOL24IN 公共高边供电链。RL_DRV 不属于对外 IO 业务，不与 USB 任何 SET/GET 请求联动，也不提供 USB 查询或控制。
- USB `0x12/0x13` 仅设置/查询 IGN GPIO；`0x18/0x19` 仅设置/查询两路夹具 GPIO；`0x1A/0x1B/0x1C` 仅设置/查询 LED GPIO。不通过请求位图启停公共母线。
- HOUT9/HOUT10、U22 CH4 未分配控制输入不提供 USB 操作，保持默认 LOW。KEY1~KEY3 是未启用的输入，不得当成受控输出。
- USB 枚举断开时，现有业务仍会撤销已有夹具压紧请求，不自动清零 IGN/LED；**不会改变 RL_DRV**。夹具物理安全性仍待现场核验。
- GPIO 回读只证明 MCU 输入/输出逻辑状态，不等于 SOL24IN 实际电压、J1 高边输出或夹具机械到位。
- 未完成 Keil 编译、烧录及复位瞬态、J1 电压、外部线束/负载的真实测量。

### 5.5 已取消的旧USB指令

旧命令0x10/0x11(ECU供电)、0x14/0x15(按键板供电)、0x16/0x17(旧SOL)和0x1F(GET_ALL_PWR)均不再定义或路由。发送上述命令应得到 **0x03 ERR_CMD**，上位机不应发出这些请求，也不应保留其业务操作。

**内部电路：** RL_DRV/PTC0 控制公共 SOL24IN 高边母线，EAL 在所有外部输出 GPIO 初始 LOW 后独立初始化 RL_DRV 为 HIGH。USB/ASW 不修改 RL_DRV。PTC8/PTC9 分别用于 LED_GREEN2 / LED_YELLOW2，不再是历史供电输出。

## 6. 已核算的收发十六进制示例

以下均为完整0xA5应用层帧，包括CRC和EOF。根据固件CRC16/MODBUS算法生成，**不是现场实测的USB记录**。

| 场景 | 十六进制 |
| --- | --- |
| PING请求，SEQ=1 | A5 01 00 F0 00 45 D8 5A |
| PING响应，SEQ=1 | A5 01 00 F0 00 04 50 4F 4E 47 53 EA 5A |
| GET_VERSION请求，SEQ=2 | A5 02 00 F1 00 44 0C 5A |
| SET_IGN=1，SEQ=3 | A5 03 00 12 01 01 00 55 5A |
| 夹具1压紧：[1,1]，SEQ=5 | A5 05 00 18 02 01 01 67 7E 5A |
| 夹具2压紧：[2,1]，SEQ=6 | A5 06 00 18 02 02 01 67 BD 5A |
| 夹具2释放：[2,0]，SEQ=7 | A5 07 00 18 02 02 00 A7 AC 5A |
| GET_CLAMPS请求，SEQ=8 | A5 08 00 19 00 09 D4 5A |
| ECU CAN发送ID=0x123、DLC=2、DATA=11 22，SEQ=9 | A5 09 00 40 0F 01 00 23 01 00 00 02 11 22 00 00 00 00 00 00 12 5B 5A |
| SET_ECU_CAN_BAUD=250k(code=04)，SEQ=10 | A5 0A 00 42 01 04 1C 46 5A |
| SET_ECU_CAN_BAUD=250k成功应答，SEQ=10 | A5 0A 00 42 00 00 1C 15 5A |
| GET_ECU_CAN_BAUD请求，SEQ=12 | A5 0C 00 43 00 32 44 5A |
| GET_ECU_CAN_BAUD返回250k(code=04)，SEQ=12 | A5 0C 00 43 00 01 04 15 00 5A |


## 7. 推荐上位机调用模型

1. 设备发现：使用WinUSB GUID、VID/PID复核、检查0x03/0x82管道。
2. 字节流解析：后台持续读取，按SOF、LEN、EOF、CRC组合完整帧，容忍拆包/连包/0x00填充。
3. 请求事务：非零SEQ分配与超时，响应按SEQ+CMD配对；0x41/SEQ0走CAN异步事件。
4. CAN服务：0x40发送、0x41收帧；0x42 设置 ECU CAN 波特率、0x43 查询。KEY CAN 固定500k；USB OK不代表CAN对端确认。
5. GPIO服务：IGN使用0x12/0x13；夹具使用0x18/0x19；LED使用0x1A单路、0x1B读回、0x1C整组位图。RL_DRV 严格是 EAL 内部硬件初始化，不受 USB 直接或间接控制。
6. 新旧固件区分：当前 GET_VERSION 在源码中硬编码为 EPB_TEST_CTRL v0.4-dev，并非构建自动版本号或在机固件校验证据。上位机不得仅按VID/PID判断夹具协议版本。

## 8. 已知风险与尚未验证项

- R01：两夹具独立设置逻辑已写入源码；尚无Keil编译/真实夹具J1电压/气路实测证据。
- R02：旧供电与SOL USB命令已移除；PTC8/PTC9分别为 LED_GREEN2 / LED_YELLOW2，外接线束和输出状态仍须核查。
- R03：批准的源码初始值为 Air1/2 LOW、LED1~6 LOW、IGN LOW、HOUT9/10 LOW、U22 CH4 未分配控制输入 LOW；板内 RL_DRV 在最后初始化为 HIGH。上电至 GPIO 初始化完成前的波形和实际负载安全仍待核。
- R04：沿用USB枚举丢失时清除已压紧夹具的既有代码；是否符合实际夹具安全尚未确认。
- R05：GET_CLAMPS/GET_IGN只回读MCU GPIO，非高边输出电压/机械位置反馈。
- R06：USB TX低优先级队列拥堵可能丢CAN_RX_IND，不能视为无损日志。
- R07：USB BUS_RESET只显式清EAL_USB RX缓冲，CDD_USB半帧累积缓存复位处理仍需联调审查。
- R08：CAN_SEND目前未在ASW层完整校验ide/id范围，需Host主动校验。
- R09：WinUSB WCID免驱枚举在Windows各机型上仍需现场确认，识别到EPB_Host不表示PING成功。
- R10：0x42/0x43 ECU CAN 码率指令已写入源码，尚未完成 Keil 编译、125k/250k/500k/1M 现场收发、广播忙拒绝、TX 邮箱忙拒绝、复位回默认速率等验证；SDK Freeze 期间可能短暂停止 CAN 接收。
- R11：外部 KEY1～3 的 0x44/0x45 已实现；按钮触点极性、接线、双边沿中断、电平稳定及实物 USB 上报尚未验证。不得自动绑定开始/停止业务。

## 9. 上位机与固件双方最小联调清单

- [ ] Keil编译并确认实际烧录了本次固件，GET_VERSION返回EPB_TEST_CTRL v0.4-dev。
- [ ] WinUSB按GUID枚举、打开0x03/0x82、PING和CRC响应验证。
- [ ] CAN0/CAN1双通道各发送/接收一帧，检查500kbit/s和通道实际线束。
- [ ] ECU CAN 下发 0x42=03(125k)、04(250k)、05(500k)、07(1M)，0x43 读回一致；验证 KEY CAN 始终 500k。
- [ ] 非法码率/长度返回 ERR_FMT；广播运行中和 TX 邮箱未空时返回 ERR_BUSY，停止/发送完成后可再次设置。
- [ ] MCU 复位后 ECU CAN 0x43 回 05；检查发送、收帧、UDS、波特率切换前后过滤器状态。
- [ ] CAN_RX_IND(SEQ=0)与正常响应交错、USB拆包/连包及64B尾填充。
- [ ] GPIO KEY1/PTC7/J1 C17、KEY2/PTC6/J1 C18、KEY3/PTC5/J1 C16 分别按下、按住、松开，核实0x44事件 key、level、levels、seq。
- [ ] 0x45 在上电/重连后的三路稳定状态查询；多键同时触发、20ms消抖、非法请求ERR_FMT、事件丢失计数及无USB后状态重新同步。
- [ ] 首先确认上电无 USB 指令时外部高边输出请求 GPIO 均为 LOW（含 HOUT9/10、U22 CH4），内部 RL_DRV 初始化为 HIGH；检查 J1 的上电瞬态和实际负载。
- [ ] IGN=0/1 和 GET，核对 J1 C10 真实电平；确认所有 USB IO 命令均不改变 RL_DRV。
- [ ] LED1~LED6 分别设置、读取、整组位图 0x00/0x3F/交替、非法参数ERR_FMT、上电LOW 和 J1 输出实测。
- [ ] 分别控制夹具1/2，测试00、01、10、11四种组合；确认动作一路不会改动另一路。
- [ ] 夹具上电、USB断开/重连、设备复位及RL_DRV供电链实测。
- [ ] 验证旧供电/SOL指令返回ERR_CMD，且不会驱动实体硬件。
- [ ] 验证PTC8/PTC9为LED6/LED5；USB断连保留输出，MCU重启恢复LOW。

## 10. 源码依据（本仓库相对路径）

- USB 枚举及描述符：Project/source/USB/USB_App/usb_descriptor.c、usb_descriptor.h
- WinUSB WCID / 端点收发 / 队列：Project/source/BSW/EAL/USB/EAL_USB.c、EAL_USB.h
- 0xA5 帧、CRC、序号、应答：Project/source/BSW/CDD/USB/CDD_USB.c、CDD_USB.h
- 命令路由：Project/source/ASW/UsbSvc/ASW_UsbSvc.c
- CAN 透传与 ECU CAN 码率控制：Project/source/ASW/BusProxy/ASW_BusProxy.c
- CAN 码率底层：Project/source/BSW/EAL/Can/EAL_CAN.c、Project/source/MCAL/Can/MCAL_CAN.c
- CAN1 广播：Project/source/ASW/EcuEmu/ASW_EcuEmu.c
- IGN、夹具与六路 LED：Project/source/ASW/IoCtrl/ASW_IoCtrl.c
- 真实 GPIO 宏、初始化、PORTC 中断与20ms消抖：Project/source/BSW/EAL/GPIO/EAL_GPIO.h、EAL_GPIO.c
- 固件启动：Project/source/main.c

**变更规则：** 固件若改变 VID/PID/GUID、端点、帧格式、CMD、PAYLOAD、极性或错误码，必须同步更新本文并通知上位机。本文与固件本次修订同步，但未取得Keil编译和真实台架验证证据。
