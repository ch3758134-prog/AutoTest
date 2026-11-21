# 05｜R1 初版真实自动测试交付与统一验收基线

**来源**：R1 最新精确源码 `fa3b18e948043a9d1960adddb627658fd40c8159`；[R1-00～03 统一源码候选](https://github.com/ch3758134-prog/AutoTest/blob/R1/docs/r1/R1_00_03_UNIFIED_SOURCE_CANDIDATE_20261120.md)；[R1 Windows/现场验证指令](https://github.com/ch3758134-prog/AutoTest/blob/R1/docs/r1/R1_04_WINDOWS_FIELD_VALIDATION.md)。本文件明确覆盖更早 R1-00 启动阶段“只有计划尚无实现”的旧状态。

## 1. 第一版究竟要交什么

**优先级最高**：Windows 上位机真正能打开工程/用例、保存并重开、选择范围、连接主控和 ECU、按安全门禁运行准备/测试/清理和 Flash、显示实时步骤/日志/判定、持久化原始证据和报告。用户可用当前项目的 Development 方式先进行现场实测，不能让尚未落地的生产扫码配置挡住运行。

用户已经批准：**R1-00 → R1-01 → R1-02 → R1-03 全部源码推进后，再一次性统一验收**。不要求每个小修复都在现场重新完成整套操作；Windows 构建/测试由本地 Codex 提供独立证据，只有稳定可启动候选才安排真实 ECU。

## 2. 截止此次整理的真实状态

| 工作包 | 已存在的源码候选（R1，不是 main） | 仍需通过 |
|---|---|---|
| R1-00 基线 | 主控 CAN 角色、TCF/DBC/工程版本、真实硬件门禁与差异文档 | 现场物理连接、固件镜像一致性 |
| R1-01 真 TEST | 测试项目勾选顺序运行、单步/单用例/范围、预检、Stop/Cleanup；编辑步骤分类与局部自动保存；手动 CAN/KEY 界面增强 | Windows Release、实际键/IGN/夹具、DBC 正负、停止与清理 |
| R1-02 Flash | `ConfiguredRealTestFlashPort` 调用同一 `HardwareConnectionService.ProgramTestFlashAsync` 与 CanTool Flash workflow；资源冻结、二次授权、限一次危险 Flash、APP `10 03 → 50 03` 后验 | 实机 UDS/Flash、烧录速度/NRC、真实后验及软件版本策略 |
| R1-03 RECORD/WAVE | `RealTestRecordSession`、SQLite、原始 CAN/DBC/FLASH/WAVE 工件、`StepOccurrence`、HTML/JSON/CSV；`WaveCompare` 正式步骤和部分双证据验证 | WAVE 的 `JudgementEvidenceKind` 记录合同冲突、时间质量/参考标定、真实归档和报告验收 |
| R1-04 统一现场 | Windows Codex 与用户现场的精确操作指令 | **未见固定最新 HEAD 成功 Build/Test/WPF/真实 ECU 的回执** |

旧 TEST-RECORD PR #65 仍 Draft；R1 已将相关模块**定点接入当前代码**，不能说 R1 没有正式报告源码，但 R1 尚未凭真实测试证明其正式生产门禁已经通过。

## 3. 第一轮集成必须收敛的阻断

**P0-A｜Windows 可启动**：Codex 锁定最新 R1 SHA，在 `HOST/` 执行 `dotnet restore AutoTest.sln`、Release Build、Release Tests、启动 `AutoTest.Desktop.exe`。需原始错误/警告和测试计数，不用模拟/截图取代。

**P0-B｜真实设备/工位**：ECU/KEY 总线角色、主控实际 0x42/0x43 支持、ECU CAN 真实响应、20 Key 组合、IGN/夹具输出，必须区分代码接口已联与硬件安全已验证。主控固件新命令如果未真正烧录，记录 `BLOCKED_BY_FW_VERSION`，而不是强行返回 OK。

**P0-C｜烧录**：先做工程资源冻结和实物目标确认、单次独立授权、真实 UDS ISO-TP/下载/Boot 到 APP 会话检查；不能用 APP Session 代替软件版本确认。没有授权不触发写入。

**P0-D｜RECORD 正式门禁**：完整 StepOccurrence、判定实际值、原始 CAN 与 FLASH 附件、Drop/Error/未知标记、同一 RunId 的报告回读。仅有源代码候选不构成报告内容、耐久性实测通过。

**P0-E｜WAVE 正式证据冲突（必须单独解决）**：R1 `RealTestRecordSession.StepCompletedAsync` 将完成的 WaveCompare 标记为 `WaveComparisonPendingArchive`，但当前 `RunStepFact.Validate` 仅允许 `JudgementEvidenceKind=DirectDbcSignal`。这会在 RECORD 写入前造成合同不一致（见 W-00）；不能因为后面的 EvidenceFinalizer 已做“双文件/重计算”就忽略入库前 Validation。修复须经 TEST/RECORD + WAVE 审核，不能直接移除判断字段验证、也不能让 Wave 借一个无关 AssertSignal 的证据拿生产 PASS。

**P0-F｜UI 最小可操作**：TEST 当前步骤/用例/勾选范围、右侧可编辑属性与结果、手动台架 CAN 单发/周期/监视和 KEY 浮窗、进度及停止反馈，满足 R1 可用。完整新 Fluent UI/文件全生命周期的大规模重构另行验收。

**SCAN 全量生产准备不是 Development 首版门禁**：SCAN #72 的绑定表/配方/批次还属于研究方案，不能把旧扫码检测作为运行当前测试的必要条件。

## 4. 一次性现场验收建议顺序

1. 工程保存/重开、DBC、用例项目修订采纳；确认 Windows 只启动一个真正的 Shell、无工具窗遗留。
2. 主控 WinUSB 接通、固件能力、ECU CAN 波特率请求/读回与 ECU 实际帧、KEY CAN 500k；无自动危险操作。
3. 开发用例单步/整用例/勾选两个用例；真实 CAN/DBC 信号有 PASS、故意 FAIL/Timeout，记录每次 Occurrence 与 CAN 时间范围。
4. KEY 模拟、IGN、夹具及停止/超时/断开，保存实际状态与独立物理验证；危险组合/断线前先安排现场安全方案。
5. WAVE：建立真实参考、真实采集、同资源 SHA 比对；对算法支持的指定信号验证 PASS/FAIL/INCONCLUSIVE，并能从报告取回双证据。未修 P0-E 或未校准时间质量的严格判定不得当生产 PASS。
6. 经现场授权的一次完整 Flash：检查 XML/HEX/Safety/Key/ECU ID/速率，Boot、下载、Reset、APP 响应，后续独立的 ECU/DBC 判定；不进行无审批的错误刷写破坏性注入。
7. 异常路径：无 CAN、错 DBC、坏资源、USB 断开、取消、证据损坏，系统不能完成生产 PASS；报告写出终态和不完整原因。
8. 退出重开，读取 SQLite、同源 JSON/HTML/CSV、真实证据与报告路径、原测试修订/工位 epoch，并由用户确认当前设备实际状态。

## 5. 每次验收回执必须有

`R1_SHA`、Windows .NET SDK/Release Build 错误警告与测试计数、WPF 启动结果；现场记录 `DUT_ID`（必要脱敏）、主控实际 FW 标识、KEY_SIM FW/线束、工程/DBC/用例/Flash Profile 的 Revision/SHA、USB/CAN 速率和报文、`RunId/StepOccurrenceId`、原始证据路径、报告、用户观察与失败阶段。

状态可为 `SOURCE_CANDIDATE`、`BUILD_PASS`、`FIELD_PASS_FOR_EXACT_CASE`、`BLOCKED_BY_HW`、`INCONCLUSIVE`。不把若干真机用例 PASS 推广为所有硬件版本与波特率通过。批量生产 Release 计数和 SCAN 匹配仍需专门门禁。

## 6. G4、R1 与用户审批

旧 G4 专项已不作为 R1 开发入口；已合入主线的历史 G4 成果继续保留。R1 研发的高优先级是**完整真实流程**，而非改控件皮肤。此次整理不对 `R1` 源码进行“自动补齐”、不合并 `main`，也不无授权运行真实 Flash。收到新 Codex/现场结果后再更新项目管理验收事实。
