# AutoTest R1｜ECU CAN 动态波特率：HOST ↔ Main_Control 交接

## 当前状态（2026-11-20）

- **固件源码已实现**：GitHub `Main_Control` 分支，核对 HEAD `e06a820e0d54dc7719cc75af1ecae005f05e164f`，`docs/interfaces/USB_HOST_CONTROL_INTERFACE.md`、`ASW_UsbSvc.c`、`ASW_BusProxy.c` 定义了 ECU CAN 0x42/0x43。
- **HOST R1 源码已接线**：主界面与手动台架均有目标波特率和“应用”操作，实际 USB 读写、读回、当前会话确认、物理命令白名单已实现；独立 FlashLab 在真实 FLASH 授权后，按冻结 XML 码率 SET / GET，原先的 250K 固定拒绝已移除。
- **未验证**：尚未确认新固件 Keil 编译、实际刷入目标主控、Windows HOST 构建、USB 调速实测及总线分析仪测量。代码接线不能当作硬件验收。

## 确定的 USB 协议

使用现有 0xA5 / SEQ / CRC16 USB 会话，与 CAN_SEND 0x40 相同的事务控制，**只改 ECU_CAN_PORT（J1 A12/A1，MCU FlexCAN1）**，不改 KEY_CAN_PORT（J1 B11/B1，MCU FlexCAN0，固定 500k）。

| CMD | 方向 | 请求 Payload | 成功应答 | 异常 |
| --- | --- | --- | --- | --- |
| `0x42 SET_ECU_CAN_BAUD` | Host→主控 | `[baud_code]`，1 字节 0..7 | STATUS=OK，空 Payload | 无效参数 ERR_FMT；周期广播或 TX MB 忙 ERR_BUSY；硬件错误 ERR_HW |
| `0x43 GET_ECU_CAN_BAUD` | Host→主控 | 空 | STATUS=OK，`[baud_code]` 一字节 | ERR_FMT、ERR_CMD（旧固件） |

| baud_code | ECU CAN 波特率 |
| --- | --- |
| 0 | 20 kbit/s |
| 1 | 50 kbit/s |
| 2 | 100 kbit/s |
| 3 | 125 kbit/s |
| 4 | 250 kbit/s |
| 5 | 500 kbit/s |
| 6 | 800 kbit/s |
| 7 | 1 Mbit/s |

设备 GET 返回的是**最近一次成功配置**，不是物理总线的独立波特率测量。上电默认 ECU/KEY 均为 500k。相同 SET 幂等。广播仍运行或者发送邮箱未空时必须停止/等待，不能绕过 ERR_BUSY。

## R1 HOST 真实接线

1. **连接主控**：先 PING / GET_VERSION，再以 0x43 探测当前设备能力和实际软件配置。不能根据固定的 `v0.4-dev` 版本文本推断支持新命令；每次 USB session epoch 独立记录读回。
2. **旧固件兼容**：只有 0x43 明确返回 `ERR_CMD=0x03`，才按旧固件固定 500k 兼容；无法查询或通信超时则 UNKNOWN 并阻止 ECU CAN。旧固件不得按 250k 误发。
3. **选择与应用**：选择只修改目标值；操作者点击“应用”才尝试设置。工位忙于 TEST、手动 CAN、KEY、FLASH 等实体任务时拒绝切换。当前 0x43 与目标不同时，先通过物理防护授权 **一次 exact 0x42 负载**，再执行 SET，要求随后 0x43 回读相等。
4. **实体保护**：`PhysicalCommandFence` 把 0x43 按只读命令放行；0x42 必须具备同一 USB generation / 同一速率代码 / 五秒窗口 / 一次性授权，无权访问 KEY CAN 波特率。
5. **当前 TEST 与手动 CAN**：发送前要求本次连接的速率读回匹配目标，未生效或未知均拒绝；测试记录/硬件授权不因改速率自动扩大。
6. **FLASH**：独立 FlashLab 的 XML 码率在冻结 Job + 用户实体授权 + 固件/密钥 SHA 复验后，才允许调整 ECU CAN 并向 USB 0x43 读回，随后才授权破坏性的 UDS 擦写。集成 TEST FLASH 要求当前工位已确认的速率与 Job XML 匹配，不允许在有效 TEST 物理授权中切换波特率。
7. **失败原则**：ERR_BUSY、ERR_HW、超时、ACK 后读回不一致均不视为已生效；不自动重试，不能默默回退到 500k，操作日志保留结果，操作者需检查实际状态。

## 现场验证清单

- [ ] 新版固件 Keil Release 编译通过、刷入，确认 0x43 在新设备上正确应答。
- [ ] 500k→250k→125k→500k，逐次 0x42 ACK + 0x43 一致；使用 CAN 分析仪验证物理发送/接收码率。
- [ ] KEY CAN 始终 500k，并验证 KEY_SIM 的按键命令未受影响。
- [ ] 固件广播运行、TX MB 忙时 SET 返回 ERR_BUSY；不强制清除当前待发 CAN 帧。
- [ ] 无效 code/len 返回 ERR_FMT；USB 中断、复位后必须重新确认速率。
- [ ] 在自动 TEST、手动 CAN 发送、UDS/FLASH 占用期间，动态改速率被阻止。
- [ ] 250k CanTool XML 的独立 FlashLab 完整执行，验证 0x42 / 0x43 在擦写前完成，失败时无擦写。
- [ ] 旧版主控 0x43 ERR_CMD 时，仅兼容 500k；无应答、CRC 错误和读回不一致均不能假装成功。

**交付边界**：此文件只记录两端源代码接口及验收门禁。所有固件源代码仍由 `Main_Control` 专项管理，R1 只修改 Host 及 R1 交接说明。
