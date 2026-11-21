# 04｜硬件与固件实体能力真值（R1 实测前必读）

> **版本来源**：`Main_Control@e06a820e0d54dc7719cc75af1ecae005f05e164f` 的 `docs/interfaces/BOARD_FIXED_IO_MAP.md` 和 `USB_HOST_CONTROL_INTERFACE.md`、[管理 IO 纠错 2026-11-18](../new-baseline-20261112/research/MAIN-CONTROL-IO-USB-SOURCE-UPDATE-20261118.md)、[KEY_SIM 风险接收](../new-baseline-20261112/research/KEY-SIM-HARDWARE-FACT-INTAKE-20261117.md)。此为**最新静态硬件/源码候选合同**，物理线束、硬件安全和烧录目标必须另取实测证据。main 内仍是较早固件快照。

## 1. 三台实物/软件角色

| 工程 | 基本对象 | 真实职责 |
|---|---|---|
| HOST | Windows WPF Prism WinUSB | 解释 TEST、会话/Fence、工程/DBC/记录/操作与工具 |
| Main_Control | TestCtr V1.5 / MK20DN512VLL10 | USB ⇄ 双 CAN、IGN/夹具/LED 与板级高边电路 |
| Key_Sim | EPB_ODD V1.1 / MC9S08DZ32 + TJA1042T/3 + DG411 ×5 | 接受 KEY CAN 20-bit mask，开关电阻网络形成五路模拟电压 |

CAN/USB 定义按**物理业务角色**，不是按芯片或外部连接器的 CAN1/CAN2 俗称。固件、HOST、现场线束/ECU 版本三者必须一致。

## 2. 物理 CAN 接口（不可重新推断）

| 角色 | MCU 外设 | J1 外部 | USB CAN Channel | 默认码率 |
|---|---|---|---|---|
| ECU CAN / `ECU_CAN_PORT` | **FlexCAN1** | **A12=CAN_H、A1=CAN_L** | **1** | 500 kbit/s |
| KEY CAN / `KEY_CAN_PORT` | **FlexCAN0** | **B11=CAN_H、B1=CAN_L** | **0** | 500 kbit/s 固定 |

主控通过 `0x40` 发送 CAN、`0x41` 主动上报 CAN RX（SEQ=0），两个通道均为透明代理。HOST 需常驻读 USB，按 SEQ+CMD 配请求/回应，不把 0x41 当普通 ACK。硬件/协议仅提供经典 CAN 数据帧（最多 8 字节）的已证实边界；**UI 可编辑“8/64”属于未来 CAN FD/通用输入设计方向时必须按真实控制器能力阻断 64 字节实体发送**，不得凭界面承诺当前 MK20 固件已支持 CAN FD。

## 3. ECU CAN 动态波特率 USB 协议（候选固件）

| 命令 | 请求 | 成功响应 | 错误 |
|---|---|---|---|
| `0x42 SET_ECU_CAN_BAUD` | 1B: code 0～7 | `STATUS=00`，空体 | `02` 格式非法；`05` 周期发送/TX 忙；`06` 硬件 |
| `0x43 GET_ECU_CAN_BAUD` | 空 | `STATUS=00`，1B code | `02` 非法请求；旧固件可能 `03 ERR_CMD` |

`00=20k, 01=50k, 02=100k, 03=125k, 04=250k, 05=500k, 06=800k, 07=1M`。只修改 ECU CAN，不保存到 Flash，主控重启恢复 500k。切换前停止 ECU CAN 周期发送、确认 UDS/Flash 未占用、清空发送活动；SET 后必须 GET 核对、再由 ECU 实际有效 CAN/诊断响应核验工作条件。GET 返回是固件软件记录的当前设置值**不是总线测速**。

`Main_Control` 分支源码已有此命令，**main/Main_Control/ 仍没有新 CMD**。只有现场烧录这版新固件并通过 Keil/实物测试后，R1 才能把此能力记为已验证。旧 `GET_VERSION` 里固定开发字符串不保证实际固件能力。

## 4. 高边输出与安全默认状态（纠正旧 IGN）

