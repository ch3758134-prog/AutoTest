# AutoTest｜WPF UI 4.3.0 接入与手动台架、自动测试双线验收设计（2026-11-19）

> 状态：技术选型 USER_DIRECTED；WPF-UI_DEPENDENCY_COMMITTED；Windows BUILD_NOT_RUN；BENCH_FIELD_NOT_RUN；TEST_EDITOR_UI_ACCEPTANCE_NOT_RUN。
> 分支：ui/ide-design；主线 G4 merge 仍待 PR #61 冲突处理。本文是 UI 实施研究/验收合同候选，未修改 TEST 执行器、DEVICE 安全门禁。

## 一、用户最新要求
- 直接采用现代 Windows WPF UI，明亮的 Fluent Light 主题，不继续比较多个 UI 框架。
- 自动测试和手动台架优先于 UDS、DBC、波形的 UI 全量重做。
- 自动测试可以先不接执行器，但必须使用真实工程/DBC/TEST/公共流程模型，具备完整、可保存、可恢复、可撤销的生产级编辑链。
- 手动台架不能只做离线 UI；必须接真实硬件能力供现场实测，其编辑、执行、监控和停止均要能独立验证。
- UDS 改为后续组件迁移实验的参照，不作为阻塞手动台架/TEST 的前置。

## 二、技术选型与已提交最小改动
- 官方库：lepoco/wpfui WPF UI，MIT；官方 NuGet 当前稳定 4.3.0，支持 .NET 10。
- 官方：https://www.nuget.org/packages/WPF-UI/ ；https://wpfui.lepo.co/documentation/getting-started.html
- 版本集中：Directory.Packages.props 添加 PackageVersion WPF-UI=4.3.0；Desktop csproj PackageReference；App.xaml 加 `ui:ThemesDictionary Theme=Light` 和 `ui:ControlsDictionary`，已有 AutoTest Tokens/Controls 继续覆盖必要专业样式。
- 本 UI 分支导入源码提交 6a0c54d、1893b63、0e49dfd。尚未 Windows 编译、启动、兼容性、DPI 或实机检查。
- Prism 管理 Shell/Region/DocumentContext/导航，WPF UI 管控件和视觉；不并行启用 .NET 原生 Fluent ThemeMode 和 WPF UI 全局主题，不把 WPF UI NavigationView 的另一套导航服务并行接入。
- 先保留常规 Window 及原生最大化行为，逐步验证 `FluentWindow` 与 Windows 任务栏、系统按钮、跨 DPI、浮窗/Owner 的行为，不直接替换主 Shell。

## 三、界面统一规则
- 编辑模式与实体运行模式分离；离线编辑无需连接 USB，运行必须经过真实设备所有权和物理授权。
- 高频字段就地编辑（Enter 确认/Esc 取消/F2 重命名）；多个有关联字段用锚定 Flyout；复杂对象在常驻 Inspector 编辑；右键提供当前对象适用命令；不强迫每个字段开模态窗口。
- 三层数据状态：临时 FieldDraft、正式领域事务 DocumentDraft、磁盘 PersistedResource，统一未保存提示、局部校验、Undo/Redo、保存/冲突/恢复。不存在无效值时也不能丢焦点/数据。
- Fluent Light 保持紧凑 IDE 信息密度，以排版、分隔线和主次状态代替大片彩色卡片；保持鼠标、键盘、右键、窗口缩放、DPI 和布局记忆。
- 自有专业控件：CAN 报文/信号格子、20 Key 组合+时间、TEST 层级步骤树和落点、实时 CAN Trace、状态事实标签；不指望 WPF UI 通用控件自动提供这些业务规则。

