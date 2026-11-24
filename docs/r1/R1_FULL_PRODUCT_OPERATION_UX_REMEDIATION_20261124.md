# AutoTest R1｜全产品操作路径审查与源码整改台账（2026-11-24）

> 性质：**R1 WPF 源码级审查和已提交变更的记录**。只在 R1 实施；不代表 Windows 编译、WPF 启动、实机测试或生产准出。禁止以 HTML、模拟状态、测试工程样例、源码提交或静态推断替代实体证据。

## 0. 来源、基线与本轮界限

- 本轮修改前 R1 HEAD：`7a35a1ca678237f94dc45c901a31c13dccf48e00`，2026-11-24 07:50 UTC。
- 直接核对的 R1 合同：`docs/r1/R1_OPERATOR_PATH_AUDIT_AND_REMEDIATION_20261123.md`；`docs/r1/product-integration/R1_PRODUCT_INTEGRATION_CONTRACT.md`；`docs/r1/product-integration/R1_MINIMAL_CONFIGURATION_BOUNDARY.md`。
- 独立核对原设计分支：`ui/r7-full-product-ux-design-20261121:docs/ui-research/R7_FULL_PRODUCT_UX_SPEC.md`、`ui/r7-1-com-scanner-production-20261122:docs/ui-research/R7_1_COM_SCANNER_PRODUCTION_REVIEW.md`、`ui/r7-2-production-test-list-20261122:docs/ui-research/R7_2_PRODUCTION_TEST_MONITOR_UX.md`。
- 设计优先级：用户最后确认的业务决策 > R1 集成合同 > R7 设计候选 > 既有旧 UI。R7 是浏览器原型，不等于 R1 WPF 功能；旧描述的强制 Prepare/Test/Cleanup UI 分组不复活；唯一正式执行序列不额外提供生产用例勾选。
- 审查手段：读取实际 XAML、ViewModel、工作区服务和 RECORD/Flash/Wave 相关源码，追踪命令及绑定。**以下“点击次数”只能是源码可推断的入口动作，尚无 Windows 人工实测计数**；不将推断冒充实际交互录像。

## 1. 七条真实任务路径（源码观察）

