# AutoTest｜主控板 IO 初始化、LED USB 控制与 RL_DRV 边界更新（2026-11-18）

> **事实记录类型**：最新固件源码/对外 USB 文档核查、G3 现行接口事实更新和 HOST 执行交接；**不授予真机危险 IO 操作许可**。  
> **精确源码版本**：`Main_Control@bf81a64e465aa284155ee73ef44a16e39f739bce`（HEAD 已回读，源修改跨多个先后提交，HEAD 本身最后一笔仅更新 HTML）。  
> **状态**：`SOURCE_UPDATED / KEIL_BUILD_NOT_VERIFIED / FIRMWARE_ON_DEVICE_NOT_VERIFIED / ELECTRICAL_FIELD_NOT_VERIFIED`。  
> **原合同**：`../G3-SUPPLEMENTS-20261116/CR-MAIN-CONTROL-USB-CLAMPS.md`、`HOST-MAIN-CONTROL-IMPLEMENTATION-HANDOFF.md`。当前源码事实优先于旧 2026-11-16 文档中的过时 GPIO 映射、初态和代码目录。批准的产品方向（两夹具独立、旧电源/SOL 对外取消、RL_DRV 不属于 HOST 业务）不变；本次只更新实现事实。

## 1. 当前固定引脚与上电软件初始化

| 业务/硬件名 | MCU GPIO | 高边与 J1 对应 | 软件初始化 | USB 用户业务 |
|---|---|---|---|---|
| **IGN / HOUT1** | **PTC1** | U5 CH1 / J1 **C10** | LOW / 关闭请求 | `0x12 SET_IGN`、`0x13 GET_IGN` |
| LED1 / HOUT2 | PTB19 | U5 CH2 / J1 C11 | LOW | 0x1A/0x1B/0x1C |
| LED2 / HOUT3 | PTB18 | U5 CH3 / J1 C25 | LOW | 0x1A/0x1B/0x1C |
| LED3 / HOUT4 | PTC4 | U5 CH4 / J1 C26 | LOW | 0x1A/0x1B/0x1C |
| **LED4 / HOUT5** | **PTC10** | U22 CH3 / J1 **C6** | LOW | 0x1A/0x1B/0x1C |
| LED5 / HOUT6 | PTC9 | U22 CH2 / J1 C7 | LOW | 0x1A/0x1B/0x1C |
| LED6 / HOUT7 | PTC8 | U22 CH1 / J1 C8 | LOW | 0x1A/0x1B/0x1C |
| 夹具1 / Air_ALVE1 | PTB1 | U23 CH4 / J1 B5 | LOW / 请求释放 | 0x18/0x19 |
| 夹具2 / Air_ALVE2 | PTC2 | U23 CH3 / J1 B4 | LOW / 请求释放 | 0x18/0x19 |
| HOUT9 | PTC3 | U23 CH2；关联 SOL3IN/H_SPEED2 | LOW | **未分配，不暴露** |
| HOUT10 | PTB0 | U23 CH1；关联 SOL1IN/H_SPEED1 | LOW | **未分配，不暴露** |
| U22 CH4 预留控制 | PTC11 | SOL12IN，板外 HOUT8 对应**未核实** | LOW | **未分配，不暴露** |
| **RL_DRV** | **PTC0** | Q4→U2 BTS50055→SOL24IN 共用板级功率链 | **最后初始化 HIGH** | **无 USB SET/GET，也不随业务请求变化** |

**重大旧资料纠正**：此前 `Main_Control@16abba7f` / 管理 G3 合同把 IGN 当成 `PTC10 / J1 C6`；最新正式固件 `EAL_GPIO.h` 已明确 IGN = `HOUT1/PTC1/J1 C10`，`PTC10/J1 C6` 现在是 **LED4/HOUT5**。旧映射必须从 HOST 执行清单与现场接线核对中废止。新 `HOUT8` 不可凭 U22 CH4 擅自命名/开放。

`EAL_GPIO_Init` 先初始化所有已识别的外部业务与预留输出 GPIO 为 LOW，**之后**初始化板级内部 RL_DRV=HIGH。该顺序属于软件初始化行为，不证明 MCU 复位至初始化完成的瞬态、功率母线、J1 端电压、负载和气阀实际状态。不能将“所有外部输出关闭”误写成“RL_DRV 也 LOW”。

## 2. USB IO 现行编码合同

以下命令在 `CDD_USB.h` 与 `ASW_IoCtrl.c`、`ASW_UsbSvc.c` 中已找到对应定义/路由，沿用现有 `0xA5/SEQ/CMD/LEN/CRC16/0x5A` 协议：

| CMD | 请求字节 | STATUS=OK 的响应数据 | 副作用及禁止推断 |
|---|---|---|---|
| `0x12 SET_IGN` | 1B `state=0/1` | 空 | 只修改 IGN/HOUT1 的 GPIO |
| `0x13 GET_IGN` | 空 | 1B `0/1` | MCU GPIO 电平 |
| `0x18 SET_CLAMP` | 2B `[clamp=1/2,state=0/1]` | 空 | 只修改指定夹具输出；另一夹具保持 |
| `0x19 GET_CLAMPS` | 空 | 1B bit0=夹具1，bit1=夹具2 | MCU GPIO，不代表气缸机械到位 |
| `0x1A SET_LED` | 2B `[index=1..6,level=0/1]` | 空 | 仅设置指定 LED/HOUT GPIO |
| `0x1B GET_LEDS` | 空 | 1B，bit0..5=LED1..6 | 读取 MCU GPIO；bit6/7 为0 |
| `0x1C SET_LEDS` | 1B `mask=0x00..0x3F` | 空 | 按次序写入六路 GPIO，不保证同一电气时刻切换 |

