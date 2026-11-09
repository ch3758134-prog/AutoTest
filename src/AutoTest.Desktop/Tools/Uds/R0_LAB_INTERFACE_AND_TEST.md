# R0-UDS-READ-01 — 独立工作台交接（软件候选）
  
## 源码/所有者
- UDS View: `AutoTest.Desktop.Tools.Uds.UdsToolView`
- UDS VM: `AutoTest.Desktop.Tools.Uds.UdsToolViewModel`
- 业务: `UdsDiagnosticWorkspace`、`UdsReplayWorkspace`、`UdsToolDocumentStore`
- 诊断事件/导出: `UdsDiagnosticEvent`、`UdsWorkbenchEvidence.ExportAsync`
- 入口（现役）：桌面主窗口 → 模块工作台 → UDS/ISO-TP Mock；宿主 `ModuleToolsWindow` 已存在，本分支不修改宿主或 DI。

## 操作与证据
- Mock 模式：ECU 身份、请求/响应 CAN ID、P2/P2*、服务编辑/保存、Hex 请求、NRC/Pending 注入和无响应测试。
- Replay：载入本地 JSON；必须匹配每条请求，失败不推进 replay 游标。
- 操作时间线：一个 CorrelationId 贯穿 UI 请求与后台 UdsClient，记录 UDS 响应序列（含 0x78）与分组的 ISO-TP 原始帧；这些 UI 时间并非 ECU 帧时戳。
- 筛选/复制/JSON 导出：需用户指定诊断包路径；导出附来源、时间语义、质量、未接入版本/Session 字段的显式 UNKNOWN；敏感 UDS 服务数据默认脱敏。
- 取消：停止当前软件请求并记录取消行为；不推断 ECU 完成/停止。
- 日志容量超过 UI 上限时显式标记 INCOMPLETE，完整存储路径尚待 RECORD/INTEGRATION 合同；不得标为完整原始物理证据。

## 依赖申请（仅真实 Physical 只读路径）
- 责任方：DEVICE + INTEGRATION；本分支不修改 `ICanFrameHub`、`CanHubIsoTpLink`、`UdsClient` 或 USB。
- 缺口：当前 DEVICE 只读接收 Hub 没有受控物理 Tx delegate；没有能核验 Station/DeviceIdentity/SessionGeneration、DutCan role、Tx/Rx IDs、允许 SID 0x22/0x19 的一次性受限诊断 Session。
- 预期：由主对话审查受限 Tx Session 合同后，提供生命周期、SessionGeneration 校验、CAN/USB 原始帧 TraceRef 和失败/丢帧语义；未提供前 Physical 必须拒绝请求，不能显示已发送。
- 对外最小连接输入：Project/Profile/Revision（当前未接入）、Station、连接 Generation、Mode、请求 PDU 和 CorrelationId；输出：请求状态、真实 Tx/Rx、NRC、超时/取消、TraceRef。
- 真实 UDS 读取/物理授权：未运行、未授权。Mock/Replay 不证明实机成功。

## Windows 机械验证（未执行）
```powershell
dotnet build AutoTest.sln -c Release
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~Tools.Uds
dotnet test tests/AutoTest.Specialists.Tests/AutoTest.Specialists.Tests.csproj -c Release --filter FullyQualifiedName~Uds
```
- 实测候选状态：DEVELOPMENT_IN_PROGRESS；Build/Test NOT_RUN；用户验收 USER_LAB_PENDING。
- Codex 仅记录完整原始编译/测试错误；不得自行修复/扩张权限。

## 2026-11-09 继续研发补充
- Replay 载入时校验响应序列：同一请求最终正/负响应后不允许新回复，ResponsePending 0x78 最多 8 条；仅 Pending 或无响应的回放依旧是 Uncertain，非物理 ECU 超时。
- Mock 单次异常根据 UdsTrace 分类：ISO-TP timeout、UDS NRC、Replay 输入不匹配及工作台错误；证据不充分时标为未隔离层级。
- Mock 历史容量限制为 512 次交换（可配置），达到上限明确拒绝后续操作，须显式重置；不会覆盖已导出的原始记录。
- 该轮增量已写入同一 Draft PR #5；Windows Build/Test 不因静态核查自动标为 PASS。

## 本轮补充：Mock 通道污染防护
- 一次 Mock UDS 请求因传输超时、取消或 ISO-TP/IO 错误而处于不确定状态后，Mock 通道进入 `RequiresReset`；后续 Mock 请求在入队前拒绝，记录 `MOCK_CHANNEL_UNCERTAIN_RESET_REQUIRED`，不产生虚假的 Tx。
- 用户须明确选择“重置 Mock ECU / 会话”后才能继续诊断。Replay 与该 Mock 通道隔离，不受其传输重置门禁影响。
- UI 提供 MockChannelStatus 与 MockResetRequired；新增回归：超时后拒绝、取消后拒绝、重置后恢复、错误分类和关联 ID。
- 注意：此机制属于 Mock 工作区自有安全处理，没有变更共享 `UdsClient` 或 `IsoTpConversation`；真实物理重新同步/重连仍需 DEVICE+主对话设计。

## 本轮增量：Replay 已验证数据冻结
- Replay Load 先验证请求 SID、UDS 应答序列和 CAN DLC，再复制并规范化全部请求/响应/帧数据；外部 UdsReplayDocument 后续修改不能影响已加载记录。
- 新增回归：调用方修改响应/NRC、帧和原始记录对象后，Replay 仍返回当时验证通过的结果及帧，避免重放证据漂移。
- 继续保持 Mock/Replay/Physical 隔离，真实 UDS Tx 仍未授权；Windows Build/xUnit NOT_RUN。
