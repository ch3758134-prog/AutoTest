# AutoTest｜G3 补充变更：主控板 USB 与双夹具接口

- 最初登记：2026-11-16；**当前接口事实按 2026-11-18 最新主控提交同步修订**。旧合同版本可从 Git 历史追溯。
- 批准：负责人已批准两路独立夹具、供电/旧 SOL 业务取消、RL_DRV 内部化和对外 USB 接口方向。
- **状态：`SOURCE_UPDATED / KEIL_BUILD_UNVERIFIED / FIELD_UNVERIFIED`**。固件源代码已有新协议，**不代表当前真实板卡已烧录或实际能使用**。
- **当前固件源码快照**：`Main_Control@bf81a64e465aa284155ee73ef44a16e39f739bce`，含外部 GPIO 默认 LOW、LED USB 0x1A～0x1C、IGN/HOUT1 映射修正，以及 RL_DRV 板级初始化 HIGH 且不受 USB 业务联动。早前 `16abba7f` 为历史源码，不再代表最新 GPIO/USB 接口全貌。
- 相关：G1 REQ-13/19/21/23/24/26/29/31/43，G2 A-02/A-05，G3-DEVICE/TEST 与公共流程/KEY_SIM 设计。

## 1. 已确认的硬件和业务边界

| 控制对象 | MCU GPIO | 外部连接 | 请求语义 |
|---|---|---|---|
| **IGN / HOUT1** | **PTC1** | **J1 C10** | 0=OFF，1=ON，软件初始化 LOW |
| 夹具1 / Air_ALVE1 | PTB1 | J1 B5 | 0=释放，1=压紧 |
| 夹具2 / Air_ALVE2 | PTC2 | J1 B4 | 0=释放，1=压紧 |
| **LED1～6 / HOUT2～7** | PTB19/PTB18/PTC4/PTC10/PTC9/PTC8 | J1 C11/C25/C26/C6/C7/C8 | 0=OFF，1=ON；USB 0x1A～0x1C，软件初始化 LOW |
| HOUT9/HOUT10、U22 CH4 预留 | PTC3/PTB0/PTC11 | 不提供外部控制业务 | 软件初始化 LOW，无 USB SET/GET |
| RL_DRV | PTC0 | PCB 内部功率控制 | **在其它外部输出 GPIO LOW 后初始化 HIGH；没有对外 USB/上位机操作对象** |

硬件 TestCtr V1.5，MCU MK20DN512VLL10；USB 至 Windows；CAN0 至 KEY_SIM、CAN1 至 DUT，现有固件两路 CAN 均初始化为 500 kbit/s。**两夹具独立，不互锁**；修改任一路不能连带强制改动另一路。外部 ECU 主电源及旧 KEY 供电、SOL 均不作为当前 HOST 可控功能。

**GPIO 回读和 USB ACK 只能表示 MCU 层事实，不是 J1 电压、阀门气路或夹具机械到位的独立测量。**

## 2. 通信层现行事实

- WinUSB：VID=`0x1183`、PID=`0x5850`，接口 GUID=`4da6d065-1e3e-47e6-8cc3-c6977351defb`，接口0，Bulk OUT=`0x03` / Bulk IN=`0x82`，64 字节最大包长。
- 应用层：请求 `SOF 0xA5, SEQ(LE 2B), CMD, LEN, PAYLOAD, CRC16/MODBUS(LE), EOF 0x5A`；响应增加 STATUS；CRC 覆盖 SEQ 到 PAYLOAD；SEQ 非零用于请求响应关联，`0x41 CAN_RX_IND`、SEQ=0 为异步上报。
- `0xF0 PING` 返回 PONG；`0xF1 GET_VERSION` 在**最新固件源码**中固定返回 `EPB_TEST_CTRL v0.4-dev`。这不是自动从固件镜像生成的可靠身份；实际设备须回读并关联现场已烧录版本。
- `0x40 CAN_SEND` 固定 15 字节，channel=0 是 KEY_SIM，channel=1 是 DUT；`0x50~0x53` 现仍保留 CAN1 周期广播；未在本变更授权删除。
- USB 收发按字节流增量处理拆包/粘包和 CRC；0x41 可以与普通命令交错。Windows 枚举到 EPB_Host 不等于 WinUSB 打开/PING 成功。

## 3. 双夹具协议：现已写入固件源代码

以下已在 `Main_Control@bf81a64e` 的 `ASW_IoCtrl.c`、`ASW_UsbSvc.c` 和 `CDD_USB.h` 交叉核对。**注意 IGN 当前为 HOUT1/PTC1/J1 C10，不再是历史 HOUT5/PTC10/J1 C6；后者属于 LED4。**

