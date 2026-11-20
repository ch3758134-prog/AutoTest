# V02-01｜结构化测试编辑器：成熟产品操作与视觉联合研究

**日期：** 2026-11-19  
**角色：** AutoTest UI/UX 研究负责人  
**阶段：** V02 / 第一批（结构化测试编辑器）  
**状态：** EVIDENCE_REVIEWED / RESEARCH_CANDIDATE / VISUAL_OPERATION_MAPPING_COMPLETE / USER_REVIEW_PENDING  
**约束：** 不改 WPF；不从当前失败实现推导正常产品设计；本轮不冻结 AutoTest 最终页面尺寸、颜色、窗口结构。仅在真实用户任务下对成熟产品的公开资料做有边界的分析。  
**前置：** R01～R06、V01 操作与视觉一体设计原则、用户 2026-11-19 Windows 首轮截图与批次验收授权。

## 0. 本轮研究什么，为什么不等于“再看几个截图”

用户当前失败截图里：左侧两个角色（用例选择、步骤编辑）被挤压、中央大片空白；“准备/测试/清理”结构难辨；高频按钮没有主次；选中等待步骤却没有直观对应的参数编辑。问题不是颜色、对齐单项，而是**对象、任务、空间、反馈之间的关系没有成立**。

本轮聚焦完整任务：

> 工程师打开一个测试用例，阅读 20+ 个含 Prepare/Test/Cleanup、IF/ELSE、WHEN/循环、CAN 信号判定、UDS、外部输入、变量的步骤；插入、选中、修改参数、引用 DBC、复制/上移/撤销、检查错误，然后返回其他用例继续工作。

每个成熟产品案例按“实际对象 → 操作路径 → 视觉承载 → 优点/代价 → 对 AutoTest 的迁移条件”分析。**不以图像是否好看代替研究。**

## 1. 研究样本与版本/来源

### 1.1 NI TestStand

- **官方机制 S-T1：** Step Settings Pane 作用于当前 Sequence File 或 Execution 窗口的 Steps Pane 中选中步骤，提供内置属性、类型专用属性及 Module 调用；多选时显示共同属性。**官方明确说明自定义 Step Type 的特定元素不一定支持该 Pane，需弹窗**，因此不能把 TestStand 说成“所有步骤均无弹窗”。
- 一手资料：https://www.ni.com/docs/en-US/bundle/teststand-api-ref/page/tsref/step-settings-pane.html
- **官方机制 S-T2：** Steps Pane 用 Step/Description/Settings 等列分别呈现步骤身份、不同类型摘要和非默认设置；列表有可配置显示方式/字体。步骤组与步骤实例可在一个连续区域被扫视、选择。多字节语言的字体支持还有特定限制，不应直接复制其默认字体。
- 一手资料：https://www.ni.com/docs/ar-EG/bundle/teststand-api-reference/page/tsref/steps-pane-sequence-file-tab.html
- **视觉样本：** NI 官方旧版 Sequence Editor 截图（不是 2026 版完整外观）：https://www.ni.com/cms/images/devzone/tut/image7988486561481583849.png ；出处 https://www.ni.com/en/support/documentation/supplemental/06/guide-to-effective-test-software-development-with-teststand.html
- 样本可辨部分：左插入调色板、步骤中心列表、右序列/变量区、下 Step Settings。它**证明这种工作空间组织真实存在**，但不能证明其固定比例在现代中文 1366×768/150% 最优。

**操作与视觉共同解释：**
1. Step 为“当前操作对象”，Settings 不是独立功能页，而是**被选中对象的编辑工具**；视觉需显示对象身份和参数联动，不能只在右侧写“步骤详情”。
2. Steps 列通过同时展示名称、摘要和设置减少反复打开详情的需求；但长值/多语言时列宽竞争会加大。
3. 下方属性区便于宽表，但会降低可见步骤行数，必须依赖可调整的纵向空间与不同内容密度。

**可迁移：** 结构对象主区域、类型化参数、批量/连续选择、步骤摘要可扫视。  
**不直接照搬：** 旧版图标和视觉皮肤、所有专用参数都压在底部的默认布局、固定窗口占比。

### 1.2 tracetronic ecu.test 2026.2

