# AutoTest｜全产品 Fluent UI 设计与纯前端实施 v1
日期：2026-11-19
实施分支：feature/fluent-bench-test-ui-20261119
产品设计状态：DESIGN_V1_COMPLETE / WHOLE_PRODUCT_SCOPE_COVERED
源码状态：PURE_FRONTEND_CODE_COMMITTED / STATIC_CHECK_PASS / WINDOWS_BUILD_NOT_RUN / VISUAL_ACCEPTANCE_PENDING
后端状态：NOT_CONNECTED（刻意隔离）
用户界面语言：简体中文；浅色主题；现代 Windows 桌面

## 一、设计立场

本 UI 专项设计覆盖整个 AutoTest Windows 上位机，而不是只考虑自动测试和手动台架。旧 G4 源码完整留在 main；本 feature 分支已清理旧 Desktop、G4Workbench、UiPreview 和旧 UI 测试，使用新的 src/AutoTest.Frontend 重建整个可运行的界面外壳。无真实业务项目引用，不重复开发 TEST/PROJECT/DEVICE/UDS/FLASH/WAVE/SCAN/RECORD 模型。

需要区分：
1. 已完成的产品 UI 信息架构和呈现设计；
2. 已编码的纯前端页面及用户交互；
3. 未完成的 Win11 实际渲染与用户视觉验收；
4. 未完成的业务服务与实体设备接线。

不可将其中某一项成功替代其他项。

## 二、产品级信息架构

### 主窗口
- 唯一顶层 AutoTest 主工作台。默认进入“运行 · 当前任务”。
- 启动恢复 UI 草稿；后续正式恢复上次工程由 PROJECT 适配；不将假文件加载称为工程恢复。
- 左侧为常规 IDE 资源浏览树，包含测试项目/用例、公共流程、DBC、波形、输入配置和烧录配置。可按需查看及筛选资源。
- 主内容区域严格保留“运行 · 当前任务 / 编辑 · 测试用例”两个页签，共享当前选中用例。正式 TEST 编辑器的专业能力由 TEST 提供。
- 顶部提供项目状态、执行入口、工具菜单；记录报告和扫码生产使用独立功能窗口，不占据自动测试主屏。
- 运行中编辑锁定由正式运行状态事件决定，当前纯前端不会合成一个假“运行中”。

### 工具副窗口
- 工具统一入口：手动台架、UDS、烧录、DBC、波形、输入模拟配置、设备诊断。
- 每类窗口唯一实例；重复调用激活旧窗口，不堆叠同类实例。
- 设置 Owner 为 AutoTest 主窗口，关闭主窗口时由 OnMainWindowClose 退出全部副窗口。
- 不存在一个额外的独立 CAN 工具窗口。CAN 收发/监视属于手动台架和相应专业上下文。

## 三、全产品 UI 页面清单

