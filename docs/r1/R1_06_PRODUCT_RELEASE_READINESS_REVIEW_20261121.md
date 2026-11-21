# AutoTest R1｜成熟工业桌面产品准出复审（2026-11-21）

- **审查对象**：`ch3758134-prog/AutoTest`，`R1@35f40781337c10d50c79cdddefddc6150125b46d`
- **审查属性**：代码路径/交互合同/发布证据审查；**并非** Windows 构建、WPF 启动、视觉走查、真机联调或完整逐行安全审计。
- **结论**：**NO-GO / BLOCKED FOR PRODUCT RELEASE**。可继续作为受控开发源码候选，但不能宣称达到成熟工业桌面产品或 R1 现场整体准出标准。
- **本轮特性**：重新核查前次 AI 修改是否真的贯穿新增、复制、保存、关闭、切换、执行及报告；不沿用先前“已完成”口头结论；把**源码已确认**与**需运行时复现**分开。

## A. 已确认的高风险代码缺陷

### P0-01｜复制 CAN 报文可绕过已失效 DBC 引用保护（源码确定）
- **来源**：`HOST/src/AutoTest.Desktop/Shell/ManualBenchCanRows.cs:33-42, 352-356, 474-489, 541-606`。
- **现象**：工程重新载入保存过的 CAN 报文，匹配不到原 DBC 指纹时，`MarkUnresolvedDbc(pin)` 设置 `_staleDbcPin`，`BuildFrame()` 因该标记拒绝发送。这是正确的。
- **缺陷**：`CopyCanRow()` 仅复制 Id/Data/Extended/Period/Included 和 `source.UseDbc ? source.DbcMessage : null`。过期绑定行处于 `UseDbc=false`，因此副本既不携带 `_staleDbcPin`，也不具有 `DbcMessage`。副本的 `BuildFrame()` 可经 RawCan 分支通过。
- **风险**：用户复制一条被禁止发送的旧帧后，新帧不经过明确确认即可作为原始帧发送。是规则绕过，不只是视觉问题。
- **必修**：复制/导入/粘贴/批量操作均要保留禁止发送原因和冻结引用。失效绑定只能经过显式“确认转为原始帧”或“重新绑定最新 DBC”恢复，记录动作；不仅用 WPF disabled 来保护。添加负向单元测试（过期 DBC 原行 → 复制 → 构造帧必须仍拒绝）、工程 reopen + batch/once 双入口测试。

### P0-02｜全局 WPF UI 异常处理过度宽松（源码确定；危险后果需实机）
- **来源**：`HOST/src/AutoTest.Desktop/App.xaml.cs:21-45`。
- **现象**：除 OOM/AccessViolation 外，绝大多数未处理 Dispatcher 异常在记录/弹窗之后统一 `e.Handled=true`。例如用户曾见 `Index was outside the bounds of the array`，界面会尝试继续运行。
- **缺口**：没有区分只读画面故障与步骤模型/设备状态/写入任务故障，也没有全局 UNKNOWN/fence 联动、故障上下文、恢复路径。
- **必修**：异常分级；在状态完整性无法确认时阻断新的物理写入并结束或确认既有任务；可恢复只读 UI 例外与不可恢复的设备写入异常分开；用户可复制日志但“复制日志”不能替代恢复机制。

### GATE-01｜缺少可核实的当前提交 Windows 构建与测试证据（准出阻断）
- 已检查 GitHub R1 当前 Actions 运行集合，没有该分支对应的运行记录。已有 `HOST/scripts/r1-verify.ps1` 只能说明写了验证脚本，不能证明其已运行或全部通过。
- 没有当前 HEAD 的 Restore/Debug Build/Release Build/Tests/WPF 首启/真实 USB/CAN 数据，**不可标 PASS**；历史编译日志亦不能替代当前 SHA。

## B. 核心功能与一致性缺口

### P1-01｜扫码绑定的项目身份复核不足（源码缺陷；竞态需复现）
- `HOST/src/AutoTest.Desktop/Projects/ProjectScanMatcher.cs:20-45` 异步读取发布配置后只比较 `workspace.Current?.Definition.Revision` 与旧 snapshot 的 `Definition.Revision`。
- 切换到**另一个同修订号**的工程后，条件仍可能成立；此时返回的是旧项目资产匹配结论。当前 `MatchAsync` 自己不发起真实运行，因此不能直接断言已经写错 ECU。
- 应同时冻结并比较 ProjectId、ManifestPath、ManifestSha256、配置版本及实际受用项目，并在真实运行最终授权前再次核实；加入并发切换/相同 Revision 不同项目的合同测试。

