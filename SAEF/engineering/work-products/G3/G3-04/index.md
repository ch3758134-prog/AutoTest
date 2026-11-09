# G3-04｜状态机、执行、UI交互（全量候审 v0.2）

> SAEF G3 正式候审内容；**G4尚未实施、所有测试初始 NOT\_RUN**。

## 上游批准约束

G2 v0.7 的系统边界/12 项 ICD/状态所有权及 P0～G1 已批准内容必须保留。详见 `README_FIRST.md` 和 `docs/COMMON_DESIGN_CONTRACT.md`。

## 本工作产品适用的详细设计模块

### C1｜测试程序编辑、节点目录、流程控制

**工程：** `AutoTest.Testing`；**G2：** G2-03/04/05/06；ICD-07

编辑文档是独立资源，节点有稳定StepId、类型、参数、子槽位、注释和引用；编辑以事务处理支持 Undo/Redo；步骤类型及属性由同一 Descriptor 供编辑器与编译器读取。避免 UI 各自定义运行语义。

**主要调用链**：用户新增资源（工程域）与添加步骤（测试域）分别走不同命令；新建测试程序→追加/编辑→事务检查→保存资源→准备运行时从保存版本编译。

**状态/异常**：Clean→Dirty→Validating→Saved/Error；SelectedStep只由UI拥有；正在运行Step属于Runtime。 非法节点/循环、坏SignalRef、被删除资源都指向StepId+属性；复制节点生成新ID；编辑不会偷偷改运行中的计划。

**设计文件 8 项：**

