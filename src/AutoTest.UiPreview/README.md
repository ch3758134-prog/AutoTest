# AutoTest · G4 原生 WPF 独立 UI 预览

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

- IDE 顶部菜单、工具栏、动态工作区域；左工程树，中央测试用例，右常驻步骤属性，底部观察，蓝色状态条。
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
