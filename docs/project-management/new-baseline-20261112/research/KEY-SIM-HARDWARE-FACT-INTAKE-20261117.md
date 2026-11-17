# AutoTest｜KEY_SIM 按键板硬件、固件与物理风险事实接收（2026-11-17）

> **记录类型**：项目管理研究事实及风险登记（非新设计批准、非固件修改指令、非真实操作许可）。  
> **状态**：`FACT_INTAKE_RECORDED / PHYSICAL_NETLIST_PENDING / BENCH_NOT_VERIFIED / DESIGN_DELTA_PENDING`。  
> **关联已批准合同**：`PM-CR-20261116-KEY`（直接 KEY 步骤）、`PM-CR-20261116-PF`（公共流程）、Main_Control 双夹具 USB 改造，以及 2026-11-17 旧 WPF 事实报告。  
> **用户提供来源**：KEY_SIM 专业 2026-11-17 汇报；`KEY_SIM.zip`、`EPB_ODD V1.1` 四页原理图及器件研究。**以下对原理图/功耗的引用依据专业汇报；管理 AI 本次没有独立用原生网表或物理实测复核计算。**

## 1. 源代码检查与版本

本次直接读取收到的 `KEY_SIM.zip` 中三份文件：

- `KEY_SIM/WS/EPB_EOL/Sources/APP/KeySim.c`（47 行）；完整 20-bit 位图、标准 CAN 0x730。
- `KEY_SIM/WS/EPB_EOL/Sources/BSW/IO/Driver_Gpio.h`（92 行）；K19→PTB4、K20→PTB6、K16→PTE2 的 ON/OFF 宏。
- `KEY_SIM/WS/EPB_EOL/Sources/BSW/IO/Driver_Gpio.c`（87 行）；20 路全 OFF 初始化。
- 收到 ZIP 的 SHA256：`13493051f0f8a1c3130a72ad448c35ca8f0179f1bef2a2d1bfdb23f0d634be88`（仅校验这份输入压缩包，不等于现场已刷固件）。
- GitHub 现有 `Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a` 为原有导入分支；没有证据证明它即现场烧录镜像，更未按最新物理疑点修复。

**注意**：`Driver_Gpio.c:65` 的 `Drv_Gpio_AllKeyHigh()` 名称含 High，但函数体依次执行全部 `KEYx_ON()`（宏把控制脚拉 LOW）；不可根据函数名推断“清空/全部关闭”。本次未在 WS Sources 下发现其他调用点；如未来复用须人工核查其实际语义。

## 2. 硬件结构和五路输出（专业调查事实）

- 板：EPB_ODD V1.1；U3 MCU `MC9S08DZ32`，U2 CAN `TJA1042T/3`；U6～U10 为五颗 `DG411DY`，控制 K1～K20 共20个开关。
- 20个可控对象是**电阻网络开关**，最终为5路模拟输出，不是20路独立可设的电压 DAC，也不保证各输出互不影响。

| 模拟网络 | 外部引脚 |
|---|---|
| `RELE_NC` | JP1-4 |
| `RELE_NO` | JP1-3 |
| `PARK_NC` | JP1-8 |
| `PARK_NO` | JP1-9 |
| `MODE_NO` | JP2-1 |

K8/K13 在所检原理图上存在跨输出网络连接；最终网络归属需要原生电气网表，不能直接将五路等同五个互相隔离的子电路。

## 3. 三路供电及模拟输出约束

| 网络 | 角色 | 不得误认 |
|---|---|---|
| `BATIN`，JP2-10 | 板输入，经保护及 MP2451 降压产生 VCC5V | 不等于所有开关/电阻网络激励电压 |
| `BAT_EPB`，JP2-3 | DG411 正供电与 AD_BaudRate 分压来源 | 不能默认为 BATIN |
| `BAT_KEY`，JP2-4 | 模拟按键电阻网络的激励来源 | 输出判定必须依据实际测得 BAT_KEY 和 ECU 输入负载 |

