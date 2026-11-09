# G3-05｜工程资源、文件和持久化（全量候审 v0.2）

> SAEF G3 正式候审内容；**G4尚未实施、所有测试初始 NOT\_RUN**。

## 上游批准约束

G2 v0.7 的系统边界/12 项 ICD/状态所有权及 P0～G1 已批准内容必须保留。详见 `README_FIRST.md` 和 `docs/COMMON_DESIGN_CONTRACT.md`。

## 本工作产品适用的详细设计模块

### B1｜工程、资源身份、文件持久化

**工程：** `AutoTest.Projects`；**G2：** G2-03/05/06；ICD-06

建立清晰的项目文件容器：项目元数据、资源清单、运行配置、DBC、测试程序、HEX引用、烧录Profile、参考波形、编号匹配规则；不把历史运行记录当可覆盖的工程资源。支持损坏/缺引用明确拒绝与旧版本迁移审计。

**主要调用链**：打开工程→读取manifest→验证schema/路径/内容哈希→构建索引→引用诊断；保存时先将资源写入临时文件并校验，再提交新revision，不静默覆盖历史资产。

**状态/异常**：Closed→Opening→Loaded/Corruped→Dirty→Saving→Loaded/SaveFailed；工程工作区与真实设备连接是独立状态。 缺文件/哈希不一致禁止编译Run；同名不同资源区分；工程中 HEX 文件可引用或导入但运行必须有内容指纹；取消保存不产生半成品。

**设计文件 10 项：**

