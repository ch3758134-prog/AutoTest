# AutoTest｜UI/UX 系统重构研究与实施合同候选 v0.1

- 时间：2026-11-17
- 研究状态：RESEARCH_CANDIDATE / CONTINUE_ITERATION；待用户审阅、项目管理兼容性审查
- UI 工作分支：ui/ide-design
- 对照代码：feature/g4-html-parity-20261115 @ a7fc6351f9fb7f048ff420c18a0280006c520d2a
- 单一现行可交互 HTML：docs/ui/AutoTest_UI.html（此次不覆盖，也不晋升候选）
- 研究依据：用户提供的 2026-11-17 当前 WPF 主界面截图与 TEST 实际编辑体验、当前 WPF 源码、下列软件官方文档
- 本文件是研究与交互合同候选，不改变 TEST、USB、CAN、KEY_SIM、夹具、UDS、Flash 的事实合同，不等于 Windows 构建或硬件验证通过。

## 1. 范围、目标与不变量

### 要解决的真实问题
1. TEST 编辑反复“选中—输入—应用属性—校验—切换”，一条无效数据可能阻断用户去看别的步骤。
2. 无步骤时三块大型阶段底色卡片、右侧空 Inspector、大片无用途白色空间；有步骤时反而编辑流程碎片化。
3. 主窗口颜色硬编码、淡蓝底叠加多种阶段底色、按钮与容器重复，缺少专业 IDE 的统一层级。
4. 加步骤弹出面积最高 1000x790 的大选择面板；常用操作要经过多余选择，用户难以连续编辑。
5. WPF 拖拽只支持 MoveStepAfter；ListBox 单选，缺少前/后/嵌套目标和批量操作。
6. DBC、KEY、读/等/判定没有统一的专属 Inspector 参数体验；资源载入又会让 Inspector 暂时退化为 Loading。
7. 运行/检查/编辑的界面权限与状态表示还不够清楚。

### 必须保持的业务与技术事实
- 唯一 TEST DSL、StepNode/StepId、StepEditTransaction/Undo、原本真实执行器，不另造第二执行模型。
- 准备/测试/清理三阶段、IF/ELSE、SWITCH/CASE、循环、异常、公共流程正式资源合同。
- DBC 英文协议原名、中文 DisplayName、位编码与引用修订；冲突不得静默择值。
- KEY_SIM 是 20 路电阻网络开关形成五路模拟电压；一个直接 KEY 步骤只有完整 20-bit mask 与保持时间；末尾无隐式 ReleaseAll。
- 手动台架与 TEST 同一设备事实源；自动 TEST 与人工工位互斥；CAN TX ACK 不等于 DUT ACK，更不等于判定 PASS。
- UI 保持浅色、完全离线 Windows WPF/Prism；不借修改 UI 之名扩充 CAN/USB 授权；无 Mock 物理执行。
- 旧截图或 HTML 与新用户明确 UX 目标冲突时，新设计可以提出替代方案，但须经用户接受及 PM 审查后才能替换正式 UI 基线。

## 2. 对现有代码的审查记录