### P1-02｜自动保存尚不等于“任何未完成工作都可以安全关闭”
- `CloseDraftGuard.cs:22-49` 和 `TestEditorView.xaml.cs:77-97`：右侧 Inspector 不能应用或新步骤尚未配置有效时关闭被拒绝，而不是先把可恢复草稿独立保存。
- 好处：不会静默放弃修改；坏处：用户在填写中途无法顺畅退出、切工程；不符合现代编辑工具应有的恢复体验。
- 应明确区分**可恢复编辑草稿**和**正式可执行资源**：草稿可自动持久化并继续从上次位置恢复，非法参数不进入正式 TEST/生产执行。保存失败时仍阻止退出并保留内容。

### P1-03｜手动 CAN 配置持久化未成为正式工程资源
- `ManualBenchCanDraftStore.cs:18-19,37-60` 把报文工作表存放在工程目录的隐藏 `.autotest/manual-can-rows.v1.json`，仅保存 JSON，不在主工程资产/修订界面出现。未见此文件的工程导入/导出、备份/迁移、用户管理入口。
- 对开发工作台“方便恢复”有价值，但成熟产品还应提供可见的命名配置、导入导出、版本化/备份和破损恢复；仍禁止将工作表当成物理发送授权。
- 当前实现还存在 P0-01 的复制失效引用问题，不能仅凭已有 JSON 保存测试就宣称完成。

### P1-04｜新增/复制/选择/清除 DBC 辅助存在不同规则
- `ManualBenchCanRows.cs:93-156,474-489`：选择不同 DBC 时可能重新填零 DATA；复制原始模式不继承“明确拒绝 DBC 自动匹配”的编辑意图；一条过期帧复制甚至丢失禁止状态。
- 应通过同一编辑事务管理所有入口，具备编辑前后预览、原始字节不被意外改写、Undo 和回归测试；操作应在右侧 Inspector/浮层提供清晰的“为什么改变了 DATA”。

### P1-05｜自动测试 DSL 的可编辑范围大于真实可运行范围
- `StepUiCatalog.cs:30-42` 明确标注 Switch/Case、When、TryCatch、Subroutine/CallSubroutine、CallPublicFlow、UdsRequest 等未接线，而添加步骤面板仍有相关编辑选项（`TestStepInspector.xaml.cs:129-158`）。
- 安全阻断本身合理，产品行为却不应让操作员配置完后才知道完全不能运行。应建立唯一 Capability Manifest 与插入面板/属性/预检/执行/帮助共用，不再分别写布尔值和文案。
- 正式 R1 的交付范围必须显式区分“可编辑结构”与“真实可执行功能”，不能把 UI 图标视为已实现。

### P1-06｜工具工作流仍不完整
- `MainWindow.xaml:146-148`：UDS 工具及生产运行被禁用。
- `WaveWorkbenchViewModel.cs:11-16` 当前明确是“只读真实波形证据/参考浏览器”，不能在该工具独立完成选择信号、触发真实采集、建立/批准参考、修改判据、复跑与比较。
- `MainWindowViewModel.cs:497-536`：FLASH 工具 USB 所有权移交需要人工先关闭台架；自动化接管流程未收敛成统一状态机。
- 需要真实端到端任务路径，不建议在既有成熟后端旁边再造第二套协议或 TEST 执行器。

### P1-07｜TEST 批量行为与产线执行未衔接
- `TestProjectWorkbenchViewModel.cs:485-533`：任何非 PASS 直接终止后续用例，是安全保守策略，但需在运行前向用户明确，提供“停止/重试/跳过”权限模型与证据（不能仅换按钮）。
- `ProjectScanMatcher.cs` 仅负责配置匹配，不自动测试。正式生产 `ProductionWorkflow.cs:28-90` 为另一套编排合同，仍有 `MockObservation`/SimulationOnly 分支；`ProductionResultCoordinator.cs:52-57` 正确阻止 Mock PASS 充当物理放行。当前不能把二者误称统一生产闭环。

### P1-08｜专业工具窗口管理仍是局部实现
- DBC 改为 owned modeless 并进行修改自动保存（`MainWindowViewModel.cs:1894-1989`），这是改进；但 Shell 手工维护 DBC 字典和计时器、手动台架另一种异步 close、FlashLab 独立 modal 生命周期、波形又一种管理。
- 应建立统一 ToolWindowHost/DocumentSession 契约：单实例、所有者、dirty/version、保存、取消、关闭、项目切换、安全释放、跨窗口焦点恢复；不是所有工具都必须改成主视图文档。

### P1-09｜波形后台加载仍缺少大数据容量治理
- `WaveWorkbenchViewModel.cs:87-129` 已把磁盘读取移到 Task.Run，这是正向改进；但 EnumerateFiles 后 `OrderByDescending` 对整个目录排序再 Take(500)，并非真分页。
- `LoadSelectedAsync` 仅以 generation 拒绝过期界面显示，没有取消已启动的重型投影；快速连续选择大数据时可有多个后台任务并发。
- 需要页式读取、索引/缓存、取消、数据质量状态，波形工作流正式化之后才能做高负载 Windows 性能验证。

