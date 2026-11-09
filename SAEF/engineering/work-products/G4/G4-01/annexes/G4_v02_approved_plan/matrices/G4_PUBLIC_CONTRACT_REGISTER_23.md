# matrices/G4_PUBLIC_CONTRACT_REGISTER_23.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`2fc846927595a063e09d3e6d5e343a3a9292208b82ae8abfd91f7d220504156e`

原始行数（含表头）：24

| ID | 接口 | 输入 | 返回 | 核心不变量 | G2 | 责任方向 | 实施状态 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CT-01 | IStationSession.ConnectAsync | StationId, CancellationToken | OperationResult<SessionInfo> | Owner A1；世代隔离，Ready≠供电安全 | ICD-01 | 主控USB/CAN/Station | DESIGN_ONLY / NOT_COMPILED |
| CT-02 | IStationControl.ExecuteAsync | StationCommand, Authorization, CancellationToken | OperationResult<CommandReceipt> | Owner A2；ACK≠物理到位 | ICD-03 | 主控USB/CAN/Station | DESIGN_ONLY / NOT_COMPILED |
| CT-03 | ICanFrameHub.SubscribeAsync | CanSubscriptionSpec, CancellationToken | IAsyncEnumerable<CanObservation> | Owner A3；单接收入口、多消费者 | ICD-02/12 | 主控USB/CAN/Station | DESIGN_ONLY / NOT_COMPILED |
| CT-04 | ICanFrameHub.TransmitAsync | CanTxRequest, LeaseToken, CancellationToken | OperationResult<CanTxReceipt> | Owner A3；通道映射和独占租约 | ICD-02/08 | 主控USB/CAN/Station | DESIGN_ONLY / NOT_COMPILED |
| CT-05 | IProjectService.OpenAsync | string path,CancellationToken | OperationResult<ProjectSnapshot> | Owner B1；版本/哈希/引用完整 | ICD-06 | 工程资源与DBC | DESIGN_ONLY / NOT_COMPILED |
| CT-06 | IProjectService.SaveAsync | ProjectSaveRequest,CancellationToken | OperationResult<SaveReceipt> | Owner B1；不留下部分提交 | ICD-06 | 工程资源与DBC | DESIGN_ONLY / NOT_COMPILED |
| CT-07 | IDbcDecoder.Decode | SignalRef,CanObservation | SignalObservation | Owner B2；带DBC修订与时间质量 | ICD-06/12 | 工程资源与DBC | DESIGN_ONLY / NOT_COMPILED |
| CT-08 | ITestAuthoringService.Apply | TestDocumentId,EditCommand | EditResult | Owner C1；Undo/Redo与StepId | ICD-07 | 测试编辑/编译/运行 | DESIGN_ONLY / NOT_COMPILED |
| CT-09 | IPlanCompiler.PrepareAsync | CompileRequest,CancellationToken | CompileResult | Owner C2；固定资源快照/拒绝坏引用 | ICD-07/08 | 测试编辑/编译/运行 | DESIGN_ONLY / NOT_COMPILED |
| CT-10 | IRunController.StartAsync | PreparedPlan,RunSnapshot,CancellationToken | OperationResult<RunId> | Owner C2；Run ID不可变 | ICD-08/10 | 测试编辑/编译/运行 | DESIGN_ONLY / NOT_COMPILED |
| CT-11 | IRunController.RequestStopAsync | RunId,CancellationToken | OperationResult | Owner C2；安全边界上才停 | ICD-08 | 测试编辑/编译/运行 | DESIGN_ONLY / NOT_COMPILED |
| CT-12 | IBarcodeInput.ObserveAsync | InputPolicy,CancellationToken | IAsyncEnumerable<BarcodeScanEvent> | Owner E1；与USB主控解耦 | ICD-04 | 扫码与规则匹配 | DESIGN_ONLY / NOT_COMPILED |
| CT-13 | IIdentityMatcher.Match | RawScan,RuleRevision,ProjectRevision | MatchDecision | Owner E1；歧义拒绝 | ICD-05 | 扫码与规则匹配 | DESIGN_ONLY / NOT_COMPILED |
| CT-14 | IUdsClient.RequestAsync | UdsRequest,UdsEndpointProfile,CancellationToken | UdsExchangeResult | Owner E2；ISO-TP完整PDU及NRC | ICD-12 | UDS/ISO-TP | DESIGN_ONLY / NOT_COMPILED |
| CT-15 | IWaveformService.CaptureAsync | SignalRef,WaveCaptureConfig,CancellationToken | WaveEvidenceRef | Owner E3；来源与时间质量 | ICD-10/12 | 波形 | DESIGN_ONLY / NOT_COMPILED |
| CT-16 | IWaveformService.CompareAsync | WaveEvidenceRef,ReferenceRef,CompareProfile | ComparisonResult | Owner E3；Pass/Fail/Inconclusive | ICD-10/12 | 波形 | DESIGN_ONLY / NOT_COMPILED |
| CT-17 | IProgrammingService.PreflightAsync | ProgrammingRequest,CancellationToken | ProgrammingReadiness | Owner E4；只评估不写ECU | ICD-09 | CAN烧录 | DESIGN_ONLY / NOT_COMPILED |
| CT-18 | IProgrammingService.ExecuteAsync | ProgrammingPlan,SafetyAuthorization,CancellationToken | ProgrammingJobId | Owner E4；仅单独放行实刷 | ICD-09/12 | CAN烧录 | DESIGN_ONLY / NOT_COMPILED |
| CT-19 | IRunFactSink.AppendAsync | RunFact,CancellationToken | RecordWriteReceipt | Owner R1；不可覆盖 | ICD-10 | 记录/统计 | DESIGN_ONLY / NOT_COMPILED |
| CT-20 | IRunFactSink.CommitAsync | RunId,CancellationToken | RecordCommitReceipt | Owner R1；Verdict与提交独立 | ICD-10 | 记录/统计 | DESIGN_ONLY / NOT_COMPILED |
| CT-21 | IApplicationCommandGateway.ExecuteAsync | ApplicationCommand,CancellationToken | ApplicationCommandResult | Owner D2/P1；VM不持有真实USB | ICD-11 | WPF视觉/Prism/工作台 | DESIGN_ONLY / NOT_COMPILED |
| CT-22 | IProductionService.PrepareAsync | MatchDecision,ProjectSnapshot,StationProfile | ProductionReadiness | Owner P1；电压匹配和资源绑定 | ICD-05/09 | 生产流程 | DESIGN_ONLY / NOT_COMPILED |
| CT-23 | IProductionService.StartAsync | ProductionPlan,Authorization | ProductionJobId | Owner P1；扫码不会覆写运行身份 | ICD-09/10 | 生产流程 | DESIGN_ONLY / NOT_COMPILED |
