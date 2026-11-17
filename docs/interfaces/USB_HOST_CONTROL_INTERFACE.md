# AutoTest 主控板 ↔ Windows 上位机 USB 外部接口说明

> **文档类型：主控板对外通信接口 / 上位机开发交接**  
> **版本：v0.1（源码现状核查稿；夹具协议待整改）**  
> **核查基线：ch3758134-prog/AutoTest，Main_Control @ f902aece8f1a5f3d4725ff99b9e739bb47e50d1b**  
> **约束：本文描述源码当前行为，不等同于实物验证通过；“目标需求/建议”不得冒充已实现协议。**

## 1. 对外边界与状态

主控板通过 USB 接收 Windows 上位机发出的命令，提供 USB 设备身份、CAN0/CAN1 双向透传、CAN1 周期广播、IGN 和夹具电磁阀输出控制。**WinUSB 是传输层；0xA5 帧是其上运行的私有应用协议。不是 COM 串口，不直接把 CAN 报文当作 USB 负载写入。**

| 项目 | 已确定的目标业务要求 | 当前源码状态 | 上位机当前使用建议 |
| --- | --- | --- | --- |
| USB 连接、PING、版本 | 保留 | 已实现，待实物回归 | 可接入 |
| CAN0 / CAN1 透传 | 保留；CAN0 通往按键板，CAN1 通往 DUT | 已实现，启动时均设置 500 kbit/s | 可接入并实测 |
| CAN1 周期广播 | 保留与否待主控板需求准出 | 旧固件已实现 0x50~0x53 | 可调查，不视为最终承诺 |
| IGN（PTC10） | 保留，可独立设置 0/1 | 已实现 0x12/0x13 | 可接入，注意仅 GPIO 电平 |
| 夹具1 Air_ALVE1（PTB1 → J1 B5） | 独立控制：1=压紧，0=释放 | 旧 0x18 为二选一控制，**不满足** | **禁止按旧 0x18 开发最终业务** |
| 夹具2 Air_ALVE2（PTC2 → J1 B4） | 独立控制：1=压紧，0=释放 | 同上 | **禁止按旧 0x18 开发最终业务** |
| ECU 供电开关 | 取消业务需求 | 0x10/0x11 仍在；会牵连共用母线 | **不要调用** |
| 按键板供电开关 | 取消业务需求 | 0x14/0x15 仍在 | **不要调用** |
| 原 SOL 电磁阀接口 | 取消业务需求 | 0x16/0x17 返回 ERR_HW | **不要调用** |
| RL_DRV / SOL24IN | **板级内部电路控制，上位机不可见** | 当前错误地与 0x10 ECU_PWR 耦合 | **上位机不设计 RL_DRV 控件/命令** |
| GET_ALL_PWR | 不应作为目标业务能力 | 0x1F 仍含已取消的供电位 | **不要调用** |

硬件输出 J1 高电平和气动动作的 1/0 定义来自项目当前已确认需求；当前固件的 GET 类命令大多只能反映 MCU 输出电平，不能证明外部负载电压或夹具实际到位。

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
| 0xF1 GET_VERSION | 主机→板 | 空 | ASCII "EPB_TEST_CTRL v0.3" | 固件当前常量，并非 Keil 构建自动版本 |
| 0x40 CAN_SEND | 主机→板 | 固定15字节，见下 | 空 | OK 表示驱动接受发送请求，**不代表 DUT 已收到/应答** |
| 0x41 CAN_RX_IND | 板→主机（主动） | 无请求 | 固定15字节，见下 | SEQ=0；可随时与普通应答交错 |
| 0x50 SET_BROADCAST | 主机→板 | 1+16*N 字节，见下 | 空 | 装填 CAN1 周期发送表 |
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

固件 main.c 当前初始化两路 CAN 均为 500 kbit/s；**当前 USB 命令集没有对外 CAN 码率设置命令**。主控板仅按通道转发 CAN，不解析按键板动作、ACK、DBC 或 UDS；这些属于上位机或远端设备的协议。USB 的 CAN0/1 是 **EAL 软件通道名**，与原理图上标为 CAN1/CAN2 的物理网名不能直接按数字等同。

