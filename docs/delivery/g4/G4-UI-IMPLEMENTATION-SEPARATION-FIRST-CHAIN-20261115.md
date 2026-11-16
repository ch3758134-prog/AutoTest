# AutoTest | G4 WPF 实现分层重整 | 第一条真实编辑操作链

## 准出状态

- **CODE_CANDIDATE / STATIC_INTEGRITY_CHECKS_PASS / WINDOWS_BUILD_UNVERIFIED / WPF_INTERACTION_UNVERIFIED / HTML_VISUAL_DIFF_OPEN**
- 项目管理允许的现有分支：`feature/g4-html-parity-20261115`
- 本轮起点：`4513fc6da889eee9d7b7a144148297d834582d3f`
- 本文编写前已核查代码 HEAD：`51f6232d167c1979c3abba44e71bc92c9c32a85b`；最终以本分支最新 HEAD 为交付基准
- 不写 main；不变更冻结 HTML、TEST 模型、数据库/资源保存格式、Devices/Flash、KEY_SIM/固件。

## 为什么重整

既有 `MainWindowViewModel` 同时持有 WPF 页签与编辑会话生命周期，`TestEditorViewModel` 同时用 `SelectedRow`、`_selectedItem`、异步 Inspector 等保存多份可变选中状态。STEP 编辑调用 `RefreshTree()` 重建全部投影，TabControl、ListBox 可在状态更新期间发出新的选中事件。Inspector 在 WPF Loaded 重入时又会重置，导致自动测试编辑链不可靠。

本轮遵守 **已批准用户操作合同优先，现有真实后端服务作为受限实现接口**，没有由源码重新发明 UI 业务语义。

## 已实现的分层界限

| 新职责 | 实施源码 | 事实 |
|---|---|---|
| 文档工作区状态 | `src/AutoTest.Desktop/Shell/DocumentWorkspaceState.cs` | 单独拥有 Tabs / Active，TryOpen/TryActivate 拒绝时不创建隐形文档；Close/Discard 通过 RemoveApproved 不二次触发草稿验证 |
| Shell 表现层 | `MainWindowViewModel.cs`、`MainWindow.xaml(.cs)` | Shell 以文档工作区对象操作生命周期；文档选择被拒绝后 WPF TabControl 同步回退 |
| TEST 编辑交互会话 | `src/AutoTest.Desktop/Testing/TestEditorInteractionState.cs` | 用稳定 StepId / Phase / Slot 标识选中项，拥有折叠状态、Inspector 异步请求代次；不增加持久 TEST 结构 |
| STEP 编辑器 | `TestEditorViewModel.cs` | 切换阶段/步骤只有单一选中入口，拒绝非法草稿时不改变选择；树重建/搜索/折叠不触发虚假的选中切换；主“添加步骤”按用户选中位置追加或下方插入 |
| Inspector 生命周期 | `TestEditorView.xaml.cs`、`TestStepInspector.xaml.cs` | 已通过真实 StepEditTransaction 的应用成功信号，才重新加载已选中的 Inspector；页签重新 Loaded 不再清空草稿；WPF STEP 行选择被拒绝时回退 |
| 测试 | `DocumentWorkspaceStateTests.cs`、`TestEditorInteractionStateTests.cs` | 增加文档切换拒绝、不创建隐形页签、确认关闭、稳定选中键、折叠恢复、异步请求防旧覆盖等源代码回归用例 |

现有 `ProjectWorkspaceService` / `StepEditTransaction` / TEST 正式保存路径仍是唯一业务入口，禁止把 UI 草稿提交误认作资源落盘。

## 第一条可验收的纵向操作链

1. 运行正式 Windows Desktop，创建或打开测试工程；
2. 创建首个 TEST 项目和空白用例（沿用上一候选已接入的正式 `CreateStarterProjectAsync` / TEST 资产服务）；
3. 打开 TEST 三阶段步骤表；选中阶段后点击“添加步骤”，或点击现有步骤后在其下方插入；
4. 连续编辑两个以上步骤及其 Inspector；修改未应用字段就切换行，必须先验证应用到正式编辑事务；失败要留在原步骤并显示错误；
5. 搜索、折叠/展开、切换文档页签及回来，不能丢选中步骤、Inspector 输入、错误或尚未落盘的 TEST 事务；
6. Ctrl+S、关闭并重新打开 TEST：经既有正式 ProjectWorkspaceService 验证文件身份、Revision/SHA，步骤真实回读；
7. 有未保存草稿时关闭/切换工程，必须有 Save / Discard / Cancel；取消保留，保存失败阻止退出。

## 不能夸大的部分

- 本次执行环境没有 Windows/.NET SDK，不具备 WPF 编译、运行及 DPI/视觉截图能力；**没有宣称 Build PASS 或 UI 验收 PASS**。
- 当前仍不是完整六阶段架构迁移：Config/DBC/项目页等尚未全部迁移至统一文档状态接口，现有 `MainWindowViewModel` 仍包含业务入口协调；后续应在这条 TEST 操作链 Windows 核验后逐域抽离。
- 没有重画冻结 HTML，也没有声称整体视觉已还原。UI 视觉差异与 Shell 像素级核验维持 OPEN。
- KEY 任意多段、CanWait/Read 扩展、复杂 Flow 运行器、项目级 Run、实体台架操作等属于另行审批的 TEST/DEVICE/Controller 合同，不在此次前端分层内。

## Windows 机械验证指令

本地 Codex **只**检出精确候选 HEAD，运行 Desktop Debug Build → `AutoTest.Desktop.Tests` → 启动正式 WPF EXE → 以上七步操作 → 采集截图、日志和错误。不自主修复任何代码，不运行真实 CAN/IGN/KEY/Flash，不触碰 main，不启动 CI。出现编译/启动错误后返回完整原始信息，由本实施对话在同分支精准修复。

## 变更范围审计

仅 `src/AutoTest.Desktop/Shell/**`、`src/AutoTest.Desktop/Testing/**`、`tests/AutoTest.Desktop.Tests/**` 和本文档；没有引入 Mock 测试，也没有创建第二个可执行 TEST 模型。
