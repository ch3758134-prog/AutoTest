# TestCtr V1.5｜器件级电气链与 GPIO 业务映射

## 源资料

- TestCtr V1.5 原理图 p1 J1、p3 U7、p5 U5/U22/U23。图纸文件 SHA-256：44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353。
- Main_Control 固定历史源码 c830dbcd56b436688437b446d222473a88b85098；不能推断为在机 HEX。
- 用户报告的外部连接：J1 C6 用于 IGN；仅记为 USER_REPORTED，非测量验收。

## 典型链路：IGN

**U7 P82/PTC10 → SOL11IN → U22 BTS724G IN3 (Pin7) → OUT3 (Pin14) → HOUT5 → J1 C6 → IGN（用户报告）**。

J1 C6 是连接器触点，不是 GPIOC Pin6。U7 PTC6 接 MCU_KEY2。
此前错误将 SOL11IN 与 IGN 字符串不一致判为电气冲突，是因为缺少 U22 器件内部通道映射。该结论撤销。真实源码内部冲突是 EAL_GPIO.h IGN=PTB19 与 EAL_GPIO.c 的实际映射 PTC10 不一致。

## 三颗 BTS724G 的通路

| 器件 | 输入控制网络 | 输出网络/J1 | 状态 |
|---|---|---|---|
| U22 CH1 | PTC8 P80 → SOL9IN → IN Pin3 | OUT Pin18 → HOUT7 → C8 | External load UNKNOWN |
| U22 CH2 | PTC9 P81 → SOL10IN → IN Pin5 | OUT Pin17 → HOUT6 → C7 | External load UNKNOWN |
| U22 CH3 | PTC10 P82 → SOL11IN → IN Pin7 | OUT Pin14 → HOUT5 → C6 | IGN USER_REPORTED |
| U5 CH1 | PTC1 P71 → SOL8IN → IN Pin3 | OUT Pin18 → HOUT1 → C10 | External load UNKNOWN |
| U5 CH2 | PTB19 P65 → SOL7IN → IN Pin5 | OUT Pin17 → HOUT2 → C11 | External load UNKNOWN |
| U5 CH3 | PTB18 P64 → SOL6IN → IN Pin7 | OUT Pin14 → HOUT3 → C25 | External load UNKNOWN |
| U5 CH4 | PTC4 P76 → SOL5IN → IN Pin9 | OUT Pin13 → HOUT4 → C26 | External load UNKNOWN |
| U23 CH1 | PTB0 P53 → SOL1IN → IN Pin3 | OUT Pin18 → HOUT10 → B3 | External load UNKNOWN |
| U23 CH2 | PTC3 P73 → SOL3IN → IN Pin5 | OUT Pin17 → HOUT9 → B2 | External load UNKNOWN |
| U23 CH3 | PTC2 P72 → SOL2IN → IN Pin7 | OUT Pin14 → Air_ALVE2 → B4 | External load UNKNOWN |
| U23 CH4 | PTB1 P54 → SOL4IN → IN Pin9 | OUT Pin13 → Air_ALVE1 → B5 | External load UNKNOWN |

U22 第四通道（IN4 Pin9、OUT4 Pin13）未标示设计网名，不得推断为 HOUT8、NC 或可重用资源。

全部11路详见 main_high_side_11_channels.csv / json；U5/U22/U23 的60个物理脚详见 bts724g_3x20_physical_pins.csv；55条追踪边见 main_high_side_edges.csv。

## 共享供电与软件边界

- U5/U22/U23 Vbb 连接 SOL24IN，经 PTC0/RL_DRV → Q4 → U2 BTS50055 管理；任何一路 Air 关闭不能默认关闭共享 SOL24IN。
- 软件 GPIO 名称是某个版本的业务别名；原理图 Net 名是电气连接。不要按字符串匹配判冲突。
- 读取 GPIO 不构成独立 HOUT 供电、电磁阀位置或气压反馈。器件 ST 为故障指示，且不是逐个负载独立反馈。
- PTC8 → HOUT7/C8，PTC9 → HOUT6/C7，PTB18 → HOUT3/C25 的外部负载用途未经实装证明。
- Air_ALVE1 为 PTB1 → U23 CH4 → J1 B5；Air_ALVE2 为 PTC2 → U23 CH3 → J1 B4；阀的动作方向未知。
- 本次更新仅是硬件设计档案，不修改固件、HOST 或现场设备。

## 修订审查

HW-MAIN-01/02/03 撤销旧名称冲突推断；HW-MAIN-04 的头文件/实现表冲突仍有效；HW-MAIN-05 电气路径补齐但机械用途未核；HW-MAIN-06 上电初值冲突仍待解决。原始判定的更正见 corrected_issue_register.csv 和 interfaces/design_source_issues.csv。
