# AutoTest｜G4-UI-ONE-SHOT｜专职执行 AI 完整实施任务

**任务编号**：G4-UI-ONE-SHOT-20261115  
**状态**：**AUTHORIZED / ONE_EXECUTOR / ONE_COMPLETE_USER_HANDOFF**（G3 已批准，G4 UI-00～03 授权实施）  
**负责人**：一个独立的、可直接访问 GitHub 仓库并编写代码的执行 AI。项目管理对话只监督成果和后续实测问题。  
**原始源码基准**：`local@a9a26ebfba1efcf032ff8ec8c435336a89469647`  
**唯一码分支**：`feature/g4-html-parity-20261115`，已基于上述 `local` SHA 创建，禁止更换父基线/重新混入 `main`。  
**唯一 UI 基准**：`main:docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html`，Git Blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`。**此 HTML 是自动测试视觉与交互权威，不是必须复制执行的 JS 演示业务代码。**

## 0｜一次性委托的含义

你承担从开始阅读源文件、定位引用、重建 WPF XAML/VM、接入真实工程编辑、删除旧 UI、Git 提交直到交付**一个完整可供项目负责人本地拉取并实测的候选**的全部实现工作。不要把它拆成多个“先设计、先建立框架、等用户看 UI、再接项目、再加 Inspector”的用户批准回合；不用重复询问已经在 G3 和 PM-BIZ 明确的事项。内部可以多 commit，但**对用户交付一次完整候选**。

**不能用用户未授权的真实 ECU 写入来验证**。本委托主要是 UI + 真实离线工程编辑、调用既有受控能力。步骤/生产/Flash 等尚未真实接通的功能不能假装 PASS。用户拿到候选后会亲自 Windows 编译、打开操作、提供错误原文和视觉截图；执行 AI 后续只依据本次完整交付的实际问题修复。

## 1｜必须先阅读，且不可自行换标准

1. [G3 正式批准记录](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/G3-APPROVAL-20261115.md)
2. [G3 交付入口](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/G3-R1-20261115/README.md) 及 `G3-00～05` 全文；
3. [冻结 HTML](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html)，**完整读取 CSS 最终覆盖、DOM、JS 实际操作与右键/键盘逻辑**；对比时以用户已认可的整个自动测试部分为准；
4. [产品业务决策](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/PM-BUSINESS-DECISIONS-20261114.md) 与 [过度按键化勘误](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/PM-INPUT-SEMANTICS-20261114.md)；
5. 检查真实 `local` 的 `MainWindow`、TEST 项目/步骤/Inspector/工程资源/DBC/Prism 注册/设备代码等**实际函数与依赖**，不得只凭文件名盲删。

## 2｜必须实现的完整可操作自动测试 WPF

**视觉严格一致，不允许设计 AI 随意发挥。** 默认最大化、浅色现代 IDE、顶部紧凑工具栏、左工程/项目用例树、中部多文档高密度测试步骤和项目列表、右侧常驻 Inspector、底部运行/日志/变量等辅助面板、状态栏；各区域宽度比例、字体行高、色彩、层次、可折叠及拖动能力，均以冻结 HTML 和 G3-01 为准。不要采用 WebView2 直接运行 HTML，不使用 HTML 截图铺底冒充 WPF。

**必须贯通的操作**：
- 真工程的创建/打开/保存/另存/重开/最近资源、树节点增删/编辑/重命名/导航；对旧工程版本给诊断，不自动覆盖。
- 测试项目创建/添加、删除、排序用例引用，保存默认 `Enabled`，临时勾选选择只对当次运行有效，初始取保存 `Enabled`；扫码生产不读取工程师临时勾选。
- 测试用例创建、重命名、准备/测试/清理阶段，步骤按 HTML 的真实可展开层级及摘要表展示，新增立即选中可编辑，复制/粘贴、移动、删除、流程节点子分支/嵌套编辑、查找与定位。
- Inspector 显示选中 Step 的完整参数、校验、资源引用及实时草稿；DBC 选择报文、信号、物理单位/值时使用真实解析与映射，不按错误字符串默认 0；没有 DBC/Controller 资源时提供实际错误/入口，不出现虚构 EPB 按钮或 K01-K20 业务按键。
- 鼠标左右键、右键菜单、F2/F9、Ctrl+Space/Ctrl+F、Ctrl+Z/Y、Delete、拖动、行内常用编辑，行为和焦点按 HTML。文本框内的 Delete/Undo 不可误删步骤或撤销已下发设备指令。
- 工程草稿/撤销/重做/保存安全、确认关闭、文件损坏、资源失效、未定义参数、错误位置显示。
- 连接、扫码、运行、波形、日志、报告等已有正式应用入口**在新 Shell 上重新接线**；已有真实能力保留受控调用，不得用假日志/定时动画代替；目前执行器不支持的步骤需要明确预检阻断、不能删掉编辑入口或假称运行成功。
- UI 工作区可以打开相关专业工具的现有正式能力入口，但**本任务不要求推翻尚未经过用户验收的 DBC/UDS/Wave/Flash/手动台架完整专业界面**；独立 CAN 工具不是当前产品要求。
- 不因 UI 研究继续修改 `ui/ide-design` 工作 HTML 而漂移本轮严实现标准。

## 3｜删旧 UI：一次重建，无兼容层

删除/整体重写旧：
- `src/AutoTest.Desktop/Shell/MainWindow.xaml`、`MainWindow.xaml.cs`、`MainWindowViewModel.cs`；
- `src/AutoTest.Desktop/Testing/TestEditorView.xaml`、其 code-behind、旧编辑 VM；`TestProjectWorkbenchView*`、`TestStepInspector*`；
- 旧 `StepParametersDialog`、`TargetMappingDialog`、`TestEditorDialogs`、过时 `StepUiCatalog` 和仅为旧 TreeView 提供的演示控件等，按**调用方全部迁移后**删除；如旧文件同时承载业务服务，则先迁移业务再删原表现类；
- 旧与新之间不得通过 `LegacyMode`、隐藏 Tab、兼容窗口、备用 Inspector 双实现等维持第二套 UI。

**必须保留**：`TestDocument/StepNode/TestProjectDocument/TestTargetMappings/StepEditTransaction`、PROJECT/DBC/Controller/TCF/TBC 资源与文件生命周期、`AutoTest.Devices` 的真实 USB/CAN 与物理 Fence、UDS/Wave/Scan/Record 正式服务以及实际数据证据；必要时在 `AutoTest.Desktop`/Application 新建**唯一**业务适配服务，不因文件最初位于旧 VM 就把真正功能删光。

此候选源码与 `main` 存在分叉：**不得直接合并 `local` 或本分支整段历史到 `main`**；后续通过用户实测与 PM 精确 Diff 审查后选择性集成。当前不允许执行 AI 自行合并 main。

## 4｜真实功能与不足的处理

- 所有编辑器都操作真实资源数据，必须能 **保存→关闭→重开** 并保留结果；不得用内存演示数据、Mock 测试器、浏览器 `localStorage` 存用户工程。
- IF/SWITCH/WHEN/TRY/CALL/UDS/Wave 等步骤若 HTML 可以编辑，必须提供与后端结构一致的参数入口和验证；现有不支持实体执行的类型给明确 `NOT_EXECUTABLE` 等阻断，**不可用仅高亮模拟来“通过”**。
- 已有 USB/CAN 连接受控逻辑、单用例正式解释器可以复用；未经 G4 进一步授权，不要把单用例 `ExecuteAsync` 简单循环造成错误包间 Cleanup，也不要绕过 `PhysicalCommandFence` 以实现“全部步骤可运行”。
- 生产扫码入口必须保留正确的预检/受控自动启动业务目标；若尚无真实多用例项目 Host，不允许把旧 `ProductionWorkflow.MockObservation` 路径接为正式生产 PASS。
- 自动测试/手动台架互斥，DUT 主电源在外部，不加主控供电按钮。
- 本工作包不包含 F2 实体刷写，不允许改任何主控/KEY_SIM 固件，不允许新增 Mock、Simulation、Replay 执行模式。

## 5｜交付方式：一次性完整候选，不强制 AI 反复编译

你在 GitHub `feature/g4-html-parity-20261115` **直接改代码并提交**，不上传 ZIP，不要求用户手动覆盖文件。优先确保完整静态引用/命名/Prism 注册/XAML 资源准确；用户明确不希望为了等待长时间 CI/构建阻塞研发，因此**不要触发 GitHub CI，不循环执行耗时 Build/Test**。如果当前环境适合做一次必要的局部静态检查可以执行，但不得因此停在中途等待；**未编译不能写 Build PASS**。

交付前人工审查：
- 旧 UI 入口是否全部消失，是否还有死引用/未注册 View/重复 Shell；
- WPF 实现是否逐项覆盖 G3-01 U-01～U-16 / G3-05 首批验收场景；
- UI 控件是否真的绑定工程数据和编辑命令，而不是只画静态页面；
- 错误、禁用、取消、保存是否与真实服务状态绑定；
- 整个修改是否仅在授权的 feature 分支，`main`/固件/Flash 专项没有被污染。

### 最终一次性交付格式（不能用开发进度替代）

1. **确切 GitHub 分支和 HEAD 完整 SHA**，本轮完整提交列表以及相对原始基线的变更摘要；
2. **删掉的旧 UI 文件、重建的新 View/VM、保留接线的后端服务**逐项清单；
3. 按 HTML U-01～U-16 表说明 `IMPLEMENTED / BLOCKED_BY_BACKEND / REQUIRES_REAL_DEVICE`，说明证据，不得全部虚报 Implemented；
4. 一份**用户本地启动命令**（工程文件以当前实际解决方案为准，绝不编造 DLL 路径）及推荐的完整手动测试操作顺序；
5. 已知问题、未做的编译/实体测试、真实物理操作安全边界；
6. **交付后立即停止改动和继续变更候选**，等待用户集中 Windows 实测后再按原始错误/截图处理，不在用户测试期间自行换基准；
7. 此时才给用户提交可拉取版本，**不自行合并 `main`**。

**关键验收条件**：用户打开真正的 WPF 上位机时，看见的不是新的“AI 设计”，而是冻结 HTML 所定义的自动测试主界面；从测试工程→项目→用例→步骤/DBC 属性→保存重开可以连续工作，不能为了兼容旧界面随意牺牲操作体验。

**立即开始代码实施，不需要下一轮 G3 分析或再次请求授权。**
