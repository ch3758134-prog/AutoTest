# AutoTest R1｜UI/UX 全系统自主质量评估与整改报告

> 评审日期：2026-11-24
>
> 审查基线：R1 @ 99618d180bc3b5aa35bc579d2281cfdaea8f13f8。
>
> 本轮实施：仅 GitHub R1；正式管理档案 Project_Management **只读**；main 未修改。
>
> 证据等级：**GitHub R1 源码读取、XAML/ViewModel 静态核对、设计/操作路径推演**。本对话 **未运行 Windows 编译、CI、WPF 鼠标键盘操作、任何 USB/CAN/ECU 实物测试**。评分是保守的研发静态审查评分，不是用户验收分。
>
> 设计依据：R1 的 R1_OPERATOR_PATH_AUDIT_AND_REMEDIATION_20261123.md、R1_PRODUCT_INTEGRATION_CONTRACT.md、R1_MINIMAL_CONFIGURATION_BOUNDARY.md、R1_FULL_PRODUCT_OPERATION_UX_REMEDIATION_20261124.md；对照 R7 全产品 UX、R7.1 串口扫码、R7.2 生产 TEST 列表的研究文本及 Project_Management README/G3/G4 Gate 记录（后二者不是本轮新批准）。
>
> 受控要求：不新建分支、不改业务 DSL、FLASH/DEVICE/SCAN/RECORD 真值合同、不造假状态、不把编译视为实体准出。

## 1. 最终结论先行

**初始 46/100（不合格）→ 第二轮 63/100（研发可用候选）→ 第三轮 66/100（研发可用候选）。** 三轮均为静态审查评分；不把源码改动自动等同于实际改善，不设没有依据的满分。最终 UI 设计交付门槛 90 分**未达到**；生产物理 START、完整生产真实 Inspection/证据/报告及部分独立专业工具后端仍阻断。任何阶段均不得记作 UI_PASS、PRODUCTION_PASS 或 GATE_PASS。

发生实质变化的项目：生产页面不再列出 COM 配置、独立工程验证及软件 START；将完整 TEST 树作为核心监视；开发整组实测从生产入口移入工程工具菜单，重新经过真实扫码/硬件冻结；对现有 RealStepFact 使用正式 StepId 做 UI 投影而不是用日志名字猜测；主控自动预检和流程/测量更清楚；FlashLab/Wave 及部分手动台架的高 DPI/密度问题进行实际 XAML 修复。未实现的正式执行状态不显示 PASS、进度、真实数据或隐含完成。

## 2. 统一 100 分尺度

| 维度 | 权重 | 初始 | 二轮 | 三轮 | 终轮扣分与剩余必要措施 |
|---|---:|---:|---:|---:|---|
| 产品信息架构 | 20 | 8 | 12 | 13 | -7：生产正式业务链未接通；ProductionInspectionView 等历史平行呈现仍待受控合并/删除；工程配置部分页面旧入口尚存 |
| 操作流程效率 | 20 | 8 | 12 | 12 | -8：全套工具仍有历史配置、重复编辑与项目/文件操作；UDS 真机调试未接通；大量业务路径没有真实键鼠测时 |
| 信息表达与状态反馈 | 15 | 6 | 9 | 10 | -5：增加生产主状态与扫码错误优先级，已隔离开发验证状态；正式 START/Flash/Inspection 尚无事实源；个别工具质量数据呈现不足 |
| 空间与布局 | 15 | 7 | 10 | 10 | -5：生产/Flash/Wave 有适配；手动台架 CAN 在非常窄时可能横滚；1366×768/150% 未有真实截图证据 |
| 视觉规范与一致性 | 10 | 6 | 7 | 7 | -3：全局 Button/Input/DataGrid 令牌改进；FlashLab 有局部独立主题，历史弹窗各自颜色/行高仍不统一 |
| 编辑与交互细节 | 10 | 5 | 7 | 7 | -3：TEST 添加步骤密度、授权折叠、CAN 行双击、DBC 连续编辑改进；大规模 Undo/焦点/窗口复测缺实机 |
| 异常与恢复体验 | 10 | 6 | 6 | 7 | -3：真值、保存/关闭边界得以保留；生产扫码失败有独立错误反馈，设备断线/中断/数据冲突仍需 WPF 实测 |
| **总计** | **100** | **46** | **63** | **66** | **未达到 90 门槛** |

### 2.1 证据粒度及评分注意事项

- “达到标准”的表述仅针对**源码层可确认的某一具体功能**，不推断控件实际渲染、焦点、输入行为、硬件响应。
- 动态工具/项目内容不会因为出现控件或 DataBinding 就被标记为已通过整机测试。
- 单击、双击、TAB、F2、Ctrl+S、自动滚动和 DPI 重排的 WPF 真实性仍需指定的 Windows 人工验证；本文“操作步数”是设计路径估算，不是屏幕录像统计。
- 模型真实源严格分层：UI 输入意图 ≠ 主控 ACK ≠ ECU/夹具观测 ≠ TEST 步骤判定 ≠ 正式单台 Inspection 结案。