- `src/AutoTest.Testing/Authoring/TestDocument.cs` → `TestDocument,StepNode`；方法 `Create/CloneSnapshot`；可编辑树，稳定StepId。
- `src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` → `StepDescriptorCatalog`；方法 `Register/Lookup/Enumerate`；类型/属性/资源要求/槽位元数据。
- `src/AutoTest.Testing/Authoring/StepEditTransaction.cs` → `StepEditTransaction`；方法 `Apply/Undo/Redo`；插入移动复制删除的原子操作。
- `src/AutoTest.Testing/Authoring/StepTreeValidator.cs` → `StepTreeValidator`；方法 `ValidateTree/ValidateSlots`；禁止循环引用与非法Else位置。
- `src/AutoTest.Testing/Authoring/TestDocumentCodec.cs` → `TestDocumentCodec`；方法 `Serialize/Deserialize/Migrate`；持久化与版本诊断。
- `src/AutoTest.Testing/Authoring/StepPropertyValidator.cs` → `StepPropertyValidator`；方法 `ValidateBinding/ValidateParameter`；属性面板编辑前验证。
- `src/AutoTest.Testing/Authoring/StepCatalogRegistration.cs` → `StepCatalogRegistration`；方法 `RegisterBuiltins`；全部工作包共用步骤注册入口。
- `src/AutoTest.Testing/Authoring/StepRefactorService.cs` → `StepRefactorService`；方法 `RenameAsset/RemapReference`；工程资源变化传播检查。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Testing.Authoring/AuthoringTransactionEngine.cs`
- `HistoricalHOST:src/AutoTest.Testing.Authoring/UndoRedoHistory.cs`
- `HistoricalHOST:src/AutoTest.Testing.Authoring/NodeTypeRegistry.cs`
- `HistoricalHOST:src/AutoTest.Testing.Domain/AuthoringModel.cs`

**仍需通过实验关闭：** 流程控制（If/Else/Loop/Parallel）必须有统一语义，但首批WPF编辑交互形式通过真实用户试用确定；Parallel先以资源独占校验限制安全。

### C2｜测试计划、执行、步骤判定和运行事实

**工程：** `AutoTest.Testing`；**G2：** G2-03/04/05/07；ICD-07/08/10

运行前根据 TestDocument+项目+Station 能力冻结 PreparedPlan；启动生成独立 RunSnapshot；步骤 Handler 使用专业能力接口，不能直接读可变ViewModel。结果区分 PASS/FAIL/INCONCLUSIVE/ERROR/SKIPPED，记录实际观测与来源。

**主要调用链**：Document→PlanCompiler→PreparedPlan→RunSnapshot→Handle+Lease→Command/Observation→Judge→RunFact→Records→UI投影。If/Else和Loop在计划结构化节点求值；Parallel不允许未仲裁的资源竞争。

**状态/异常**：Idle→Preparing→Ready→Running→PausePending/Paused/StopPending→Finalizing→Finished/Failed/Interrupted；Step状态 Pending/Running/Passed/Failed/Inconclusive/Skipped/Error。 未得物理证据返回Unknown或Inconclusive；USB断连取消后续副作用；正在擦写不能假定可暂停；RunFact落盘失败仍保留未归档告警。

**设计文件 10 项：**

- `src/AutoTest.Testing/Preparation/PlanCompiler.cs` → `PlanCompiler`；方法 `PrepareAsync/ValidateDependencies`；冻结步骤图、绑定与诊断。
- `src/AutoTest.Testing/Preparation/PreparedPlan.cs` → `PreparedPlan`；方法 `ValidateFingerprint`；只读执行快照。
- `src/AutoTest.Testing/Runtime/RunCoordinator.cs` → `RunCoordinator`；方法 `StartAsync/RequestStop/PauseAtBoundary`；运行唯一状态拥有者。
- `src/AutoTest.Testing/Runtime/StepDispatcher.cs` → `StepDispatcher`；方法 `DispatchAsync`；按StepDescriptor查找Handler。
- `src/AutoTest.Testing/Runtime/StepHandlerRegistry.cs` → `StepHandlerRegistry`；方法 `Resolve/Register`；可替换Handler与版本合同。
- `src/AutoTest.Testing/Runtime/FlowExecutors.cs` → `IfExecutor,LoopExecutor,ParallelExecutor`；方法 `ExecuteAsync`；明确条件求值及有界循环。
- `src/AutoTest.Testing/Runtime/ResourceLeaseScope.cs` → `ResourceLeaseScope`；方法 `AcquireAsync/DisposeAsync`；操作结束释放资源租约。
- `src/AutoTest.Testing/Runtime/StepJudge.cs` → `StepJudge`；方法 `Evaluate/NoEvidence`；可追踪预期/实际与质量。
- `src/AutoTest.Testing/Runtime/RunFactJournal.cs` → `RunFactJournal`；方法 `Append/Publish`；运行事实追加且带OperationId。
- `src/AutoTest.Testing/Runtime/RunControlPolicy.cs` → `RunControlPolicy`；方法 `CanPause/CanSkip/CanStep`；不可中断副作用的安全边界。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Testing.Runtime/Preparation/L5RunPreparation.cs`
- `HistoricalHOST:src/AutoTest.Testing.Runtime/Execution/RunExecutionKernel.cs`
- `HistoricalHOST:src/AutoTest.Testing.Runtime/Execution/RunControlCoordinator.cs`
- `HistoricalHOST:src/AutoTest.Testing.Runtime/Execution/RunCapabilityScope.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Runtime/QuickSignalCheckHandler.cs`

**仍需通过实验关闭：** 首版步骤支持优先以真实使用反馈确定；具体判定阈值不在G3写死。

### D1｜WPF 视觉系统、主题与组件画廊

**工程：** `AutoTest.Desktop`；**G2：** G2-03/05；ICD-11

视觉不仅是配色：统一 Color/Type/Spacing/Density/Icon/States/Contrast Token；资源字典提供控件模板，不在业务页面硬编码视觉值；组件画廊要能独立运行并接受用户截图反馈。

**主要调用链**：Token→ResourceDictionary→ControlTemplate→业务View；主题切换只替换资源层，不修改设备状态与业务VM。

**状态/异常**：Theme=Light/Dark；Density=Compact/Normal；控件状态 Normal/Hover/Focused/Selected/Disabled/Invalid/Running/Passed/Failed/Unknown。 颜色不可作为唯一状态依据；缩放125/150/200%、长中文属性、英文标识与窄窗口不允许内容被剪掉；图标资源加载失败要有文字替代。

**设计文件 10 项：**

