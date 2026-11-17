# AutoTest 主控板 ↔ Windows 上位机 USB 外部接口说明

> **文档类型：主控板对外通信接口 / 上位机开发交接**  
> **版本：v0.2（固件控制接口已整改，待 Keil 编译和真实台架验证）**  
> **源码基线：ch3758134-prog/AutoTest，Main_Control @ a2ef6912561828adf6a945cad0f102872458d007**  
> **约束：本文描述源码当前行为，不等同于实物验证通过；“目标需求/建议”不得冒充已实现协议。**

## 1. 对外边界与状态

主控板使用 WinUSB Bulk 与 Windows 上位机通信，传输层上传送 0xA5 私有应用协议。**不是 COM 串口，也不直接把 CAN 帧写入 USB 端点。** 当前源码的已知功能与业务范围：

| 项目 | 当前固件源码 | 说明 |
| --- | --- | --- |
| USB PING/版本查询 | 已实现 | 上电设备识别仍需实测 |
| CAN0/CAN1 透传 | 已实现，均初始化为500kbit/s | 软件CAN0通往按键板、CAN1通往DUT |
| CAN1周期广播 | 旧功能仍存在 0x50~0x53 | 是否长期保留作为业务功能未另行批准 |
| IGN/PTC10 | 0x12/0x13设置/查询 | 保留，查询为MCU GPIO电平 |
| 夹具1/PTB1、J1 B5 | 0x18/0x19，独立控制 | 1=压紧，0=释放 |
| 夹具2/PTC2、J1 B4 | 0x18/0x19，独立控制 | 1=压紧，0=释放 |
| ECU/按键板供电控制 | 已从USB命令和业务代码删除 | 不对上位机提供接口 |
| 原SOL控制 | 已从USB命令和业务代码删除 | 不对上位机提供接口 |
| RL_DRV | 仅板级初始化 | PCB内部功率电路控制，上位机不可见 |

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

## 5. GPIO 控制命令（本次已修订源码）

### 5.1 IGN

| CMD | 请求PAYLOAD | 成功应答PAYLOAD | 实际行为 |
| --- | --- | --- | --- |
| 0x12 SET_IGN | 1B：0关闭/1开启 | 空 | 设置PTC10 GPIO |
| 0x13 GET_IGN | 空 | 1B：0/1 | 读取PTC10 GPIO电平 |

物理路径 PTC10→SOL11IN→U22 CH3→HOUT5→J1 C6。查询仅表示MCU电平，不表示J1实测电压或DUT点火反馈。固件初始化时IGN仍为HIGH（保留原代码默认行为，未做安全性批准）。

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

### 5.3 已取消的旧USB指令

旧命令0x10/0x11(ECU供电)、0x14/0x15(按键板供电)、0x16/0x17(旧SOL)和0x1F(GET_ALL_PWR)均不再定义或路由。发送上述命令应得到 **0x03 ERR_CMD**，上位机不应发出这些请求，也不应保留其业务操作。

**内部电路：** RL_DRV/PTC0控制公共SOL24IN供电，当前仅在EAL_GPIO_Init内初始化HIGH；USB不提供单独操作。原PTC8/PTC9历史供电控制引脚不再初始化和驱动，物理状态需按实际线路核查。

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
| CAN1发送ID=0x123、DLC=2、DATA=11 22，SEQ=9 | A5 09 00 40 0F 01 00 23 01 00 00 02 11 22 00 00 00 00 00 00 12 5B 5A |

## 7. 推荐上位机调用模型

1. 设备发现：使用WinUSB GUID、VID/PID复核、检查0x03/0x82管道。
2. 字节流解析：后台持续读取，按SOF、LEN、EOF、CRC组合完整帧，容忍拆包/连包/0x00填充。
3. 请求事务：非零SEQ分配与超时，响应按SEQ+CMD配对；0x41/SEQ0走CAN异步事件。
4. CAN服务：0x40发送、0x41收帧；USB OK不代表CAN对端确认。
5. GPIO服务：IGN使用0x12/0x13；**夹具使用已修订的两字节0x18和空请求0x19**；不提供RL_DRV或旧供电操作。
6. 新旧固件区分：本次GET_VERSION源码字符串改为EPB_TEST_CTRL v0.4-dev。上位机不得仅按VID/PID判断夹具协议版本。

## 8. 已知风险与尚未验证项

- R01：两夹具独立设置逻辑已写入源码；尚无Keil编译/真实夹具J1电压/气路实测证据。
- R02：旧供电与SOL USB命令已移除；旧PTC8/PTC9不再配置，外接线束及引脚复位状态须核查。
- R03：上电GPIO默认是Air1/2 LOW、RL_DRV HIGH、IGN HIGH；上电瞬态与真实负载安全待核。
- R04：沿用USB枚举丢失时清除已压紧夹具的既有代码；是否符合实际夹具安全尚未确认。
- R05：GET_CLAMPS/GET_IGN只回读MCU GPIO，非高边输出电压/机械位置反馈。
- R06：USB TX低优先级队列拥堵可能丢CAN_RX_IND，不能视为无损日志。
- R07：USB BUS_RESET只显式清EAL_USB RX缓冲，CDD_USB半帧累积缓存复位处理仍需联调审查。
- R08：CAN_SEND目前未在ASW层完整校验ide/id范围，需Host主动校验。
- R09：WinUSB WCID免驱枚举在Windows各机型上仍需现场确认，识别到EPB_Host不表示PING成功。

## 9. 上位机与固件双方最小联调清单

- [ ] Keil编译并确认实际烧录了本次固件，GET_VERSION返回EPB_TEST_CTRL v0.4-dev。
- [ ] WinUSB按GUID枚举、打开0x03/0x82、PING和CRC响应验证。
- [ ] CAN0/CAN1双通道各发送/接收一帧，检查500kbit/s和通道实际线束。
- [ ] CAN_RX_IND(SEQ=0)与正常响应交错、USB拆包/连包及64B尾填充。
- [ ] IGN=0/1和GET，核对J1 C6真实电平。
- [ ] 分别控制夹具1/2，测试00、01、10、11四种组合；确认动作一路不会改动另一路。
- [ ] 夹具上电、USB断开/重连、设备复位及RL_DRV供电链实测。
- [ ] 验证旧供电/SOL指令返回ERR_CMD，且不会驱动实体硬件。
- [ ] 验证旧PTC8/PTC9物理输出状态符合现场线束要求。

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

**变更规则：** 固件若改变 VID/PID/GUID、端点、帧格式、CMD、PAYLOAD、极性或错误码，必须同步更新本文并通知上位机。本文与固件本次修订同步，但未取得Keil编译和真实台架验证证据。
