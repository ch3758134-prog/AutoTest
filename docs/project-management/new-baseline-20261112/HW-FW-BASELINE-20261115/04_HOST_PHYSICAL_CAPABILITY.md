# HW/HOST｜固件命令、硬件边界与上位机依赖审查

## 原理：分清五级事实
`HOST REQUEST → USB ACK → 固件 GPIO/CAN 动作 → 电气/机械输出 → DUT 实际观测`。前一层成功不证明下一层已成立。用户现场电磁阀改 GPIO 后，如果仅内部映射改变、USB 动作语义与错误码不变，则**无需默认重写上位机**，但仍需发布新的构型组合与实体验证。

## 已有主控协议静态事实
来源 `Main_Control@c830dbcd` 的 `CDD_USB.h`、`ASW_UsbSvc.c`、`ASW_PwrCtrl.c`、`ASW_BusProxy.c`；不是现役固件证明。

| USB CMD | 源码语义 | HOST/硬件边界 |
|---|---|---|
| 0xF0 / 0xF1 | PING / GET_VERSION | 仅通信与自报版本；版本字符串 `EPB_TEST_CTRL v0.3` 不等于 FW SHA |
| 0x10/0x11 | SET/GET ECU_PWR | **历史固件能力**；BIZ-11 当前 DUT 电源外置，不成为 HOST 控制义务 |
| 0x12/0x13 | SET/GET IGN | 1=ON 0=OFF；使用 GPIO 表（当前源码 PTC10），外部真实点火电平/有效极性需核对 |
| 0x14/0x15 | SET/GET KEYPWR | 历史固件 KEY 供电控制；现场板卡/线束和产品需求需审查 |
| 0x16/0x17 | SET/GET SOL | **动作值** 0=升起、1=下降/压紧；当前源码 HIGH=升起 LOW=压紧；GET 由 GPIO 输入读回换算，不能代表机械到位 |
| 0x1F | GET_ALL_PWR | 仅若干 GPIO 读回 bit flags；无电压/机械独立证据 |
| 0x40/0x41 | CAN_SEND / CAN_RX_IND | 15B payload `port,IDE,ID(LE4),DLC,data8`；0=按键板 1=DUT 的固件/现场角色协议 |
| 0x50-0x53 | ECU 广播设置/启动/停止/状态 | 在 ECU CAN1 上执行需实际授权及物理约束，不因固件已有即成为可任意操作的 UI 功能 |

主控 `main.c` 开 `CAN0` / `CAN1` 500kbps；`MCAL_CAN.c` 的 PAD 与图纸 p3/p4 的 **CAN1/CAN2 设计网络命名错位**；查看 [主控引脚档案](02_MAIN_CONTROLLER_PIN_AND_MODULES.md)。硬件 ACL、USB Fence 与实体授权以当前受控 HOST 源码为准，**固件能够发送不等于 HOST 应无条件允许发送**。

## HOST 与实体能力分离
- HOST `MainControllerCodec` 目前有 `Ping` / `CanSend` / `CanRxIndication` / `SetIgn`；其他命令可能在专业服务中封装，不能仅据这一个类宣称所有命令均 UI 可用。
- `KeypadCommandCodec` 使用 CAN0、ID `0x730`、20-bit mask；电阻网络最终电压、ECU 功能映射来自 Controller/Profile 与现场实测。
- CAN1 DUT 的 UDS/ISO-TP/Flash 与其他刺激共享受控真实设备会话；无源码/安全批准不能绕过 `PhysicalCommandFence`。
- 一台 PC/工位/主控/当前 DUT，自动测试/手动台架/FlashLab 不能争抢物理发送所有权；独立工具后端复用现有设备核心。
- `HardwareCapability` 的管理状态应是 `DESIGNED`、`FIRMWARE_EXPOSED`、`HOST_WIRED`、`FIELD_VERIFIED`、`PRODUCT_AUTHORIZED`，**不得**以 UI 按钮存在或图纸信号存在直接标为 VERIFIED。

## 变更触发矩阵（PM 负责签字）
| 变化 | 固件影响 | HOST 影响 | 验收 |
|---|---|---|---|
| SOL GPIO 由 A 脚搬 B 脚，SET_SOL/GET_SOL 动作值不变 | GPIO mux/初态/驱动链检查 | **通常无协议修改**；核对能力与结果提示即可 | 上电静态、受控动作、读回与实际结果分离 |
| SOL 动作值反向或默认状态变化 | 电平逻辑/风险态变化 | HOST 显示与自动 TEST/手动台架控制需审查 | 必须重新批准契约 |
| CAN 芯片通道/收发器/线束变化 | CAN PAD/role/rate 改动 | BusRoleMap/硬件配置和真实收发需核对 | CAN0/1 两通道、DUT/KEY_SIM 实测 |
| KEY_SIM GPIO/Kx 电阻组合改变 | KEY mask→模拟输出变化 | Controller/Profile 资源有效性和参数需审查 | 五路输出与 DUT 识别区间实测 |
| 只改 PCB 丝印或文档 | 不自动重编译 | 不自动改 HOST | 受控文件版本与一致性核查 |

**G4 WPF 与独立 FlashLab 不因该档案被停止**。触发真实兼容变化时 PM 发定点 CR，绝不让 FW AI 修改主 UI/TEST/Flash 私有代码。
