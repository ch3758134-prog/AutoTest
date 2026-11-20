# AutoTest UI-01 / UI-02｜全局工作空间、文件操作与视觉微调

日期：2026-11-19｜分支 `ui/workspace-files-v01`，基于已得到用户初步认可的 `ui/test-hifi-white-v01`。  
状态：**源码已提交，静态 XAML/事件/括号校核 PASS；Windows BUILD_NOT_RUN、RUN_NOT_RUN、用户视觉/文件操作待验收**。  
**不修改 main；不接真实 DEVICE、CAN、TEST 运行器；不把 UI 草稿当生产工程。**

## 本轮完成代码映射

| 批次 | 任务 | 代码位置 / 实际动作 | 结果范围 |
|---|---|---|---|
| UI-01 | 保留白色高保真 TEST | `MainWindow.xaml`、`Styles/Theme.xaml` | 保留紧凑步骤表、右侧持续参数、问题列表 |
| UI-01 | 紫色去除 | `Models.cs`、XAML、`Theme.xaml` | THEN/ELSE/CASE 使用中性灰蓝结构色，源文件检查无 `#7B55D8` |
| UI-01 | 主控连接态 | 顶部「主控板 · 未连接」及底部状态 | 连接按钮禁用，设备诊断可导航；无伪造已连接 |
| UI-01 | 全局导航 | 文件/编辑/视图菜单；自动测试/生产检测/专业工具/记录报告 | 同窗口切换，专业工具目前只有工作页承载与能力说明 |
| UI-01 | 多文档 | `WorkspacePage` + 中部文档标签 + `ShowPage` | 文件/专业页/TEST 切换时 TEST 状态不重建 |
| UI-01 | 工程侧边栏 | `AssetList` 以资源类型分组，支持搜索、双击、右键 | 工程树不被专业工具覆盖 |
| UI-01 | 面板布局 | 列/行 GridSplitter，可恢复默认，宽度持久化 | 窄窗按步骤/参数/问题同窗切换（待 Windows DPI 实测） |
| UI-01 | 不同步骤的 Inspector | `PreviewInspector.IsCanSignal` 与非 CAN 参数布局 | ELSE/日志/等待/UDS 等不会再显示不相关 CAN1 判定表单；类型正式编辑归 UI-03 |
| UI-01 | 命令可用性 | `HasProject`、`CanSaveActiveFile`、`HasAssetSelection` | 未打开工程或未选资源时相关按钮禁用 |
| UI-02 | UI 文件工程生命周期 | `WorkspaceStorage.cs`、`WorkspaceActions.cs` | 新建/打开、资源创建/导入、文本修改、保存/保存全部/另存资源副本 |
| UI-02 | 操作与数据保护 | 文件 `*.bak`， `ui-trash/`，工程操作撤销、文件撤销历史 | 删除可在当前会话恢复；写入实际磁盘，禁止跨工程资源拖带 |
| UI-02 | 退出与恢复 | `Window_Closing`、`LocalSession`、恢复快照 | 保存/丢弃/取消、最近工程、活动页、未保存文本恢复 |
| UI-02 | 类型范围 | `UiWorkspaceManifest` `AutoTest.UI.Workspace/1` | 仅 UI 工作区 `*.atui-project.json`，正式 `project.autotest-project.json` 未接入 |

## 实测准备——必须在 Windows 上确认，不能由静态审查替代

构建：
```powershell
git fetch origin
git switch --track origin/ui/workspace-files-v01
dotnet build UI/AutoTest.TestHiFi/AutoTest.TestHiFi.csproj -c Debug
dotnet run --project UI/AutoTest.TestHiFi/AutoTest.TestHiFi.csproj
```

执行验收：
1. 确认 TEST 页面依旧是白底、TC-003 示例 29 步、灰蓝色 THEN/ELSE、主控板未连接。选中第 18 步 ELSE，确认不再显示 CAN1、DBC 判定表单。
2. 从专业工具导航打开 CAN、UDS、设备诊断、报告页，切回 TEST，应恢复原选中用例和步骤；工程资源区没有被覆盖。
3. 菜单【文件→新建 UI 工程】，选择磁盘位置并建项目。查看项目文件实际出现。
4. 工程文件→＋ 新建 TEST 文本草稿、DBC 资源；右键打开、重命名、复制；确认真正写入 `ui-assets/`。
5. 双击资源，在中央文件编辑区输入多行文字，标签出现「●」；Ctrl+S、保存全部后关闭重开，文本与文件名持续存在。
6. 修改文字，Ctrl+Z/Y 检查连续输入回退；从其他文件 Tab 切回，未保存修改不能消失。
7. 另存资源副本，确认新 ID 与独立文件；删除后查看 `ui-trash/`，Ctrl+Z 撤销恢复。
8. 关闭有未保存文件、切换工程及退出时，三选项（保存/丢弃/取消）分别有效；“取消”绝不丢编辑。
9. 退出/重新打开，最近工程、已打开文件、活动标签、TEST 用例阶段/步骤及面板宽度尽量恢复；异常退出后出现草稿快照恢复询问。
10. 1920×1080@100% 与 1366×768@150% 观察主内容可读、面板可调整、不遮挡任务栏、小屏可以在步骤/参数/问题视图间切换。真实 DPI 适配尚无运行证据。

## 技术与边界说明

- 这是**UI-01 与 UI-02 范围内可提交的真实 WPF/本地文件操作候选**，不是项目最终 R1 的正式 PROJECT/TEST 接线版本。导入的 .dbc 是 UTF-8 文本副本，信号选择仍然使用演示数据，不能宣称真实 DBC 解码和测试执行。
- TEST 主页面依然采用设计样本。真正步骤树节点修改、参数写回正式 TEST DSL、公共流程修订与依赖检查属于 UI-03/后端合同，不能靠 UI-02 文件文本编辑器假装解决。
- 专业工具工作页当前用于**验证导航与状态恢复**；CAN/UDS/Flash 真实操作仍禁用，不能生成假 ACK/PASS。
- 完整生产扫码、USB/CAN、实物按键 IO 接入不在本批，不能因界面上有入口而宣称完成。
- 本地草稿 `*.atui-project.json` 文件是为本次阶段的文件交互验收建立，不能混进生产工程，且未来接正式 Schema 时需迁移/剔除该 UI 草稿格式，不直接改写已有生产配置。
- 代码源文件回读：12 文件；MainWindow.xaml/Theme/App/SignalPicker XML 标签配对检查通过、44 个 WPF 事件处理器均找到声明、`x:Name` 唯一、所有源码括号静态扫描无错、无饱和紫色 `#7B55D8`。这**不等于 Windows 编译通过**。
- 如果 Windows 报错，按具体 CS/XAML 错误修复候选分支；所有修改继续在本分支，不提交 main。

## 交付判断

**UI-01 + UI-02 的独立 WPF 实施候选已完成代码编写和源码静态校核，可以进入 Windows 编译与操作验收；不能声称用户已经验收通过。** 真正连接正式 AutoTest PROJECT/TEST 领域合同仍是待办，不与当前纯 UI 文件格式混为一谈。
