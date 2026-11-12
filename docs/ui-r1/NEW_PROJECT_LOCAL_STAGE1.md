# AutoTest｜第一阶段：浅色 Shell + 新建工程

## 分支与交付边界

- GitHub `main`：固定的、尚未验收这批 UI 的基线。
- GitHub `local`：唯一新的前端候选；本地 Codex 也在 `local` 工作。不创建其他研发分支。
- 本批提交标记 `[skip ci]`，没有启动 GitHub CI。
- 用户本地编译修错并实测后才提交 `local`；未经用户批准，不把这批 UI 接入 main。

## 第一批修改与新增文件

| 文件 | 职责 |
| --- | --- |
| `src/AutoTest.Desktop/App.xaml` | 按确定顺序合并全局主题资源与可见性转换器 |
| `src/AutoTest.Desktop/Themes/Tokens/{Colors,Typography,Metrics}.xaml` | 统一浅色配色、微软雅黑、DIP 字号与布局尺寸 |
| `src/AutoTest.Desktop/Themes/Controls/{Buttons,Inputs,Navigation,Panels}.xaml` | 普通/主按钮、输入框、菜单/树、面板与状态色 |
| `src/AutoTest.Desktop/Shell/MainWindow.xaml` | 文件菜单、工具栏、可调左资源栏、欢迎页/工程概览、底部状态 |
| `src/AutoTest.Desktop/Shell/MainWindow.xaml.cs` | 构造注入 ViewModel，不读写磁盘 |
| `src/AutoTest.Desktop/Shell/MainWindowViewModel.cs` | 当前工程展示、创建命令、界面状态 |
| `src/AutoTest.Desktop/Projects/ProjectWorkspaceService.cs` | 唯一工程上下文；调用 ProjectDefinition/ProjectRepository，异步非覆盖创建 |
| `src/AutoTest.Desktop/Projects/NewProjectDialogViewModel.cs` | 对话框校验、路径预览、创建与取消命令 |
| `src/AutoTest.Desktop/Projects/NewProjectDialog.xaml(.cs)` | 对话框视觉与原生目录选择 |
| `src/AutoTest.Desktop/Projects/NewProjectDialogService.cs` | 设置 WPF 对话框 Owner，调起模态窗口 |
| `src/AutoTest.Desktop/Bootstrap/PrismRegistration.cs` | Prism 注册单一工程上下文服务 |
| `src/AutoTest.Desktop/AutoTest.Desktop.csproj` | 引用正式 Projects 领域工程 |
| `tests/AutoTest.Desktop.Tests/Projects/ProjectCreationSmokeTests.cs` | 持久化、重开、重复创建拒绝与名称校验 |

## 实际操作与数据

1. 主窗口空工程状态，点击菜单“文件 → 新建工程…”、顶部按钮或 Ctrl+N。
2. 输入名称、选择保存父目录，预览 `父目录/工程名称/project.autotest-project.json`。
3. 点击创建，先在目标父目录中创建随机名称的临时文件夹，在其中保存正式后端 `ProjectDefinition` 并重新打开校验。
4. 写入成功后仅将临时文件夹**移动到一个不存在的目标工程目录**，避免覆盖任何已有工程。
5. `ProjectWorkspaceService.Current` 更新，主窗口资源树显示工程根节点，工程概览显示工程名称和真实路径。
6. 失败时保留已存在的工程；在对话框给出具体错误，允许调整名称/位置后重试。

本阶段**不**生成虚假 TEST、DBC、ECU 或报告资源，也不调用 WinUSB、CAN、扫码、UDS 或硬件控制。

## 视觉验收

- 参考 QuickUse 浅色工具窗口：白色内容、淡蓝选中、细边框、常规 Windows 菜单与工具栏。
- 默认最大化；最小 1100×680 DIP；菜单 35、工具栏 46、状态栏 27、资源栏 260 且可拖动。
- 字体 Microsoft YaHei UI；正文 13 DIP；支持 100%、125%、150% 缩放。
- 打开、资源编辑等后续阶段按钮必须处于禁用状态，不能做无效操作。

## 本地 Codex 验收

```powershell
git fetch origin main local
git switch local
git pull --ff-only origin local
dotnet build src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Debug
dotnet run --project src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug
```

只有本地 build/test 通过、主窗口创建和重新读取工程操作成功，并由用户确认后，才允许 Codex 提交其修正到 `local`。不得运行 GitHub CI、不得自行合入 main。