## 3. 全 R1 界面与入口清单

本轮从 R1 Git 树盘点 HOST/src/AutoTest.Desktop、HOST/src/AutoTest.FlashLab 的 **约 50 个 XAML 页面/弹窗及主题字典**，分批回读各视图的布局、命令、动作和数据入口；核心主线进一步回读 ViewModel、XAML code-behind 与相关领域接口。**这不是启动后的动态窗口清单**；正式运行时的当前工作区是 AutomaticTestHomeView，不是代码库里另一张 ProductionInspectionView。

| 领域 | R1 实际入口与 WPF 页面 | 用户自然操作路径 | 检查结论 |
|---|---|---|---|
| 外壳 | Shell/MainWindow.xaml(.cs)、MainWindowViewModel.cs、MainWindowViewModel.Production.cs | 启动→上次型号工程→生产；全局管理进入工程；工程文档标签/菜单/工具 | 已核对真入口、命令和关闭；顶部工程师入口由大按钮移入“管理”菜单 |
| 工程与文件 | Projects/NewProjectDialog.xaml、ResourceNameDialog.xaml、Shell/Navigation/ProjectExplorerView.xaml、Shell/ProjectOverviewView.xaml、ProjectOverviewInspector.xaml | 创建/打开→工程树单击→选择资源/编辑→保存/恢复 | 工程资源入口仍较多；保留当前工程唯一有序 TEST 项目；不可制造第二正式生产方案 |
| 检测准备 | Shell/ProjectConfigurationOverviewView.xaml、ProjectConfigurationOverviewInspector.xaml、ProjectBarcodeExpectationView.xaml、Projects/InspectionStationProcedureEditorView.xaml | 当前工程→扫码规则→原生 Flash→夹具/IGN→运行前资源检查 | 总览重复卡片/按钮已缩减；扫码设置严格位于工程侧，不出现在生产页 |
| ECU 通用配置 | Configuration/ConfigurationWorkbenchView.xaml、ControllerEditorPane.xaml、ConfigurationInlineEditorView.xaml、Configuration/KeyMaskPickerDialog.xaml | 配置资源→KEY 网络/信号及状态编辑→应用保存 | 存在与原始 20-Key 工业定义混淆风险；改动不得替换已冻结原生 KEY_SIM 合同 |
| TEST 项目 | Testing/TestProjectWorkbenchView.xaml、TestProjectSelectionInspector.xaml | 编辑有序用例/插入/顺序→开发临时勾选→检查→仅开发运行所选 | 删除重复的“确认所选”入口，开发勾选不影响正式生产全量用例 |
| TEST 编辑器 | Testing/TestEditorView.xaml、TestStepInspector.xaml、DbcReferencePicker.xaml、Themes/Controls/PreviewEditor.xaml | 选用例→插步骤→配置 RAW CAN/DBC/KEY/WAVE→保存/撤销→单步/用例调试 | 步骤菜单与属性焦点保留；本轮收紧步骤选择器和身份/Flash 授权空间 |
| TEST 调试 | 同 TestEditorView，MainWindow “运行时间线 / CAN 事件 / 变量观察 / 问题” | 指定范围→运行前检查→实测步/例→STOP→查 StepOccurrence/报告 | 真正命令仍需 WPF/ECU 实测；不能以工程验证代替生产 Inspection |
| 生产实际主视图 | Shell/AutomaticTestHomeView.xaml(.cs)、AutomaticTestHomeViewModel.cs/.Run.cs/.Scanner.cs、AutomaticTestPlanService.cs | 设备自动就绪→扫码→状态门禁→实体 START（未接）→前置烧录→全部 TEST→记录（未接） | 重构为任务优先布局；实际 STEP 状态可接入工程真实事件，但正式生产尚待后端适配 |
| 生产旧研究视图 | Shell/ProductionInspectionView.xaml(.cs)、ProductionInspectionViewModel.cs | 非当前主生产入口 | 单独保留且代码较完整，需明确历史去留；绝不能只修改旧页冒充修复主界面 |
| 手动台架 | Shell/ManualBenchWindow.xaml、ManualBenchCanPropertiesView/Window.xaml、ManualBenchKeyActionView/Window.xaml、Hardware/CanMonitorView.xaml | 独立打开→CAN 多行定时/单发→KEY 保存组合点击运行→IGN/夹具→右侧/下方 CAN RX | 真硬件主控服务保持；CAN 行缩窄、双击属性可用，狭小尺寸尚缺量测 |
| DBC | Dbc/DbcWorkbenchView.xaml、DbcMessageEditDialog.xaml、DbcSignalEditDialog.xaml | 独立文件或项目资源→报文→信号高密度编辑→校验/保存 | 保留此前哈希冲突保护与行内别名/高级列；未逐物理 DPI 验证 |
| UDS | Shell/UdsWorkbenchWindow.xaml(.cs) | 无工程打开→编辑服务序列→持久化→加载真实来源回放→核对请求/响应/NRC | 目前是**离线序列与回放工作台**，不是可实时发送诊断的成熟 UDS 终端 |
| Flash | AutoTest.FlashLab/Shell/FlashLabWindow.xaml(.cs)、NativeFlashProfileWindow.xaml(.cs) | 独立工具→原生配置/固件/算法→主控连接→真实阶段/进度/CAN 证据 | 本轮重排窄屏；旧 XML/JSON 路径仅作为历史兼容入口，不应变成生产必选 |
| Wave | Shell/WaveWorkbenchWindow.xaml(.cs)、WaveWorkbenchViewModel(.Capture/.Workspace).cs、Wave*PickerDialog.xaml | 无工程打开已有证据或工程 DBC 在线观察→多轨采集→参考/判定 | 当前源码支持独立归档查看；无工程实时 CAN 和某些质量联动仍阻断 |
| Record | Shell/RecordHistoryWindow.xaml(.cs)、RECORD 现有 Query/Exporter | 任意时刻打开→选择已有库→按 ECU 身份/模式→多次结果→报告 | 多真实数据库查询来源已接，但全工厂统一索引与单台正式生产闭环缺失 |
| 扫码硬件诊断 | Shell/SerialScannerWindow.xaml、SerialScannerSetupView.xaml | 开发/工具→COM 串口枚举→设备绑定/换设备→实测扫码 | 设备设置不属于普通生产页面；唯一 DUT 身份不可被重新扫码重复计新增 |
| 控件主题 | Themes/Tokens/Colors/Metrics/Typography.xaml；Themes/Controls/Buttons/Inputs/Tables/Navigation/Panels/PreviewEditor/CanSignalEditor.xaml | 全工作区复用 Fluent 浅色按钮、下拉、表格、焦点态 | 本轮对基础密度统一，局部窗口仍覆盖默认值；不存在全局已验收视觉基线 |

