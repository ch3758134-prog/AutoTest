# AutoTest｜TEST-RECORD TR-01 代码首批实施记录（2026-11-19）

- 分支：`feature/test-record-backend-20261119`
- 代码审查取样 HEAD：`1aba18e44d34c4633a1cf90cb457b6fdf29043e3`（本文写入后 HEAD 变化，以最终 Draft PR 为准）
- 对比基线：`HOST@ffb23f8c05ec4a64b82cd781db6357685d082c04`
- Issue：#64
- 管理合同：`Project_Management/docs/project-management/new-baseline-20261112/research/TEST-RECORD-BACKEND-SPECIALIST-20261119.md`
- 当前状态：**TR-01 PRIVATE_BACKEND_PARTIAL_IMPLEMENTED / SHARED_REVIEW_PENDING / WINDOWS_BUILD_NOT_RUN / HARDWARE_NOT_VERIFIED**
- **尚不可宣称真实 ECU→SQLite→正式报告闭环已接通**。

## 已经提交的实际代码

### TEST Authoring 和编译

- `HOST/src/AutoTest.Testing/Authoring/StepResultRecordingOptions.cs`：新增可配置附加结果合同。保留唯一 StepNode.Properties 和原 TEST JSON，键为 `result.capture`（None/Always/NonPass）、`result.extraVariables`（最多16个）、`result.requirementId`、`result.reportNote`、`result.showEvidence`。标准安全审计不受其关闭。
- `HOST/src/AutoTest.Testing/Preparation/PlanCompiler.cs`：编译之前验证配置内容，阻止无效录制选项随真实操作进入运行。

### TEST Runtime

- `HOST/src/AutoTest.Testing/Runtime/RealRunEventSink.cs`：新增 `RealStepStarted`、`IRealRunEventSink`，用于**等待式** StepBegin/StepComplete/RunComplete 事件；TEST 不直接依赖 RECORD。
- `HOST/src/AutoTest.Testing/Runtime/RealRunCoordinator.cs`：
  - 保留原 `StartAsync(plan, cancellation)` 兼容入口；
  - 新增 `StartAsync(plan, runId, events, cancellation)`，由上层在 RECORD Begin 前分配单一 RunId；
  - 真正执行每次节点创建独立 StepOccurrenceId，携带父身份、Phase、循环 1-based 迭代和路径；
  - 保存开始 UTC、结束 UTC、主机单调 Stopwatch 耗时；Read/WaitUntil/AssertSignal 保留原生 SignalObservation、TestSignalStatus（含 Unit、EvidenceRef、Quality、源时间）；
  - 可选捕获指定变量为 AdditionalResults，附加结果不会关闭标准判断与证据；
  - 发生 LostFrames 后即使以后观测到匹配值，也标记 SIGNAL_GAP/InvalidData，运行不能被误判 Passed；
  - 逐步事件回调异常会中断后续真实脚本操作，并进入原有清理路径。
- 没有修改 USB、固件或真实 CAN/DBC 端口。

### RECORD

- `HOST/src/AutoTest.Records/Model/RunFact.cs`：在原 `RunStepFact` 上添加**可选** occurrence、Parent、Phase、时间边界、时长、Unit、信号状态、Requirement、额外结果等，不为旧记录虚构 GUID。
- `HOST/src/AutoTest.Records/Storage/RecordDatabase.cs`：SQLite v1 的原 `run_step_facts` 不变，扩展原 `StepPayload` JSON；新 null 字段不写入，维持旧编码，继续使用不可变事件与 Hash 机制。
- `HOST/src/AutoTest.Records/Exports/RunReportExporter.cs`：HTML/JSON/CSV 展示 occurrence、信号测量与额外结果；HTML 遵循 showEvidence 的可见性，但审计事实不删除；新增 `ExportFilesAsync` 以临时文件＋刷新＋覆盖移动保存 HTML/JSON/CSV，并阻止 Writing 状态导出“最终报告”。