当前 CAN_SEND 校验 channel、DLC、长度，但未在 ASW_BusProxy 层完整检查 ide、ID 取值；上位机仍应主动校验。CAN 接收上报为低优先级，队列拥堵时存在丢弃风险，不能把 USB 日志当作无损总线记录。

### 4.2 CAN1 周期广播（0x50~0x53）

0x50 请求：
- PAYLOAD[0] = count，范围 1~8。
- 后面 count 个帧，每帧固定16字节：ide(1) + id(4 LE) + period_ms(2 LE) + dlc(1) + data(8)。
- period_ms 5~60000；dlc 0~8；所有帧**固定从 CAN1**发送。
- 发送新的 0x50 成功时，会先停止旧广播、替换帧表，不自动启动；再发 0x51。
- 0x52 停止但保留帧表；0x53 返回 [running,count]。
- 当 USB 枚举状态丢失时固件会停止广播；主机重连应明确重新检查状态。USB 断连检测是按枚举状态，不等价于应用层心跳。
- 本组是当前源码已有功能，是否作为目标主控板业务基线的必需交付项，待需求审查确认。

## 5. GPIO 控制命令：当前实现与已确认目标的差异

### 5.1 IGN（可用，但需要实测校验）

| CMD | 请求 | 成功应答 | 当前实现 |
| --- | --- | --- | --- |
| 0x12 SET_IGN | 1字节：0=OFF、1=ON | STATUS=0x00，空 PAYLOAD | 输出到 PTC10 |
| 0x13 GET_IGN | 空 | 1字节：0/1 | 读取 MCU GPIO 电平 |

物理路径：PTC10 → SOL11IN → U22 CH3 → HOUT5 → J1 C6。GET_IGN **不是** J1 实测电压或 ECU 点火状态反馈。硬件功率母线 RL_DRV 是板级内部功能，不对上位机暴露。

### 5.2 两路夹具（当前源码不可按目标直接使用）

**已确认的目标动作（用户定义）：**

| 业务名称 | MCU GPIO | J1 | 状态 0 | 状态 1 |
| --- | --- | --- | --- | --- |
| 夹具1 / Air_ALVE1 | PTB1 | B5 | 释放（输出低） | 压紧（输出高） |
| 夹具2 / Air_ALVE2 | PTC2 | B4 | 释放（输出低） | 压紧（输出高） |

两路独立，修改一个夹具不应无条件修改另一个；不实现彼此互锁。固件回读只代表 MCU 输出/请求状态，非机械到位证据。

**当前 Main_Control 代码：**
- 0x18 SET_AIR_ALVE 只收1字节：0=两个关、1=关2开1、2=关1开2，不能同时压紧两路，也不能独立保持另一夹具状态。
- 0x19 GET_AIR_ALVE 请求为空，成功应答为1字节：bit0=Air1 MCU GPIO；bit1=Air2 MCU GPIO。
- 当前 0x18 额外依赖 RL_DRV，USB 断开和旧 ECU 供电命令可能改变夹具状态，均未按新需求审定。

**供下一版固件/上位机协商的接口建议（非当前可执行契约，未经批准）：**
- 保留命令号 0x18，改为 **SET_CLAMP**；请求两字节：[clamp=1或2, state=0或1]，只改变所选夹具。
- 保留命令号 0x19，改为 **GET_CLAMPS**；请求空，返回1字节位图：bit0=夹具1，bit1=夹具2。
- 上位机把0/1显示为“释放/压紧”；不自行把查询结果解释为现场机械反馈。
- **固件没有实际按该新格式实现前，上位机不得发送上述两字节 0x18 来操作现场设备。**
- 上电、USB断连、重连后的夹具默认状态尚未批准，不得假设“断开后保持”或“断开后释放”。

### 5.3 已取消但当前仍留在源码中的命令

