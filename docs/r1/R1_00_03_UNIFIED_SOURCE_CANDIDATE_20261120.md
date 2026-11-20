# AutoTest R1-00～R1-03｜统一源码候选与一次性 Windows/真机验收

**开发分支**：`R1`（不修改 `main` / `Main_Control` / `Key_Sim`）。  
**审查来源**：[Issue #70](https://github.com/ch3758134-prog/AutoTest/issues/70)、`docs/r1/R1_00_CURRENT_BASELINE_20261120.md`、`docs/r1/R1_04_WINDOWS_FIELD_VALIDATION.md`。  
**冻结类别**：`SOURCE_CANDIDATE / WINDOWS_BUILD_PENDING / TESTS_PENDING / WPF_START_PENDING / REAL_ECU_PENDING`。  
**用户审批约定（2026-11-20）**：R1-00 → R1-01 → R1-02 → R1-03 整体推进后统一验收，**不按单包要求用户现场实测**；Codex 可一次性进行 Windows 构建/测试并回报，不得在验收前冒称真机通过。

## R1-00｜真实基线和硬件角色
- 已落档当前源码能力矩阵：`docs/r1/R1_00_CURRENT_BASELINE_20261120.md`。
- 真实 ECU CAN 外部 A1/A12，KEY CAN 外部 B1/B11；HOST 按 `DutCan / KeyCan` 角色处理。USB ECU 波特率 `0x42 / 0x43` 源码存在设置与读回，并非已经现场验证物理线速。
- 禁止非真实设备、假扫码或通用 Mock 结果晋升生产记录；用户当前可以使用 Development 模式，不要求扫码匹配配置完整。

## R1-01｜执行与操作
- 保持原有唯一 `RealRunCoordinator` 与 `RealTestWorkbenchService`，支持当前用例、勾选用例和单步骤真实诊断、流程/设备预检、停止/清理和运行步骤结果。
- `TestEditorView` 增加延迟自动保存：仅自动保存已经完成/应用的有效 TEST 草稿；属性输入中、非法值及未完成新步骤不静默持久化，保存仍做资源版本/SHA 检查。当前项目的用例 **Pinned Revision/SHA 仍须按既有显式“采纳最新修订”流程更新**，不能因自动保存暗中替换。
- 添加步骤改分类导航（常用/CAN/工位/UDS/波形/流程），搜索可跨所有类型；单击进入右侧步骤编辑，不再把分组长目录纵向平铺。
- 手动台架 ECU CAN 周期发送：不调用 KEY CAN 的固件 `0x50` 调度；通过现有 USB `0x40` 实体写接口在 HOST 按间隔循环，受 `PhysicalCommandFence`、逐次 ACK、50～60000ms 周期、总负载不超过25帧/秒、最多8000写入、5分钟会话与取消/停止约束。行内单发仍始终只发送一次。实际周期抖动与硬件接收需要真机验证。
- 手动台架可拖动三栏分隔，发送与监视列优先显示完整 DATA；KEY 动作的 20 Key 掩码、保持时间改独立浮窗，沿用工程动作库与延迟自动保存/关闭保存。界面保持浅色、主操作亮蓝+白字。
- 开发运行可不填 DUT 身份；不自动授予 Production 放行。

## R1-02｜FLASH 自动 TEST
- 使用原有 `ConfiguredRealTestFlashPort` → `HardwareConnectionService.ProgramTestFlashAsync` → CanTool 专业 Flash/UDS/ISO-TP 后端，不复制独立 WPF FLASH 工具或另造协议栈。
- 一次预检冻结单个真实 Flash 资源（XML、HEX、Key、安全文件、ECU 型号、SHA、CAN 波特率）；绑定主控 USB epoch，必须独立输入精确实体授权，一次运行最多一次危险 Flash 尝试。FLASH 失败、复位后 APP `10 03 / 50 03` 未回读，均不能继续正常 TEST 写入或宣称成功。
- CanTool XML 250K/500K 的 HOST 接入已有 `0x42/0x43` 写入/读回确认；不能仅按 UI 下拉框就当作物理线速。此次修正 WPF 步骤说明为真实授权行为。
- 纯 FLASH 的传输 ACK / APP Session 可以记录为技术执行结果，但**不能凭它自动产生生产 PASS**；正式通过还受 RECORD 证据与该项目实际定义的判定门禁约束。

## R1-03｜正式步骤事实/波形/审计/报告
- `RealTestRecordSession` 在真实步骤执行前建档，追加 `StepOccurrenceId` / Phase / StepId / EvidenceRef / 诊断详情；`RecordedRealRunEvidenceFinalizer` 归档原始 CAN/DBC 及真实 FLASH 轨迹，缺失、损坏、丢帧为非正式 PASS。
- 波形从同一真实 DBC 信号流采集。原 `WaveStart / WaveStop / WaveAnalyze` 保持“采集/分析数据”语义；新增正式 `WaveCompare` 为真正 TEST 判定，要求已完成采集 + 冻结参考或已映射分析配置，输出通过、失败或无法判定，仍通过唯一 TEST 执行器。
- 波形比较输入与计算结果从 `WaveEvidenceStore` 的不可变 SHA-256 sidecar 文件读回，复核信号/版本、采集对应关系、参考指纹和重计算结果；两个真实波形证据 ID 关联当前 `StepOccurrenceId`，并归档至正式 `ArtifactRepository`。
- 正式波形 PASS 需要当前同一 StepOccurrence 的 **Capture + Comparison** 双工件经重算校验、完整归档且质量门禁满足。普通 `Read→Expect` 仍不能靠无证据变量变为正式 ECU PASS。
- 结果支持原有 SQLite + HTML/JSON/CSV 报告及证据完整性标记；新增 `WaveCompareContractTests` 只验证纯静态 DSL/预检合同，不使用 Mock DUT 或假波形冒充现场测试。

## R1-04｜统一验证，不分包现场验收
Windows Codex 请先确认本地没有需要保留的未提交改动，切换至远端最新 `R1`，回报 **准确完整 SHA**，在 `HOST/` 内对 `AutoTest.sln` 完成：
1. Restore、Release Build（记录首个错误与全部警告）；
2. Release Tests（总数、成功/失败/跳过及完整失败名）；
3. 实际启动 `AutoTest.Desktop.exe` 并观察能否打开工程/TEST 编辑器、手动台架/KEY 浮窗及波形工作台；
4. 不自动连接 ECU、不自行运行烧录或危险 IO，不擅改业务代码，不切到其它分支；
5. 将启动错误和日志交 R1 专业定点修正；构建或测试未通过不能进入用户真机验收。

**最终一次真实验收批次**：
- 工程/测试保存、重开、项目用例修订显式采用；自动保存无覆盖或草稿丢失；
- 添加步骤分类、深底白字、三栏拖动及完整 CAN 帧无遮挡、KEY 浮窗 20 Key 组合和保存；
- ECU CAN 250K/500K 真机配置读回与物理线速；手动单发、勾选周期并发、停止后无继续发送；KEY/IGN/夹具真实输出；
- 运行当前步骤、当前用例、勾选用例集合；真 CAN/DBC PASS/FAIL、超时/断连/停止与安全清理；
- 真实 `WaveStart→WaveStop→参考波形建立→WaveCompare` PASS/FAIL/无法判定，采集与比较证据必须能在正式记录和波形工作台回读；
- 现场单次授权 Flash：真实 ISO-TP/UDS/下载 ACK、ECU APP 恢复、错误授权/资源/断连非 PASS；
- SQLite 事实、HTML/JSON/CSV、原始 CAN/FLASH/波形证据、RunId、修订指纹和证据完整性。
- 如果某硬件未接齐或某门禁实际阻断，按 `BLOCKED_BY_HW / NOT_VERIFIED` 记录，不得伪 PASS。

**结论**：本文件只说明 GitHub 源码已形成本次合并验证候选；Windows 编译/真机验收仍需 Codex/用户真实结果，不得擅自写为 PASS。