## 4. 完整场景评审（非正式操作计数）

以下场景分数是**按该场景单独打的静态诊断分，不是汇总 100 分的算术分项**。标记为“已改代码”的步骤仍不能标记 Windows 已通过。

| 用户场景 | 基线 | 本轮第三轮 | 实际源码路径 / 成本 / 影响 |
|---|---:|---:|---|
| 产线操作：待机→扫码→身份→START→Flash→全 TEST→结案 | 34 | 59 | 正式路径仍因物理 START/夹具事实/统一 Inspection **BLOCKED**；页面去掉鼠标设置和假工程验证，提高状态主次但不增加真实生产成功能力 |
| 工程师：创建工程→用例→DBC/KEY/CAN 编辑→运行→保存 | 53 | 64 | 启动恢复工程、单项目及编辑能力已有；精简选择器、运行授权折叠，仍有项目检查/标签/执行身份切换操作负担 |
| 手动台架：多个周期 CAN→KEY 组合→IO→实时 RX | 55 | 61 | 组合 CAN ID/DLC + 数据优先、保留单发/停发、KEY 不自动释放；窄布局仍可能横滚或折叠，真机 STOP/双击待验 |
| DBC：选报文→连续修改大量信号→中文别名/位布局→保存 | 58 | 58 | 本轮沿用已提交的 DBC 字节级与表格功能，无新的实测证据，不人为加分 |
| UDS：独立编辑序列→连续请求与响应/日志 | 32 | 32 | 离线数据和回放已存在，但实体 ECU 服务序列仍缺真实可用 UI 通路；此场景**阻断** |
| Flash：配置→固件/安全→开始/停止→进度/原始 CAN | 42 | 55 | 实际 FlashLab 窄屏分上下监视、宽屏并排，保留原主控/算法/证据，不能因布局变更推断烧录成功 |
| Wave：多信号采集→参考→质量→比较判定 | 46 | 56 | 工具栏改为多行、自适应，已有真实证据和多轨画图；缺无工程在线源及实物采样质量回归 |
| 记录：同 ECU 跨尝试→失败步→证据/报告 | 54 | 54 | 沿用之前多库筛选及 HTML/JSON；没有新生产 Run 事实或统一良率，不因 UI 本轮无更改而加分 |

### 4.1 工程师完整操作路径与已知摩擦

1. 主程序加载上次工程；从全局“管理→进入开发与调试”切换；无工程时创建 ECU 型号工程并校验唯一身份字段（ProjectWorkspaceService 和 NewProjectDialog）。
2. 在工程树打开唯一正式 TEST 项目；插入多个有序用例、保存固定资源引用。*项目中定义与开发调试临时勾选必须分离*。
3. 导入/绑定 DBC，CAN RAW 数据直编是核心，DBC 仅辅助；20 Key 的组合/保持时间保持持久语义；标准波形/信号参数引用需固定修订。
4. 在 TEST 步骤列表插入、双击编辑、撤销、保存；“运行此步骤/用例”和开发范围运行来自现有真实 TEST 执行器。普通开发调试不依赖生产扫码；包含危险 Flash 步骤仍须单独授权。
5. 当前工具栏保留直接运行与 STOP；较少用的 DUT 身份/Flash 授权可展开；异常提示必须保留非法草稿和当前滚动/焦点。**无 Windows 事实说明焦点一定稳定**。
6. 打开记录工具按真实 DUT 查 StepOccurrence/导出；不得以“已执行步骤”直接推断生产 PASS。

