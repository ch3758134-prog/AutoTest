# AutoTest R1｜统一 TEST 执行定义与资源合同（2026-11-22）

**适用分支：** `R1`；**产品形态：** Windows/WPF、单工位单 DUT、默认极简「自动测试」、独立「开发与调试」；**本文件是工程设计与整改依据，绝不作为终端操作员页面文字。**

> 本文针对 R1 源码现状进行回读归纳。成熟产品对照：NI TestStand Process Models（https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html）、ecu.test Project editor 与 package reference（https://docs.tracetronic.com/help/ecu.test/en/Main-program/Editor/Project-editor.html）、ecu.test Global Mapping（https://docs.tracetronic.com/help/ecu.test/en/Main-program/Global-mapping.html）、OpenTAP Resources（https://doc.opentap.io/Developer%20Guide/Resources/Readme.html）。它们用于比对职责划分，**不是**将别家产品作为 AutoTest 已支持能力。

## 1. 冻结的产品语义

1. 「测试用例编辑器」是界面；`TestDocument` 是**唯一可执行用例定义**。定义包括 Prepare/Test/Cleanup、步骤属性、映射和结果规则。编辑器不可自建第二执行器。
2. `TestProjectDocument` 是**唯一有序自动测试树**，每个 `TestProjectEntry.ItemId` 是一次引用实例；`PackageAssetId` 指向可复用用例定义。同一定义可重复引用。项目条目进入后默认参与完整自动测试；旧 `Enabled=false` 是要保留的历史排除事实，不得静默恢复可能导致物理动作。
3. `RunSelected` 是开发调试**本次临时运行范围**，绝不能覆盖项目持久实例。正式自动测试及准备必须使用 `AutomaticRunEntries()`；同一个调用实例需能在 RECORD 中区别实际多次执行。
4. 自动测试首页和开发调试工作区可以拥有**独立的工程选择状态**，但引用、编译、设备、记录必须复用同一套后端；不能引入「第二套 TEST 项目」或「扫码配方再选一批用例」。
5. 不在自动测试首页展示 V4、研究资料基线、SHA、资产 ID、PR、调试报告、工具栏及可折叠开发证据。普通用户只看扫码对象、测试清单、当前步骤、测量、结果与必要状态。

## 2. 对象与归属

| 对象 | 数据权属 | 不应承担 |
|---|---|---|
| ECU 工程 `ProjectDefinition` | ECU 型号和资源资产目录、修订、依赖 | 操作员手工检查步骤或直接授权设备动作 |
| TEST 用例 `TestDocument` | Prepare/Test/Cleanup、CAN/KEY/UDS/WAVE 判定逻辑、步骤映射 | 固件发布、全工位扫码、全局型号路由 |
| TEST 项目 `TestProjectDocument` | 有序引用、实例身份、完整自动测试范围、后续的项目级流程 | 第二套步骤 DSL、硬件通信实现 |
| 用例实例 `TestProjectEntry` | 当前引用的确定修订、名称、后续专属参数与调用策略 | 修改共享用例本体 |
| DBC | ECU CAN 报文和信号真实结构、单位与枚举表 | 自动产生任何可采纳的实测数据 |
| 波形配置 `WaveSettingsDocument` 和参考目录 | 信号身份、参考数据、采集质量及容差判据 | 擅自把一次实际波形提升为标准 |
| 原生烧录发布 `NativeFlashPublicationStore` | 当前型号发布的工艺、受控固件、安全算法、不可变 Job | 用例内部重复烧录、扫码配置再次选择固件 |
| 工位/扫码 | USB 主控、ECU CAN、KEY CAN、串口参数、扫码原始 ID、实体 KEY、夹具硬件能力 | 测试用例清单、固件和波形的再次选择 |
| 记录 `RECORD` | 真实 ECU、每次 Run/CaseAttempt/StepOccurrence、资源修订、报文/波形证据与报告 | 回写测试定义或用日志文本猜测合格 |

## 3. 各资源严格配合方法

### DBC
- DBC 作为工程资源 `AssetEntry(Id,Revision,Sha256)`，用例通过 `TestTargetMapping` 引用确定报文、帧型、信号、单位、资源修订。
- CAN 原始帧是一级能力，完全可以不绑定 DBC；当选了 DBC 信号时，必须同步解码/编码到**真实字节**。
- 用例运行前核对用到的 DBC 是否存在、内容 SHA、消息 ID/扩展帧、信号名及单位；不因其他未用草稿资源不匹配而意外扩展授权范围。
- 编辑 DBC 后，在开发界面显示引用失效并通过正常保存/明确更新修复；正式执行禁止静默替换旧资源。

