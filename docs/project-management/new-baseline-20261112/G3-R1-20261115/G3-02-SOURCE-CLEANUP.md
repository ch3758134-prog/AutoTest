# AutoTest｜G3-02 旧上位机 UI 删除与后端继承边界 v0.2

**阶段：G3 详细设计候审；不等于当前已删除源码。**
**用户决策（2026-11-15）：不保留旧 UI 以求兼容；以冻结 HTML 严格重建自动测试 WPF，原有有效后端保留并按缺口补充。**
**精确源码基准：** `local@a9a26ebfba1efcf032ff8ec8c435336a89469647`；`main@c8c4b5c89e29390bb56ef0e699716dfa45ac0bda` 是此前对照基线，两支已分叉；本轮主线仅增加设计和 HTML 归档。
**UI 标准：** [UI-AUTO-G3-20261115-S01](../../../ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html) Blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`。

## 一、范围裁决

1. **允许删除并重写现有自动测试相关 WPF 表现层，不保留兼容旧 UI 的第二套 View/ViewModel/布局切换/菜单。** 新 UI 必须匹配 HTML，而不是由旧 XAML 能力反向限定设计。
2. **不得为了清理 UI 删除真实领域核心**：`TestDocument/StepNode/StepEditTransaction/TestProjectDocument`、`ProjectWorkspaceService`、DBC 编解码、Controller 配置、真实 USB/CAN、ISO-TP、Wave、Record 等仍是正确的复用对象。
3. 当前一些 UI ViewModel 包含业务逻辑，**“提取服务逻辑后删掉整个旧 UI ViewModel”不是“保留旧 UI 兼容”**。必须把被依赖的资源保存、撤销、设备授权与工位身份搬至单一业务服务或新 UI VM 调用，不能直接删文件导致能力丢失。
4. 清理须针对**统一 G4 UI 切片的候选分支**做原子提交：同一候选既移除旧入口又实现新入口；用户本地能打开应用后验证。不要要求先长期维持双 UI，避免 MainWindow 两套导航并存。
5. **不整支合并 `local` 到 `main`**：`local` 相对 2026-11-15 的 main 已分叉（Git compare ahead 107、behind 99，约 88 变化文件）；保留它作为现阶段真实业务后端来源，以精确文件及提交选择性集成。允许在 G4 明确的受控分支进行新 UI 重建，但不得把未审查历史文件批量带入 main。

## 二、UI 文件清理/替换台账（有明确动作，不说“尽量兼容”）

| 文件 | 处理 | 附带必要动作/依赖 |
|---|---|---|
| `src/AutoTest.Desktop/Shell/MainWindow.xaml` | **完全重写 XAML** | HTML 的紧凑顶部、工程树、主内容、右 Inspector、底调试区、状态栏；默认最大化 |
| `src/AutoTest.Desktop/Shell/MainWindow.xaml.cs` | **完全重写窗口事件适配** | 关闭前草稿保护、扫码键盘事件、焦点/快捷键、应用退出取消；不复用旧窗口组织 |
| `src/AutoTest.Desktop/Shell/MainWindowViewModel.cs` | **删除旧 UI ViewModel 实现，换新 Shell/Workspace VM** | 旧文件约 932 行混合编辑页签、USB、扫码、CAN 和导航；将真正可用服务调用接入单一新工作区和共用会话，不保留第二套 VM |
| `src/AutoTest.Desktop/Testing/TestEditorView.xaml`、`.xaml.cs` | **旧视图删除/完全重建** | 从旧 TreeView 编辑方式改为冻结 HTML 的高密度步骤行、列摘要/层级/快速编辑/分组和 Inspector 关联 |
| `src/AutoTest.Desktop/Testing/TestEditorViewModel.cs` | **删除旧 UI VM 并以新编辑会话投影替代** | 其现有 `StepEditTransaction`、草稿/撤销/保存/调用入口有业务价值，先移动到唯一编辑服务，不能静默丢撤销或保存安全 |
| `src/AutoTest.Desktop/Testing/TestProjectWorkbenchView.xaml`、`.xaml.cs`、`TestProjectWorkbenchViewModel.cs` | **旧 UI 全量替换** | 仍使用底层 `TestProjectDocument.Packages` 与 `ItemId/Enabled`，支持按排序组织、工程师临时勾选（由 Enabled 初始化） |
| `src/AutoTest.Desktop/Testing/TestStepInspector.xaml`、`.xaml.cs` | **旧 UI 删除/按 HTML 全新实现** | 右侧常驻属性，随步骤选择同步，不再把关键参数交互退回弹窗；DBC 信号输入要真实校验 |
| `src/AutoTest.Desktop/Testing/StepParametersDialog.xaml`、`.xaml.cs` | **旧高频参数弹窗退出主路径，替代完成后删除** | 新 Inspector 承担所有正式可编辑参数；如某低频操作确需弹窗，必须依据 HTML，不保留旧弹窗以“兼容” |
| `src/AutoTest.Desktop/Testing/TargetMappingDialog.xaml`、`.xaml.cs` | **旧映射弹窗退出主路径，候选删除** | DBC/Controller 资源选择与 Inspector 中相应选择器集成；保留 `TestTargetMappings` 资源身份及 pin |
| `src/AutoTest.Desktop/Testing/TestEditorDialogs.cs` | **旧 UI 聚合逻辑删除/替换为最小消息或状态服务** | 其 `EditParameters/ChooseMapping` 不再作为核心操作入口；合理确认与错误提示可由统一用户反馈服务实现 |
| `src/AutoTest.Desktop/Testing/StepUiCatalog.cs` | **重建表现元数据** | 后端 `StepDescriptorCatalog` 是真实类型依据；禁用/未支持状态明确，不能把当前仅 13 项旧 UI 列表当所有可用步骤 |
| `src/AutoTest.Desktop/Testing/StepTreeItem.cs`、`TestTargetCatalog.cs` | **替换/择优迁移业务映射** | `StepNode` 正式层级和映射不变；为 HTML 建轻量高性能可编辑行投影，不同时维护两套当前步骤身份 |
| `src/AutoTest.Desktop/Themes/**` | **按 HTML 最终 CSS 重新制作 ResourceDictionary** | 不保留旧 UI 的不同字号/空间配置；主题仍浅色、中文、DPI 适配 |
| `src/AutoTest.Desktop/App.xaml`、`.xaml.cs`、`Bootstrap/PrismRegistration.cs` | **保留应用生命周期，重接新的 UI 和服务** | 这是 Prism/DI/异常处理基础设施，不属于要为了换视觉而删除的旧界面 |
| `src/AutoTest.Desktop/Configuration/**`、`Dbc/**`、`Hardware/CanMonitorView.xaml` | **旧工具 UI 为后续独立替换候选** | 本轮自动测试切片不以“旧 UI 不满意”为理由先删专业工具、破坏工程资源入口；工具 UI 验收后按新方案删除，底层服务不复制 |
| `src/AutoTest.Desktop/Projects/NewProjectDialog* / ResourceNameDialog*` | **按冻结 HTML 的高频交互检查后替换** | 新建工程和重命名如果 HTML 对应无弹窗，就不得保持旧交互；绝对不影响工程创建及安全保存语义 |

**删除规则：** `DeleteAndReplaceInSameCandidate`。针对每组提供**旧调用点→新调用点清单**，确认 XAML `x:Class`、Prism 注册、项目引用、测试程序集引用已迁移。构建和用户操作通过之前不合并 `main`，但不因“兼容”恢复旧 UI。

## 三、必须保留的后端核心（可修订，不可整体重写）

| 后端目录 / 文件 | 真实价值及现存限制 |
|---|---|
| `src/AutoTest.Testing/Authoring/**` | `TestDocument`、`StepNode`、`StepEditTransaction`、校验、TargetMapping、项目有序 Entry、资源指纹。保留 DSL，设计新增节点和属性 |
| `src/AutoTest.Testing/Preparation/PlanCompiler.cs` | 编译与不支持步骤预检；目前未覆盖 HTML 全部步骤；应按 G3-TEST-FLOW 扩展 |
| `src/AutoTest.Testing/Runtime/RealRunCoordinator.cs` | 现有真实单包解释器；须支持流程、事实/Occurrence 和项目级父 Run，但不另做第二解释器 |
| `src/AutoTest.Desktop/Projects/ProjectWorkspaceService.cs` | 创建/打开、资源 Revision/SHA、DBC/TEST 保存与项目绑定；后续可抽到 Application，不要因其目前位于 Desktop 而删除 |
| `src/AutoTest.Projects/{Dbc,ControllerConfig,Resources,Project}/**` | DBC 编码解析、工程资源、Controller 配置与 KEY_SIM 底层映射，不内置 EPB 业务按钮 |
| `src/AutoTest.Devices/**` | WinUSB、CAN 帧、真实控制、`PhysicalCommandFence`、设备证据；不能用 HTML 模拟通讯替代 |
| `src/AutoTest.Specialists/{Uds,Flash,Wave,Scan}/**` | 专业算法/服务；缺少/不安全的实体能力明确待完成，不因为用户暂后置专业工具 UI 而删除 |
| `src/AutoTest.Records/**` | 运行事实、审计、存储、报告；必须对接真实 Run 而非 HTML 演示 |
| `src/AutoTest.Application/Production/**` | 存在 `MockObservation`、`SimulationOnly` 历史路径；**必须替换掉模拟生产执行**，不能保留旧路径并称完成实体生产 |
| `src/AutoTest.Devices/Station/StationControlService.cs` | 当前实现明确拒绝物理输出，**不是可用的真实 IGN/夹具服务**；G4 按已批准 DEVICE 合同接入受控真实通路或显式阻断，不能 UI 伪成功 |

## 四、实际后端/前端需要新增的能力

| 缺口 ID | 精确内容 | 责任与验收 |
|---|---|---|
| B-01 | 统一工程/选中资源/多页签/未保存/撤销身份 | 新 Shell + Workspace；打开保存重开，无双状态 |
| B-02 | DBC/Controller 资源绑定后的步骤摘要、Inspector 编辑与校验 | TEST/PROJECT；编辑物理值，错误不可默写 0 |
| B-03 | `IF/SWITCH/WHEN/TRY/子程序/表达式/等待 CAN` 正式编译与解释 | TEST-FLOW；每种均有运行事实和错误，未实现不启动 |
| B-04 | 测试项目级 Run、临时选择、资源冻结、包间 Cleanup、StepOccurrence 和证据 | A2 + DEVICE + RECORD；单设备唯一所有权 |
| B-05 | 工程师真实停止/暂停/断点/单步/跳过等调试生命周期 | TEST + DEVICE；物理动作不可逆边界明确 |
| B-06 | 扫码生产自动启动真实预检与工序、身份、统计 | SCAN + RECORD；移除旧 Mock 路径 |
| B-07 | 通用输入模拟/IGN/夹具/真实 CAN 按准确硬件映射 | DEVICE；不硬编码 EPB，主控 ACK 不等 DUT 实测 |
| B-08 | UDS/Flash/Wave 与自动 TEST 的步骤/证据接线 | 各专项；Flash 真实刷写需要独立许可证据和方案 |
| B-09 | 高密度步骤上百行渲染/响应、DPI/窗口大小、键鼠导航 | UI；对照冻结 HTML 严格回归 |
| B-10 | 真正生产 Run 保存与报告、扫码复测不计新增 | RECORD；失败不覆盖既有 Run |

## 五、源码清理 G4 受控步骤

1. **清理前**固定 `local` HEAD、核对项目引用与历史用户工程格式；**不**自动恢复旧 UI。
2. 在一个短期受控的 UI 候选分支重建 Shell/TEST UI，并将旧 UI 相关文件在该候选中删除或完整覆盖；保留清晰 Diff、无旧 UI 兼容开关。
3. 在新 View/VM 只绑定现有唯一真实服务与资源，不创建临时第二领域文档，先打通**离线真实编辑/保存**，不运行物理刺激。
4. 本地由用户或 Codex 按机械指令进行 Windows Build、启动、打开工程、新建项目、插入步骤、DBC、撤销、保存重开、DPI 操作，反馈错误原文；Codex 不擅自研究或修复。
5. 修复且用户确认后形成受控可审查候选；不将 `local` 整支历史直接倒入 `main`。旧 XAML、旧 VM/弹窗不因未合并而保留在候选中。
6. G4-A2/TEST-FLOW/SCAN/DEVICE/WAVE 逐个接真实行为。仅 HTML 操作相似但未接真实后端的步骤显式禁止物理运行。

## 六、最重要的禁止项

- 禁止 `WebView2` / HTML 直接运行演示 JS 作为正式生产 WPF 的捷径；
- 禁止为“兼容旧界面”维持双 Shell、双 Inspector、双 TEST 编辑器、旧 Modal 参数编辑主路径；
- 禁止从工程中删除任意有效版本或“自动覆盖为新格式”而无迁移证据；
- 禁止为了减少错误和构建时间屏蔽必需 TEST 类型、删除真实设备限制或假造数据；
- 禁止删除被记录或其他专业工具引用的公共能力；
- 禁止 Mock/Replay 作为真实 ECU 功能验收依据；纯算法单元测试不得代替真实硬件证据。

**本候选建议：** 可以执行旧 UI 表现层的大幅删除/重建，但必须是**经过 G3 准出并明确授权的 G4 有边界软件交付**。这是严格遵照冻结 HTML 的正确路径，不是要保留旧 UI 兼容性。
