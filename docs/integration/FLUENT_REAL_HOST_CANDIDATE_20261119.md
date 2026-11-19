# AutoTest｜Fluent 新 UI × 正式 HOST 真能力集成候选（2026-11-19）

**状态：GITHUB_SOURCE_COMMITTED / REAL_SERVICE_WIRING_PARTIAL / WINDOWS_BUILD_NOT_RUN / USER_HARDWARE_ACCEPTANCE_NOT_RUN**

## 1. 当前基线与交付路径

- 全系统稳定基线：`main@ffb23f8c05ec4a64b82cd781db6357685d082c04`。当前仓库三工程平级：`HOST/`、`Main_Control/`、`Key_Sim/`。
- 已获 UI 操作体验认可的纯前端参考：`feature/fluent-bench-test-ui-20261119@7ec1f0fc53e8ca71dfad42c3cf72ac87d62ee3f5`。
- 新 HOST 集成候选：`feature/host-fluent-integration-20261119`，从正式 `main` 建立，不合并旧的、已与目录整理冲突的 UI PR #63。
- 当前唯一**正式业务上位机**入口仍是 `HOST/src/AutoTest.Desktop/AutoTest.Desktop.csproj`。此分支没有把 `AutoTest.Frontend` 虚拟数据当作正式工程，也没有建立第二个独立 USB/CAN/TEST 运行器。
- 主控板和按键板 firmware 路径及内容保持 `main` 不变；本集成工作只改 HOST WPF 呈现及其交互连接。

## 2. 已实际接入的 UI → 正式后端

| 新 UI 区域 | 实际绑定的正式服务/对象 | 说明 |
|---|---|---|
| 主窗口连接与状态 | `MainWindowViewModel.ConnectHardwareCommand`、`HardwareStatus` | 使用原 `HardwareConnectionService`；不创建设备模拟 |
| 工程文件树 | `ProjectExplorerView` 与 `ProjectWorkspaceService` | 保留真正打开工程、测试项目、用例与资源的能力 |
| 自动恢复上次工程 | `MainWindow.RestoreLastProjectAsync` 的原有启动逻辑 | 页面初始显示“运行”，工程/最近编辑资源继续从正式持久化读 |
| 运行页左侧项目选测 | `TestProjectWorkbenchViewModel.Packages/RunSelected` | 直接使用原项目临时选择；双击进入正式用例编辑 |
| 运行页用例运行 | `TestEditorViewModel.PrepareRunApprovalCommand`、`RunCommand`、`StopRunCommand` | **只绑定当前正式用例**；硬件门禁仍由 TEST/DEVICE 决定 |
| 运行页步骤执行记录 | `TestEditorViewModel.RunFactRows` | 仅呈现真正发布的执行事实，不制造 PASS |
| 运行页 CAN 事件 | `MainWindowViewModel.CanRecords` | 来自主控接收的真实帧，不能替代 ECU 物理验证 |
| 编辑页 | 原 `ActiveEditorView`、`DocumentTabs`、`ActiveEditorInspector` | 保留既有 TEST 结构编辑/校验/资源编辑服务；不接第二 DSL |
| 编辑期间真实执行 | 订阅 `TestEditorViewModel.IsBusy` | 自动切换到运行页；编辑页锁定，运行结束由 TEST 恢复 |
| 工具入口 | 现有 `OpenManualBench`、`OpenDbcTool`、`OpenWaveWorkbench`、`OpenFlashWorkbench` 等 | 不改正式子系统；副窗口由主窗口拥有 |
| 自动保存与关闭 | 原 `AutoSaveOpenEditorsAsync`、`PrepareEditorsForCloseAsync`、`ShutdownAsync` | 保留已有工程保存验证和硬件清理，**不引用 UI 演示的 .ui-draft.json** |

新增 WPF UI 4.3.0 / Fluent Light 主题、统一 AT 色彩与基础视觉 token。旧的 Win32 无边框标题栏替换为原生 Windows 边框；正式 UI 生命周期及工具 Owner 关系保留。