| 页面 | 独立文件 | 视觉/用户工作流 | 当前代码状态 |
|---|---|---|---|
| 主 Shell | MainWindow.xaml | 工程、文件、自动测试双页签、工具入口、状态 | 已编码，待 Win 构建 |
| 自动测试运行 | Views/RunWorkspace.xaml | 临时选测、当前用例、未执行步骤、真实日志与报告容器 | 已编码，不模拟 TEST |
| TEST 编辑承载 | Views/EditorWorkspace.xaml | 步骤表、右 Inspector、就地改名、插入/移动/复制/删除、F9 | 仅 UI 草稿，无 TEST DSL |
| 项目与文件 | MainWindow.xaml + UiWorkbenchState.cs | 资源树、名称筛选、右键、F2、删除确认、自动保存提示 | 已编码，非 PROJECT |
| 手动台架 | Tools/ManualBenchView.xaml | 左 CAN 发报、中工位及输入动作、右接收/操作记录；高级 KEY20 | 已编码，所有实体命令禁用 |
| UDS 诊断 | Tools/UdsView.xaml | 服务库、可保存请求序列、参数和响应区 | 已编码，不能实体发送 |
| ECU 烧录 | Tools/FlashView.xaml | 镜像路径、Profile 草稿、预检、阶段、UDS/审计 | 已编码，不预检、不烧录 |
| DBC 编辑 | Tools/DbcView.xaml | 报文列表、信号编辑、位视图区、属性 | 已编码，不读写真实 DBC |
| 波形分析 | Tools/WaveView.xaml | 信号、宽幅绘图区、参考/容差、采样质量 | 已编码，无假曲线/判定 |
| 输入模拟配置 | Tools/InputConfigView.xaml | 工程状态命名、20 个内部电阻开关组合、五路输出说明 | 已编码，不代表物理电压 |
| 设备与诊断 | Tools/DeviceView.xaml | 设备身份、连接/断开入口、通路角色、错误证据 | 已编码，真实连接禁用 |
| 历史与报告 | Tools/RecordsView.xaml | 执行记录、查询筛选、报告、步骤证据、统计 | 已编码，无假报告 |
| 扫码生产 | Tools/ProductionView.xaml | 编号输入、工位、真实 Run、结果和统计 | 已编码，扫码/运行禁用 |

完整产品设计范围覆盖已归档 R1 P01~P13 功能；但 13 个功能领域并非 13 个常驻主导航按钮。

## 四、统一视觉系统

- WPF UI 4.3.0 + App.xaml Fluent Light。
- Resources/WorkbenchStyles.xaml 集中放置 AT.Canvas、AT.Surface、AT.SubtleSurface、AT.Border、AT.Text、AT.Muted、AT.Accent、AT.Success/Warning/Danger 等。
- 中文 UI 采用 Microsoft YaHei UI；常规数据表 12px，正文 13px，二级标题 15px，页面标题 20px。
- 少量明确的主操作使用强调色。表格密度要优先保证信息完整，不用全卡片化覆盖数据。
- 原生 Windows 窗口标题/最大化/任务栏；不重复实现 Win32 自绘标题栏。
- 窗口按专业内容进行不同宽度分配：波形给绘图区，UDS 给请求与响应，烧录给任务阶段与日志。
- 普通字段就地编辑；高级硬件组合藏在展开编辑中，不占据手动台架日常核心区域。
- 简体中文用户语义；KEY_SIM 20 bits 并不是 20 个业务功能键；EPB 驻车/释放不作为通用默认业务按钮。

## 五、统一交互与状态合同

- 运行主入口始终明确；运行/编辑共用当前用例的 UI 选择。
- 工程编辑自动保存：**当前实现只针对前端演示草稿**，自动保存于 LocalAppData/AutoTest/FrontendPreview/ui-draft.json。正式 PROJECT/TEST 文件自动保存由专业适配接入。
- 有独立导入/导出 .ui-draft.json 操作；不伪装为正式 TestDocument、工程 manifest、DBC 或 RunRecord。
- 工程树支持右键、双击、F2、Ctrl+F、Delete；步骤演示编辑提供新增/复制/上下移动/删除、F9 断点标记；Ctrl+S 强制保存 UI 草稿。
- 请求、主控 ACK、ECU 响应、外部测量、测试判定和 UNKNOWN 属于不同事实；不可在 UI 中合成为一条假的成功状态。
- 真实设备未接入：CAN/UDS/KEY/IGN/夹具/Flash/Run 等实体执行按钮全部不可用；日志、波形、报告展示真实空状态。
- CAN 原始帧输入不强依赖 DBC；DBC 是可选编码/解码配置支持，不能替代 CAN 真实业务服务。
- 自动测试与工具之间的实体工位所有权、运行期间的可编辑性由正式 TEST/DEVICE 接口提供，UI 只能呈现准确状态，不能自行发放硬件许可。

## 六、实施边界与分工

