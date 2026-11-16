# HW-01｜资产、证据级别及待确认事项
`RESEARCH_ARCHIVED`，未签发任何现役硬件构型。

## 原件追溯
| ID | 来源 | 精确标识 | 本轮事实 |
|---|---|---|---|
| MAIN-SCH | `Project_Management/SAEF/engineering/assets/hardware/HW-MAIN-TestCtr-V1.5.pdf` | Git Blob `67fe395...`；SHA256 `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353` | 5 页，Altium 输出，图上标日期 2021/1/11 |
| KEY-SCH | `Project_Management/SAEF/engineering/assets/hardware/HW-KEY-EPB_ODD-V1.1.pdf` | Git Blob `ee00af0...`；SHA256 `2f642b559ddcb4c325e872a67b0d74e0472f0db5bcb9021c1cbb4ff8596a6b2d` | 4 页，Altium 输出，图上标日期 2025/7/11 |
| MAIN-FW | `Main_Control@c830dbcd56b436688437b446d222473a88b85098` | `Project/source/...` | EAL/MCAL、USB、CAN、PwrCtrl 等源代码 |
| KEY-FW | `Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a` | `WS/EPB_EOL/Sources/...` | 0x730 KeyMask、GPIO 20 Key 与 CAN RX 处理 |
| HIST-ZIP | 用户历史附件 | EPB_Test_Keil.zip / KEY_SIM.zip | 不能默认与 Git 分支/现役烧录镜像同版本 |

## 重要未闭合项
- `D-MAIN-GPIO-01`：固件 PTC8/PTC9/PTC10/PTB18 与 V1.5 图纸的 SOL9IN/10IN/11IN/6IN 网络不同；头文件 IGN PTB19 对应图纸 SOL7IN。不能自动认定图纸错或源代码错，需实物 PCB Rev 和线束核对。
- `D-INIT-01`：`EAL_GPIO.c` 初始化注释写部分输出 LOW，但数组全部 `1U`；需验证上电/复位/USB 断连默认电平及负载，谨防非预期动作。
- `D-CAN-01`：图纸称 `TXCAN1/RXCAN1` 的芯片 PAD 属 FlexCAN0，`TXCAN2/RXCAN2` 属 FlexCAN1；HOST port 0/1 不按图纸标签猜。
- `D-KEY-PIN-01`：按键板 V1.1 K16 为 `PTB3/P9`，分支 GPIO 宏为 `PTE2`；K1、K3~K8 的图纸采用 `ADP*` 焊盘别名，源码为 `PTA*`，必须按芯片完整引脚复用表核验，不能仅比名字断定物理一致。
- `D-KEY-PROTO-01`：`Main_Control/CDD_KeySim.c` 历史辅助发送 `0x100` 动作枚举；`Key_Sim/KeySim.c` 和 HOST KeypadCommandCodec 用 `0x730` + 20bit Mask。主控 `ASW_BusProxy` 是原始透传，不应把旧 CDD_KeySim 直接当现役按键链路。
- `D-PCB-01`：没有原生 PCB、Gerber、BOM、PCB netlist/修订贴纸、线束针位表，不得给 CSV 中空字段填“已核实”。
- `D-RELEASE-01`：缺当前主控/Key MCU 实际写入的 HEX/CRC、固件构建参数及站点记录，不能宣称固件源版本正在运行。

## 实物资料最小补录
由专职 AI 在获授权的 HW/FW 专项下登记：板卡丝印/Rev、MCU 完整料号/封装、连接器型号和针位方向、PCB CAD/截图/制板文件、供电与地参考、线束两端针脚/线色/标签、现场 SOL 改线及导通测量、固件 HEX/编译日期/CRC、USB version/实测日志。没有的明确 UNKNOWN，不凭经验补写。

**配置核查的发布组合**：`station_id + MAIN_board_rev + KEY_board_rev + harness_rev + MAIN_FW_SHA/HEX_CRC + KEY_FW_SHA/HEX_CRC + HOST_HEAD + verified_evidence`；任一关键维度变化触发局部影响审查。
