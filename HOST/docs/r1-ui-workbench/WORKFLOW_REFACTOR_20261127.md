# AutoTest R1｜工程使用认知与工作区破坏性重构记录

日期：2026-11-27；实施分支：`R1`；禁止改动 `main`、建立第二套执行器、恢复扫码配方/TCF/TBC/工位夹具编辑器、安全 DLL 选择、TEST 内 FLASH。

## 1. 用户实际上在操作什么

1. **ECU 工程**：单型号、唯一有序 TEST；打开默认进入上次编辑的用例，左侧仅负责显示与切换 TEST 用例。
2. **工程资源入口**：DBC、标准波形、公共流程、FLASH 的归属和状态一目了然。它是导航和管理入口，不建立第二套资产库、执行器或配置文档。
3. **DBC**：专业文件工具；可以独立新建/打开、修改报文/信号、重命名磁盘文件、另存为；当前工程只有一份生效 DBC，文件重命名不得改变其信号解释或使 TEST 引用无效。
4. **标准波形**：通过 ECU 实际采集 → 多信号同次记录 → 时间质量检查 → 建立标准 → 人工比较。它是测量基准，不是普通的“新建文件/导入文件”资源类型。
5. **公共流程**：TEST 步骤定义的复用。在流程编辑环境中创建、直接改名、编辑、查看用例引用；支持从 TEST 步骤复制为公共流程，原测试步骤不被暗中替换。调用它的 TEST 仍由原执行器运行。
6. **FLASH**：工程唯一 HEX 与原生烧录参数；固定原始 SafetyFile，由软件内部提供；ECU CAN 采用同一烧录参数。真实生产 FLASH 是 TEST 前置，非用例步骤。
7. **生产工位**：扫码完整身份 → 匹配整个工程 → 固定夹具 → START → 每次烧录 → 唯一 TEST 连续执行 → RECORD → 安全释放。扫码枪、夹具和实体输入不是 ECU 工程资源。

## 2. 本次 R1 源码修改

- `Shell/EngineeringResourcesView.xaml(.cs)` + `MainWindowViewModel.Resources.cs`：建立工程资源工作区；DBC/WAVE/公共流程/FLASH 各自进入真实专业工具；刷新当前文件/标准/流程/固件状态。
- `Shell/MainWindow.xaml` 与 `Navigation/ProjectExplorerView.xaml`：TEST 用例导航只显示正式有序用例，把混杂的资源操作移至工程资源入口。
- `Dbc/DbcWorkbenchView*`、`DbcEditorDialogs.cs`、`Projects/ProjectWorkspaceService.cs`：从 DBC 工具本身新建/打开/重命名/另存为/导出；工程 .dbc 使用可读真实文件名，重命名磁盘文件时原子更新工程清单，保留稳定引用身份与内容指纹；输入已有 .dbc 后缀不重复拼接。
- `Specialists/Wave/WaveReferenceCatalog.cs`：多信号参考用明确的逻辑标准组身份管理，不按同名字符串拼凑。旧单通道参考保持独立，验证失败/重复信号不形成新的可用标准。
- `Shell/WaveWorkbenchViewModel*` 与 `WaveWorkbenchWindow.xaml`：选择实际同次采集，建立可有多路信号的标准，原始证据不修改；标准列表按标准组展示。
- `Testing/PublicFlowEditorView.cs`：在公共流程环境内直接重命名、检查哪些 TEST/流程调用本流程；属性窗口明确完成/取消，点击背景不自动关闭；删除日常修订术语。
- `Testing/TestEditorViewModel.cs`、`TestEditorView.xaml(.cs)`：步骤右键可复制为公共流程；删除 TEST UI 中遗留的旧 FLASH 授权输入；当前 TEST 仍支持单步骤、当前用例真实调试。
- `tests/AutoTest.Specialists.Tests/Wave/WaveLabAnalysisTests.cs`：覆盖多路标准创建、重新载入、同名历史单路参考隔离、重复标准/重复信号拒绝。
- `tests/AutoTest.Desktop.Tests/Projects/DbcEditorWorkflowTests.cs`：覆盖真实 DBC 文件重命名后文件路径、DBC 内容和 TEST 绑定引用不变。

## 3. 刻意保留的真实安全约束

- DBC 未知扩展不安全回写时不得假装完整可编辑或静默丢失数据。
- 标准波形只能由真实有效采集建立；单次多信号记录必须分别通过时间质量检查。原始记录不可篡改。
- 独立 WAVE 人工比较与正式 TEST 波形自动判定是不同场景。不得将人工比对自动记为生产 PASS。
- 正式 TEST 仍要求已复核的波形判据；当前 R1 的 `ConfiguredWavePort` 主要按单个信号/配置对象执行。**本次新增多路标准管理并不意味着多信号联合自动判定已实现。**
- 原有 TEST 项目连续运行、KEY_SIM 保持、CAN 周期任务等规则不因 UI 改动而变；必须通过真实主控和 ECU 回归验证。
- 生产的物理夹具、实体 START/STOP、机械到位条件、记录持久化与释放链路仍需真实接线、Desktop 适配器及实物验收，不能按 UI 成功或主控 ACK 推断全链路 PASS。

## 4. 验收状态

**代码已实际提交 GitHub R1；当前执行环境没有 Windows dotnet/WPF 编译器。** 已做的检查是文件内容回读、XAML 标签/事件结构检查、关键操作路径回读，以及新增代码测试（测试用例还没有在 Windows/.NET 环境执行）。不应写“构建通过”“可发布”“实测通过”。

第一轮现场验收必须：
1. 本地 Codex 仅拉取 R1，Release 编译与启动；如失败提供完整日志，R1 源码修复，不能由本地 Codex 改代码。
2. 工程导航/资源入口：一个正式用例列表，能直接返回上次用例；DBC、WAVE、公共流程、FLASH 入口真实可用。
3. DBC：新建、打开、实际文件重命名、另存/导出、关闭重开，以及 TEST 的 DBC 信号引用。
4. 波形：同次多信号真实采集后建立一项标准；独立单路参考不被误合并；原始采样保持不变。
5. 公共流程：用例右键复制为流程、流程重命名/查看调用方、双击编辑步骤、完成/取消，TEST 仍可调用。
6. TEST：调试不再出现旧 FLASH 授权；跨用例 CAN/KEY_SIM/IGN 状态与证据由真实主控验证。
7. 生产：所有物理输入/输出及正式 FLASH、TEST、RECORD、释放必须逐段验证；任何未接通链路不能显示虚假的成功。

## 5. 后续尚未完成的完整重构

- 由多信号标准组直接驱动 TEST 多路联合判定（时间同步与质量统一判定，保持三态结论），并整合目前分开的 `WaveSettingsDocument` 和用户的标准管理过程。
- 公共流程编辑环境与 TEST 步骤表、运行调试交互完全统一；流程提取替换调用、跨工程流程复用和参数迁移的完整链路。
- 正式生产真实物理工位 adapter、START/STOP、异常中止/释放、完整 RECORD 证据闭环和批量报表验收。
- 关闭主窗口及所有独立工具窗、历史工程升级、长期静态资源/重复配置清理、Windows 实际交互全量回归。

**不能把这个记录或本轮视觉变化当作以上未完成工作的验收。**
