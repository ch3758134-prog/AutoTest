# AutoTest R1｜真实台架 CAN 周期发送跨用例丢失事件

状态：**现场故障已报告；源码诊断与无声失效防护已提交；真实故障根因尚未由本台架证据确认；不准标为修复验收 PASS。**
实施限定：R1 直接提交；未修改 main；未执行本机编译、CI 或真实 CAN 实测。

## 1. 用户现场事实

用户实测：上一个用例启动的周期 CAN 在下一个用例不再出现，导致依赖此报文的 ECU 判定失败。此为真实台架失效反馈，优先级 P0。不能以已有的 `retainProjectActuation` 源码测试宣称真实连续发送可靠。

## 2. 已核实的运行和停止路径

- 用例编辑器“运行此步骤/运行此用例”调用 `RealTestWorkbenchService.ExecuteAsync`，未传 `projectSession`，`ownsStation=true`，结束后必经 `ConfiguredStationActionPort.CleanupAsync`，停止全部周期 worker。这是此前特意设计的独立授权作用域，**不支持多次分别点击跨用例保持**。
- “运行勾选范围”（只含非 FLASH 用例）和工程整组验证调用 `OpenProjectSessionAsync`，共享一个 `ConfiguredStationActionPort`，`RealRunCoordinator.StartAsync(...retainProjectActuation:true)` 不在普通用例结束清理该端口；项目作用域最终统一清理。
- 共享作用域内任何实际执行的 `CanStopPeriodic`（包括某个用例 Cleanup 内）仍会显式停止当前共享端口的全部周期任务。
- `durationMs>0` 的周期 sender 在指定时长后自行停止，`durationMs=0` 才是直到显式停止/作用域结束。请核对真实保存的 TEST 步骤属性，不能从 UI 显示误认。
- 同 CAN ID 再次周期启动会停止旧 worker 并替换 DATA，StopReason=`REPLACED`。
- USB 异常导致 `SEND_ERROR`；真实主控上 `0x50`-`0x53` 板端广播功能存在于主控固件源码，但**当前 TEST 未使用**；TEST 使用 Windows 定时 worker 反复通过 USB `0x40` 发送。

## 3. 本次落地的诊断和防护

1. `PeriodicCanTaskEvidence` 增加 `LastUsbAckUtc`，并锁定历史快照，避免运行 UI 与后台新增任务并发遍历证据表。
2. `ConfiguredStationActionPort.CheckHealthAsync` 在真实运行边界检查无时长上限的活动 sender，若距最近 USB ACK 已超过 `max(3000ms, 3×periodMs)`，阻止继续按有效 ECU 环境做后续判定。此阈值是**严重停滞诊断**而不是 65ms 周期允收阈值。
3. `ProjectExecutionSession.ExecuteCaseAsync` 在每个 TEST 用例边界保存 `PeriodicCaseBoundaries`：开始前活动任务、结束后活动任务、该用例期间停掉的任务（含本用例中启动又停止的），保留 StopReason / SourceCaseId / SourceStepId / USB ACK / 时长等；记录到原项目父运行 JSON。
4. 工程整组验证运行界面和开发“运行勾选范围”逐用例显示当前活动周期任务数量与停止 ID/原因，避免隐藏“上一用例 Cleanup/定时停止”的真实事实。
5. 此次**未**将当前 TEST 默默改用固件 `0x50`：固件是最多 8 条/整体替换帧表的模型，物理授权与失联停止策略尚未完成；未经验证切换可能产生更危险的残留周期任务。

## 4. 单次 Windows 实测必须收集的证据

- 确定是“运行此用例”逐个点击，还是“工程整组验证/运行勾选范围”一次启动；两种行为不同。
- 上一用例发送步骤的 `periodMs`、`durationMs`、CAN ID、DATA；上一用例 Cleanup 是否包含 `CanStopPeriodic`；后续用例是否重新设置同 ID。
- 整组运行时查看工程目录 `test-records/project-runs/<ProjectRunId>.json` 中 `PeriodicCaseBoundaries` 与 `PeriodicCan`；停发原因区别为 `DURATION_ELAPSED` / `EXPLICIT_STOP_ALL` / `REPLACED` / `SEND_ERROR` / `PROJECT_OR_CASE_SCOPE_ENDED`。
- 独立 CAN 分析仪监听 MCU ECU CAN 外部 A12(H)/A1(L)，跨测试阶段记录总线真实接收的报文时间戳。USB ACK **不能**证明 DUT 收到或实际 65ms 周期精度。

## 5. 最小受控复现

在同一真实有序项目内，用例 1：Write 0x160 / periodMs=65 / durationMs=0，Wait 500ms；用例 2：Wait 1000ms + 基于 ECU 实际响应信号的判定；用例 1 Cleanup 不放显式 Stop；项目收尾才显式停。必须**一次**启动整组，而不是分别运行两个独立用例。用独立总线记录对照边界时间线，读取项目 JSON 和 UI StopReason。不能将两用例之间无 USB ACK、驱动确认但无 CAN 实发、接收丢帧或判定 NoData 判为通过。

## 6. 仍待解决的产品级设计

- 固定整车环境 CAN 应优先评估主控 MCU 自主广播，以降低 Windows 与 USB 往返抖动，但须先严格设计增删、原子整表更新、任务所有权/白名单、主动与异常停止、物理失联保护。
- 独立“运行此用例”与项目连续调试的入口应在 UI 上明确区分，不能让操作者误认为两次独立授权自动共享物理周期任务。
- 需要独立 CAN 分析仪完整验证连续性、间隔和丢帧，以及异常停止后 ECU 状态；在此之前生产准出仍为 NO-GO。

## 7. 后续已补的直接源代码整改（同一次事件）

- 修复开发“运行勾选范围”入口原来跳过已配置项目准备/收尾的问题。非 FLASH 选中用例现按“项目准备 → 有序勾选用例 → 项目收尾”创建同一个物理项目 Scope；项目阶段不可从固定资源解析时阻断，不允许偷偷略过。
- 将开发选中范围的固定顺序核验改为对照项目完整 `OrderedExecutions()`，支持开始和结束阶段的独立 TEST 记录，保持“选中仅为开发调试、不另存第二套生产方案”的产品边界。
- 补上“上一用例结束时活动的无限时长周期任务，在下一用例开始前必须仍活动”的严格检查；发现缺失就直接中止后续 ECU 判定，报告 CAN ID 与已知停止原因。
- 增强周期发送健康检测：包括当前无新 USB ACK 及曾经出现超过宽松阈值的严重 USB ACK 间断。此诊断不代表 ECU 总线 65ms 时序合格。
- TEST 编辑器独立运行用例/步骤后明确标示“周期 CAN 已随独立作用域清理”，不再让其表现为跨用例可继承。
- 以上为源码静态实施，未执行 Windows 编译、单测、Keil 编译、CI 或台架连续发帧验收。**真实 CAN 是否恢复、硬件固件是否为新版、用户原始测试工程是否有定时/显式 STOP 仍须实测判定。**