- UI：负责全部产品呈现、交互、资源探索、窗口与导航、统一状态和人体工学；当前新源码独立无 ProjectReference。
- TEST：正式步骤树/编辑器领域、事务与验证、流程/运行语义、调试、证据。
- PROJECT：正式工程文件资源、DBC/配置版本与文件保存。
- DEVICE/UDS/Flash/Wave/Scan/Record：专业业务与真实事实。
- INTEGRATION：经用户视觉/交互认可后逐个连接 View/VM、命令状态、事实与资源，不能直接恢复旧 G4 页面覆盖新 UI。

旧 main 中“严格按 2026-11-15 冻结 HTML 复刻”的条款是历史基线；用户已明确授权本分支重新设计视觉。新的设计基准正式替换旧 HTML 之前，须由项目管理按精确 Git SHA 版本化更新审查，保留旧文件追溯。

## 七、当前代码清单与 Windows 验收

源目录：src/AutoTest.Frontend/
- App.xaml / Resources/WorkbenchStyles.xaml：全局主题
- DraftModels.cs：UI 草稿字段，绝不作为生产 DSL
- UiWorkbenchState.cs：跨窗口共享展示会话，UI 草稿自动保存
- MainWindow.xaml(.cs)：主工作台、工程文件和副窗口管理
- ToolWindow.xaml(.cs)：统一副窗口外壳与生命周期
- Views/RunWorkspace, EditorWorkspace：运行/编辑
- Tools/ManualBench, Uds, Flash, Dbc, Wave, InputConfig, Device, Records, Production：专业页面

本轮 GitHub 静态检查：
- XAML/XML 标签结构检查：通过（不是 WPF 语义/编译证明）。
- 页面事件与 CodeBehind 名称对应检查：通过。
- AT 静态资源引用检查：通过。
- UI 新工程 ProjectReference 数量：0。
- Windows Debug Build、启动与用户操作截图：NOT_RUN。
- 物理 USB/CAN/ECU 实测：不在本次范围、NOT_RUN。

Windows 机械验证：
1. 从指定 feature 分支精确 HEAD 拉取。
2. dotnet restore src/AutoTest.Frontend/AutoTest.Frontend.csproj
3. dotnet build src/AutoTest.Frontend/AutoTest.Frontend.csproj -c Debug --no-restore
4. dotnet run --project src/AutoTest.Frontend/AutoTest.Frontend.csproj -c Debug --no-build
5. 1920×1080、1366×768；100%/125%/150% DPI：核查标题栏、工程树、页签、主按钮、底部状态、滑动/缩放和任务栏。
6. 双击文件打开编辑；F2 改名、Ctrl+F 查找；新增/复制/移动/删除步骤，F9；切运行页签看相同用例；关闭/重启验证草稿恢复。
7. 逐个打开九类副窗口，编辑 CAN/KEY/UDS/DBC/波形/输入配置，关闭并重开，验证状态保存；主窗口关闭时副窗口都退出。
8. 确认所有实体操作按钮没有真实发送、不会出现假 ACK/假 CAN 帧/假波形/假 PASS。
9. 记录实际截图、编译错误或交互问题，由 UI 专项修改当前分支。不要让 Codex 自行接线或重新设计。

## 八、后续迭代标准

这次不是宣告全部专业交互已最终验收。Windows 实测后需要继续：
- 修正实际 DPI/屏幕尺寸下的视图截断和重点区域比例；
- 提升工程树和专业编辑的键鼠交互、模态边界及空态操作；
- 检查 UDS 请求编辑、波形游标布局、烧录阶段可读性、报告格式预留等是否符合真实专业用户习惯；
- 冻结用户认可的视觉后再出“可接线 UI 组件合同”，避免页面与正式后端同时任意修改。

交付结论：CODE_CANDIDATE / COMPLETE_SCOPE / NOT_BUILT / NOT_USER_ACCEPTED / NOT_CONNECTED。
