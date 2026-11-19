# TestCtr V1.5｜电路工作原理

**资料层次：** 原理图 D1~D5（硬件连接）、器件工作机理（带条件的电气推导）、历史固件 S1、用户现场陈述 O1。不存在把“旧程序说 IGN”直接当作原理图设计名称的转换。

## 1. 供电和上电依赖
D4：BAT+ 经输入保护形成 SOL24+，U4 LMR14203 与 U11 分别建立 VCC5V/VCC3P3；U8提供正负12V。PCUSB5V 经 U16 TLP291 参与 U4 SHDN/VCC5V_EN 的使能条件。单独 BAT+ 有电并不能证明 MCU 已启动。

D3/D4/D5：MCU PTC0/RL_DRV 经 Q4 驱动 U2 BTS50055 的低有效 IN；U2 将 BAT+ 接通至 **SOL24IN**，这是 U5/U22/U23 共用的 Vbb 功率电源。各个 Uxx 通道的 ON/OFF 与母线 ON/OFF 是两个控制层次。U2 IS/DIAG_SOL 不等于每一路负载电压。

## 2. MCU、复位与复用
D3：U7 MK20DN512VLL10 的100个封装脚、模拟/数字电源与参考、RESET RC、晶振、JTAG和 M95080 EEPROM 均属于物理资源。复位态不等于软件 GPIO Init 值。100脚的完整 ALT/MUX 尚未校完；Pad 允许 GPIO 并不代表板上有自由可驱动的接口。

## 3. 通信物理链
D3/D4/D1：图纸 CAN1 由 PTA12/13 → U9 ADM3054（逻辑侧 VCC3P3，隔离总线侧 PCUSB5V）→ CAN1_H/L → J1 B11/B1；对应历史 MCAL CH0。图纸 CAN2 由 PTC17/16 → U10 TJA1042（VCC5V/VCC3P3，STB 接 GND）→ J1 A12/A1；对应历史 MCAL CH1。不能把驱动初始化成功解释成总线外部已通。

D4：USB P1/ADuM4160/U7 分属 PCUSB5V/PCGND 与板侧 VCC5V/GND；VBUS 又影响供电使能。U12 TJA1021、U13 MAX3232 分别提供单线收发器和 RS232 电平转换，网络名称不等于完整通信协议栈。

## 4. 功率驱动与信号扇出
D3/D5/D1：U5、U22、U23 每路 BTS724G IN 高有效、OUT 经保护 MOS 与 SOL24IN 连接。没有 Vbb、发生保护关断、没有负载连接时，GPIO HIGH 不代表真实负载动作。ST1/2、ST3/4 是成对状态输出，未证明 MCU 可读。

高边 11 条有网名路径见 [high_side_paths.csv](high_side_paths.csv)。U22 第四通道 IN4/OUT4 图纸未标完整网名，不能臆造 HOUT8。

**IGN 例：** U7 P82/PTC10 → SOL11IN → U22 IN3（Pin7）/OUT3（Pin14）→ HOUT5 → J1 C6。J1 C6 用于 IGN 属用户报告。历史 `EAL_GPIO.c` 的 IGN/PTC10 与设计通路相符；`EAL_GPIO.h` 的 PTB19 与 `.c` 不同，是源码内部冲突。

**共用引脚扇出：** PTB0/SOL1IN → U23 CH1/HOUT10/J1 B2，同时 → Q1/H_SPEED1；PTC3/SOL3IN → U23 CH2/HOUT9/J1 B3，同时 → Q2/H_SPEED2；PTC2/SOL2IN → U23 CH3/Air_ALVE2/J1 B4，同时 → Q7/H_SPEED3；PTB1/SOL4IN → U23 CH4/Air_ALVE1/J1 B5，同时 → Q6/H_SPEED4。软件不能将 Air 与对应 HALL 输出登记为两个独立 MCU 控制资源。

JU1/JU2 TLE6228 是低边输出，导通时向地吸收负载电流，与 BTS724G 高边开关供电逻辑不同。

## 5. 模拟输出、电流采样
D2/D3：U3 MAX5715 的四路 DAC 信号经过 U1 TLV4172 和 SP_REF/反馈网络形成 SPEED1~4；最终输出取决于 DAC 参考、运放供电与反馈电阻，不是 DAC 寄存器值直接等于端子电压。D4：SOL/Motor 分流电阻分别为 20mΩ/1.3mΩ，通过 INA190A2 与 RC 接 ADC，电流计算比例不得混用；真实量程及标定状态待核。

## 6. 外部输入与连接器
D1/D2/D3：KEY1~3 经过100kΩ串联、150kΩ下拉、RC与钳位进入 MCU_KEY1~3（PTC7/6/5），理想无负载分压系数0.6。D1 的外部 SOL1~13 经过 TLP293-4 光耦 LED 和上拉形成 MCU_SOLx 输入：LED 有足够电流时 MCU 侧通常拉低。**外部 SOLx / MCU_SOLx 输入 / SOLxIN 高边控制** 是不同电路。

## 7. 软件接口最低审查项
固定来源 `Main_Control@c830dbcd56b436688437b446d222473a88b85098`；需检查控制极性、供电先决条件、共享扇出、上电态、反馈是否真实可观测、输入/输出网名层次及异常条件。旧 GPIO 五路 initLevel=1U，注释称低，与实际代码不一致。任何 GPIO 业务名称调整须先通过完整电气链，并不意味着可以更改实体台架。

## 8. 当前资料边界
设计图五页已索引；U7 100脚、J1 64触点、高边11条已命名通路已交叉校验。尚无完整 PCB Netlist、布局、实装线束与现役 HEX；其他模块尚未实现全部元件 Pin-to-Pin 网表。需要进一步调查的事项见 [review.csv](review.csv)。