| 路径 | 实际起点/主要窗口 | 源码可推断入口序列/交互负担 | 实际数据/命令来源 | 本轮结果与留存问题 |
|---|---|---|---|---|
| A 新建 ECU 工程 | `MainWindow` → 文件/新建工程 → `NewProjectDialog` → `ProjectWorkspaceService` | 进入开发与调试、文件→新建，输入工程名/型号代码/位置，创建；若使用菜单约 3 次导航/提交点击，未计文本录入及文件夹选择 | `CreateReadyToEditAsync` 原子创建 PROJECT + 单一有序项目 + 空白 TEST | **已改**：新增型号代码输入、严格校验并写工程身份；默认可编辑 TEST 仍真实创建；窗口体验待测 |
| A 添加用例 | 主菜单/工程树“新建测试用例” → `TestProjectWorkbenchViewModel` → TEST 编辑器 | 一次选择新建入口、一次命名提交；不需先手工创建独立定义、再单独加入项目 | `CreateCaseInsideProjectAsync` + 真实测试项目保存/引用修订 | **已改**：普通新建统一在唯一有序项目内创建并打开，不再制造孤立用例 |
| A 工程树/资源 | `ProjectExplorerView` → `MainWindowViewModel.OpenSelectedResourceAsync` | 单击直接打开；搜索只高亮不摧毁树；DBC 以工具窗口打开 | `ProjectWorkspaceService` AssetEntry | 已有路径；修正误导性的“新建测试项目”入口；文件联动/保存仍待 WPF 实测 |
| B TEST 编辑 | `TestEditorView` → 步骤列表 → 选择器/参数编辑器 | 选中插入点 → 添加 → 搜索或分类 → 选类型即列表插入 → 编辑；双击实际步骤行进入属性 | `StepEditTransaction.Insert`、`TestStepInspector`，单 TEST DSL | **已改**：双击定位、双列选择器、显式应用/取消而非点击遮罩提交；复杂编辑滚动和快捷批量操作尚待验证 |
| B TEST 调试 | `TestEditorViewModel` 的 Preflight、RunSelectedStep、RunCurrentCase | 运行单步需设备/可执行快照及相应真实授权；运行用例与临时工程范围区分 | `RealTestWorkbenchService`、真实设备状态、冻结资源 | 命令存在，未重写；仍须 Windows 实测和物理验证；现存准备/测试/清理后端相位与期望连续步骤模型存在差距 |
| C 手动台架 | 工具→手动台架 → `ManualBenchWindow` | 连接 USB；新建多个 CAN 行并在表中单发/启停；KEY 快捷按钮单击执行、双击编辑；看右侧 CAN RX | `ManualBenchViewModel`、真实 `HardwareConnectionService`、周期任务 | **已改**：窄屏重排发送/KEY/监视，并明确“停止全部发送”与单条停止；不修改真实发送、KEY/USB 管线；发送行横向空间仍可能不足，待不同 DPI 测量 |
| D DBC | 工程资源 DBC → `DbcWorkbenchView`；现在可直接选择独立 .dbc 文件 | 选报文→信号表连续编辑→位布局→保存；备注/枚举浮窗明确取消/应用 | `DbcWorkbenchViewModel` / DBC 修订事务 | **已改**：禁止点击编辑遮罩外隐式提交；独立文件会话和文件指纹校验已接入；信号别名行内编辑、常用列与高级列已完成源码修改 |
| E UDS | 工具→UDS 诊断序列与记录回放→独立 WPF 工具 | 可编辑、复制、排序、保存/打开实际 UDS JSON 服务序列，按文件交换顺序回放导入记录 | 现有 UdsToolDocumentStore/UdsReplayWorkspace，无物理发送 | **已实现离线可操作工具**；真机 CAN/ISO-TP 会话仍阻断，严禁以回放假充 ECU 回复 |
| E Flash | 工具→FlashLab；检测准备→原生 Flash Profile | 独立打开可不依赖工程；原 XML 导入仍可见，但不得作为正式检测配置必选项 | FlashLab 真实服务和 `NativeFlashProfileWindow` | 既有工具保留，需优化真实参数/阶段/证据，未修改底层烧录 |
| E Wave | 工具→`WaveWorkbenchWindow` | 工程内可实时观察；独立模式可选择已有 wave-records 目录并分析已归档采集/参考 | WaveEvidenceStore、主控 CAN/DBC 信号流 | **部分整改**：已收紧波形窗口硬性最小宽度以改善高 DPI，无工程独立归档查看、比较、参考保存已接入；无工程实时 CAN 采集仍阻断，不虚构数据 |
| F 自动测试 | 默认 `AutomaticTestHomeView`（扫码→工程匹配→准备检查） | 当前有“工程整组实测”显式软件工程验证，而不是最终生产 START；正式连续物理过程尚未满足门禁 | COM 扫码、ProductionPreparation、Flash、TEST 依赖 | **已改**：已重排纵向阶段+中央完整 TEST 树+右侧状态，窄屏状态下移；工程验证隐藏在调试区，正式 START/夹具证据/Run 闭环仍阻断 |
| G 检测记录 | 主窗口全局“记录与报告”→`RecordHistoryWindow` | 全局一次进入；默认当前工程 SQLite，也可添加最多 16 个已有记录库；输入 ECU 编号跨工程查询→选记录→打开/导出 HTML/JSON | **现有** `RecordHistoryToolSource`、`RunHistoryQuery`、`RunReportExporter` 和 artifact SHA 验证 | **新增真实操作**：只读查询/单台报告导出，显示中文结果/归档状态；跨已有记录库查询已实现；仍缺统一生产 Inspection 结案与统计 |

## 2. 源码级问题台账（操作路径/状态/布局/验收）

