# AutoTest｜G3 补充变更：主控板 USB 与双夹具接口

- 日期：2026-11-16（PT）；**2026-11-16 晚间追踪到固件源代码整改新提交**。
- 批准：负责人已批准两路独立夹具、供电/旧 SOL 业务取消、RL_DRV 内部化和对外 USB 接口方向。
- **状态：`SOURCE_UPDATED / KEIL_BUILD_UNVERIFIED / FIELD_UNVERIFIED`**。固件源代码已有新协议，**不代表当前真实板卡已烧录或实际能使用**。
- 权威固件源快照：`Main_Control@16abba7fec94dc4b7fbdc05b91ee209b5c7efa12`，接口文档 v0.2（Blob `cb9765539d651be551c0e4771f22bca7a00eac1f`）。该分支此前的 `5d2ad5b7` 只是历史调查，已被当前代码证据更新。
- 相关：G1 REQ-13/19/21/23/24/26/29/31/43，G2 A-02/A-05，G3-DEVICE/TEST 与公共流程/KEY_SIM 设计。

## 1. 已确认的硬件和业务边界

| 控制对象 | MCU GPIO | 外部连接 | 请求语义 |
|---|---|---|---|
| IGN | PTC10 | J1 C6 | 0=OFF，1=ON |
| 夹具1 / Air_ALVE1 | PTB1 | J1 B5 | 0=释放，1=压紧 |
| 夹具2 / Air_ALVE2 | PTC2 | J1 B4 | 0=释放，1=压紧 |
| RL_DRV | PTC0 | PCB 内部功率控制 | **没有对外 USB/上位机操作对象** |

硬件 TestCtr V1.5，MCU MK20DN512VLL10；USB 至 Windows；CAN0 至 KEY_SIM、CAN1 至 DUT，现有固件两路 CAN 均初始化为 500 kbit/s。**两夹具独立，不互锁**；修改任一路不能连带强制改动另一路。外部 ECU 主电源及旧 KEY 供电、SOL 均不作为当前 HOST 可控功能。

**GPIO 回读和 USB ACK 只能表示 MCU 层事实，不是 J1 电压、阀门气路或夹具机械到位的独立测量。**

## 2. 通信层现行事实

- WinUSB：VID=`0x1183`、PID=`0x5850`，接口 GUID=`4da6d065-1e3e-47e6-8cc3-c6977351defb`，接口0，Bulk OUT=`0x03` / Bulk IN=`0x82`，64 字节最大包长。
- 应用层：请求 `SOF 0xA5, SEQ(LE 2B), CMD, LEN, PAYLOAD, CRC16/MODBUS(LE), EOF 0x5A`；响应增加 STATUS；CRC 覆盖 SEQ 到 PAYLOAD；SEQ 非零用于请求响应关联，`0x41 CAN_RX_IND`、SEQ=0 为异步上报。
- `0xF0 PING` 返回 PONG；`0xF1 GET_VERSION` 在**最新固件源码**中固定返回 `EPB_TEST_CTRL v0.4-dev`。这不是自动从固件镜像生成的可靠身份；实际设备须回读并关联现场已烧录版本。
- `0x40 CAN_SEND` 固定 15 字节，channel=0 是 KEY_SIM，channel=1 是 DUT；`0x50~0x53` 现仍保留 CAN1 周期广播；未在本变更授权删除。
- USB 收发按字节流增量处理拆包/粘包和 CRC；0x41 可以与普通命令交错。Windows 枚举到 EPB_Host 不等于 WinUSB 打开/PING 成功。

## 3. 双夹具协议：现已写入固件源代码

以下已在 `Main_Control@16abba7f` 的 `ASW_PwrCtrl.c`、`ASW_UsbSvc.c` 和 `CDD_USB.h` 交叉核对：

| 命令 | 请求 Payload | 成功响应 | 含义 |
|---|---|---|---|
| `0x12 SET_IGN` | 1B `[state=0/1]` | Status=0、空 | 仅设置 IGN GPIO |
| `0x13 GET_IGN` | 空 | Status=0、1B | 读取 IGN GPIO |
| **`0x18 SET_CLAMP`** | **2B `[clamp=1或2, state=0或1]`** | Status=0、空 | 独立设置指定夹具，另一夹具输出不变 |
| **`0x19 GET_CLAMPS`** | 空 | Status=0、1B | bit0=夹具1、bit1=夹具2 的 GPIO 电平 |

