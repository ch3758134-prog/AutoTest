# AutoTest R1｜统一 TEST 工程 IDE — 全新 Shell 设计与替换边界

状态：**WPF 源码候选，必须以 Windows 构建/启动及用户窗口截图验收为准。**

## 产品判定

不是两套“自动测试/工程开发”UI，也不再将 13 个专业功能固化成 13 个同级页面。

AutoTest 是一套**以 TEST 程序为主的工程 IDE**。启动后直接显示当前 TEST 的步骤、预期与实际测量、用例选择、Verdict 和证据；左侧工程树始终可用，选项卡“测试结构 / 工程文件”解决运行与编辑的不同关注点。扫码仅是外部工位 COM 输入，放在上部小状态区。DBC、Wave、UDS、Flash、CAN、台架、编号规则等是可按需打开、关闭、浮动的编辑器或工具，不是固定主页面。

### 删除旧 UI，而不是兼容包壳

本分支完整**替换** `src/AutoTest.Desktop/Shell/MainWindow.xaml` 及 `MainWindow.xaml.cs`，删除：
- 固定 `WorkbenchTabs`/P01–P13 导航系统；
- `R1OperatorSurface` + `R1WorkspaceGrid` 双中央容器；
- `R1WorkspaceMode` 自动测试/工程开发/专业工具模式开关；
- `ApplyWorkspaceMode`、`PopulateTab(int)`、`RefreshDocumentTabVisibility` 的整数索引逻辑；
- 固定全局 Inspector、永久占位的大日志面板以及两层重复工具栏；
- 工程中心默认首页及从上一个旧项目继承的 Mock 操作入口。

新 `DocumentTabs` 是**唯一**文档宿主，动态创建打开的 TabItem；常驻且不可关闭的第一个文档是“自动测试”。打开其他工具不会改变正在运行的 TEST 及其设备 Session。工具浮动时把**同一个 View 实例**移至独立 WPF Window，关闭后回停靠，不复制 ViewModel 或 USB 连接。

## 可直接验证的交互

1. 进入软件，左侧工程树可见，中间直接为自动测试，**无 P01 工程中心和 P01～P13 功能标签**。
2. 左侧“测试结构”：TEST 的 TestCase 复选与步骤清单。左侧“工程文件”：当前工程资源树、TEST/DBC 等文件，从工程资源双击打开真实编辑器。
3. TEST 编辑/DBC 编辑/Wave Studio 作为按需打开的文档；窗口菜单支持三者独立浮动到其他显示器并回停靠。
4. UDS / Flash / CAN / Station / Controller / Bench / Record / Audit 通过专业工具菜单打开，并复用原专业 View、业务 Service 与 R1 DeviceLab 唯一 WinUSB/CAN Hub。
5. 扫码枪 COM 状态在顶部小区域；真实编号绑定匹配触发完整 TEST，保留当前已批准的物理权限门禁、RunId、原始证据及 SQLite 入库路径。
6. 运行日志默认仅有一行标题，需查看时展开；TEST 的当前步骤和具体判定主要在中心执行器。
7. 测试用例选中范围只影响工程局部运行；扫码正式路径始终运行完整已发布 TEST。Prepare/Cleanup 保留。

## 底层必须保留的**不是旧 UI**

- 原 `V1Workspace` 工程对象及资源修订、`G4IntegrationHost` 集成服务；
- `LabTestWorkspaceViewModel` 当前 TEST 模型、编译/运行、选择用例、StepFact；
- `DeviceLabEmbeddedView` 真实主控唯一连接；
- DBC/Controller/Wave/UDS/Flash/记录的现役业务服务与工具 View（视觉按后续评审继续细化）；
- COM 编号匹配、重复 DUT 复测政策、实际 Run 的记录证据；
- 真实刷写未退出时不得强行释放主控。

这些是业务数据和设备行为的连续性，不是产品 UI 兼容要求。**新 Shell 不需要兼容旧页面索引、布局存储、命令外观或独立 Mock 窗口。**

## 当前未交付与门禁

- CI Build/Test/Publish + WPF 启动烟测必须在精确 HEAD 上 PASS；任何失败由主对话直接改 GitHub，Codex 只负责本地拉取、构建、启动、日志和截图。
- 浮动只是 WPF 原生独立窗口；真正多文档分屏/拖拽停靠与布局保存尚未建立成熟 Docking Manager，不得称已完整 IDE。
- 工程树目前以项目资源分类和打开实际文件夹为主，仍未形成完整文件复制/移动/重命名/依赖事务系统。
- CI 无法验证真实 ECU、独立窗口视觉比例、设备 ACK/NRC 等，仍须用户本机实测。
- 完整目标 TEST 步骤集（UDS/Flash/CAN 周期/Wave/IGN 等）不能因为有单独专业工具就被视为已经全部具备真实自动执行能力。

后续 UI 修改应始终从**唯一文档管理体系**进行；禁止把旧 P01-P13 导航或第二套生产 Shell 加回来。
