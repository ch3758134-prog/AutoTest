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

**初始 46/100 → 第二轮 63/100 → 第三轮 66/100 → 第四轮 69/100 → 第五轮 73/100 → 第六轮 76/100 → 第七轮 77/100 → 第八轮 79/100 → 第九轮 82/100。** 全部为静态审查评分；不把源码改动自动等同于实际改善，不设没有依据的满分。最终 UI 设计交付门槛 90 分**未达到**；生产物理 START、完整生产真实 Inspection/证据/报告及部分独立专业工具后端仍阻断。任何阶段均不得记作 UI_PASS、PRODUCTION_PASS 或 GATE_PASS。

发生实质变化的项目：生产页面不再列出 COM 配置、独立工程验证及软件 START；将完整 TEST 树作为核心监视；开发整组实测从生产入口移入工程工具菜单，重新经过真实扫码/硬件冻结；对现有 RealStepFact 使用正式 StepId 做 UI 投影而不是用日志名字猜测；主控自动预检和流程/测量更清楚；FlashLab/Wave 及部分手动台架的高 DPI/密度问题进行实际 XAML 修复。未实现的正式执行状态不显示 PASS、进度、真实数据或隐含完成。

## 2. 统一 100 分尺度

| 维度 | 权重 | 初始 | 二轮 | 三轮（历史基线） | 截至三轮扣分 |
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

- **最新第九轮静态评分：82/100，等级：工程使用候选（仅源码层，未实测）。** 不是设计交付候选，不符合 90 分目标，也不等于 Windows 运行通过。
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

第三轮历史复评为 **66/100；当时 DESIGN_GATE=BLOCKED；BUILD/WPF/HARDWARE=NOT_RUN**。静态检查本身不能代替 1366×768/150% 的实际工业操作体验，也不会因新一轮小修补而自动涨分。


## 12. 第四至第六轮（2026-11-24）自主延续整改

本章节承接第三轮末 R1 源码 HEAD e199b565da77eea332389d59feb4998f541b7fd1。完成代码审查时 HEAD 70caa2edd730de49602da99bf471ea5089d0bf23，GitHub 比较显示随后 50 次提交涉及 30 个文件；报告自身更新还会增加一次提交。所有修改仍只在 R1，未触碰 main / Project_Management 或固件、TEST、FLASH、DEVICE、SCAN、RECORD 既定底层合同。

### 12.1 同样的七维评分尺度

| 维度 | 权重 | 第三轮 | 第四轮 | 第五轮 | 第六轮 | 第六轮扣分原因 |
|---|---:|---:|---:|---:|---:|---|
| 产品信息架构 | 20 | 13 | 13 | 13 | 14 | -6：正式生产状态机缺真实事件；工程与旧研究界面仍有部分重复 |
| 操作流程效率 | 20 | 12 | 13 | 14 | 14 | -6：专业 UDS 无实时诊断；高频任务无键鼠耗时和跳转统计实测 |
| 信息表达与状态反馈 | 15 | 10 | 11 | 11 | 12 | -3：原始记录/证据/波形断线可视化提高；仍无真实生产结案事件 |
| 空间与布局 | 15 | 10 | 10 | 12 | 13 | -2：已调整窗口 DIP 约束、任务分屏；缺 1366×768/150% WPF 截图 |
| 视觉规范与一致性 | 10 | 7 | 7 | 7 | 7 | -3：局部历史工具窗口覆盖 Style；缺实机对比，未主观提分 |
| 编辑与交互细节 | 10 | 7 | 7 | 8 | 8 | -2：UDS/Flash 保存冲突与键盘操作改善，但输入法/焦点未真实复查 |
| 异常与恢复体验 | 10 | 7 | 8 | 8 | 8 | -2：冲突保护/异步互锁已接；磁盘/USB/人工中断测试未运行 |
| **总分** | **100** | **66** | **69** | **73** | **76** | **最终 90 分门槛未达到** |

以上是严格静态证据评分，任何代码提交都不自动获得加分。第四轮提升 3 分，第五轮提升 4 分，第六轮提升 3 分，均对应可核对的窗口/状态改动；没有 Windows 验证就不能补足尚缺分值。

### 12.2 第四轮：RECORD 追溯与状态真值（69/100）

**新发现：** RecordHistoryWindow.xaml(.cs) 只显示运行摘要和导出 HTML。单 ECU 多次尝试的真实失败 StepOccurrence、实际/期望测量、原始事件及证据完整性不能在窗口内定位；此外 Writing / Interrupted 归档可因带有 Pass verdict 字符串而被显示成最终“通过”。

