# G2-04｜状态归属、运行控制与副作用（架构候审 v0.7）

## 状态拥有者注册表

详见 `tables/STATE_OWNERS.csv`。核心规则：**状态的写入者唯一；跨模块使用只读投影或命令/事件，不通过共享 ViewModel 可变对象同步。**

| 状态 | 所有者 | 永远不能混同 |
|---|---|---|
| DeviceSession | A-SESSION | USB连接 ≠ 硬件输出可控 |
| StationCapability | A-STATION | 电磁阀ACK ≠ 夹具到位 |
| Project/TestDocument | B-PROJECT/C-AUTHOR | 编辑文档 ≠ 执行快照 |
| Run | C-RUNTIME | 请求停止 ≠ 不可中断副作用已经停止 |
| ScanDraft/MatchDecision | E-SCAN | 新扫码候选 ≠ 当前 DUT 运行身份 |
| ProgrammingJob | E-FLASH | 发送完成 ≠ ECU 校验成功 |
| RunRecord | R-RECORD | 计算结果 PASS ≠ 记录已成功持久化 |
| Workbench | D-SHELL | 选中步骤 ≠ 正在执行的步骤 |

## 运行阶段的最小状态合同

`Preparing → Ready → Running → Finalizing → Finished/Failed/Interrupted`。在 Running 期间可有 `PausePending/Paused/StopPending`，但是否允许暂停与取消由步骤声明的 `Interruptibility` 和真实物理效果决定。G3 每类步骤必须提供副作用边界与失败清理计划，不能要求硬件立刻暂停不可中断的 ECU 操作。

**失联处理**：USB 世代变更后，旧世代的请求不可错误结算。未确认的输出状态变为 `Unknown`；恢复连接前重新执行能力和工位前置检查。不能沿用上次 UI 的绿色状态。

## 对外结果分类

- `Accepted/Rejected`：应用是否接受该请求；不表示硬件动作完成。
- `Acknowledged/TimedOut/TransportLost`：通信和固件确认事实。
- `Observed/Unknown/Invalid`：源数据和物理观测事实，可有质量标记。
- `Passed/Failed/Inconclusive/Interrupted`：依据测试程序定义的判定结论；`Inconclusive` 不能被默认计作 PASS。
- `Committed/Incomplete`：历史记录的持久化状态，不随步骤判定自动变成完整。

## G3 接收

明确枚举、错误码、异常与重试语义及具体交互时间关系，使用 Mock 测试验证：断连→Unknown、运行中编辑→不污染快照、暂停不可中断操作→Pending、写盘失败→Incomplete。

## 注册表B｜状态写入者与不变量

| 状态对象 | 唯一所有者 | 候选状态转移 | 关键不变量 |
| --- | --- | --- | --- |
| DeviceSession | A-SESSION | Detached → Connecting → Connected → Degraded/Detached | 连接世代与未完成请求身份不能串代 |
| StationCapability | A-STATION | Unknown → Identified → Ready/Restricted → Unknown | 主控连接成功≠实际输出安全许可 |
| ProjectDocument | B-PROJECT | Closed → Loaded → Dirty → Valid/Invalid → Saved | 未保存与已保存区分 |
| TestDocument | C-AUTHOR | Closed → Editing → Valid/Invalid → Saved | Run不可读取正在改的文档 |
| Run | C-RUNTIME | Preparing → Ready → Running → StopPending/Paused → Finalizing → Finished/Interrupted | 暂停边界依步骤；已完成判定与记录提交分开 |
| ScanDraft | E-SCAN | Empty → Captured → Parsed/Rejected | 不得修改已开始的Run快照 |
| MatchDecision | E-SCAN | Unresolved → Unique/NoMatch/Ambiguous/Invalid | 没有唯一匹配时禁止自动高风险操作 |
| ProgrammingJob | E-FLASH | Prepared → InProgress → Verified/Failed/RecoveryRequired | 未知恢复能力时不能自动重刷 |
| RunRecord | R-RECORD | Open → Appending → Committed/Incomplete | 记录提交失败不可展示完整归档 |
| Workbench | D-SHELL | ActiveDocument × Selection × Device × ActiveRun | 选择上下文与执行上下文正交 |