| ID | 页面及源码位置 | 用户任务与已发现问题 | 整改办法/现在状态 | Windows/实物验收条件 |
|---|---|---|---|---|
| UI-A01 | `Projects/NewProjectDialog.xaml`、VM、`ProjectWorkspaceService.cs` | 原新工程未显式收集 ECU 型号，后续扫码匹配还要返回补配置 | **已修改**：型号为工程身份，限定 2–16 位字母数字，存于现有 `EcuModelCode`，不设额外注册对象 | 新建后重新打开，型号严格保留并用于工程身份，错误输入不落盘 |
| UI-A02 | `Shell/MainWindowViewModel.cs:CreateTestProgramAsync` | 顶部创建孤立 TEST 与项目内创建不一致；容易正式检测漏跑 | **已修改**：统一走项目内创建与引用保存 | 新用例立即进入有序项目树，重启仍在；无重复项目 |
| UI-A03 | `Shell/Navigation/ProjectExplorerView.xaml`、`MainWindow.xaml` | 入口重复，“新建独立用例”“新建项目”暴露实现概念 | **已修改**：同一真实命令统一产品文案、直接打开唯一测试项目 | 菜单/树同类入口目标相同、无无效按钮 |
| UI-A04 | `MainWindow.xaml.cs` | 自动保存、退出/工具关闭仍需真实窗口回归 | **已有实现待验证**：空闲计时保存与异步退出；未绕过设备收尾 | 合法改动自动保存，不反复弹保存；异常退出仍保留草稿和证据 |
| UI-B01 | `Testing/TestEditorView.xaml.cs:StepsList_DoubleClick` | 双击行可能对旧选择发起属性编辑 | **已修改**：按点击位置定位 Row、跳过输入控件和操作按钮 | 任意行双击打开该行、双击不误操作硬件 |
| UI-B02 | `Testing/TestEditorView.xaml` | 步骤选择器信息过密，三列难读 | **已修改**：双列密度；已有分类/搜索/键盘选择 | 多类步骤搜索、插入位置与撤销正确 |
| UI-B03 | `Testing/TestEditorView.xaml(.cs)` | 点击参数窗口外隐式应用、可能丢失草稿 | **已修改**：遮罩点击不提交，显式应用并关闭/取消 | 无意点击不改变 TEST；字段错误仍保留草稿 |
| UI-B04 | `Testing/TestEditorViewModel.cs` | 单步/整用例真实命令与预检是否一致 | 已有代码静态确认，**待 Windows/硬件验证** | 权限/快照/真实 StepOccurrence 一致，真实运行失败不被 UI 隐藏 |
| UI-B05 | TEST 作者模型与资源依赖 | 完整连续用例、公共流程、资源版本必须同一执行器 | **后续专项阻断**，不得靠第二 DSL 掩盖 | 正式执行读取与当前可保存编辑内容完全相同，旧资源变更有诊断 |
| UI-C01 | `ManualBenchWindow.xaml(.cs)` | 三硬性最小列宽在 1366×768/150% DPI 超出工作区 | **已修改**：窄窗口两列及底部 CAN 监视，宽屏三列；降低窗口最小尺寸 | 1366/150% 无窗口被任务栏遮挡；关键 CAN/KEY 控件仍可达 |
| UI-C02 | `ManualBenchKeyActions.cs`、`ManualBenchCanRows.cs` | KEY 单击执行/双击编辑，CAN 批量启停与后台周期清理 | 原真实命令保留，**待真机** | KEY 不因双击误发送；保持时间结束后状态继续保持；周期发送可查可停，退出不遗留任务 |
| UI-C03 | `ManualBenchWindow.xaml` | CAN 表主数据仍随窄屏发生横向滚动 | **未完成**：要研究紧凑行/操作列的真正布局方案，而非缩字 | 8 字节原始 DATA、CAN ID、DLC、周期状态尽量无横滚 |
| UI-D01 | `Dbc/DbcWorkbenchView.xaml(.cs)` | 参数浮窗外点击触发隐式保存 | **已修改**：显式取消/应用 | 中文别名/枚举改动只在确认后写入；放弃不改资产 |
| UI-D02 | `Dbc/DbcWorkbenchViewModel.cs` | 同一信号的大量参数水平滚动、报文/信号备注需弹窗 | **待操作审查**：当前表格已支持部分连续编辑 | 多个信号快速编辑/非法字段提示/保存后引用提示 |
| UI-D03 | `MainWindowViewModel.OpenDbcToolAsync` | 不打开工程无法独立使用 DBC | **已修改**：独立 DBC 原文件编辑/哈希冲突检查、工程 DBC 保持受控修订；切换工程保留独立窗口 | 无工程启动、打开 .dbc、编辑/保存本地文件，明确纳入工程入口 |
| UI-E01 | `FlashLabWindow.xaml` | XML、JSON、原生 Profile 多入口并存，正式配置概念混乱 | **待工具整改**：保留诊断兼容但工程正式流程只用原生 Profile | 工程保存后不需在每台检测前选 XML；真实 Flash 阶段/ACK/响应正确 |
| UI-E02 | `WaveWorkbenchWindow.xaml`、`OpenWaveWorkbenchAsync` | 目前必须先有工程，窗口多种宽度存在高 DPI 风险 | **已修改**：独立归档读取真实 wave-records 且可比较/保存参考；实时采集仍要求正式工程 DBC | 无工程观察/采集；有工程时正确明确来源；多信号同步时间轴 |
| UI-E03 | UDS 专业工具 | UI 当前缺独立 UDS 可用工作台入口 | **已改**：独立 UDS 服务序列编辑、保存/自动保存、交换记录离线回放；物理 UDS 通讯未接入 | 新建/保存连续 UDS 序列，真实请求/响应/NRC/取消及日志 |
| UI-F01 | `AutomaticTestHomeView.xaml` 与 Run VM | 软件工程实测可能被误解为生产放行 | **已修改**：按钮文案和提示明确工程验证，正式 START 未冒充 | 不能因工程验证通过而更新正式良率/本台生产 PASS |
| UI-F02 | R1 product-integration 合同 | 实体 START、夹具实际到位证据、前置 Flash+TEST+RECORD 端到端不存在已验收闭环 | **P0 执行链阻断**；不得在本 UI 专项改写服务 | 真实扫码/机械/启动/烧录/全量 TEST/单台记录按因果一条链运行 |
| UI-F03 | 生产窗口 | 流程纵向、中央完整 TEST 只读监视、上台结果保留需现场审查 | **已重排 UI 源码；待 Windows 验证**：纵向阶段＋中央用例步骤＋结果侧栏；窗口窄时结果下移 | 当前用例和步骤始终可辨，停止/失败停留，扫码忙时拒绝 |
| UI-G01 | 新增 `Shell/RecordHistoryWindow.xaml(.cs)` | 原来没有公共查记录/报表入口，需找内部文件和 ID | **已新增接现有 RECORD**：全局按钮、ECU 编号过滤、模式、记录表、报告打开/导出 | 同一编号多次运行按时间显示，HTML/JSON 可核对真实事实 |
| UI-G02 | 同上、`AutoTest.Records` | RECORD 仍按当前工程存 SQLite，尚非跨工程完整工厂历史索引 | **未完成**，目前可显式切换已有 SQLite 文件 | 不依赖内部 RunId/GUID 找到全工厂实体所有 attempts |
| UI-G03 | 工程验证 Receipt、生产记录 | 现有部分工程整组验证并非统一正式 RECORD Run/Inspection | **P0 后端/执行链阻断**，禁止生成虚假汇总或报告 | 每台完整烧录/全部 TEST/异常停止均有不可覆盖的单台归档 |

