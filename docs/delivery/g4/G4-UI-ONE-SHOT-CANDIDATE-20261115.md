# AutoTest｜G4-UI-ONE-SHOT WPF 源码候选与实测交接（2026-11-15）

**性质：** 已实际提交原生 WPF 改动的首版 **Windows 待编译／待视觉验收候选**；不等同于 G4 UI-03 全部通过、也不等同于 R1 自动测试或实体 ECU 验收。

- **唯一执行分支：** `feature/g4-html-parity-20261115`
- **交接起点：** `333325fff1d4159a0a213ddf4e362a213fa12b2a`，未另从 `main` 起步
- **源码收尾提交：** `3f4cc09fb1d8d4fff0b1136e1ec57642bfb517fb`（本报告会另追加一个说明提交，分支最后 HEAD 为准）
- **冻结 HTML Git Blob：** `c1a55e295c0f5321736fa3a8a7db20e13415a727`；未修改冻结设计
- **工作包及限制：** G3 批准 `PASS_WITH_LIMITATIONS`；G4-UI-00～03 非实体编辑授权
- **CI/Build：** 未触发 GitHub Actions；当前会话无 Windows/.NET SDK，未编译、未运行、未连接主控和 DUT，不能写 Build PASS
- **Git 操作：** 仅推进该特性分支；未合并、未改动 `main`；不创建会触发 backend-windows PR CI 的 PR

## 1. 实际 WPF 文件变更

### 整体重写与重接线
- `src/AutoTest.Desktop/Shell/MainWindow.xaml`：38/39/23 工具栏与状态行、270 左树、320 常驻 Inspector、180 底观察，默认最大化、可收起可拖动、页签
- `Shell/MainWindow.xaml.cs`：保留安全关闭/扫码/工程资源选择，新增区块折叠入口
- `Shell/MainWindowViewModel.cs`：复用唯一真实 ProjectWorkspaceService、真实 USB/CAN/扫码；编辑器右侧 Inspector 单实例承载、自动测试分组、打开项目下的资源引用
- `Testing/TestEditorView.xaml`、`.xaml.cs`、`TestEditorViewModel.cs`：完全替换旧 TreeView 表现，真实 StepEditTransaction 控制虚拟化行投影、右键、快捷键、拖动、阶段/流程收起、双击属性、Wait/Loop 行内编辑、复制/粘贴、撤销/保存
- `Testing/TestProjectWorkbenchView.xaml`、`.xaml.cs`、`TestProjectWorkbenchViewModel.cs`：有序真实资产、修订/SHA、实时 TEST 步骤数、工程 Enabled 与临时 RunSelected 分开、保存重开及用例导航
- `Testing/TestStepInspector.xaml`、`.xaml.cs`、`StepUiCatalog.cs`：单一常驻属性路径、真实 DBC/Controller/Raw CAN 选择，增加未接线流程/UDS/波形/Flash 的编辑入口与明确状态，不宣称可执行
- `Themes/Tokens/Colors.xaml`、`Metrics.xaml`、`Typography.xaml` 和 `Themes/Controls/Buttons.xaml`：HTML 主色及 12px 密度基准

### 新增原生工作区
- `Shell/ProjectOverviewView.xaml`、`.xaml.cs`：真实工程总览页签
- `Shell/ProjectOverviewInspector.xaml`、`.xaml.cs`：真实工程状态属性页
- `Testing/StepRowProjection.cs`：唯一正式 StepNode 树的显示投影，不持久化第二模型
- `Testing/TestProjectSelectionInspector.xaml`、`.xaml.cs`：项目选择与工程默认分离的检查/属性面板

### 明确删除旧 UI
- `Testing/StepParametersDialog.xaml`
- `Testing/StepParametersDialog.xaml.cs`
- `Testing/TargetMappingDialog.xaml`
- `Testing/TargetMappingDialog.xaml.cs`

