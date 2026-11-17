# AutoTest G4 · 正式工程及测试程序编辑工作台

本工作台和纯 UI 预览使用**相同的 WPF 窗口与共享控件**。新增真实工程模式
`AutoTest.G4Workbench`，保留 `AutoTest.UiPreview` 原样例预览运行方式。
不启动 USB、CAN、UDS 或任何 ECU 执行代码。

## Windows 命令

```powershell
dotnet build src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug
dotnet run --project src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug --no-build
```

正式入口左上「新建工程」或「工程」打开 `project.autotest-project.json`；
左树选择或新建 TEST，编辑步骤，按「检查问题」，再按「保存」。
保存由 `ProjectWorkspaceService.SaveTestEditorAsync` 处理项目资源的版本冲突与原子文件替换。

## 文件级真实性验证（与真实设备无关）

```powershell
dotnet run --project src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug --no-build -- --verify-workspace .\G4_WORKSPACE_VERIFY.txt
Get-Content .\G4_WORKSPACE_VERIFY.txt
```

必须看到 `G4_WORKSPACE_VERIFY_PASS` 才能声称真实工程创建、TEST 保存回读通路验证通过。
此操作创建后清理系统临时目录下的真实工程文件，不使用 Mock 或设备模拟。

## 能力边界

- 使用真实 `ProjectWorkspaceService`、`TestEditorSession`、`TestDocument`、
  `StepDescriptorCatalog`、`StepEditTransaction`、`TestDocumentCodec`；
  保持真实步骤 GUID、嵌套 Slots 和已有资源 Mappings。
- 真实工作台可新建/打开工程，创建/打开多个正式 TEST、编辑步骤、切换、撤销/重做、
  检查结构、保存回读。通过已有项目服务处理资源 SHA / Revision 冲突。
- 工程资源从真实 Manifest 读取，可通过既有服务导入 DBC；正式 TEST 资源可通过既有服务重命名。
- 新增步骤来自正式 `StepUiCatalog`，不是旧的 `DemoStep` 示例类型。
  必填映射及参数未配置时，保存应失败并指出问题；不会生成假 DBC/UDS 绑定。
- 左侧共享工程树可展开真实 DBC 文件、报文与信号；右侧 CAN 发送步骤可选择已导入的真实
  DBC 报文，勾选信号并输入物理值。保存时检查 DBC 资产 ID、Revision、SHA256 和数值范围。
  配置字段不等于可执行的 CAN 发送步骤；真正的执行服务仍待后续单独接线。
- Controller 对象映射编辑以及设备和自动执行仍未接入。
  右侧的启用/断点属于编辑标记，不应被解释为真实执行引擎已经执行或跳过步骤。
- `AutoTest.UiPreview` 仍保留冻结 HTML 样例，不包含业务项目依赖。
- 当前 GitHub 代码提交完成，但 **Windows WPF Build/启动/验证脚本尚未经本环境执行**。