## 新增逻辑测试（只有源码，未执行）

- `HOST/tests/AutoTest.Testing.Tests/RealOccurrenceTests.cs`：Loop 同 StepId 多 occurrence、附加变量、错误配置编译拒绝、事件落盘拒绝阻止真实写入端口、原生 DBC 信号质量证据、LostFrames 禁止假 PASS。
- `HOST/tests/AutoTest.Records.Tests/StepOccurrenceReportTests.cs`：相同 StepId 两 occurrence SQLite 回读与 HTML/JSON/CSV、旧版事实无虚构身份、非法执行区间拒绝。

上述测试使用**内存测试桩或测试 DB**，只能证明代码契约；禁止代替现场 ECU 验收。

## 精确遗留接口：批准后 HOST/PROJECT 负责接线

1. **项目/运行身份**：PROJECT/HOST 提供合法且准确的 ProjectSnapshot、StationProfile、锁定资产、实际软件构建身份；单工位 Development 无扫码时的非生产身份需明确，不得编造归一化 DUT 身份。
2. **真实运行入口适配器**：HOST/INTEGRATION 审查后由 `RealTestWorkbenchService` 在物理写入前执行 `RunRecordWriter.BeginAsync`，以外部分配 RunId 调用已有 RealRunCoordinator 新重载；适配 `StepStartedAsync`→标准 RunFact，`StepCompletedAsync`→RunStepFact 与结构化真实测量/证据，结束用 `CompleteAsync(RunCompletion)` 而非不判断结果的 CommitAsync。
3. **实时耐中断数据**：现有 `RealRunEvidenceRecorder` 的 100000 条 RAM 尾部写 JSON 仍未替代为持续落盘；需要经 HOST/DEVICE 接线审查，将原 CAN/DBC/DEVICE 证据与 StepOccurrence/ArtifactRepository 建立强引用和丢失统计。崩溃 `RecoverInterruptedRunsAsync` 需要在 HOST 唯一进程启动时调用。
4. **报告落地/访问**：RECORD 已具备 ExportFilesAsync；HOST 应在数据库完成后调用并返回路径/状态；失败时保存具体错误且**不能**宣称报告已生成。UI 只消费后端事实与状态，不自行计算 Verdict。
5. **复杂故障**：当前 TEST 失败处理逻辑已有 Prepare gate、continue 与 cleanup；用户在编辑器定义的逐步 continue/retry 策略仍待专门审查，不能以报告改造为理由自行改变真实硬件操作。
6. **生产/扫码**：本包不将 Development PASS 提升为 Production PASS，不接管统计和出厂放行。

## 验证状态

| 检查 | 结果 |
| --- | --- |
| GitHub 目标分支提交并回读 | PASS（以实际 GitHub 文件为准） |
| 修改范围仅 TEST/RECORD 后端、专项单元测试和文档 | PASS（GitHub 差异审查） |
| C# 括号/大括号/中括号基础静态配对 | PASS（非编译检查） |
| Windows Restore/Build | **NOT_RUN**，本执行环境没有 dotnet SDK |
| TEST/RECORD 单元测试 | **NOT_RUN** |
| WPF 启动 | **NOT_RUN** |
| 真实 ECU/DBC 正向和负向运行、RunId/StepOccurrence/数据库与报告 | **NOT_VERIFIED** |
| 真正完成用户端编辑→真实 ECU→SQLite→报告闭环 | **NOT_YET_CONNECTED** |
| PM 共享接口审查 | **PENDING** |

本地 Codex 下一次验证应以 Draft PR **精确 HEAD** 在 Windows 对 HOST 解决方案进行 restore/build/test 并提交原始输出，不能使用模拟 PASS 代替真机验收。

## 本轮不做

不改 `main` / `HOST` 分支、不改主控/按键板/USB/真实 DEVICE、不重画 UI、不新建第二套数据库/测试引擎、不自行加入 PROJECT/共享 Contracts/生产放行合同。