- `src/AutoTest.Desktop/Themes/Tokens/Colors.xaml` → `ColorTokens`；方法 `Primary/Surface/Status`；基础色与状态色分离。
- `src/AutoTest.Desktop/Themes/Tokens/Typography.xaml` → `TypographyTokens`；方法 `FontSize/Weight`；导航/内容/属性/表格字体体系。
- `src/AutoTest.Desktop/Themes/Tokens/Metrics.xaml` → `MetricTokens`；方法 `Compact/Normal`；高密度UI尺寸。
- `src/AutoTest.Desktop/Themes/Light.xaml` → `LightTheme`；方法 `MergedDictionaries`；浅色主题资源映射。
- `src/AutoTest.Desktop/Themes/Dark.xaml` → `DarkTheme`；方法 `MergedDictionaries`；深色主题资源映射。
- `src/AutoTest.Desktop/Themes/Controls/TreeStyles.xaml` → `TreeStyles`；方法 `Selection/Hover/RunState`；工程树/步骤树视觉状态。
- `src/AutoTest.Desktop/Themes/Controls/Inputs.xaml` → `InputStyles`；方法 `Validation/Disabled`；字段编辑视觉及错误反馈。
- `src/AutoTest.Desktop/Themes/Controls/Tables.xaml` → `TableStyles`；方法 `RowVirtualization`；CAN日志/结果/统计高密度表。
- `src/AutoTest.Desktop/Themes/Controls/RunBadges.xaml` → `RunBadges`；方法 `Passed/Failed/Unknown`；状态图标和文字配合。
- `src/AutoTest.Desktop/DesignGallery/GalleryWindow.xaml` → `GalleryWindow`；方法 `PreviewTheme/PreviewDensity`；组件/文字/状态可视化验收。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickDesktop/Themes/Palette.xaml`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Themes/TreeTable.xaml`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Themes/Inspector.xaml`

**仍需通过实验关闭：** 初版视觉方向由用户认可，但尺寸/信息密度/操作反馈需真实WPF运行后调整；静态HTML不能替代WPF验收。

### D2｜Prism 工作台、编辑器及专业工具窗口

**工程：** `AutoTest.Desktop / AutoTest.Application`；**G2：** G2-03/04/05；ICD-11

统一 HOST：工程树+中央文档+右侧属性+底部日志/运行结果；研发与生产使用同一核心服务，工作空间是布局与权限投影，不是两套功能实现；专业CAN/UDS/烧录/波形采用独立可操作工具窗口或文档视图。

**主要调用链**：按键/菜单→VM命令→Application→领域服务→只读投影回VM；UI操作“+新建资源”与“+添加步骤”分离；选中步骤只触发属性展示，运行状态独立更新。

**状态/异常**：Selection(Asset/Step/RunFact)；Document(Dirty/Saved/Conflict)；Window(Open/Hidden/Detached)；RunView按事实展示非自造状态。 设备断连禁用危险按钮并显示原因；无法编辑不做假控件；保存失败保留Dirty；界面被关闭不取消不可中断的操作。

**设计文件 14 项：**

- `src/AutoTest.Desktop/App.xaml` → `App`；方法 `Startup/Resources`；唯一WPF启动和Theme字典。
- `src/AutoTest.Desktop/Bootstrap/PrismRegistration.cs` → `PrismRegistration`；方法 `RegisterTypes/CreateShell`；Prism 9导航、容器组合。
- `src/AutoTest.Desktop/Shell/MainWindow.xaml` → `MainWindow`；方法 `Document/Left/Right/BottomRegions`；IDE工作台。
- `src/AutoTest.Desktop/Shell/MainWindowViewModel.cs` → `MainWindowViewModel`；方法 `OpenCommand/RunCommand`；只发送应用命令。
- `src/AutoTest.Desktop/Shell/WorkbenchLayoutService.cs` → `WorkbenchLayoutService`；方法 `SaveLayout/RestoreLayout`；窗口与Dock状态保存。
- `src/AutoTest.Desktop/Shell/SelectionContext.cs` → `SelectionContext`；方法 `Select/GetSelection`；选中项与当前运行步骤分离。
- `src/AutoTest.Desktop/Project/ProjectExplorerView.xaml` → `ProjectExplorerView`；方法 `AddResource/Rename/Delete`；工程导航和操作。
- `src/AutoTest.Desktop/Testing/TestEditorView.xaml` → `TestEditorView`；方法 `SelectStep/InsertStep`；树/步骤双击及右键编辑。
- `src/AutoTest.Desktop/Testing/StepInspectorView.xaml` → `StepInspectorView`；方法 `EditProperty/Validate`；右侧属性编辑和即时诊断。
- `src/AutoTest.Desktop/Testing/RunMonitorView.xaml` → `RunMonitorView`；方法 `ObserveRun/ShowEvidence`；预期/实际/判定/步骤状态。
- `src/AutoTest.Desktop/Devices/BenchDiagnosticsView.xaml` → `BenchDiagnosticsView`；方法 `Connect/ShowRawFrames`；USB及台架复验入口。
- `src/AutoTest.Desktop/Tools/ToolWindowRegistry.cs` → `ToolWindowRegistry`；方法 `OpenCan/OpenUds/OpenFlash/OpenWave`；共享专业窗口生命周期。
- `src/AutoTest.Desktop/Production/ProductionWorkspaceView.xaml` → `ProductionWorkspaceView`；方法 `Capture/Preflight/Run`；生产工作区与结果。
- `src/AutoTest.Application/Commands/ApplicationCommandRouter.cs` → `ApplicationCommandRouter`；方法 `ExecuteAsync/GetCanExecuteReason`；跨模块命令与失败反馈。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickDesktop/Shell/WorkspaceModeController.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Editor/NodeEditors/CanSendNodeEditor.xaml.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/CanTrace/CanTracePaneView.xaml.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Uds/UdsDebugWindow.xaml.cs`