- **官方机制 S-E1：** Action Window 每个条目表示可插入的潜在步骤；右键菜单可在当前标记位置插入，或拖放到释放位置；某些步骤插入前仍会弹参数化对话框；按住 ALT/使用右键拖放会改变候选插入形式。
- https://docs.tracetronic.com/help/ecu.test/2026.2/en/Main-program/Action-window/Inserting-test-steps.html
- **官方机制 S-E2：** Work Window 的 Test Case Tab 上部为步骤、步骤备注和 Tags，下部为变量、类型和 Local Mapping；同一 Package 的 Properties、Signal Recordings、Trace Analysis 和 Test Report 各有独立内容页。
- https://docs.tracetronic.com/help/ecu.test/en/Main-program/Editor/Test-case-editor/Work-window.html
- **官方机制 S-E3：** 对 TEST 快捷键可见：Ctrl+Space/双 Shift 调用 EasyInsert；Ctrl+Up/Down 移动步骤（穿越块的版本另有快捷键），Ctrl+Z/Y 撤销/重做，Space 隐藏/显示，Ctrl++/- 展开/折叠。反映的是“按钮并非唯一操作路径”。
- https://docs.tracetronic.com/help/ecu.test/en/Usage/Keyboard-shortcuts.html
- **官方机制 S-E4：** 选中步骤的右键菜单有停用、展开、断点、跳到 Mapping Viewer；问题检查工具可双击问题定位关联步骤。
- https://docs.tracetronic.com/help/ecu.test/2026.2/en/Main-program/Editor/Test-case-editor/Test-step-context-menu.html
- https://docs.tracetronic.com/help/ecu.test/en/Main-program/Editor/Test-case-editor/Toolbar.html
- **视觉样本：**
  - 2026.2 教程中的多层树、Precondition/Action/Postcondition：https://docs.tracetronic.com/help/ecu.test/en/_images/Package.png
  - Package Editor 的步骤/参数/期望/变量分区：https://docs.tracetronic.com/help/ecu.test/de/_images/Packageeditor_Package.png
  - 这些截图来自文档示例，不保证是完整默认窗口状态，更不是 AutoTest 完整设计稿。

**操作与视觉共同解释：**
1. 当步骤有层级、结构和类型时，**编号、缩进、展开提示、不同步骤类型标记**承担语义；不能将阶段与步骤全部渲染成相同字号的蓝色小字。
2. “Action/Name、Parameter、Expectation/Value”可在结构表中形成概要，让用户不必每点一步才能知道大概在做什么。
3. 快速插入同时有菜单、拖放和键盘路线；对 AutoTest 应研究命令作用域、插入前/后定位和错误恢复，而不是简单增加所有按钮。
4. ecu.test 部分插入需模态参数化，AutoTest 的高频连续编辑可以**提出**“插入后在持续详情区直接编辑”的替代候选；这属于本项目推论，非 ecu.test 已这样做的事实。

**可迁移：** 插入位置明确、不同角色的结构节点、摘要列、对象右键与快捷键、错误到对象导航。  
**不直接照搬：** 其具体步骤树图标、参数化弹窗频率、每个下部辅助面板的固定布局。

### 1.3 JetBrains IntelliJ IDEA（视觉与工具窗证据，非测试软件）

- 2025 Islands 主题设计说明：JetBrains 表示目标是让编辑器与工具窗区分更清楚、导航更突出、Tab 更易辨认；它公开提到使用 mockup survey、A/B、访谈、使用统计来比较两种设计方向。
- https://blog.jetbrains.com/platform/2025/09/islands-theme-the-new-look-coming-to-jetbrains-ides/
- 2026 官方 UI 说明：工具窗图标、工具窗位置/拆分、编辑器 Tab 字体及空间与 compact mode 可调整；树层级指南可显示，工具窗主/辅助动作可以通过菜单、上下文与键盘共享。
- https://www.jetbrains.com/help/idea/new-ui.html
- https://www.jetbrains.com/help/idea/tool-windows.html

**操作与视觉共同解释：** 工具窗与主编辑对象必须在视觉上被辨认成不同职责的区域。改进 Tab 可读性、让工具窗收纳不等于隐藏任务。**JetBrains 自己通过用户反馈决定视觉方向**，说明不能拿一张漂亮样板直接冻结 AutoTest。

**边界：** IDE 代码编辑的肌肉记忆、Git 工具窗与 AutoTest 的 TEST 用例/实体执行流程不同；不建议机械复制窄图标栏。

## 2. 同一用户任务的证据矩阵：操作行为如何规定视觉