| CMD | 当前代码实际行为 | 当前上位机要求 |
| --- | --- | --- |
| 0x10 SET_ECU_PWR | 0/1；会操作 ECU_PWR_DRV 和共用 RL_DRV，0 还强制关闭两路 Air | 禁止调用；待固件删除 |
| 0x11 GET_ECU_PWR | 返回 ECU_PWR_DRV GPIO 电平 | 禁止调用 |
| 0x14 SET_KEYPWR | 0/1；控制旧 KEY_PWR_DRV | 禁止调用 |
| 0x15 GET_KEYPWR | 返回 KEY_PWR_DRV GPIO 电平 | 禁止调用 |
| 0x16 SET_SOL | 直接返回 ERR_HW | 禁止调用；旧入口待删除 |
| 0x17 GET_SOL | 直接返回 ERR_HW | 禁止调用；旧入口待删除 |
| 0x1F GET_ALL_PWR | 返回 bit0 ECU_PWR、bit1 IGN、bit2 KEY_PWR | 禁止按目标业务使用 |

**注意：**“取消 ECU/按键板供电控制”不意味着主控板内部驱动/供电硬件可以任意关闭；RL_DRV 属于 PCB 内部管理，不属于 USB 公共业务接口。

## 6. 已核算的收发十六进制示例

以下每行是**完整应用帧**，包含 SOF/CRC/EOF；不是 USB setup packet。CRC 值按固件 Crc16Modbus 算法计算。

~~~
# ① 请求 PING，SEQ=0x0001
A5 01 00 F0 00 45 D8 5A

# PING 成功响应，PAYLOAD = "PONG"
A5 01 00 F0 00 04 50 4F 4E 47 53 EA 5A

# ② 请求 GET_VERSION，SEQ=0x0002
A5 02 00 F1 00 44 0C 5A

# ③ 请求 SET_IGN(1)，SEQ=0x0003
A5 03 00 12 01 01 00 55 5A

# 其成功响应
A5 03 00 12 00 00 C0 05 5A

# ④ 发送 CAN1 标准ID=0x123、DLC=2、Data=[0x11,0x22]，SEQ=0x0004
A5 04 00 40 0F 01 00 23 01 00 00 02 11 22 00 00 00 00 00 00 3C F6 5A

# ⑤ 假设 CAN1 接收到同一报文，则主控板主动上报如下
A5 00 00 41 00 0F 01 00 23 01 00 00 02 11 22 00 00 00 00 00 00 B8 05 5A

# ⑥ BROADCAST_STATUS 当前未运行、帧表为空的响应样例 (SEQ=5)
A5 05 00 53 00 02 00 00 6B CC 5A
~~~

示例⑤和⑥是**格式/状态示例**，不是对现场设备实际 CAN 接收和广播状态的证明。

## 7. 推荐上位机调用模型

1. **设备发现层**：根据固定接口 GUID 枚举路径、打开 WinUSB、识别 IN/OUT 两条 Bulk 管道。
2. **收发层**：单一后台读取管道；发送队列与超时；逐字节增量解析响应帧（不是按 64B 切帧）。
3. **事务层**：给每条上位机主动请求分配非零 SEQ，按 SEQ+CMD 匹配；0x41 走独立异步 CAN 事件通道。
4. **CAN 服务**：对外提供 CAN0、CAN1 发送与接收；不要以 CAN_SEND 的 USB OK 当作 ECU 应答。
5. **控制服务**：IGN 可以用已实现的 0x12/0x13；夹具1/2 等待修订协议后再启用；不要为 RL_DRV、已取消的供电、原 SOL 创建上位机业务接口。
6. **观测与错误**：分别报告 USB物理连接、设备枚举、WinUSB打开、PING/帧校验、命令超时、STATUS、CAN总线与夹具硬件动作，避免混称“连接失败”。

## 8. 源码审查发现的风险（需在固件整改/实测闭环）

