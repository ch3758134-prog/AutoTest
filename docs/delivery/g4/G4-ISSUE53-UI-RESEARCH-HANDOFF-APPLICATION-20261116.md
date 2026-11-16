# AutoTest | G4 Issue #53 | UI 研究专项交接与 WPF 执行映射

**状态：CODE_CANDIDATE / WINDOWS_BUILD_UNVERIFIED / HTML_PARITY_OPEN / NOT_GATE_ACCEPTED**

- 来源：用户于 2026-11-16 提供的《AutoTest｜G4 WPF 自动测试工作台操作链、逻辑链与视觉设计专项澄清指令》（完整 15 章、954 行）；本文件为**逐项实施映射与差异台账**，不是替代源设计的新产品定义。
- 执行分支：`feature/g4-html-parity-20261115`
- 本轮开始前 HEAD：`24eafceadf0f7ea831232d928bac36e86a289f0c`
- 本文编写时 HEAD：`59a67d132c645dfe7b5ebc168f0bb7a730d9001d`。本文最终提交后请采用分支最新精确 SHA。
- 冻结 HTML：`docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html` / blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`
- 正式合同：`G3-01-STRICT-UI-PARITY.md`、`G3-03-TEST-RUN-CONTRACT.md`
- Issue：`#53`。未修改 main、`ui/ide-design`、冻结 HTML 或 TEST/Controller/Device 执行合同。

## 一、固定的产品操作、数据、视觉三条链

工程师的第一目标是**连续编写测试用例**：打开工程 → 测试项目 → 测试用例 → 准备/测试/清理 → 选择动作 → 当前 StepId 右侧配置 → 保存/静态检查 → 经正式授权运行 → 查看真实证据。不为编辑等待、IF/ELSE、空白工程强制先接 USB、导入 DBC 或配置所有 Controller 资源。

**对象边界：**
- 测试项目：正式 `TestProjectDocument` 有序引用，每条 Entry 的 `Enabled` 持久，`Revision/SHA` 固定，不静默追新。
- 测试用例：唯一 `TestDocument` + `StepNode` + `StepEditTransaction`，阶段为 Prepare/Test/Cleanup。不得另建用户可见的“测试程序”平级对象。
- 工程资源：DBC、按键动作、波形参考等仅在对应动作需要时引入。
- 本次执行勾选：`ProjectRunSelectionState` 临时会话，和工程默认启用、打开/选中哪个用例严格分离，双向绑定工程树与项目表。
- 运行/预检：编辑完成 ≠ 预检通过 ≠ 设备 ACK ≠ ECU 实际测试 PASS。无真实事实不得显示 PASS。

**视觉空间：** 上菜单/工程+连接/全局工具；左工程对象，中央工作内容，右常驻 Inspector，底部真实事件/变量/问题，最后状态栏。浅色、高密度、35 DIP STEP 行/约 45 DIP 表头；阶段三色和动作类别图标独立于选中/搜索/断点/真实执行/错误视觉。窗口最大化不得盖住任务栏。

## 二、直接改动：针对旧“先填属性再插入”的失败

