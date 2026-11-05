# G4 v0.2 · 并行方向 `RECORD` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**8项**；新增工程/测试责任：**1项**；关联验证：**6项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Records/Model/RunRecord.cs` — RunRecord,RunAttempt；关键方法：`CreateFromSnapshot/Validate`；DUT、工程、软固件、测试计划指纹。
- `src/AutoTest.Records/Model/RunFact.cs` — RunFact；关键方法：`Append/ValidateSequence`；结构化步骤/操作/观测事实。
- `src/AutoTest.Records/Storage/RecordDatabase.cs` — RecordDatabase；关键方法：`Open/Migrate/CommitTransaction`；SQLite schema和事务。
- `src/AutoTest.Records/Storage/RunRecordWriter.cs` — RunRecordWriter；关键方法：`Begin/Append/CommitAsync`；追加写和提交回执。
- `src/AutoTest.Records/Storage/ArtifactRepository.cs` — ArtifactRepository；关键方法：`Put/Verify/Read`；波形、报文和烧录文件证据引用。
- `src/AutoTest.Records/Queries/RunHistoryQuery.cs` — RunHistoryQuery；关键方法：`FindByDut/FindByRun/Filter`；历史不可变只读查询。
- `src/AutoTest.Records/Queries/ProductionStatistics.cs` — ProductionStatistics；关键方法：`CountAttempts/CountDistinctDuts/GetSummary`；次数与不同ECU统计严格分离。
- `src/AutoTest.Records/Exports/RunReportExporter.cs` — RunReportExporter；关键方法：`ExportSummary/ExportStepFacts`；报告包含证据来源与Unknown。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Records.Tests/RecordAppendTests.cs` — 模块测试代码 / V3；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.Records.Sqlite/SqliteExecutionRecordStore.cs` → `src/AutoTest.Records/Storage/RecordDatabase.cs`；策略 `ADAPT`；必须验证：SQLite schema/read/query; append-only and failure preservation。
- `src/AutoTest.Records/ExecutionRecordBuilder.cs` → `src/AutoTest.Records/Storage/RunRecordWriter.cs`；策略 `ADAPT`；必须验证：Fact-to-record transformation; preserve run identity and failure。
- `src/AutoTest.Records/FileArtifactStore.cs` → `src/AutoTest.Records/Storage/ArtifactRepository.cs`；策略 `ADAPT`；必须验证：Evidence artifact hash and immutable references。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `R1-T01` ECU-A两次ECU-B一次=3次/2台 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RunRecordContractTests.Verify_R1_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `R1-T02` 失败Run保留 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RunRecordContractTests.Verify_R1_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `R1-T03` 事务中断恢复 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RunRecordContractTests.Verify_R1_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `R1-T04` 产线统计筛选口径 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RunRecordContractTests.Verify_R1_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `R1-T05` 引用损坏证据识别 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`RunRecordContractTests.Verify_R1_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `R1-T06` RunSnapshot与记录一致 — `AUTO_MOCK_PLUS_MANUAL`；候选类/程序：`RunRecordContractTests.Verify_R1_T06`；前置：无实物许可；先Mock，现场行为依G3-08。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