**仍需通过实验关闭：** 具体 Dock 库是否采用第三方必须在 G4 小型WPF原型对比许可证与可用性；不要求全部 UI 操作现在永久冻结。

### E2｜ISO-TP / UDS 专业诊断与测试步骤

**工程：** `AutoTest.Specialists`；**G2：** G2-03/05/07；ICD-08/12

共享 CAN FrameHub 和发送独占租约；ISO-TP 负责分段重组/流控/定时器，UDS负责请求响应关联、NRC、会话与服务权限。独立 UDS 窗口和测试步骤均使用同一诊断服务。旧HOST QuickUdsService 仅单帧请求/有限服务，不能称完整栈。

**主要调用链**：工具/步骤→UdsClient→ISO-TP→TxLease+CanHub→ECU→响应收集→关联SID/NRC→诊断事实；FlowControl 由 ISO-TP 层处理，不让UI逐帧驱动。

**状态/异常**：Session=Disconnected/Default/Extended/Programming(按ECU可用)；Conversation=Idle/Sending/Waiting/ResponsePending/Complete/TimedOut；诊断会话不是 USB 会话。 允许的服务按风险分级；只读服务先实机，0x27、0x31、0x34/36/37等可能写入服务须独立授权；NRC0x78有界等待；同一ECU与Flash不能无序并行。

**设计文件 9 项：**