**代码整改：** 在现有多 SQLite 真实源查询列表下增加可调高的三个只读页签：逐步骤事实、原始事件、证据完整性。通过 IRecordHistoryToolSource 的已有 ExportFullReportJsonAsync 获取真实 RunReportExporter 事实，不创造第二套 RECORD，回读 canonical path、disposition、actual、expected、时间戳、occurrenceId、evidenceId、异常码、采集质量。切换选中记录时取消旧请求，限制每类 UI 预览 1200 行并整体设置 DataGrid.ItemsSource，避免大量 UI 通知；完整真实报告仍可打开。多库查询、报告导出移至后台线程，避免压住 WPF Dispatcher。

**反向修复：** 检查 ReportCaptureHealth 的实际 JSON 属性大小写；明确已归档与未结案、生产/开发/诊断 verdict，不将开发 Pass 表述成正式生产合格。未归档记录的查看和导出均有“未结案”提示。

**验证与扣分：** 仅 GitHub 源码、实际导出接口静态关联；没有真实大体量 SQLite 和 HTML/JSON 比较运行日志。保留未验扣分。

### 12.3 第五轮：全系统高 DPI 和工具编辑（73/100）

**新发现：** 主窗 MinWidth 原固定 1220 WPF DIP，独立 DBC/KEY/扫码/原生烧录等窗口最小高度多为 560–625 DIP，1366×768/150% 可能不能看到底部操作；FlashLab/UDS/手动台架固定多栏在窄屏挤占核心数据。UDS 异步保存过程中允许继续输入，新编辑可能被之前的保存回调误标“已保存”。

**代码整改：**

- MainWindow.xaml(.cs)、MainWindowViewModel.Production.cs：主窗 MinWidth 820、MinHeight 455 DIP；窄屏隐藏重复的工程工具栏但菜单始终保留，短高屏默认折叠自动展开的观察区，工程树实际可收缩至 230 DIP。
- DesktopWindowSizing.cs / FlashWindowSizing.cs：新建仅用于视图的 WPF 可用区域尺寸辅助；相关 Desktop 各种独立窗口和 FlashLab/原生配置在启动或恢复时按 WPF DIP 限制到可见区域，不改变后续用户手动缩放。
- UdsWorkbenchWindow.xaml(.cs)：先改传统左右/上下布局；反向复审发现上下同时显示在短高屏依然不可用，于是改为窄屏全高度“服务序列/回放与响应”视图切换，宽屏保留并排。支持 Ctrl+N/O/S、Ctrl+Shift+S、Ctrl+D、Insert/Delete；真实文件读取/保存期间锁定输入。服务序列修改后之前装入的真实回放匹配自动失效，避免旧响应被当作新请求的验证。
- ManualBenchWindow.xaml(.cs)：宽屏 CAN/KEY/RX 三栏；中屏 CAN/KEY + 下方监视；窄屏全宽 CAN、KEY/工位、CAN 监视任务切换，不停止真实周期 CAN 或保持 KEY。ManualBenchCanPropertiesView.xaml 改成 RAW CAN 字节主导与 DBC 辅助弹性宽度，去掉多余说明并显示参数校验。
- DbcWorkbenchView.xaml：信号连续表格为主、位布局按需展开；将“点击外部即可完成”误导性文字改为应用/取消明确收尾。
- NativeFlashProfileWindow.xaml(.cs)：给 HEX/安全文件异步导入、原生配置读取及保存增加输入互锁和导入中关闭拦截，不再在未提交的编辑仍可变化时称旧文件已保存。未变更烧录工艺、固件/密钥文件语义和硬件处理。

**验证与扣分：** 新窗口所用 DPI helper 在正确 WPF 项目自动纳入源代码；启动/恢复的边界仍待 Windows 1920×1080、1366×768 100/125/150% 的键鼠与实际位置验证。

### 12.4 第六轮：生产真值、WAVE 可视数据质量、异常逆向审查（76/100）

**新发现：** 生产工作区没有任何正式 production StepOccurrence，却一直显示“当前用例 — / 步骤 — / 期望 — / 实际 —”等空信息；进入工程整组软件 START 后再切生产可能遗留已冻结扫码身份。WaveWorkbenchWindow 的原始采样被视觉降采样之后才进行缺口判断；原始采集掉帧会有被“补线”误读的可能。NaN/Infinity 也可能使整图渲染失败。RECORD 的开发 Pass 与正式检测 Pass 同名有误解风险。

**代码整改：**

