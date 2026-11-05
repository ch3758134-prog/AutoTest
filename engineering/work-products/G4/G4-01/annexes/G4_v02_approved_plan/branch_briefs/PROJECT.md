# G4 v0.2 · 并行方向 `PROJECT` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**17项**；新增工程/测试责任：**1项**；关联验证：**11项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Projects/Project/ProjectDefinition.cs` — ProjectDefinition；关键方法：`Validate/WithRevision`；ProjectId、资源清单与格式版本。
- `src/AutoTest.Projects/Project/ProjectRepository.cs` — ProjectRepository；关键方法：`Create/OpenAsync/SaveAsync`；暂存写入+校验+原子替换或明确失败。
- `src/AutoTest.Projects/Project/ProjectPathPolicy.cs` — ProjectPathPolicy；关键方法：`Normalize/ValidateInsideRoot`；阻止资源路径越界。
- `src/AutoTest.Projects/Resources/AssetEntry.cs` — AssetEntry；关键方法：`Create/VerifyHash`；AssetId、Revision、Kind、SHA256。
- `src/AutoTest.Projects/Resources/AssetCatalog.cs` — AssetCatalog；关键方法：`Import/ReplaceRevision/Resolve`；统一资产身份不依赖文件名。
- `src/AutoTest.Projects/Resources/ReferenceValidator.cs` — ReferenceValidator；关键方法：`ScanDependencies/RejectDangling`；DBC、测试步骤与配置引用检查。
- `src/AutoTest.Projects/Resources/ResourceEditService.cs` — ResourceEditService；关键方法：`Add/Rename/Remove/Undo`；工程树操作事务。
- `src/AutoTest.Projects/Profiles/DutAndStationProfiles.cs` — DutProfile,StationProfileRef；关键方法：`ResolveProfile/CheckVoltage`；型号配置与电源前置关系。
- `src/AutoTest.Projects/Profiles/ResourceBindingService.cs` — ResourceBindingService；关键方法：`Resolve/ValidateSnapshot`；逻辑端点与能力引用。
- `src/AutoTest.Projects/Storage/SchemaMigrationService.cs` — SchemaMigrationService；关键方法：`Preview/MigrateCopy`；版本迁移可预检与失败停机。
- `src/AutoTest.Projects/Dbc/DbcSource.cs` — DbcSource；关键方法：`Load/Hash`；保留源文件与编码诊断。
- `src/AutoTest.Projects/Dbc/DbcDatabase.cs` — DbcDatabase；关键方法：`FindMessage/FindSignal`；信号与报文不可变定义。
- `src/AutoTest.Projects/Dbc/DbcParserAdapter.cs` — DbcParserAdapter；关键方法：`Parse/TryParse`；DBC语义解析与错误位置。
- `src/AutoTest.Projects/Dbc/DbcSignalCodec.cs` — DbcSignalCodec；关键方法：`DecodeRaw/EncodeRaw/ToPhysical`；明确Intel/Motorola位序。
- `src/AutoTest.Projects/Dbc/DbcSignalResolver.cs` — DbcSignalResolver；关键方法：`Resolve(SignalRef)/DetectStale`；防修订漂移。
- `src/AutoTest.Projects/Dbc/DbcEditSession.cs` — DbcEditSession；关键方法：`Apply/Validate/Export`；高级编辑可演进的事务边界。
- `src/AutoTest.Projects/Dbc/DecodedSignalPublisher.cs` — DecodedSignalPublisher；关键方法：`OnCanFrame/ObserveSignal`；复用 CAN Hub 输入和质量标记。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Projects.Tests/ProjectDbcRoundTripTests.cs` — 模块测试代码 / V1；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.Communication.Dbc/DbcDatabase.cs` → `src/AutoTest.Projects/Dbc/DbcDatabase.cs`；策略 `PORT_ALGORITHM`；必须验证：Parse/decode and diagnostics; parser vendor pinning/compatibility test。
- `src/AutoTest.Communication.Dbc/DbcContracts.cs` → `src/AutoTest.Projects/Dbc/DbcSignalCodec.cs`；策略 `ADAPT`；必须验证：Existing DBC field values and encoding types; do not duplicate Contracts DTO。
- `src/AutoTest.Communication.Dbc/DbcExporter.cs` → `src/AutoTest.Projects/Dbc/DbcEditSession.cs`；策略 `ADAPT`；必须验证：Roundtrip/export preservation; verify edited format。
- `src/AutoTest.Communication.Dbc/DbcSemanticComparer.cs` → `src/AutoTest.Projects/Dbc/DbcEditSession.cs`；策略 `PORT_ALGORITHM`；必须验证：Compare DBC revisions; stable SignalRef。
- `src/AutoTest.Communication.Dbc/Authoring/DbcAuthoringSession.cs` → `src/AutoTest.Projects/Dbc/DbcEditSession.cs`；策略 `ADAPT`；必须验证：Draft/edit validation and immutable versions。
- `src/AutoTest.Communication.Dbc/Authoring/DbcSourcePatchWriter.cs` → `src/AutoTest.Projects/Dbc/DbcEditSession.cs`；策略 `ADAPT`；必须验证：Minimal patch and round-trip behavior。
- `src/AutoTest.Communication.Dbc/Authoring/DbcAuthoringValidator.cs` → `src/AutoTest.Projects/Dbc/DbcEditSession.cs`；策略 `PORT_ALGORITHM`；必须验证：Reject malformed DBC edits; source-preserving behavior。
- `src/AutoTest.QuickDesktop/Dbc/DbcReferenceRemapper.cs` → `src/AutoTest.Projects/Dbc/DbcSignalResolver.cs`；策略 `ADAPT`；必须验证：After DBC revision, validate/remap references explicitly。
- `src/AutoTest.QuickTesting/Signals/QuickDbcSignalResolver.cs` → `src/AutoTest.Projects/Dbc/DbcSignalResolver.cs`；策略 `ADAPT`；必须验证：Broadcast signal resolution and quality propagation。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `B1-T01` 创建保存重开 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProjectRepositoryContractTests.Verify_B1_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B1-T02` 资源重命名引用不丢失 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProjectRepositoryContractTests.Verify_B1_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B1-T03` 删除被引用资源时阻止 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProjectRepositoryContractTests.Verify_B1_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B1-T04` 错误路径与损坏json — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProjectRepositoryContractTests.Verify_B1_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B1-T05` 两个revision区别 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProjectRepositoryContractTests.Verify_B1_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B1-T06` 离线工作 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProjectRepositoryContractTests.Verify_B1_T06`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B2-T01` 已知小端/大端字节序 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`DbcCodecContractTests.Verify_B2_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B2-T02` 有符号和比例 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`DbcCodecContractTests.Verify_B2_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B2-T03` 缺失帧不判PASS — `AUTO_UNIT_OR_MOCK`；候选类/程序：`DbcCodecContractTests.Verify_B2_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B2-T04` DBC编辑产生新修订 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`DbcCodecContractTests.Verify_B2_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `B2-T05` 真实EPB电流信号解码 — `BENCH_MANUAL_OR_UI`；候选类/程序：`DbcCodecContractTests.Verify_B2_T05`；前置：B0+B2。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
