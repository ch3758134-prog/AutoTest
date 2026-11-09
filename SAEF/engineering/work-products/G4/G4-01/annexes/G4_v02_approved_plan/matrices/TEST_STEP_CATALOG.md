# matrices/TEST_STEP_CATALOG.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`f60a18fc98c7709df3850d50181adb86f3fcf0e95fefc58419571841165ac9a4`

原始行数（含表头）：24

| 类型 | 节点 | 用途 | 参数 | 风险 | 验证层次 |
| --- | --- | --- | --- | --- | --- |
| flow.sequence | Sequence | 顺序容器 | children[n] | 无 | 模拟阶段必须通过 |
| flow.if | If/Else | 条件分支 | condition,true,false | 无 | 模拟阶段必须通过 |
| flow.loop | Loop | 有界循环 | maxCount,condition,body | 无 | 需要有界与取消 |
| flow.parallel | Parallel | 并行容器 | branches,joinPolicy | 并行争用 | 必须预检资源冲突 |
| general.note | Note | 说明 | text | 无 | 不产出PASS |
| general.wait | Wait | 定时 | duration | 无 | 可取消 |
| station.ign | Ignition | IGN操作 | state,endpoint | 物理输出 | B3前仅Fake |
| station.fixture | Fixture | 夹具电磁阀操作 | command,endpoint | 机械风险 | 无到位反馈 |
| station.keymask | Keypad | 按键板掩码 | mask,holdOrRelease | 实体电气 | B4前仅Fake |
| can.send | CAN Send | 发送CAN帧 | BusRole,frame | 总线写 | B2后实机 |
| can.wait | CAN Wait | 等待帧 | filter,timeout | 读 | 消费Hub |
| dbc.observe | Signal Observe | 获取信号 | SignalRef,window | 读 | 真实帧/DBC |
| dbc.assert | Signal Assert | 判定信号 | SignalRef,expected,tolerance | 读 | Unknown不可通过 |
| uds.request | UDS Request | 诊断请求 | UdsProfile,service,payload | 根据服务风险 | 无破坏服务先测 |
| flash.program | Programming | 烧录Job | ImageRef,ProfileRef | 不可逆 | 实刷需专项批准 |
| wave.capture | Wave Capture | 采集波形 | SignalRef,captureConfig | 读 | 真实电流待测 |
| wave.assert | Wave Assert | 比较波形 | CaptureRef,ReferenceRef,ComparisonRef | 读 | 阈值待实测 |
| record.note | Add Evidence | 记录人工证据 | note,attachments | 记录写 | 不可当物理实测 |
| production.scan | Scan Identity | 获取DUT身份 | scanRuleRef | 输入 | 只用于生产工作流 |
| production.match | Match Config | 选择配置 | matchRuleRef | 配置 | 歧义拒绝 |
| resource.acquire | Acquire Lease | 资源取得 | endpoint,policy | 锁 | 可能等待/超时 |
| resource.release | Release Lease | 释放资源 | leaseRef | 锁 | 异常清理必须执行 |
| general.subtest | Subtest | 子测试程序调用 | TestAssetRef,bindings | 继承被调步骤 | 循环引用拒绝 |
