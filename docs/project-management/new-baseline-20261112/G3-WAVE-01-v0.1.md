# AutoTest｜G3-WAVE-01 标准波形采集、参考管理与 TEST 自动判定详细设计（候审）

- 版本：v0.1，2026-11-14；状态：**G3 候审，不授予 G4 实施、生产放行或实体试验权限**。
- 上游：已批准 G2-GATE v0.1 A-03/A-04/A-06、G2-L04/L07，G1 v0.3。
- HOST 静态基线：`local@a9a26ebfba1efcf032ff8ec8c435336a89469647`；`main@ec3f04f1b6331dd22266d3bd1b1fac71aa451418` 为管理基线。
- UI 视觉/交互由独立 UI 专项负责；本稿只规范采集、判定、事实与可操作的行为合同。

## 1. 实际目标

自动测试工程师能够选择当前工程里的 DBC 信号，从真实 DUT 运行时采集一段波形，保存并查看质量与来源，由工程师明确批准为命名**标准参考**；之后在测试包内针对同一信号和参考执行自动比较，给出可解释的 Pass/Fail/Inconclusive，并将原始波形/参考版本/实际误差/Run/Step 关联保存。独立高级分析软件可稍后完善，但这条自动测试工作链不能后置。

**波形比较结果必须真实参与 TEST 判定。** 不能只算出一个 `Pass` 字符串显示在日志里，而整次 Run 仍然 `Inconclusive`。

## 2. 现有源码事实与未完成部分

| 源码 | 实际可复用基础 | 必须设计或验证的差距 |
|---|---|---|
| `LiveDbcSignalService` | 一个共用的真实 CAN RX → DBC SignalObservation feed；绑定精确 DBC，处理 NoFrame/Stale/DecodeError/LostFrames/Disconnected，支持 `IWaveSignalSource` | 确认实际会话 epoch、DBC 修订、时间质量、丢失事实随采集传递；实际 DUT 没跑过本轮验证 |
| `WaveCaptureService` / `WaveSampleBuffer` | 有界时间窗、样本缓冲、停止/保存证据 | 源码存在不等于当前 ECU 能得到有效样本；控制大小、时间、质量和取消 |
| `WaveReferenceWorkspaceService` | 搜索真实 `wave-records/evidence` 并明确提升参考，校验 DBC 信号及 `CanJudge` | `CanJudge` 只证时间/数据质量，不自动证明来源是真实现场；必须增加来源审核 |
| `WaveTimeQuality` | 检查时间单调、时钟一致、缓冲溢出、Gap、输入异常等 | `ReplayDerived`、`Simulated` 在时间一致时也可 `CanJudge`；不能作为真实生产基准直接批准 |
| `WaveReferenceCatalog/Repository` | 命名参考、不可变文件、SHA/Revision 校验 | 运行前应锁定精确参考 ID/Revision/SHA，不靠“同名最新”自动替换 |
| `ConfiguredWavePort` | `WaveStart/Stop/AnalyzeWithProfileAsync` 真实服务和 profile 检验 | Run 中存在数据质量、参考与动作状态关联不足；`PromoteCapturedReference` 也需来源/审批限定 |
| `RealRunCoordinator` | `WaveAnalyze` 生成 `TestDatum` 变量和步骤事实，`Expect` 才设置 `asserted/failed` | 当前 **WaveAnalyze 本身不影响最终 Verdict**；须设计自动判定的明确 TEST 语义，不能默默改变旧包含义 |

## 3. 标准波形的生成与审批合同

1. 必须从已打开真实工程的**确定 DBC 资产**选择 `BusRole + CAN ID + SignalName + Unit + Revision/SHA`；不可只凭相同字符串信号名绑定。
2. 采集记录附 `CaptureId`、时间窗、当前 USB SessionGeneration、真实 CAN/DBC 信号来源、采样数量/间隔、丢失/质量、软件版本、运行或手动采集上下文。若数据路径没有足够来源标识，则将来源标记为 `UNVERIFIED`，不虚构设备认证。
3. **生产参考审核政策候选**：仅当采集来源可追溯到当前真实 WinUSB/CAN1/DBC 现场会话，且无 Gap、DecodeError、BufferOverflow、时钟异常、参数错配，才能被工程师明确提升为可用于生产的标准参考。回放/仿真波形可以留做分析资料，但不能作为该类真实参考。
4. 从真实合格 DUT 获取参考还需要人工确认其作为标准的业务合理性；不能仅因为曲线平滑或通过算法检查就自动标记为“合格 ECU 的标准波形”。
5. 保存参考采用新身份和不可变资产 SHA，记录审批时间、工程/信号身份、来源 EvidenceId、审批人或操作确认。变更标准必须新建修订，不覆写已被 TEST 引用的版本。
6. 过时 DBC、无效来源、未知时间质量、无样本、样本不足、实际信号变化后不允许以相同名称自动复用。

## 4. 波形判定合同

### 4.1 输入与配置

