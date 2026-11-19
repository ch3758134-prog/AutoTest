# AutoTest｜FLASH 指定 ECU 真实烧录阶段结果与剩余验证登记（2026-11-18）

> **管理对象**：烧录业务与 FlashLab UI 专项，**不扩展至主控固件、USB/通用 ISO-TP 传输实现、CAN 通道硬件事实或整体 G4 Gate**。  
> **记录类型**：专项事实汇报接收 + GitHub 静态交叉检查 + 有限制验收建议。  
> **状态**：`FIELD_REPORT_RECEIVED / REFERENCE_FLASH_ACKED / APP_POSTCHECK_UNVERIFIED / SOURCE_CROSSCHECKED / LATEST_RAW_RUN_NOT_ARCHIVED / PM_GATE_APPROVAL_PENDING`。  
> **专项建议**：`FLASH — 指定 HydEPB ECU 参考烧录 PASS_WITH_LIMITATIONS`，仅限所述 ECU、配置、镜像和安全算法组合，**不是全项目 Gate 通过或跨 ECU 通用 Flash PASS**。  
> **审批状态**：本记录不擅自代替负责人做整个 G4 / R1 准出批准。

## 一、报告来源、源码及独立证据边界

- 2026-11-18 FLASH 专项现场汇报：显示最新一次 FlashLab 截图已达到 80,896/80,896 B 传输、80,896 B FF01 验证和“09 ECU 复位 已完成”；“10 APP 启动/版本”仍为待核验。
- 另一较早轮的原始诊断记录据报告显示第158块 FF01 校验通过、`11 01 → 51 01` 与 `REFERENCE_FLASH_ACKED_POST_RESET_APP_UNVERIFIED`。**较早日志不能充当最新截图对应运行的完整原始日志**。
- 管理 AI 本次**独立核查了 GitHub 源码、分支和 PR 元信息**，未在本轮取得这次截图对应的完整原始日志、Windows Build/Test 原始输出、现场机读镜像指纹；因此不把现场汇报直接升级为全证据复核结论。
- GitHub：`feature/flash-lab-20261114@aafc2099793d2eb3c9b98e375168ff04aed4760d`；[Draft PR #55](https://github.com/ch3758134-prog/AutoTest/pull/55)，当前仍 open/draft、未合并 main。
- 源码静态核查：`src/AutoTest.FlashLab/Programming/CanToolFlashJob.cs`（精确 Profile、镜像对齐及尾签名），`CanToolProgrammingProtocol.cs`（FF00/FF01、34/36/37、74 特例及 checksum），`CanToolFlashWorkflow.cs`（流程、结果、复位与后验边界），`src/AutoTest.FlashLab/Shell/FlashLabWindow.xaml.cs`（09/10阶段显示）。
- PR 描述和旧 F2 文档仍主要引用较早源码候选与“Windows/ECU 待验证”状态；**必须作为历史记录保留，但 FLASH 专项需要更新其交付摘要以准确反映新证据，不能删除原限制**。

## 二、限定适用对象与冻结参数

| 字段 | 报告中的实测 Profile |
|---|---|
| ECU | 特定绿通 HydEPB / CYT2B63 参考样本，不能代表其他 ECU |
| CanTool 配置 | `Configuration.xml`（现场来源，需保存文件 SHA256） |
| APP 诊断 CAN ID | 物理请求 `0x747` / 响应 `0x74F` |
| Boot 诊断 CAN ID | 物理请求 `0x18DA50F1` / 响应 `0x18DAF150` |
| Flash 地址（闭区间） | `0x10008000..0x1006FFFF` |
| 擦除长度 | `0x68000 = 425,984 B`；**独立于数据传输字节数** |
| 对齐块 | `0x200 = 512 B`；非单 CAN 帧或 ISO-TP 流控 blockSize |
| HEX 文件 | `HydEPB_CYT2B63_BMA510_LVTONG_12V_500K_UTV_VCU_20260922_1220.hex` |
| 原始数据 | 79,884 B；原始地址 `0x10008000..0x1001B80B` |
| 计划传输 | 157 个对齐数据块 + 1 个专用 512B 尾签名块 = 158 块 = **80,896 B** |
| 尾块 | `0x1006FE00`，填 `FF`，末尾 `12 34 56 78`；尾块反码累加 checksum `0x04E7` |
| 安全算法 | 已核实精确 SHA 的托管 `SafetyFile.dll` 通过 CanTool 参考内置 LFSR 路径；其它 DLL/ECU 不推断兼容 |

准确 HEX、XML、DLL 的现场 SHA256 **尚未随本次汇报逐项提交到管理记录**，应补齐后才能形成可复现实验包。

## 三、参考流程与成功边界

- APP/Boot 识别、切换和会话：APP `10 03 → 50 03`、APP `11 01 → 51 01`、Boot `10 02 → 50 02`（实际流程允许起始已在 Boot）。
- Boot 安全：`27 01 → 67 01`，`27 02 + Key → 67 02`。
- 擦除整个约定区间：`31 01 FF00 + Addr4BE + Length4BE → 71 01 FF00`。
- **158次**：`34 00 44 + Addr4BE + 00000200 → 74 20 00 00 02 02`；`36 + BSC + 512B → 76 + BSC`；`37 → 77`；`31 01 FF01 + Addr4BE + 00000200 → 71 01 FF01 + checksum`，必须有完整有效校验字段并与本地16位反码累加相符。
- 最终 Boot `11 01 → 51 01`。**此处仅证明 ECU 给出复位请求正响应，不能证明 APP 后续启动成功、版本一致、车辆功能正常**。参考 CanTool 的成功轨迹到此为止；APP 验证是新的独立后验任务。
- 专项指出 APP→Boot 的旧固定2秒等待已移除，首次 Boot 会话未应答只允许一次有边界重试，**不得自动重试擦除/下载/编程副作用**。

## 四、静态源码核查结论与需要保留的差异

1. `CanToolFlashWorkflow.cs` 已按 APP/Boot 两组配置 ID 做切换，按块要求确认 74、76、77、FF01 checksum；最终在收到真实 `51 01` 后返回 `REFERENCE_FLASH_ACKED_POST_RESET_APP_UNVERIFIED`；UI 的阶段09也仅在对应日志出现时标为完成，阶段10保持待验证。
2. `CanToolFlashJob.cs` 为精确 `APP=0x747/0x74F, Boot=0x18DA50F1/0x18DAF150, start=0x10008000,end=0x1006FFFF,block=0x200` 组合自动加入尾签名；存在另一个显式 `legacyTailSignature` 开关，因此未知 ECU 的签名规则必须有单独授权/校验，而非沿用本样本。
3. `CanToolProgrammingProtocol.ParseDownloadMaximum` 只针对字节完全相等的 `74 20 00 00 02 02` 返回 `0x202`；其函数签名**并无 ECU Profile 参数**。对本次 ECU 的特例已由专项实测支持，但在声明通用严格解析前，应由 FLASH 专业确保此例外仅在受支持的 Profile 或经精确批准的场景启用；不能仅靠“其它响应严格”宣称跨 ECU 适配安全。
4. `CheckVerify` 会检查完整 `71 01 FF01` 并要求 ECU 校验值等于本机计算值，而不是只要 `71` 即接受。
5. PR #55 的说明仍含旧版本 Build/现场验证待完成文字、较老 HEAD，且 PR 累计差异多。FLASH 专项需以精确新 HEAD 修订交付说明；不批准合并 main，也不将其扩展为 DEVICE/USB/通用 CAN 代码的系统验收。
6. 当前没有取得最新完整运行的逐块时序/总耗时原始证据，因此不设定新性能达标结论；旧约1.09秒/块只能作为旧运行观察基线。

## 五、尚未关闭的专项问题与责任

| 本档本地编号 | 问题 | 责任/验收输入 | 状态 |
|---|---|---|---|
| `FL-R01` | 复位后 APP 启动、标识和软件版本后验 | FLASH；需明确 DID/标识规则、APP 实际响应及与 HEX 的匹配方式 | `OPEN` |
| `FL-R02` | 最新一次现场试验全量证据与 Windows Build/Test | FLASH；原始 USB/CAN/UDS 日志、截图时间对照、冻结 XML/HEX/DLL SHA、准确 EXE/HEAD | `OPEN` |
| `FL-R03` | 错误终态区分：擦除已执行、部分传输、校验失败、复位无应答、后验未知 | FLASH；真副作用与 UNKNOWN 策略，失败证据不得被覆盖 | `OPEN` |
| `FL-R04` | 74 特例与尾签名严格限于相容 Profile/特定实测条件 | FLASH；代码作用域/预检及新增案例 | `OPEN` |
| `FL-R05` | 全新运行的每块34/36/37/31耗时与总耗时 | FLASH；完整新一轮原始时序，不能拿旧计时推断 | `OPEN` |
| `FL-R06` | FLASH 审计 ZIP/摘要仍有旧只读工具文字 | FLASH UI/审计；文案与实际执行、已确认字节和后验状态一致 | `OPEN` |
| `FL-R07` | PR #55 说明和旧 F2 提交文档与新真实进展不一致 | FLASH；更新候选说明，保留历史来源，不跨专业扩权 | `OPEN` |

## 六、准入与裁决建议（仅 FLASH 域）

- **接受**该样本已报告的真实 FLASH 写入、逐块校验、Boot 复位阶段结果，建议记录为 `REFERENCE_FLASH_ACKED_POST_RESET_APP_UNVERIFIED`。
- **建议**在能对应现场证据的专项评审中，以 `FLASH / SPECIFIC_ECU / PASS_WITH_LIMITATIONS` 登记阶段结果；已通过的是参考烧录核心阶段，不是生产完整合格判定。
- **不通过/不覆盖** APP 后验、跨 ECU 的算法/擦除/签名可用性、最新性能指标、完整错误恢复与主项目 G4 Gate。未核验部分为明确 `NOT_VERIFIED`，不应在UI或报告中写为 PASSED。
- **不因本次结果扩大权限**：本文件不要求修改主控板/引脚、CAN 通道分配、USB驱动、共享传输层，也不准自动复刷、放宽实体写入授权或合并 PR #55。
- **后续正式准出**：负责人应能看到同一次运行的证据绑定（冻结参数/文件 SHA、时间线、块级 ACK/Checksum、复位响应和独立 APP 后验），并明示批准所针对的具体验收范围。

## 七、外部源码依据

- [FLASH 专项分支](https://github.com/ch3758134-prog/AutoTest/tree/aafc2099793d2eb3c9b98e375168ff04aed4760d/src/AutoTest.FlashLab)
- [PR #55](https://github.com/ch3758134-prog/AutoTest/pull/55)
- [CanToolFlashJob](https://github.com/ch3758134-prog/AutoTest/blob/aafc2099793d2eb3c9b98e375168ff04aed4760d/src/AutoTest.FlashLab/Programming/CanToolFlashJob.cs)
- [CanToolProgrammingProtocol](https://github.com/ch3758134-prog/AutoTest/blob/aafc2099793d2eb3c9b98e375168ff04aed4760d/src/AutoTest.FlashLab/Programming/CanToolProgrammingProtocol.cs)
- [CanToolFlashWorkflow](https://github.com/ch3758134-prog/AutoTest/blob/aafc2099793d2eb3c9b98e375168ff04aed4760d/src/AutoTest.FlashLab/Programming/CanToolFlashWorkflow.cs)
