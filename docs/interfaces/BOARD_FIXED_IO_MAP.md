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
| LED_RED1 | HOUT2 | PTB19 | SOL7IN | U5 CH2 | C11 | USB控制 (0x1A/0x1B/0x1C) |
| LED_YELLOW1 | HOUT3 | PTB18 | SOL6IN | U5 CH3 | C25 | USB控制 (0x1A/0x1B/0x1C) |
| LED_GREEN1 | HOUT4 | PTC4 | SOL5IN | U5 CH4 | C26 | USB控制 (0x1A/0x1B/0x1C) |
| LED_RED2 | HOUT5 | PTC10 | SOL11IN | U22 CH3 | C6 | USB控制，旧IGN已迁出 |
| LED_YELLOW2 | HOUT6 | PTC9 | SOL10IN | U22 CH2 | C7 | USB控制 (0x1A/0x1B/0x1C) |
| LED_GREEN2 | HOUT7 | PTC8 | SOL9IN | U22 CH1 | C8 | USB控制 (0x1A/0x1B/0x1C) |

上述 HOUT 属高边输出，均需共用 `SOL24IN`，GPIO HIGH 并不证明输出端子已有负载电压。**已批准并落实的源码初始化：IGN、六路 LED、Air_ALVE1/2、HOUT9/10、U22 CH4 预留控制输入全部 LOW；`RL_DRV` 属内部硬件使能，在上述 IO 均设为 LOW 后由 EAL 初始化 HIGH。** 只有烧录新固件后此行为才生效；复位到 GPIO 初始化前的瞬态仍需硬件检查。现场线束仍需确认从旧 J1 C6 改至 J1 C10。

## 3. 外部 KEY 输入（只定义不启用）

| 宏 | MCU Pad | J1 | 电路 |
|---|---|---|---|
| KEY1 | PTC7 | C17 | R7/R10/C7 分压滤波 → MCU_KEY1 |
| KEY2 | PTC6 | C18 | R8/R11/C8 分压滤波 → MCU_KEY2 |
| KEY3 | PTC5 | C16 | R9/R13/C9 分压滤波 → MCU_KEY3 |

仅给出 `KEY1_PORT/PIN`～`KEY3_PORT/PIN`；不初始化为 GPIO/ADC、不读值、不添加 USB 业务和阈值判定。这三路是主控板外部输入，**不是 KEY_SIM 的 20 路 K1～K20 控制**。

## 4. 未改变的行为与验证边界

- USB 通信驱动与服务、`0x40` CAN 发送及 `0x41` CAN 接收上报的帧格式不变，通道值保持 0/1。
- Air_ALVE1/2 的 GPIO 映射、USB 操作值不变。`RL_DRV` 仅由 EAL 初始化，是硬件电路内部使能，不归 ASW_IoCtrl 管理，更不会跟随 USB 指令状态变化。
- 已完成源码映射变更：**IGN 从 HOUT5/PTC10/J1 C6 迁移为 HOUT1/PTC1/J1 C10**；保留现有 IGN 命令号，上电默认改成 LOW。六路 LED 按 RED1/YELLOW1/GREEN1/RED2/YELLOW2/GREEN2 顺序占用 HOUT2~HOUT7。未据此宣称已完成现场改线或硬件验证。
- 六路 LED 已初始化为 GPIO 输出 LOW；USB CMD 0x1A 单路设置 [index, level]，0x1B 读取位图，0x1C 设置六路位图(0x00~0x3F)。USB index 1..6 与位图 bit0..5 依次对应 RED1、YELLOW1、GREEN1、RED2、YELLOW2、GREEN2；协议编号不变。
- USB OK 只说明业务 GPIO 指令执行，GET 只读 MCU GPIO 电平；不提供 J1 端子电压或灯状态反馈。USB 断开不自动改变 LED 请求，且 USB 不负责 RL_DRV；MCU 复位后 LED 重新初始化 LOW。
- KEY1~KEY3 仍仅声明固定硬件资源，不提供采样逻辑或 USB 命令。
- MCU 引脚、驱动器、连接器依据原理图；实际装配、线束及上电输出仍需实体复核。

## 5. 未分配高边输入与内部硬件初始化（2026-11-18 决定）

- U23 CH2 `HOUT9=PTC3/SOL3IN`、U23 CH1 `HOUT10=PTB0/SOL1IN` 均显式初始化 LOW；不对 USB 暴露控制。
- U22 CH4 `SOL12IN=PTC11` 亦初始化 LOW；该路板外输出网络尚未确认，不预先命名为 HOUT8。
- `RL_DRV=PTC0` 是板级电路内部使能（PTC0→Q4→U2→SOL24IN），不属于上位机业务 IO。`EAL_GPIO_Init()` 先关闭各外部受控高边 GPIO，再独立把 RL_DRV 初始化为 HIGH。`ASW_IoCtrl` 不再维护公共母线请求位图或向 RL_DRV 写电平。
- USB `0x12/0x13、0x18/0x19、0x1A/0x1B/0x1C` 仅控制 IGN、夹具、LED，命令与响应格式不变；不直接或间接控制 RL_DRV，也不增加 RL_DRV 的 USB 指令。
- GPIO 初始 LOW、GPIO 回读和 USB OK 只描述固件动作；由于实际 PCB/线束、RESET 瞬态、输出电压、气阀机械状态尚无实测，不能据此宣称工位安全性已验证。