- AutomaticTestHomeViewModel.cs、AutomaticTestHomeView.xaml：生产必须由正式真实 StepOccurrence 证据来源才显示测量/步骤卡片；后端尚未接时不再填充长期不可用的“—”数据占据空间，当前扫码和工位阶段继续反映真实状态，未接线不假定成功。断线主控状态为“未连接”而不是“连接中”。
- AutomaticTestHomeViewModel.Run.cs、MainWindowViewModel.Production.cs：返回生产时撤销开发整组验证的软件 START 准备、强制重新扫码核对 DUT 身份。开发调试仍在工程工作区完成。
- WaveWorkbenchWindow.xaml.cs 的 RenderWave、VisualGapLimit、TraceHasDiscontinuities、DrawTrace：先按原始观察间隔与非有限样本分段，再对每段做纯视觉采样抽稀；参考容差带不跨不连续时间；发现真实缺口时有可见警示；不更改信号原始证据、参考冻结资源或 WAVE 算法判定。
- RecordHistoryWindow.xaml.cs：区分“检测通过”“调试通过”“诊断通过”；区分“归档报告”与“未结案资料”，不把中断和 Writing 报告误当正式放行。
- MainWindow.xaml(.cs)：小屏、高 DPI 的 TEST 主编辑区优先，不让自动观察栏抢掉当前步骤的操作空间。

**新增独立复核：** 逐一回读 AutomaticTestHomeView、ManualBenchWindow、UdsWorkbenchWindow、RecordHistoryWindow、WaveWorkbenchWindow、NativeFlashProfileWindow、DbcWorkbenchView、MainWindow 八个实际核心 XAML 及其 code-behind，简单 XML 标签栈未发现闭合不平衡，XAML 声明的事件处理函数未发现缺失。**这不是 XAML 编译、资源绑定检查、Windows 键鼠录屏或实物测试。**

### 12.5 各主要场景重新评分（只是场景诊断分，非七维总分）

| 场景 | 第三轮 | 第六轮 | 仍不通过的真实原因 |
|---|---:|---:|---|
| 生产检测 | 59 | 62 | 实体 START、夹具到位事实、正式全流程 Inspection/Record 尚未闭环 |
| ECU 工程与 TEST | 64 | 68 | 空间和快捷键改善；长流程编辑、运行失败定位和自动保存未 WPF 验证 |
| 手动台架 | 61 | 73 | 全宽 CAN/KEY/RX 新布局；真实周期 CAN、保持/停发和设备回读未实测 |
| DBC | 58 | 63 | 信号矩阵及浮窗显隐合理；大量实际 DBC 操作及回读尚未验证 |
| UDS | 32 | 43 | 独立序列和回放可编辑，但实体 ECU UDS 会话没有安全接线 |
| FLASH | 55 | 68 | 原生配置编辑/导入的异步一致性改进；没有真实 ECU 烧录验收 |
| WAVE | 56 | 66 | 原始采样缺口不再被可视线段跨接，参考与真机采样质量仍待验 |
| RECORD | 54 | 70 | 真实步骤/事件/质量/报告关联可钻取；批量生产索引和正式结案不完整 |

### 12.6 第四至第六轮变更文件、证据等级与准出门槛

以 R1 上一个 66 分候选 HEAD e199b565da77eea332389d59feb4998f541b7fd1 对比，新增代码提交期间 GitHub compare 显示约 50 个提交、30 个变更文件（后续报告落档不计入这 50 次）。完整变更以 GitHub compare 为准。主要是：

- Desktop Shell：RecordHistoryWindow.xaml(.cs)、UdsWorkbenchWindow.xaml(.cs)、ManualBenchWindow.xaml(.cs)、ManualBenchCanPropertiesView.xaml、MainWindow.xaml(.cs)、MainWindowViewModel.Production.cs、AutomaticTestHomeView.xaml、AutomaticTestHomeViewModel.cs/.Run.cs、WaveWorkbenchWindow.xaml.cs、DesktopWindowSizing.cs（新）
- Desktop DBC：DbcWorkbenchView.xaml、DbcMessageEditDialog.xaml.cs、DbcSignalEditDialog.xaml.cs
- Desktop Configuration / Projects：KeyMaskPickerDialog.xaml.cs、WaveReferencePickerDialog.xaml.cs、WaveSignalPickerDialog.xaml.cs、NewProjectDialog.xaml.cs、ResourceNameDialog.xaml.cs
- Desktop 工具弹窗：ManualBenchCanPropertiesWindow.xaml.cs、ManualBenchKeyActionWindow.xaml.cs、SerialScannerWindow.xaml.cs
- FlashLab：FlashWindowSizing.cs（新）、FlashLabWindow.xaml.cs、NativeFlashProfileWindow.xaml(.cs)

**仍阻断准出：** 正式产线实体 START/夹具/每次前置烧录/全部 TEST/正式 RECORD 尚未有可用整机闭环；UDS 独立实体实时操作未接通；WAVE 无工程在线源和采样质量尚未按真实 DUT 验证；跨工程生产结果发现和单台批量报告缺实体事实；Windows 构建/运行/DPI/焦点/关闭未测。