非法 Payload 长度、夹具号或状态值：`0x02 ERR_FMT`。未识别命令：`0x03 ERR_CMD`。正常的 GPIO 组合是 00、01、10、11，必须在真实板卡验证其中每个状态，特别确认只设置一个夹具不影响另一夹具。

**必须区分两种固件：**历史 `v0.3` 旧版 `0x18` 只有 1B 互斥语义；当前 `v0.4-dev` 源码采用 2B 独立语义。HOST 新版必须先确认现场实际部署了兼容的新固件，并通过明确的协议身份/版本与适用联调证据才放开夹具实体操作；**不自动降级到旧一字节 0x18**。固定字符串 `v0.4-dev` 可以作为检查线索，但不能独自证明实际编译/烧录 SHA。

## 4. 已取消指令和内部供电

最新源码已从 `CDD_USB.h` 与 `ASW_UsbSvc.c` 移除旧 USB 业务定义/路由：`0x10/0x11` ECU 供电、`0x14/0x15` KEY 供电、`0x16/0x17` SOL、`0x1F` GET_ALL_PWR。对这些未知命令，新源码应返回 `ERR_CMD`；HOST 不得再有这些业务控件或调用。

当前 `EAL_GPIO_Init`：Air1/2 LOW、IGN HIGH、RL_DRV HIGH；历史 PTC8/PTC9 不再配置为旧供电输出。**RL_DRV HIGH 是 PCB 公共功率链内部初始化，不是给 HOST 的 ECU 电源开关**。不能为删除旧业务功能而关闭 RL_DRV，旧 PTC8/PTC9 的物理复位态须实测核查。

`ASW_PwrCtrl_Task` 在 USB **枚举丢失且曾压紧**时将两个夹具输出置 LOW。此行为仍保留在新源码，**并未获得新安全策略或真实气路验证**；不等于应用心跳超时也不等于机械到位反馈。上电、断线、重连和异常的最终安全策略仍须现场审查；HOST 不得自选默认释放、默认保持或声称恢复成功。

## 5. 实施责任与验收边界

**MAIN_CONTROL**：确认目标工程编译、固件镜像/板卡版本身份，更新接口文档；提供 Keil Build 日志；测试新命令非法输入、单夹具独立保持、旧命令 ERR_CMD、启动/断连/重连、IGN/J1/RL_DRV 的实际电气效果。当前**仅有源码证据，没有 Build/现场 PASS**。

**DEVICE**：在现有 USB/会话/PhysicalCommandFence 上增加 `SET_CLAMP/GET_CLAMPS` 编解码、错误/版本识别与独立输出；必要时提供能力不满足时禁用，不绕过已有真实授权。

**TEST / PROJECT**：支持两个独立夹具作为 TEST 可配置动作、公共流程中可调用动作、正式资源与运行证据；不编造另一套 DSL 或新设备执行器。

**WPF / INTEGRATION**：手动台架与 Inspector 显示两个夹具独立控制及请求/ACK/UNKNOWN；移除旧供电/旧 SOL 用户入口；保留 USB、CAN0/1、IGN 真实能力。每个操作最终从唯一 DEVICE 端口发出。

**分层验证**：源码提交 → Keil Build → 现场装板识别 → WinUSB/PING → 正式夹具命令 ACK → MCU GPIO/J1 实测 → 气动机械动作实测；任一缺失不得跨层宣称 PASS。

## 6. 精确来源

- [固件接口 v0.2](https://github.com/ch3758134-prog/AutoTest/blob/16abba7fec94dc4b7fbdc05b91ee209b5c7efa12/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)
- [固件源 ASW_PwrCtrl](https://github.com/ch3758134-prog/AutoTest/blob/16abba7fec94dc4b7fbdc05b91ee209b5c7efa12/Project/source/ASW/PwrCtrl/ASW_PwrCtrl.c)
- [固件协议头 CDD_USB.h](https://github.com/ch3758134-prog/AutoTest/blob/16abba7fec94dc4b7fbdc05b91ee209b5c7efa12/Project/source/BSW/CDD/USB/CDD_USB.h)
- [原理图审查档案](https://github.com/ch3758134-prog/AutoTest/tree/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115)
