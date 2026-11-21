# AutoTest｜实体硬件、固件与可复用技术资产的事实边界
**2026-11-21｜本文件汇总已核对的静态资料，不表示实物电气/固件已验证。**

## 1. 唯一物理拓扑
主控 `TestCtr V1.5`（`MK20DN512VLL10`）通过 USB 连接 Windows HOST；主控两路 CAN 分别连接真实 ECU 与按键模拟板；按键板 `EPB_ODD V1.1`（`MC9S08DZ32`）接受 20 Key 掩码，经 5 路电阻网络向 ECU 输出模拟电压。当前单 PC/单工位/同时 1 DUT。

| 角色 | MCU 外设 | 主控 J1 对外 | USB 通道 | CAN 码率 |
|---|---|---|---|---|
| **ECU CAN** | FlexCAN1 | A12=H / A1=L | 1 | 初始 500 kbit/s，候选固件支持 0x42/0x43 动态设置/查询 |
| **KEY CAN** | FlexCAN0 | B11=H / B1=L | 0 | 固定 500 kbit/s |

CAN 物理角色已经由负责人定死，不能根据旧“CAN1/CAN2”外壳标签互换。当前 USB 与 MK20 固件只有经典 CAN 至多 8B 实物依据，UI 显示 64B 不能等同 CAN FD 物理能力。USB 0x40 接受发送请求不证明 ECU 接受/回应。

## 2. IO 与夹具
依据 Main_Control 已归档静态接口资料：IGN=HOUT1/PTC1/J1 C10（USB 0x12/0x13）；LED1~6 对应 HOUT2~HOUT7，旧 PTC10/C6 为 LED_RED2 **不是 IGN**；夹具1/2通过独立 Air_ALVE 请求、J1 B5/B4（USB 0x18/0x19）；RL_DRV/PTC0 是板级内部供电逻辑 HIGH，**不得暴露为可控 USB 业务 IO**。相关输出默认关闭请求与真实端口电压、气缸位置不能画等号。

**新生产场景的额外事实缺口**：实际夹具压紧反馈、两个电磁阀的动作时序和可靠性、台架实体启动/停止输入、固件 USB 上报、硬件断电后夹具与 ECU 的状态，都尚无正式现场验收证据。不要因为生产状态机要求而臆造传感器或 USB CMD。

## 3. KEY_SIM 实物限制
20 个 K1~K20 是开关状态而非 20 个模拟电压输出；由五路电阻网络输出。TEST 直接编辑完整 20 位 mask 和保持时间，允许等待、不隐式释放。K19/K20 GPIO/电路差异、多键热功耗/温升和供电工况仍需实物调查，不以 AI 理论推断硬编码禁止组合，也不默认所有组合安全。

## 4. ECU CAN 速率
Main_Control 候选固件有 0x42 SET / 0x43 GET，速率编码 0..7 对应 20/50/100/125/250/500/800/1000 kbit/s；仅 ECU CAN，重启不持久。**固件分支源码存在≠现场已刷固件≠主控读回证明 ECU 有真实通信**。具体 Keil 编译与 USB/CAN 实测门禁不变。CanTool Flash Job 现有支持的波特率集合不能由 MCU 码率列表推断扩大。

## 5. 技术资产的共享规则
- 独立手动台架 CAN 和 TEST/Production 共享真实 DEVICE 与 DBC 解析能力，但工具窗口独立且无需工程。
- UDS 工具、独立 FLASH 工具与生产前置烧录复用经过验证的 ISO-TP/UDS/CanTool 烧录引擎，不能复制第二套物理协议。
- 资源的**所有权**不能从“谁在引用它”推断：DBC/波形/固件/安全算法可作为独立资源，工程只选择当前有效项。
- 需要单设备物理会话协调、工具发送与 TEST/Flash 运行避免互相干扰，但这不是禁止同时打开查看窗口的理由。
- Flash 后 APP 50 03 会话响应≠真实软件版本 DID/功能判定；真正生产 PASS 必须有适合该 ECU 的已验证判据。

## 6. 源证据与状态
- [现有 HW/FW 详细静态表（2026-11-21 快照）](../current-20261121/04_HARDWARE_FIRMWARE_BASELINE.md)。
- [Main_Control 固定 IO 映射](https://github.com/ch3758134-prog/AutoTest/blob/Main_Control/docs/interfaces/BOARD_FIXED_IO_MAP.md)。
- [主控 USB 控制接口](https://github.com/ch3758134-prog/AutoTest/blob/Main_Control/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)。
- [真实 FLASH 参考及限定实测](../new-baseline-20261112/research/FLASH-REFERENCE-VALIDATION-INTAKE-20261118.md)。
- [KEY_SIM 风险接收](../new-baseline-20261112/research/KEY-SIM-HARDWARE-FACT-INTAKE-20261117.md)。

现有 Main_Control/Key_Sim 分支仍各自负责 FW 构建与现场验证，**不得通过项目管理改产品业务而直接修改硬件真值**。