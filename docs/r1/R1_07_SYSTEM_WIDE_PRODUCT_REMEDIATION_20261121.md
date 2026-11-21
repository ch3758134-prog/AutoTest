# AutoTest R1｜全系统成熟产品审查、技术判断、整改及准出矩阵

> **2026-11-21 设计修正（覆盖此前过度安全结论）**：普通 WPF 窗口/编辑异常不属于“实体设备处于危险电气状态”的事实证据。先前 A03/A04 的“任意 UI 异常强制 WinUSB 故障联锁”已在 `R1@295f4ea7f8` **撤销**，保留现有具体 CAN/KEY/IGN/Flash 协议授权和故障处理；本文件中出现的 A03/A04 均是被撤销的历史研究记录，**不是当前有效代码功能**。  
> **SCAN×FLASH 实际接入**：已按专项 `S-07/S-08/S-09` 添加可选 UT1、扫码配方中的 TEST 项目/固件/Flash 配置/波特率、真实 CanTool Job 冻结一致性检查、扫码准备预览与“应用到本次 TEST 选择”。详见 [R1_SCAN_FLASH_CONFIG_INTEGRATION_20261121.md](../r1-scan/R1_SCAN_FLASH_CONFIG_INTEGRATION_20261121.md)。生产统计/实物扫描/版本读回仍待接线。源码候选未通过 Windows 构建。


**基线**：`R1@8812847cd814a344e4c7ed4791fa59bf02304238`（独立准出复审之后）  
**本轮修改 HEAD**：`R1@32fdd3738cef8469b596e446e9ef118edde2d739`  
**日期**：2026-11-21  
**范围**：HOST/Desktop、TEST、DEVICE/WinUSB、PROJECT、DBC、SCAN、PRODUCTION、UDS/ISO-TP、FLASH、WAVE、RECORD、UI/工具窗口、测试/交付。主控和按键板固件、`main` 均不写入。  
**当前 Gate**：**SOURCE_CHANGES_COMMITTED / STATIC_REVIEW_PARTIAL / WINDOWS_BUILD_NOT_VERIFIED / WPF_RUNTIME_NOT_VERIFIED / ECU_NOT_VERIFIED / RELEASE_NO-GO**。

## 0. 判断口径

成熟工业测试产品不是一个集齐了所有按钮的 Demo。每个入口应有真实可执行动作、明确权限/条件、真实数据来源、版本锁定、停止/恢复路径和证据。优先保持以下不变量：

- 一台 PC、一工位、一台 DUT；主控 USB、ECU 外部 A1/A12、KEY 外部 B1/B11。ECU CAN 0x42/0x43 运行时码率读回；KEY CAN 不随 ECU 码率变化。
- **一个真实 TEST 执行器**：`RealRunCoordinator` + `RealTestWorkbenchService` + `RealTestRecordSession`；不能造第二个 Mock 生产执行器顶替。
- 硬件 ACK ≠ ECU 响应；FLASH Boot ACK ≠ APP 复位后通过；原始 CAN/DBC/波形/FLASH 证据缺失时不得正式 PASS。
- 工作配置/项目引用/正式可执行资源/一次性实体授权属于**四种不同状态**。可以自动保存编辑配置，但绝不自动恢复实体授权或启动 CAN/IGN/KEY/FLASH。
- 操作者能直接回到 TEST；DBC/UDS/波形等为独立专业工具，但普通属性编辑属于 Inspector/当前工具内浮层。单击工程树即可编辑；编辑无需记住多个窗口和滚动目录。

### 参考架构（研究背景，非 AutoTest 现有源码）

NI TestStand 的 process model 把 UUT 身份、运行入口、测试报告和日志纳入公用执行流程，而非每个测试用例各自实现；Sequence Editor 与 Operator Interface 面向不同操作角色。参考：
- https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-user-interface-development-best-practices.html
- https://www.ni.com/en/support/documentation/supplemental/06/a-guide-to-developing-effective--maintainable-teststand-sequence.html

微软 Windows/WPF 建议对导航、可访问性、DPI 和小窗口内的滚动做一致性设计；不代表 WPF 自带完整 Fluent 控件：
- https://learn.microsoft.com/en-us/windows/apps/get-started/best-practices
- https://learn.microsoft.com/en-us/windows/apps/design/accessibility/designing-inclusive-software
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout

## 1. 核查方法与边界

本轮重新读取 R1 的 Git Tree：约 1046 项，HOST `src` 有 249 个 C# 文件与 44 个 XAML 文件（包含历史 UI 预览模块）。形成全仓模块清单，集中回读真实安全/数据/编辑调用链；对本轮变更进行 SHA 回读、C# 花括号静态平衡检查和两张主窗 XAML 结构及事件处理函数核对。**这不是对全部 293 个文件完成逐行语义证明**，也不是 Windows 真机验收。未通过的门禁不写 PASS。

## 2. 本轮实际修改的代码

| 编号 | 技术问题 | 修改文件及决定 | GitHub commit | 代码状态 |
|---|---|---|---|---|
| A01 | 过期 DBC 引用复制后丢失限制，原始 CAN 可绕过 | `ManualBenchCanRows.cs` 新增 `DuplicateForEditing`：连同 `_staleDbcPin`、显式 RAW 意图复制；源/副本均要求操作者显式解绑或重绑 | `e10d69b070` | 已提交 / 负向测试待运行 |
| A02 | 扫码匹配不同工程共用 Revision 时返回旧结果 | `ProjectScanMatcher.cs` 冻结并检查 ProjectId、ManifestPath、Revision、SHA，在异步载入后及最终返回前复核 | `e10d69b070` | 已提交 / 同修订工程测试待运行 |
| A03 | 未处理 WPF 异常可能允许继续实体操作 | `PhysicalCommandFence`/WinUsbByteLink/HardwareConnectionService/App/Shell 新增故障锁存，撤消既有授权、拒绝所有新的物理 OUT；需明确断开和新连接 | `dc5bfc36f4`, `daa6680834`, `47a2f8070b` | 已提交 / 底层测试待运行 / 电气状态 UNKNOWN |
| A04 | USB 重连握手可能抹掉并发故障锁存 | `HardwareConnectionService` 用 Fault Generation 确认连接全过程无新故障，才允许解除旧联锁 | `4bd223f173` | 已提交 / 并发验证待做 |
| A05 | 手动台架退出时后台保存尚未结束就 Dispose | 统一由异步关闭门禁等待正在运行的保存；无改动也不允许跳过收尾 | `89e7fe2a67` | 已提交 / Windows UI 待验 |
| A06 | 手动 CAN 草稿只保留最新版本，无可恢复备份 | `ManualBenchCanDraftStore` 原子覆盖时保留 .bak，提供只读验证的备份加载 API；坏文件不能自动获得发送权限 | `89e7fe2a67` | 已提交 / 文件测试待运行 |
| A07 | TEST 周期与台架周期用不同计时合同 | `AutoTest.Core.MonotonicDeadline` 统一单调时钟 deadline，过期跳周期、不补发突发；TEST 与台架共享数学合同 | `e5a9e13ad5` | 已提交 / USB 实际抖动未验 |
| A08 | 主窗口/台架底部控件小且主题不一致 | 调整主窗状态区可读性，手动台架底栏使用浅色主题 token | `c8ca6c72e2`, `187d481b69` | 已提交 / DPI/实际排版待验 |
| A09 | 手动 Ctrl+S、自动保存和关闭保存可能并发使用 Revision/SHA | `MainWindowViewModel` 全部路径复用 `_editorSaveGate`，保护同一编辑事务 | `32fdd3738c` | 已提交 / 并发 UI 回归待验 |

**新增/扩充测试源码**：
- `HOST/tests/AutoTest.Desktop.Tests/Shell/ManualBenchCanDraftStoreTests.cs`：旧 DBC 副本绕过阻断、完整 RAW DATA 保留、CAN 文件前版备份读取
- `HOST/tests/AutoTest.Desktop.Tests/Projects/ProjectScanIdentityTests.cs`：相同 Revision 的不同项目无法复用扫码匹配
- `HOST/tests/AutoTest.Devices.Tests/PhysicalEmergencyInterlockTests.cs`：故障锁存后既有和重新发放的物理授权都不能发生写入，诊断只读可访问
- `HOST/tests/AutoTest.Desktop.Tests/Shell/PeriodicCanDeadlineTests.cs`：手动与 TEST 调度共用同样 deadline 计算