| 命令 | 请求 Payload | 成功响应 | 含义 |
|---|---|---|---|
| `0x12 SET_IGN` | 1B `[state=0/1]` | Status=0、空 | 仅设置 IGN GPIO |
| `0x13 GET_IGN` | 空 | Status=0、1B | 读取 IGN GPIO |
| **`0x18 SET_CLAMP`** | **2B `[clamp=1或2, state=0或1]`** | Status=0、空 | 独立设置指定夹具，另一夹具输出不变 |
| **`0x19 GET_CLAMPS`** | 空 | Status=0、1B | bit0=夹具1、bit1=夹具2 的 GPIO 电平 |
| **`0x1A SET_LED`** | 2B `[index=1..6,level=0/1]` | Status=0、空 | 单独控制 LED1～LED6 对应 GPIO |
| **`0x1B GET_LEDS`** | 空 | Status=0、1B | bit0～bit5 反映 LED1～LED6 GPIO 电平 |
| **`0x1C SET_LEDS`** | 1B `[bitmap=0x00..0x3F]` | Status=0、空 | 设置六路目标 GPIO，逐个写入不保证硬件同时切换 |

非法 Payload 长度、夹具号或状态值：`0x02 ERR_FMT`。未识别命令：`0x03 ERR_CMD`。正常的 GPIO 组合是 00、01、10、11，必须在真实板卡验证其中每个状态，特别确认只设置一个夹具不影响另一夹具。

**必须区分两种固件：**历史 `v0.3` 旧版 `0x18` 只有 1B 互斥语义；当前 `v0.4-dev` 源码采用 2B 独立语义。HOST 新版必须先确认现场实际部署了兼容的新固件，并通过明确的协议身份/版本与适用联调证据才放开夹具实体操作；**不自动降级到旧一字节 0x18**。固定字符串 `v0.4-dev` 可以作为检查线索，但不能独自证明实际编译/烧录 SHA。

## 4. 已取消指令和内部供电

最新源码已从 `CDD_USB.h` 与 `ASW_UsbSvc.c` 移除旧 USB 业务定义/路由：`0x10/0x11` ECU 供电、`0x14/0x15` KEY 供电、`0x16/0x17` SOL、`0x1F` GET_ALL_PWR。对这些未知命令，新源码应返回 `ERR_CMD`；HOST 不得再有这些业务控件或调用。

当前 `EAL_GPIO_Init`：IGN/HOUT1、LED1～LED6/HOUT2～7、Air1/2、HOUT9/HOUT10、U22 CH4 未分配控制脚先初始化 **LOW**，随后 **RL_DRV/PTC0 初始化 HIGH** 维持板级内部共用功率链。`ASW_IoCtrl` 不读写 RL_DRV，也不根据 USB 命令改变其电平。PTC8/PTC9 分别是 LED6/LED5，并非旧供电输出。MCU 复位至 GPIO 初始化前的端子瞬态、实装功率状态仍待测量。

`ASW_IoCtrl_Task` 在 USB **枚举丢失且曾有压紧请求**时将两夹具 GPIO 置 LOW。**不会因枚举丢失自动关闭 IGN 或 LED1～LED6，也不会改变 RL_DRV**。这些是当前源码行为，未完成真实气路/电气安全验证，不能当作通用工位急停或自动恢复合同。

## 5. 实施责任与验收边界

**MAIN_CONTROL**：确认目标工程编译、固件镜像/板卡版本身份，更新接口文档；提供 Keil Build 日志；测试新命令非法输入、单夹具独立保持、旧命令 ERR_CMD、启动/断连/重连、IGN/J1/RL_DRV 的实际电气效果。当前**仅有源码证据，没有 Build/现场 PASS**。

**DEVICE**：在现有 USB/会话/PhysicalCommandFence 上增加 `SET_CLAMP/GET_CLAMPS` 编解码、错误/版本识别与独立输出；必要时提供能力不满足时禁用，不绕过已有真实授权。

**TEST / PROJECT**：支持两个独立夹具作为 TEST 可配置动作、公共流程中可调用动作、正式资源与运行证据；不编造另一套 DSL 或新设备执行器。

**WPF / INTEGRATION**：手动台架与 Inspector 显示两个夹具独立控制，按已批准的六路 LED/HOUT 功能研究接线及状态/审计；移除旧供电/旧 SOL 用户入口；保留 USB、CAN0/1、IGN 真实能力。每个操作最终从唯一 DEVICE 端口发出。LED 是否接入 TEST 步骤、是否启用实体操作仍须按具体 G4 合同与实机门禁核准。

**分层验证**：源码提交 → Keil Build → 现场装板识别 → WinUSB/PING → 正式夹具命令 ACK → MCU GPIO/J1 实测 → 气动机械动作实测；任一缺失不得跨层宣称 PASS。

## 6. 精确来源

- [现行固件 USB IO 接口](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)
- [本轮固件 GPIO/USB 变化和验收证据分层](../research/MAIN-CONTROL-IO-USB-SOURCE-UPDATE-20261118.md)
- [现行固件源 ASW_IoCtrl](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/Project/source/ASW/IoCtrl/ASW_IoCtrl.c)
- [现行固件协议头 CDD_USB.h](https://github.com/ch3758134-prog/AutoTest/blob/bf81a64e465aa284155ee73ef44a16e39f739bce/Project/source/BSW/CDD/USB/CDD_USB.h)
- [原理图审查档案](https://github.com/ch3758134-prog/AutoTest/tree/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115)
