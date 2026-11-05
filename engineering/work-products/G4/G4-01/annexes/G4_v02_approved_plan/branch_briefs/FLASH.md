# G4 v0.2 · 并行方向 `FLASH` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**10项**；新增工程/测试责任：**1项**；关联验证：**7项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Specialists/Flash/FirmwareImageParser.cs` — FirmwareImageParser；关键方法：`ParseHex/ParseS19/ValidateSegments`；分段镜像、校验与重叠检测。
- `src/AutoTest.Specialists/Flash/ProgrammingProfile.cs` — ProgrammingProfile；关键方法：`Validate/DescribeTarget`；目标/请求ID/块大小/安全策略/超时。
- `src/AutoTest.Specialists/Flash/ProgrammingPreflight.cs` — ProgrammingPreflight；关键方法：`CheckAsync/RejectMismatch`；ECU身份/文件/供电/会话/权限。
- `src/AutoTest.Specialists/Flash/IProgrammingWorkflow.cs` — IProgrammingWorkflow；关键方法：`BuildPlan/ExecuteAsync`；ECU专用阶段序列可注入。
- `src/AutoTest.Specialists/Flash/ProgrammingJobCoordinator.cs` — ProgrammingJobCoordinator；关键方法：`StartAsync/RequestCancel/Observe`；唯一Job状态所有者。
- `src/AutoTest.Specialists/Flash/ProgrammingStageHandlers.cs` — ProgrammingStageHandlers；关键方法：`EnterSession/Transfer/Verify/Reset`；按profile授权调UDS。
- `src/AutoTest.Specialists/Flash/FlashTransferScheduler.cs` — FlashTransferScheduler；关键方法：`WriteBlocks/CheckResponse`；分块、NRC、进度与失败记录。
- `src/AutoTest.Specialists/Flash/ProgrammingEvidence.cs` — ProgrammingEvidence；关键方法：`RecordStage/Finalize`；每次尝试/失败阶段可追溯。
- `src/AutoTest.Specialists/Flash/MockBootloader.cs` — MockBootloader；关键方法：`ReceiveRequest/SimulateNrc`；可注入掉电/延迟/错误的ECU仿真。
- `src/AutoTest.Desktop/Tools/FlashToolView.xaml` — FlashToolView；关键方法：`SelectProfile/Preflight/ShowStages`；独立烧录工具共享服务。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Specialists.Tests/FirmwareImageAndFlashMockTests.cs` — 模块测试代码 / V5；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

此方向暂未指定可直接复用的旧源码；须遵循G3和当前公共合同，不凭记忆假设旧代码。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `E4-T01` IntelHEX/S19边界地址与校验 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProgrammingMockContractTests.Verify_E4_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E4-T02` 多段重叠/空文件 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProgrammingMockContractTests.Verify_E4_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E4-T03` 错误型号和电压 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProgrammingMockContractTests.Verify_E4_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E4-T04` Mock完整烧录 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProgrammingMockContractTests.Verify_E4_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E4-T05` 中间NRC/掉线/取消 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProgrammingMockContractTests.Verify_E4_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E4-T06` 不一致镜像禁止启动 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`ProgrammingMockContractTests.Verify_E4_T06`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E4-T07` 真实刷写需专项审核 — `SPECIAL_APPROVAL_ONLY`；候选类/程序：`ProgrammingMockContractTests.Verify_E4_T07`；前置：实际ECU刷写独立批准。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
