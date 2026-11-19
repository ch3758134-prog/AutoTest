# AutoTest | G4 纯 UI 阶段：与正式业务接线彻底隔离

## 触发与边界

2026-11-16 用户以最新 Windows WPF 截图和冻结 HTML 浏览器截图指出：实际 Desktop UI 仍有明显视觉与操作链差异，尤其中央空白、右侧表单反客为主、工程状态限制 UI 审查。用户指示暂停原有全部业务接线，**先将 UI 单独实现**。

做法：在同一个被授权的实施分支 `feature/g4-html-parity-20261115` 创建**单独可启动 WPF 工程** `src/AutoTest.UiPreview/`。不删除正式业务源码，不改原来的 `AutoTest.Desktop`，也不把原有业务引用、Mock 或设备交互带进新工程。用户无需连接 USB、导入 DBC、打开真实 TEST 工程即可审查用户界面。旧程序与新预览程序不能混淆。

## 代码结构

| 文件 | 职责 |
|---|---|
| `AutoTest.UiPreview.csproj` | 完全独立 WPF WinExe，**没有 ProjectReference/PackageReference** |
| `App.xaml(.cs)` | 专用启动入口和浅色 UI 资源 |
| `MainWindow.xaml` | 原生 WPF Shell、工程树、三阶段 STEPs、底部观察、Inspector、步骤类型分组选择弹层 |
| `MainWindow.xaml.cs` | UI-only 行为：选中、折叠、搜索、添加、改名、复制、移动、删除、临时撤销及窗口工作区控制 |
| `PreviewModels.cs` | 内存演示工程和用例视图，不含任何 `AutoTest.Testing` / ECU / Devices 类型 |
| `README.md` | 唯一正确构建入口、操作与禁止范围 |

## 与冻结 HTML 的独立对照

源：`docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html` Blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`。

| UI-DIFF-ID | 视觉/操作目标 | WPF 实现范围 | 验证 |
|---|---|---|---|
| PREVIEW-01 | 顶部 IDE 栏、工具栏、主工作区域 | 原生 WPF 32/38 DIP 顶部、左245 DIP/右302 DIP/中央弹性、底部工作区 | SOURCE_ONLY |
| PREVIEW-02 | 初始有可阅读的样例用例而非空白等待后台 | 独立内存中的 HTML 同名 11 步样例，IF/THEN/ELSE、三阶段 | SOURCE_ONLY |
| PREVIEW-03 | 当前步骤与右 Inspector 绑定，非“新建参数列表” | 一个选中模型，Inspector 动态专业参数；右侧从不展示第二个创建向导 | SOURCE_ONLY |
| PREVIEW-04 | 单击添加类型后列表马上出现步骤并选中 | 原生分组 ActionPalette，UI-only 创建并打开当前步骤参数 | SOURCE_ONLY |
| PREVIEW-05 | 图标、阶段背景、搜索、选中、断点和状态可辨 | 分类色、准备蓝/测试绿/清理金、步骤行35/表头45 DIP | SOURCE_ONLY |
| PREVIEW-06 | 键鼠连续编辑，不干扰真实资源 | 增删复制移动/临时撤销、搜索 Enter/Shift+Enter、Inspector 输入联动 | SOURCE_ONLY |
| PREVIEW-07 | 最大化不遮挡 Windows 任务栏、窄屏不挤死中间 | WM_GETMINMAXINFO 多屏工作区、窄屏自动折叠 Inspector/观察栏，状态栏可恢复 | SOURCE_ONLY |
| PREVIEW-08 | 不虚假展示设备或生产运行 | 所有专业按钮有 UI-only 提示，真实运行禁用、Ctrl+S 明确不保存 | SOURCE_ONLY |

冻结 HTML 本身的样例数据并非正式运行数据，不得反向填充旧测试工程，也不得冒充产品功能已交付。

## 后续 Gate

当前只有新 UI 预览项目的源码静态核查条件；**未获得 Windows Build、WPF 启动、同尺寸浏览器-WPF 截图或用户操作验收证据**。结论：`UI_ONLY_CODE_CANDIDATE / G4_GATE_OPEN`。

本地 Codex 仅检出精确提交并运行

```powershell
dotnet build src/AutoTest.UiPreview/AutoTest.UiPreview.csproj -c Debug
dotnet run --project src/AutoTest.UiPreview/AutoTest.UiPreview.csproj -c Debug --no-build
```

不允许修改源代码、触碰真实 ECU、运行旧 Desktop 代替新 UI 或自动合并 main。将原始编译错误及视觉截图交回本对话，逐个修复 UI-only 工程，再评估逐层接回正式后端。

不在此阶段修改 TEST/Controller/Device/KEY-SEQ-01/CAN-WAIT-01/READ-STEP-01/FIXTURE-STEP-01 等未批准语义。