### WAVE
- 工具支持真实信号采集/多曲线观察是**专业工具**，与自动 TEST 的波形判定资源不是同一个界面对象。
- `WaveSettingsDocument` 包含确定 DBC 信号、判定阈值和显式批准的参考名称；真实 `WaveReferenceCatalog` 为参考数据提供身份/修订/内容指纹。
- 正式判定不仅要有参考名称，还必须核对信号身份、修订、CAN ID、单位及时间质量，并在物理执行之前**固定唯一匹配的参考 ID**；执行途中不能漂移到同名新参考。
- 只有真实采集且通过质量检查的样本可以被显式提升为参考，缺帧/时间戳不可靠返回无效/不确定，不能被冒充 PASS。

### FLASH
- 当前核实可复用的是 `NativeFlashProfileDocument`、`NativeFlashPublicationStore`、`NativeFlashJobBuilder.Freeze` 和现有真实 UDS/CanTool 参考流程。
- 正式自动测试在每台 ECU 上走**统一前置烧录**，读取工程唯一当前发布版，冻结 HEX、安全算法、APP/BOOT CAN ID、ECU 波特率与 Job 指纹。
- 不在用例里再次设置 Flash 步骤，不把 XML 外部配置或临时编辑草稿当作已发布工艺。独立 FLASH 调试工具仍可单独运行。
- `ProductionPreparationService` 已遵守前置烧录；`ScanRecipePreparationService` 旧配方还要求唯一 Flash 步骤，**属于尚未完全迁移的冲突**，不得将旧配方直接作为正式自动测试指挥源。
- 发布检查绝不等于本次实体烧录授权；USB ACK、硬件状态和完成的 CAN/UDS 证据也应单独记录。

### 扫码/工位/夹具/实体 KEY
- 扫码设备配置是**工位级**：USB COM、串口参数、断线恢复；扫码原文只代表一个唯一标识，不必然含 ECU 型号。
- 型号/编号匹配属于工程目录与绑定规则；型号匹配到唯一工程后，直接读取该工程唯一有效自动测试定义与发布烧录配置，不再创建测试集合的第二份清单。复检不计为新 ECU。
- 主控 ECU CAN = FlexCAN1 / USB 通道 1 / 外接口 A12/A1；按键板 KEY CAN = FlexCAN0 / USB 通道 0 / 外接口 B11/B1。实际波特率受固件 0x42/0x43 设置与查询约束。
- 夹具物理动作能力与工位 IO 绑定，**动作时机、开始/结束/异常流程归自动测试项目**；要支持编辑，不用测试开始/结束写死一个固定动作。
- 用户明确不要求增加独立夹具到位传感器的硬性判断。可根据编辑好的 ECU CAN 报文作为工艺确认条件，但那只能证明**报文到达**，不能伪称机械反馈已安装。
- 主控实体 KEY 与软件按钮都应汇入同一个 START 状态机，禁止重复边沿触发多个运行；这些真实启动接线当前**尚未完整完成**，不能用 UI 按钮冒充。

## 4. 唯一执行流水线

`ScannerIdentity → ModelProject → TEST Project EntryIds → ResourceClosure → Immutable Run Preparation → Start Event → Published FLASH → Test Cases (one executor) → Cleanup/Exception Actions → RECORD → Operator Result`

分三层严格性：

1. **编辑时：** 允许保存不完整草稿，具体提示步骤和缺失资源；不会因为未配置固件就禁止编辑 DBC。
2. **准备时：** 检查**本次实际会执行**的依赖闭包，冻结测试实例顺序、每条用例和 DBC/WAVE 指纹、已发布 FLASH Job；没有真实数据则返回清楚的诊断。允许只读打开不完整工程，禁止运行。
3. **真实执行时：** 校验主控真实连接/ECU CAN 配置、扫码 DUT、单次 START、设备会话、需要的授权，并确认准备快照没有变更。停止/异常清理优先保证设备可追溯状态。最终合格只由真实判定证据产生。

## 5. 本轮 R1 源码落地

