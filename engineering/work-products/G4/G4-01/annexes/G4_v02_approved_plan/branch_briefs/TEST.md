# G4 v0.2 · 并行方向 `TEST` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**18项**；新增工程/测试责任：**1项**；关联验证：**12项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Testing/Authoring/TestDocument.cs` — TestDocument,StepNode；关键方法：`Create/CloneSnapshot`；可编辑树，稳定StepId。
- `src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` — StepDescriptorCatalog；关键方法：`Register/Lookup/Enumerate`；类型/属性/资源要求/槽位元数据。
- `src/AutoTest.Testing/Authoring/StepEditTransaction.cs` — StepEditTransaction；关键方法：`Apply/Undo/Redo`；插入移动复制删除的原子操作。
- `src/AutoTest.Testing/Authoring/StepTreeValidator.cs` — StepTreeValidator；关键方法：`ValidateTree/ValidateSlots`；禁止循环引用与非法Else位置。
- `src/AutoTest.Testing/Authoring/TestDocumentCodec.cs` — TestDocumentCodec；关键方法：`Serialize/Deserialize/Migrate`；持久化与版本诊断。
- `src/AutoTest.Testing/Authoring/StepPropertyValidator.cs` — StepPropertyValidator；关键方法：`ValidateBinding/ValidateParameter`；属性面板编辑前验证。
- `src/AutoTest.Testing/Authoring/StepCatalogRegistration.cs` — StepCatalogRegistration；关键方法：`RegisterBuiltins`；全部工作包共用步骤注册入口。
- `src/AutoTest.Testing/Authoring/StepRefactorService.cs` — StepRefactorService；关键方法：`RenameAsset/RemapReference`；工程资源变化传播检查。
- `src/AutoTest.Testing/Preparation/PlanCompiler.cs` — PlanCompiler；关键方法：`PrepareAsync/ValidateDependencies`；冻结步骤图、绑定与诊断。
- `src/AutoTest.Testing/Preparation/PreparedPlan.cs` — PreparedPlan；关键方法：`ValidateFingerprint`；只读执行快照。
- `src/AutoTest.Testing/Runtime/RunCoordinator.cs` — RunCoordinator；关键方法：`StartAsync/RequestStop/PauseAtBoundary`；运行唯一状态拥有者。
- `src/AutoTest.Testing/Runtime/StepDispatcher.cs` — StepDispatcher；关键方法：`DispatchAsync`；按StepDescriptor查找Handler。
- `src/AutoTest.Testing/Runtime/StepHandlerRegistry.cs` — StepHandlerRegistry；关键方法：`Resolve/Register`；可替换Handler与版本合同。
- `src/AutoTest.Testing/Runtime/FlowExecutors.cs` — IfExecutor,LoopExecutor,ParallelExecutor；关键方法：`ExecuteAsync`；明确条件求值及有界循环。
- `src/AutoTest.Testing/Runtime/ResourceLeaseScope.cs` — ResourceLeaseScope；关键方法：`AcquireAsync/DisposeAsync`；操作结束释放资源租约。
- `src/AutoTest.Testing/Runtime/StepJudge.cs` — StepJudge；关键方法：`Evaluate/NoEvidence`；可追踪预期/实际与质量。
- `src/AutoTest.Testing/Runtime/RunFactJournal.cs` — RunFactJournal；关键方法：`Append/Publish`；运行事实追加且带OperationId。
- `src/AutoTest.Testing/Runtime/RunControlPolicy.cs` — RunControlPolicy；关键方法：`CanPause/CanSkip/CanStep`；不可中断副作用的安全边界。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Testing.Tests/AuthoringAndRuntimeTests.cs` — 模块测试代码 / V1；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.Testing.Authoring/AuthoringTransactionEngine.cs` → `src/AutoTest.Testing/Authoring/StepEditTransaction.cs`；策略 `PORT_ALGORITHM`；必须验证：Tree edit transactions, Undo/Redo; stable StepId and new document format。
- `src/AutoTest.Testing.Authoring/NodeTypeRegistry.cs` → `src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs`；策略 `ADAPT`；必须验证：Use approved 23 step types; no silent feature loss。
- `src/AutoTest.Testing.Authoring/UndoRedoHistory.cs` → `src/AutoTest.Testing/Authoring/StepEditTransaction.cs`；策略 `PORT_ALGORITHM`；必须验证：History semantics; batch edits and identity。
- `src/AutoTest.Testing.Authoring/TestProgramAssetCodec.cs` → `src/AutoTest.Testing/Authoring/TestDocumentCodec.cs`；策略 `ADAPT`；必须验证：Decode legacy test JSON through explicit import adapter。
- `src/AutoTest.Testing.Authoring/ProgramDiagnosticsService.cs` → `src/AutoTest.Testing/Authoring/StepTreeValidator.cs`；策略 `PORT_ALGORITHM`；必须验证：Broken refs, invalid nesting and diagnostic errors。
- `src/AutoTest.Testing.Runtime/Execution/RunExecutionKernel.cs` → `src/AutoTest.Testing/Runtime/FlowExecutors.cs`；策略 `PORT_ALGORITHM`；必须验证：Sequence/If/Loop/Subtest behavior; require snapshots, cancellation, fact sink。
- `src/AutoTest.Testing.Runtime/Execution/RunControlCoordinator.cs` → `src/AutoTest.Testing/Runtime/RunControlPolicy.cs`；策略 `ADAPT`；必须验证：Pause/Stop timing; actual schedule guarantees。
- `src/AutoTest.Testing.Runtime/Execution/RunCapabilityScope.cs` → `src/AutoTest.Testing/Runtime/ResourceLeaseScope.cs`；策略 `ADAPT`；必须验证：Capability lifetime/release; lease conflicts。
- `src/AutoTest.Testing.Runtime/Execution/RuntimeLeafDispatcher.cs` → `src/AutoTest.Testing/Runtime/StepDispatcher.cs`；策略 `ADAPT`；必须验证：Handler routing through Contracts, no device singleton。
- `src/AutoTest.Testing.Runtime/Preparation/L5RunPreparation.cs` → `src/AutoTest.Testing/Preparation/PlanCompiler.cs`；策略 `ADAPT`；必须验证：Preflight compile and pinned references; no mutable RunSnapshot。
- `src/AutoTest.QuickTesting/Runtime/QuickKeyActionHandler.cs` → `src/AutoTest.Testing/Runtime/StepHandlerRegistry.cs`；策略 `REFERENCE_ONLY`；必须验证：Old keypad semantics, ensure new Station/authorization boundary。
- `src/AutoTest.QuickTesting/Runtime/QuickPowerSetHandler.cs` → `src/AutoTest.Testing/Runtime/StepHandlerRegistry.cs`；策略 `REWRITE_NO_FALSE_CAPABILITY`；必须验证：Cannot recreate removed dut.power/keypad.power control。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `C1-T01` 右键插入/复制/删除/撤销 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`AuthoringTransactionContractTests.Verify_C1_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C1-T02` If/Else子槽位规则 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`AuthoringTransactionContractTests.Verify_C1_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C1-T03` Loop次数边界 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`AuthoringTransactionContractTests.Verify_C1_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C1-T04` Parallel与资源冲突前置 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`AuthoringTransactionContractTests.Verify_C1_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C1-T05` 重命名后的引用检查 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`AuthoringTransactionContractTests.Verify_C1_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C1-T06` 保存重开稳定ID — `AUTO_UNIT_OR_MOCK`；候选类/程序：`AuthoringTransactionContractTests.Verify_C1_T06`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C2-T01` 模拟PASS/FAIL/INCONCLUSIVE独立用例 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RuntimeContractTests.Verify_C2_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C2-T02` 运行中编辑快照不变 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RuntimeContractTests.Verify_C2_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C2-T03` 停止与暂停边界 — `AUTO_MOCK_PLUS_MANUAL`；候选类/程序：`RuntimeContractTests.Verify_C2_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C2-T04` Loop上限 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RuntimeContractTests.Verify_C2_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C2-T05` Parallel租约冲突 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RuntimeContractTests.Verify_C2_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `C2-T06` 资源更换后Plan失效 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RuntimeContractTests.Verify_C2_T06`；前置：无实物许可；先Mock，现场行为依G3-08。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