## 四、双线阶段实施（建议分工，可与 G4 集成同步研究）
### F0 G4 集成准备
- PM/INTEGRATION 处理 PR #61 `mergeable_state=dirty`，保护 main 新增管理与设备约束；确定正式接收 G4 的 SHA。UI 分支不直接覆盖 main。
### F1 WPF UI 最小技术验证
- 本地 Windows Codex restore/build/start，先检查 Prism 启动、ResourceDictionary 顺序、主题、字体、中文、输入焦点、Popup/Window owner 和 Windows 任务栏；失败只处理前端兼容，不动领域核心。
### F2 手动台架——实际硬件最小闭环
- 使用 G4 中 `ManualBenchWindow`/`ManualBenchViewModel`/`HardwareConnectionService` 真服务。先贯通连接→CAN 真实 RX 监控→单条原始帧发送/USB ACK→DUT 可观察响应、异常/断连/停止，再做 DBC 编辑和多条单次发送。
- 然后验真实 KEY 动作的任意段 20-bit 掩码/保持 ms/保存重开/实体执行/ACK/最终状态说明；再验 IGN 与两路夹具（主控 GPIO 状态不是外部电压/机械到位）；LED 六路根据固件能力独立验。
- 保持用户已批准的布局：左 CAN 发送、中工位 KEY/IGN/夹具、右 CAN 监控；选中报文可以行内改字段、快捷 Flyout 或完整 Inspector，启动/停止操作不被编辑弹窗覆盖。
- **现场已知限制**：G4 `ManualBenchCanRows.PrepareCanRows` 明确阻断 ECU CAN 周期发送（主控 0x50 调度在 KEY CAN），现在可以真实测试单发和多条单次批量，但不可宣称 ECU CAN 周期物理发送可用；要单独核对 DEVICE/固件合同，不能绕过 Fence。
- 所有实体控制使用固定 BusRole/现有命令授权；CAN/KEY/IGN/夹具/LED 的请求、USB ACK、GPIO 回读、CAN 响应、电压/机械结果分层展示；取消/断连实体状态未知不假定恢复。
### F3 自动测试——完整离线真实编辑验收
- 保持唯一 PROJECT/TEST DSL、`TestDocument`、`StepNode`、`StepId`、`StepEditTransaction`、`TestProjectDocument`、`PublicFlowDocument` 和正式 ProjectWorkspace 存取。
- UI 实现新建/打开工程、项目/用例/公共流程、准备/测试/清理、编辑层级、插入前/后/槽、多选、复制、撤销/重做、字段编辑、DBC 报文/信号/物理值引用、独立 KEY mask+hold、UDS/波形资源的已支持绑定。
- 测试项目的临时勾选要与默认启用状态分离；运行/断点/变量/结果窗口可做真实状态空间，但未接 RunCoordinator 时不得生成伪 PASS、伪过程日志或让运行按钮表示真实可用；可以提示未接执行器。
- 真实工程导入、编辑、验证引用、存盘、关闭重开、文件外部变化冲突、坏资源可恢复、长用例不卡顿必须作为设计验收门槛。
### F4 组合与扩展
- 手动台架真实可操作、TEST 编辑真实可存盘后，验证两者共用工程数据、资源修订和用户术语，不需要两套 CAN 编码、KEY 定义。
- 后续在 UDS 上验证服务参数化编辑/保存/反复发送/历史回填，波形/DBC 按真实专业任务再对接；仅批准后安排 TEST RunCoordinator 的真机自动运行接线。

## 五、验收证据
- F1：Windows 构建启动记录、1366×768/1920×1080、100/125/150% DPI、任务栏不遮挡、焦点/右键/Flyout 截图；没有 Windows 结果不得标为通过。
- F2：连接主控身份与代次、CAN1 DUT/KEY CAN 固定业务角色、实际 TX/RX、单发与多条批发、真实 KEY 动作步骤/持久保存、IGN/夹具读取及操作、断连/停止/UNKNOWN、操作日志导出和外部 DUT/电气观察；每项单独记 FIELD_*。
- F3：真实 PROJECT 文件离线编辑 A/B，准备/测试/清理复杂控制流、DBC 值、KEY 掩码、公共流程、跨用例引用、保存重开、撤销、多选、100 步切换、无效字段切换不丢稿、资源冲突复核；无运行器执行，不宣称 AUTO_RUN_FIELD_PASS。
- 当前 G4 合并冲突、周期发送后端限制、公共流程运行能力不完善，均作为显式 BLOCKER 而非 UI 假成功。

## 六、职责
- UI 专项：现代视觉与交互组件、设计原型、用户任务验收、整合方案。
- PM：G4 进入主线的审批与安全/职责审查、任务状态分流。
- DEVICE/固件：物理能力/USB/硬件行为合同、实测授权及设备错误证据。
- Codex：严格按已经批准的任务执行 Windows 拉取、编译、启动、采集失败日志；不自主改动实体行为。
- 用户：真实操作 UI 和现场 ECU 行为验收。

本合同是新的研究建议及已选技术事实的记录，后续按用户反馈和 PM 审查迭代；不得仅凭本文宣布各模块完成。