`TestEditorDialogs` 已缩减为必要确认和错误消息，不再调用上述高频参数弹窗。原 `StepTreeItem` 保留仅用于真实树坐标和编辑事务，不再作为旧界面 TreeView。保留不变的正式模块包括 `AutoTest.Projects`、`AutoTest.Devices`、`AutoTest.Records`、`AutoTest.Specialists`、`StepEditTransaction`、`TestDocument`、`TestProjectDocument`、`TestTargetCatalog`、`RealTestWorkbenchService`。对 `StepTreeValidator` 的唯一小修正是子程序的 `target` 标识不再错误地当做 CAN/信号映射。

## 2. 对照 G3-01 U-01～U-16 的真实状态

| 项 | 状态 | 证据和仍有限制 |
|---|---|---|
| U-01 工程 | **IMPLEMENTED / WINDOWS_UNVERIFIED** | 原 ProjectWorkspaceService 创建/打开/恢复最近一个工程，冲突和草稿防护沿用正式后端；另存为未另接 |
| U-02 工程树 | **PARTIAL** | 自动测试→项目/用例分组；打开项目后以真实 Item/Asset 显示项目引用并可导航。未验证大工程性能、所有状态同步 |
| U-03 测试项目 | **IMPLEMENTED / WINDOWS_UNVERIFIED** | 排序/增删、默认 Enabled、修订 SHA/真实步骤数、真实保存；引用失效独立显示 |
| U-04 临时选择 | **IMPLEMENTED / WINDOWS_UNVERIFIED** | RunSelected 非持久；全选/清空/检查所选，独立于默认 Enabled；无实际项目执行 |
| U-05 TEST 阶段 | **IMPLEMENTED / WINDOWS_UNVERIFIED** | Prepare/Test/Cleanup 由现有正式 TEST 构造，行可折叠、可追加子步骤 |
| U-06 步骤行 | **PARTIAL** | 35px 行、5 列、资源/静态状态、StepId 实际引用；真实运行 Occurrence 状态尚无项目事件来源 |
| U-07 流程 | **AUTHORING_ONLY / BLOCKED_BY_BACKEND** | IF/ELSE/Loop 等真实 Slots；Switch/Case/When/TryCatch/Subroutine/Call 作为正式 StepNode 编辑，执行前必须由正式编译器阻断未接线类型 |
| U-08 新增步骤 | **IMPLEMENTED / WINDOWS_UNVERIFIED** | 选择阶段/步骤/Slot 后新步骤通过 Inspector 配置；StepEditTransaction 单一事务与真实 Mapping |
| U-09 Inspector/DBC | **IMPLEMENTED / WINDOWS_UNVERIFIED** | 单右轨正式 DBC 信号物理值编辑、报文编码/真实绑定；具体复用帧和设备需用户导入真实 DBC 检查 |
| U-10 行内参数 | **PARTIAL** | Wait 和 Loop 直接可编辑；IGN/按键/CAN 仍走右轨 Inspector，未冒充数值快速编辑 |
| U-11 键鼠 | **PARTIAL** | F2、F9、Ctrl+Space/F/Z/Y/C/V、Delete、拖动、上下移动、右键；需在 Windows 验证焦点/文本输入细节 |
| U-12 页签 | **IMPLEMENTED / WINDOWS_UNVERIFIED** | 工程概览/项目/用例/配置按真实资源独立寿命管理，同一资源复用 Tab，关闭前草稿检查 |
| U-13 真实观察 | **PARTIAL** | 仅绑定实际 CAN FrameObserved、DBC 解码和既有单 TEST RunFacts；项目级事件、变量、完整历史尚未接线 |
| U-14 检查与 Run | **BLOCKED_BY_BACKEND** | 现有 PlanCompiler 静态诊断和正式资产 ID/Revision/SHA 检查；未接项目 Run/StationLease，不返回 PASS |
| U-15 生产扫码 | **BLOCKED_BY_BACKEND** | 键盘扫码和正式工程匹配路径沿用；扫码不会借旧 MockObservation 自动执行/生成生产 PASS |
| U-16 工具入口 | **PARTIAL** | DBC 实际导入/打开、CAN 真实监视、USB 连接；UDS/WAVE/Flash/手动台架未接线入口明确禁用（各专业独立验收） |