- `src/AutoTest.Projects/Project/ProjectDefinition.cs` → `ProjectDefinition`；方法 `Validate/WithRevision`；ProjectId、资源清单与格式版本。
- `src/AutoTest.Projects/Project/ProjectRepository.cs` → `ProjectRepository`；方法 `Create/OpenAsync/SaveAsync`；暂存写入+校验+原子替换或明确失败。
- `src/AutoTest.Projects/Project/ProjectPathPolicy.cs` → `ProjectPathPolicy`；方法 `Normalize/ValidateInsideRoot`；阻止资源路径越界。
- `src/AutoTest.Projects/Resources/AssetEntry.cs` → `AssetEntry`；方法 `Create/VerifyHash`；AssetId、Revision、Kind、SHA256。
- `src/AutoTest.Projects/Resources/AssetCatalog.cs` → `AssetCatalog`；方法 `Import/ReplaceRevision/Resolve`；统一资产身份不依赖文件名。
- `src/AutoTest.Projects/Resources/ReferenceValidator.cs` → `ReferenceValidator`；方法 `ScanDependencies/RejectDangling`；DBC、测试步骤与配置引用检查。
- `src/AutoTest.Projects/Resources/ResourceEditService.cs` → `ResourceEditService`；方法 `Add/Rename/Remove/Undo`；工程树操作事务。
- `src/AutoTest.Projects/Profiles/DutAndStationProfiles.cs` → `DutProfile,StationProfileRef`；方法 `ResolveProfile/CheckVoltage`；型号配置与电源前置关系。
- `src/AutoTest.Projects/Profiles/ResourceBindingService.cs` → `ResourceBindingService`；方法 `Resolve/ValidateSnapshot`；逻辑端点与能力引用。
- `src/AutoTest.Projects/Storage/SchemaMigrationService.cs` → `SchemaMigrationService`；方法 `Preview/MigrateCopy`；版本迁移可预检与失败停机。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Workspace/WorkspaceManifest.cs`
- `HistoricalHOST:src/AutoTest.Workspace/FileAssetStore.cs`
- `HistoricalHOST:src/AutoTest.Workspace/AssetDocumentCodec.cs`
- `HistoricalHOST:src/AutoTest.Resources.Runtime/CapabilityBindingResolver.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Infrastructure/ResourceRemovalDependencyInspector.cs`

**仍需通过实验关闭：** 当前提议 project.json + assets/ 按类型归档、记录存独立记录库；最终格式需要一次Mock工程往返检验与真实UI试用，但基础资源身份不变。

### B2｜DBC解析、信号定义与数值转换

**工程：** `AutoTest.Projects`；**G2：** G2-03/05/06；ICD-06/12

以 DBC 资源修订为唯一信号定义来源，解析帧ID、IDE、位序、字节序、缩放、偏移、signed、枚举/多路复用；SignalRef 附带 DBC revision。编辑 DBC 的高级能力沿用旧研究但不阻碍首版只读导入与查询。

**主要调用链**：资源目录→DBC parse→SignalRef→CAN帧→数值解码和单位→CAN/波形/测试订阅；工程内 DBC 编辑要产生新版本并核查受影响步骤。

**状态/异常**：Parse: Valid/Warnings/Invalid；Signal: Fresh/Stale/Unavailable/Lossy；错误信号不允许默认0合格。 错误位序、数据长度、未定义信号、过期修订/多路复用条件缺失必须明确；实际报文与 DBC 对不上不写成设备故障。

**设计文件 7 项：**

- `src/AutoTest.Projects/Dbc/DbcSource.cs` → `DbcSource`；方法 `Load/Hash`；保留源文件与编码诊断。
- `src/AutoTest.Projects/Dbc/DbcDatabase.cs` → `DbcDatabase`；方法 `FindMessage/FindSignal`；信号与报文不可变定义。
- `src/AutoTest.Projects/Dbc/DbcParserAdapter.cs` → `DbcParserAdapter`；方法 `Parse/TryParse`；DBC语义解析与错误位置。
- `src/AutoTest.Projects/Dbc/DbcSignalCodec.cs` → `DbcSignalCodec`；方法 `DecodeRaw/EncodeRaw/ToPhysical`；明确Intel/Motorola位序。
- `src/AutoTest.Projects/Dbc/DbcSignalResolver.cs` → `DbcSignalResolver`；方法 `Resolve(SignalRef)/DetectStale`；防修订漂移。
- `src/AutoTest.Projects/Dbc/DbcEditSession.cs` → `DbcEditSession`；方法 `Apply/Validate/Export`；高级编辑可演进的事务边界。
- `src/AutoTest.Projects/Dbc/DecodedSignalPublisher.cs` → `DecodedSignalPublisher`；方法 `OnCanFrame/ObserveSignal`；复用 CAN Hub 输入和质量标记。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Communication.Dbc/DbcDatabase.cs`
- `HistoricalHOST:src/AutoTest.Communication.Dbc/Authoring/DbcAuthoringSession.cs`
- `HistoricalHOST:src/AutoTest.Communication.Dbc/Authoring/DbcSourcePatchWriter.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Signals/QuickDbcSignalResolver.cs`

**仍需通过实验关闭：** 真实EPB DBC中电机电流的具体帧/信号和广播周期，需要导入用户提供的实际 DBC 并在 B2 实测。

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

### E1｜Honeywell USB 扫码输入与编号匹配

**工程：** `AutoTest.Specialists`；**G2：** G2-01/04/05/06；ICD-04/05

实物扫码枪 USB直连Windows PC，绝不从主控USB/CAN读取扫码。输入适配必须支持待判定的键盘模拟/虚拟串口/HID路径；采集只产生原始ScanEvent，解析与规则匹配是另两层。HOST不生成实物编号。

**主要调用链**：USB扫描→RawEvent→Normalization→RuleSet→MatchDecision→工位预检→用户确认/自动策略→RunSnapshot；运行期间新扫码只形成下次候选，不覆盖当前Run。

**状态/异常**：Idle→Capturing→Parsed→Matched/Ambiguous/NoMatch→Confirmed/Discarded；未验证的输入模式不能伪装成已自动识别。 扫码枪误在属性编辑焦点输入的防护；重复扫描不是历史冲突；编号解析冲突阻止生产启动；匹配修订入快照。

**设计文件 7 项：**

