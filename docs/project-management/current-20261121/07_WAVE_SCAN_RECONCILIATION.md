# 07｜2026-11-21 WAVE、SCAN 新研究纳入总系统的方式

**关键区分**：两个专项均已完成第一轮源码审查/设计/验收交接文档，仍是**候审技术研究**，与 R1 现有源码可能存在冲突，不能自动改写 `TEST/RECORD/PROJECT/Production` 业务合同。归档目前不批准直接重构。

## 1. WAVE 波形判定研究｜Issue #71

来源：
- [W-00 真实采样与源码阻断](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-00_SOURCE_CAPABILITY_AUDIT_20261121.md)
- [W-01 参考工业判定和误判](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-01_INDUSTRIAL_COMPARISON_AND_FALSE_JUDGEMENT_20261121.md)
- [W-02 后端 V2 增量合同草案](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-02_BACKEND_INTERFACE_AND_RECORD_CONTRACT_20261121.md)
- [W-03 单信号现场闭环方案](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/W-03_REAL_ECU_ACCEPTANCE_AND_ROLLOUT_20261121.md)

**新增的重要事实/风险**：
- 现有比较器采用线性插值和有限自动时移，可在纯数值时间序列上工作，但**不一定适用于 ECU Bool/Enum 状态阶跃**，也可能用自动对齐掩盖“响应迟滞”这个正要测的故障。
- 当前 CAN RX/DBC 观测的时间性质是主机接收时间（`HostArrival`）；对设备实际瞬时事件/高精度响应毫秒数不能无校准地给出工业级 PASS。
- 波形参考、DBC 信号 SHA、单位、总线角色、来源与采集质量必须冻结；从模拟/回放升级为生产参考，需人工审核+真实来源，不可静默转换。
- **跨层已证实合同冲突**：当前 `R1` 的 `RealTestRecordSession` 写 `JudgementEvidenceKind=WaveComparisonPendingArchive`，`Records/Model/RunFact.cs` 的 `RunStepFact.Validate` 只允许 `DirectDbcSignal`。有后置 Wave evidence Finalizer 仍不能解决步骤事实入库前的拒绝。应以先红后绿的真实整合测试约束修复。
- 真实波形与参考之间的捕获 ID、`WaveStart/WaveStop/WaveCompare` **各自不同 StepOccurrence** 要关联到同一个不可变 CaptureGroup；循环中 `captureKey` 复用不得使不同轮证据串档。

**研究推荐的候审 P0 类型**：
1. `DiscreteStateSequence`：状态转移次序、过渡延迟、最终保持和禁止状态，按采样缺失保守 Unknown。
2. `WindowBounds`：特定时间窗上下界、持续时间和有效覆盖，防止用短时合格片段掩盖整段违约。
3. `AnalogEnvelope`：仅对确实连续且更新足够快的慢变化 CAN/DBC 数值按受限包络比对。

较高频谱 FFT、DTW、任意自动时移、外部 ADC 与多信号复杂图形不作为当前硬件 R1 P0 默认。**以上三类以及 `VerifiedWaveComparison` 命名、Schema v2、阈值和时钟约束，仍是研究建议，需要 TEST/RECORD+R1 评审后实现**，不能称为已批准当前版本合同。

**对 R1 的最小可执行建议**：先修复 WAVE 入库证据类型、严格限定一条真实 DBC 信号的参考/采样/对比/报告闭环，校验四态。真实采样必须有输入/参考的文件 SHA、时间质量、原始 CAN 片段与判定 occurrence 引用。若现场时间精度不能支持时效判定，明确限制可判目标与阈值，不把算法“普通波形相似”推广为所有 ECU 功能可靠。

## 2. SCAN 扫码与自动测试配置｜Issue #72

来源：
- [S-00 代码能力与断点](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-00_SOURCE_AUDIT_20261121.md)
- [S-01 绑定表/冻结 RunPlan 合同 v0.1](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-01_MATCHING_RUNPLAN_INTERFACE_CONTRACT_20261121.md)
- [S-02 研发/生产操作与复测](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-02_PRODUCTION_DEVELOPMENT_OPERATOR_FLOWS_20261121.md)
- [S-03 R1 最小接线计划](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-03_R1_IMPLEMENTATION_HANDOFF_20261121.md)