- R01 / **夹具语义错误**：0x18 两路互斥；与已确认的独立控制冲突。
- R02 / **已取消功能仍能驱动真实 GPIO**：0x10、0x14 仍可执行，0x10 还影响 RL_DRV。
- R03 / **上电 GPIO 初态待确认**：EAL_GPIO_Init 当前 Air1/2=LOW，RL_DRV、KEY_PWR_DRV、ECU_PWR_DRV、IGN=HIGH；不应将历史状态视为已验证安全值。
- R04 / **USB断线策略未批准**：ASW_PwrCtrl_Task 在 Air 请求非零且 USB enum 不就绪时调用 AirOff；机械动作和恢复策略未定。
- R05 / **缺少物理夹具到位反馈**：GET_AIR_ALVE 和 GET_IGN 读取的是 MCU GPIO，并非 J1/负载/机械传感器。
- R06 / **USB/CAN 可靠性需实测**：USB IN 低优先级队列满可丢 CAN_RX_IND；USB OUT 接收区256B，应用帧累积长度受限；不能保证零丢帧。上位机应计数、记录并识别丢帧可能性。
- R07 / **断线后半帧处理需测试**：EAL_USB 的接收区在 bus reset 清空；CDD_USB 的累积缓冲只在启动调用 CDD_USB_Init，代码未见 USB bus reset 时显式清空累积半帧。
- R08 / **部分命令的参数防御有限**：CAN_SEND 未做完整的 IDE/ID 验证；有些 handler 对长度与指针的防御不一致；Host 不得因此发送任意畸形帧。
- R09 / **协议版本识别不足**：GET_VERSION 固定文本不能自动判定夹具协议能力；在夹具新协议发布时需要明确固件/主机兼容门槛，不能仅用 VID/PID 认定功能正确。
- R10 / **WinUSB 免驱需验证**：固件有 WCID 1.0 和 GUID 路径，但 Windows 识别到设备不等于自动安装 WinUSB、更不等于 PING 成功。

## 9. 上位机与固件双方最小联调清单

- [ ] Windows 能枚举设备接口 GUID，并核实 VID/PID、0x03/0x82 管道。
- [ ] PING 有应答；SEQ/CMD、CRC、ASCII "PONG" 正确。
- [ ] GET_VERSION 正确并保留原始返回字节。
- [ ] 发送/接收 CAN0、CAN1 各一帧，检查通道映射和 500 kbit/s。
- [ ] 长帧、多帧连发、拆包、CRC 错误、64B 边界和异步0x41 交错场景。
- [ ] IGN ON/OFF 与查询，并确认 J1 输出实际电平。
- [ ] 固件完成夹具协议修订后分别测试夹具1/2的 00、01、10、11 四种组合以及一夹具动作不影响另一夹具。
- [ ] 明确并验证上电、USB断连、重连、设备复位情况下的夹具状态。
- [ ] 排查并移除已废弃供电指令的实际控制路径。

## 10. 源码依据（本仓库相对路径）

- USB 枚举及描述符：Project/source/USB/USB_App/usb_descriptor.c、usb_descriptor.h
- WinUSB WCID / 端点收发 / 队列：Project/source/BSW/EAL/USB/EAL_USB.c、EAL_USB.h
- 0xA5 帧、CRC、序号、应答：Project/source/BSW/CDD/USB/CDD_USB.c、CDD_USB.h
- 命令路由：Project/source/ASW/UsbSvc/ASW_UsbSvc.c
- CAN 透传：Project/source/ASW/BusProxy/ASW_BusProxy.c
- CAN1 广播：Project/source/ASW/EcuEmu/ASW_EcuEmu.c
- IGN、夹具、旧供电：Project/source/ASW/PwrCtrl/ASW_PwrCtrl.c
- 真实 GPIO 宏及初始化：Project/source/BSW/EAL/GPIO/EAL_GPIO.h、EAL_GPIO.c
- 固件启动：Project/source/main.c

**变更规则：** 固件若改变 VID/PID/GUID、端点、帧格式、CMD、PAYLOAD、极性或错误码，必须同步更新本文并通知上位机；未经核查的候选协议不得被写成已实现接口。本次提交仅新增文档，不修改固件执行逻辑。
