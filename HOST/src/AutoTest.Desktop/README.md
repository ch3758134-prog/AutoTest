# AutoTest.Desktop — 全新纯 UI 工作环境

此分支已以一个独立 WPF/Prism 桌面应用替换旧 G4 Desktop 表现层。本项目**不引用** AutoTest.Testing、AutoTest.Projects、AutoTest.Devices、AutoTest.FlashLab 等真实业务程序集。

## Windows 运行
```powershell
cd HOST
dotnet restore .\src\AutoTest.Desktop\AutoTest.Desktop.csproj
dotnet run --project .\src\AutoTest.Desktop\AutoTest.Desktop.csproj
```

在窗口使用 Ctrl+N 新建测试用例、Ctrl+S 保存 `.at-ui.json` 操作会话、Ctrl+O 重新打开、Ctrl+Tab 切换文档。

### 边界
`.at-ui.json` 是纯 UI 操作会话，**不是正式工程、不是 TEST DSL，也不是可执行脚本**。显示的诊断阶段是设计结构，不代表已经运行。波形绘制只绘制用户明确手工填入的点，绝不自称设备采样。不能发送 CAN、UDS、Flash、IGN 或 KEY 实体命令。该版本用于统一 UI 验收，实际运行与真实保存需要后续另外接线。

构建与运行未在此工具环境验证，不应将此 README 视为 Windows 实测通过。