- `src/AutoTest.Specialists/Scan/BarcodeInputRouter.cs` → `BarcodeInputRouter`；方法 `AttachMode/PublishRaw`；统一输入事件源。
- `src/AutoTest.Specialists/Scan/KeyboardWedgeCapture.cs` → `KeyboardWedgeCapture`；方法 `OnKeyInput/CompleteScan`；键盘模拟模式可配置焦点安全。
- `src/AutoTest.Specialists/Scan/SerialBarcodeCapture.cs` → `SerialBarcodeCapture`；方法 `OpenAsync/ReadAsync`；仅实测为串口设备时启用。
- `src/AutoTest.Specialists/Scan/BarcodeScanEvent.cs` → `BarcodeScanEvent`；方法 `ValidateSource/WithTime`；Raw、来源、时间、质量。
- `src/AutoTest.Specialists/Scan/IdentityRuleSet.cs` → `IdentityRuleSet`；方法 `Validate/Match`；编号格式解析规则受控修订。
- `src/AutoTest.Specialists/Scan/ConfigMatchService.cs` → `ConfigMatchService`；方法 `Evaluate/RejectAmbiguous`；型号、CAN、测试与烧录资源关联。
- `src/AutoTest.Specialists/Scan/ScanDraftService.cs` → `ScanDraftService`；方法 `Capture/Confirm/Discard`；扫码草稿不得改写运行身份。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Workspace/WorkspaceModels.cs`
- `HistoricalHOST:src/AutoTest.Application/WorkspaceUseCases.cs`

**仍需通过实验关闭：** 扫码枪具体型号/USB枚举/HID键盘或COM模式尚未提供，必须借实际设备实验确定。

### E3｜波形采集、参考资产与判定

**工程：** `AutoTest.Specialists`；**G2：** G2-03/05/06；ICD-10/12

波形来自实际 CAN 电机电流信号（DBC定义），先建立原始报文和解码序列的证据；采集时基默认HOST接收时间，不虚构硬件采样。参考波形带单位、版本和标定条件；比较器插件化，旧src对齐/容差/匹配率算法为可验证候选。

**主要调用链**：CAN帧→DBC SignalRef→WaveCapture→(时间质量,样本,证据)→选参考资源→Compare→判定；手动工具和测试步骤共享同一数据模型。

**状态/异常**：Capture=Idle/Armed/Recording/Stopping/Complete/Incomplete；Comparison=NotEvaluated/Pass/Fail/Inconclusive。 丢帧/时基不明/单位不一致时禁止无证据PASS；算法输入越界、零采样、重叠不足必须Fail或Inconclusive；无法用软件ACK替代电机电流曲线。

**设计文件 9 项：**

- `src/AutoTest.Specialists/Wave/WaveCaptureService.cs` → `WaveCaptureService`；方法 `StartAsync/StopAsync/Observe`；订阅DBC信号、生成CaptureId。
- `src/AutoTest.Specialists/Wave/WaveSampleBuffer.cs` → `WaveSampleBuffer`；方法 `Append/Snapshot`；单调时间序列和容量限制。
- `src/AutoTest.Specialists/Wave/WaveTimeQuality.cs` → `WaveTimeQuality`；方法 `Describe/ValidateForJudgement`；采样间隔及丢帧质量。
- `src/AutoTest.Specialists/Wave/WaveReferenceRepository.cs` → `WaveReferenceRepository`；方法 `SaveReference/LoadReference`；参考资产版本与用途。
- `src/AutoTest.Specialists/Wave/WaveComparisonConfig.cs` → `WaveComparisonConfig`；方法 `ValidateThresholds`；容差/时间偏移/最小覆盖率。
- `src/AutoTest.Specialists/Wave/WaveComparator.cs` → `WaveComparator`；方法 `Align/Interpolate/Compare`；候选算法与可替换策略。
- `src/AutoTest.Specialists/Wave/WaveEvidenceCodec.cs` → `WaveEvidenceCodec`；方法 `Write/Read/Verify`；原始采样、配置、比较结果。
- `src/AutoTest.Specialists/Wave/WaveStepHandler.cs` → `WaveStepHandler`；方法 `ExecuteAsync`；供自动测试复用采集服务。
- `src/AutoTest.Desktop/Tools/WaveToolView.xaml` → `WaveToolView`；方法 `ShowChart/MarkReference/Compare`；独立波形工具与运行证据。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickTesting/Waveform/QuickWaveformComparator.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Waveform/QuickWaveformCaptureEngine.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Waveform/QuickReferenceWaveformCodec.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Controls/WaveformChart.cs`

