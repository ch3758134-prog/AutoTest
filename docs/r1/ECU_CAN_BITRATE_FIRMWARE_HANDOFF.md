# AutoTest R1｜ECU CAN 波特率可变配置：固件接口待实现

**状态：主控固件待实现；R1 HOST 已提供“目标码率选择／未生效拦截”，但不得宣称动态改速已经生效。**

## 1. 已核实的源码事实（2026-11-20）

- ECU CAN：`ECU_CAN_PORT`，MCU FlexCAN1，J1 A12/A1。KEY CAN：`KEY_CAN_PORT`，MCU FlexCAN0，J1 B11/B1。**不得用所谓 CAN1/CAN2 的编号自行交换角色。**
- `Main_Control/Project/source/main.c` 上电使用 `EAL_CAN_Open(ECU_CAN_PORT, EAL_CAN_BAUD_500K)` 与 `EAL_CAN_Open(KEY_CAN_PORT, EAL_CAN_BAUD_500K)`。
- `EAL_CAN.h` 已定义 20/50/100/125/250/500/800/1000 kbit/s 的枚举；`MCAL_CAN_SetBaudrate` 使用已有 `FLEXCAN_SetTimingConfig`。**这不代表 USB HOST 已有设置能力或现场波特率已测量。**
- 已发布的 `Main_Control/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md` 中只有 0x40 CAN_SEND 和 0x41 CAN_RX_IND，**没有**更改或查询波特率的 USB 命令。
- 因此 R1 HOST 的波特率下拉框只是期望值：默认 500k；非 500k 时必须显示「未生效」，禁止后续真实 ECU CAN 写入、自动 TEST 与 FLASH，不会偷偷使用旧 500k 发送。

## 2. 交给主控板固件专项的必要交付

请先评审并确认 USB 协议版本和命令码，以下 **0x42 / 0x43 仅是候选设计，尚未批准或实施**：

| 候选 CMD | 含义 | 请求 PAYLOAD | 成功应答 | 错误 |
|---|---|---|---|---|
| 0x42 | SET_ECU_CAN_BITRATE | 1B 波特率枚举，参照 EAL_CAN_BAUD_*（0..7） | 回显确认的代码（1B） | ERR_FMT、ERR_BUSY、ERR_HW |
| 0x43 | GET_ECU_CAN_BITRATE | 0B | 当前固件实际配置的 ECU CAN 枚举（1B） | ERR_CMD、ERR_HW |

安全前提：

1. **仅 ECU_CAN_PORT 可切换。** KEY_CAN_PORT 的 500k 不得受影响。
2. 固件忙于 CAN 发送、ECU CAN 广播或存在其他未收尾任务时返回 ERR_BUSY；不在 CAN 报文传输中途强制改变硬件时序。
3. 运行时重新配置前核查 FlexCAN Freeze、TX MB 空闲、RX 重启、状态清除与收发恢复的时序，必要时增加空闲等待；不得简单用 ACK 当作实际线速测量。
4. 成功设置后 GET 必须读回当前固件配置；USB 断线、ECU 控制器复位、主控重启后的默认值和是否保持设置需要明确合同。
5. 增加能力探测/版本标识：Host 不能单纯根据固定的 `v0.4-dev` 文本推断新功能可用。
6. 独立实测 125/250/500/1000k（以及批准的其他时序）、标准/扩展帧和收发、CAN RX 证据、超时与断连重连。须用总线分析仪确认真实波特率，不把软件读回当作物理证明。

## 3. R1 HOST 完成固件接入之后的接线要求

- 增加带本次 USB 会话 epoch 的能力探测、写入与读回；只有 GET 成功匹配选择值，才显示「已配置」（这也不等于物理总线已测量）。
- 检查设备运行空闲后才允许 SET；禁止正在执行的 TEST、手动 CAN、KEY、FLASH 中途更改波特率。
- 调整 TEST、手动台架、UDS/FLASH 三类使用 ECU CAN 的链路，确保实际生效波特率与本次测试快照一致；不能忽略失败改用 500k。
- 记录波特率变更请求、ACK、读回、设备 epoch 和失败证据。不可把该设置应用到 KEY_SIM。
- 真正完成固件编译、硬件烧录与现场验证以后，才能开放 500k 以外的真实发送。

## 4. 工程边界

本文件为跨专项交接，**不代表已修改固件**，也不替代主控板 USB 协议正式批准。R1 当前保留真实 USB 500k 能力，其他速率保护性阻止实体操作。扫码配置不影响开发诊断：无 ECU 编号的 TEST 记录明确为 Diagnostic，绝不进入生产计数。
