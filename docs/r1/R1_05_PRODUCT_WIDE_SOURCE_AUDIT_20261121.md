# AutoTest R1｜成熟工业桌面产品全局源码审查 · 第一轮（2026-11-21）

> 本报告主动从**完整工业产品**角度发现问题，不以用户截图或以往 BUG 清单作为审查范围。原始审查点：`R1@02f18c65e5ba6038da10067c6285450de6cce911`。本轮现场未实施 Windows 编译/启动/真机动作。  
> 状态：**REVIEW_IN_PROGRESS / VERIFIED_CODE_FINDINGS / NOT_WINDOWS_TESTED**。不能把静态审查等同于全仓 100% 逐行审查，更不能宣称现场 PASS。

## 1. 审查基准与方法

- 参照 Windows 官方[设计基础](https://learn.microsoft.com/windows/apps/design/basics/)与[原生行为建议](https://learn.microsoft.com/windows/apps/get-started/best-practices)、[模态与浮层](https://learn.microsoft.com/windows/apps/develop/ui/controls/dialogs-and-flyouts/)；按照工业工位软件要求补充设备安全、证据链、停止/断连和恢复原则。
- 完成仓库全量**文件清单与模块边界盘点**：远端 R1 树约 1038 项、HOST 约 505 项；HOST 中有约 323 个 C# 源文件、47 个 XAML 文件（统计包含测试及专用工具）。这些是文件清单数量，**不是**逐一验证过的代码数量。
- 已逐段回读主 Shell/TEST/PROJECT/台架/DBC/WAVE/FLASH/RECORD/USB 生命周期，以及代表性 UI 样式、测试与解决方案入口。继续扩展各模块的逐文件复查、静态合同测试和 Windows 运行验证；未查全的不写“完全通过”。
- 判定：**CONFIRMED**=源代码可直接证明的问题；**RISK**=具有确定风险模式，需事件时序/现场复现；**GAP**=成熟产品应有但当前交互缺少的能力；**PROTECTED**=现有能力应保留，不能为了视觉重做而回退。

## 2. 主动发现问题（优先级从安全到产品）

### P0｜安全与数据可靠性
1. **P0-01 退出超时后的设备状态无法确认（CONFIRMED path）。** `HOST/src/AutoTest.Desktop/Shell/MainWindow.xaml.cs:131-162` 对 `ShutdownAsync().WaitAsync(6s)` 的 Timeout 仅写 Trace，`finally` 仍调用 `Application.Shutdown(0)`；而 `HardwareConnectionService.cs:845-886` 断开需要等待 USB/桥接/监视任务。这意味着可能在收尾未确认完成时杀掉进程。**修正目标：**退出状态机必须区分设备已释放、停止失败、超时 UNKNOWN；无法确认停止时不能显示“安全退出成功”，更不能把未完成的物理收尾隐去。需要独立压力/断连测试和用户可理解的异常恢复入口。
2. **P0-02 KEY 动作异步保存可能清除后续编辑标志（CONFIRMED，源代码已修待构建）。** `ManualBenchKeyActions.cs:262-266,445-478` 原先在 `_savingKeyActions` 期间忽略 `MarkKeyDirty`，并在落盘完成后无条件将 dirty=false。本轮修为编辑期间 UI 锁定、变更 generation 对照保存快照，解决“修改发生在 await 中但被认定保存”的数据丢失风险。源代码提交 `b08b40942b2eec01876d6b67cebb235a8821020e`，仍需并发合同测试及 Windows build。
3. **P0-03 全局界面异常恢复缺少“设备安全态”门禁（RISK）。** `App.xaml.cs:21-38` 对大部分 Dispatcher 未处理异常显示 MessageBox 后 `e.Handled=true`，主窗口/工位可能继续可操作。需要核实当前是否处于真实写入、资源损坏、状态不可恢复，并限制继续危险操作；不能一律无条件恢复。

### P1｜产品核心路径与功能闭环
4. **P1-01 手动 CAN 报文列表不持久化（CONFIRMED）。** `ManualBenchCanRows.cs:366-412` 每次创建 ViewModel 用 `AddCanRow()` 初始化，行的顺序、ID、DATA、周期、勾选状态仅保留在内存。台架工具关闭再开将重新初始化。成熟产品必须具备工程级/用户工作台级配置保存、恢复、重命名、批量操作及资源版本追踪；不能和 KEY 动作库的已有持久化路径割裂。
5. **P1-02 周期 CAN 实际间隔与标称不一致（CONFIRMED）。** `ManualBenchCanRows.cs:560-592` 用 `await SendManualBatchCanAsync` 后 `Task.Delay(PeriodMs)`；因此实际周期≈USB ACK/队列等待时间+PeriodMs，不能承诺精确 65ms；多帧并发还受共同 USB 锁影响。应使用单调时钟 deadline 驱动、丢周期/滞后统计、实际发送时间戳、平均/最大间隔与停止后的零延续发送验证。USB 0x50 属于 KEY CAN，不可误接到 ECU CAN。
6. **P1-03 波形工具缺少“一站式获取参考/分析”体验（CONFIRMED/GAP）。** `WaveWorkbenchViewModel.cs:11-16,55-102` 当前是同步读取已经存在的波形证据及参考的只读浏览器；采集与建立参考需绕到 TEST 步骤/配置页面。真实比较后端存在，**不应重写**；应从波形工作台可视化引导信号选择→采集→质量→选择/批准参考→比较→证据与测试步骤，缺条件时清楚提示。
7. **P1-04 DBC 专业工作台模态阻挡主程序（CONFIRMED）。** `MainWindowViewModel.cs:1863-1914` 手动创建 DBC Window 并 `ShowDialog`；编辑 DBC 期间主 Shell 不能正常切换，工具与主界面的会话生命周期体验不一致。应采用受管理的 owned modeless 工具窗口 / 工作台文档，并保持资源 Revision/SHA 冲突保护，不建立第二套数据模型。
8. **P1-05 DBC/信号、TEST 参数与 CAN 工具各自数值展示规则不一（CONFIRMED）。** `DbcWorkbenchViewModel.cs:70-91` 仍使用 `ToString("R")` 展示工程数值以便 roundtrip；TEST/手动 CAN 已调整为 G12 展示。产品应区分“底层精确存储/校验”与“UI 按 DBC 分辨率、单位和合法步距展示”，不要在每个页面分别 hard-code G12/G17/R。
9. **P1-06 工程用例修订采用路径缺少完整任务引导（GAP）。** `TestProjectWorkbenchViewModel.cs:343-405,554-592` 依赖显式采用当前 Revision/SHA（安全设计正确），但保存 TEST 后项目可能显示固定旧修订并阻止预检。需要在工程树/项目行呈现差异、冲突原因、影响的用例以及一键逐条审核；不允许静默更新冻结资源。
10. **P1-07 当前项目批量运行缺少可理解的后续执行策略（GAP）。** `TestProjectWorkbenchViewModel.cs:501-533` 默认非 PASS 立即停止整个批次；安全保守是正确的，但用户应清楚知道“停止/跳过/继续”策略、哪些动作有危险、哪些类型允许继续。此功能必须由真实执行器支持，不能只改按钮文案。
11. **P1-08 工位工具切换成本过高（CONFIRMED）。** `MainWindowViewModel.cs:495-533` 进入 FLASH 要求先人工关闭台架、断开主控 USB，FLASH 关闭后还要人工重新连接。USB 独占保护应保留，但成熟产品应设计可见的受控工具会话移交（确认停止→安全释放→工具接管→状态恢复）而非隐式反复操作。
12. **P1-09 波形资源刷新会阻塞 UI（CONFIRMED）。** `WaveWorkbenchViewModel.cs:55-105` 在 WPF 绑定的同步 `Refresh()` 中遍历最多 500 份磁盘证据并读回验证；大工程可能导致界面卡顿。应改异步增量加载、取消、缓存、分页并保持真实性校验。
13. **P1-10 用户界面和复用组件未统一（CONFIRMED）。** 代表性 XAML：`TestEditorView.xaml` 有 94 次直接颜色字面值、58 处固定尺寸，`MainWindow.xaml` 有 51 次颜色字面值及 70 处固定尺寸，`ManualBenchWindow.xaml` 45 次颜色/47 固定尺寸（纯静态文本计数）。需要统一颜色 token、字体、Button/ComboBox/DataGrid/搜索/焦点/禁用/状态与最小 DPI 布局；浅色优先、深背景配白字不是个别补丁。
14. **P1-11 原始步骤配置与运行前检查的可发现性不足（GAP）。** `TestStepInspector.xaml.cs:1297-1355` 保存/校验仅通过提交行为触发；步骤目录中仍有如 CASE Slot “运行前阻断”的选项。应把不可执行、未映射、缺数据、危险操作，提前以逐字段可读状态和“为什么不能运行”展示；不能让操作者跑到预检才理解配置不足。

### P2｜运维、性能与历史整理
15. **P2-01 运行报告缺少真正分页（CONFIRMED）。** `RunReportExporter.cs:44-56` 100,000 条以上直接报错，低于上限时一次加载全部 facts/step facts。大批量现场长期运行会出现导出内存与响应问题。需要分批读取/流式 HTML/CSV/JSON、取消与进度。
16. **P2-02 旧界面与示例 Mock 资产仍留在仓库（CONFIRMED 资产存在，不代表生产代码使用 Mock）。** 旧 `ManualBenchCanPropertiesWindow`、`ManualBenchKeyActionWindow` 与新 `*View` 并存，`AutoTest.UiPreview`、`AutoTest.G4Workbench` 和 `HOST/samples/mock_*` 仍在树中；主 `AutoTest.sln` 暂未包含 UiPreview/G4Workbench，因此不能简单认定运行时引用了 Mock。需按产物追踪引用后删除无效支线、明确测试夹具与可交付发布包的边界。
17. **P2-03 异常诊断只能靠外部日志（CONFIRMED）。** `UiExceptionReporter.cs:10-34` 将完整堆栈写到 `%LOCALAPPDATA%\AutoTest\logs\ui-errors.log`，`App.xaml.cs:21-38` 弹异常摘要。应增加“复制诊断信息、打开日志、关联当前 RunId/StepOccurrenceId/操作路径”的 UI，避免用户重复截屏或寻找路径。历史“Index was outside the bounds of the array”缺少原始堆栈，**不能臆造已修复**。
18. **P2-04 自动测试与工具工作台测试覆盖不均（RISK）。** 已有 TEST/PROJECT/DEVICE/DBC/WAVE 等纯逻辑测试和安全合同测试，但新增的异步关闭、浮层导航、焦点切换、DPI 下控件遮挡、异步保存竞争仍需 Windows UI 自动化、故障注入与真实接口验证。不能只凭编译检查或截图判定成熟度。
19. **P2-05 人机术语和动作分类不统一（GAP）。** `Remove`、`Delete`、`Disable`、`Included`、`RunSelected`、`Enable` 等不同域交错，部分中文按钮不同操作共享词。应统一“从项目移除/移除工程资产/删除磁盘文件（另行审批）”、“默认启用/本次选中”、“单次发送/周期任务/启动/停止”，同时定义状态文案表。
20. **P2-06 专业模块仍各自构建弹窗、筛选、保存调度（CONFIRMED）。** `MainWindowViewModel` 2000+ 行、`TestStepInspector.xaml.cs` 1500+ 行，且 TEST/CAN/KEY/DBC 各有编辑与保存流程。应收敛成应用级统一资源编辑与保存契约、状态/事务通知，以及可复用的 Inspector 和工具窗口管理器，而不是把所有操作继续加进 Shell ViewModel。

## 3. 已确认应该保留的能力
- **不能为了 UI 改掉真实硬件链路**：ECU=外部 A1/A12，KEY=B1/B11，主控 WinUSB 确认、物理权限 Fence、运行任务互斥与失败安全状态。
- **不能放松项目不可变性**：TEST 及 DBC Revision/SHA 固定、明确采纳最新、RUN 前设备/资源指纹冻结。
- **不能抹掉不确定性**：报告 StepOccurrence、原始 CAN、FLASH 和波形证据应保留 PASS/FAIL/UNKNOWN，不能将 USB ACK 当作 ECU 回复。
- **应复用现有质量控制**：手动 CAN 监视 `ManualBenchMonitor.cs:84-120` 使用有界队列及 100ms 批处理并报告 UI 丢失；WaveEvidenceStore 与 ReportExporter 有校验基础，应扩展而非重建。

## 4. 实施顺序与核查 Gate
1. **A · P0 安全收尾**：梳理执行器/台架/USB/诊断/FLASH 的状态机，统一异步关闭、停止、超时、撤权、UNKNOWN 与错误恢复。完成断连/超时/重入测试，Windows 编译/测试。其他 UI 优化不得放宽硬件安全。
2. **B · 全业务链**：工程新建→文件管理/版本→TEST 编辑/选范围→台架/DBC 辅助→预检/运行→波形/UDS/FLASH→日志证据/历史报告→复用工程，逐链做“用户不需要猜下一步”的任务审查，补齐持久化与可发现性。
3. **C · 交互基建**：视觉 Token / 控件体系、宽度/DPI、统一 Inspector/浮层/工具工作台、键盘/右键/搜索/撤销/自动保存提示、错误和空状态。Windows 触控区域/缩放需实测。
4. **D · 工程可靠性**：源代码静态分析和失败注入、并发/取消测试、自动报告一致性测试、清理废弃代码与仅测试使用样例、Release packaging 验证。
5. **E · 一次统一验收**：先 Codex Windows Debug/Release Build + 全量测试 + 启动回归；再用户集中真机验收。任何门禁缺证据写 `NOT_VERIFIED/BLOCKED`，不因 GitHub 代码提交而填 PASS。

### 优先级说明
P0=丢数据、错误设备终态或潜在危险操作；P1=真实任务无法闭环、错误预期/严重交互；P2=可维护性、增长风险和一致性。以上列表是**持续审查的首批发现**，不是最终问题上限。后续按模块加问题索引、源码定位、修复提交、回归用例，直到完成全面检查再进行正式 Gate。
