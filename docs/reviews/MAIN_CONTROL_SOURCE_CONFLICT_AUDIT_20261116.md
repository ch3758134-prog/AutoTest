# Main_Control 源码冲突整理与修改边界（2026-11-16）

## 版本与权限

- 源码基准：`Main_Control@c830dbcd56b436688437b446d222473a88b85098`。
- 目标：消除**同一个固件的重复引脚配置及错误注释**，不改变 USB 命令、GPIO 有效引脚/初始电平、CAN 通道、USB 驱动行为。
- 本次**不修改 Air 管脚、不执行烧录、不合并 Main_Control**，仅 Draft PR 等待审查。
- 原理图：TestCtr V1.5 p1/p3/p5。静态设计事实 ≠ 实际 PCB/线束。

## 已处理：旧 GPIO 代码内部冲突

- 原 `EAL_GPIO.h` 的 `IGN_PORT/IGN_PIN=GPIO_B/19`，但 `EAL_GPIO.c` 的 `s_sigPort/s_sigPin` 对 IGN 真正使用 `GPIO_C/10`；业务宏 `IGN_HIGH` 调用 `EAL_GPIO_Set`，走后者。
- 现在将旧**实际运行行为** `GPIO_C/10` 固定在头文件唯一配置宏中，`.c` 表使用头文件宏，后续无需双份修改。
- `EAL_GPIO.c` 和 `main.c` 原注释写“四路输出 LOW”，实际数组五项全是 `1U`。本次只改注释，**五路上电仍全 HIGH**。没有任何“安全修复”的承诺。

## 尚未解决：原理图/固件映射不同

| 旧源码逻辑名 | 当前实际 GPIO | TestCtr V1.5 MCU 网络（p3） | 状态 |
|---|---|---|---|
| RL_DRV | PTC0 / U7 P70 | RL_DRV | 设计连接可追踪，现场状态未知 |
| KEY_PWR_DRV | PTC8 / P80 | SOL9IN | 占用/语义冲突，不能擅改 |
| ECU_PWR_DRV | PTC9 / P81 | SOL10IN | 占用/语义冲突，不能擅改 |
| IGN | PTC10 / P82 | SOL11IN | 占用/语义冲突，不能擅改 |
| SOL | PTB18 / P64 | SOL6IN | 历史动作语义与设计网络不一致 |

**警告：** 此清理候选保留上述旧行为，不可作为安全性已验证的固件直接烧录。后续以 Air 高边两通道单独做硬件→固件映射；不能把一个通道的关断自动等同为整个 SOL24IN 母线应关断。

## 待后续独立处理的代码问题

1. `ASW_EcuEmu.c`: DWT 时间基 96MHz / 32bit，约 44.74 秒回绕；`EAL_TIME_MS_TO_TICKS(60000)` 溢出，60000ms 周期无法准确表达。
2. `EAL_USB.c`: RX 满时丢弃、TX 高优先级队列满时丢帧；业务无可靠入队失败返回。
3. `ASW_BusProxy.c`: 校验 15B 和 DLC，未校验 IDE 标识（应 0/1）及标准/扩展 ID 范围。
4. `CDD_USB.c`: USB 总线复位仅清 EAL USB RX；CDD 的半帧累积状态未明确同步复位。
5. `MCAL_CAN.c`: Freeze/FRZACK 多处无限循环；无初始化超时诊断。
6. `CDD_KeySim.c` 以 CAN ID 0x100 构建旧命令，**并未进入现行 Keil Target**；不能把它当作运行时已启用。Key_Sim 的固定源码使用 CAN ID 0x730。
7. `GET_SOL` 与相关 `GET_*` 返回的是 GPIO 读数/命令语义，不是实际气阀动作或电源输出的独立测量。
8. PR #57 一次性禁用既有命令、加新 0x18/0x19 并混入 Air 控制，**不建议直接合并**，应另行确定最小兼容方案。

## 验证状态

已检查本次差异只改 GPIO 绑定**定义来源**和注释，运行中 `RL_DRV/PTC0`、`KEY/ PTC8`、`ECU/PTC9`、`IGN/PTC10`、`SOL/PTB18`、所有初始电平与原始源码一致。尚未执行真实 Keil 构建和硬件测试；这是代码候审，不是签发/烧录许可。