**仍需通过实验关闭：** 实测波形参考门限、触发与实时刷新策略待真实ECU采样反馈；本模块不得提前冻结最终比较算法。

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

### R1｜运行事实、历史记录、查询与统计

**工程：** `AutoTest.Records`；**G2：** G2-04/05/06；ICD-10

每个 RunId 独立、不可覆盖；结果 Verdict 与记录提交状态分开；每步操作和证据带关联ID；生产统计区分 RunCount、DistinctDutCount、按最终完成情况统计。旧src已有SQLite与artifact研究，需检验恢复与一致性。

**主要调用链**：Runtime/Programming→AppendFacts→事务提交→CommitReceipt→History/Statistics；失败记录应当保留尝试事实，即使测试 Verdict 无法归档也不能显示已提交。

**状态/异常**：Record=Pending→Writing→Committed/Failed/Recovered；Evidence=Present/Missing/Corrupt。 SQLite写盘失败不报归档成功；两次同编号记录不得覆盖；存储缺证据需显示Unavailable；报告不可把ACK翻译成夹具到位。

**设计文件 8 项：**

- `src/AutoTest.Records/Model/RunRecord.cs` → `RunRecord,RunAttempt`；方法 `CreateFromSnapshot/Validate`；DUT、工程、软固件、测试计划指纹。
- `src/AutoTest.Records/Model/RunFact.cs` → `RunFact`；方法 `Append/ValidateSequence`；结构化步骤/操作/观测事实。
- `src/AutoTest.Records/Storage/RecordDatabase.cs` → `RecordDatabase`；方法 `Open/Migrate/CommitTransaction`；SQLite schema和事务。
- `src/AutoTest.Records/Storage/RunRecordWriter.cs` → `RunRecordWriter`；方法 `Begin/Append/CommitAsync`；追加写和提交回执。
- `src/AutoTest.Records/Storage/ArtifactRepository.cs` → `ArtifactRepository`；方法 `Put/Verify/Read`；波形、报文和烧录文件证据引用。
- `src/AutoTest.Records/Queries/RunHistoryQuery.cs` → `RunHistoryQuery`；方法 `FindByDut/FindByRun/Filter`；历史不可变只读查询。
- `src/AutoTest.Records/Queries/ProductionStatistics.cs` → `ProductionStatistics`；方法 `CountAttempts/CountDistinctDuts/GetSummary`；次数与不同ECU统计严格分离。
- `src/AutoTest.Records/Exports/RunReportExporter.cs` → `RunReportExporter`；方法 `ExportSummary/ExportStepFacts`；报告包含证据来源与Unknown。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Records.Sqlite/SqliteExecutionRecordStore.cs`
- `HistoricalHOST:src/AutoTest.Records/ExecutionRecordBuilder.cs`
- `HistoricalHOST:src/AutoTest.Records/FileArtifactStore.cs`

**仍需通过实验关闭：** 保留原始 CAN 与波形的容量、留存周期和压缩方案需实际样本测量后定，不编造政策。

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

文件/类/方法、接口、步骤类型、测试、证据和问题均在本次交付 CSV 中有稳定 ID。主索引：`tables/FILE_CLASS_FUNCTION_MATRIX.csv`。仅有正文但缺少对应表格或设计模块时，不能宣称该工作产品完成。

## Gate 限制

批准详细设计不等于通过对应单元测试、真实台架或完整生产烧录。