### 4.2 工具操作路径与独立性

- 手动台架：不建工程即可使用真实主控硬件；新增 CAN 报文行→双击属性→字节/DBC 辅助→启动多个独立周期发送/单发→停止；KEY动作一键点击、双击编辑，等待结束维持最后组合，纯 WAIT 不发帧。生产、工具和 TEST 的设备权限仍由实际后端统一。
- DBC：从工具菜单打开独立 .dbc，批量修改中文别名与参数，检查异常并保存；当前工程内的 DBC 仍由工程资源和固定修订管理。不能绕过项目版本保护。
- UDS：无工程可用已有服务序列编辑/保存和真实导入记录回放。**不存在已经验收的实时 UDS 发送/取消/日志闭环**。
- Flash：独立 FlashLab 原生参数、文件及设备绑定、真实 UDS 阶段与 CAN 证据；此次只重排空间，不调整 CanTool 烧录算法或硬件链。
- Wave：无工程可打开归档进行真实历史分析/参考比较；工程 DBC 下可在线观察/记录；检查多信号时间轴、采样质量和比较结果需实机实测。

### 4.3 产线操作路径与状态设计

- WAIT：检查主控 USB/ECU CAN 回读/COM 串口扫码，默认用户无需鼠标进行任何参数配置。
- SCAN：真实扫码 → 工程既定期望匹配 → 固定本次 DUT 及资源指纹；重复扫码只新增观察，绝不自动新增出厂台数或触发复检。
- BLOCKED：缺工程、无 DBC/烧录资源、设备断线、扫码拒绝、实体 START 不可观测时清晰显示，不能运行 PASS 动画。
- FORMAL START：实体开始输入及夹具到位必须有真正来源；**目前待接线**，UI 不提供软件启动主按钮。
- FLASH：每一次检测（包括复检）强制原生前置烧录；**尚没有正式生产完整可验闭环**。
- TEST：工程唯一有序项目中的全量用例运行，中央保留用例/步骤行，正式 StepOccurrence 后续要接统一真实生产记录来源。现有工程验证的 RealStepFact 只是开发证据。
- FINAL：本台 Inspection 合格/不合格/未知及完整报告与结果停留，需真实 RECORD 结案；**当前均不能宣称已实现生产 PASS**。

### 4.4 RECORD 操作路径

1. 工具全局“记录与报告”，无需当前 ECU 工程即可打开。
2. 自动定位当前工程真实 SQLite 或选择多个既有库，输入唯一编号、模式查询。
3. 结果应保留同 DUT 不同尝试、所属工程、时间与最终归档状态；异常/未提交不可合并成成功。
4. 选中某次真实记录打开对应源库报告，HTML/JSON 导出保留原始事实。还缺正式 Inspection、批量良率及完整自动跨工程记录索引。

## 5. 各轮问题发现和源码修改

### 第一轮：全系统基线（46/100）

**P0/UI-F01** 当前产线主视图 AutomaticTestHomeView.xaml 包含“扫码枪设置”“工程验证（非生产）”、配置/状态文字和固定三栏，生产人员无法迅速区分日常检测与工程开发。**影响：** 工位指令错误、状态误解、焦点分散。

**P0/UI-F02** AutomaticTestHomeViewModel.Run.cs 只把 StepBegan/StepReported 转为字符串事件，状态投影没有按照 StepId 更新中央 TEST 步骤；操作员看到“执行中”，但不一定知道是哪一步。**影响：** 失败定位慢且容易误识别步骤。

**P1/UI-L01** FlashLabWindow.xaml 1080 DIP 最小宽与原三栏刚性尺寸，使 1366/150% DPI 可用工作区受限；Wave 65/63px 固定单行命令栏容易溢出。**影响：** 核心按钮或数据区不完整。

**P1/UI-C01** ManualBenchWindow.xaml CAN 行包含独立 Name、ID、DLC、状态和三操作按钮，DATA 数据列被挤压。**影响：** 无法快速核对连续发送的 8 字节真实报文。

**P1/UI-B01** TestEditorView.xaml 步骤选择器 810px、双列描述反复占位，顶端完整运行身份/Flash 授权长期展开。**影响：** 高频插入/编辑需移动视线并占用列表纵向空间。

**P1/UI-A01** ProjectConfigurationOverviewView.xaml 使用重复检查按钮及介绍卡片，常用扫码/Flash/夹具入口被大面积固定文案隔开。

**P1/UI-V01** 全局按钮最小高 25、表格默认行高 42、输入高度 27/29，密度和焦点不完全统一；Flash、UDS、WAVE 均有局部样式覆盖。

### 第二轮：实际整改（63/100）

