# G4 v0.2 · 并行方向 `WAVE` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**9项**；新增工程/测试责任：**1项**；关联验证：**6项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Specialists/Wave/WaveCaptureService.cs` — WaveCaptureService；关键方法：`StartAsync/StopAsync/Observe`；订阅DBC信号、生成CaptureId。
- `src/AutoTest.Specialists/Wave/WaveSampleBuffer.cs` — WaveSampleBuffer；关键方法：`Append/Snapshot`；单调时间序列和容量限制。
- `src/AutoTest.Specialists/Wave/WaveTimeQuality.cs` — WaveTimeQuality；关键方法：`Describe/ValidateForJudgement`；采样间隔及丢帧质量。
- `src/AutoTest.Specialists/Wave/WaveReferenceRepository.cs` — WaveReferenceRepository；关键方法：`SaveReference/LoadReference`；参考资产版本与用途。
- `src/AutoTest.Specialists/Wave/WaveComparisonConfig.cs` — WaveComparisonConfig；关键方法：`ValidateThresholds`；容差/时间偏移/最小覆盖率。
- `src/AutoTest.Specialists/Wave/WaveComparator.cs` — WaveComparator；关键方法：`Align/Interpolate/Compare`；候选算法与可替换策略。
- `src/AutoTest.Specialists/Wave/WaveEvidenceCodec.cs` — WaveEvidenceCodec；关键方法：`Write/Read/Verify`；原始采样、配置、比较结果。
- `src/AutoTest.Specialists/Wave/WaveStepHandler.cs` — WaveStepHandler；关键方法：`ExecuteAsync`；供自动测试复用采集服务。
- `src/AutoTest.Desktop/Tools/WaveToolView.xaml` — WaveToolView；关键方法：`ShowChart/MarkReference/Compare`；独立波形工具与运行证据。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Specialists.Tests/WaveComparatorTests.cs` — 模块测试代码 / V4；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.QuickTesting/Waveform/QuickWaveformComparator.cs` → `src/AutoTest.Specialists/Wave/WaveComparator.cs`；策略 `PORT_ALGORITHM`；必须验证：Alignment/tolerance; add time quality, missing samples and units。
- `src/AutoTest.QuickTesting/Waveform/QuickWaveformCaptureEngine.cs` → `src/AutoTest.Specialists/Wave/WaveCaptureService.cs`；策略 `ADAPT`；必须验证：Broadcast-based capture; single shared CAN source。
- `src/AutoTest.QuickTesting/Waveform/QuickWaveformEvidenceCodec.cs` → `src/AutoTest.Specialists/Wave/WaveEvidenceCodec.cs`；策略 `PORT_ALGORITHM`；必须验证：Wave evidence serialization and references。
- `src/AutoTest.QuickDesktop/Waveform/SignalCaptureArchive.cs` → `src/AutoTest.Specialists/Wave/WaveReferenceRepository.cs`；策略 `ADAPT`；必须验证：Reference archive reading and source version metadata。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `E3-T01` 参考与实际相同波形 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`WaveEvidenceContractTests.Verify_E3_T01`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E3-T02` 时间偏移容差 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`WaveEvidenceContractTests.Verify_E3_T02`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E3-T03` 采样缺失/乱序 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`WaveEvidenceContractTests.Verify_E3_T03`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E3-T04` 数据单位不匹配 — `AUTO_UNIT_OR_MOCK`；候选类/程序：`WaveEvidenceContractTests.Verify_E3_T04`；前置：无实物许可；先Mock，现场行为依G3-08。
- `E3-T05` 回放及真实CAN电流 — `BENCH_MANUAL_OR_UI`；候选类/程序：`WaveEvidenceContractTests.Verify_E3_T05`；前置：B0+B2+信号/参考版本。
- `E3-T06` 运行证据引用复核 — `AUTO_MOCK_PLUS_MANUAL`；候选类/程序：`WaveEvidenceContractTests.Verify_E3_T06`；前置：无实物许可；先Mock，现场行为依G3-08。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。
