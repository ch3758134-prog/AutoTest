# AutoTest R1｜P0 TEST 项目连续执行实施与 HydEPB 实测交接

实施基线：R1@0f98b6732d88fb09948758e6bf7688cd2dcf5c3d
实施分支：仅 R1。main 未修改。
状态：源码已提交；未编译、未执行自动化测试、未进行 Windows 或真实 ECU 验证。生产 Gate 保持 BLOCKED。

## 1. 项目级执行合同

一个有序 TEST 项目在已完成前置 FLASH 后只申请一次受限真实 USB 物理授权，并为本次执行分配 ProjectRunId。项目所有用例共享唯一 ConfiguredStationActionPort，周期 CAN、实际 KEY_SIM 请求状态及 IGN 输出请求随项目会话保持。每个用例仍通过原 PlanCompiler、RealRunCoordinator 和 RealTestRecordSession 独立执行，保有自己的 Prepare、Test、脚本 Cleanup、RunId、CaseAttemptId、StepOccurrence 与实际测量证据。跨用例不偷偷共享脚本变量。

项目用例的脚本 Cleanup 仍真实执行，但不会隐式释放项目周期任务。正常结束、STOP、设备故障或资源版本变更，唯一项目 Scope 负责统一停止周期 CAN 并撤销 PhysicalCommandFence TEST 授权；不自动释放 KEY，也不隐式修改 IGN。外层再执行工程明确配置的收尾动作。单独执行用例或步骤则仍使用原有独立 Scope，结束即清理临时周期任务。

## 2. 物理许可和资源冻结

OpenProjectSessionAsync 在第一条 TEST 物理写入前，检查真实 USB 连接世代、CAN 波特率回读、完整有序 TEST 用例、每条用例引用的固定资源和物理 Write 格式。授权指纹包括本次 ProjectRunId、DUT 身份、工程 SHA、USB epoch 及有序用例指纹；允许的 CAN role/ID/标准-扩展帧和 IGN 权限只来自这些已冻结用例，续期不扩大白名单。每进入下一用例前重新读取实际工程清单哈希，复核完整资源闭包。USB 连接世代变化会取消正在进行的运行。

独立 FLASH 工艺在 TEST Scope 之前执行；项目中嵌入互斥 FLASH 步骤会被预检阻断，不允许偷偷重授权。此前 USB/CAN/UDS/FLASH 实现保留。正式生产实体 START/Inspection 尚未接通，不得冒充正式生产 PASS。

## 3. 周期 CAN 生命周期与证据

单发 periodMs=0；持续 periodMs>0 且 durationMs=0；定时 periodMs>0 且 durationMs>0。相同通道/帧格式/CAN ID 再次启动时先停止旧 worker，再等待新 DATA 首帧真实 USB ACK 后启动新 worker，旧数据不得继续在软件后台调度。显式 CanStopPeriodic 作用于当前项目作用域全部任务；用例切换不隐式停发。

PeriodicCanTaskEvidence 保存 TaskId、CAN role/ID/扩展格式/DLC/DATA、目标周期/时长、创建用例/StepId/StepOccurrenceId、开始和停止时间/原因、主机请求数、USB ACK 次数、最长主机 ACK 间隔、错过周期数及异常。记录归档在工程根目录/test-records/project-runs/<ProjectRunId>.json，父记录关联各 CaseRunId、CaseAttemptId 和独立报告路径。

USB ACK 只证明主控接受请求，不是 ECU 接收帧或 CAN 总线实测周期。真实 65ms 必须使用独立 CAN 分析仪采样并依 ECU 规范判定，禁止把最大 USB ACK 间隔作为实际 CAN 线上周期。TEST 编辑器取消新建周期报文默认 5 秒定时结束的设计。

## 4. 结果和 RECORD

动作型用例可以产生 CompletedNoAssertion；只有原始证据完整、无丢失和关联损坏时，RECORD 才能归档 RunState.Completed + Verdict.Inconclusive + TEST_ACTIONS_COMPLETED_WITHOUT_PRODUCT_ASSERTION。它不等于 ECU PASS。每次有产品断言的用例必须保留自己真实判定结果。普通功能 FAIL 在相应 RECORD 中保留且可以继续后续诊断；PrepareGate、脚本 Cleanup 失败、NoData/Inconclusive、Abort、设备错误及物理授权状态不明禁止跨用例继续。整组已有 FAIL 时最终记录 FINISHED_WITH_FUNCTIONAL_FAILURE，后续 PASS 不能覆盖。

## 5. 核心修改与回归源代码

修改：RealTestWorkbenchService.cs 和新 Project 部分、RealRunCoordinator.cs、ConfiguredStationActionPort.cs 及新 PeriodicEvidence 部分、RealTestRecordSession.cs、AutomaticTestHomeViewModel.Run.cs 和 .cs、TestStepInspector.xaml/.cs、StepUiCatalog.cs、EngineeringVerificationWindow.xaml。新测试：ContinuousProjectRunTests.cs；修改 PhysicalTestRunFenceTests.cs。

回归源码覆盖项目作用域不执行用例级物理清理、独立用例结束仍会清理、无产品断言不伪 PASS、项目白名单两 ID 受限授权和续期不可扩大范围；未执行测试。连续周期发送替换/STOP、真实 USB 故障等必须由受控工位进行额外测试，不能以该源码测试替代。

## 6. HydEPB 必验顺序

1. Windows R1 加载固定的 HydEPB 工程，确认唯一有序 TEST 项目按准备、静态驻车、静态释放执行，复核 DBC/波形/固件/安全算法和工位夹具动作配置。
2. 实际连接主控 USB、确认 ECU CAN 码率回读和真实 COM 扫码，接独立 CAN 分析仪监听 ECU CAN 的 A1/A12。
3. 在工程整组实测窗口由工程师明确确认真实擦写操作；先完成前置 FLASH 和 APP 后验，然后启动唯一 ProjectRunId。
4. 用例 01 启动两条不同 ID、periodMs=65、durationMs=0 的周期 CAN；动作型用例 RECORD 应仅标记执行完成无产品判定。
5. 在用例 01→02、02→03 的边界，用外部分析仪确认两条 CAN 在实际总线持续发送，没有隐藏停止、重复并发或大间断。用例 02 驻车与用例 03 释放分别执行真实 KEY 动作并以独立 CAN/DBC 观测断言。
6. 分别测试同 ID DATA 更新、定时到期、显式 CanStopPeriodic、普通功能 FAIL 继续但整组保持 FAIL、准备失败阻断、STOP、主控断线、周期 worker 错误、工程外部变更及再次整组启动。
7. 对照项目父记录、每条用例 RunId、CaseAttemptId 和 StepOccurrence，以及外部分析仪记录；全部后台 worker 在整组结束时必须停止。65ms 的允收窗口须依据 ECU 工艺要求确定，不预设人为容差。

## 7. 尚未验收或未覆盖

Windows 编译 NOT_RUN；自动化测试执行 NOT_RUN；实际 CAN 65ms 验证 NOT_TESTED；HydEPB 驻车/释放 NOT_TESTED；正式生产 START/Inspection BLOCKED。当前完整项目工程验证使用连续 Scope；开发临时勾选多用例入口尚未逐一改线，接口可接受任意冻结的有序子集。独立 FLASH 用例在连续 TEST Scope 中明确拒绝。大流量证据缓冲达到限制时必须判定 RECORD 不完整，不得宣称 PASS。