| ID | 变更范围 | 用户可见变化 | 依据 / 未验证边界 |
|---|---|---|---|
| F01 | AutomaticTestHomeView.xaml(.cs) | 原生产首屏改为当前阶段/真实工位提示/ECU 编号 + 完整 TEST 监视 + 实测详情；去掉生产区扫码 COM 和工程按钮；窄屏将详情移至列表下 | 源码确认；未验证 1920/1366/DPI、长用例 |
| F02 | AutomaticTestPlanService.cs、AutomaticTestHomeViewModel.Run.cs | 保留正式 StepId；真实步骤触发才更新行状态、当前测量/判据；运行中主动展开，STOP 中断标记未知 | 原 RealStepFact 真实事件；不把 Completed 推断为 STEP PASS 或生产 PASS |
| F03 | MainWindowViewModel.Production.cs、MainWindow.xaml、AutomaticTestHomeViewModel.Scanner.cs | 工程整组实测移入工程工作区“运行”菜单；由工程师显式准备并重新扫码后运行，仍需原硬件许可；生产没有软件 START | 真实安全和资源检查仍是原后端；无新生产执行模型 |
| L01 | FlashLabWindow.xaml(.cs)、WaveWorkbenchWindow.xaml | Flash 窄屏配置左、烧录阶段右上、CAN 右下；Wave 命令工具栏可换行，减轻固定横宽约束 | 纯 UI 更改，尚无 Win10/11 DPI screenshot |
| C01 | ManualBenchWindow.xaml | CAN 数据优先，名称/ID/DLC 归并；行内动作保留“启/停”和“单发”，双击或右键编辑属性 | 不修改 CAN/KEY 发送；极小窗口仍需实测横滚 |
| B01 | TestEditorView.xaml、TestProjectWorkbenchView.xaml | 步骤选择器标题优先、搜索和 ToolTip；身份/FLASH 授权折叠，运行/STOP 一直可见；删除同一检查命令的重复按钮 | 不改变 TEST DSL 和运行授权；焦点/Tab/双击需 Windows 验收 |
| A01 | ProjectConfigurationOverviewView.xaml | 移除第二重复“重新核对”/空介绍卡，工程摘要与状态对齐、配置项缩窄 | 原真实工位设置及资源管理命令未删除 |
| V01 | Themes/Controls/Buttons/Inputs/Tables.xaml、Metrics.xaml | 默认按钮 30px、输入 30px、表格行高 36px、表头 38px，维持浅色和真实告警色 | 局部特殊控件未全部归一 |
| Q01 | 所有本轮改动 XAML | 回读 XAML 事件绑定，七个主视图标签结构扫描未发现闭合错误；原生服务未改 | **静态扫描不是 WPF XAML 编译，不证明资源/绑定/视觉无错** |

### 第三轮：第一次使用者反向审查（66/100）

不沿用第一轮“修复就算通过”的假设，按初次使用者的生产和工程身份审查：

1. **新发现 R3-F01：** 上次“工程整组实测”的完成、Flash、Step 状态在切回生产时仍可能通过 VM 绑定显示为当前产线状态，即使事实是开发调试真实发生。修复：生产显示采用严格正式事实投影；实体生产未运行时统一显示“未执行 / 尚无正式 Inspection”，不引用旧开发运行。涉及 AutomaticTestHomeViewModel.cs、AutomaticTestHomeView.xaml。
2. **新发现 R3-F02：** Scanner 断线、条码拒绝与普通“请扫码”文案混杂。修复：ProductionAlert 只突出真实拒绝/断线资源原因，其余正常指引由 ProductionGuidance 展示；移除重复长扫码文本，禁用把“工程实测已就绪”当作生产就绪。涉及 AutomaticTestHomeViewModel.cs、AutomaticTestHomeView.xaml。
3. **新发现 R3-A01：** 生产标题栏单独的“开发与调试”大按钮仍像产线主操作。修复：移到 AutoTest 全局“管理→进入开发与调试”，工程开发者有路径，产线正常操作屏中不出现配置/编辑按钮。无工程师账号/权限门禁，不虚称实现了访问控制。
4. **新发现 R3-F03：** 大项目中真实当前 StepId 虽有状态，但不一定在可视范围。修复：跟随执行默认开启，实际 Cases 集合发生运行状态变化时自动定位活动用例和步骤；可关闭自动跟随查看旧事实；无执行或新数据则不滚动。涉及 AutomaticTestHomeView.xaml(.cs)。
5. **反向未关闭 R3-C01：** 手动台架极窄窗口仍有 CAN 数据横滚风险，KEY/监视总体布局尚缺真实 150% DPI 检查。**扣分保留**。
6. **反向未关闭 R3-B01：** 已有完整步骤右键/双击/浮窗，但复杂流程控制子树与多窗口焦点仍需真机/Windows 操作；快捷键覆盖并非全部一致。**扣分保留**。
7. **反向未关闭 R3-G01：** 记录源可以人工加入多库并核对报告，但自动跨工程发现、真实批量统计和正式单台 Inspection 尚未打通。**扣分保留**。

因此第三轮只对架构、真值区分和异常解释合计加 **3 分**，没有主观把 Flash、UDS、RECORD 因页面“更漂亮”而加分。