| 文件 | 改动 |
|---|---|
| `Desktop/Projects/AutomaticTestResourceContract.cs` | 新建统一只读资源闭包；有序 TEST 实例、TEST 编译诊断、真实使用 DBC 报文信号与单位、波形配置/参考资源的引用检查；可读但未就绪的工程不会隐式运行 |
| `Desktop/Shell/AutomaticTestPlanService.cs` | 默认自动测试导入/恢复改为同一资源闭包，不重复自己筛选一套用例；保留缺陷列表 |
| `Desktop/Projects/ProductionPreparationService.cs` | 真实自动测试执行准备复用同一闭包与 EntryId 顺序；保留现有 Native FLASH 发布与固件/算法冻结 |
| `Testing/Authoring/TestRunStepScope.cs` | `WaveCompare` 使用的 `profile` 纳入实际映射闭包，修正遗漏 |
| `Desktop/Testing/ConfiguredWavePort.cs` | 物理执行前冻结标准波形参考 ID；运行比较拒绝名称、信号或参考版本漂移 |
| `Desktop/Testing/RealTestWorkbenchService.cs` | 将波形冻结检查接入真实 TEST 执行前、硬件实际授权前 |
| `Desktop/Shell/AutomaticTestHomeViewModel.cs` | 显示真实资源缺陷，不再把只读导入或主控连接等同整机就绪 |

**未执行 CI、编译、设备实测；不得把源码修改称为通过。**

## 6. 当前未完成、不得宣传为交付闭环

- 实体 KEY → 统一 START 状态机；扫码 COM 的默认自动连接和型号绑定；夹具可编辑的整体开始/结束/异常流程。
- 正式自动测试整机 Flash+TEST+RECORD 的串行执行与结果可追溯（当前只有分段真实后端和离线准备）。
- 用例调用实例专属参数的编辑及应用；旧扫码配方的物理执行授权路径迁移。
- WAVE 参考在整机快照中的完整存档与重启恢复、发布语义；硬件可靠时间源与现场稳定性验收。
- TEST 的其他未执行 DSL 步骤（例如 Switch/When/异常流）仍按现有能力边界判断，不因为某个 UI 按钮存在就假称可跑。

## 7. 旧工程迁移规则

1. 不生成 V4/资料基线工程，不把旧研发材料写入自动测试首页。
2. 已登记且 `Enabled=true` 的条目参与正式范围；新条目默认参与；旧 `Enabled=false` 保留为排除事实，必须由开发者按项目实际来源明确迁移。
3. DBC/TEST/波形/固件的 ID、修订、SHA 均须保留；不使用自动重写哈希掩盖引用失效。
4. 旧扫码配方可保留供读取参考，但不能同时与新前置烧录模式决定另一套测试与固件；需要进行显式迁移。
5. 对已有用户项目先备份，再由正常编辑器完成引用修复和发布；不能把临时烟雾测试工程自动当作正式型号工程。

## 8. 用户可导入的交付文件

`AutoTest_R1_可导入ECU通讯检查工程.zip`

解压整个目录（不是只抽出清单）。在自动测试界面点「测试工程 / 打开测试工程」，选择 `project.autotest-project.json`。

文件基于用户提供的「绿通通信矩阵.dbc」，其中 `VERSION` 明确写有**来源整理_未实机校验_v1**。只附带一个**只读** `EPB_HeartBit` 真实 CAN 信号到达检查（扩展帧 0x0CFE2750），不包含虚构 HEX、安全算法、标准波形、夹具控制或任何 mock。通过导入仅证明工程结构被读取，不代表完整 ECU 测试放行。

### 现场验收建议（由用户本地执行）

1. 自动测试首页选择解压的 `project.autotest-project.json`，应能看到 1 条真实测试用例，并能独立恢复；开发调试切换不改变此选择。
2. 对未发布 Flash 或缺波形参考的正式项目，要求显示具体未就绪原因，而不是空白、自动补资源或假 PASS。
3. 实际 ECG/DBC 报文与 ID 在真机核对后才运行只读信号判定；不连接硬件不应 PASS。
4. 修改 DBC 后，运行准备应明确拒绝过期 mapping；正式配置修复后重新确认资源。
5. 使用真实波形参考：不同信号或单位、坏时间质量、替换参考快照必须被拒绝。
6. 旧 `Enabled=false` 条目保留排除，前台显示与实际执行范围一致。
7. 真实 FLASH、扫码、夹具和实体 KEY 的整体接线由后续现场集成验收，不在本轮冒称完成。

## 9. 继续迭代边界

从本文件开始应按**同一资源合同**继续在 `R1` 修正运行入口、编辑器资源失效提示和扫码夹具配置，不另建产品分支；不做模拟器替代真机测试。根据用户决定，**不自动启动 CI/编译，Windows 构建、实物运行由用户现场验收**。