## 3. 统一交互合同：落实与未完成

1. **项目语义**：一个型号工程 + 唯一有序项目，普通创建用例即加入，不做第二测试方案；已修改 A01/A02。
2. **复杂编辑**：TEST/DBC 参数窗口外点击不隐式应用，手动台架早已保持显式；已修改 B03/D01。
3. **鼠标双击**：TEST 必须按实际点击行，手动台架 KEY 按键双击要先取消潜在实体动作；前者已改，后者现有代码待硬件验证。
4. **搜索**：项目树保留层级只高亮、TEST 分类搜索、KEY 按动作名即时搜索；已有，需现场键盘/重置测试。
5. **状态真值**：Intent/USB ACK/ECU 反馈/TEST verdict/生产 Inspection 各自独立；UI 不推断电气到位或 PASS。
6. **自动保存**：合法文件变更应自动保存；非法草稿不允许进入运行；退出阻断必须由真实持久化/设备错误触发。
7. **专业工具**：独立窗口保持自身任务语义；手动台架和 Flash 可脱离工程；DBC 独立文件编辑、Wave 独立归档分析、UDS 独立序列编辑与回放已接线。Wave 实时 CAN 和 UDS 实体请求仍需专门真实设备会话合同。不得把无工程模式简单换成临时伪工程。
8. **视觉**：Fluent 浅色、深文字，令牌优先；本批新增窗口使用白底、浅灰表格、清楚的操作层次。现存局部 XAML 颜色硬编码尚未完全清理，不能声称全局设计系统已经统一。

