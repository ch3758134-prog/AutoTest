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

[历史主控源码固定提交](https://github.com/ch3758134-prog/AutoTest/tree/c830dbcd56b436688437b446d222473a88b85098/Project/source)。例如 U7 P80/PTC8 在原理图中为 SOL9IN，而 `EAL_GPIO.c` 中作为 KEY_PWR_DRV；P81、P82 亦有冲突；`EAL_GPIO.h` 中 IGN=PB19 与 `.c` 表驱动=PC10 不一致。均应保留独立版本事实，不自行修改源码。

设计态缺失：全部 ALT/MUX 表核对、逐器件 Pin-to-Pin 网表、PCB 原生 CAD/装配、线束、现役 HEX 与现场测量。需要改脚时应按 MCU Pad→复用→原理图 Net→外围电路→接口→历史固件逐层审查。

[回到硬件总入口](../../README.md)