## C. 产品体验/维护问题（P2）
1. **视觉组件未统一**：对具体 XAML 的静态计数，`MainWindow.xaml` 51 处直接 HEX 颜色/70 处固定尺寸，`TestEditorView.xaml` 94/60，`TestStepInspector.xaml` 43/23，`ManualBenchWindow.xaml` 45/47；虽然 `Themes/Tokens/Colors.xaml` 已有基础 token，仍未普遍复用。浅色产品应统一按钮/表格/输入框/弹层/状态文本、DPI 和焦点样式，不再逐处截图补丁。
2. **业务状态缺乏共同定义**：Enabled（工程可用）/Selected（本次勾选）/RunSelected，PASS/FAIL/UNKNOWN/设备 ACK 与正式 ECU 证据、资源待核/失效/未启用等词汇及颜色应形成统一可操作状态表。
3. **编辑器逻辑过重**：`MainWindowViewModel.cs` 约 2,100 行、`TestStepInspector.xaml.cs` 约 1,500 行；资源事务、UI Dialog 和硬件接管集中在大型 ViewModel/code-behind 中，增加跨模块回归难度。
4. **重复/废弃代码未移除**：`ManualBenchCanPropertiesWindow`、`ManualBenchKeyActionWindow` 与新 `*View` 同时存在；UiPreview/G4Workbench 及样例 Mock 资产并存。需引用追踪后删/隔离，而不是简单搜索到 Mock 就认定产线有假数据。
5. **错误诊断可复制但未归一化**：`App.xaml.cs:21-44` 可复制调用堆栈，仍没有统一的错误类别、当前任务/RunId/StepOccurrence、可恢复操作提示和设备安全态关联。

## D. 现有代码正确、应保护的设计
- ECU CAN / KEY CAN 物理角色固定，不能把主控 USB 写 ACK 当 ECU 应答。
- `RealRunCoordinator.cs:797-839`：准备阶段阻断、脚本清理、物理 KEY/CAN 清理、WAVE 清理，失败写入错误 fact 并使真实 Verdict 不为 PASS。这是关键正确设计；不能把外层冗余清理 catch 单独解释为“TEST 没有收尾”。
- `RealTestRecordSession.cs:391-500`：Formal PASS 依赖当前 StepOccurrence、源信号/DBC 身份、经归档验证的证据；不会把普通无来源的变量 Expect 自动计为 ECU 正式 PASS。
- `ProductionResultCoordinator.cs:52-57`：Mock/Simulation 结果不会赋予物理 DUT PASS/放行资格。
- `ManualBenchCanRows.cs:659-777`：真实 USB 逐帧确认和有界运行已存在；本轮单调周期调度属于源码改进，但仍待真机波特率/周期/抖动实测。
- 波形参考和原始采集的 immutable evidence/sha 校验、测试期间硬件 lease、DBC revision/SHA 冻结不能为了方便而删除。

## E. 整改优先级与正式 Gate

| Gate | 必须完成 | 验证方式 |
|---|---|---|
| G-A 安全 | 修过期 DBC 复制/导入绕过、异常后的物理禁止态、扫码旧工程身份复核 | 失败/并发单元测试，故障注入，USB/KEY/CAN 现场状态验证 |
| G-B 数据 | 统一草稿恢复与正式资源，资源复制/版本/备份/撤销，文件切工程/断电/损坏恢复 | 持久化/版本冲突/中途退出/重开测试 |
| G-C 核心任务 | TEST 编辑→预检→运行→波形/UDS/FLASH→证据/报告；扫码匹配与生产运行角色明确 | Windows 全链演示、真实 ECU 操作、失败证据 |
| G-D 产品交互 | 一套 UI Tokens、控件、Inspector/工具窗口管理与状态词，1024/1280/1920 宽与 100%/125%/150% 缩放 | Windows 实际截图/键鼠路径/键盘/关闭恢复验证 |
| G-E 构建交付 | 固定完整 SHA，Restore+Debug/Release Build+全量 Tests+WPF 启动+实机 | 日志/用例名/截图/RunId/证据归档，前四 Gate 未过不准合 main |

**用户不是基础软件 BUG 的发现者**。优先由研发在 Windows 自动构建、代码审查、故障注入、持续 UI 回归中发现缺陷，最后集中提交真实 ECU 验收。

**审查限制**：本报告只覆盖实际重新回读和横向核查的代表性关键源码，不宣称仓库 1,045 个文件已全部逐行审计；本次没有使用 Windows 编译器或物理设备，也没有证明可视化结果正常。后续可将每项与修复 commit、测试日志、真机证据绑定再准出。