| 真实操作 | 已修改的源码 | 状态 |
|---|---|---|
| 在阶段/分支添加 | `TestEditorView.xaml` 阶段与 Slot 行都有独立添加按钮；`AddAtRow_Click` 按完整 `StepSelectionKey` 检查相同阶段、父 StepId、Slot 后才能开启 Palette | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 类型选择 | `OpenActionPalette` 原生 WPF 分组搜索面板（CAN与数据/工位操作/UDS/波形/流程）；选类型单击立即执行 `ChooseNewStepAction` | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 新增后立即见到步骤 | `StepEditTransaction.Insert` 新增可撤销正式编辑草稿；`_newDraftId` 标记未完善；`RefreshTree(StepId)` 自动选中并进入实际 Inspector | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 阶段/分支右侧 | `TestEditorView.BuildPhaseInspector`：只显示阶段及添加说明和按钮；不创建隐藏的“新步骤属性配置”表单；纯导航不再等待 DBC 加载 | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 已有步骤右侧 | **已删除** `TestStepInspector.xaml` 旧 `ActionSelectorPanel`、`ActionSearch`、`ActionList` 和“插入步骤”按钮逻辑；Inspector 只接正式选中的 StepId，显式显示步骤类型、名称、专业参数和常用操作 | CODE_UPDATED / WINDOWS_UNVERIFIED |
| Inspector 同步 | `InspectorLoading` 清走上一步属性，真实操作对象读取完成后再显示本 StepId；异步请求代次避免旧请求覆盖；应用失败保留原编辑输入 | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 连续行内编辑 | Wait/Loop 默认显示工程师摘要；双击才打开输入框；Enter 直接使用 `StepEditTransaction.SetProperty`；Esc 恢复；非法输入阻止切换/搜索/保存 | CODE_UPDATED / WINDOWS_UNVERIFIED |
| THEN/ELSE/CASE | 分支自身有“＋插入”，不让用户再找右键菜单；`StepInsertionTarget` 保留合法 Slot 和 SWITCH CASE 限制，按父 StepId + Slot 区分 THEN、ELSE | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 撤销与保存 | 未完成新增不能保存/预检/实体执行；取消用 Undo 撤销插入，不产生补偿 Delete 历史；Redo 恢复待完善状态 | EXISTING + UPDATED / WINDOWS_UNVERIFIED |
| 类型语言 | 主 Palette 仅一个正式“发送 CAN 报文”（Write+DBC/Raw），不再向用户同时提供误导性 CanSend 替代入口；“按键动作”、“UDS 服务”作为用户语言 | CODE_UPDATED / WINDOWS_UNVERIFIED |

以上修改不是第二套 TEST 数据格式；后端 `ProjectWorkspaceService` / `TestDocumentCodec` / `StepEditTransaction` 等保持唯一事实源。

## 三、交接 15 章执行台账

| 章节 | 用户的实际验收焦点 | 状态 |
|---|---|---|
| 1 定位 | 操作链/逻辑链/视觉三项并行，不是保存 JSON 就合格 | APPLIED_AS_ACCEPTANCE_CONTRACT |
| 2 空白工程 | 无 ECU 也能新建工程、项目和用例，立即进入三阶段 | EXISTING / WINDOWS_UNVERIFIED |
| 3 组织关系 | 项目顺序+本次勾选+默认 Enabled+编辑焦点独立，Revision/SHA 显式采用 | EXISTING / WINDOWS_UNVERIFIED |
| 4 添加 STEP | 类型选中立即产生可撤销 StepId 并显示右 Inspector | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 5 选中与 Inspector | 一步一属性、无重复选择类型；合法草稿自动应用；非法值不能丢 | CODE_UPDATED / WINDOWS_UNVERIFIED |
| 6 流程 | IF THEN/ELSE、SWITCH CASE、LOOP Slot 真层级、子树复制/移动/删除 | EXISTING + SLOT_UI_UPDATED / WINDOWS_UNVERIFIED |
| 7 专业步骤 | CAN 通过 DBC 编辑信号/单位/范围；Raw CAN 可选；按键动作 / UDS 服务正名 | PARTIAL，合同差异见下 |
| 8 执行状态 | 只有真实 Run Fact 才显示真实结果，编辑/预检/实体 PASS 不混淆 | EXISTING，运行全链 OPEN |
| 9 视觉 | 三区分色、类别图标、选中蓝、搜索黄、状态独立；排版 DPI | SOURCE_STYLED / VISUAL_UNVERIFIED |
| 10 控件状态 | 导航、项目行、STEP、Inspector、DBC、页签、真实运行各自状态来源 | SOURCE_REVIEWED / INTEGRATION_OPEN |
| 11 业务语言 | 不向工程师暴露 GUID/TargetKind/Mapping 步骤，真实适配 | PARTIAL / PROFESSIONAL_EDITOR_OPEN |
| 12 多页签 | 同文档复用编辑会话，独立草稿，返回保持 StepId 和属性 | EXISTING + GUARDS / WINDOWS_UNVERIFIED |
| 13 Windows 十场景 | 工程→十分钟连续编辑→行内/Inspector→流程→勾选→DBC→草稿→视觉→DPI→无实体命令 | NOT_EXECUTED |
| 14 方法 | 逐个操作映射 HTML 行为 + 正式数据 + WPF 状态 + 运行证据 | PARTIAL / RUN EVIDENCE OPEN |
| 15 权限 | 仅改 Desktop WPF 交互；领域和设备语义另行审批 | COMPLIANT |

## 四、仍需领域审批的明确差异（不伪造执行）

