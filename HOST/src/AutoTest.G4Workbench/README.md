# AutoTest G4｜正式 WPF IDE（唯一正式编辑路径）

## 正式入口

G4Workbench 现在直接启动 `AutoTest.Desktop.Shell.MainWindow`，并通过现有
`PrismRegistration` 注入真实工程、项目和 TEST 服务。

- **无** `RealEditorWorkspaceGateway`
- **无** `UiTestSnapshot`／`DemoStep`／`DemoPhase`／额外 TEST 结构副本
- 步骤由 `TestEditorViewModel`、`StepEditTransaction` 与不可变 `StepNode` 编辑
- 工程和 TEST 由 `ProjectWorkspaceService` 按 Revision/SHA 处理保存与冲突
- 测试项目直接用 `TestProjectDocument`，本次执行勾选与默认 Enabled 分离
- 右侧正式 `TestStepInspector` 使用 DBC/Controller MappingChoice 配置真实 Write 动作
- `AutoTest.UiPreview` 是独立视觉样例，不被正式工作台引用

**禁止重新接回 DemoStep 转换桥，禁止在 G4 内复制另一套 MainWindow.xaml。**

## Windows Build 与实际回归

```powershell
dotnet build src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug
dotnet run --project src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug --no-build
dotnet run --project src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug --no-build -- --verify-workspace .\G4_WORKSPACE_VERIFY.txt
Get-Content .\G4_WORKSPACE_VERIFY.txt
```

只有实际出现 `G4_WORKSPACE_VERIFY_PASS` 才表示真实文件链路验证成功。
请进一步人工检查冻结 HTML 的树层级、TEST 行距、弹窗和右侧属性。

## 交付边界

此提交实现的是**统一正式编辑入口，移除旧适配链**。UI 测试、DBC 和工程能力
来自原有 `AutoTest.Desktop` 服务。G4 入口没有独立硬件模拟后端；
实体操作仅能按现有设备会话和授权机制执行。
Windows WPF 构建、运行以及视觉一致性尚未由当前运行环境验证。