## 3. 必须保留的诚实限制

- **测试项目多用例的整组真实运行尚未接入**。`TestProjectWorkbenchViewModel` 具有 `RunSelected` 和 `PreflightSelectedCommand`，不具备可靠的正式整组 Run 命令。运行页的“运行当前用例”只调用正式当前用例 Run；不能冒充“运行勾选的全部用例”。
- **独立实时 UDS 工作台尚未在主 HOST 接通**。虽然 `AutoTest.Specialists.Uds` 存在客户端及会话底层实现，不能把静态 UI 服务列表冒充 ECU 请求成功。
- **生产扫码自动匹配并启动正式 Run 尚待 SCAN/TEST 集成**。当前扫码入口只继承已有捕获与匹配，不能将其当作完整生产模式。
- **真实执行报告历史查询、完整可追溯报告窗口尚未与 RECORD 端到端接通**；RunFactRows 不是正式报告。
- **Fluent 已重建主窗口，但各专业工具内部控件目前保留旧的真实业务 View**。手动台架、波形工具优化了独立窗口大小和浅色背景，其余内部表单需在正式功能不回退的前提下继续视觉统一。
- FlashLab 是独占 USB 的专业工具：打开它前按正式服务释放当前连接；不能因为窗口更美观而绕过 USB 排他/实体风险校验。
- 当前尚没有 Windows 编译、真实 DUT 运行证据，因此不能标记 FULL_SYSTEM_PASS，也不能把该分支直接合入 main。

## 4. Windows 机械验证命令

在本地 AutoTest 仓库根目录：

```powershell
git fetch origin feature/host-fluent-integration-20261119
git switch --detach origin/feature/host-fluent-integration-20261119
cd HOST

dotnet restore src/AutoTest.Desktop/AutoTest.Desktop.csproj
dotnet build src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug --no-restore
dotnet run --project src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug --no-build
```

出现编译/XAML 解析错误时，提交具体文件、行号和完整堆栈，先在候选分支修复，不动 main。

## 5. 候选功能验收清单（先静态与 WPF，再在真实台架上执行）

1. Windows 启动真实 `AutoTest.Desktop`，自然显示“运行”页；自动恢复上次工程。
2. 工程树选择、实际测试项目打开、用例勾选，切换“编辑”页修改真实用例，再保存并重新打开确认。
3. 在运行页选择**当前实际用例**，按原 TEST 预检、授权和执行门禁操作，观察真实 RunFactRows、CAN、停止与错误行为；不要误将项目勾选当作整组运行。
4. 工具菜单独立打开真实手动台架，核对保存 CAN 行、输入动作和设备操作权限；运行过程中不得抢占测试硬件。
5. DBC 打开完整专业编辑窗口；波形、烧录仅在具备真实许可与台架准备时实测，烧录 USB 移交需确认。
6. 关闭主窗口时，先处理未保存草稿并完成硬件受控停机；工具窗口一起退出。
7. 1920×1080 / 1366×768、100/125/150% DPI 检查 Fluent 主窗口、运行/编辑切换、面板宽度、底部状态、工具最大最小尺寸和任务栏。
8. 对 UDS、生产扫码自动执行、历史报告、项目整组运行逐项记录为明确尚未接入，而不是报告 PASS。

## 6. 静态源代码检查

- 已在 GitHub 回读主 Shell XAML 结构及 MainWindow CodeBehind 的事件处理函数、真实 VM 引用、旧主业务 View 保留情况。
- 源码静态核查不证明 Win11 WPF 编译、资源加载、运行稳定性或者物理操作通过。
- 仅在 Windows 实机编译运行完成并由用户审查后，才可以把此分支作为整体集成候选继续深化。

**当前结论：REAL_HOST_UI_INTEGRATION_CANDIDATE, NOT_FULLY_COMPLETE。**
