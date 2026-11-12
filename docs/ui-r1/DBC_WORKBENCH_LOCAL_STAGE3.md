# AutoTest｜第三阶段：DBC 工作台（GitHub local 待实测）

## 用户工作流
1. 在正式工程中导入 `.dbc`（作为真实工程资产；落盘为 `assets/dbc/<guid>.dbc`）。
2. 左侧 DBC 资源双击，或选中后通过右键／工具栏“打开 DBC”启动独立工作台窗口。
3. 工作台报文列表按 CAN ID 排序，选择报文显示其信号表：起始位、位长、字节序、符号、因子、偏移、物理范围、单位、接收节点、复用标记。
4. 通过编辑报文／信号按钮打开参数对话框。只有“应用参数”才写入内存编辑草稿；未保存标志明显提示。
5. 通过 Ctrl+S 或“保存 DBC”调用 `DbcDocumentStore.SaveAsync`，检查文件 SHA，使用后端 `DbcWorkbenchDocument.ExportText` 进行回写语义校验，验证实际文件重新读取成功，再更新工程清单对应资源的 SHA256 和 Revision。
6. 用 F5 或“重新加载”重新读取，验证报文／信号的更改仍在。未保存时关闭或重载必须询问。
7. 文件和工程清单被外部修改后，保存拒绝覆盖；后端保留 DBC `.bak`。若文件写入成功但工程清单写入失败，报错并提示检查备份，不谎报成功。

## 现有后端边界，严禁绕过
- DBC 解析、语义模型、编辑验证、只读判断、导出保存来自 `AutoTest.Projects.Dbc`。
- `DbcWorkbenchDocument.CanEdit=false` 的文件不可修改保存。原始 DBC 保留；尽可能展示后端可解析的报文和信号。例：带 `CM_`／`BA_` 等尚未支持回写语法的 DBC。
- 当前 `DbcDocumentStore.OpenAsync` 文件上限为 4 MB，导入上限因此与其对齐；非 UTF-8 文件会被后端拒绝打开；不会悄悄使用损坏字符代替。
- 当前后端写回限定 8 字节数据长度、标准 CAN ID 的安全编辑路径；不能声称支持 CAN FD、全部扩展帧及任意厂商 DBC 指令。
- 本批不修改 `AutoTest.Projects`，不再实现第二套 DBC 解析或 serializer。
- 这次没有添加新信号、删除报文、编辑 VAL_ 值表，也没有实现实时 CAN 解码 UI；这些是后续独立的阶段。

## 文件责任
- `src/AutoTest.Desktop/Projects/ProjectWorkspaceService.cs`：读取已导入 DBC、保存/重新读取、更新工程资源 Revision/Hash。
- `src/AutoTest.Desktop/Projects/DbcEditorSession.cs`：文件 SHA、所属工程与资源修订的打开上下文。
- `src/AutoTest.Desktop/Dbc/DbcWorkbenchWindow.xaml(.cs)`：独立报文/信号浏览与参数详情。
- `src/AutoTest.Desktop/Dbc/DbcWorkbenchViewModel.cs`：选中、编辑草稿、保存/重载状态和操作。
- `src/AutoTest.Desktop/Dbc/DbcMessageEditDialog.xaml(.cs)`、`DbcSignalEditDialog.xaml(.cs)`：参数编辑。
- `src/AutoTest.Desktop/Dbc/DbcEditorDialogs.cs`、`DbcWorkbenchWindowService.cs`：WPF 对话框与窗口展示。
- `src/AutoTest.Desktop/Shell/MainWindow.xaml(.cs)`、`MainWindowViewModel.cs`、`Bootstrap/PrismRegistration.cs`：从工程资源打开 DBC。
- `tests/AutoTest.Desktop.Tests/Projects/DbcEditorWorkflowTests.cs`：导入/修改/保存/重开、只读保护、磁盘冲突、文件上限。

## 本地质量要求
Windows Debug Build + Desktop.Tests；启动 UI，导入包含真实 11-bit 报文和信号的 UTF-8 DBC；修改 Factor、Offset、DLC，保存，关闭工作台，重新打开并核实资源 SHA 和修订一致；测试只读 DBC 与错误提示；检查对话框 100%/125%/150% 缩放、键盘与列表交互。只让 Codex 提交完整差异或错误日志；不得强行 git reset/clean，也不操作 GitHub CI。