**截至第六轮：76/100（仅静态审查，基本可用候选）｜DESIGN_GATE=BLOCKED｜BUILD=NOT_RUN｜WPF_HANDS_ON=NOT_RUN｜HARDWARE=NOT_RUN｜CI=NOT_RUN。** 不得因源码提交和自身评分直接标记 90+ 或验收通过，后续本地 Windows 验证清单仍按第 8 节 UI-01 至 UI-18 执行。


## 16. 第七轮：消除隐藏宽度覆盖、诊断配置空间与单台/批量报告断层（77/100）

代码审查起点：R1 的第六轮末更新。第七轮代码审查时 GitHub R1 HEAD 为 4eb0b7c6bc3237cb3f9dcd0f785e8b71d14f32c7；相对第三轮末 e199b565da77eea332389d59feb4998f541b7fd1，现已新增 60 个提交，修改或创建 34 个文件（含本报告；当前报告更新后数量以最终 R1 diff 为准）。

### 16.1 不参考旧缺陷表重新检查高频场景

1. **TEST 步骤选择器的“表面修好了，打开时又变大”问题：** 原 TestEditorView.xaml 虽然把 PaletteSurface 缩至 690 DIP，但 TestEditorView.xaml.cs.OpenActionPalette 仍会用 840 DIP 宽度覆盖 XAML；在 1366×768/150% 下还存在开窗高度超过有效区域风险。改为依据实际编辑视图宽度、当前 Window 和 WPF 工作区共同限制宽高，不让选择器遮挡屏幕；恢复一行“当前插入位置”实际上下文，删除无效提示。此问题是第七轮独立发现而非用户截图指明。仍需真实 WPF Popup 浮窗位置、键盘导航、插入点验证。
2. **UDS 缩窄后端点配置的垂直浪费：** UdsWorkbenchWindow.xaml 的诊断端点 / CAN TxRx / P2/P2* 原来固定占上方整个表单，短高屏全高主任务页仍不够高。新增可展开的原生诊断端点配置，紧凑模式默认收起但保留随时编辑入口；P2/P2* 改为可折行，不改变任何地址/超时合同。只改善 UI。正式实体 ECU UDS 会话继续 BLOCKED。
3. **DBC 信号下拉文本精度不合理：** DbcEditableSignal.cs 将物理值原本的 G17 字符串同时用作真实编码值和显示名称，像 0.1 一类十进制值可能显示为很长的二进制浮点尾数。现在 **真实 SelectedValue 仍保持 G17 严格可回编输入**，但下拉的 Label 使用 G9 便于工程师阅读。Codec、DBC 和实际信号身份一字未改。该修改没有用缩短 Value 造成隐式信号精度损失。
4. **RECORD 批量导出真实缺失：** RecordHistoryWindow.xaml(.cs) 之前只提供单次 HTML/JSON，无法汇总用户在当前筛选结果中多选的真实已归档运行。新操作可按 Ctrl 多选至少两条 Committed 记录，将原始 RunReportExporter 的每次 HTML 或 JSON 事实内容打包成一个 ZIP。Writing/Interrupted 不会作为正式已归档结果混入 ZIP。过程可取消、阻止正在打包时直接关闭窗口、失败删除临时文件；只有所有真实报告生成并写完 ZIP 才发布最终文件，且不覆盖已有报告或修改底层 SQLite。所有来源分别使用原始 RunId/RecordSource，绝不把不同 ECU 证据交叉关联。

### 16.2 第七轮评分（相对第六轮）

| 评价维度 | 第六轮 | 第七轮 | 变化理由与保留扣分 |
|---|---:|---:|---|
| 产品信息架构（20） | 14 | 14 | 无新的业务合同/正式工位闭环，不提分 |
| 操作流程效率（20） | 14 | 15 | +1：多次 RECORD 报告变为一次实际 ZIP 操作，TEST 浮窗按使用空间开，UDS 端点按需展开 |
| 信息表达与状态反馈（15） | 12 | 12 | DBC Label 精度改善，但没有真实图像/绑定证据，不提分 |
| 空间与布局（15） | 13 | 13 | Popup/UDS 表单结构改善，未进行 WPF 1366×768 实测，不提分 |
| 视觉规范与一致性（10） | 7 | 7 | 局部旧样式和 Windows 截图不全 |
| 编辑与交互细节（10） | 8 | 8 | 需要 Tab、浮窗、撤销、保存和上下文实际操作验证 |
| 异常与恢复体验（10） | 8 | 8 | ZIP 取消与无覆盖代码具备，未做真实磁盘异常/权限回归 |
| **总分（100）** | **76** | **77** | **设计准出仍 BLOCKED** |

