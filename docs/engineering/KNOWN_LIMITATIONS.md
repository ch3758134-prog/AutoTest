# G4 V0-R2 已知限制与硬停止

1. 仅本地模拟请求、ACK、模拟推导观测和内存事实集；未实现真实 USB/CAN、诊断、烧录、扫码、DBC、测试计划执行、生产记录或设备操作。
2. 模拟 ACK 不是物理输出事实，模拟观测不能形成物理 PASS。`RecordCommitReceipt.Durable=false`，进程退出后历史不持久。
3. 断连、超时、旧 Session 与无授权路径已有源码/测试，**尚未在目标 Windows 上执行测试**。
4. CAN 仅公共契约签名；`TimeQuality` 与 `EvidenceQuality` 区分，CAN Payload 使用不可变数据。
5. `global.json` 已按主对话裁决更新为 SDK 10.0.400/latestPatch；保留 Prism 及测试包原候选版本。Prism Community License **主对话已确认**；但 NuGet 恢复、Windows 构建和 WPF 启动 `NOT_RUN`。
6. 原新增 11 路径 **主对话已批准 V0 使用**；六个 WPF 启动文件属临时 UI 职责，不意味着 UI 设计 Gate 已验收。
7. `dotnet --info` 历史初始化异常需记录而非单独阻断；`--version`、MSBuild、Restore、Build、Test 才是逐级硬门禁；Codex 不能自行修复失败。
8. `Authorization.MockOnly` 只发放模拟权限，真实组合路径不支持；未授权物理输出严格禁止。
9. 公共 CT-01～CT-23 仍为候选，Windows Build/审查前不冻结；HOST Git BASE_COMMIT 未建立，G4/G5 Gate 为 `NOT_REVIEWED`。
