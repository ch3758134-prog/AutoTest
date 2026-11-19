# AutoTest｜Issue #53｜G4 冻结 HTML 严格 WPF 整改候选（2026-11-15）

**状态：CODE_CANDIDATE / WINDOWS_BUILD_UNVERIFIED / VISUAL_DIFF_OPEN；不得宣布 UI-03 准出。**

## 1. 正式基准与授权

- 项目管理裁决：[Issue #53 comment 6906604305](https://github.com/ch3758134-prog/AutoTest/issues/53#issuecomment-6906604305)，结论 `APPROVED_WITH_EXISTING_AUTHORITY`。
- 单一修改分支：`feature/g4-html-parity-20261115`。本次整改起点：`1c351b6804685ed654de69cdf2c8a860e7b6996f`。
- 本说明所审查的源码提交：`ab726a76c87fd4aada5b834f382394113f5b243e`；新增此交接文档后，GitHub 分支最终 HEAD 将另行变化，以分支实际 HEAD 为准。
- 唯一 UI 设计标准：`docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html`，Git Blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`。本次未修改。
- 仅原生 WPF/Prism、正式工程/TEST/DBC 资产；未修改 `main`、固件、实体设备/安全、Flash/UDS/生产后端；未操作 ECU/工位。

## 2. 这轮实际代码整改

从起点到本报告源码 HEAD：**23 个提交、31 个变更文件；新增约 2,125 行、删除约 837 行。** 其中包含 1 个正式 Desktop 项目选择行为测试文件。这只是源码变化规模，不等于 UI 合同已验收。

| 范围 | 已变更的表现/接线 | 有意保留的正式真实能力 |
|---|---|---|
| Shell | 重写 `MainWindow.xaml/.cs`：单 38px 原生 Windows Chrome 顶栏、39px 工具栏、270px 左区、35px 页签区、320px Inspector、180px 观察区、23px 状态栏；默认最大化、停靠可折叠及拖动 | 单 `MainWindowViewModel` 的实际项目打开/关闭草稿保护、会话及设备授权 |
| 左工程导航 | 新建 `Shell/Navigation/ProjectExplorerView.xaml/.cs`，全面替换 Windows 默认树节点模板；重写 `ResourceTreeNode` 与真实项目→用例的投影；独立「自动测试」「工程资源」分区、SVG Path 图标、内联创建/临时勾选；已引用的 TEST 不再重复出现在「未加入项目」 | `AssetId`、`TestProjectEntry.ItemId`、项目 Revision/SHA，不增第二套工程模型 |
| 单次选测 | 新建 `Projects/ProjectRunSelectionState.cs`，工程树与测试项目行复用同一 (project,item) 的临时勾选身份；初值来自 `Enabled`，不持久化；新增 `ProjectRunSelectionStateTests.cs` | 原 `TestProjectDocument.Packages` 顺序、默认 Enabled 和真实保存 |
| 工程概览 | 重写三卡片工作区 `ProjectOverviewView.xaml` 和精简 `ProjectOverviewInspector.xaml`；删除之前擅加的主页冗长状态/操作块 | 工程资源计数和正式项目状态绑定，不使用演示计数 |
| 测试项目 | 重写 `TestProjectWorkbenchView.xaml/.cs` 与选择 Inspector：突出「本次执行」选择栏，45px 有序用例行，行内打开/上移/下移/移除、默认启用、真实步数 | `TestProjectWorkbenchViewModel` 的保存/撤销性草稿保护、正式 PackageRevision/SHA 与预检阻断 |
| TEST 步骤 | 重写 `TestEditorView.xaml`：29px Prepare/Test/Cleanup、25px Slot、35px Step 五列、阶段直接添加、行内 Wait/Loop、现有键鼠及拖放；保留 `StepRowProjection` 的唯一 `StepNode` 投影 | `StepEditTransaction`、`TestDocument/StepNode/Slots/StepId`、正式编辑验证与资源 pin |
| Inspector | 重写 `TestStepInspector.xaml`，修改旧四列表格为 320px 右栏逐个 DBC 信号物理值/单位/范围输入；参数仍由既有受控 C# 编码/校验 | `TestTargetCatalog`、DBC 实际解析、原子 Apply、草稿取消/关闭保护 |
| 工程配置 | 新增 `ProjectConfigurationOverviewView/Inspector` 与 `ConfigurationInlineEditorView`；DUT/输入/工位/预检四子页签，真实 TCF/Controller/TBC `ConfigurationWorkbenchViewModel` 嵌入，创建/打开不跳回旧通用配置页 | 真实配置资源、Revision、保存冲突和校验。专业 Wave/Scan/Flash 自身编辑器仍归专业边界 |
| 统一主题 | 重写 Buttons/Inputs/Navigation，新增 Tables ResourceDictionary；主要输入/表格/hover/selection/disabled 采用冻结 HTML 的紧凑浅色标尺 | 原生 WPF，无 WebView2、HTML JS 仿真或第二套执行器 |

原 G4 首版已经删除的 `StepParametersDialog.*`、`TargetMappingDialog.*` 仍保持删除，未恢复旧高频弹窗。与当前整改无关且有实际用途的专业工具界面和业务服务没有为追求删除数量而破坏。

## 3. HTML U-01～U-16 真实覆盖边界

| 合同 | 当前整改候选状态 | 必须在 Windows/专项补证的内容 |
|---|---|---|
| U-01 / U-02 工程/导航 | PRESENTATION_IMPLEMENTED / WINDOWS_UNVERIFIED | 恢复最近工程、节点交互、旧工程资源、缩进与键鼠 |
| U-03 / U-04 测试项目/临时选择 | PRESENTATION_IMPLEMENTED / WINDOWS_UNVERIFIED | 真实项目重开、树与表双向勾选、引用失效和修改默认后保存 |
| U-05～U-08 阶段/步骤/流程/新增 | PRESENTATION_IMPLEMENTED / WINDOWS_UNVERIFIED | >100 步骤、大深度 IF/SWITCH/WHEN、拖动、插入与 Save/Reopen |
| U-09 Inspector/DBC | PRESENTATION_IMPLEMENTED / WINDOWS_UNVERIFIED | 实际导入 DBC，Signal 物理量/范围/编码与错误阻断 |
| U-10 行内编辑 | PARTIAL | Wait/Loop 直接编辑；IGN、夹具等仍通过右侧真实 Inspector；需同 HTML 对照 |
| U-11 / U-12 键鼠/多资源页签 | PRESENTATION_IMPLEMENTED / WINDOWS_UNVERIFIED | F2/F9/Ctrl/鼠标焦点、关闭前草稿保护、主页签状态 |
| U-13 观察 | PARTIAL / REAL_DATA_ONLY | 已有 CAN/单 TEST 事实；缺正式项目级时间线/变量/Occurrence 事件 |
| U-14 项目检查/运行 | BLOCKED_BY_BACKEND | 尚无项目 Run/StationLease；仅真实资产身份检查；禁止模拟 PASS |
| U-15 生产扫码 | BLOCKED_BY_BACKEND | 扫码输入已有受控匹配入口；生产运行不引入 Mock 或伪结论 |
| U-16 专业工具 | PARTIAL / OUT_OF_THIS_SLICE | 保留真实 DBC、CAN 监视和设备连接；其他独立工作台不冒充本轮完成 |

## 4. 当前审查证据与未完成

- GitHub 已完成顺序提交和远程分支 HEAD 更新，可按指定 SHA 回读。
- 曾对整改后的 11 个核心 WPF XAML 执行标签成对静态检查，并对其 C# code-behind 的事件名进行检查，未发现标签栈或事件名缺失。该检查不等于 WPF XAML 编译器验证。
- **本执行环境无 Windows/.NET SDK；没有 Windows Debug Build、WPF 实际启动截图或控件焦点操作记录。** 不得写 Build PASS，也不得把 GitHub 静态检查当成真实编译。
- **V-UI-01～08 未准出。** 1366×768、1920×1080、2560×1440 与 100%/125%/150% DPI 的 HTML/WPF 同尺寸同流程对照仍需 Windows 现场完成；任何明显偏差记录 UI-DIFF 并修复/获得用户明确接受。
- 未引入 UI Mock、随机 Run Facts、WebView2、硬件自动执行、实体 IGN/夹具/CAN 写入或 ECU 刷写；独立 Flash Lab 不在本候选修改中。
- 新的桌面行为测试是对真实 **transient selection state** 的单元断言，不是 Mock ECU / 模拟 Run / 物理测试。

## 5. Codex 本地机械验证职责（不规定本地路径）

获取本分支最新精确 HEAD，确认无未提交工作被覆盖；仅执行一次 Windows Debug Build 并收集完整原始错误/警告；若构建成功，启动真实 WPF，确认单顶栏、四个工作区、项目→用例勾选、工程概览、右 Inspector、底部观察。然后由项目负责人依 G3-01 在相同项目、分辨率和操作序列下对冻结 HTML 截图比较。若失败，Codex 不自行修改/修复源码，只提交原始日志和截图，由主对话诊断并在同一特性分支精确修复。禁止 CI、main 合并、实体设备写入、生产 Run 和 Flash。

**Gate 裁决由项目管理 AI 与项目负责人做出：这只是整改后的真实源码候选，不是自我批准的“严格 HTML 已完成”。**