单独场景评估变更：工程/TEST 68→69；UDS 43→45；DBC 63→64；RECORD 70→74。场景分仅供暴露弱项，不与七维总分做加权替代。

### 16.3 新增代码位置与独立复核

- Testing/TestEditorView.xaml(.cs)：步骤选择器动态打开尺寸、当前插入位置、搜索选择动作。
- Shell/UdsWorkbenchWindow.xaml(.cs)：可折叠地址/超时工艺设置和窄屏任务页。
- Dbc/DbcEditableSignal.cs：信号枚举项精确传值与人类可读显示值分离。
- Shell/RecordHistoryWindow.xaml(.cs)：真实 Committed 多选、批量 ZIP 导出、取消/关闭及原始 Run 来源归属。
- 再次单独回读并静态扫描这几个关键 XAML（含 RecordHistory、UDS、TEST）标签和事件处理函数，未发现闭合错误或显然缺失的命名事件；不能证明 .NET/WPF 编译及执行成功。

### 16.4 交付否决项仍有效

最终状态仍为 **77/100、DESIGN_GATE=BLOCKED、BUILD=NOT_RUN、WPF_HANDS_ON=NOT_RUN、HARDWARE=NOT_RUN、CI=NOT_RUN**。正式生产实体 START/夹具、强制 FLASH、完整 TEST 与正式 RECORD 结案还没有可验收的整机闭环；独立 UDS 实体通信尚未真正接通；WAVE 独立在线真实采样与判定精度也缺现场证据。本轮只对原生 UI 和必要状态接线进行修改，不为了刷到 90 分伪造可运行能力。必须按第 8 节 UI-01～UI-18 用 Windows 操作和现场记录逐项补证。


## 17. 第七轮收尾复核与精确交接

- 第七轮最终**源码实现**检查点：R1 @ **8bb20b508d1ca4f1a8faf50a22c439fe3a682400**。本报告落档提交将成为它之后的新 R1 HEAD；最终下载应使用 R1 最新分支头而不是旧 SHA。
- 相对第三轮末 e199b565da77eea332389d59feb4998f541b7fd1，最后一次 GitHub compare 核查显示 **63 次后续提交、34 个变更文件**（其中包含报告），未修改 main。所有确认来自 GitHub API 的真实提交与文件内容回读，不是本地编译结论。
- 追加反向修复：RECORD 单次报告打开/导出完成后更新批量导出启用状态，Ctrl 多选时不给空选择误导；底部失败长文本通过省略与 ToolTip 保留全文，不抢掉批量按钮。打包进度回调在取消/成功后不再覆盖最终导出状态。
- **本轮准出决定保持原样：77/100，BLOCKED。** 与其在没有 Windows 运行和产线物理闭环的条件下宣布 90 分，不如保留可复现的界面静态问题清单以及 UI-01 至 UI-18 的真实验证路径。


## 18. 第八轮：真实证据、跨 ECU 烧录限制与保存竞态（79/100）

**时间、范围：** 2026-11-24。以第七轮 R1@412c1b340cdd7abad314740df2f1bc764450043f 为起点，第八轮代码复审时 R1@3d469d0d3b2178979bb37e01ec580245e7cae4c8。GitHub compare 显示从该起点新增 24 个提交，变更 13 个文件（报告后续另计）。所有更改只在 R1；main、Project_Management、真实固件均未修改。**未运行编译、任何 CI、WPF/Windows 键鼠测试或实体 USB/CAN/ECU 测试。**

### 18.1 重新审查时独立发现的问题与修复

**WAVE：画线的连续性不等于采样连续性，更不等于判定有效。**

- 旧 WPF 曲线把 10 ms 周期信号中的 80～150 ms 采样断点连接成连续线：原因是原显示断线策略的固定 200 ms 最小阈值过大。修正为基于原始采样时间中位间隔的显示分段。
- 反向复查又发现重复或逆序时间戳会让曲线降采样拒绝数据，或让游标基于乱序二分搜索给出错误 Δy/极值。当前绘制按原始时间分段，非有限数值、重复/逆序时间、采样缺口显式断开。游标选择跨无效值、断点、非单调时钟或超出当前信号真实范围、只有一个实际样本的区间时不再给出数值测量。
- 再次反查发现一个阈值上限不合理：正常 5 秒周期慢速信号不能被一律判为断线。移除了固定 2 秒上限，仍保留相对采样频率的检测。
- 将纯视觉连续性策略抽到新文件 AutoTest.Specialists/Wave/WaveDisplayContinuity.cs；WaveWorkbenchWindow.xaml.cs 只调用此策略，并确保后台真实采样事件触发的绘图 UI 操作经过 Dispatcher。没有修改原始 wave 证据、参考版本或 WaveComparator。
- 新增 WaveLabAnalysisTests：10 ms 短暂采样中断、重复/逆序时间戳、NaN、正常连续样本、慢速合法采样和游标越界的测试用例。**仅已提交测试源码，未执行测试。**