- `KEY-SEQ-01`：K01–K20 任意多段自由闭合/保持/纯等待的共享按键动作资源。目前单次 Pulse/Hold/Release 不能宣称实现自由序列。
- `CAN-WAIT-01`：仅按 CAN ID（标准/扩展帧）等待新报文，可不匹配数据；当前编辑类型 `CanWait` 未获真实运行器接线。
- `READ-STEP-01`：DBC 信号 Latest/Next + 可选变量 + 可选判定；不能强制每次保存变量或自动产出 FAIL。
- `FIXTURE-STEP-01`：夹具 TEST 动作与真实控制执行授权。前端不能凭一个按钮制造夹具已可控的假象。
- 未批准的更多 Flow 执行、项目级 Run、步骤级 Enabled 等仍维持阻断。

## 五、剩余 HTML 差异与实机验证记录

| ID | 观察路径 | 验证门槛 | 事实 |
|---|---|---|---|
| UI-DIFF-01 | 空白用例，选中“准备”并添加 Wait | 中央立即出现步骤，不再出现右侧操作类型大列表 | CODE_UPDATED；WINDOWS_UNVERIFIED |
| UI-DIFF-02 | 已有 CAN/IGN/Read/Loop 步骤逐个点击 | Inspector 与当前 StepId、类型及参数一致；切换没有旧表单或旧属性闪现 | CODE_UPDATED；WINDOWS_UNVERIFIED |
| UI-DIFF-03 | IF/ELSE 子分支 | THEN/ELSE 行可直接添加，插入的子树坐标准确 | CODE_UPDATED；WINDOWS_UNVERIFIED |
| UI-DIFF-04 | 等待/循环行内编辑 | 未双击前是摘要；双击才是输入框；合法更新与右侧同步，非法不静默丢失 | CODE_UPDATED；WINDOWS_UNVERIFIED |
| UI-DIFF-05 | 测试项目与左工程树选中 | 本次勾选同步、默认启用不改变、固定修订不自动追新 | EXISTING；WINDOWS_UNVERIFIED |
| UI-DIFF-06 | 30 步多分支 + 原 HTML 同分辨率对照 | 三区配色、类别、选择、搜索、表头、列宽、Inspector 分组、焦点 | VISUAL_UNVERIFIED |
| UI-DIFF-07 | 1366×768、1920×1080、2560×1440 及 100/125/150% DPI | 系统任务栏不被覆盖、无重要控件裁切 | WINDOWS_UNVERIFIED |
| UI-DIFF-08 | 多页签草稿/保存/重开/真实运行阻断 | Cancel/Discard/Save 无丢失；无设备时不会生成假 PASS | WINDOWS_UNVERIFIED |

**静态审查证据**：核对已修改的 XAML 标签结构、事件方法完整性、C# 基本括号结构和旧 Inspector 操作类型入口已删除。新增/修订 TEST Desktop 回归测试源代码，包括唯一 CAN 发送入口、Wait/Loop 行内状态、THEN/ELSE Slot 身份、取消 Undo/Redo。测试尚未在 Windows 执行。

**不得宣称 Build、Test、浏览器与 WPF 行为/像素级对照 PASS。** 本环境没有 Windows WPF 运行能力，实际差异需 Windows Codex 机械 Build/Test/运行截图后回到本专业对话修复。

## 六、本地 Codex 一次验证指令

1. 检出分支最终**精确 SHA**；确认工作区无未提交用户改动，否则停止。
2. `dotnet build src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug`，失败只返回完整原始日志。
3. 通过后 `dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Debug --no-restore`。
4. 启动正式 Desktop，离线使用真实空工程/真实资源：新建项目和用例 → 准备 Wait/IGN → 测试 CAN/Read/IF/ELSE/CASE/Loop → 清理 IGN → 行内/Inspector 双向编辑 → Ctrl+Z/Y → Ctrl+S/关闭/重开 → 资源引用 → 本次选择 → 不同 DPI。
5. 同尺寸运行冻结 HTML 和 WPF 对照**同等工程状态**，至少一份 30 步/多分支测试用例；记录 UI-DIFF-ID、HTML/WPF 截图、复现步骤、错误、具体 SHA。
6. 不进行 CAN/KEY/IGN/夹具/UDS/Flash 实体指令，不修改源码、不提交 main、不启 CI、不造模拟运行 PASS。

**Gate 结论：NOT_GATE_ACCEPTED。**
