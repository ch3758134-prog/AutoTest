# TestCtr V1.5｜主控板硬件设计参考

**面向固件工程师的统一入口：**

1. [统一硬件资源快速查询 HTML](resource_lookup.html)：搜索业务符号、MCU Port/Pin、设计 Net、J1 触点，单页查看硬件中间器件、工作原理和固件约束
2. [完整统一模型 design.json](design.json)：`resource_index` 同时包含 100 个 U7 物理引脚、64 个 J1 设计触点和 30 条电路路径。数据可跨端点反向检索，包含设备/网络/源码/未知项
3. [硬件电路工作原理](THEORY_OF_OPERATION.md)：按电路机理连续解释，适合了解新的板级功能
4. [MCU 原始端点索引](mcu_pins.csv)、[J1 原始端点索引](j1_contacts.csv)：仅作为底层可核对的明细，不再是两个彼此孤立的人工查找入口
5. [高边通路源表](high_side_paths.csv)、[历史固件绑定](firmware_bindings.csv)、[未决问题](review.csv)：固定证据链

**例如 IGN：** 从 `IGN`、`PTC10`、`SOL11IN`、`HOUT5` 或 `J1 C6` 可回到同一 U7→U22→J1 通路；J1 C6 的 IGN 用途来自用户报告。**PTC6** 是另一条 `KEY2` 分压输入路径，切勿与 J1 C6 混淆。

**重要的中间电路：** Air_ALVE1/PTB1 同时使用 U23 CH4 与 Q6/H_SPEED4；Air_ALVE2/PTC2 同时使用 U23 CH3 与 Q7/H_SPEED3；隔离 CAN1 的总线侧电源来自 PCUSB5V；外部 SOL1..13 经光耦输入为反相信号，且逐路光耦物理脚号仍待补核。

**原始设计证据：** [TestCtr V1.5 五页原理图](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf)，SHA256 `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353`。历史源码 `Main_Control@c830dbcd56b436688437b446d222473a88b85098`。**没有认定 PCB/线束、实际电压、负载、在机 HEX 已现场验证。**

项目管理审查边界：这是硬件设计档案，不授权固件改写、烧录或现场操作。