长度、index、level、位图高2位非法均拒绝（`0x02 ERR_FMT`）。`0x1A/0x1B/0x1C` 是 GPIO **控制对象命名为 LED1~6**，不证明外部负载一定是发光二极管、J1 电压或机械行为。USB 读回只代表 GPIO。未分配 GPIO、RL_DRV 和旧取消功能不加入 USB。

旧供电 `0x10/0x11`、按键供电 `0x14/0x15`、SOL `0x16/0x17`、汇总供电 `0x1F` 不再作为新固件业务接口。主控 CAN/USB 帧协议和 CAN 路由没有因本次 IO 变更修改。

`ASW_IoCtrl` 已移除任何 `RL_DRV` 读写或 ON/OFF 自动门控；RL_DRV 仅在 EAL 硬件初始化出现。不得让 HOST 通过某条 IGN/LED/夹具命令“间接控制 RL_DRV”。上述 IO 控制动作之间没有以此功率信号实现的软件互锁。

## 3. 断连/复位与当前软件事实

- `ASW_IoCtrl_Task`：**仅 USB 枚举不就绪且曾有夹具压紧请求**时，撤销两夹具请求（置 LOW），该行为尚未实测/批准为机械安全策略。
- **IGN/LED** 不因上述 USB 枚举断连回调自动清零，软件 GPIO 请求可保持旧值；MCU 真正复位重跑初始化后回到 LOW。USB 断连不等于 MCU 断电，也不等于应用层心跳丢失。
- `RL_DRV` 不受 USB 枚举变化影响；初始化后为 HIGH。外部功率电路及 SOL24IN 是否建立实际电压，必须由电气测量确认。
- `0xF1 GET_VERSION` 仍为硬编码字符串 `EPB_TEST_CTRL v0.4-dev`，**并未随此次新增 LED/默认 IO 变化自动产生新版本标识**。HOST 不能只凭这个字符串断言现场板卡具有此 HEAD 的新 GPIO 逻辑或 LED 指令；需要烧录回执与镜像身份/受控功能验证。

## 4. HOST 执行 AI 必须取得的接口信息

- 检查已存在的 `MainControllerCodec`、DEVICE 服务、PhysicalCommandFence、手动台架与正式 TEST Inspector；仅定点增加/补齐 **IGN、两独立夹具、六路 LED 单路及整组**的编码、状态、预检与审计；不动 USB 基础帧/CAN 通路。
- 永远不提供 RL_DRV、HOUT9/HOUT10、U22 CH4 的用户 SET/GET，也不恢复 ECU/KEY 供电或旧 SOL。
- IGN 物理映射必须认 `PTC1/J1 C10`；若旧台架线束仍接 `PTC10/J1 C6`，应阻断声称“IGN 真实可用”并要求现场核线，避免 LED4 被误当点火。
- 在主控 Keil Build、真实固件识别、WinUSB GET_VERSION/能力确认与必要现场安全许可前，不把新 LED/夹具等动作自动置为可实体执行。可先开发和测试 Codec/非实体编辑。
- GPIO 初始化 LOW、设置/查询 STATUS OK、J1 输出电压、ECU/LED/夹具物理响应需分别验收；未观测电气/机械状态时不能显示真实负载已切换。
- 特别审查 USB 断连夹具释放（可能影响夹具机械安全）和断线后 IGN/LED 持续输出；停止 TEST/关闭 WPF 不保证所有输出自动清零，不能擅自发新“清零所有”业务命令。

## 5. 后续专项核对矩阵

| 验证层 | 要做的事 | 当前结果 |
|---|---|---|
| Keil 编译 | 精确 HEAD + 工程编译与镜像哈希 | `NOT_VERIFIED` |
| 软件初始化与复位 | 上电/复位时 GPIO 输出态、初始化顺序、PTC0 及实际外部负载 | `NOT_VERIFIED` |
| USB 协议 | 0x12/13、0x18/19、0x1A/1B/1C 的正常/非法 Payload、读回与错误码 | `NOT_VERIFIED` |
| HOUT/J1 电气 | IGN=HOUT1/J1 C10、LED4=HOUT5/J1 C6，其余6路，夹具独立 | `NOT_VERIFIED` |
| 业务与安全 | 真 ECU 点火响应、LED/负载状态、两夹具动作、断连/重连 | `NOT_VERIFIED` |

## 6. 精确来源（Main_Control）

- [EAL_GPIO.h](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/Project/source/BSW/EAL/GPIO/EAL_GPIO.h)
- [EAL_GPIO.c](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/Project/source/BSW/EAL/GPIO/EAL_GPIO.c)
- [ASW_IoCtrl.c](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/Project/source/ASW/IoCtrl/ASW_IoCtrl.c)
- [CDD_USB.h](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/Project/source/BSW/CDD/USB/CDD_USB.h)
- [最新 USB_HOST_CONTROL_INTERFACE](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)

**本记录更新的是当前源码事实、风险与上位机待联调接口，不回写历史 Gate 审批原文，不声称任何真实外部输出已经得到验证。**
