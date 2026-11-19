# HW-02｜主控板 MCU、原理图网络与功能模块关系

**源** [TestCtr V1.5](assets/HW-MAIN-TestCtr-V1.5.pdf)（p1 外部连接；p2 DAC/模拟输出；p3 MCU U7；p4 供电/CAN/USB/电流检测；p5 多路 SOL/HALL/LAMP 驱动）。**状态均为 SCHEMATIC_DESIGNED，未获得 PCB_AS_BUILT。**

## MCU 图纸 Pin 与固件 GPIO 冲突（必须先解决）
| 逻辑 | 固件表驱动位置 | V1.5 图纸 U7 物理脚/网络 | 结论 |
|---|---|---|---|
| RL_DRV | PTC0 | **P70/PTC0 → RL_DRV** | 图纸与源码一致，实物未验 |
| KEY_PWR_DRV | PTC8 | **P80/PTC8 → SOL9IN** | 版本/布线不一致 |
| ECU_PWR_DRV | PTC9 | **P81/PTC9 → SOL10IN** | 版本/布线不一致 |
| IGN | `EAL_GPIO.c` PTC10；`EAL_GPIO.h` PTB19 | **P82/PTC10 → SOL11IN**，**P65/PTB19 → SOL7IN** | 固件内部不一致且与历史图纸有差异 |
| SOL | PTB18 | **P64/PTB18 → SOL6IN** | 现场另已报告电磁阀引脚变动；新连接 UNKNOWN |

`EAL_GPIO_Init` 的五个默认输出值均 `1U`；`EAL_GPIO.c` 的文字说明却称部分 LOW。必须按数组和 MCU 引脚初始化代码解释**代码**，但不能据此推断实物电压/继电器动作。查看 `Main_Control/Project/source/BSW/EAL/GPIO/EAL_GPIO.{h,c}` 与 `ASW/PwrCtrl/ASW_PwrCtrl.c`。

## FlexCAN 物理 PAD 与图纸总线命名
| MCU 外设 | 固件引脚/复用 | V1.5 图纸 p3→p4 | 当前 HOST 角色 |
|---|---|---|---|
| FlexCAN0 | PTA12 TX / PTA13 RX，ALT2，U7 P42/P43 | `TXCAN1/RXCAN1` → p4 `CAN1` 物理收发器网络 | CAN port 0 = KEY_SIM（现场角色事实；具体线束仍未核定） |
| FlexCAN1 | PTC17 TX / PTC16 RX，ALT2，U7 P91/P90 | `TXCAN2/RXCAN2` → p4 `CAN2` 物理收发器网络 | CAN port 1 = DUT（现场角色事实；具体线束仍未核定） |
图纸的“CAN1/CAN2”是设计网络名称，不是固件的 CAN0/CAN1，也不是 HOST 的 port 0/1；不要按字符串编号强制相等。连接器 J1 见 `data/connector_pin_map.csv`。

## 图纸功能模块（所有“可用”仅指设计存在）
- **主控 U7**：MK20DN512VLL10，p3；主要信号的 **63 条明确 U7 引脚→Net** 见 `data/main_mcu_signal_map.csv`。这不是 MCU 全部物理脚，电源、复位、晶振、未列脚仍需补齐完整封装表。
- **外部 J1 CON-64**：p1，多排 SPEED、SOL、CAN、KLINE、KEY、LAMP、HOUT、GND 等；目前仅录入已可明确读取的高风险连接器针位子集。完整 64 位、方向、NC、线束均待 PCB/实物核验。
- **隔离/输出 U14/U15/U17/U20**：p1 `MCU_SOL1..13` 经多通道隔离路径关联 `SOL1..13`；每路是否实际装配、可带何种负载不能仅凭图推定。
- **模拟输出**：p2 MAX5715 与 TLV4172 网络形成 `SPEED1..4`，存在 `SP_REF`、`DAC_1..4`；不等于目前 HOST 已实现四路可校准模拟输出。
- **供电**：p4 USB 端口 P1/隔离 U18、12V 输入/电源 U8/U4、3.3V、继电器及控制管等；实际外部 DUT 主电源按 PM-BIZ BIZ-11 **不归上位机控制**。历史固件的 0x10 命令不转换成产品义务。
- **CAN**：p4 图纸 `CAN1` 的 U9 与 `CAN2` 的 U10，设计层可能存在不同收发/隔离电路；需实物终端电阻、线束针脚及电源域核对。
- **UART/KLINE**：p4 `KTXD/KRXD`、`KLINE`，以及 232 网络，属设计潜在能力，不宣称 HOST 可用。
- **SOL/HALL/LAMP**：p5 多通道驱动器 U22/U5/U23，`HOUT*`、`HALL_5VCTR`、`MCU_LAMP*` 和模拟采样网络；不得把 `SOLnIN` 与 `MCU_SOLn` 误判为同一信号。
- **电流/模拟检测**：p3/p4 `AD_MCU_SOL-current`、`AD_MCU_Motor_current`、`AD_VCC12V`、`DIAG_SOL` 等仅标图纸信号，真实 ADC 初始化/采样链路待专职 AI 检查。

## 对上位机的效力
图纸提供“硬件设计能力候选”；必须同时有 **当前 PCB 连线、对应 MCU 固件实际启用、协议可访问、现场验证** 才能升级为“HOST 可用能力”。GPIO 引脚修改若仍保持 SET_SOL/GET_SOL 的动作值语义及安全约束，HOST 逻辑接口原则不需要变；但物理动作可靠性、上电默认态、读回含义必须重新验证。
