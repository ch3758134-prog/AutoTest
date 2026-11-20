# AutoTest｜全新纯 UI 项目

## 运行

Windows 机器使用 .NET 10 SDK；执行 `dotnet run --project UI/AutoTest.UI/AutoTest.UI.csproj`（从仓库根目录）或在 HOST/AutoTest.sln 选择 `AutoTest.UI` 启动项目。

## 定位

本工程是完整上位机交互体验的独立 UI 实施分支，不引用任何 TEST/PROJECT/DEVICE 业务程序集；用户可在各专业工作空间编辑 UI 会话内容。

`Ctrl+S` 保存 `.autotest-ui.json` 文件，仅代表纯 UI 交互会话，不是正式 TEST DSL/PROJECT 资产或真实 ECU 测试证据。

硬件写入、UDS 执行、刷写、波形采集、报告结果均不伪造。UI 上相关按钮被禁用并提供原因。

旧 G4 桌面界面及其测试、UiPreview、G4Workbench 在本 UI 实施分支移除；后端独立项目保留历史。

## 验证状态

代码已提交；Windows WPF 构建/启动/键鼠/多 DPI 测试尚未执行。用户统一 UX 验收前不得标为 PASS。