用户（由 UI 专项设计呈现）选择参考波形和配置。实际比较使用 `CompareProfile`/现有 `WaveComparisonConfig`：时间对齐范围、绝对/相对误差、最低覆盖、越限时长等参数，必须从当次选中的配置 Revision/SHA 固定。单位必须匹配；当前信号 DBC 和参考信号 DBC 的全身份必须一致。

### 4.2 输出与 Verdict

比较至少返回：`Pass | Fail | Inconclusive | Error`、明确原因码、参考版本、实际证据 ID、样本数/覆盖/时钟来源、RMSE/最大误差/时间偏移/越限时长等实际算法可提供的指标。原始数据缺失、质量不符、丢帧、参考失效，不能被转换成 Fail 伪造 ECU 不合格，也不能变成 Pass。

当前 `RealRunCoordinator.WaveAnalyze` 只把比较结果写入变量，Run 的 Verdict 是由 `Expect` 决定的。**建议 G3 采用兼容旧 DSL 的原子作者工作流**：用户插入一个“波形判定”业务动作时，在当前 `TestDocument` 事务中明确创建 `WaveStart` + `WaveStop` + `WaveAnalyze` + `Expect`（或同一现有 TEST 模型上的等价复合结构），其中 `Expect` 明确检查波形变量为 `Pass`；不可在后台隐式“偷偷附加断言”并破坏旧 TEST 语义。条件不足时，不运行有效 Expect，保留 Inconclusive；实际判定规则和生成步骤必须在 G3/G4 代码审查验证。

例如：WaveAnalyze 返回 `Fail` 是**比较结论**，不是“动作执行失败”；后续显式 Expect 才使包 Failed。返回 `Inconclusive` 则不能简单用字符串 `Inconclusive != Pass` 变成产品 Fail，必须走可解释的数据质量/无效观测分支。该细节是 G4-WAVE 编译和单元/真实验证的关键合同。

### 4.3 与 A2 的绑定

结果按 `ProjectRunId → PackageAttemptId → StepOccurrenceId` 保存；Loop 中相同 StepId 多次采集应有唯一 CaptureId，不能因为 `captureKey` 相同而覆盖旧证据。运行中资源改动不影响已冻结分析。取消时停止订阅并保存可能已有的有效/不完整事实；不凭后台保存成功推定物理采样完整。

## 5. 交给独立 UI 专项的行为界面

不冻结 XAML 或布局，只要求 UI 能支持：工程内选 DBC 信号 → 采集预检 → 明确开始/停止 → 曲线、样本质量与设备来源 → 显式保存参考/命名/确认 → 选择参考与阈值 → 将波形判定插入测试程序 → 实时查看 Pass/Fail/Inconclusive 和错误原因 → 点击失败事实回到参数/曲线。UI 不应自行读取和覆写波形 JSON、计算假指标或独立打开第二条 CAN RX。

## 6. G4-WAVE 建议薄切片（需后续审批）

| 顺序 | 技术变更边界 | 验证层级 |
|---|---|---|
| W-00 | 当前代码与实际 DBC/USB 会话能力固定基线、观察现有 reference/evidence 格式 | 静态 review，不声称已实际采集 |
| W-01 | Reference 提升增加真实来源与操作确认、精确资产固定 | 离线读写/修订/过期拒绝 + 真采集来源复核 |
| W-02 | 统一 Run/Step/Capture 关联、质量/缺帧、实时事件输出 | 单元契约 + 实际 CAN/DBC 观察 |
| W-03 | 给 TEST 插入可解释且三态正确的自动波形判定复合动作 | 编译、明确 Pass/Fail/Inconclusive + 用户操作审查 |
| W-04 | 与 A2 多包 Run 证据、停止/异常清理及 UI 同步 | 真实 DUT Wave capture / compare / verdict / 原始文件关联 |

## 7. Windows + DUT 真实验证矩阵（均尚未执行）

- W-T01：真实 DUT CAN1 选定 DBC 信号，在相同时刻观察数值与实际波形采集一致。
- W-T02：丢报文、重连、DBC 资源变更、空采集、时钟异常分别保持无效/未知，禁止误判合格。
- W-T03：经操作者确认的实际合格样本生成新参考，保存重开 SHA/信号/来源一致。
- W-T04：回放/仿真来源即使时间质量满足要求，也不得被认可为生产真实标准。
- W-T05：在固定容差/覆盖条件下比较真实波形，分别看到 Pass、Fail、Inconclusive 原因与指标。
- W-T06：测试程序中波形比较明确参与整次 Run 的断言；缺数据只构成 Inconclusive，不强行 Fail/Pass。
- W-T07：两次运行、同一包多次引用、Loop 多次捕获，记录完整保留且可以定位。
- W-T08：运行中停止、采集超过期限、文件写入失败不产生伪完整参考或报告。

## 8. 本次建议的审批范围

允许审批参考的真实来源门禁、独立命名和精确修订、真实采集质量、波形比较与 TEST 断言分离/联动、A2 结果关联和 W-00～04 的后续实施范围。**不因 G3 文档批准而执行任何 G4 代码修改或 ECU 实测**；现有波形是否精度足够，必须现场测量，不能凭代码演算代替。
