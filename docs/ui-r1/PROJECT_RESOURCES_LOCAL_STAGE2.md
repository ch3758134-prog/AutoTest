# AutoTest ｜ local 工作批次 2：主窗口与基础工程资源

## 真实操作范围
1. 修复新建工程弹窗顶部文本遮挡：顶栏 78 DIP、全窗 445 DIP；中间内容超出时支持独立滚动。
2. 去掉实心深蓝主按钮：按钮用白色/灰蓝浅色背景和普通边框，重要操作通过位置、文字而非大面积填色区别。
3. “文件 → 打开工程”、顶部“打开工程”和 Ctrl+O 都调起原生 Windows 选择框，**默认从 `AppContext.BaseDirectory`（EXE 所在目录）打开**，可以在选择框中改路径。工程资源仍使用清单目录下的相对路径，绝不存储 EXE 目录下的假的资产路径。
4. 新建工程继续用 `ProjectDefinition.Create` 与 `ProjectRepository.SaveDefinitionAsync`。打开工程用 `ProjectRepository.OpenDefinitionAsync`，不新建第二套工程数据。
5. 左侧工程树显示测试程序与用例、DBC、控制器配置、波形参考、固件与烧录资源分类和数量。**空节点是纯 UI 提示**，不会写入工程清单。
6. “新建测试程序”用 `TestDocument.Create`、`TestDocumentCodec.Serialize` 生成真实空 TEST 文档并登记到清单；“导入 DBC”将本地真实 .dbc 文件复制到工程内相对目录并登记 SHA256。
7. 选中真实资源后显示名称、相对路径、类型、修订；可以 F2/右键重命名，或“移除引用”，**只更新清单，不删除原资源文件**。被其他资产依赖的条目无法直接移除。
8. 保存资源操作通过任务异步执行，检查工程清单的哈希以拒绝覆盖已被其他进程修改的版本；大 DBC 导入有 32 MB 的保护上限。本批不实现 DBC 报文解析和 TEST 步骤编辑器。

## 本批文件
- 修改 `src/AutoTest.Desktop/Themes/Tokens/Colors.xaml`、`Themes/Controls/Buttons.xaml`
- 修改 `src/AutoTest.Desktop/Projects/NewProjectDialog.xaml`、`NewProjectDialogViewModel.cs`、`ProjectWorkspaceService.cs`
- 修改 `src/AutoTest.Desktop/Shell/MainWindow.xaml`、`MainWindow.xaml.cs`、`MainWindowViewModel.cs`
- 修改 `src/AutoTest.Desktop/AutoTest.Desktop.csproj`、`Bootstrap/PrismRegistration.cs`
- 新增 `Projects/ResourceTreeNode.cs`、`ProjectFileDialogService.cs`、`ResourceNameDialog.xaml`、`ResourceNameDialog.xaml.cs`
- 新增 `tests/AutoTest.Desktop.Tests/Projects/ProjectResourceManagementTests.cs`

## 只在本地验证
Codex 只使用 `local` 分支，先确认保存现有修改再 `git fetch origin local` 与 `git pull --ff-only origin local`。本地 Debug Build/Test、显示和操作检查。只修此批 UI + 项目管理接线的 Bug，不修改领域工程，不运行 GitHub CI，也不自主合入 main。

重点检查：新建工程弹窗 100/125/150% 缩放没有遮挡、所有蓝色大面积按钮已取消、Ctrl+O 打开 EXE 目录、真实工程切换、空分类不伪造资源、TEST/DBC 生成文件与清单一致、F2/右键重命名、移除引用保留原文件、外部工程清单修改时拒绝覆盖。
