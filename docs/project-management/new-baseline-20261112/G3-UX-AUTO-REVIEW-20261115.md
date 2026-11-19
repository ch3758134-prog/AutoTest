# AutoTest｜G3-UX-AUTO-01 自动测试 IDE 原型审查及 WPF 实施映射 v0.1

**日期：2026-11-15**  
**结论：`ACCEPT_AS_G3_AUTO_TEST_UI_DIRECTION_WITH_GAPS`；不是整个 UI 验收，也不是 G3 Gate 通过或 G4 编码授权。**  
**审查精确依据：** `ui/ide-design@ec75a9fe69626d2a83e0c4ae6b23d187d5ccf5a2`，`docs/ui/AutoTest_UI.html`（Git Blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`）；`docs/ui/UX_DECISIONS.md` 同提交。  
**现行后端静态对照：** `local@a9a26ebfba1efcf032ff8ec8c435336a89469647`；源码静态审查，不代表 Windows WPF/实体 DUT 回归已执行。  
**用户反馈：** 用户已在本轮表示“现在这版的 html 的 ui 我看自动测试是没问题了”，并要求 PM 审查后按该版推进 G3、准备 G4。此语句视为**自动测试 UI 方向认可**，而不是全部专业工具或 G3/G4 阶段审批。

## 1. 本轮审查范围及决定

**自动测试可继承的现行设计**：

1. 左侧工程导航将**测试项目**展开为其有序**测试用例**；资源分组单独放置，无第三个“测试程序”用户概念。
2. 测试项目通过勾选本次选中的用例、显示数量/顺序、在独立栏中维护项目默认启用，具备选择范围预检入口。
3. 测试用例中央高密度步骤表含阶段、嵌套流程分支、步骤名/主要参数摘要/资源/状态；右侧 Inspector 持续提供当前步骤的可编辑详细参数；底部运行时间线/CAN/变量/问题/设备状态为辅助观测。
4. 具有添加后立即编辑、行内少量高频编辑、步骤复制/移动/删除、重命名、搜索、撤销/重做、断点、右键与拖动等交互方向。
5. 独立语义步骤覆盖 CAN 发送、等待 CAN、读取信号、判定、按配置输入动作、IGN/夹具、UDS 服务、波形、变量和 IF/ELSE/SWITCH/WHEN/循环/异常/子程序。
6. HTML 明确把运行演示标为 `NOT EVALUATED`，不宣称与真实设备已接通；这一证据区分必须保持。

**本轮仅认可以上信息架构与交互方向。** 不重新做 UI 概念图、不要求 UI 专项再推倒自动测试页面。独立 DBC/UDS/Flash/WAVE/手动台架的视觉/操作仍由 UI 专项持续研究和用户单独验收。若用户在 G4 Windows 实测发现问题，允许在同一路径继续迭代并按版本重新接入。

## 2. HTML 与实际 WPF/TEST 的映射（这是 G3 必须完成的合同）

| HTML 交互对象 | 现有真实类型/服务 | G3 / G4 的实现要求 |
|---|---|---|
| 当前工程/测试项目/用例 | `ProjectWorkspaceService`；`TestProjectDocument.Packages`；`TestDocument.Phases` | 复用真实资源树、Revision/SHA/ID；不得以 HTML `cases/testProjects` JSON 直接替代 TEST 格式 |
| 用例工程默认/本次勾选 | `TestProjectEntry.Enabled`、`ItemId` | `RunSelection` 为当次内存选择，默认值由 `Enabled` 决定；不能偷偷保存临时选择或改变生产方案；同用例多次引用按 Entry 区分 |
| 准备/测试/清理 | `TestPhase.Prepare/Test/Cleanup` | 逻辑步骤顺序与阶段保留；包级 Cleanup 不能自动撤销项目会话，物理状态处理由 G3-A2/DEVICE 定义 |
| 步骤树/嵌套分支/修改 | `StepNode.Type/Properties/Slots`、`StepEditTransaction`、`StepTreeValidator` | ID 稳定、实际 Slots 映射、撤销/保存/未保存草稿保护；不要重建第二套 DSL |
| 右侧 CAN/DBC 信号编辑 | `TestTargetMapping`、`DbcMessage`/DBC encoder、`TestStepInspector` | DBC 信号物理值有单位/范围/编码验证；必须写入固定版本的目标映射，禁止无提示强制将错误输入转 `0` |
| 等待 CAN / 读取信号 / 判定 | 当前 `CanWait` 尚非可执行内置；`Read/WaitUntil/Expect` 可供复用 | 明确“收到新报文按 ID 等待”“读取/保存变量”“读取并期望判定”的执行合同、超时/数据质量、表达式语法及结果 |
| IF、SWITCH、WHEN、TRY、调用子程序 | `StepDescriptorCatalog` 中 `If` 可执行；`Switch/When/TryCatch/CallSubroutine` 仍未开放执行 | HTML 自由布尔/选择表达式与当前 `variable/equals` 不等价；需在 G3 完成表达式/作用域/分支/异常/调用合同，不让 G4 Codex 猜 API |
| CAN/按键/IGN/夹具步骤 | `Write`、`TestTargetMapping`；`ConfiguredStationActionPort` | 按工程 Controller 配置与真硬件接口绑定，不内置 EPB 业务语义；夹具当前 TEST target 类型/实机接线待 G3-DEVICE 对齐 |
| UDS 服务步骤 | 既有 `UdsSessionService`、`UdsStepHandler`、`UdsRequest` 目前非通用可执行 | 阶段内调用的 SID、寻址、响应、NRC/超时与风险策略必须单独 G3 接口化；不把 HTML 演示当真实 UDS |
| 波形采集/判定 | `WaveStart/WaveStop/WaveAnalyze`、`ConfiguredWavePort` | HTML 单一“采集”应映射清楚启动/结束/捕获范围；质量不足为 Inconclusive；比较结果进入 TEST 的 Verdict 规则按已批 G3-WAVE |
| 项目运行、步骤状态、证据 | `RealTestWorkbenchService` 单包、`RealRunCoordinator`、G3-A2 v0.2 Issue #48 候审 | 项目 Run/Entry Attempt/StepOccurrence 身份、实时事件、统一工位独占/物理授权和记录；不能循环使用旧单包 Run 代替项目 Host |
| 扫码生产运行 | `ProductionWorkflow`/Scan 服务仍存在历史 Mock/Simulation 字段 | 按用户要求扫码匹配+预检满足后自动启动生产方案；独立于工程师临时选择，实体真执行/证据另行审查 |

## 3. 本次发现的明确差异

### GAP-01｜临时勾选初始值（高优先级，局部语义修订）
HTML `currentItems(t)` 使用 `ui.temporary[it.id] !== false`，所以条目在没有显式临时选择时**一律初始勾选**，即使保存的 `it.enabled=false`。G3 明确默认从 `TestProjectEntry.Enabled` 初始化当前选择、当次取消不会修改持久启用。只需改行为绑定，不需重做 UI。

### GAP-02｜HTML 自由参数和现有 TEST DSL 不等价（实施前必须关闭）
HTML `if/when/expect/switch` 使用文本表达式，现有解释器的 `If` 使用 `variable/equals` 等固定属性，Switch/When/TryCatch/CallSubroutine 不具备真实执行。必须由 G3-TEST 专业具体定义“显示参数→正式属性/语法→编译→事实→异常/错误提示”，且保持 HTML 已获得用户认可的操作路径。**不能靠只换步骤名称接入旧后端**。

### GAP-03｜源资源与数据验证（实施前必须关闭）
HTML 是 `localStorage` + 示例 JSON 交互状态；WPF 必须使用真正工程资源、固定 Revision/SHA、TCF、DBC/Controller/UDS/Wave 资产；测试步骤对象不能靠字符串名与 demo resource 隐式匹配。HTML 对 DBC 信号值采用 `Number(v)||0`，真 WPF 不可在非法输入时静默写 0，要保留原输入并显示真实校验错误。

### GAP-04｜项目选择和真实执行（G3-A2 必需）
HTML `run-selection` 做的是本次选测**预检演示**，`run-ui-preview` 只逐步高亮并生成 `NOT EVALUATED`；WPF G4 必须接入真实 ProjectRun Host，而不是把预演代码翻译成 WPF。每个用户测试用例、重复引用、循环步骤必须有稳定身份及实机证据。

### GAP-05｜物理动作范围、波形与 UDS（G3-DEVICE/WAVE/F1 继续）
TEST/设备/UDS/波形后端有已批准技术设计及尚未实现部分。按钮 UI 不代表动作可安全执行；DUT 主电源不属 HOST 控制、KEY_SIM 只是电阻网络输入模拟，CAN1 DUT/CAN0 KeySim，物理 ACL/USB generation/实机响应不能跳过。

### GAP-06｜启动和演示工程（G4 WPF 必须区分）
HTML 默认加载 `makeSample()`（含 EPB 样例），但已有 `makeBlank()`。正式 WPF 启动应以当前/最近工程或空白工程为准；EPB 资源仅作为明确选择的示例工程，绝不能固化为新 ECU 默认输入或判据。

## 4. G3 专项推进与 G4 最小实施切片

**G3 现在可并行收敛，UI 不是阻断项：**
- `G3-A1`：以上 HTML 固定提交作为**自动测试 UI 的当前设计源**；补齐步骤展示/属性/编辑/资源/保存行为到真实 `StepNode`、`TestTargetMapping` 的映射合同。视觉/布局不再全面推倒。
- `G3-A2`：[Issue #48](https://github.com/ch3758134-prog/AutoTest/issues/48) 的项目级 Run 身份/编译/工位会话/清理/事件，并纳入临时选择默认与生产区分。
- `G3-TEST-FLOW`：专项完成 IF/SWITCH/WHEN/TRY/子程序/变量/表达式与自定义步骤真正的执行合同、错误及调试语义。
- `G3-DEVICE/WAVE`：采用已批准真实硬件、证据、三态波形约束，补步骤级接入。
- `G3-UDS/FLASH`：专业工具可继续研究；自动测试 UDS 服务调用/烧录阶段安全边界按接口另审；不因一页 HTML 而授权实体烧录。
- `G3-SCAN`：生产扫码自动触发已定，后端工作流剥离 Mock 残留后才能实机认定。

**G4 准备切片（尚未授权代码）：**
1. `G4-A1-UI-01`：当前 WPF Shell/测试项目/测试用例/Inspector 按接受的 HTML 完成真实工程编辑/选择，不接入新实体命令；用户 Windows 连续编辑实测；
2. `G4-TEST-FLOW-01`：正式步骤类型和条件/异常/变量/嵌套流程编译/执行，保证实际 DSL 与 UI 一致；
3. `G4-A2-00/01/02`：多用例 ProjectRun、资源快照、工位会话、逐步事实和日志；
4. `G4-DEVICE/WAVE/SCAN/UDS`：分别按已有批准技术合同接入真实设备/波形/扫码/受控诊断，进行用户 Windows/台架实测；
5. `G4-RECORD`：跑通真实结果/报告/持久化、失败证据和复测追溯；不能仅凭 UI 模拟宣称生产系统可用。

**验收区分**：HTML 用户认可 ≠ WPF 代码已实现 ≠ Build 通过 ≠ 真实 ECU 测试通过 ≠ G3 Gate 整体准出。任何 G4 工作包须按精确 HEAD、文件责任、测试层次单独批准，且不得因 HTML 合格删除现有 TEST/DBC/DEVICE 真实模型。

## 5. 审查裁决建议

**建议项目负责人确认：**

- **接受** `ui/ide-design@ec75a9fe...` 的自动测试视觉与操作结构作为当前 G3-A1/A2 和未来 G4 WPF 实施设计基准；
- **不批准** 整份 HTML 的专业工具、实体操作、生产服务已经完成；它们仍独立迭代；
- **要求 G3 优先关闭** GAP-01～04、TEST-FLOW 真实执行语义，再申请对应 G4 代码任务；
- 不要求 UI 专项为这些后端差异重新设计页面，只需要必要的状态/校验/错误呈现协调。

本记录属于 **PM 静态源码兼容性审查和实施详细设计输入**，不冒充已执行浏览器自动化、WPF 编译或真实 ECU 实测；UI 用户体验正面反馈来自项目负责人本人。