## 4. 交付边界和禁止误报

- 新记录窗口只读 **真实 SQLite**。用户可以选择已有记录库，不创建假历史。报告来自现有 `RunReportExporter`，保留 artifact 验证语义。它**不等于**正式生产 Inspection 汇总已经接线。
- `R7.1` 冻结扫码枪为 **USB 虚拟串口 COM**；不凭原 R7 的 HID/POS 提案造配置项。
- `R7.2` 纠正生产工作区为 **纵向工位流程 + 中央完整 TEST 用例及步骤 + 右侧 ECU/烧录/状态**。浏览器模型不是现场数据。
- 当前主控物理证据是 Classic CAN 0–8 字节，不能因某些界面支持输入 64 字节就把 CAN FD 列为真实硬件能力。
- KEY_SIM **20 Key = 五路电阻网络输出**；保持时间结束不释放当前组合；纯等待不发送，显式下一操作才切换；停止不能将 USB ACK 当输出波形实测。
- TEST 与生产均必须拒绝假工程、缺失版本依赖、暗中替换资产、模拟 PASS；正式生产不能按开发临时勾选删减执行范围。

## 5. Windows Codex 本地验证步骤（本轮未执行）

前置：`git checkout R1 && git pull`，确认 HEAD；Codex 可以在 **用户本地 Windows** 执行构建/界面操作，提交反馈。本轮对话侧**未主动**运行 CI、Windows 构建、WPF 实际操作或实机测试。

| 顺序 | 操作 | 通过条件 | 验证层 |
|---|---|---|---|
| W01 | Windows 生成 HOST/ 下 WPF Solution，记录所有 XAML/C# 错误；不自动改设备核心 | 编译成功、实际窗口能启动，若失败记录具体错误文件行号 | 构建 |
| W02 | 无工程启动，先打开“记录与报告” | 不闪退、无虚构数据、可选择已有 SQLite 文件 | WPF |
| W03 | 新建 ECU 工程，填 `HYDEPBA01`，另试非法码，重新打开 | 合法码保存为 EcuModelCode；非法阻断；已有项目+用例可编辑 | WPF/PROJECT |
| W04 | 在菜单/树新建“驻车测试”，重启工程 | 用例已进入有序测试项目且打开编辑器；无独立孤立资源 | WPF/PROJECT |
| W05 | 以 IF/CAN/KEY/WAIT 逐条新增、取消、撤销、复制、双击第二行 | 新步骤先出现在正确位置；双击命中行；外围点击不会提交 | WPF |
| W06 | DBC 信号连续编辑，浮窗取消与确认、非法字节布局尝试 | 取消无写入，应用合法保存，非法阻断并说明 | WPF/DBC |
| W07 | 在 1920×1080 100%、125%、150% 和 1366×768 100%、125%、150% 改变手动台架及波形窗口大小 | CAN 发送与 KEY 状态可访问；窄宽度 CAN RX 在下方；不盖住任务栏；记实际横滚 | WPF/DPI |
| W08 | 用历史已归档真实 RECORD SQLite 按 DUT 查多次运行，打开/导出 HTML/JSON | 显示本地时间/中文状态；每次原始 Run 独立；不覆盖原始证据 | WPF/RECORD |
| W09 | 在无工程条件分别点手动台架、Flash、DBC、Wave、UDS | DBC 可打开并保存独立文件、Wave 可打开并比较归档、UDS 可编辑序列和回放记录；**不得**判定 Wave 独立实时采集/UDS 实体发送已经具备 | WPF |
| W10 | 对物理主控与 ECU 执行独立 CAN、KEY、IGN/夹具、Wave、Flash 和 TEST（需现场授权） | 硬件 ACK、ECU 观测、STOP、安全收尾、记录各有真实证据 | 实体 |
| W11 | 正式生产扫码/夹具/实体 START/每次 Flash/全 TEST/完整记录 | **现阶段预期 BLOCKED**，不得替换为工程整组实测假装通过 | 实体+生产准出 |

