# G4 v0.2 · 并行方向 `UDS` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**9项**；新增工程/测试责任：**1项**；关联验证：**6项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Specialists/Uds/IsoTpFrameCodec.cs` — IsoTpFrameCodec；关键方法：`EncodeSingle/ParseFirst/ParseConsecutive/ParseFlow`；标准帧类型和长度检查。
- `src/AutoTest.Specialists/Uds/IsoTpConversation.cs` — IsoTpConversation；关键方法：`SendPduAsync/ReceivePduAsync`；FC/BS/STmin/序号与超时。
- `src/AutoTest.Specialists/Uds/UdsEndpointProfile.cs` — UdsEndpointProfile；关键方法：`ValidateIds/ValidateAddressing`；CAN TX/RX ID及地址模式配置。
- `src/AutoTest.Specialists/Uds/UdsClient.cs` — UdsClient；关键方法：`RequestAsync/InterpretResponse`；正响应/NRC/P2*可配置。
- `src/AutoTest.Specialists/Uds/UdsServiceCatalog.cs` — UdsServiceCatalog；关键方法：`Register/GetRiskLevel`；服务参数与权限分级。
- `src/AutoTest.Specialists/Uds/UdsSessionService.cs` — UdsSessionService；关键方法：`StartSession/TesterPresentPolicy/EndSession`；会话生命周期、独占租约。
- `src/AutoTest.Specialists/Uds/UdsTrace.cs` — UdsTrace；关键方法：`AppendRequest/AppendResponse`；原始PDU/总线帧与错误可溯源。
- `src/AutoTest.Specialists/Uds/UdsStepHandler.cs` — UdsStepHandler；关键方法：`ExecuteAsync`；供测试步骤复用而非复制协议。
- `src/AutoTest.Desktop/Tools/UdsToolView.xaml` — UdsToolView；关键方法：`Send/ShowNrc/ShowSession`；独立原始请求响应窗口。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Specialists.Tests/IsoTpUdsTests.cs` — 模块测试代码 / V4；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.QuickDesktop/Uds/QuickUdsService.cs` → `src/AutoTest.Specialists/Uds/UdsClient.cs`；策略 `REFERENCE_ONLY`；必须验证：Not full UDS stack; use G3 service requirements and CanTool as reference。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `E2-T01` ISO-TP SF/FF/CF/FC所有类型 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`IsoTpUdsContractTests.Verify_E2_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E2-T02` 序号错/流控超时 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`IsoTpUdsContractTests.Verify_E2_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E2-T03` NRC0x78和NRC0x21 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`IsoTpUdsContractTests.Verify_E2_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E2-T04` 无响应和错误SID — `AUTO_UNIT_OR_MOCK`；候选类/程序：`IsoTpUdsContractTests.Verify_E2_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E2-T05` UDS真机非破坏性0x22 — `BENCH_MANUAL_OR_UI`；候选类/程序：`IsoTpUdsContractTests.Verify_E2_T05`；前置：B0+B2+只读UDS许可。
- `E2-T06` 工具与测试同时占用拒绝 — `AUTO_MOCK_PLUS_MANUAL`；候选类/程序：`IsoTpUdsContractTests.Verify_E2_T06`；前置：无实物许可；先Mock，现场行为依G3-08。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
