# KEY_SIM V1.1｜按键板硬件设计档案

**来源：** [4页 EPB_ODD V1.1 原始原理图](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-KEY-EPB_ODD-V1.1.pdf)，SHA256 `2f642b559ddcb4c325e872a67b0d74e0472f0db5bcb9021c1cbb4ff8596a6b2d`。

## 资源索引

- [U3 32个物理脚](pins_u3_32.csv)：MC9S08DZ32，原图 p2；将网络名与芯片封装 PAD 分栏记录。
- [K1～K20 DG411开关和电阻支路](switches_k01_k20.csv)：原图 p3；记录控制脚、DG411 器件/通道、R 元件及图纸标注的输出网络。
- [JP1/JP2 共20触点](connectors_jp1_jp2.csv)：原图 p1；无网名不等于 NC。
- [功能模块](modules.csv) · [待核实差异](review_issues.csv)。

## 电路工作链

1. `JP2 BATIN → D1/F1 → U1 MP2451 → VCC5V`（p4）。
2. `JP2 CANH/CANL ↔ U2 TJA1042T/3 ↔ U3 MSCAN`（p1/p2）。
3. `U3 20个控制信号 → U6/U7/U8/U9/U10 DG411DY模拟开关 → 多支路电阻组合 → RELE_NC、RELE_NO、PARK_NC、PARK_NO、MODE_NO → JP1/JP2`（p3/p1）。

**20 Key 控制的是20个模拟开关；整板形成五路设计输出，不是20路独立模拟电压。** 每路实际电压取决于 BAT_KEY、ECU 输入电路、R、开关电阻与组合状态，不能仅凭 GPIO 掩码假定具体电压。

## 历史源码参考

[Key_Sim@313ed34e](https://github.com/ch3758134-prog/AutoTest/tree/313ed34ecc8a6e01529998787c8c870cc2e4db4a/WS/EPB_EOL/Sources)：`APP/KeySim.c` 接收标准 CAN ID 0x730、8 字节；0x01+20bit mask 设定开关，0x80 释放；`Driver_Gpio.h` 实现 ON=低、OFF=高。**这仅是静态源码，不保证现场已运行。**

## 必须保留的疑点

- p3 K8/R18 和 K13/R25 的右端原图标注均为 `RELE_NC`，虽与邻近支路分组不直观一致，不能自行改名。
- p2 U3 P9（图纸 PTB3_9P、候选实际 Pad PTE2）；P10（PTB4_10P/PTE3）；P12（PTB6_12P/PTE5）须区分“图纸网络名”和“器件物理功能”。历史 KEY19/KEY20 使用 PTB4/PTB6，与封装候选存在差异，不能未审查即改固件。
- 实际 PCB/线束/运行固件/五路模拟电压均 UNKNOWN。

[回到硬件总入口](../../README.md)
