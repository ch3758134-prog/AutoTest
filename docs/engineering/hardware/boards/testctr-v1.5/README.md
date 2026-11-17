# TestCtr V1.5｜主控板硬件设计档案

**来源：** [5页原始原理图](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf)，SHA256 `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353`。本文件仅登记设计态资源，**不等于 PCB/线束或现役固件已验证**。

## 资源索引

- [U7 100个物理脚](pins_u7_100.csv)：MK20DN512VLL10，源图第3页；ALT/MUX 仅关键引脚经过初轮复核，空字段不代表可用。
- [J1 64个设计触点](connector_j1_64.csv)：原理图第1页，含两个 UNLABELED 触点，不自动视为 NC。
- [功能电路目录](modules.csv)：电源、MCU/时钟、USB、双CAN、K-Line/RS232、智能高边驱动、灯驱动、DAC/SPEED、电流检测、HALL 等。
- [跨图纸/历史固件差异](../../interfaces/design_source_issues.csv)。

## 依原理图理解板级工作关系

| 模块 | 元件与信号关系 | 图纸 |
|---|---|---|
| 电源与主控开关 | BAT+、U4 LMR14203、U8 12V、U11 3.3V；U7 P70/PTC0（RL_DRV）→Q4 2N7002→U2 BTS50055→SOL24IN | p2、p4 |
| MCU/复位/时钟 | U7 MK20DN512VLL10，JTAG、晶振、复位、电源与模拟参考 | p3、p5 |
| CAN1（图纸命名） | P42/PTA12 TXCAN1、P43/PTA13 RXCAN1→U9 ADM3054 隔离收发器→CAN1_H/L→J1 B11/B1 | p3、p4、p1 |
| CAN2（图纸命名） | P91/PTC17 TXCAN2、P90/PTC16 RXCAN2→U10 TJA1042T/3→CAN2_H/L→J1 A12/A1 | p3、p4、p1 |
| USB/K-Line/RS232 | U18 ADUM4160、U12 TJA1021、U13 MAX3232 与相应外部通路 | p4 |
| 智能高边驱动 | SOLxIN→U5/U22/U23 BTS724G→HOUTx/Air_ALVE 等设计网络；不得把 `SOLxIN` 与 `MCU_SOLx` 自动视为同一路 | p5 |
| 灯/HALL | MCU_LAMPx→TLE6228；H_SPEEDx→晶体管电路→HALL_5V 等 | p5 |
| 模拟量/电流 | U3 MAX5715→U1 TLV4172→SPEED1..4；INA190A2→ADC；数字与模拟端的电压/量程需单独核实 | p3、p4 |
| J1 外部接口 | A/B/C 三组触点、CAN、SOL、HOUT、SPEED、灯与电源 | p1 |

## 固件参考不是硬件定义

[历史主控源码固定提交](https://github.com/ch3758134-prog/AutoTest/tree/c830dbcd56b436688437b446d222473a88b85098/Project/source)。例如 U7 P80/PTC8（SOL9IN）通过 U22 CH1 输出 HOUT7/J1 C8，对应旧固件的 KEY_PWR_DRV 业务别名，**网络名不同本身不构成冲突**；P81/PTC9 同理通向 HOUT6/J1 C7。P82/PTC10（SOL11IN）经 U22 CH3 输出 HOUT5/J1 C6，用户报告该触点实际用于 IGN。真正的源码内部问题是 `EAL_GPIO.h` 的 IGN=PTB19 与 `.c` 的 PTC10 映射不同。完整数据见 [hardware_software_bindings.csv](hardware_software_bindings.csv)。

设计态缺失：全部 ALT/MUX 表核对、逐器件 Pin-to-Pin 网表、PCB 原生 CAD/装配、线束、现役 HEX 与现场测量。需要改脚时应按 MCU Pad→复用→原理图 Net→外围电路→接口→历史固件逐层审查。

[回到硬件总入口](../../README.md)

## 工作原理与软件要求（2026-11-17）

- [按实际电路解释全板工作逻辑](CIRCUIT_WORKING_PRINCIPLES.md)
- [17个模块的电气机制和固件约束 CSV](circuit_principles_17_modules.csv)
- [GPIO多用途扇出/电源依赖 CSV](cross_module_signal_fanout.csv)

**必须关注的硬件资源交叉：** PTB1/SOL4IN 同时控制 U23 Air_ALVE1 和 Q6/H_SPEED4；PTC2/SOL2IN 同时控制 U23 Air_ALVE2 和 Q7/H_SPEED3。PCUSB5V 同时参与 U4 的5V使能和隔离CAN1 U9的VDD2。外部SOLx经光耦转换成反相的MCU_SOLx。

## U5/U22/U23：器件级高边驱动追踪（2026-11-17）
- [HTML 快速审查页面](high_side_trace_review.html) · [完整通道列表 CSV](main_high_side_11_channels.csv) · [JSON](main_high_side_11_channels.json)
- [U5/U22/U23 全 60 个物理脚](bts724g_3x20_physical_pins.csv) · [55 条网络追踪边](main_high_side_edges.csv)
- [误判撤销与历史源码对照](HARDWARE_TRACE_REVIEW.md) · [7项复核](corrected_issue_register.csv)

PTC10 经 SOL11IN→U22 CH3→HOUT5→J1 C6；用户确认 J1 C6 实际用于 IGN。图纸控制 Net 名称不等于固件业务名称。U22 第4通道未标有 HOUT8，不得推断。

- [硬件到固件 5 路旧业务绑定关系](hardware_software_bindings.csv)
- [主控全模块硬件—固件覆盖矩阵](module_firmware_matrix.csv)