## 3. 面向成熟产品的全模块准出矩阵

| 产品域 | 当前源码基础 | 必须补齐的用户任务 | Gate 状态 |
|---|---|---|---|
| Shell/导航 | 浅色 IDE 工程树、文档标签、工作区、状态区 | 统一打开/关闭/焦点/快捷键/错误提示；全部窗口生命周期；异常后明显的设备故障状态与恢复 | PARTIAL |
| PROJECT 工程资源 | 单 ProjectWorkspaceService，资产修订 SHA、编辑会话 | 工程复制/改名/移除、备份/迁移、项目锁定、失败恢复；显示资源依赖和可执行性 | PARTIAL |
| TEST 编辑 | Prepare/Test/Cleanup、Inspector、分类步骤面板 | SWITCH/WHEN/异常/子程序/公共流程真实可运行；新步骤未完成草稿可关闭重开；撤销、选中/禁用/断点一致 | BLOCKED |
| TEST 执行 | RealRunCoordinator，准备/清理，单步/用例/项目批次基础 | 所有 UI 声称可运行的类型必须与 PlanCompiler/真执行器一致；跳过/运行到/停止/失败原因可追溯 | PARTIAL |
| DEVICE/主控 USB | 固件真实协议、一次性物理 fence，CAN0/1 角色定义 | 断线/ACK 超时/重入/UNKNOWN 故障注入；异常锁存后重启连接验收；维护实际设备信息 | PARTIAL |
| 手动台架 | CAN 发送/监视、KEY 动作、IGN/夹具部分入口 | CAN 表成为可见工程资产；发送任务周期实测、停止零续发；所有输入属性一致、快速切换/无遮挡 | PARTIAL |
| DBC | 编辑器/实时解析、冻结映射、数值精度保护 | 直接 HEX、完整 DATA、信号辅助同屏；非法编辑预防、批量编辑、修订采纳向导、可视化错误说明 | PARTIAL |
| WAVE | DBC 时间序列、证据 sidecar、WaveCompare TEST 步骤 | 单窗口选择信号→真实采集→质量→保存/批准参考→比较→追溯报告，不依赖跨多页绕行 | BLOCKED |
| UDS | UdsClient/ISO-TP、序列/文档后端 | 正式 HOST UDS 实体工具、可保存服务序列、NRC/取消、独占 USB/诊断交互 | BLOCKED |
| FLASH | CanTool 流程后端、真实授权与 APP 后验 | UI 文件导入/配置/进度/CAN 原始报文全部可用；与手动台架/UDS USB 独占移交形成单流程 | PARTIAL |
| SCAN | 扫码枪输入与配置匹配后端 | 规则发布、编号唯一性与重复检测、编号→测试程序/DBC/配置映射、无扫码开发运行和生产入口分层 | BLOCKED |
| PRODUCTION | ProductionWorkflow/Release policy 源码，但菜单未启用 | 一次 UUT 身份→冻结配方→TEST/FLASH→RECORD→结果/复测/数量；不把独立 MockObservation 当真实结果 | BLOCKED |
| RECORD | StepOccurrence、原始证据、SQLite、HTML/JSON/CSV，正式 PASS 证据门禁 | 数据检索/筛选、报告分页/流式导出、失败证据一键诊断、断电/崩溃恢复和备份 | PARTIAL |
| 视觉/无障碍 | 有部分 tokens 与浅色主题 | 全局控件、色彩/禁用/焦点/数据密度、100/125/150% DPI、缩放下无遮挡与键盘导航 | BLOCKED |
| 工程发布 | HOST.sln、纯逻辑回归、Windows 门禁脚本 | Windows Debug/Release、全量测试、WPF 启动、无 mock/安装包、持续关键路径测试、实机证据 | NOT_VERIFIED |

## 4. 关键产品架构决策

**统一工程/工位/执行身份，但不把全部工具硬塞进一个页面**。

