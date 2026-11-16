# AutoTest · 阶段 1：视觉原型与交互

**状态：UI_ONLY / STATIC_SAMPLE / NO_BACKEND_WIRING / WINDOWS_BUILD_UNVERIFIED / VISUAL_PARITY_OPEN**

这是一个**新的独立 WinExe**，不是对 `AutoTest.Desktop` 做兼容叠加，不是 WebView2，也没有引用原有 Shell、Project、TEST、DBC、Devices、Flash 或运行服务。其所有步骤、项目、报文名称均为**仅用于 UI 审查的冻结 HTML 样例数据，保存在进程内存中**。

## 运行

在 Windows 的 AutoTest 仓库根目录：

```powershell
dotnet build src/AutoTest.UiPreview/AutoTest.UiPreview.csproj -c Debug
dotnet run --project src/AutoTest.UiPreview/AutoTest.UiPreview.csproj -c Debug --no-build
```

也可打开 `src/AutoTest.UiPreview/bin/Debug/net10.0-windows/AutoTest.UiPreview.exe`。

**不要运行旧的 `AutoTest.Desktop.exe` 来检查本轮无接线 UI**；旧程序保留供后续正式接线，不代表 UI 预览成果。

## 从冻结 HTML 复现的 UI 范畴

- 顶部菜单、工具栏、动态工作区域；左工程树，中央测试用例，右常驻步骤属性，底部观察，蓝色状态条。
- 三阶段准备/测试/清理不同的色带，35 DIP 步骤行、45 DIP 表头；显示原始 HTML 的 11 步示例，包括 IF 的 THEN/ELSE 嵌套。
- STEP 图标、资源、参数摘要、选中、搜索高亮、运行勾选及右侧属性的内容联动。
- 添加步骤：阶段/分支“＋添加”或顶栏“＋添加步骤” → 分组搜索选择类型 → **单击类型即出现行并自动选中，右侧直接填写属性**。没有右侧创建向导。
- 纯内存的名称与参数编辑、启用、断点、复制、移动、删除、撤销/重做、阶段折叠；Ctrl+F 搜索、Enter/Shift+Enter 定位；F2 步骤名称；Ctrl+I 属性面板；Esc 关闭步骤类型弹层。
- Win32 `WM_GETMINMAXINFO` 使用当前显示器工作区避免任务栏被盖；小有效宽高折叠属性和底部观察，底部状态栏可恢复。

## 明确不存在的能力

**没有任何**自动测试执行/预检/报告、CAN 收发、USB 连接、Ignition/KEY/夹具控制、UDS、Flash、DBC 导入或解析、正式资源保存、STEP DSL 或运行日志绑定。所有专业工具按钮只提示 UI 预览尚未接线，运行按钮禁用。

“待执行”和样例报文名称仅复刻冻结 HTML 的**展示数据**，不是 DUT 已连接、报文已接收、预检通过或测试 PASS。Ctrl+S 不保存，关闭进程即清空内存改动。

## 验收状态

当前由 GitHub 代码静态检查；**Windows 编译和运行、浏览器 HTML 与 WPF 的同屏截图对照、100/125/150% DPI、键鼠连续操作均未在当前执行环境测试**。这些必须由本地 Codex 机械启动与截图后才能关闭。源码未触碰冻结 HTML 和原有正式业务模块。

**目标**：先冻结用户看到的 UI 和操作链，再独立审批正式业务适配；不能由旧 ViewModel 反向决定 UI。

## 六阶段交付约束

1. **阶段 1（当前）**：视觉原型与交互；清理 UI 自我说明和冗余术语，用户 Windows 视觉/操作验收。全部测试内容为内存示例。
2. **阶段 2（待批准进入）**：统一控件、XAML ResourceDictionary 与样式 Token，不新增任何设备能力。
3. **阶段 3**：正式 UI ViewModel、Binding、Command、页面状态、编辑草稿/校验，仍不接后端。
4. **阶段 4**：复用真实 ProjectWorkspaceService、TestDocument、StepEditTransaction、DBC/Controller 的离线编辑与存储。
5. **阶段 5**：应用服务接线；USB/WinUSB 先行，随后经批准接 CAN/KEY/IGN/夹具/UDS/波形。不直通硬件协议。
6. **阶段 6**：真实 Windows/DUT 端到端操作、视觉差异、设备证据与版本审查。

每一阶段必须提供精确 Git SHA、可复现操作、结果和未关闭差异，由用户确认后进入下一阶段。**阶段 1 严禁引用 AutoTest.Devices，之前提前写入的 USB Controller 已撤销。**