## 6. 已知仍需专项解决的阻断

- **P0/F**：生产实体 START 与夹具确认、统一 Flash→TEST→RECORD 原生执行主链未完成。核心服务归执行链专业责任，UI 不强改。
- **P0/G**：工程整组验证 receipt 与正式不可覆盖的单台生产 Inspection 并非已统一的记录闭环。
- **P1/E**：独立 DBC/波形归档/UDS 序列入口已有源码；**Wave 无工程实时 CAN 解码绑定、UDS 真实 ECU 请求**仍缺设备会话/授权接线，不用离线回放冒充实体执行。
- **P1/C/E**：生产、手动台架、波形已有窄屏调整，但 1366×768/125%/150% 仍需 Windows 真正量测；CAN 数据、FlashLab 和 UDS 响应布局仍可能横向滚动。
- **P1/D/G**：DBC 常用参数与中文别名已行内编辑，记录支持人工加入多个真实工程库联合查询；仍缺全工厂统一历史索引、自动发现、完整单台证据钻取与正式统计。
- **P1/B**：当前测试文档的真实相位模型与后续“连续步骤树”业务决定仍有差距，不能单靠 XAML 伪造语义迁移。
- **P1/资源**：HydEPB 原八用例的历史迁移包含仍未裁决的步骤，不可作为实测通过工程。

## 7. Gate

本轮状态 **SOURCE_CHANGED / STATIC_SPOT_REVIEW_ONLY / WINDOWS_NOT_RUN / ECU_NOT_RUN**。R1 已有实际 WPF/交互代码改动，主交互入口、型号标识、历史记录、弹窗事务和台架高 DPI 布局有具体整改；但**不宣称四批全部结束，不宣称 R1 已达到正式生产交付**。下一准入应由 Windows Codex 逐项按表核对，优先修正真实编译/操作错误，再推动剩余专业工具与真实执行链。

## 8. 第二轮（2026-11-24）继续整改及下一轮回归合同

**状态：代码实际提交 R1，静态审查，未 Windows 编译/未 WPF 操作/未真机验证。** 本节更新本文件此前的第一轮审查，旧“阻断”以本节最新状态和上方已纠正的台账为准。

### 8.1 新增/修改模块