## 6. 全局视觉和交互一致性合同与结果

| 检查项 | 要求 | 源码结果 | 证据等级 |
|---|---|---|---|
| 背景 | Fluent 浅色/白底，非黑底/深蓝底 | 全局 Canvas/Panel 为浅色白底；生产主视图重新采用 #F5F7FA + White | 源码 |
| 主要动作 | 主操作明确，同一任务不重复按钮 | 生产不再有软件 START/COM 设置；测试项目重复检查按钮删除 | 源码 |
| 文字 | 正文字色能读，注释不淹没真实数据 | 生产首屏“当前状态/身份”优先；波形/步骤选择器冗余解释减少 | 源码 |
| 行密度 | 表格可滚动、连续编辑，不固定大卡片 | 全局 DataGrid 36px/38px；生产 CASE43px/STEP37px，原真实层级保留 | 源码 |
| CAN DATA | 完整有效字节是主信息 | 手动台架以 DATA 作为弹性主列；报文 ID/DLC 合并，仍可查看 HEX/完整属性 | 源码；高 DPI 未验 |
| KEY 保持 | 保持时间结束不自动释放组合 | 未修改 KEY_SIM 的持久真实合同；现有 KEY 相关状态仅称 ACK 请求，不称五路实测电压 | 先前代码 + 本轮未动 |
| 正式生产状态 | ACK 不等于机械到位，开发事实不等于正式生产 | 生产实际数据使用独立未接线正式状态；真实扫码拒绝可以解释，禁止假 PASS | 源码 |
| 输入键盘 | 选择/双击/F2/Ctrl+S/撤销/焦点一致 | 主入口/TEST/DBC/手动台架已有部分接线，本轮保持；仍需 WPF 焦点/IME 验证 | 部分源码，未验 |
| 浮窗与窗口 | 不能外部点击隐式保存或丢草稿；主窗退出清理 | 复用已有 DBC/TEST 明确提交与实际子窗口关闭保护，不改底层设备释放 | 旧源码 + 静态复核 |
| 多屏与 DPI | 1920×1080、1366×768、100/125/150% | 生产、Flash、Wave 有动态布局/Auto 行，手动台架之前已调整；仍缺实测截图与可操作断言 | 源码，**未验** |
| 异常恢复 | 错误时显示真实失败/下一步，不抹掉非法编辑 | 生产提醒资源/扫码真实阻断，原编辑器保留非法草稿和错误提示；真实磁盘冲突待测试 | 源码，**未验** |
| 无工程工具 | 工具无需创建假 ECU 工程 | 手动台架、DBC、Flash、波形历史、UDS 文档及回放可独立进入；波形独立在线/UDS 实体未接 | 源码 |

## 7. R1 文件变更范围

相对基线 99618d180bc3b5aa35bc579d2281cfdaea8f13f8，完成的本轮源代码修改涉及下列路径（列表以 R1 compare 结果为准，正式报告本身是额外新增文件）：

- Shell/AutomaticTestHomeView.xaml(.cs)
- Shell/AutomaticTestHomeViewModel.cs、.Run.cs、.Scanner.cs
- Shell/AutomaticTestPlanService.cs
- Shell/MainWindow.xaml
- Shell/MainWindowViewModel.Production.cs
- Shell/ManualBenchWindow.xaml
- Shell/ProjectConfigurationOverviewView.xaml
- Shell/WaveWorkbenchWindow.xaml
- Testing/TestEditorView.xaml
- Testing/TestProjectWorkbenchView.xaml
- Themes/Controls/Buttons.xaml、Inputs.xaml、Tables.xaml
- Themes/Tokens/Metrics.xaml
- AutoTest.FlashLab/Shell/FlashLabWindow.xaml(.cs)

额外保留来源：R1 先前提交过的 DBC/UDS/波形归档/Record/TEST 后端实现**不是本轮凭空新增**，不得重复记为本轮成果。

## 8. Windows Codex 回归验收合同

未经用户另行授权，当前 AI **不执行**以下测试；由本地 Windows 验证流程执行后，将真实截图、日志、错误行号或设备证据回填。建议逐项记录实际点击次数、键盘 Tab 次数、时间、滚动、窗口切换次数，并以失败优先。