| 业务 | MCU GPIO / 内部 HOUT | 板外 J1 | USB 命令 | 固件默认请求 |
|---|---|---|---|---|
| **IGN** | **PTC1 / HOUT1** | **C10** | **0x12 设置、0x13 查询** | LOW/OFF |
| LED_RED1 | PTB19 / HOUT2 | C11 | 0x1A/0x1B/0x1C | LOW |
| LED_YELLOW1 | PTB18 / HOUT3 | C25 | 0x1A/0x1B/0x1C | LOW |
| LED_GREEN1 | PTC4 / HOUT4 | C26 | 0x1A/0x1B/0x1C | LOW |
| LED_RED2 | **PTC10 / HOUT5** | **C6** | 0x1A/0x1B/0x1C | LOW |
| LED_YELLOW2 | PTC9 / HOUT6 | C7 | 0x1A/0x1B/0x1C | LOW |
| LED_GREEN2 | PTC8 / HOUT7 | C8 | 0x1A/0x1B/0x1C | LOW |
| 夹具 1/2 | 独立 Air_ALVE1/2 | J1 B5 / B4 | 0x18 设置、0x19 查询 | LOW 请求释放 |
| **RL_DRV** | **PTC0，板级 Q4→U2→SOL24IN** | **内部供电链** | **无 USB 控制** | **外部 GPIO 初始化后 HIGH** |
| 其它高边 HOUT9/HOUT10、U22 CH4 | 预留 | 未批准业务控制 | **无 USB** | LOW |

**重大撤销**：旧 IGN=PTC10/J1 C6 是历史错误结论，已于 2026-11-18 被最新硬件/源码映射取代。若现场旧线束仍接 C6，不能把 LED_RED2 的响应误报为 IGN 有效。

USB 初态和 GPIO 回读只证明 MCU 逻辑，不保证高边端子已有负载电压、夹具气缸实际压紧/释放、IGN 已被 ECU 接收。旧 ECU/KEY 板电源 SET/GET 和 SOL `0x10/11,14/15,16/17,1F` 已撤销，不恢复给 UI 或 TEST。**USB 枚举断开会触发夹具 GPIO 请求释放**，同时不会自动清零 IGN/LED，这是必须现场审查的机械安全行为，而不是已证明的安全策略。

## 5. KEY_SIM 电阻网络与固件风险

KEY_SIM CAN 标准帧 `0x730`：`[0x01, mask0, mask1, mask2低4位, 0,0,0,0]` 写完整 K01～K20 mask；`[0x80,0...]` 表示全部断开。**ON 在该板控制逻辑为 GPIO LOW**；上电固件尝试全部 OFF。20 路开关经五套电阻网络形成五路 ECU 可测电压；不能描述为 20 个可自由输出的电压源。用户在 TEST 里可以直接编辑 mask + holdMs，保持时间完成后**默认不隐式释放**；物理非法组合、安全保持和断连时状态需硬件实测确定。

未解决的硬件高风险：K19/K20 固件宏与原理图引脚差异待核、K16/部分多 Key 组合在 24V 下的电阻额定和温升待核、`BATIN/BAT_EPB/BAT_KEY` 实际供电及 ECU 输入负载待核。理论功耗示例**不是实测值**，不得由 HOST 根据未验证假设随意硬编码“禁用某几键”或默许所有组合长时间保持。

## 6. 安全验收必须分层

1. 固件源码与接口一致；
2. Keil/CodeWarrior 对明确 HEAD 构建成功；
3. 刷写已识别实体镜像，读回固件能力；
4. 实测 J1 电压、CAN 帧、KEY_SIM 模拟量、机械夹具、IGN 与设备断开/复位；
5. 在真实 ECU 与安全方案下验证 TEST/UDS/FLASH/WAVE；运行日志保存 ECU 原始响应和版本。

**禁止**：主控 USB OK=ECU PASS；CAN ACK=KEY 电压已到位；GPIO LOW=高边安全绝对成立；0x43=总线测速；独立烧录工具成功=R1 自动 TEST 通过。
