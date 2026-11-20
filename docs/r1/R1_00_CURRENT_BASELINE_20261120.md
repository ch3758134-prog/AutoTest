# AutoTest R1-00｜2026-11-20 最新源码基线与能力矩阵

> 审查基线：R1@81ed13ab68f7c5afff9e52c5f537ba0043c904d8。此文件的状态是源码静态核查，不是 Windows Build 或真机通过声明。

## 责任与边界
- 主仓库三工程：HOST、Main_Control、Key_Sim；R1 仅修改 HOST 和 docs/r1。
- 用户现场 ECU CAN 接口为主控 A1/A12；KEY CAN 接口为 B1/B11。使用 BusRole.DutCan / KeyCan，不根据旧编号推断物理路由。
- 真实 WinUSB 物理写入经过 PhysicalCommandFence；主控 USB 0x42/0x43 ECU CAN 波特率写入/读回在 R1 源码已接线，但尚未现场物理验证。
- 默认非生产开发诊断允许不录入 DUT 编号；不可伪造扫码身份，也不可计入正式出厂。
- R1 未经审查不能改 main、不能修改固件、不能进行虚假 Mock 验收。

## 静态能力矩阵

| 域 | 有效源文件/入口 | 源码状态 | 剩余门禁 |
| --- | --- | --- | --- |
| 工程/版本 | HOST/src/AutoTest.Desktop/Projects/TestEditorSession.cs；ProjectWorkspaceService | SOURCE_EXISTS_WIRED；按资源版本与 SHA 验证 | Windows 保存/重新打开与冲突实测 |
| TEST 步骤 | HOST/src/AutoTest.Desktop/Testing/TestEditorViewModel.cs；RealTestWorkbenchService.cs | SOURCE_EXISTS_WIRED，单步骤、当前用例已有入口 | 自动保存、禁用状态、实际可编辑/可运行范围核实 |
| 项目用例勾选 | HOST 中 TestProjectWorkbenchViewModel 与 TEST Runner | SOURCE_EXISTS_WIRED；按勾选顺序串行 | 多用例 RunId/报告现场证据 |
| CAN/DBC | ConfiguredRealTestActions、LiveDbcSignalService、主控 USB 0x40/0x41 | SOURCE_EXISTS_WIRED；真实观测/发送与冻结 DBC 映射 | 实际 ECU 收发、总线负载/掉帧 |
| 手动 CAN 周期 | ManualBenchCanRows.cs；SendManualBatchCanAsync | 源码曾人为禁用，预留 HOST 周期循环 | 解禁需保留 USB 安全授权/停止/时间限制；真机验证时序 |
| KEY/IGN/夹具 | ManualBenchViewModel、ManualBenchKeyActions、HardwareConnectionService | SOURCE_EXISTS_WIRED | 接线、默认 IO 状态及物理 ACK/回读 |
| UDS | 专业 UDS / ISO-TP 独立后端 | SOURCE_EXISTS；任意 UdsRequest 自动 TEST 尚未保证可执行 | 不与 FLASH 专用 UDS 混淆 |
| Flash 自动步骤 | ConfiguredRealTestFlashPort、ProgramTestFlashAsync、CanToolFlashJob | SOURCE_EXISTS_WIRED；Profile/HEX/Seed-Key 审核、危险授权与 APP 10 03 后验 | 真实 ECU Flash 成功、失败安全终态 |
| 波形采集 | ConfiguredWavePort、WaveCaptureService、WaveReferenceCatalog | SOURCE_EXISTS_WIRED；WaveStart/WaveStop/WaveAnalyze | 波形参考采集与实测数据质量 |
| 波形判定 | WaveComparator、WaveAnalyze 返回数据，TEST Expect | INTEGRATION_LIMITATION；独立旧 WaveCompare 标记不可执行 | 明确波形比较与判定证据来源/三态，不能伪 PASS |
| 记录/报告 | RealTestRecordSession、RealRunEvidenceRecorder、RecordedRealRunEvidenceFinalizer、RunReportExporter | SOURCE_EXISTS_WIRED；StepOccurrence/设备/FLASH 原始证据归档 | SQLite/HTML/JSON/CSV 重新打开和完整性检查 |
| UI | TestEditorView、ManualBenchWindow、WaveWorkbenchWindow | SOURCE_EXISTS；窗口布局、列表裁剪、步骤分类交互不达用户验收 | R1 修正后交 Codex Build/启动确认 |

## 首个纵向闭环与风险
1. 打开工程，修改 TEST/DBC/波形参考并持久保存；扫码配置未完成可用开发身份。
2. 真实 USB 连接、设置/读回 ECU CAN 波特率；先确认实际物理链路。
3. 选中用例或步骤执行 KEY/IGN/CAN 与直接 DBC 断言；安全停止。
4. 从步骤运行唯一专业 FLASH 后端并收集过程证据，APP 会话未恢复不得当 PASS。
5. 将每次步骤/用例结果和证据写入正式记录和 HTML 报告；能回读并定位异常。
6. Windows Release Build/Tests/WPF 启动及最终真机全部留待 R1-04 一次验收。缺失证据写 NOT_VERIFIED，不能写 PASS。

## 当前批次策略
R1-00 → R1-01 → R1-02 → R1-03 按代码提交顺序完成；用户要求四项完成后才集中验收，不在中间要求用户单独实测。其他 UX 反馈（添加步骤、KEY 浮窗、台架列宽/波形工作台/自动保存）作为这批 R1 实施内容跟踪。