- `src/AutoTest.Specialists/Uds/IsoTpFrameCodec.cs` → `IsoTpFrameCodec`；方法 `EncodeSingle/ParseFirst/ParseConsecutive/ParseFlow`；标准帧类型和长度检查。
- `src/AutoTest.Specialists/Uds/IsoTpConversation.cs` → `IsoTpConversation`；方法 `SendPduAsync/ReceivePduAsync`；FC/BS/STmin/序号与超时。
- `src/AutoTest.Specialists/Uds/UdsEndpointProfile.cs` → `UdsEndpointProfile`；方法 `ValidateIds/ValidateAddressing`；CAN TX/RX ID及地址模式配置。
- `src/AutoTest.Specialists/Uds/UdsClient.cs` → `UdsClient`；方法 `RequestAsync/InterpretResponse`；正响应/NRC/P2\*可配置。
- `src/AutoTest.Specialists/Uds/UdsServiceCatalog.cs` → `UdsServiceCatalog`；方法 `Register/GetRiskLevel`；服务参数与权限分级。
- `src/AutoTest.Specialists/Uds/UdsSessionService.cs` → `UdsSessionService`；方法 `StartSession/TesterPresentPolicy/EndSession`；会话生命周期、独占租约。
- `src/AutoTest.Specialists/Uds/UdsTrace.cs` → `UdsTrace`；方法 `AppendRequest/AppendResponse`；原始PDU/总线帧与错误可溯源。
- `src/AutoTest.Specialists/Uds/UdsStepHandler.cs` → `UdsStepHandler`；方法 `ExecuteAsync`；供测试步骤复用而非复制协议。
- `src/AutoTest.Desktop/Tools/UdsToolView.xaml` → `UdsToolView`；方法 `Send/ShowNrc/ShowSession`；独立原始请求响应窗口。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickDesktop/Uds/QuickUdsService.cs`
- `CanTool:Protocol/UdsTransport.cs`
- `CanTool:Protocol/SessionManager.cs`
- `CanTool:Protocol/IsoTpReassembler.cs`

**仍需通过实验关闭：** ECU实际服务/寻址/NRC/时序参数由真实对象测试决定。先支持 ISO-TP经典CAN normal addressing的标准子集，寻址扩展受控升级。

### E4｜烧录配置、镜像、ProgrammingJob 与仿真

**工程：** `AutoTest.Specialists`；**G2：** G2-03/05/06/07；ICD-09/10/12

CanTool只是领域参考：配置、HEX/S19、传输会话、Seed/Key、地址与段、擦除、下载、校验、复位等对象值得认识；不复制其某ECU专用顺序、算法和常数。AutoTest烧录服务必须可以被独立工具、测试步骤或生产协同调用，真实刷写另行授权。

**主要调用链**：固件文件+Profile+目标身份→Preflight→不可变JobPlan→TxLease+UdsSession→Workflow阶段→校验/重启→持久化JobResult；Stage顺序按ECU Profile决定，不能硬编码所有ECU流程。

**状态/异常**：Job=Draft→Preflight→Ready→Running(stage)→Verify/Complete/Failed/Interrupted/RecoveryRequired；CancelPending只能在具体安全边界执行。 不自动重试擦写、不隐式启用Seed/Key；任何超时后不得假定ECU未写入；断电时标记RecoveryRequired；缺恢复资料拒绝实机。

**设计文件 10 项：**

- `src/AutoTest.Specialists/Flash/FirmwareImageParser.cs` → `FirmwareImageParser`；方法 `ParseHex/ParseS19/ValidateSegments`；分段镜像、校验与重叠检测。
- `src/AutoTest.Specialists/Flash/ProgrammingProfile.cs` → `ProgrammingProfile`；方法 `Validate/DescribeTarget`；目标/请求ID/块大小/安全策略/超时。
- `src/AutoTest.Specialists/Flash/ProgrammingPreflight.cs` → `ProgrammingPreflight`；方法 `CheckAsync/RejectMismatch`；ECU身份/文件/供电/会话/权限。
- `src/AutoTest.Specialists/Flash/IProgrammingWorkflow.cs` → `IProgrammingWorkflow`；方法 `BuildPlan/ExecuteAsync`；ECU专用阶段序列可注入。
- `src/AutoTest.Specialists/Flash/ProgrammingJobCoordinator.cs` → `ProgrammingJobCoordinator`；方法 `StartAsync/RequestCancel/Observe`；唯一Job状态所有者。
- `src/AutoTest.Specialists/Flash/ProgrammingStageHandlers.cs` → `ProgrammingStageHandlers`；方法 `EnterSession/Transfer/Verify/Reset`；按profile授权调UDS。
- `src/AutoTest.Specialists/Flash/FlashTransferScheduler.cs` → `FlashTransferScheduler`；方法 `WriteBlocks/CheckResponse`；分块、NRC、进度与失败记录。
- `src/AutoTest.Specialists/Flash/ProgrammingEvidence.cs` → `ProgrammingEvidence`；方法 `RecordStage/Finalize`；每次尝试/失败阶段可追溯。
- `src/AutoTest.Specialists/Flash/MockBootloader.cs` → `MockBootloader`；方法 `ReceiveRequest/SimulateNrc`；可注入掉电/延迟/错误的ECU仿真。
- `src/AutoTest.Desktop/Tools/FlashToolView.xaml` → `FlashToolView`；方法 `SelectProfile/Preflight/ShowStages`；独立烧录工具共享服务。

**参考源码/资料：**

- `CanTool:Config/FlashConfiguration.cs`
- `CanTool:Services/FirmwareParser.cs`
- `CanTool:Services/FlashSequence.cs`
- `CanTool:Protocol/SecurityAccess.cs`
- `CanTool:Services/FlashService.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Uds/QuickUdsService.cs`

**仍需通过实验关闭：** 缺少现场成功刷写对应配置、目标 ECU、HEX 和日志；这些是实机刷写的硬前置，不应成为G3伪造常数。

### P1｜生产流程：扫码、烧录、测试与防遗漏

**工程：** `AutoTest.Application`；**G2：** G2-03/04/05/06/08；ICD-05/09/10

生产空间仅协同已有专业服务，不重新写 CAN/UDS/Flash/Runtime。扫码得到匹配草稿→验证 DUT 型号和物理电压→根据项目配置选择烧录和测试资源→冻结作业身份→按可编辑工作流执行→保存记录、统计和不完整提示。

**主要调用链**：ScanDraft→MatchDecision→Preflight→JobSnapshot→(受控 Flash? / TestRun)→结果记录→统计；此流程作为可配置候选，用户试用后可以调整操作顺序，不影响身份锁定及共用服务。

**状态/异常**：Empty→Scanned→Matched→Ready→Running→AwaitingCommit→Completed/Blocked/Failed；运行中二次扫描不改变当前Job。 匹配歧义、12V/24V不符或未知、烧录不可恢复均禁止自动启动；测试重复必须新RunId；遗漏和中断展示独立状态。

**设计文件 7 项：**

- `src/AutoTest.Application/Production/ProductionSession.cs` → `ProductionSession`；方法 `StartDraft/Confirm/Reset`；生产作业身份边界。
- `src/AutoTest.Application/Production/ProductionPreflight.cs` → `ProductionPreflight`；方法 `ValidateMatch/ValidateStation`；资源、目标、权限和电压检查。
- `src/AutoTest.Application/Production/ProductionWorkflow.cs` → `ProductionWorkflow`；方法 `Prepare/ExecuteAsync`；组合FlashJob和TestRun而不重复实现。
- `src/AutoTest.Application/Production/ProductionResultCoordinator.cs` → `ProductionResultCoordinator`；方法 `FinalizeAsync/RequireCommit`；检查Run/Flash/Record完成状态。
- `src/AutoTest.Application/Production/ProductionStatisticsProjection.cs` → `ProductionStatisticsProjection`；方法 `Refresh/Filter`；读取记录域统计不可私存第二份。
- `src/AutoTest.Application/Production/ProductionCommandHandler.cs` → `ProductionCommandHandler`；方法 `Scan/Start/Stop/RetryWithNewRun`；命令权限和状态反馈。
- `src/AutoTest.Desktop/Production/ProductionViewModel.cs` → `ProductionViewModel`；方法 `CaptureScan/StartWork/ShowResult`；可见扫码/匹配/进度/统计。

**参考源码/资料：**

- `CanTool:Services/FlashService.cs`
- `HistoricalHOST:src/AutoTest.Application/RunUseCases.cs`
- `HistoricalHOST:src/AutoTest.Application/RecordUseCases.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Operator/OperatorRunWorkspaceView.xaml.cs`

**仍需通过实验关闭：** 最终生产操作顺序、操作员权限与确认策略要使用真实工位试用后确定；当前结构提供可变流程而非写死。

## 对应的表格登记

文件/类/方法、接口、步骤类型、测试、证据和问题均在本次交付 CSV 中有稳定 ID。主索引：`tables/TEST_STEP_CATALOG.csv`。仅有正文但缺少对应表格或设计模块时，不能宣称该工作产品完成。

## Gate 限制

批准详细设计不等于通过对应单元测试、真实台架或完整生产烧录。