**UDS：读取内容与并发保存校验的哈希必须来自同一次文件读取。**

- UdsWorkbenchWindow.xaml.cs 原来先读取验证 UdsToolDocument，再第二次读取文件计算 SHA。外部程序若在两次读取之间改变文件，可能造成“旧界面数据关联新文件 SHA”，后续覆盖防护失真。
- UdsToolDocumentStore.cs 新增单次读取并验证的 UdsToolDocumentSnapshot（文档+该次原始字节 SHA256）。编辑器打开、保存后回读均使用同一快照，不再双读；保存后核对完整 Profile、Schema 与有序服务内容，不只检查服务数量。
- Ctrl+N、打开其他文档、载入回放和父窗口退出前，先尝试提交正在输入的 DataGrid 单元格；未提交成功禁止放弃/切换。在线加载回放时限制额外清除动作，以免重入修改状态。
- UdsReplayWorkspace.cs 的离线回放文件没有录制的 CorrelationId，却原来用 Guid.NewGuid() 为每次回放新造一个关联身份。改为 Guid.Empty（未知/未记录），避免把假的追踪身份混入真实 ECU 事件印象。回放文件长度实际读取再次检查 8 MiB 上限。
- 新增/扩充 UdsWorkbenchTests 中的同源哈希和回放相关 ID 检查。未接入实体 ECU UDS 实时诊断，不能据此宣称完成 UDS 工具。

**FLASH：同 CAN ID 和地址不等于验证了另一个 ECU。**

- 重要安全缺口：NativeFlashProfileDocument.ValidateForFreeze 原仅严格核对已验证 HydEPB 的 CAN 端点、Flash 几何/块长等参数，没有强制 EcuModelCode 等于 HYDEPB。其它型号即使复制一套 HydEPB 参数也可能进入可执行 Flash Job 冻结。
- 已补加明确的 fail-closed 车型检查：此 R1 物理烧录工艺**只对 HYDEPB**有效，其它 ECU 可以建立并保存参数草稿，但不能使用未经 OEM 验证的方法生成可执行的烧录 Job。完善 NativeFlashProfileTests 对跨车型配置复制的拒绝用例；不改变真正经过验证的 HYDEPB 行为。
- NativeFlashProfileWindow.xaml(.cs) 对非 HYDEPB 型号显示明确、简短的“仅可离线配置、不允许套用当前烧录方法”说明；从实际加载的 Profile 型号而非启动参数派生显示。
- 发现保存/导入完成后离线 Freeze 仍在后台进行时，用户可以更改界面参数，造成“显示的冻结计划不是当前草稿”的风险。增加 PreviewBusy 输入互锁、加载中/导入中/校验中关闭保护、冻结前的原生 Profile SHA 修订比对。只有同一份已保存文件才能展示冻结结果；不修改任何实际 USB/UDS/Flash 发送逻辑。

**RECORD：数据库的 Verdict 是来源判定，不自动证明完整生产放行。**

- 多库历史“Production + Pass + Committed”原显示为“检测通过”，容易掩盖没有确认完整采集和证据归档事实的问题。现在显示为“判定通过”，表头改“记录判定”，详情必须另查看 CaptureHealth、EvidenceArchiveIncomplete 等独立证据质量。
- 缺少 evidenceArchiveIncomplete 布尔字段的老记录必须判为完整性 UNKNOWN，而不能当成“未发现缺失”。采集完整标记非 true 时明确提示不得从记录 Pass 推断生产放行。
- 批量真实报告 ZIP 增加 manifest.json，逐项保留原始 RunId、ECU ID、工程来源、模式、原始 Verdict/提交状态、时间与每份导出文件 SHA256。清单是归档索引，不是第二套 TEST 判定，也不为未结案记录签发生产结果。取消、未完成临时包、已存在目标文件均按 fail-closed 操作。

### 18.2 本轮静态评分

