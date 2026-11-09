# G0-04 · 主控固件代码事实（硬件能力利用覆盖）

> SAEF v2.0 / G0-04。**M2 内容候审；本地 G0 仍为 NOT_STARTED，B0～B6 新复验未执行。**本文件仅记录来源可核的板级/代码事实、负责人报告的历史行为和待现场验证事项，三者不能等同。

> 固件现状在硬件能力清单之后读取。**冻结不改**，只记录现有源码如何操作硬件，不能让源码中已写的功能限制对硬件能力的定义。

## 完整工程目录与构建身份

- 原件：`FW-MAIN-Project.zip`，SHA-256 `34b7e11b7d5d0fbb7b2178070f52e41febd88c15033a215516084ca294900239`。
- Keil 工程入口 `Project/Template.uvprojx`；XML 包含 `Template`、`rtc_func Release` 等 Target；`<FilePath>` 出现 118 次，**不等于现场一次编译的独立源文件数**。
- 现场 MDK 版本、最终选择 Target、实际链接结果、烧录映像哈希未知；因此 `SOURCE_PRESENT ≠ BUILD_VERIFIED ≠ FLASH_IDENTITY`。

## Target/参与编译文件

- 已在工程中定位 `Project/source/main.c`、`ASW/UsbSvc`、`ASW/BusProxy`、`ASW/PwrCtrl`、`BSW/CDD/USB`、`BSW/EAL/{USB,CAN,GPIO}`、`MCAL/{Can,Gpio}` 等。
- 全部 Target 的条件宏、ExcludedFromBuild、链接/中断表需对照现场 Target 核对；不得因 ZIP 内存在某文件就认定它参与烧录版本。

## 模块职责与依赖

主循环初始化时间/GPIO/双 CAN/USB 后轮询两路 CAN 与 USB 业务。实际运行的软件边界可以表述为 `ASW → EAL → MCAL → MCU`；它是当前组织现状，不是要求未来 HOST 或两板固件都按同样目录重构。

## 主要类型/宏/函数及入口

| 来自 G0-02 的能力 | 当前源码入口（`Project/source/` 下） | 现状级别 | 未形成的证据 |
|---|---|---|---|
| `CAP-M-USB` | `main.c`、`ASW/UsbSvc/ASW_UsbSvc.c`、`BSW/CDD/USB/CDD_USB.c`、`BSW/EAL/USB/EAL_USB.c` | `SOURCE_PRESENT` | 实际 PC 连接/CRC 压力/断线恢复 |
| `CAP-M-CAN-A/B` | `ASW/BusProxy/ASW_BusProxy.c`、`BSW/EAL/Can/EAL_CAN.c` | `SOURCE_PRESENT` | 两路收发性能及错误统计验证 |
| `CAP-M-IGN` | `ASW/PwrCtrl/ASW_PwrCtrl.c`、`BSW/EAL/GPIO/EAL_GPIO.c` | `SOURCE_PRESENT` | J1/DUT IGN 电压、失电态 |
| `CAP-M-SOL` | 同上 GPIO/PwrCtrl | `SOURCE_PRESENT` | 真实阀与机构状态、物理反馈 |
| `CAP-M-ANALOG` | 图纸 P2 存在 DAC/运放 | `NOT_ASSESSED_FOR_COMPLETE_CONTROL` | 是否有完整 DAC 驱动及 HOST 操作需独立审查 |
| `CAP-M-POWER` | 当前源码可能保留名为 PWR 的 GPIO/宏 | `NO_EXTERNAL_SUPPLY_CONTROL_CLAIM` | 不把历史名称等同 DUT 电源可控 |

## 驱动到物理器件的调用链

阅读时先选 `CAP-M-...`，再定位软件使用：USB `CDD_USB_Task`→`ASW_UsbSvc`；CAN 0x40→`ASW_BusProxy_HandleCanSendCmd`→`EAL_CAN_Send`；收到 CAN→`ASW_BusProxy_OnCanRx`→`CDD_USB_SendFrame(0x41)`；IGN 命令 0x12→`ASW_PwrCtrl_HandleCmd`→`EAL_GPIO_Set`→GPIO 端口。链路被源码覆盖，不代表最后外部电压、CAN ECU 响应或工装机械状态真实到位。

## 协议/状态/异常

- 现存 USB 帧：`A5 | SEQ(2 LE) | CMD | LEN | PAYLOAD | CRC16(2 LE) | 5A`；响应另含状态字段。0x40/0x41 传 15 字节 CAN payload（`channel,ide,id4LE,dlc,data8`）；异步 0x41 使用 SEQ=0。
- `EAL_GPIO.c` 的 `s_sigInitLevel` 五项均为 `1U`，旧注释说部分初始低；编译代码与注释不一致。不能据此保证实际外部端口安全初态。
- IGN 映射的**代码表项为 PTC10**，而历史宏/注释出现 `PB19`；P5 高边/线束要与电气事实分别核对。
- 存在旧 `CDD_KeySim` 的历史 `0x100` 路径，不能与当前按键板 0x730 接收协议混同；真实使用应按实际 Target/主控透传链确定。
- USB 发送队列具有丢帧计数变量，性能上界与波形时间质量没有实测结论。

## 现有测试证据与约束

源码文件审计支持“**已看到代码**”，不支持“已在现场同配置运行成功”。`SOURCE_VERIFIED` 可作为 G2/G3 设计输入，但对 IGN/SOL 危险动作的实体放行需现场证据、明确操作安全条件以及负责人许可。所有修复由后续工作包/变更正式决策，本阶段不修改 Keil 工程。


## G1 实务阅读说明（v005）

研究主控功能时，应先从 G0-02 的外部硬件能力与 J1 图纸网络读起，再通过本文件核对相关软件入口；若未取得实际 HEX/ELF、Target 与构建日志，不能把指定源码函数直接称为现役 MCU 中执行的指令。源代码冻结状态不代表板卡能力因此被禁止在 G1 提出新需求。

---

## 固件研发边界纠正
本章“现役固件冻结不改”只表示**当前复验周期暂不修改**，不是项目的长期研发限制。未来 USB/CAN/输出行为若需要调整，可经具体 G3/G4 工作包和现场授权修改主控固件，并对影响的 HOST/按键通信进行回归。ZIP 内源文件存在不等于现场烧录二进制身份；必须独立核对。

## M2 固件迭代身份规则

此处代码仅为当前上传工程与编译路径的静态事实。现场版本映像 SHA、工具链/Target 与真实板卡一致性必须在 B0 确认。两套固件未来允许依据需求和实测设立独立开发工作包，记录源码版本、可复现构建、协议变更和回归；“本轮保持不改”不构成架构永久冻结。