| 路径 | 真实源码 | 已改交互 | 仍需验证 |
|---|---|---|---|
| DBC | `Shell/MainWindowViewModel.cs`; `Dbc/DbcWorkbenchViewModel.cs`, `DbcWorkbenchView.xaml(.cs)` | 不开工程打开独立 DBC；工程 DBC 仍版本管理；中文别名/常用参数可连续行内编辑，更多字段可切换展示；受控自动保存与退出 | WPF 表格批量修改、撤销/重做、合法与非法 DBC 输入、外部改盘拒绝覆盖 |
| 波形 | `WaveWorkbenchWindow.xaml(.cs)`; `WaveWorkbenchViewModel.cs`, `.Capture.cs`, `.Workspace.cs` | 无工程打开工具，选择既有 wave-records，载入真实证据、同次多信号回放、参考保存/比较；从独立库显式切回工程资源；关闭父窗等待实际采集完成 | 参考文件完整性、切换源并重新绑定工程 DBC、采集停止及断线、与 TEST 互斥 |
| UDS | 新增 `Shell/UdsWorkbenchWindow.xaml(.cs)`; 复用 `UdsToolDocumentStore`、`UdsReplayWorkspace` | 新建/编辑/复制/移动/删除/保存/重载诊断序列；配置实际请求/响应 ID、P2；从真实外部回放文件按序核对响应/NRC/帧，不发 CAN | 文档验证/格式错误、外部文件变化、自动保存、关闭草稿提示、非法回放不改变位置；**实体 UDS 会话仍阻断** |
| 操作员 | `AutomaticTestHomeView.xaml(.cs)`; `AutomaticTestHomeViewModel.Run.cs` | 左侧工位纵向阶段、中间完整有序 TEST/步骤，右侧真实状态；高 DPI 重排；工程验证进次级区；Flash/Record 显示真实过程状态 | 1366/150% 实机界面、完整用例与步骤监视、失败信息持续显示、实体 START 和统一生产 Run 仍缺 |
| 记录 | `RecordHistoryWindow.xaml(.cs)` | 最多 16 个真实 SQLite 库，按 DUT ID 合并多次记录，来源显示、原库报告导出；只读模式先校验 RECORD 版本和表结构 | 合并排序、重复编号、多库部分损坏、证据归档和 HTML/JSON 核对；跨项目自动发现与正式统计仍缺 |
| 窗口生命周期 | `MainWindow.xaml.cs`, `MainWindowViewModel.cs`, `WaveWorkbenchWindow.xaml.cs`, `UdsWorkbenchWindow.xaml.cs` | 退出前等待 Wave 真实证据最终保存、UDS 草稿持久化；切换 ECU 工程不关闭独立 DBC；异常阻断而非悄悄清理 | USB 占用/操作中关闭、工程切换、程序退出、不遗留后台物理任务 |
| TEST KEY_SIM | `Testing/TestStepInspector.xaml(.cs)` | 新 20-Key 原始步骤仅常规 Hold/明确 Release/纯 Wait，旧 Pulse 仅已存在旧节点可见；保持/等待严格 1～600000ms | 真机最后 KEY 组合继续保持、纯等待无 CAN、异常 STOP 不暗中释放 |

### 8.2 Windows Codex 专项扩充验证

- **W12** 无工程、工具→独立 DBC，选择真正 .dbc，改三个信号的中文别名和起始位等常用列，逐行 Tab；确认高级列开关、Ctrl+Z/Y、重开文件内容、外部修改冲突。
- **W13** 无工程、工具→波形，选择已有 `wave-records`；同次多信号回放、保存参考、比较。再打开工程、切回工程波形，确认 signal choices 按工程重新加载；独立模式实时采集命令必须处于受控不可用状态。
- **W14** 无工程、工具→UDS，输入实用 ECU Tx/Rx ID、P2/P2*、两个合法请求，保存/重开，新增/复制/删除/排序；导入有真实来源的回放 JSON，按顺序核对 NRC 与帧。绝对不得推断实现了真实 ECU 请求发送。
- **W15** 1366×768 100/125/150% 与 1920×1080 100/125/150% 查看操作员：纵向六阶段、全部有序用例/步骤、右侧（窄窗下方）真实状态应可访问；不能有假 START、假 PASS、假进度。
- **W16** 用两个不同工程真实 SQLite 库，含同一 ECU 编号的多次记录、异常记录，联合查询后逐条导出并核对源库；输入非 RECORD SQLite，必须明确拒绝，不能对其执行建表迁移。
- **W17** 用实际正在保存的 Wave 捕获和存在未保存修改的独立 UDS/DBC，尝试切工程、关闭主窗口。检查等待、提示、被阻断时草稿存在、真实 CAN/KEY 周期发送退出后没有孤儿任务。
- **W18** 实际 DBC、UDS、Wave 功能之外仍需专业专项补齐真实 UDS 物理会话、无工程独立在线波形 DBC 绑定、原生 Flash 参数精简以及正式生产状态机/单台报告，此处标记 BLOCKED，禁止假通过。

**源代码审查与测试分层：** `SOURCE_COMMITTED` 不代表 `BUILD_PASS`；`BUILD_PASS` 不代表 `WPF_WORKFLOW_PASS`；`WPF_WORKFLOW_PASS` 不代表 `HARDWARE_PASS` 或 `PRODUCTION_RELEASE_PASS`。本对话明确未运行任何 CI、Windows 构建或实体测试。
