# TestCtr V1.5｜主控板硬件设计参考

**原始设计证据：** [五页 TestCtr V1.5 原理图](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf)，SHA256 `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353`。

## 读法

1. [电路工作原理](THEORY_OF_OPERATION.md)：连续阅读电气工作顺序，理解供电、通信、输出、模拟量和输入调理。
2. [设计事实模型](design.json)：模块、输入/输出、依赖、电气逻辑、共享控制线与不同证据状态。
3. [U7 物理脚](mcu_pins.csv)（100）与 [J1 接点](j1_contacts.csv)（64）：设计端点。未列出网名的引脚不能判定 NC 或可复用。
4. [BTS724G 完整高边输出路径](high_side_paths.csv)（11）：经 MCU / Net / 驱动内部通道 / J1 串联校验。
5. [固定历史固件映射](firmware_bindings.csv)：固定 `Main_Control@c830dbcd56b436688437b446d222473a88b85098`，不得推断为现场运行 HEX。
6. [未决项与证据等级](review.csv)：所有未知、未测量、源码内冲突单独保存。

## 硬件—固件解释边界

- 原理图 Net 名是电气网名，固件业务符号是某个版本的接口名，不能按字符串不一致判电气冲突。
- 例如 PTC10 → SOL11IN → U22 CH3 IN7/OUT14 → HOUT5 → J1 C6；现场 IGN 用途属于用户报告，而非图纸原始命名。
- **J1 C6 不是 MCU PTC6**。PTC6 的图纸网名为 MCU_KEY2。
- U23 CH1/HOUT10 → J1 **B2**；U23 CH2/HOUT9 → J1 **B3**，已与 J1 64 触点索引交叉核对。
- U23 四路 SOLxIN 同时连接 HALL 晶体管电路；RL_DRV 控制的 SOL24IN 是 U5、U22、U23 共用母线。
- 设计态完整引脚、网络片段不等于完整 CAD Netlist；未核实 ALT/MUX、PCB、线束、实际 HEX 和机械负载。

此目录取代此前主控板的零散补充表、重复审查说明和局部 HTML 索引；不得把历史文件的旧判定作为新事实。