| AutoTest 任务 | 来自成熟产品的可核实机制 | 必须体现在视觉上的关系 | 失败反例 / 需要测试的取舍 |
|---|---|---|---|
| 找到当前用例和测试阶段 | NI 步骤组、ecu.test Precondition/Action/Postcondition | 标题、阶段区块、步骤树、当前用例身份各自独立 | 当前阶段和步骤同一小字、视觉上看不出操作目标 |
| 浏览 20+ 步骤及嵌套分支 | NI Steps Pane 列，ecu.test 缩进/折叠 | 编号、层级、节点类型、摘要、选中高亮清楚 | 左侧 180px 树过窄、中央留白 |
| 添加指定类型步骤 | ecu.test Action Window 右键/拖放/EasyInsert | “新增”主命令靠近步骤列表；新节点明确出现在哪里 | 所有命令等权重、先选类型仍不知道插入位置 |
| 选中后持续改三个字段 | NI Step Settings、Ignition 当前对象属性联动（R02） | 选中节点与参数区域标题/类型/字段相互对应 | 选中后 Inspector 仍为“请选择步骤” |
| 连续修改若干相邻步骤 | NI 编辑区、ecu.test 快捷键 | 主结构和详情应同屏或仅最少切换；草稿输入与保存状态清晰 | 每个字段弹一次模态窗、选中切换后丢草稿 |
| 上移、下移、复制、撤销 | ecu.test 快捷键及上下文命令 | 操作在对象附近可发现；主/次命令区分，右键和快捷键一致 | 上移、下移、复制、撤销堆成多行同重按钮 |
| 流程控制 IF/ELSE / LOOP | ecu.test 层级、NI 组/不同步骤类型 | 分支容器与子节点有清楚层级、折叠、插入目标 | ELSE 只是一个与 WAIT 同样的普通子行 |
| 检查错误并定位 | ecu.test 包检查结果双击定位 | Problems 与步骤/字段可导航联动，错误明确附着对象 | 只有顶部“发生错误”却不知道哪一步 |
| 读取当次运行与编辑草稿区别 | NI Editor/Execution View 与 R05 | 运行选择/事实不能只靠与编辑节点相同的蓝色表示 | 高亮=选中=运行中=通过，造成误读 |

## 3. 人因与视觉问题提炼（不是控件样式补丁）

**问题 Q1：页面为什么“大面积空白而工作区很挤”？** 根因候选是布局网格未按任务对象分配空间：步骤列表是主要编辑对象，却被当作局部工具窗；而未使用的中央区域没有工作职责。先进行布局结构试验，不应先调整按钮颜色。

**问题 Q2：为什么“准备/测试/清理”难识别？** 概念上阶段是容器，步骤是子对象，必须采用不同视觉层级（标题重量、区块边界、缩进、编号/图标）和不同交互行为。不能只靠 Row 的文字。

**问题 Q3：为什么动作按钮难用？** 添加（创建对象）与上移（改变结构）、撤销（历史编辑）、删除（可能危险）不是等权重命令；应该依据频率、可恢复性和当前上下文组织为分级命令面。用颜色区分只是表现的最后一环。

**问题 Q4：Inspector 需要放哪？** 不应预先固定“右边”。先比较两种共存模式：
- 右侧持续属性：跨步骤连续改值效率可能较高，挤压横向宽度；
- 下方参数工作台：给结构宽表留横向空间，挤压可见步骤数量；
其价值必须在 1366×768/150%、1920×1080 下用真实内容检验。

## 4. 第一批研究的结论

1. **主要编辑对象必须是视觉重心。** 在 TEST 编辑模式下，步骤结构在屏幕上占主要工作区；用例导航和参数区配合它，而不是独占中央空白。
2. **对象和属性的联系必须显性。** 选中步骤后，属性区标题、类型、字段、有效性状态应立即一致。
3. **不同层级的节点必须拥有不同视觉与操作语义。** Prepare/Test/Cleanup 是阶段容器；IF/ELSE 等属于流程结构，不能当作普通等待步骤显示。
4. **高频命令就近、低频命令按需、快捷键一致。** 设计命令表面时要同时研究如何发现、何时可用、作用于哪一层、如何撤销。
5. **空间和密度需要实际验证。** 不从厂商文档机械复制 30%、40%、30% 或固定像素；所有候选以同一组内容和连续任务比较。
6. **执行事实和编辑状态仍保持独立。** 选中蓝、草稿未保存、断点、正在运行、PASS/FAIL/UNKNOWN 不允许只依靠不同颜色的同一个符号。

## 5. 本轮尚缺证据

- 无 TestStand、ecu.test、JetBrains 应用的本项目独立真人上机实测，仅依据官方公开文档/截图。
- 厂商截图不是完整最新版 WPF 设计规范，不能用视觉轮廓判断性能。
- 未验证 AutoTest 字体、组件响应式缩放与中文字号；本轮不冻结尺寸和设计系统 token。
- 尚未覆盖 TEST 的全部专业步骤类型及变量、子程序、引用版本等详细属性编辑；在 V02-02 中展开。
- V02-01 不构成 WPF 可实施设计准出。**未经用户审查和原型任务测试，不得将两套候选中的任意一套作为已批准界面。**

## 6. 下一成果关系

- V02-01-DESIGN_COMPARISON：对同一个用例内容，提供不同结构/参数区域方案，明确为什么能解决上述任务，以及可能产生的新问题。
- V02-02：在更完整的 20+ 节点/多步骤类型/错误状态中细化结构层级、命令路径、参数编辑行为及键鼠反馈。
- V03：结合同类 DBC/Wave 专业任务研究，形成跨工具一致的视觉—操作模式选择。
- 最终分批用户审查后，才有条件进行 WPF 的准确视觉实施。

**阶段结论：V02-01-RESEARCH_RECORDED，未开始 WPF 修改。**