1. **研发/调试工作区**：工程树 + TEST 编辑/运行文档 + 右侧 Inspector；支持手动台架和各专业工具。调试运行不强制扫码；没有 Production 放行资格。
2. **生产运行工作区**：扫码/手选真实 DUT 身份 → 已发布配置 → 预检 → 单次运行 → 失败与复测 → 完整记录 → 生产放行。复用 TEST/DEVICE/RECORD，不引入第二真实 TEST DSL/执行器。
3. **设备会话管理**：只有一个当前会话控制者；独立 FLASH/UDS 的 WinUSB 独占必须通过可视化移交合同，禁止后台周期 CAN 与 FLASH 并行。
4. **唯一文件编辑合同**：本地可恢复草稿 ≠ 已发布正式资源；后台保存/显式保存/切换/关闭同一异步写门禁；Revision/SHA 变化只能显式采纳。
5. **统一步骤能力清单**：一份机器可读 Capability Manifest 同时驱动“添加步骤”、Inspector、预检、真实执行器与帮助；缺少真实后端的动作不得以“可正常运行”呈现。
6. **证据模型**：RunId→CaseAttemptId→StepOccurrenceId→DBC/CAN/WAVE/FLASH immutable artifacts→最终报告。PASS、FAIL、UNKNOWN 和取消应各自有完备证据与解释。

## 5. 安全边界 / 非功能质量要求

- **禁止 Mock 进入真实工位**：样例和纯逻辑测试中的模拟/回放可留测试域，但真实 DEVICE/RECORD 禁止假 DUT 通过。
- **UI 未处理异常**：本轮主 WinUSB 新增物理授权联锁；但是 FlashLab 的独立工具会话、已进入 native write 的竞态和现场设备电气状态仍必须实测，不能宣称硬件已处于安全态。
- **性能**：CAN 监视接收有上限及批量 UI 刷新；波形后台投影已存在但不是页式目录索引；报告一次载入所有 facts，未达到大数据稳定标准。
- **失败可恢复**：坏配置保留原始证据和上一版备份；工程/DBC/TEST 的错误不应逼用户删除工程重新来过。
- **终止与退出**：应终止所有自有工具窗口、停止实体任务、保存草稿、释放设备；超时 UNKNOWN 不应强制宣称安全退出。
- **操作可理解性**：界面不出现面向开发人员的硬编码提示与重复状态；禁用、未安装、未配置、待现场核对分别给出“原因/下一步/安全后果”。

## 6. 研发整改顺序（不分包要求用户验收）

**Gate A / P0 安全与数据**：完成本轮 9 项修复的 Windows 编译、静态测试，补 FLASH 专业会话异常联锁、UI crash/USB 断连中物理出站门禁、保存竞争与配置恢复的运行测试。

**Gate B / 真实操作闭环**：统一步骤 Capability Manifest，完成剩余流程控制和公共流程调用；把 WAVE、UDS、SCAN/Production 与真实 TEST/DEVICE/RECORD 集成；所有 UI 按真实执行能力准入。

**Gate C / 现代 WPF 产品体系**：完成 UI Tokens/控件重建、左右布局/DPI、单窗口 Inspector/工具工作台、文件/删除/版本/Undo/自动保存/异常恢复的一致操作，不保留旧窗兼容僵尸代码。

**Gate D / 可验证交付**：固定唯一 SHA，`HOST/scripts/r1-verify.ps1` 在 Windows 上先真实跑 Restore、Debug/Release Build、全量 Tests；收集完整首次失败和修复；WPF 全路径键鼠启动走查；实机 ECU CAN/KEY/IGN/WAVE/UDS/FLASH/RECORD 分项事实，最终由用户一次集中验收。

**当前阻断清单不能被省略：** 未完成 UI 完整重建、未接通 UDS/Production、波形工具缺完整操作闭环、流程控制部分不可执行、缺 Windows 及 ECU 证据。本轮改动任何一项都不可以因为 GitHub 静态提交就计成 PASS，`main` 不合入。

## 7. 整改沟通与版本控制

- 只在 `R1` 开发；此阶段 GitHub 源码为单一交付中心，不提供覆盖源码 ZIP。
- 审查报告为可追踪的技术判断，源文件补丁、测试文件、Windows 日志、RunId/证据和正式验收结论分别存档。
- 下一轮代码检查不以用户截图为起点，而以**完整的用户任务链、错误/异常分支和设备生命周期**为审查对象。
