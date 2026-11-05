# matrices/PUBLIC_TYPE_CLOSURE_REGISTER.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`f1117a0aa36dbebec3a87a63bc0fd9c41aedecac44e70119b27047126c81f1bf`

原始行数（含表头）：26

| 类型或类型族 | 拟定归属程序集 | 形式 | 需冻结成员/行为 | 来源身份 | 责任模块 | 阻断的问题 | 完成判据 | 现状 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| StationId | AutoTest.Core | struct | Value:string；不可空白 | G3原型 | G3-0 | 稳定强类型；不同ID不能互换 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| RunId | AutoTest.Core | struct | Value:Guid；禁止Empty | G3原型 | G3-0 | 唯一测试运行ID，不等于DUT ID | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| AssetId/AssetRevision/AssetRef | AutoTest.Core | value/reference | ID/revision/SHA256 | G3原型 | G3-0 | 引用需绑定内容哈希 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| OperationResult<T>/Outcome | AutoTest.Core | result | Accepted/Rejected/Uncertain；错误码、操作ID、证据refs | G3原型 | G3-0 | ACK ≠ 物理证据；不得吞错误 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| EvidenceQuality/TimeQuality | AutoTest.Core | enum(s) | 分离原始证据可信度与时钟源/精度 | G4需闭合 | G3-0 | 不要把时间源质量误当EvidenceQuality | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| CanFrame/CanObservation | AutoTest.Contracts | value message | role, IDE, ID, DLC, immutable payload, monotonic, UTC, source, loss | G4需闭合 | A3 | byte[]暴露可变；clone或ImmutableArray，观察身份明确 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| SessionInfo | AutoTest.Contracts | DTO | session generation, device ID/fw, transport, state | G4需闭合 | A1 | ConnectAsync返回类型；必须跟随generation隔离 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| StationCommand/Authorization/CommandReceipt | AutoTest.Contracts | command/result | action, scope, operation, ACK kind, observed evidence | G4需闭合 | A2 | 执行收据不能冒充夹具到位/IGN实测 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| StationProfile/BusRole/BusRoleMap | AutoTest.Contracts | profile | logical DutCan/KeypadCan与源端口映射版本 | G4需闭合 | A2 | 不可把固件CAN0/1直接暴露到HOST业务 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| CanSubscriptionSpec/CanTxRequest/CanTxReceipt | AutoTest.Contracts | requests | filter, backpressure, LeaseToken, timestamp | G4需闭合 | A3 | 单Hub、发送租约、源质量及loss | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| LeaseToken | AutoTest.Contracts | typed token | owner/resource, generation, expiry/release | G4需闭合 | A3 | 过期和竞争不能继续发帧 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| ProjectSnapshot/ProjectSaveRequest/SaveReceipt | AutoTest.Contracts | document DTO | schema, revision, assets, fingerprint | G4需闭合 | B1 | 原子存储、错误路径不损坏历史 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| SignalRef/SignalObservation | AutoTest.Contracts | DBC DTO | dbc revision, frame, signal, value, unit, quality | G4需闭合 | B2 | 丢帧/缺帧不判PASS | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| TestDocumentId/EditCommand/EditResult | AutoTest.Contracts | edit protocol | stable StepId, transaction, undo/redo, diagnostics | G4需闭合 | C1 | 不允许UI直改运行中snapshot | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| CompileRequest/CompileResult | AutoTest.Contracts | compile result | resources, immutable plan, diagnostics, referenced hash | G4需闭合 | C2 | 坏引用必须拒绝运行 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| PreparedPlan/RunSnapshot | AutoTest.Contracts | immutable plan | asset/profile/DBC/HEX versions & runtime evidence | G4需闭合 | C2 | 可放实现细节到Testing，但跨域使用的接口DTO在Contracts | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| RunControlRequest/RunState/StepState | AutoTest.Contracts | state contract | runId, requested transition, safe-stop result | G4需闭合 | C2 | Pause/Stop/Step/Skip状态边界有真实语义 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| BarcodeScanEvent/RawScan/InputPolicy | AutoTest.Contracts | input DTO | capture origin, raw, time, focus state | G4需闭合 | E1 | PC扫码输入不经过主控板USB | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| RuleRevision/ProjectRevision/MatchDecision | AutoTest.Contracts | matching DTO | zero/multi match => deny; identity bind | G4需闭合 | E1 | 冲突不允许默认选择 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| UdsRequest/UdsEndpointProfile/UdsExchangeResult | AutoTest.Contracts | UDS DTO | P2/P2*, addressing, NRC, raw frames, evidence | G4需闭合 | E2 | 诊断工具与测试步骤共用CanHub/租约 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| WaveSample/WaveReference/WaveVerdict | AutoTest.Contracts | wave DTO | units, time base, gaps, parameters, source sha | G4需闭合 | E3 | 波形只能依据实际电机电流广播或回放判定 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| ProgrammingRequest/Plan/Readiness/JobId | AutoTest.Contracts | flash DTO | target, image hash, authorization, flow step | G4需闭合 | E4 | 闪存/擦写必须专项批准，不允许自动推定安全 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| SafetyAuthorization | AutoTest.Contracts | token | explicit authorized ops, scope, identity, lifetime | G4需闭合 | A2/E4 | UI按钮/连接成功不能自动授权 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| RunFact/RecordWriteReceipt/RecordCommitReceipt | AutoTest.Contracts | record DTO | append-only, immutable runId/evidence refs, durable status | G4需闭合 | R1 | 不同ECU数量和检测次数分开 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
| UiSelection/RunProjection/StationProjection | AutoTest.Contracts | UI state DTO | selection != runtime highlight != device status | G4需闭合 | D2 | Prism VM仅投影调用，不拿设备句柄 | V0或对应模块合同可编译、正/负合同测试通过；版本哈希受控 | DESIGN_TO_COMPILE |