| 用例 | 实际操作 | 通过标准 |
|---|---|---|
| UI-01 | Windows Release 编译 R1 HEAD | 0 错误；所有 XAML 资源能解析；编译日志原样归档；源码检查不得替代 |
| UI-02 | 1920×1080/100%、125%、150% 启动生产并观察无工程/无设备 | 页面不含 COM 设置/CAN 参数/TEST 编辑/开发 START；没有模拟成功；阶段明确 |
| UI-03 | 1366×768/100%、125%、150% 缩放生产窗口 | 测试树高度可用；状态区移动下方；不覆盖任务栏；行文字与失败状态可读 |
| UI-04 | 真实 COM 扫码：合法、重复、错误型号、设备断开、工程资源变化 | 顶部主状态和拒绝原因明确；无自动 Flash/夹具/软件 START；重复扫码不计新台 |
| UI-05 | 在工程开发菜单准备整组实测→真实扫码→完成硬件准备→明确授权 | 受控开始; 工程运行的 StepId、ACTUAL/EXPECTED/ERROR 正确，回生产不残留开发 PASS |
| UI-06 | TEST 多用例、多层 IF/WHEN/循环，编辑/撤销/保存、双击、断点、单步、按需展开授权 | 原步骤位置正确；合法保存、非法阻断；焦点与快捷键稳定；源 DSL 未被暗中替换 |
| UI-07 | TEST 添加 15 个不同类别步骤并连续编辑 RAW CAN、DBC 信号、20Key 保持/纯等待 | 不必滚动长类型列表反复配置；CAN 完整字节能读；结束保持不误释放 |
| UI-08 | 新建工程、修改型号、TEST 项目顺序、关闭/重启自动恢复 | 唯一型号工程/有序用例；真正资源指纹、保存一致；没有第二生产选择开关 |
| UI-09 | 800/910/1150/1440 DIP 宽度打开 FlashLab，核对阶段与 CAN 收发列表 | 窄屏右上阶段/右下 CAN；宽屏三栏；停止/日志/真实进度不遮挡，FLASH 服务行为不变 |
| UI-10 | Wave 在 820/980/1420 DIP 下开合工具、选择多个信号、观察/采集/参考/对比 | 多行工具栏可用；真实采样质量/证据独立、断线可见；假数据为零 |
| UI-11 | 手动台架宽/窄屏同时发送多 CAN、KEY 点击/双击编辑、IGN/夹具、CAN RX | 数据完整可见且可停；KEY ACK 与物理五路电压未混淆；退出停止周期任务 |
| UI-12 | DBC 无工程打开，连续改中文别名、倍率、起始位、非法参数、取消/保存/Undo | 高密度表格可用；非法原始文件结构不被无声破坏；工程/独立文件保存边界清楚 |
| UI-13 | UDS 无工程打开，编辑/保存/回放真实 JSON 诊断序列 | 响应/NRC 来源明确“回放”，不提供伪实体发送；实时会话仍标 BLOCKED |
| UI-14 | 原生 Flash 配置与旧 CanTool XML 区分，导入真实三文件并核对 | 生产不用外部 XML 必选；配置持久化/证据无伪造；烧录只能现场授权 |
| UI-15 | RECORD 按同一 ECU 多次尝试、多工程、多模式查询，导出 HTML/JSON | 不重复计新增、每次原 Run/事实/证据独立可追溯；报告出处不交叉 |
| UI-16 | 正在保存 DBC/UDS/Wave/TEST 途中切工程、关闭主窗、磁盘冲突/拔 USB | 失败保留草稿/证据，不冻结窗口、不丢失 USB 所有权、不假称状态完成 |
| UI-17 | R1 所有主要页面以首次工程师/产线身份操作，不参照旧缺陷清单复测 | 自主新增缺陷、点击/滚动/焦点/反馈视频、评分闭环且不隐瞒未验项 |
| UI-18 | 全部正式生产链真实扫码、机械夹具、实体 START、每次 FLASH、全 TEST、单台 Inspection 和报告 | **当前预期 BLOCKED**。只有现场真实数据闭环和专业验收才能准出；不得用工程实测代替 |

建议每个结果写明：Build/Static/WPF/Hardware 四层证据等级、具体工程 SHA、窗口尺寸/DPI、预期与实际、失败步骤、截图路径和整改责任。

## 9. 仍未关闭的阻断与优先顺序

| 优先级 | 缺口 | 涉及页/模块 | 为什么不能交付/后续动作 |
|---|---|---|---|
| **P0** | 正式生产 START/夹具实际到位证据/独立单台 Inspection 全流程 | AutomaticTestHomeView、ProductionPreparation、硬件/RECORD | 没有正式生产物理事件/结案事实；不可显示生产 PASS。交执行链专业研究及真机验收 |
| **P0** | 真正生产失败定位和可钻取的原始 StepOccurrence 及完整报告 | AutomaticTestHome、RecordHistory、RECORD | 当前开发 StepId 事实不能替代正式生产 StepOccurrence；需正式产线 Record 联动 |
| **P1** | 实体 UDS 工具连贯服务交互 | UdsWorkbenchWindow / UdsSessionService / CAN 设备独占会话 | 现有独立 UI 仅离线编辑回放，不是实时 ECU 诊断终端；需安全授权的后端会话 |
| **P1** | Wave 无工程实时源及独立波形判定质量边界 | WaveWorkbenchWindow/VM | 离线归档可用；无工程在线 CAN 信号需要独立绑定/质量来源，不可用假映射 |
| **P1** | 手动台架超窄宽度 CAN 数据列横向滚动 | ManualBenchWindow.xaml、ManualBenchWindow.xaml.cs | 数据/按钮同时可读需 DPI 上位机实测与进一步行编辑方案 |
| **P1** | 工程/配置/DBC/旧生产视图概念尚有重复，未受控移除 | ProjectOverview、ConfigurationWorkbench、ProductionInspectionView | 静态审查发现平行旧 UI，不应仅凭未被当前入口引用就删除；先查动态打开与存档迁移 |
| **P1** | FlashLab、波形和嵌套工具在 1366/150% 实际布局未知 | FlashLab/Wave/主窗口 | 尺寸重排有源码，尚无截图、焦点、滚动测试 |
| **P1** | 全局按钮/输入/对话框一致性尚不完整 | Themes + FlashLab + 某些独立浮窗 | 局部 Style 覆盖、中文字体和密度需继续全窗口审查 |
| **P1** | 异常恢复/自动保存并发情况缺人机测试 | MainWindow/TestEditor/UDS/DBC/Flash | 未验证磁盘权限/外部修改/最小化/切换时是否阻塞或遗留物理动作 |
| **P2** | 正式批次统计、自动跨工程报告索引和审核视图 | RecordHistory | 多库手动选择不是完整工厂记录发现体系；不得虚称已闭环 |