原理图标称反馈推得 VCC5V 约4.95V（仅稳态条件满足时的理论值）。专业示例：假设外部 ECU 输入 10kΩ 下拉，`PARK_NC` 在 BAT_KEY=12V 约5.86V、24V 约11.72V；无负载时理论值可能更接近 BAT_KEY。AD_BaudRate 分压在 BAT_EPB=12V 约1.565V、24V 约3.130V。**这些均为特定等效电路假设下的理论结果，未做 ECU 实际输入、容差、保护或温升验证**。12/24V 稳态可供电不等于任意 24V 支路均可长期闭合，更不代表满足汽车瞬态/负载突降要求。

## 4. 固件控制事实（已直接核查 WS 源码）

| 项 | 事实 |
|---|---|
| CAN | 标准帧 ID=`0x730`，DLC=8（主控 CAN0→KEY_SIM） |
| SET | Data[0]=`0x01`，Data[1..3]=20-bit mask 小端、Data[3]高4位清0，Data[4..7]=0 |
| ALL OFF | Data[0]=`0x80`，Data[1..7]=0；代码执行 `KeySim_WriteMask(0)` |
| 位映射 | K01=bit0；K20=bit19；有效范围 `0x00000..0xFFFFF` |
| 逻辑电平 | `KEYx_ON()` 写控制脚 LOW；`KEYx_OFF()` 写 HIGH |
| 上电代码 | `Drv_Gpio_Init()` 使K1～K20全部OFF |

**重要：**源代码能证明这些调用逻辑存在，不能据此推出现场固件相同、每个 DG411 实际导通/断开、CAN0 已送达 KEY_SIM、5路电压已到位或 ECU 已识别。`0x80` 全断与绿通 ECU 业务“正常无操作 `mask=576`”完全不同；这也区别于编辑器新建步骤默认掩码及连接后未观测的物理状态。KEY 步骤保持完成后不得暗中切换电平（已批准 G3 直接 KEY 合同）。

## 5. 引脚与功率高优先级风险

| 风险号 | 事实/疑点 | 必须取得的证据 | 管理状态 |
|---|---|---|---|
| `KS-R01` | **K19：**MCU 封装 Pin10→PTE3（据四页原理图研究），WS 宏却使用 `PTB4`（`Driver_Gpio.h:86-87`） | 原生电气网表、实装 MCU 封装核对、实物单 Key 输出；确认后再决定固件修正 | `HIGH / UNRESOLVED` |
| `KS-R02` | **K20：**MCU Pin12→PTE5（据汇报），WS 宏却使用 `PTB6`（`Driver_Gpio.h:89-90`） | 同上；不允许只改宏未核原理图/线路 | `HIGH / UNRESOLVED` |
| `KS-R03` | **K16：**实际 PTE2 与固件相符，但原理图网名 `PTB3_9P` 不一致 | 原生网表和焊盘/线路确认，注明是网名历史残留还是连线冲突 | `MEDIUM / INVESTIGATE` |
| `KS-R04` | **24V BAT_KEY 高功耗：**假设 K16/K17/K18 同闭合、DG411 Ron≈25Ω、忽略 ECU 输入/温度，专业理论给出 R28(470Ω)≈**0.91W**；K5/K6/K7 有类似待核情况 | BOM 真实功率额定、封装/PCB散热、实测电流与温升、连续保持极限、供应电压；核网表模型 | `HIGH / REQUIRES_HARDWARE_CLEARANCE` |
| `KS-R05` | K8/K13 跨模拟网络，5 路独立性不足证 | 原生电气网表、5路输出与组合实测 | `MEDIUM / INVESTIGATE` |
| `KS-R06` | BATIN、BAT_EPB、BAT_KEY 独立供电网络，12/24V 接法及状态未核 | 现场线束、各供电电压、ECU 输入负载/偏置和设备版本证据 | `HIGH / PRECONDITION_UNKNOWN` |
| `KS-R07` | 现场烧录版本与 ZIP/分支相同无证据 | 固件镜像哈希、烧录回执/标识、CAN 响应及真实 20 Key 测试 | `HIGH / FIELD_NOT_VERIFIED` |

**不能把0.91W视为已测功耗或直接判定烧毁**：电气等效模型、导通电阻、实际 BAT_KEY、负载、电阻功率额定和热环境均待核；但同样**不能把任何掩码的24V长期保持视为已证实安全**。

## 6. 与已批准产品设计的关系和当前处置

