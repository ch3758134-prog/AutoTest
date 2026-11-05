# G4 v0.2 · 并行方向 `SCAN` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**7项**；新增工程/测试责任：**1项**；关联验证：**6项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Specialists/Scan/BarcodeInputRouter.cs` — BarcodeInputRouter；关键方法：`AttachMode/PublishRaw`；统一输入事件源。
- `src/AutoTest.Specialists/Scan/KeyboardWedgeCapture.cs` — KeyboardWedgeCapture；关键方法：`OnKeyInput/CompleteScan`；键盘模拟模式可配置焦点安全。
- `src/AutoTest.Specialists/Scan/SerialBarcodeCapture.cs` — SerialBarcodeCapture；关键方法：`OpenAsync/ReadAsync`；仅实测为串口设备时启用。
- `src/AutoTest.Specialists/Scan/BarcodeScanEvent.cs` — BarcodeScanEvent；关键方法：`ValidateSource/WithTime`；Raw、来源、时间、质量。
- `src/AutoTest.Specialists/Scan/IdentityRuleSet.cs` — IdentityRuleSet；关键方法：`Validate/Match`；编号格式解析规则受控修订。
- `src/AutoTest.Specialists/Scan/ConfigMatchService.cs` — ConfigMatchService；关键方法：`Evaluate/RejectAmbiguous`；型号、CAN、测试与烧录资源关联。
- `src/AutoTest.Specialists/Scan/ScanDraftService.cs` — ScanDraftService；关键方法：`Capture/Confirm/Discard`；扫码草稿不得改写运行身份。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Specialists.Tests/BarcodeRuleTests.cs` — 模块测试代码 / V4；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

此方向暂未指定可直接复用的旧源码；须遵循G3和当前公共合同，不凭记忆假设旧代码。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `E1-T01` 记事本是否出字符串+回车实测 — `BENCH_MANUAL_OR_UI`；候选类/程序：`BarcodeRuleContractTests.Verify_E1_T01`；前置：扫码焦点专项。
- `E1-T02` 乱焦点防护 — `AUTO_MOCK_PLUS_MANUAL`；候选类/程序：`BarcodeRuleContractTests.Verify_E1_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E1-T03` 短码与空码 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`BarcodeRuleContractTests.Verify_E1_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E1-T04` 同一DUT重复扫描 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`BarcodeRuleContractTests.Verify_E1_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E1-T05` 两条匹配规则歧义 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`BarcodeRuleContractTests.Verify_E1_T05`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E1-T06` 运行中再次扫码不改身份 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`BarcodeRuleContractTests.Verify_E1_T06`；前置：无实物许可；先Mock，现场行为依G3-08。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