## 10. 质量准出判断与继续规则

- **最终评分：66/100，等级：研发可用候选。** 不是设计交付候选，不符合 90 分目标。
- **准出检查：FAIL/BLOCKED。** 已触发“核心产线真实路径未完成”“未接线专业工具”“未经过 WPF 常见 DPI 验证”“正式记录缺闭环”等多个否决项。
- **代码与验证：R1 源码已提交；BUILD=NOT_RUN；WPF_HANDS_ON=NOT_RUN；USB/CAN/ECU=NOT_RUN；CI=NOT_RUN。** 不把静态无错、代码 commit、Gate 历史记录当作本轮通过。
- **后续自主迭代顺序：**（1）本地 Windows UI-01/02/03 等真实证据→消灭编译与控件实际问题；（2）完成剩余 UI/状态接线及单页高频操作→再盲测；（3）等待正式生产物理与 RECORD 合同接通后，重新校准生产 UX，达到 90+ 并完成独立验收。不可依靠主观提分。
- **项目管理边界：** 本轮报告只保存在 R1。Project_Management 分支的现有 Gate/项目管理证据作为只读上下文，本轮没有创建管理 Gate 或修改批准记录。

**报告的源码变更 SHA，以 GitHub R1 当前提交历史、基线对比和交付时所述 HEAD 为准；报告内的“未运行”事实为本次对话确实未自行执行的测试。**


## 11. 报告形成后追加的独立代码复审（不人为提分）

- **R3-F04，已提交源码：** 在 AutomaticTestHomeViewModel.Run.cs 发现一种新 UI 性能问题：若每条真实 RealStepFact 都完整复制当前用例的所有嵌套步骤节点，长用例的多次 StepOccurrence 会导致大量 WPF ItemsControl 刷新和选择/滚动跳变。本轮改为只替换真正发生变化的节点及祖先，未受影响节点保持相同引用；最终运行完成时还用实际 RealRunResult.Facts 核对最后事实，避免调度器上晚到的 StepReported 更新丢失。**这只保证源码中的数据相关性，不证明 WPF 真机运行流畅；必须 UI-05 / UI-17 复测。**
- **R3-L02，已提交源码：** 在 FlashLabWindow.xaml 新增本地浅色按钮、键盘焦点与紧凑 DataGrid 定义，与 Desktop 主要视觉基线接近；在 FlashLabWindow.xaml.cs 显式引入 System.Windows.Controls，避免自适应布局新增 Grid.SetRowSpan 的缺少命名空间编译问题。**没有以这一点给视觉维度额外加分**。
- **R3-B02，已提交源码：** TestProjectWorkbenchView.xaml 同一临时开发用例检查命令曾在顶部和运行区重复出现；删去后一处，不变更临时选择和正式生产全量用例的不同语义。历史排除修复按钮仅改善文案，未据此宣称旧工程兼容全部合格。
- **附加静态验收：** 在最终相关 R1 源码上重新扫描 AutomaticTestHomeView、TestEditorView、TestProjectWorkbenchView、WaveWorkbenchWindow、ManualBenchWindow、FlashLabWindow、ProjectConfigurationOverviewView、MainWindow 八个主要 XAML：所检查标签嵌套未报闭合错误，XAML 声明的代码事件处理函数未发现缺失。**这不是 WPF XAML 编译**；ResourceDictionary、程序集引用、Binding 类型及实际键鼠操作仍需 UI-01 等 Windows 项目复测。
- **改动规模：** 本节形成时，自基线 99618d180bc3b5aa35bc579d2281cfdaea8f13f8 起已提交 37 个源码/报告提交，GitHub compare 显示 19 个 .cs/.xaml 源码文件和本报告，共 20 文件；最后的报告附录更新会产生一个额外文档提交。只能以最后实际 R1 HEAD 及 diff 追溯改动数量。

复评仍为 **66/100；DESIGN_GATE=BLOCKED；BUILD/WPF/HARDWARE=NOT_RUN**。静态检查本身不能代替 1366×768/150% 的实际工业操作体验，也不会因新一轮小修补而自动涨分。