1. 已批准的 **“取消独立按键配置用户功能，直接在 TEST 步骤/公共流程勾选 K01～K20 + 保持时间”**不变；WPF 仍允许任意合法 20-bit 输入、保存、编辑和不破坏工程数据。
2. 将**编辑能力**与**实际能否驱动硬件**分离。当前物理证据不足，不能因为 mask 在范围内即认定在该供电/负载/保持时间下可安全执行；特别是上述高风险组合和 K19/K20，真实执行应进行前置能力/安全判定，未满足时按明确原因 `BLOCKED / NOT_VERIFIED` 处理。
3. 不在现阶段将 `K16+K17+K18`、`K5+K6+K7` **直接写成完整黑名单**、不编造供电电压阈值或最长安全保持时间。这些是已知风险示例，尚需 HW/DEVICE 的完整组合与电气安全分析，形成可审查的、版本/供电相关约束。未通过审查前，不执行已识别的高风险组合的24V持续通电实测；现场实验须有负责人明确批准的安全方案。
4. 只凭主控 USB/CAN `0x40` 成功 ACK，不能声称 KEY_SIM GPIO/5路电压改变，更不能推断 DUT 已识别；KEY_SIM 旧 WS 接口这段代码未显示针对 KEY 命令的独立应用层确认帧。
5. 断电、CAN/USB 断线与 KEY 状态保持、复位、长按收尾语义需要实际固件和硬件验证；HOST 不得为了“安全”擅自把绿通的 `mask=576` 当通用零位，也不得强制自动清零破坏用例含义。
6. 原 KEY 直接步骤合同提到的支持掩码/ForbiddenPairs 必须经**真实硬件与版本证据**产生；不能让执行 AI 根据理论示例自行硬编码安全规则。
7. 不修改此前批准的 G3 正文、冻结 HTML、主控/KEY_SIM 固件或 WPF 源码；本文件仅增加事实与风险追踪。

## 7. 定点后续工作包（待设计/实测，不自动授权写代码）

- **HW / KEY_SIM 固件**：`KS-WP01` 原生网表/MCU 32pin核对与 K16/K19/K20 台架试验，确认每个开关的 GPIO→DG411→JP 连线；`KS-WP02` BOM、R28/同类电阻功耗和 12/24V 温升/电压能力；`KS-WP03` 现场 WS 固件版本识别与 CAN 接收/复位/断线行为。
- **DEVICE + TEST**：`KS-WP04` 在现有唯一设备会话和 TEST DSL 中设计依据硬件型号、供电条件、输出 mask 和保持时间的合法性检查与证据；不建立第二套执行器或另造 KEY_SIM 固件能力。
- **UI + INTEGRATION**：`KS-WP05` WPF KEY 步骤与手动台架显示编辑值、业务无操作值、最近一次命令状态与实际输出/验证状态的区别；清楚显示能力未确认/高风险预检阻断理由。不得伪造模拟电压实测数字。
- **PROJECT**：`KS-WP06` 将硬件/固件版本、供电接法、五路输出验证与约束文档关联到可审核工程资源；旧 ECU 专用参数不变成全局默认。

**验收层级**：`SOURCE_VERIFIED`（可核源代码）→ `NETLIST_VERIFIED`（原生网表/实装）→ `FIRMWARE_IDENTIFIED`（现场刷入）→ `COMMAND_OBSERVED`（总线/接口）→ `ELECTRICAL_VERIFIED`（5路与功率）→ `DUT_VERIFIED`（对应 ECU 正确识别）。未覆盖层级不得标 PASS。

## 8. 交叉引用

- [G3 直接 KEY_SIM 步骤（已批准设计）](../G3-SUPPLEMENTS-20261116/CR-KEY-DIRECT-STEP.md)
- [G3 公共流程（已批准设计）](../G3-SUPPLEMENTS-20261116/CR-PUBLIC-FLOW.md)
- [旧 WPF 真实工程与历史用例调查](LEGACY-WPF-FACT-INTAKE-20261117.md)
- [主控 USB/双夹具接口（已批准设计）](../G3-SUPPLEMENTS-20261116/CR-MAIN-CONTROL-USB-CLAMPS.md)
- 当前 Key_Sim 源码分支：`Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a`，与收到 ZIP 的字节一致性尚未通过 Git SHA/二进制等价比对；本次只独立阅读了收到 ZIP 的 WS 源码，不能据此保证其对应当前现场固件。