**重要事实：** U-01～U-14 并未全部验收；当前仅形成可供本地 Build/视觉/离线编辑实测的候选。G3-01 所要求的 1366/1920/2560 分辨率、100/125/150% DPI 同图对照与 UI-DIFF 闭环尚未完成，不能声明严格等同冻结 HTML。

## 3. Windows 本地实际启动

请在已有 AutoTest 仓库工作树中（应先处理自己的未提交改动，不使用 `reset --hard`）：

```powershell
git fetch origin feature/g4-html-parity-20261115
git switch feature/g4-html-parity-20261115
git pull --ff-only origin feature/g4-html-parity-20261115
git rev-parse HEAD
dotnet --version
dotnet restore .\AutoTest.sln
dotnet build .\AutoTest.sln -c Debug --no-restore --nologo
dotnet run --project .\src\AutoTest.Desktop\AutoTest.Desktop.csproj -c Debug --no-build
```

要求 Windows + .NET SDK 10.0.400（见 `global.json`）。若本地已有同名分支且未整合，`git switch` 或 `pull --ff-only` 应拒绝而不是强行覆盖。

## 4. 建议集中手动实测顺序（全部在不执行 ECU 指令前）

1. 启动最大化，检查左树/页签/右属性/底观察是否与冻结 HTML 一致；记录同屏截图、DPI 和分辨率
2. 新建或打开真实工程，在工程概览点击新建两个测试用例及一个测试项目
3. 项目页添加两个用例，查看真实步骤数和修订；上下调整、临时勾选/清空、修改默认 Enabled；保存后关闭重开核对默认与当次选择
4. 双击项目中的测试用例，选准备/测试/清理，增加 Wait/Loop/IF/ELSE，折叠、复制/粘贴、拖动、F2/F9、Undo/Redo
5. 在 Wait/Loop 摘要格直接修改数值；在右侧 Inspector 修改其他步骤参数，检查未应用草稿时是否正确阻止离开
6. 导入真实 DBC，在 CAN 发送操作中选择报文并修改物理信号值；观察提示、无 DBC 与无效资源的阻断
7. 保存 TEST，关闭页签再打开，检查步骤 ID/资源绑定/值、流程子分支是否保持
8. 点击“检查所选”，期待真实的静态检查结果或明确 `BLOCKED_BY_BACKEND`，**不能出现模拟 PASS**
9. 如需连主控、观察 CAN1/DUT 或 CAN0/按键板，由现场操作者决定；本次 AI 未对实体发指令
10. 反馈完整 Windows Build 错误原文、启动异常、HTML/WPF 并排截图及 UI-DIFF 编号；后续只按此候选做修正

## 5. 明确未完成及安全边界

- **本环境无法 Windows 编译/运行/WPF 截图，也未获实体 ECU 操作授权**；本轮只完成 GitHub 源码提交及 XAML 标签/事件/主要绑定静态检查
- **真实项目级 Run**、包间 Cleanup、Station Lease、真实 ProjectRunEvent、单步/运行到/跳过执行器、生产扫描自动 Run、报告闭环：由后续 G4 后端工作包落地，当前显式阻断
- **高级流程解释器**（Switch/When/TryCatch/Call）、UDS 请求、实体 Flash、波形采集/比较：编辑入口可见，尚未获得可执行证明；禁止把占位参数误解为可运行功能
- **严格像素级 HTML parity** 需用户实际 Windows 截图批准；WPF 并未承诺所有细节无差异
- **没有调用真实硬件/写 ECU、没有 Flash、没有 Mock/Simulation/Replay、没有修改固件**
- **没有触发 CI**；由于仓库 `.github/workflows/backend-windows.yml` 的 PR 监听包含 `src/AutoTest.Testing/**`，本轮没有创建可能自动触发耗时 CI 的 PR，也没有合并 `main`
- 候选固定后停止追加本轮 UI 提交，等待用户一次性本地实测反馈。
