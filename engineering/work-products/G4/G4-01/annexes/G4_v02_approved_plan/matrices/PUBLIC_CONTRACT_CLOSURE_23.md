# matrices/PUBLIC_CONTRACT_CLOSURE_23.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`d844fbfe7aee7fb10172edd9b690ed7aed84c4abf8d8b8b7001ec101c6dfb032`

原始行数（含表头）：24

| CT | 接口 | G3原型输入 | G3原型返回 | 必须闭合的类型/所有权 | 所有权 | G3不变量 | 阶段 | 现状 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CT-01 | IStationSession.ConnectAsync | StationId, CancellationToken | OperationResult<SessionInfo> | SessionInfo/StationId/OperationResult | 主控USB/CAN/Station | Owner A1；世代隔离，Ready≠供电安全 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-02 | IStationControl.ExecuteAsync | StationCommand, Authorization, CancellationToken | OperationResult<CommandReceipt> | StationCommand/Authorization/CommandReceipt | 主控USB/CAN/Station | Owner A2；ACK≠物理到位 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-03 | ICanFrameHub.SubscribeAsync | CanSubscriptionSpec, CancellationToken | IAsyncEnumerable<CanObservation> | CanSubscriptionSpec/CanObservation | 主控USB/CAN/Station | Owner A3；单接收入口、多消费者 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-04 | ICanFrameHub.TransmitAsync | CanTxRequest, LeaseToken, CancellationToken | OperationResult<CanTxReceipt> | CanTxRequest/LeaseToken/CanTxReceipt | 主控USB/CAN/Station | Owner A3；通道映射和独占租约 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-05 | IProjectService.OpenAsync | string path,CancellationToken | OperationResult<ProjectSnapshot> | ProjectSnapshot/AssetRef | 工程资源与DBC | Owner B1；版本/哈希/引用完整 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-06 | IProjectService.SaveAsync | ProjectSaveRequest,CancellationToken | OperationResult<SaveReceipt> | ProjectSaveRequest/SaveReceipt | 工程资源与DBC | Owner B1；不留下部分提交 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-07 | IDbcDecoder.Decode | SignalRef,CanObservation | SignalObservation | SignalRef/SignalObservation | 工程资源与DBC | Owner B2；带DBC修订与时间质量 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-08 | ITestAuthoringService.Apply | TestDocumentId,EditCommand | EditResult | TestDocumentId/EditCommand/EditResult | 测试编辑/编译/运行 | Owner C1；Undo/Redo与StepId | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-09 | IPlanCompiler.PrepareAsync | CompileRequest,CancellationToken | CompileResult | CompileRequest/CompileResult/PreparedPlan | 测试编辑/编译/运行 | Owner C2；固定资源快照/拒绝坏引用 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-10 | IRunController.StartAsync | PreparedPlan,RunSnapshot,CancellationToken | OperationResult<RunId> | PreparedPlan/RunSnapshot/RunId | 测试编辑/编译/运行 | Owner C2；Run ID不可变 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-11 | IRunController.RequestStopAsync | RunId,CancellationToken | OperationResult | RunId/Unit/RunState | 测试编辑/编译/运行 | Owner C2；安全边界上才停 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-12 | IBarcodeInput.ObserveAsync | InputPolicy,CancellationToken | IAsyncEnumerable<BarcodeScanEvent> | InputPolicy/BarcodeScanEvent | 扫码与规则匹配 | Owner E1；与USB主控解耦 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-13 | IIdentityMatcher.Match | RawScan,RuleRevision,ProjectRevision | MatchDecision | RawScan/RuleRevision/ProjectRevision/MatchDecision | 扫码与规则匹配 | Owner E1；歧义拒绝 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-14 | IUdsClient.RequestAsync | UdsRequest,UdsEndpointProfile,CancellationToken | UdsExchangeResult | UdsRequest/UdsEndpointProfile/UdsExchangeResult | UDS/ISO-TP | Owner E2；ISO-TP完整PDU及NRC | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-15 | IWaveformService.CaptureAsync | SignalRef,WaveCaptureConfig,CancellationToken | WaveEvidenceRef | SignalRef/WaveCaptureConfig/WaveEvidenceRef | 波形 | Owner E3；来源与时间质量 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-16 | IWaveformService.CompareAsync | WaveEvidenceRef,ReferenceRef,CompareProfile | ComparisonResult | WaveEvidenceRef/ReferenceRef/CompareProfile/ComparisonResult | 波形 | Owner E3；Pass/Fail/Inconclusive | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-17 | IProgrammingService.PreflightAsync | ProgrammingRequest,CancellationToken | ProgrammingReadiness | ProgrammingRequest/ProgrammingReadiness | CAN烧录 | Owner E4；只评估不写ECU | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-18 | IProgrammingService.ExecuteAsync | ProgrammingPlan,SafetyAuthorization,CancellationToken | ProgrammingJobId | ProgrammingPlan/SafetyAuthorization/ProgrammingJobId | CAN烧录 | Owner E4；仅单独放行实刷 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-19 | IRunFactSink.AppendAsync | RunFact,CancellationToken | RecordWriteReceipt | RunFact/RecordWriteReceipt | 记录/统计 | Owner R1；不可覆盖 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-20 | IRunFactSink.CommitAsync | RunId,CancellationToken | RecordCommitReceipt | RunId/RecordCommitReceipt | 记录/统计 | Owner R1；Verdict与提交独立 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-21 | IApplicationCommandGateway.ExecuteAsync | ApplicationCommand,CancellationToken | ApplicationCommandResult | ApplicationCommand/ApplicationCommandResult | WPF视觉/Prism/工作台 | Owner D2/P1；VM不持有真实USB | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-22 | IProductionService.PrepareAsync | MatchDecision,ProjectSnapshot,StationProfile | ProductionReadiness | MatchDecision/ProjectSnapshot/StationProfile/ProductionReadiness | 生产流程 | Owner P1；电压匹配和资源绑定 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
| CT-23 | IProductionService.StartAsync | ProductionPlan,Authorization | ProductionJobId | ProductionPlan/Authorization/ProductionJobId | 生产流程 | Owner P1；扫码不会覆写运行身份 | V0公共API签名先闭合，专项对象随模块提交 | NOT_COMPILED / NOT_AUTHORIZED |
