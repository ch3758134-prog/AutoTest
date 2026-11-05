# matrices/G4_FILE_OWNERSHIP_147.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`e1ae51c625446b78ce4769357ecb45300db0182a3509fb3bf3f06bc2258ab8fd`

原始行数（含表头）：148

| G3模块 | G3计划文件 | 所属程序集或资料 | 责任方向 | 实施波次 | 类型或对象 | 关键函数 | 职责边界 | 文件修改权限 | 本轮状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| G3-0 | src/AutoTest.Core/Ids.cs | AutoTest.Core | INTEGRATION | V0 | StationId,EndpointId,ProjectId,AssetId,TestDocumentId,StepId,RunId | Create/TryParse/ToString | 不可混用的类型化ID | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Core/OperationResult.cs | AutoTest.Core | INTEGRATION | V0 | OperationResult<T>,OperationFailure | Accepted/Rejected/Uncertain | 请求接受、设备应答与真实观测不同层级 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Core/TimeEvidence.cs | AutoTest.Core | INTEGRATION | V0 | EvidenceRef,TimeQuality,TimePoint | FromHostArrival/FromReplay | 记录时源、质量、采集来源 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Core/VersionFingerprint.cs | AutoTest.Core | INTEGRATION | V0 | RevisionFingerprint | FromSha256/Matches | 资源与程序按内容识别 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Contracts/DeviceContracts.cs | AutoTest.Contracts | INTEGRATION | V0 | IStationSession,IStationControl | ConnectAsync/ExecuteAsync | 设备层统一入口 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Contracts/CanContracts.cs | AutoTest.Contracts | INTEGRATION | V0 | ICanFrameHub,ITransmitLeaseService | SubscribeAsync/TransmitAsync | 单接收流与租约 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Contracts/ProjectContracts.cs | AutoTest.Contracts | INTEGRATION | V0 | IProjectService,IAssetResolver | OpenAsync/Resolve/SaveAsync | 工程只读快照与资源验证 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Contracts/TestingContracts.cs | AutoTest.Contracts | INTEGRATION | V0 | ITestAuthoringService,IPlanCompiler,IRunController | Apply/PrepareAsync/StartAsync | 编辑与执行不互引UI | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Contracts/SpecialistContracts.cs | AutoTest.Contracts | INTEGRATION | V0 | IBarcodeInput,IUdsClient,IWaveformService,IProgrammingService | Capture/Execute/Compare/Program | 专业入口分开，共享CAN | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Contracts/RecordContracts.cs | AutoTest.Contracts | INTEGRATION | V0 | IRunFactSink,IRecordReader | AppendAsync/CommitAsync/QueryAsync | 追加事实与可验证提交 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Contracts/UiContracts.cs | AutoTest.Contracts | INTEGRATION | V0 | IApplicationCommandGateway,WorkbenchProjection | ExecuteAsync/Observe | UI只用命令和只读投影 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| G3-0 | src/AutoTest.Application/Composition/ApplicationComposition.cs | AutoTest.Application | INTEGRATION | V0 | ApplicationComposition | RegisterCore/RegisterMock/RegisterReal | 唯一装配根/防止双设备句柄 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/IUsbByteLink.cs | AutoTest.Devices | DEVICE | V0→V2 | IUsbByteLink | OpenAsync/ReadAsync/WriteAsync/CloseAsync | 仅负责字节流与取消 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/UsbWireCodec.cs | AutoTest.Devices | DEVICE | V0→V2 | UsbWireCodec | EncodeRequest/TryDecodeResponse/Crc16 | 无IO字节帧 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/UsbFrameDecoder.cs | AutoTest.Devices | DEVICE | V0→V2 | UsbFrameDecoder | Feed/Reset | 处理拆帧粘帧与异常前缀 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/UsbDiscovery.cs | AutoTest.Devices | DEVICE | V0→V2 | UsbDiscovery | ListCandidatesAsync | 不把扫码枪枚举为主控 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/PendingRequestRegistry.cs | AutoTest.Devices | DEVICE | V0→V2 | PendingRequestRegistry | Register/TryComplete/FailGeneration | 世代、序号、命令强关联 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/UsbSessionService.cs | AutoTest.Devices | DEVICE | V0→V2 | UsbSessionService | ConnectAsync/RequestAsync/DisconnectAsync | 单会话所有者、后台读循环 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/MainControllerCodec.cs | AutoTest.Devices | DEVICE | V0→V2 | MainControllerCodec | EncodePing/EncodeCan/EncodeIgn/EncodeSol | 业务命令映射按源码 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A1 | src/AutoTest.Devices/Usb/UsbDiagnosticsProjection.cs | AutoTest.Devices | DEVICE | V0→V2 | UsbDiagnosticsProjection | ObserveState/ObserveRawFrame | 只读原始报文与异常统计 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/StationProfile.cs | AutoTest.Devices | DEVICE | V3 | StationProfile | Validate/DescribeBindings | 逻辑角色到设备通道映射 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/CapabilityRegistry.cs | AutoTest.Devices | DEVICE | V3 | CapabilityRegistry | PublishVerified/GetAvailable | 只发布已证实能力 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/StationPreflight.cs | AutoTest.Devices | DEVICE | V3 | StationPreflight | CheckAsync | 供电型号/接线/权限检查 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/StationControlService.cs | AutoTest.Devices | DEVICE | V3 | StationControlService | SetIgnAsync/OperateFixtureAsync | 有副作用操作入口 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/KeypadCommandCodec.cs | AutoTest.Devices | DEVICE | V3 | KeypadCommandCodec | EncodeMask/EncodeReleaseAll | 0x730 + 20位mask，8字节帧 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/KeypadControlService.cs | AutoTest.Devices | DEVICE | V3 | KeypadControlService | SetMaskAsync/ReleaseAllAsync | 仅经 KEY_CAN 发送 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/OutputObservation.cs | AutoTest.Devices | DEVICE | V3 | OutputObservation | ClassifyEvidence | 请求/ACK/电气/机械不同证据等级 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A2 | src/AutoTest.Devices/Station/FirmwareIdentityTracker.cs | AutoTest.Devices | DEVICE | V3 | FirmwareIdentityTracker | ReadAndCompare/RecordIdentity | 主/按键固件版本及追踪 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A3 | src/AutoTest.Devices/Can/CanFrameHub.cs | AutoTest.Devices | DEVICE | V2 | CanFrameHub | Publish/Subscribe/Unsubscribe | 统一扇出、每订阅独立限额 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A3 | src/AutoTest.Devices/Can/CanFrameCodec.cs | AutoTest.Devices | DEVICE | V2 | CanFrameCodec | FromUsbPayload/ToUsbPayload | 15字节CAN字段校验 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A3 | src/AutoTest.Devices/Can/CanTransmitGate.cs | AutoTest.Devices | DEVICE | V2 | CanTransmitGate | AcquireAsync/Release/TransmitAsync | 发送与诊断独占会话 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A3 | src/AutoTest.Devices/Can/CanSubscription.cs | AutoTest.Devices | DEVICE | V2 | CanSubscription | Match/ReadAsync | 按BusRole/ID/IDE过滤 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A3 | src/AutoTest.Devices/Can/CanLossMonitor.cs | AutoTest.Devices | DEVICE | V2 | CanLossMonitor | OnDrop/ReadSnapshot | 本地缓冲丢失可见 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A3 | src/AutoTest.Devices/Can/CanTimeAnnotator.cs | AutoTest.Devices | DEVICE | V2 | CanTimeAnnotator | StampHostArrival/AnnotateReplay | HOST到达时刻不冒充ECU采样时间 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| A3 | src/AutoTest.Devices/Can/CanTraceProjection.cs | AutoTest.Devices | DEVICE | V2 | CanTraceProjection | Append/Query/Export | 日志只读消费，不阻塞收包 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Project/ProjectDefinition.cs | AutoTest.Projects | PROJECT | V1 | ProjectDefinition | Validate/WithRevision | ProjectId、资源清单与格式版本 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Project/ProjectRepository.cs | AutoTest.Projects | PROJECT | V1 | ProjectRepository | Create/OpenAsync/SaveAsync | 暂存写入+校验+原子替换或明确失败 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Project/ProjectPathPolicy.cs | AutoTest.Projects | PROJECT | V1 | ProjectPathPolicy | Normalize/ValidateInsideRoot | 阻止资源路径越界 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Resources/AssetEntry.cs | AutoTest.Projects | PROJECT | V1 | AssetEntry | Create/VerifyHash | AssetId、Revision、Kind、SHA256 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Resources/AssetCatalog.cs | AutoTest.Projects | PROJECT | V1 | AssetCatalog | Import/ReplaceRevision/Resolve | 统一资产身份不依赖文件名 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Resources/ReferenceValidator.cs | AutoTest.Projects | PROJECT | V1 | ReferenceValidator | ScanDependencies/RejectDangling | DBC、测试步骤与配置引用检查 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Resources/ResourceEditService.cs | AutoTest.Projects | PROJECT | V1 | ResourceEditService | Add/Rename/Remove/Undo | 工程树操作事务 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Profiles/DutAndStationProfiles.cs | AutoTest.Projects | PROJECT | V1 | DutProfile,StationProfileRef | ResolveProfile/CheckVoltage | 型号配置与电源前置关系 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Profiles/ResourceBindingService.cs | AutoTest.Projects | PROJECT | V1 | ResourceBindingService | Resolve/ValidateSnapshot | 逻辑端点与能力引用 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B1 | src/AutoTest.Projects/Storage/SchemaMigrationService.cs | AutoTest.Projects | PROJECT | V1 | SchemaMigrationService | Preview/MigrateCopy | 版本迁移可预检与失败停机 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B2 | src/AutoTest.Projects/Dbc/DbcSource.cs | AutoTest.Projects | PROJECT | V1 | DbcSource | Load/Hash | 保留源文件与编码诊断 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B2 | src/AutoTest.Projects/Dbc/DbcDatabase.cs | AutoTest.Projects | PROJECT | V1 | DbcDatabase | FindMessage/FindSignal | 信号与报文不可变定义 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B2 | src/AutoTest.Projects/Dbc/DbcParserAdapter.cs | AutoTest.Projects | PROJECT | V1 | DbcParserAdapter | Parse/TryParse | DBC语义解析与错误位置 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B2 | src/AutoTest.Projects/Dbc/DbcSignalCodec.cs | AutoTest.Projects | PROJECT | V1 | DbcSignalCodec | DecodeRaw/EncodeRaw/ToPhysical | 明确Intel/Motorola位序 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B2 | src/AutoTest.Projects/Dbc/DbcSignalResolver.cs | AutoTest.Projects | PROJECT | V1 | DbcSignalResolver | Resolve(SignalRef)/DetectStale | 防修订漂移 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B2 | src/AutoTest.Projects/Dbc/DbcEditSession.cs | AutoTest.Projects | PROJECT | V1 | DbcEditSession | Apply/Validate/Export | 高级编辑可演进的事务边界 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| B2 | src/AutoTest.Projects/Dbc/DecodedSignalPublisher.cs | AutoTest.Projects | PROJECT | V1 | DecodedSignalPublisher | OnCanFrame/ObserveSignal | 复用 CAN Hub 输入和质量标记 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/TestDocument.cs | AutoTest.Testing | TEST | V1 | TestDocument,StepNode | Create/CloneSnapshot | 可编辑树，稳定StepId | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs | AutoTest.Testing | TEST | V1 | StepDescriptorCatalog | Register/Lookup/Enumerate | 类型/属性/资源要求/槽位元数据 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/StepEditTransaction.cs | AutoTest.Testing | TEST | V1 | StepEditTransaction | Apply/Undo/Redo | 插入移动复制删除的原子操作 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/StepTreeValidator.cs | AutoTest.Testing | TEST | V1 | StepTreeValidator | ValidateTree/ValidateSlots | 禁止循环引用与非法Else位置 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/TestDocumentCodec.cs | AutoTest.Testing | TEST | V1 | TestDocumentCodec | Serialize/Deserialize/Migrate | 持久化与版本诊断 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/StepPropertyValidator.cs | AutoTest.Testing | TEST | V1 | StepPropertyValidator | ValidateBinding/ValidateParameter | 属性面板编辑前验证 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/StepCatalogRegistration.cs | AutoTest.Testing | TEST | V1 | StepCatalogRegistration | RegisterBuiltins | 全部工作包共用步骤注册入口 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C1 | src/AutoTest.Testing/Authoring/StepRefactorService.cs | AutoTest.Testing | TEST | V1 | StepRefactorService | RenameAsset/RemapReference | 工程资源变化传播检查 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Preparation/PlanCompiler.cs | AutoTest.Testing | TEST | V1→V3 | PlanCompiler | PrepareAsync/ValidateDependencies | 冻结步骤图、绑定与诊断 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Preparation/PreparedPlan.cs | AutoTest.Testing | TEST | V1→V3 | PreparedPlan | ValidateFingerprint | 只读执行快照 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/RunCoordinator.cs | AutoTest.Testing | TEST | V1→V3 | RunCoordinator | StartAsync/RequestStop/PauseAtBoundary | 运行唯一状态拥有者 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/StepDispatcher.cs | AutoTest.Testing | TEST | V1→V3 | StepDispatcher | DispatchAsync | 按StepDescriptor查找Handler | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/StepHandlerRegistry.cs | AutoTest.Testing | TEST | V1→V3 | StepHandlerRegistry | Resolve/Register | 可替换Handler与版本合同 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/FlowExecutors.cs | AutoTest.Testing | TEST | V1→V3 | IfExecutor,LoopExecutor,ParallelExecutor | ExecuteAsync | 明确条件求值及有界循环 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/ResourceLeaseScope.cs | AutoTest.Testing | TEST | V1→V3 | ResourceLeaseScope | AcquireAsync/DisposeAsync | 操作结束释放资源租约 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/StepJudge.cs | AutoTest.Testing | TEST | V1→V3 | StepJudge | Evaluate/NoEvidence | 可追踪预期/实际与质量 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/RunFactJournal.cs | AutoTest.Testing | TEST | V1→V3 | RunFactJournal | Append/Publish | 运行事实追加且带OperationId | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| C2 | src/AutoTest.Testing/Runtime/RunControlPolicy.cs | AutoTest.Testing | TEST | V1→V3 | RunControlPolicy | CanPause/CanSkip/CanStep | 不可中断副作用的安全边界 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Tokens/Colors.xaml | AutoTest.Desktop | UI | V1 | ColorTokens | Primary/Surface/Status | 基础色与状态色分离 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Tokens/Typography.xaml | AutoTest.Desktop | UI | V1 | TypographyTokens | FontSize/Weight | 导航/内容/属性/表格字体体系 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Tokens/Metrics.xaml | AutoTest.Desktop | UI | V1 | MetricTokens | Compact/Normal | 高密度UI尺寸 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Light.xaml | AutoTest.Desktop | UI | V1 | LightTheme | MergedDictionaries | 浅色主题资源映射 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Dark.xaml | AutoTest.Desktop | UI | V1 | DarkTheme | MergedDictionaries | 深色主题资源映射 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Controls/TreeStyles.xaml | AutoTest.Desktop | UI | V1 | TreeStyles | Selection/Hover/RunState | 工程树/步骤树视觉状态 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Controls/Inputs.xaml | AutoTest.Desktop | UI | V1 | InputStyles | Validation/Disabled | 字段编辑视觉及错误反馈 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Controls/Tables.xaml | AutoTest.Desktop | UI | V1 | TableStyles | RowVirtualization | CAN日志/结果/统计高密度表 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/Themes/Controls/RunBadges.xaml | AutoTest.Desktop | UI | V1 | RunBadges | Passed/Failed/Unknown | 状态图标和文字配合 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D1 | src/AutoTest.Desktop/DesignGallery/GalleryWindow.xaml | AutoTest.Desktop | UI | V1 | GalleryWindow | PreviewTheme/PreviewDensity | 组件/文字/状态可视化验收 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/App.xaml | AutoTest.Desktop | UI | V1→V4 | App | Startup/Resources | 唯一WPF启动和Theme字典 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Bootstrap/PrismRegistration.cs | AutoTest.Desktop | UI | V1→V4 | PrismRegistration | RegisterTypes/CreateShell | Prism 9导航、容器组合 | 变更须经集成负责人 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Shell/MainWindow.xaml | AutoTest.Desktop | UI | V1→V4 | MainWindow | Document/Left/Right/BottomRegions | IDE工作台 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Shell/MainWindowViewModel.cs | AutoTest.Desktop | UI | V1→V4 | MainWindowViewModel | OpenCommand/RunCommand | 只发送应用命令 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Shell/WorkbenchLayoutService.cs | AutoTest.Desktop | UI | V1→V4 | WorkbenchLayoutService | SaveLayout/RestoreLayout | 窗口与Dock状态保存 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Shell/SelectionContext.cs | AutoTest.Desktop | UI | V1→V4 | SelectionContext | Select/GetSelection | 选中项与当前运行步骤分离 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Project/ProjectExplorerView.xaml | AutoTest.Desktop | UI | V1→V4 | ProjectExplorerView | AddResource/Rename/Delete | 工程导航和操作 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Testing/TestEditorView.xaml | AutoTest.Desktop | UI | V1→V4 | TestEditorView | SelectStep/InsertStep | 树/步骤双击及右键编辑 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Testing/StepInspectorView.xaml | AutoTest.Desktop | UI | V1→V4 | StepInspectorView | EditProperty/Validate | 右侧属性编辑和即时诊断 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Testing/RunMonitorView.xaml | AutoTest.Desktop | UI | V1→V4 | RunMonitorView | ObserveRun/ShowEvidence | 预期/实际/判定/步骤状态 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Devices/BenchDiagnosticsView.xaml | AutoTest.Desktop | UI | V1→V4 | BenchDiagnosticsView | Connect/ShowRawFrames | USB及台架复验入口 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Tools/ToolWindowRegistry.cs | AutoTest.Desktop | UI | V1→V4 | ToolWindowRegistry | OpenCan/OpenUds/OpenFlash/OpenWave | 共享专业窗口生命周期 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Desktop/Production/ProductionWorkspaceView.xaml | AutoTest.Desktop | UI | V1→V4 | ProductionWorkspaceView | Capture/Preflight/Run | 生产工作区与结果 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| D2 | src/AutoTest.Application/Commands/ApplicationCommandRouter.cs | AutoTest.Application | INTEGRATION | V1→V4 | ApplicationCommandRouter | ExecuteAsync/GetCanExecuteReason | 跨模块命令与失败反馈 | 唯一公共配置 | DESIGN_ONLY / NOT_CREATED |
| E1 | src/AutoTest.Specialists/Scan/BarcodeInputRouter.cs | AutoTest.Specialists | SCAN | V4 | BarcodeInputRouter | AttachMode/PublishRaw | 统一输入事件源 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E1 | src/AutoTest.Specialists/Scan/KeyboardWedgeCapture.cs | AutoTest.Specialists | SCAN | V4 | KeyboardWedgeCapture | OnKeyInput/CompleteScan | 键盘模拟模式可配置焦点安全 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E1 | src/AutoTest.Specialists/Scan/SerialBarcodeCapture.cs | AutoTest.Specialists | SCAN | V4 | SerialBarcodeCapture | OpenAsync/ReadAsync | 仅实测为串口设备时启用 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E1 | src/AutoTest.Specialists/Scan/BarcodeScanEvent.cs | AutoTest.Specialists | SCAN | V4 | BarcodeScanEvent | ValidateSource/WithTime | Raw、来源、时间、质量 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E1 | src/AutoTest.Specialists/Scan/IdentityRuleSet.cs | AutoTest.Specialists | SCAN | V4 | IdentityRuleSet | Validate/Match | 编号格式解析规则受控修订 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E1 | src/AutoTest.Specialists/Scan/ConfigMatchService.cs | AutoTest.Specialists | SCAN | V4 | ConfigMatchService | Evaluate/RejectAmbiguous | 型号、CAN、测试与烧录资源关联 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E1 | src/AutoTest.Specialists/Scan/ScanDraftService.cs | AutoTest.Specialists | SCAN | V4 | ScanDraftService | Capture/Confirm/Discard | 扫码草稿不得改写运行身份 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/IsoTpFrameCodec.cs | AutoTest.Specialists | UDS | V4 | IsoTpFrameCodec | EncodeSingle/ParseFirst/ParseConsecutive/ParseFlow | 标准帧类型和长度检查 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/IsoTpConversation.cs | AutoTest.Specialists | UDS | V4 | IsoTpConversation | SendPduAsync/ReceivePduAsync | FC/BS/STmin/序号与超时 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/UdsEndpointProfile.cs | AutoTest.Specialists | UDS | V4 | UdsEndpointProfile | ValidateIds/ValidateAddressing | CAN TX/RX ID及地址模式配置 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/UdsClient.cs | AutoTest.Specialists | UDS | V4 | UdsClient | RequestAsync/InterpretResponse | 正响应/NRC/P2*可配置 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/UdsServiceCatalog.cs | AutoTest.Specialists | UDS | V4 | UdsServiceCatalog | Register/GetRiskLevel | 服务参数与权限分级 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/UdsSessionService.cs | AutoTest.Specialists | UDS | V4 | UdsSessionService | StartSession/TesterPresentPolicy/EndSession | 会话生命周期、独占租约 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/UdsTrace.cs | AutoTest.Specialists | UDS | V4 | UdsTrace | AppendRequest/AppendResponse | 原始PDU/总线帧与错误可溯源 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Specialists/Uds/UdsStepHandler.cs | AutoTest.Specialists | UDS | V4 | UdsStepHandler | ExecuteAsync | 供测试步骤复用而非复制协议 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E2 | src/AutoTest.Desktop/Tools/UdsToolView.xaml | AutoTest.Desktop | UDS | V4 | UdsToolView | Send/ShowNrc/ShowSession | 独立原始请求响应窗口 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveCaptureService.cs | AutoTest.Specialists | WAVE | V4 | WaveCaptureService | StartAsync/StopAsync/Observe | 订阅DBC信号、生成CaptureId | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveSampleBuffer.cs | AutoTest.Specialists | WAVE | V4 | WaveSampleBuffer | Append/Snapshot | 单调时间序列和容量限制 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveTimeQuality.cs | AutoTest.Specialists | WAVE | V4 | WaveTimeQuality | Describe/ValidateForJudgement | 采样间隔及丢帧质量 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveReferenceRepository.cs | AutoTest.Specialists | WAVE | V4 | WaveReferenceRepository | SaveReference/LoadReference | 参考资产版本与用途 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveComparisonConfig.cs | AutoTest.Specialists | WAVE | V4 | WaveComparisonConfig | ValidateThresholds | 容差/时间偏移/最小覆盖率 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveComparator.cs | AutoTest.Specialists | WAVE | V4 | WaveComparator | Align/Interpolate/Compare | 候选算法与可替换策略 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveEvidenceCodec.cs | AutoTest.Specialists | WAVE | V4 | WaveEvidenceCodec | Write/Read/Verify | 原始采样、配置、比较结果 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Specialists/Wave/WaveStepHandler.cs | AutoTest.Specialists | WAVE | V4 | WaveStepHandler | ExecuteAsync | 供自动测试复用采集服务 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E3 | src/AutoTest.Desktop/Tools/WaveToolView.xaml | AutoTest.Desktop | WAVE | V4 | WaveToolView | ShowChart/MarkReference/Compare | 独立波形工具与运行证据 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/FirmwareImageParser.cs | AutoTest.Specialists | FLASH | V5 | FirmwareImageParser | ParseHex/ParseS19/ValidateSegments | 分段镜像、校验与重叠检测 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/ProgrammingProfile.cs | AutoTest.Specialists | FLASH | V5 | ProgrammingProfile | Validate/DescribeTarget | 目标/请求ID/块大小/安全策略/超时 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/ProgrammingPreflight.cs | AutoTest.Specialists | FLASH | V5 | ProgrammingPreflight | CheckAsync/RejectMismatch | ECU身份/文件/供电/会话/权限 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/IProgrammingWorkflow.cs | AutoTest.Specialists | FLASH | V5 | IProgrammingWorkflow | BuildPlan/ExecuteAsync | ECU专用阶段序列可注入 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/ProgrammingJobCoordinator.cs | AutoTest.Specialists | FLASH | V5 | ProgrammingJobCoordinator | StartAsync/RequestCancel/Observe | 唯一Job状态所有者 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/ProgrammingStageHandlers.cs | AutoTest.Specialists | FLASH | V5 | ProgrammingStageHandlers | EnterSession/Transfer/Verify/Reset | 按profile授权调UDS | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/FlashTransferScheduler.cs | AutoTest.Specialists | FLASH | V5 | FlashTransferScheduler | WriteBlocks/CheckResponse | 分块、NRC、进度与失败记录 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/ProgrammingEvidence.cs | AutoTest.Specialists | FLASH | V5 | ProgrammingEvidence | RecordStage/Finalize | 每次尝试/失败阶段可追溯 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Specialists/Flash/MockBootloader.cs | AutoTest.Specialists | FLASH | V5 | MockBootloader | ReceiveRequest/SimulateNrc | 可注入掉电/延迟/错误的ECU仿真 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| E4 | src/AutoTest.Desktop/Tools/FlashToolView.xaml | AutoTest.Desktop | FLASH | V5 | FlashToolView | SelectProfile/Preflight/ShowStages | 独立烧录工具共享服务 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Model/RunRecord.cs | AutoTest.Records | RECORD | V3→V5 | RunRecord,RunAttempt | CreateFromSnapshot/Validate | DUT、工程、软固件、测试计划指纹 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Model/RunFact.cs | AutoTest.Records | RECORD | V3→V5 | RunFact | Append/ValidateSequence | 结构化步骤/操作/观测事实 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Storage/RecordDatabase.cs | AutoTest.Records | RECORD | V3→V5 | RecordDatabase | Open/Migrate/CommitTransaction | SQLite schema和事务 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Storage/RunRecordWriter.cs | AutoTest.Records | RECORD | V3→V5 | RunRecordWriter | Begin/Append/CommitAsync | 追加写和提交回执 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Storage/ArtifactRepository.cs | AutoTest.Records | RECORD | V3→V5 | ArtifactRepository | Put/Verify/Read | 波形、报文和烧录文件证据引用 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Queries/RunHistoryQuery.cs | AutoTest.Records | RECORD | V3→V5 | RunHistoryQuery | FindByDut/FindByRun/Filter | 历史不可变只读查询 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Queries/ProductionStatistics.cs | AutoTest.Records | RECORD | V3→V5 | ProductionStatistics | CountAttempts/CountDistinctDuts/GetSummary | 次数与不同ECU统计严格分离 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| R1 | src/AutoTest.Records/Exports/RunReportExporter.cs | AutoTest.Records | RECORD | V3→V5 | RunReportExporter | ExportSummary/ExportStepFacts | 报告包含证据来源与Unknown | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| P1 | src/AutoTest.Application/Production/ProductionSession.cs | AutoTest.Application | PRODUCTION | V5 | ProductionSession | StartDraft/Confirm/Reset | 生产作业身份边界 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| P1 | src/AutoTest.Application/Production/ProductionPreflight.cs | AutoTest.Application | PRODUCTION | V5 | ProductionPreflight | ValidateMatch/ValidateStation | 资源、目标、权限和电压检查 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| P1 | src/AutoTest.Application/Production/ProductionWorkflow.cs | AutoTest.Application | PRODUCTION | V5 | ProductionWorkflow | Prepare/ExecuteAsync | 组合FlashJob和TestRun而不重复实现 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| P1 | src/AutoTest.Application/Production/ProductionResultCoordinator.cs | AutoTest.Application | PRODUCTION | V5 | ProductionResultCoordinator | FinalizeAsync/RequireCommit | 检查Run/Flash/Record完成状态 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| P1 | src/AutoTest.Application/Production/ProductionStatisticsProjection.cs | AutoTest.Application | PRODUCTION | V5 | ProductionStatisticsProjection | Refresh/Filter | 读取记录域统计不可私存第二份 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| P1 | src/AutoTest.Application/Production/ProductionCommandHandler.cs | AutoTest.Application | PRODUCTION | V5 | ProductionCommandHandler | Scan/Start/Stop/RetryWithNewRun | 命令权限和状态反馈 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| P1 | src/AutoTest.Desktop/Production/ProductionViewModel.cs | AutoTest.Desktop | PRODUCTION | V5 | ProductionViewModel | CaptureScan/StartWork/ShowResult | 可见扫码/匹配/进度/统计 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| FW1 | docs/integration/FirmwareCompatibility.md | Docs/Verification | DEVICE | V2→V3 | FirmwareCompatibility | DefineSupportedPairs | 双固件/主控协议/Station能力兼容范围 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| FW1 | src/AutoTest.Devices/Station/FirmwareCompatibilityChecker.cs | AutoTest.Devices | DEVICE | V2→V3 | FirmwareCompatibilityChecker | Validate/ExplainLimitations | 连接后检查版本能力，不自动刷主控 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
| FW1 | verification/FirmwareChangeImpact.md | Docs/Verification | DEVICE | V2→V3 | FirmwareChangeImpact | TraceChangedCommands/SelectRegression | 修订触及接口时重测B1～B6必要项 | 本方向可改，其他方向只读 | DESIGN_ONLY / NOT_CREATED |