**已确认的实际断点**：扫描输入适配、Regex 身份规则、已发布配方匹配和部分资源 SHA 校验存在，但 `ProjectScanMatcher.MatchAsync` 只返回 `Code/RecipeId/Identity`，不负责项目级有序用例冻结、TCF/TBC/DBC/Flash job/ECU CAN 波特率确认、真实执行与记录。现有 `ProductionWorkflow` 的内存 RunId 不能未经映射就与每个 TEST 的真实 RunId 视为同一身份。

**研究提出的三层业务对象**：
- 外部 DUT 编号和扫描输入（只捕获不生成编号）；
- **独立“编号→配置/配方”绑定表**（`DutConfigBindingTable`，与扫描 Regex 规则分离、版本化、可编辑/发布/撤销）；
- 配方 `ProductionRecipeV2` 与单次不可变 `FrozenScanRunPlan`，绑定已批准项目范围、各用例 Revision/SHA、TCF/TBC、DBC/KEY、主控目标速率、Flash job 文件 SHA 和报告政策。扫描成功只进入待确认/预检，不是实体动作许可。

**必须仍保留的已批准产品边界**：工程师无扫码也能单步/单用例/选中范围运行；外部已存在唯一编号，不生成新号码；匹配不自动在 Enter 事件中控制 IGN/夹具/Flash；同一 DUT 重复扫描不计一次新测试、更不能重复新增出厂。生产放行应单独于 TEST PASS，统计要区分扫描、尝试、运行和首次放行。

**候审（不能擅定）**：
- S-01 建议 P0 按“每个唯一外部编号精确绑定”且不按型号 Regex 自动推导配方。此模式**尚待负责人结合实际产线/业务数据确认**。独立绑定表的方向与先前用户“编号只唯一、绑定另管”一致，但具体批量导入格式、绑定层次与生效范围需要评审。
- `ReleaseScope`、出厂幂等键、复检/重新放行权限、ProductionRecipeV2 数据迁移、运行上下文中的编号是否需验证 ECU DID 等属于新的生产政策/技术合同；**不因本次整体文档整理就自动获得出厂政策批准**。
- 现有旧 `scan-rules` 可保留兼容，不得自动把旧的 RecipeDraft 静默升级为已发布生产授权。

**对 R1 的最小可执行建议**：独立交付扫码枪真实捕获/输入状态→可解释匹配→资源/计划预览与确认（先不做自动烧录），且**Development 模式不受任何扫码设置影响**。随后受控接入唯一 TEST 执行器与 RECORD/Production 幂等门禁，真实扫码枪和真实 ECU 实测后才能称生产扫码工作流可用。

## 3. 两专业与 R1 的接口责任和先后顺序

| 方向 | 研究分支责任 | R1/TEST/RECORD/PROJECT 接收条件 |
|---|---|---|
| WAVE | 判定算法反例、时间/参考质量、四态、独立波形证据合同 | 先评审 W-02 的增量 Schema 与记录门禁，再做最小 diff；新类型按同一 occurrence/附件 SHA 证明，无混用 DirectDbcSignal |
| SCAN | 业务数据模型、绑定/配置匹配、生产/研发真实用户操作和测试场景 | 先评审 S-01/S-03 与生产 ReleaseScope 及数据迁移，再由 R1 完成只准备不授权的执行桥 |
| R1 | 真实 TEST、自动 Flash、单工位 WPF/RECORD 与整体实测 | 不等 WAVE/SCAN 全量完成才继续基础 TEST/FLASH；已纳入的 WAVE 必须修入库 Gate 才可正式验收 |
| UI | 展示明确配置字段、参考/绑定来源、错误与恢复状态 | 不拥有波形数值算法、扫码匹配生产政策、原始证据或设备授权 |

**审查结论**：WAVE/SCAN 新研究极有价值，正式纳入系统**缺口与接口评审输入**；尚未批准 WAVE v2/SCAN v2 直接成为生产合同，也尚未验证真实波形/扫码现场 PASS。
