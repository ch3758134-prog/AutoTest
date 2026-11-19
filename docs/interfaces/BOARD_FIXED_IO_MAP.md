# TestCtr V1.5｜固定实体接口与 GPIO 业务映射

**适用范围：** 主控板固件 `Main_Control`，实体接口对应 TestCtr V1.5 原理图。USB 字节协议保持原样。此文档不宣称已在现场烧录或电压实测。

## 1. CAN 双通路（固定，不允许按 PCB 丝印名称推断 MCU 通道）

| 业务角色 | J1 外接口 | MCU 外设和引脚 | 收发器 | USB 0x40/0x41 channel |
|---|---|---|---|---|
| `ECU_CAN_PORT` | A12 CAN_H / A1 CAN_L | CAN1：PTC17 TX、PTC16 RX | U10 TJA1042 | 1 |
| `KEY_CAN_PORT` | B11 CAN_H / B1 CAN_L | CAN0：PTA12 TX、PTA13 RX | U9 ADM3054 | 0 |

C 业务层统一引用 `ECU_CAN_PORT`、`KEY_CAN_PORT` 与 `ECU_CAN_USB_CHANNEL`、`KEY_CAN_USB_CHANNEL`；硬件驱动 `MCAL_CAN_CH0/CH1` 继续代表 MCU 寄存器控制器。**不互换 USB 0/1 值，不修改 USB 驱动、协议、CRC、帧格式。**

## 2. HOUT 与业务名称

| 业务名 | HOUT | MCU Pad | 控制网 | 驱动器 | J1 | 状态 |
|---|---|---|---|---|---|---|
| IGN | HOUT1 | PTC1 | SOL8IN | U5 CH1 | C10 | 现有 0x12/0x13 操作此 GPIO |
| LED1 | HOUT2 | PTB19 | SOL7IN | U5 CH2 | C11 | USB控制 (0x1A/0x1B/0x1C) |
| LED2 | HOUT3 | PTB18 | SOL6IN | U5 CH3 | C25 | USB控制 (0x1A/0x1B/0x1C) |
| LED3 | HOUT4 | PTC4 | SOL5IN | U5 CH4 | C26 | USB控制 (0x1A/0x1B/0x1C) |
| LED4 | HOUT5 | PTC10 | SOL11IN | U22 CH3 | C6 | USB控制，旧IGN已迁出 |
| LED5 | HOUT6 | PTC9 | SOL10IN | U22 CH2 | C7 | USB控制 (0x1A/0x1B/0x1C) |
| LED6 | HOUT7 | PTC8 | SOL9IN | U22 CH1 | C8 | USB控制 (0x1A/0x1B/0x1C) |

上述 HOUT 属高边输出，均需共用 `SOL24IN`，GPIO HIGH 并不证明输出端子已有负载电压。IGN 旧 HIGH 上电态暂予保留，但该初始化会作用于**新端子 J1 C10**。烧录实测前必须检查实体线束是否已经从旧 J1 C6 改至 J1 C10。

## 3. 外部 KEY 输入（只定义不启用）

| 宏 | MCU Pad | J1 | 电路 |
|---|---|---|---|
| KEY1 | PTC7 | C17 | R7/R10/C7 分压滤波 → MCU_KEY1 |
| KEY2 | PTC6 | C18 | R8/R11/C8 分压滤波 → MCU_KEY2 |
| KEY3 | PTC5 | C16 | R9/R13/C9 分压滤波 → MCU_KEY3 |

仅给出 `KEY1_PORT/PIN`～`KEY3_PORT/PIN`；不初始化为 GPIO/ADC、不读值、不添加 USB 业务和阈值判定。这三路是主控板外部输入，**不是 KEY_SIM 的 20 路 K1～K20 控制**。

## 4. 未改变的行为与验证边界

- USB 通信驱动与服务、`0x40` CAN 发送及 `0x41` CAN 接收上报的帧格式不变，通道值保持 0/1。
- 夹具 Air_ALVE1/2、RL_DRV 不变。
- 唯一计划改变的物理输出是 **IGN 从 HOUT5/PTC10/J1 C6 改为 HOUT1/PTC1/J1 C10**；保留现有 IGN 命令号及上电 HIGH 初始化。
- LED1~LED6 已初始化为 GPIO 输出 LOW；USB CMD 0x1A 单路设置 [index, level]，0x1B 空请求读取六路 GPIO 位图，0x1C 单字节设置六路位图(0x00~0x3F)。LED 位图 bit0..5 分别对应 LED1..LED6。
- USB OK 只说明 GPIO 设置完成，GET 只读 MCU GPIO 电平；不提供 J1 输出电压或灯状态反馈。USB 断开不自动改变 LED，MCU 复位后六路重新初始化LOW。
- KEY1~KEY3 仍仅声明固定硬件资源，不提供采样逻辑或 USB 命令。
- MCU 引脚、驱动器、连接器依据原理图；实际装配、线束及上电输出仍需实体复核。
