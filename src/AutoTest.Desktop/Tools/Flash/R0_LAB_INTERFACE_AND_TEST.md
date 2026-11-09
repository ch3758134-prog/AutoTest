# R0-FLASH-DIAG-01 — 独立 Dry-run 工作台交接（软件候选）

## 源码/所有者
- Flash View: `AutoTest.Desktop.Tools.Flash.FlashToolView`
- Flash VM: `AutoTest.Desktop.Tools.Flash.FlashToolViewModel`
- 业务端口：`FlashDryRunWorkspace.LoadImage/PreflightAsync/StartDryRunAsync/Observe/WaitAsync/Cancel/ExportMockAuditAsync`
- Job 事实源：`ProgrammingJobCoordinator`/`ProgrammingEvidence`；不可新增第二套 Job 状态。
- 入口（现役）：桌面主窗口 → 模块工作台 → Flash/Dry-run；本专业不修改 `ModuleToolsWindow`、Bootstrap 或 Contracts。

## 功能与安全
- 输入 BIN/HEX/S-record、显示镜像 SHA256/地址段/字节数；读取 Mock ECU 预检阻断原因。
- Mock 作业含阶段、各次 UDS TX/RX/PDU CorrelationId、NRC、最后成功 TransferData ACK、确认字节数、剩余未确认字节；Seed/Key 与固件 payload 不在 UI 导出明文。
- 进度只来自已收到正确 TransferData ACK 的字节数，100% 只表示数据传输确认，不表示校验/版本回读成功。
- 取消/模拟掉线/NRC 失败保留终止阶段与检查建议，不假定物理 ECU 完成任何写入。
- UI 最多展示最近 3000 条阶段行与 3000 条 UDS 行；溢出显示遗漏计数，完整 JobFacts 留于工作区并通过现役 `ExportMockAuditAsync` 导出。
- 所有模拟电压、身份与作业均标记 MOCK；没有真实 ECU 刷写权限。

## 依赖申请（物理刷写不在此候选授权）
- 责任方：主对话组织 FLASH + UDS 核心 + DEVICE + INTEGRATION 联合设计。
- 缺口：ECU/Bootloader 真实身份/Revision、镜像合法地址映射、供电/夹具条件、Seed/Key 受控提供者、单次 Programming Session 授权、真正 CAN/USB Trace、写后校验与中断恢复规则。
- 不能通过取消 `ProgrammingPreflight` / `UdsClient` 的 Mock/Physical 风险检查来模拟物理进展。
- 对外最小输入：Project/Profile Revision、Image Asset/SHA、Station/Device Generation、Mode、授权上下文；输出：JobId、Preflight blockers、逐阶段事实、NRC/Uncertain、证据引用、取消与恢复状态。
- 真实烧录：未授权、未实测。

## Windows 机械验证（未执行）
```powershell
dotnet build AutoTest.sln -c Release
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~Tools.Flash
dotnet test tests/AutoTest.Specialists.Tests/AutoTest.Specialists.Tests.csproj -c Release --filter FullyQualifiedName~Flash
```
- Build/Test NOT_RUN；用户实测 USER_LAB_PENDING；既有 145/145 是历史基线，非本次候选测试证据。
- Codex 仅机械执行并反馈原始输出；共享代码和物理授权不得由本分支修改。

## 2026-11-09 继续研发补充
- UDS 请求在分块传输或后续阶段无确认最终响应时改为 ProgrammingJobState.Uncertain，错误码 UNCONFIRMED_UDS_RESPONSE；已确认 ACK 保留，但不能假定继续传输或自动重试安全。
- 明确 NRC 拒绝仍记录 Failed；显式取消仍记录 Cancelled，伴随“Mock 可能部分写入”警告。
- Mock 审计 JSON Schema=2 新增 AcknowledgedTransferBytes、LastAcknowledgedTransfer、ExpectedSegmentVerifications、ActualSegmentVerifications、MockHashVerified、PhysicalTransportUsed=false；保留全部后台阶段事实。已有消费端需按版本识别，不视为公共合同变化。
- 同一 Draft PR #5 持续更新，Build/Test 仍待主对话分配 Windows Codex 机械验证。
