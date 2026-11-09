# G4 v0.2 · 并行方向 `PRODUCTION` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**7项**；新增工程/测试责任：**1项**；关联验证：**6项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Application/Production/ProductionSession.cs` — ProductionSession；关键方法：`StartDraft/Confirm/Reset`；生产作业身份边界。
- `src/AutoTest.Application/Production/ProductionPreflight.cs` — ProductionPreflight；关键方法：`ValidateMatch/ValidateStation`；资源、目标、权限和电压检查。
- `src/AutoTest.Application/Production/ProductionWorkflow.cs` — ProductionWorkflow；关键方法：`Prepare/ExecuteAsync`；组合FlashJob和TestRun而不重复实现。
- `src/AutoTest.Application/Production/ProductionResultCoordinator.cs` — ProductionResultCoordinator；关键方法：`FinalizeAsync/RequireCommit`；检查Run/Flash/Record完成状态。
- `src/AutoTest.Application/Production/ProductionStatisticsProjection.cs` — ProductionStatisticsProjection；关键方法：`Refresh/Filter`；读取记录域统计不可私存第二份。
- `src/AutoTest.Application/Production/ProductionCommandHandler.cs` — ProductionCommandHandler；关键方法：`Scan/Start/Stop/RetryWithNewRun`；命令权限和状态反馈。
- `src/AutoTest.Desktop/Production/ProductionViewModel.cs` — ProductionViewModel；关键方法：`CaptureScan/StartWork/ShowResult`；可见扫码/匹配/进度/统计。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Application.Tests/ProductionOrchestrationTests.cs` — 模块测试代码 / V5；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.QuickDesktop/TestPlans/BatchRecordStore.cs` → `src/AutoTest.Application/Production/ProductionResultCoordinator.cs`；策略 `REFERENCE_ONLY`；必须验证：Historical batch behavior; avoid duplicate Record store。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `P1-T01` 扫码到匹配Mock — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProductionWorkflowContractTests.Verify_P1_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `P1-T02` 错误型号/不匹配 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProductionWorkflowContractTests.Verify_P1_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `P1-T03` 同DUT复测新RunId — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProductionWorkflowContractTests.Verify_P1_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `P1-T04` 生产统计校验 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProductionWorkflowContractTests.Verify_P1_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `P1-T05` 无刷写许可流程拒绝 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProductionWorkflowContractTests.Verify_P1_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `P1-T06` 完整H4真实演练另行授权 — `BENCH_MANUAL_OR_UI`；候选类/程序：`ProductionWorkflowContractTests.Verify_P1_T06`；前置：H4流程演练独立批准。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