| 文件 | 实有能力 | 需整改的 UX 差异 |
| --- | --- | --- |
| Shell/MainWindow.xaml | Shell 菜单/命令/文档标签、工程树、右 Inspector、底部运行信息、分割条 | 统一颜色与布局密度；底部运行面板默认按编辑/调试模式切换，避免无关空区 |
| Shell/Navigation/ProjectExplorerView.xaml | 工程与资源节点、资源搜索、勾选 | 分类、缩进、数量状态和实际动作；用户操作层级更清晰 |
| Testing/TestEditorView.xaml | 紧凑步骤行、阶段/Slot、搜索、菜单和大 Popup | 阶段空白提示不足、常用插入流程过重、独立参数编辑和动态落点不够 |
| Testing/TestEditorView.xaml.cs | InspectorHost、右键、F2、撤销、单项拖动、快编 Wait/Loop | TryLeaveInspector 校验阻塞导航；Drop 只 MoveStepAfter；Single SelectedItem |
| Testing/TestEditorViewModel.cs | 正式 TEST 事务、资源选择缓存、StepId、可撤销插入 | 当前 Inspector 中间态强耦合保存；错误时导航锁定；每次选择可能显示 Loading |
| Testing/TestStepInspector.xaml(.cs) | 根据 Write/Read/KEY/DBC 构建参数、应用属性 | 尽量改为持续编辑 Form Draft；字段级提示；根据步骤类型提供专用控件 |
| Testing/StepRowProjection.cs | 可扁平化树结构、阶段/Slot/Step 摘要 | 多选状态/拖拽落点/结构视觉；保留虚拟化 |
| Themes/Tokens/{Colors,Metrics,Typography}.xaml | 初步集中令牌，Segoe UI 和默认 12px | 统一覆盖视图中硬编码颜色、间距、字体 |
| Themes/Controls/*.xaml | Button/Panel/Navigation/Table 共享样式 | 交互强弱、边界/悬停/禁用/无效和行密度的完整体系 |

注意：源中已经有一些紧凑行、右侧 Inspector 和步骤事务能力。本研究不是要求重复实现已有功能。

## 3. 新的视觉令牌与布局

建议视觉色板（设计建议，不是已批准颜色）：
- AppBackground #F5F7FA；EditorSurface #FFFFFF；ToolSurface #EEF1F5；Separator #DFE4EA
- TextPrimary #202B38；TextSecondary #617286；ActionPrimary #2463A6；Selection #EAF2FB
- Success #2B7857；Error #B44442；Warning #99661F；Hover #F7F9FC
- 不用大块淡蓝/淡绿/淡黄区分阶段，阶段仅用细标记和标题文字。主操作色只用于主要行动和当前选中。
- 中央主结构采用少框/细分隔线。避免每个阶段、步骤、设置项都加嵌套圆角卡片。
- 默认紧凑：顶部菜单 34px；命令栏 36px；文档页签 32px；阶段头 34px；步骤行 32–36px；输入控件 28–32px；基础文字 12–13px；正常圆角 3–5px。
- 默认布局以约 1440x900 进行研究，必须同时验证 1366x768、1920x1080、100%/125%/150% DPI。Windows 工作区内最大化，不能盖住任务栏。
- 工程树默认约 245px（允许拖动、可记忆），Inspector 默认约 375px（建议范围 320–480），中间其余。不能在 1366 下硬挤 DBC 编辑控件。
- 编辑场景下底部日志/变量/输出默认收起为可见标题条；运行/调试时可自动展开，可手动保持用户布局。
- 提供“恢复默认布局”，不要第一轮实现任意浮动、任意停靠的复杂窗口系统。
- 所有工作区和工具使用同一令牌；手动台架仍为其专业任务的左 CAN 发送、中 KEY/IGN/夹具、右 CAN 监控，不能强制套用 TEST Inspector 布局。

## 4. TEST 编辑器的三层状态

A. FieldDraft（UI 瞬态）：按 StepId + FieldKey 保存用户输入，允许格式无效，允许切换步骤但保持未完成输入；记录字段错误，不能静默把无效值写入 DSL。
B. TestDocumentDraft（正式 TEST 事务）：有效修改以最小原子事务进入 StepEditTransaction；支持撤销/重做、稳定 StepId、无失真。
C. PersistedResource（工程资产）：由现有工程保存合同持久化，并校验引用版本/SHA；保存失败保留 A/B，不关闭或覆盖。

规则：
- 单击步骤始终可以切换到新 Inspector；旧字段缓存不能在新步被覆盖；过期异步资源加载不得回写错误的 StepId。
- 输入格式错误显示在字段附近，并在问题列表聚合；可继续浏览/编辑其他步骤。
- 正式保存前阻止存在未提交且无效 FieldDraft 的资源写入，但不要阻止查看和切换。
- 有效数值的实时更新进入正式撤销栈；建议按焦点离开或 250–400ms 的输入空闲窗口提交，避免逐字符产生不可用的撤销记录。
- 非法旧字段/不支持步骤保留原始结构与标识，必要时编辑只读并给出具体缺失合同，不自行转义执行。

## 5. 单一测试用例编辑流程

### 5.1 空用例
- 顶部显示名称、用例阶段、脏标记、检查、运行（门禁）、保存。
- 准备/测试/清理在中央显示为三条紧凑阶段行；默认焦点选中测试阶段且提供“添加步骤”。
- 空白内容区域可提供常用命令、从工程 DBC/已有步骤拖入的可操作入口；不加入无关仪表盘。
- 常用操作（CAN 发送、KEY 组合、IGN、读取信号、等待、判定、IF、循环）1–2 个操作可插入；长尾操作用 Ctrl+Space 过滤搜索。

### 5.2 步骤插入与选择
- 明确前插/后插/容器内部槽。新建后生成稳定 StepId，选中新行，右边定位第一个必要字段。
- 中央只承担步骤顺序/层级/摘要/状态/断点/插入；右边直接修改当前步骤参数。
- 单选激活 Inspector；Shift/Ctrl 多选多个 StepId 后，Inspector 只显示公共属性与合法批量操作，不能以第一条步骤的参数覆盖其余。
- 右键给当前步骤/阶段/子槽独有命令，不把不适用命令排成统一长菜单。

### 5.3 拖拽与键盘
- 合法落点：Before(row), After(row), Into(slot)，分别用明确横线/子槽背景引导；非法落点显示不可放置和原因。
- 通过 StepEditTransaction 一次性完成合法移动，失败则无副作用；不允许把容器拖入其子孙节点。
- Ctrl+C/V、Delete、Ctrl+Z/Y、F2、F9、Alt+Up/Down 保留；多选移动/复制操作必须重审 StepId、父子关系与原顺序。

### 5.4 专业步骤 Inspector
- KEY：完整 20-bit Mask 与保持 ms；20 个 K 格子、HEX 输入、摘要同步。0 与业务“无操作”分开。无隐式全断开。
- CAN：原始 HEX / 工程 DBC 互切；ID（hex/dec）、协议原名/中文别名/注释搜索；选择 DBC 报文即加载信号编辑器，Intel/Motorola/比例/偏移/符号/枚举经现有编码库产生原始帧预览；冲突与资源失效必须可见。
- Read / WaitUntil / Expect：分别强调读取来源、判定条件、期限、变量、运行态；不可把 CAN RX 混同信号满足/测试 PASS。
- IF/ELSE、SWITCH、LOOP：容器子槽结构可见，允许在内部直接插入、复制和移动；界面不许提供未批准的实体运行能力。

## 6. 工具与运行态统一表现
- 全局状态栏保留 USB 设备身份/连接世代、CAN0/1、工程/文件状态、运行授权/UNKNOWN。
- 同一文档窗口以页签管理；不让专业工具覆盖工程树；复杂工具允许独立窗口，但须明确唯一资源事实源和返回编辑器的路径。
- 手动台架独立体验，遵守共享颜色、字号、快捷键、确认层级与日志语义，不需要复制测试步骤表的结构。
- 自动 TEST、扫码生产、手动操作权限不能由按钮颜色或禁用态代替真实服务门禁。
- 编辑态与运行态分离：允许编辑合法离线草稿，不代表同一草稿已可用于实机。运行中的事实结果不写回静态源文件。

## 7. 施工顺序（经用户/PM 确认后 G4 执行）

R-0：冻结本研究对应 Git SHA 与当前主线源码对照，记录基线截图与操作视频（由用户或本地 Codex 采集）。不得修改硬件协议。
R-1：UI branch 唯一 HTML 中验证主题/密度与空用例、丰富用例两个完整页面。用户确认交互效果后才能转 WPF。
R-2：WPF Shell/Theme 令牌整理，导航和顶栏保持原功能，修改纯视觉与尺寸；构建启动通过后独立检查。
R-3：替换当前硬阻塞 Inspector 导航；建立 per-StepId 字段草稿 + 正式 TEST 事务提交边界；检查刷新、资源异步加载、切换与撤销。
R-4：步骤选择、上下文插入、多选、拖拽 Before/After/Into、复制移动、断点，验证所有 StepId/父子结构。
R-5：CAN/DBC、KEY、Read/Wait/Expect、流程控制专属 Inspector 交互；功能使用现有领域适配器。
R-6：长用例性能、错误恢复、保存重开和真实执行前预检；不同屏幕/DPI；最后给 PM 兼容审查。

明确可删重建：TestEditorView.xaml 当前 Palette 大弹窗布局、TestStepInspector 旧“应用属性”形式和相关阻塞式切换 UI，旧无效的硬编码颜色。
明确保留：TEST DSL/StepNode/StepId/StepEditTransaction、真实 Run 及硬件 Service、工程资源/DBC/KEY 领域编码和版本/权限检查。
有疑问需调查：Inspector 的旧格式兼容写入边界、复杂子槽多选原子事务支持、跨工程复制、异步校验的取消语义。不能默认为已实现。

## 8. 用户可直接执行的验收场景（设计目标）

A1. 新建空用例：不用手动先创建步骤也能看到三个阶段及真实可点的插入入口。
A2. 选择 TEST → CAN发送 → DBC报文：可直接输入物理值，立即有摘要与原始帧预览，不需第三个窗口。
A3. 选择 KEY：HEX 6144 → 20 Key 勾选同步 → 保持1000ms，跳到另一条、回来、数据不变；无自动 ReleaseAll。
A4. 故意输入无效字段，再选另一条步骤：切换成功，错误草稿仍可回到原步骤查看；保存/预检明确拒绝未完成值。
A5. IF/ELSE 内新建三条步骤，分别前插、后插、拖入 THEN 与 ELSE，落点与结果一致，撤销重做不变更未移动节点的 StepId。
A6. Shift/Ctrl 多选相邻及非相邻步骤，复制、移动和撤销；不合法跨槽批量操作显式拒绝。
A7. 存盘 → 关闭 → 重开，步骤顺序、用户名称、独立摘要、DBC 引用、KEY mask、断点、StepId 保持。
A8. 旧 DBC 重名/别名冲突、文件版本变动：无静默转换或隐藏丢字段。
A9. 1366x768 与 1920x1080、100/125/150% DPI，关键功能入口可见，窗口最大化不覆盖任务栏。
A10. 100 个步骤、IF/ELSE 嵌套、Inspector 连续切换：不出现明显阻塞或全窗口反复 Loading。
A11. 无设备/USB断开时可以继续离线编辑，运行入口严格预检；USB ACK 不是 ECU 响应/通过。
A12. 开关专业工具并返回原测试编辑文档：页签、步骤选择、右侧草稿和分栏宽度可恢复。

上述 A1-A12 为后续验证目标，**不是现在已经完成的测试报告**。

## 9. 已研究的成熟产品资料（外部参考，不是 AutoTest 事实）
- Vector vTESTstudio Test Design Editors： https://www.vector.com/en/product/vteststudio/test-design-editors/
- NI TestStand Step Settings Pane： https://www.ni.com/docs/en-US/bundle/teststand-api-ref/page/tsref/step-settings-pane.html
- NI TestStand Steps Pane： https://www.ni.com/docs/ar-EG/bundle/teststand-api-reference/page/tsref/steps-pane-sequence-file-tab.html
- TraceTronic ecu.test Work window： https://docs.tracetronic.com/help/ecu.test/en/Main-program/Editor/Test-case-editor/Work-window.html
- Visual Studio window layout： https://learn.microsoft.com/en-us/visualstudio/ide/customizing-window-layouts-in-visual-studio
- JetBrains Compact Mode： https://www.jetbrains.com/help/idea/new-ui.html

## 10. 交付边界与批准事项
- 当前仅是 UI 专业研究成果，新的 CSS/控件构想没有直接修改 WPF、没有覆盖现行唯一 HTML、没有改 TEST/硬件接口。
- 下一道关口：在 ui/ide-design 的唯一 HTML 中制作可操作的 TEST 编辑器 V3 迭代，用户体验评审；随后按项目流程送 PM 做领域/硬件兼容审查，再进入受控 WPF 实施。
- 未批准之前不得称 V3 视觉冻结、G3/G4 PASS，不得宣称已完成本地编译或真实设备实测。