| 维度 | 七轮 | 八轮 | 变化说明 |
|---|---:|---:|---|
| 信息架构（20） | 14 | 14 | 产线正式执行状态机及分车型 OEM 方法仍缺真实实体合同 |
| 操作效率（20） | 15 | 15 | 保存/编辑边界修复，仍无实际场景计时 |
| 信息与状态（15） | 12 | 13 | +1：判定/放行分离、采样断点和游标质量判别、ZIP 可审计哈希索引 |
| 空间布局（15） | 13 | 13 | 无新的 Windows 截图，不提分 |
| 视觉一致性（10） | 7 | 7 | 工具风格、DPI 现场验收尚未到位 |
| 编辑与交互（10） | 8 | 8 | 没有实际输入法、焦点和回归录像 |
| 异常恢复（10） | 8 | 9 | +1：跨车型 Flash Freeze 限制、原生配置并发校验、UDS 单次哈希快照 |
| **总分（100）** | **77** | **79** | **BLOCKED：不是产品设计/整机准出** |

该评分只评价当前已核对的 R1 源码静态候选，不代表通过 Windows 构建，更不代表真实 DUT 验收。硬门槛仍包括实体 START、夹具确认、每次正式前置 FLASH、完整 TEST、正式 Inspection/RECORD/报告与扫码实体绑定闭环；UDS 独立工具实体收发、WAVE 真实采样质量、R1 全窗口实际操作截图/焦点/恢复和多型号真实 FLASH 方法也仍未验收。

### 18.3 本轮独立静态交叉复核

本轮完成 GitHub HEAD 上 13 个变更文件的二次结构读取：含 RecordHistoryWindow.xaml(.cs)、UdsWorkbenchWindow.xaml.cs、WaveWorkbenchWindow.xaml.cs、NativeFlashProfileDocument.cs、NativeFlashProfileWindow.xaml(.cs)、UdsReplayWorkspace.cs、UdsToolDocumentStore.cs、WaveDisplayContinuity.cs，以及三份对应测试。简单 C# 代码括号扫描与 XAML 标签/事件处理方法名检查没有找到闭合或处理函数缺失问题。此仅是辅助静态检查；不是 MSBuild 的 C# 编译、WPF XAML 资源类型/编译检查，更不是自动化测试结果。

**八轮决定：79/100｜UI_DESIGN_GATE=BLOCKED｜PRODUCTION_GATE=BLOCKED｜BUILD=NOT_RUN｜TESTS=NOT_RUN｜WPF_HANDS_ON=NOT_RUN｜HARDWARE=NOT_RUN｜CI=NOT_RUN。**


### 18.4 KEY_SIM 保持语义独立回查（不加分）

额外回读 Shell/ManualBenchKeyActions.cs、Devices/Station/RawKeyAction.cs、Devices.Tests/ManualKeySequenceFenceTests.cs：真实动作库运行器只对非 WaitOnly 段授权并发送 KEY CAN；WaitOnly 只等待不生成掩码清零帧；保持时间结束后代码没有追加隐式 Release。明确掩码零才下发 ReleaseAll；Stop 取消任务并撤销 KEY 授权，没有伪造“已释放实际按键板电压”的 ACK。可见日志明确“主控 ACK 非五路实际电压”。这与现有 20 KEY／5 路电压和状态继续保持的已批准语义一致，但**不证明实物按键板保持/电压、断线异常状态已通过测试**。本轮未修改该链，未增加评分。


## 19. 第九轮——工业级实际编辑流程、异步关闭与工程整组实测监视（82/100）

继续审查基线为 2026-11-24 第八轮 R1 SHA 966385ff764b29785b9d5adbb7f4f4bd75e177f9。本轮全部实施仅在 R1，不操作 main 和固件。不运行 Windows 构建、CI、软件测试或实体硬件。代码差异、提交数按 GitHub R1 compare 实际值回读。

### 19.1 手动台架：真正的 CAN/KEY 浮窗，弃用遮罩

原手动台架虽然已有独立属性 Window 类，实际运行时仍从台架打开临时遮罩式 UserControl，遮住工位信息，CAN/KEY 两类的属性模型、用户关闭操作不统一。已在 ManualBenchWindow.xaml(.cs) 中将动作按钮双击/编辑改为持续存在的 ManualBenchKeyActionWindow；CAN 行属性使用 ManualBenchCanPropertiesWindow，复用原 CAN DATA 主导、DBC 辅助完整 ManualBenchCanPropertiesView。各个 CAN 浮窗根据初次打开时确定的 BenchCanRow 编辑，后台切换选中行不会错误地把字节更改写到另一报文。相同行只激活原有浮窗；CAN 其他行可各自同时打开。无效的遮罩层与相应 code-behind 删除。

CAN 浮窗关闭前持久保存原项目 CAN 草稿；KEY 浮窗先等待已有定时自动保存，再保存编辑增量；两类都避免在 WPF 首次 Closing 回调的同步阶段重复调用 Close。手动台架退出前等待子浮窗保存后再释放共享 ViewModel；失败则拒绝退出、保留草稿和可见错误。KEY 20 Key 持续保持、纯等待不发 CAN、显式零掩码释放的硬件语义未改变。

