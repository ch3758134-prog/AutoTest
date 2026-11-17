# TestCtr V1.5｜Air 输出控制固件候审（不允许直接烧录）

审查日期：2026-11-17。源：Main_Control@c830dbcd56b436688437b446d222473a88b85098、TestCtr V1.5 图纸 p1/p3/p5、Infineon BTS724G V1.0 datasheet p3。所有结论是 DESIGN 状态。

## 硬件电气链

| 设计输出 | U7 | 输入网 | U23 BTS724G | 驱动输出 | J1 |
|---|---|---|---|---|---|
| Air_ALVE1 | Pin 54 PTB1 | SOL4IN | Pin 9 IN4 → Pin 13 OUT4 | Air_ALVE1 | B5 |
| Air_ALVE2 | Pin 72 PTC2 | SOL2IN | Pin 7 IN3 → Pin 14 OUT3 | Air_ALVE2 | B4 |

U23 Vbb=SOL24IN，经 RL_DRV(PTC0)→Q4→U2 BTS50055 控制的主供电路径供应。BTS724G 输入高电平使能对应高边通道；U23 Pin 8 ST3/4 是两路共用的故障指示，并非阀位反馈；示意图中没有可以直接证明机械压紧/释放的传感器。典型高边器件内部过流保护不等于任何负载都可安全运行，需要校核实际电磁阀参数、24V电源、续流与共地。

## 现状缺陷

- 原 EAL_GPIO.c 把 KEY_PWR/ECU_PWR/IGN/SOL 映射到 PTC8、PTC9、PTC10、PTB18；这些在 V1.5 原理图分别对应 SOL9IN、SOL10IN、SOL11IN、SOL6IN，并非 Air 两通道。
- 原源代码对五个 GPIO 的 initLevel 全为 1；启动时可能误启高边通道。
- EAL_GPIO.h 的 IGN PB19 与 .c 实际 PC10 不一致。
- SET_SOL(0x16) 的 0/1 升起/下落是历史逻辑命令，不可在未经现场语义确认前直接映射 Air1/Air2。
- GET_SOL 回读 GPIO，不测量压力、机械位置或负载输出电压。

## 此候选的可审查改动

1. EAL_GPIO 只绑定已能从图纸确定的 RL_DRV PTC0、Air1 PTB1、Air2 PTC2，并全部初始化 LOW。其他历史信号枚举保留，但不可直接操作；对应旧 USB 指令统一 ERR_HW，防误动作。
2. CDD_USB 新增 raw Air 命令 0x18 SET / 0x19 GET；SET payload 1 字节：0=双路关闭、1=Air1、2=Air2，3/其他非法。GET 四字节：{requested_selection, Air1 GPIO_READ, Air2 GPIO_READ, RL_DRV GPIO_READ}。这些读数不代表输出电压/阀位。
3. 切换前先清空双路并切断高边供电；按当前图纸连接，在软件允许后可重新供电与选择一路。拒绝同时开启两路。
4. USB 未枚举时，撤销已选择的 Air 输出。尚不能保证“上位机进程挂死但 USB 枚举未掉”的安全关闭。
5. 宏 AIR_LIVE_OUTPUT_AUTHORIZED 固定为 **0**，在机械动作映射、供电、负载与验证批准前，**0x18 请求1或2将返回 ERR_HW，禁止通电**。此文件不能被理解为现场可烧录的发布固件。
6. Keil Target 使用原文件路径，无新增 .c，避免 Keil 工程引用变化。USB/CAN 业务数据帧未修改。

## 裁决前必须确认

- Air_ALVE1/2 在现场各接什么阀？压紧、释放、保持与默认安全状态的机械语义如何定义？
- 真实板卡是否按 V1.5 走线？B4/B5 的线束和端子如何接？SOL24IN 是否只供给预期负载？
- 是否允许双通道同时激活，是否需要 break-before-make、最短关闭时间、动作超时、断连/复位后状态。
- USB 当前 HOST 是否要继续调用 0x16/0x17，还是先测试 raw 0x18/0x19？定义兼容方案以后再调整。
- 旧 IGN、KEY_PWR、ECU_PWR 在实物上的真实接线应单独复原，不得把本次候选当成这些功能的新版本。
- 正式实测需现场复核后解除门禁，且需要上位机/HOST 调用方的审查批准。

## 检查证据

本分支尚未经过真实 Keil Build、烧录、台架实测。本候选是审查基线，不能直接部署到生产台架。