**场景未验证：** 多浮窗焦点/双击与单击是否误触发、窗口层叠、KEY 实物 ACK/保持、周期 CAN 停发/保存冲突及 WPF 输入法。

### 19.2 主屏、副屏和 DPI 可达性

DesktopWindowSizing.cs、FlashWindowSizing.cs 不再仅凭主屏 SystemParameters.WorkArea 或 owner 宽高猜测窗口工作区，改通过 Windows MonitorFromWindow / GetMonitorInfo 得到窗口所属显示器物理 WorkArea，并按 PresentationSource 的 TransformFromDevice 转换为该窗 WPF DIP 坐标，限制第一次显示的位置及尺寸，不永久覆盖用户手动拖拽几何信息。仍需要在 Windows 的主副屏不同 DPI、1366×768/150%、1920×1080/100%、任务栏位于屏幕侧边时实测。

### 19.3 全工程真实实测不再“启动后看不见”

独立增加 EngineeringVerificationWindow.xaml(.cs)：只观察既有 AutomaticTestHomeViewModel 所暴露的真实 OperatorRunState、OperatorFlashState、OperatorRecordState、OperatorRunEvidence、OperatorCurrentCase/Step、OperatorActual/Expected/Failure、Cases 用例/步骤树与 OperatorEvents。具备跟随执行、手动选择步骤、显示事件和明确停止请求。没有制造第二套 TEST 执行器、工位数据、真实生产 START、PASS 或 RECORD。

MainWindowViewModel.Production.cs 在“准备本台扫码身份”时打开工程实测监视器，运行菜单可随时激活，回生产页面时关闭工程监视器。MainWindowViewModel.cs 的工程关闭/切换保护新增 IsOperatorRunning，覆盖之前单独 _realTest.IsRunning 没有覆盖的“FLASH 进行中，尚未正式进入 TEST”的窗口关闭风险，防止破坏硬件上下文。

AutomaticTestHomeViewModel.Run.cs 返回生产时重新从只读原始工程构建用例树，开发整组实测的“实测通过”不残留到操作员正式生产界面。AutomaticTestHomeViewModel.cs 在主控 USB session 改变时使已接受扫码身份和未完成异步核对失效，必须重新核对真实 ECU；同一工程不能依靠旧 USB 会话的扫码身份执行新运行。

### 19.4 DBC 规范/小数易用性

DbcMessageEditDialog、DbcSignalEditDialog 的原始报文/信号/发送节点名称使用 ASCII DBC 标识符约束，而非 Unicode \w 模式；中文显示名仍使用用户独立别名。允许改 CAN ID 时，>0x7FF 合法标识自动选择 29 位扩展帧。DBC 信号比例、偏移、上下限以易读 G12 编辑，未编辑的数值回写时保存原二进制值，杜绝改名顺带让精度尾数发生默默变化。

### 19.5 第九轮评分与独立源码检查

逐一源代码回读 EngineeringVerificationWindow、MainWindow、ManualBenchWindow、ManualBenchCanPropertiesView/Window、ManualBenchKeyActionWindow、两个 DBC 属性编辑器的 XAML 与 C# 事件处理；简单标签栈检查、命名事件和主窗口约 30 个命令绑定匹配未发现缺失。**这不是 MSBuild、WPF 资源绑定、键鼠和真实 ECU 验收。**

| 维度 | 八轮 | 九轮 | 依据 |
|---|---:|---:|---|
| 信息架构 /20 | 14 | 14 | 实体生产全流程仍缺必要数据合同 |
| 操作效率 /20 | 15 | 16 | +1 真正多 CAN/KEY 浮窗、单一工程整组监视入口 |
| 状态反馈 /15 | 13 | 14 | +1 真正的工程 FLASH/TEST 步骤可观测，明确不冒充生产 |
| 空间布局 /15 | 13 | 13 | 多显示器仅完成源码逻辑，无真实 DPI 截图 |
| 视觉规范 /10 | 7 | 7 | 没有足够实际布局和主题验收 |
| 编辑细节 /10 | 8 | 9 | +1 浮窗编辑固定对象、保存和明确关闭 |
| 异常恢复 /10 | 9 | 9 | 竞争保护在源码可见，但真实故障回归尚缺 |
| **总分** | **79** | **82** | **未达 90 分，所有产品准出仍被阻断** |

**第九轮决定：静态 82/100、UI_DESIGN_GATE=BLOCKED、PRODUCTION_GATE=BLOCKED、BUILD=NOT_RUN、TESTS=NOT_RUN、WPF_HANDS_ON=NOT_RUN、HARDWARE=NOT_RUN、CI=NOT_RUN。** 不得把报告加分当实体投产通行证。
