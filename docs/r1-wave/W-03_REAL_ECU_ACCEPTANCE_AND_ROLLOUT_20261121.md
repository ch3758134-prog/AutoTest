# W-03｜R1 WAVE 单信号纵向闭环实施路线与真实 ECU 验收方案

**Issue**：#71。**研究快照**：`R1@fa3b18e948043a9d1960adddb627658fd40c8159`。**状态**：`IMPLEMENTATION_CANDIDATE_PENDING_REVIEW / FIELD_NOT_VERIFIED`。本研究分支仅产出调查、设计与验收程序，**未修改 HOST 源码、未编译、未接入 ECU 现场**。

## 1. 验收目标与不可绕过的边界

验收对象不是“能画图”，而是一次真实 `WaveCompare` 测试在既有 R1 上形成：

`真实 ECU 接入 → DBC 精确绑定 → 真实主机时戳 CAN/信号捕获 → 已核准且锁定版本的参考 → 采样质量/响应或包络判定 → 正确四态 → TEST 步骤结果 → RECORD 同 Run/Case/Occurrence 归档 → HTML/JSON 报告 → 从报告证据追溯回原始 CAN 帧`。

**一次合格的真实波形比较并不代替正式 Run PASS**；须同时满足所有 TEST 断言及现有 RECORD 的归档、完整性和终态门禁。

- **物理限制**：当前是 ECU CAN/DBC 上报数据，不是快速模拟波形；不准选择需要无支持的 PWM/电流尖峰/外部电气测量的首个验收样例。
- **时钟限制**：当前 0x41 `HostArrivalOnly`。不实测完成时钟和更新率标定，不能以毫秒时效性验收作为 P0 产线 PASS。
- **真实参考**：必须由实际已验证合格 ECU 在明确的操作条件下录制，并人为批准，留下 DBC/ECU/时间来源/录制状态/审批元数据；Replay/Simulation 只能验证算法。
- **工程权限**：本专项提交候选并等待 R1 审查；未经批准不得写 `R1`/`main`，不得修改或刷写主控/按键板固件，也不新增 Mock。

## 2. 实施分层计划（按依赖，不与 R1 当前开发抢改）

| 包 | 交付 | Gate / 不满足即停止 |
| --- | --- | --- |
| `W-P0-00` 静态回归与接口核验 | 复读 R1 最新 HEAD 和关联 PR；明确 W-00 所有差异，特别是 `WaveComparisonPendingArchive` vs `RunStepFact.Validate`；归属 R1/RECORD 专项 | 新 R1 源码差异明确且没有未解决的双重写入冲突 |
| `W-P0-01` 先闭合 RECORD Gate | 真实 `WaveCompare` 的模拟**链路集成测试**：StepStarted→WaveCompare 两个 evidence IDs→现有 RECORD 接受限定 WAVE kind→校验两份文件→Report JSON/HTML；异常归档不出正式 PASS | 同一 StepOccurrence 真实步骤、WAVE 专用 kind 通过严格校验；与 DirectDbcSignal 不串证据 |
| `W-P0-02` 固定真实采集与参考资产 | 在 `ConfiguredWavePort + WaveCaptureService + WaveEvidenceCodec + WaveReferenceCatalog` 增加 V2 Run/Case/Capture group/3 Occurrence、参考批准与 SHA 精确引用；修复 Loop captureKey 复用 | 回放能作算法研究但不能通过生产参考门禁，错 Reference/DBC/Signal/Unit 必阻断 |
| `W-P0-03` 保守算法 | 为一个选定信号实现可审查的 `DiscreteStateSequence` 或 `WindowBounds`，明确真实 trigger、有效时间覆盖、状态保持和缺失分类；保留旧比较器非生产/兼容模式 | 离线不变性/对抗性用例全部正确，自动偏移不能掩盖响应延迟 |
| `W-P0-04` EDIT/EXEC/REPORT 薄闭环 | TEST 步骤属性保存冻结 v2、真实运行选择当前项目、Compare 显示 4 态，已归档报告列出原始/参考/差异/质量/关键异常 | 从工程保存重开到整次运行有一条完整可审计的单信号用例 |
| `W-FIELD-01` 真机 | 在 Windows + 主控 + 真实 ECU + 正确 DBC 上完成 PASS、真实偏差 FAIL、至少 4 类不可判/错误，以及证据审查 | **只有此步通过才能声明 FIELD_VERIFIED**，绝不由离线 Replay/Mock 替代 |

**最小首版功能**：单工位、单 ECU、单个真实 DBC 信号、1 次参考采集、1 个 TEST 用例里的一个 WAVE 判定组、1 套正式报告。多信号联合判定、外部 ADC、高频频域分析、复杂多窗口 UI、跨平台 MDF 自动化在此后迭代。

## 3. R1 开发测试必须覆盖的单元和集成矩阵

### 3.1 波形判定算法/质量门禁（离线、明确 LAB_ONLY）

| ID | 测试条件 | 期望 Verdict / 原因 | 防护目标 |
| --- | --- | --- | --- |
| `W-ALG-01` | 正常、完整、同步或已校准的实验时序，满足所有明示规则 | `PASS`（算法级） | 基础正例 |
| `W-ALG-02` | 完整有效的实测违反明确值范围 / 状态顺序 | `FAIL` | 对已证明不合格目标敏感 |
| `W-ALG-03` | 延迟仅能通过旧 50ms RMSE 平移掩盖 | `FAIL` 或 `INCONCLUSIVE`（取决于时钟是否可证明） | 禁止假 PASS |
| `W-ALG-04` | bool/enum 状态 0→2，期望 1 | `FAIL`，不得线性插值补出 1 | 避免枚举插值 |
| `W-ALG-05` | 关键跳变点丢帧/不连续 | `INCONCLUSIVE` | 不将缺失定性为 ECU 故障或正常 |
| `W-ALG-06` | 起点、尾端或重要保持区间没有足够样本 | `INCONCLUSIVE` | 防边界外推/尾点失效 |
| `W-ALG-07` | Replay/Synthetic 参考尝试作为生产参考 | `ERROR` / 审批拒绝 | 阻断假实体参考 |
| `W-ALG-08` | 参考/DBC ID、Rev、SHA、Role、FrameId、Unit 任一不合 | `ERROR` | 资源全身份不可换 |
| `W-ALG-09` | 未知时钟、跨设备重连、反转时间戳 | `INCONCLUSIVE` | 时间质量保守 |
| `W-ALG-10` | 大 USB/Hub/订阅缓冲溢出，或证据源丢失 | `INCONCLUSIVE` | 显性数据缺失 |
| `W-ALG-11` | 不均匀采样：密集正常段+稀疏异常段 | `FAIL` 或 `INCONCLUSIVE`，不得“按点 95% PASS” | 时间覆盖门禁 |
| `W-ALG-12` | 最后采样点越差且无终点支持 | `FAIL`（数值即时超差）或 `INCONCLUSIVE`（仅持续时间门槛） | 分清错误证明与持续时间证据 |
| `W-ALG-13` | 数据采样总点数不足、波形超过 100k 上限 | `INCONCLUSIVE`，不得 truncate 为“完整 PASS” | 有界缓冲 |
| `W-ALG-14` | 同一个测试步骤进入 Loop 两次 | 两个独立 CaptureId 和三个各自 Occurrence 链 | 防跨次污染 |
| `W-ALG-15` | 旧 Schema v1 参考及非批准来源 | 允许 LAB_ONLY 审阅，但正式 `WaveCompare` 拒绝 | 正向兼容与生产隔离 |
| `W-ALG-16` | 操作触发只有 USB ACK，没有 ECU 物理动作可观测起点 | 需要精确响应时间时 `INCONCLUSIVE` | ACK 不冒充动作证明 |
| `W-ALG-17` | 比较数据中有明确非有限值/错误阈值 | `ERROR` | 参数非法不能 PASS |
| `W-ALG-18` | 信号在整个窗口沉默，但通讯线路还有非目标 CAN | `INCONCLUSIVE`，除非明确的停报规范+健康双证据 | NoFrame 不假装状态 PASS |

所有测试产生机器可读取 `algorithm id/version + input sample provenance + frozen config + expected code + actual code`。**离线通过只声明 algorithm/integration tests PASS，不写 FIELD PASS。**

### 3.2 TEST→RECORD 正式链路测试（离线接口合同级）

| ID | 验证条件 | 断言 |
| --- | --- | --- |
| `W-REC-01` | WaveCompare PASS + 2 个校验有效的波形文件 + 完整原始 CAN 证据 + 当前 Run/Case/Occurrence | RunStepFact **不抛 Validate 错**，专用 Wave 证明有效，正式 RECORD 可 PASS（如其他断言也通过） |
| `W-REC-02` | WaveCompare PASS 但缺 ComparisonEvidence | `RunState.Unknown / Verdict.Inconclusive` 或执行存储错误，不允许正式 PASS |
| `W-REC-03` | WaveCompare PASS 但删掉 Reference/approval SHA 证明 | 正式 PASS 受阻，报告显示缺失/不可信 |
| `W-REC-04` | 2 个波形 SHA/证据 ID 被替换、断链或人工改文件 | 归档复核失败，不能 PASS |
| `W-REC-05` | Capture 取自 Run-A、Compare 执行 Run-B | 拒绝跨 Run/Case/Occurrence 冒用 |
| `W-REC-06` | 非 TEST / Prepare / Cleanup 阶段创建“合格”WaveCompare | 不授予生产 TEST 判定证据资格 |
| `W-REC-07` | StepOccurrence 重复 / 同一 Capture 被两个 Loop 次序复用 | 拒绝重复或不一致归属 |
| `W-REC-08` | 只收到目标 CAN 帧，无质量/比较/归档 | 不产生有效 Wave 断言 |
| `W-REC-09` | 只有普通 `WaveAnalyze` / `Expect` 字符串检查 | 不授予 `VerifiedWaveComparison` 生产证明 |
| `W-REC-10` | 旧 `AssertSignal` / `WaitUntil` 正常执行 | 现有 `DirectDbcSignal` 逻辑不回归、不接受错误类型 |
| `W-REC-11` | WAVE 归档正常但全局 raw timeline 溢出 | Run PASS 受阻，明确 source/raw incomplete |
| `W-REC-12` | 写磁盘失败、取消/中断、软件意外终止 | 仍可检索未完成事实；不得形成伪完整报告 |
| `W-REC-13` | 完整导出 HTML + JSON + Step CSV | 一致展示同 RunId/Occurrence、波形结论、参考 SHA、异常区间、附件完整性 |
| `W-REC-14` | 原始 CAN 样本没有对应 source evidence ID 的归档字节 | 禁止原始数据来源不明的正式 PASS |

**特别回归**：本轮审查的 `WaveComparisonPendingArchive` / `RunStepFact.Validate` 合同冲突必须专门有一条**先红后绿**集成测试；不得通过给 `RunStepFact.Validate` 随意加入所有字符串来“修复”。

## 4. 现场标定（W-FIELD-PRE）——先测系统能看到什么

现场台架必须由用户或 R1 实测执行者在真实 Windows/主控/ECU 环境完成；本研究没有 Windows 设备和实体线束的执行记录。

### 4.1 采集预检

1. 检查项目、有效 DBC Asset ID/Rev/SHA、当前 DUT 型号和真实 ECU 硬件/固件版本，不能使用只为测试合同而创建的伪造 DBC。
2. 确认工位主控 USB 已连接，ECU CAN 使用对外 A12(H)/A1(L)，与 KEY CAN B11(H)/B1(L) 区分；确定本次是测试工程里的正确 BusRole。确认 ECU CAN 0x43 波特率实际读回，未确认时不建议正式测试。
3. 在只读 CAN 监视模式观察目标 CAN ID、Extended flag、DLC、报文数据与 DBC 物理值，与已知当前 ECU 状态一致。确认目标信号刷新规律和“停报”语义。
4. 在轻负载与正常工位负载分别记录目标信号 `inter-arrival` 的最小、中位数、p95、p99、最大间隔及无帧窗口；统计 `HardwareCanReceiveBridge.DroppedBeforeDecode`、`CanFrameHub.TotalDropped`、`LiveDbcSignalService sink drop` 以及 `RealRunEvidenceRecorder.DroppedEvents` 的**本次增量**。不可由累计计数直接推断本次丢失。
5. 明确本用例的 trigger 是 CAN 已观察到的信号事件，还是 KEY/IGN 实际外部动作；如果只有 USB ACK，将响应时间验收标记为 `TIME_UNVERIFIED`，不能宣称按时响应。
6. 用真实 ECU 在固定条件下重复采集至少多个独立运行（具体次数由工艺/统计要求确定），测样本间隔、动作区间、稳定段和不确定度；日志含原始数据而非只用截图。

### 4.2 何时允许启用 P0 生产判定

仅在工程师审查并保存后：`RequiredSignalDataRate` 能支撑**本用例**的时间/数值精度；`EffectiveTimeCoverage` 和每关键窗口最大 gap 在预设可接受范围；时钟误差 `U` 已被真实测量或该规则无需严格时间门槛；参考批准、样件身份和固定版本已归档；异常注入测试能明确区分 FAIL 与 INCONCLUSIVE。**如果达不到条件，缩小功能目标或停留在诊断/开发模式，不随意放宽参数。**

## 5. 真实 ECU 验收步骤与报告必备结果

以下所有行必须实际执行；没有真实数据只能记录 `NOT_RUN`。条件选择以现场可控、安全、不损坏设备为前提，禁止为创造 FAIL 进行危险短路或不受控硬件故障注入。

| 用例 | 真实操作 | 预期结论 | 必存证据 |
| --- | --- | --- | --- |
| `W-F01` **真实 PASS** | 选择已确认的合格 DUT 和真实 DBC；按现有测试程序由真实动作触发；采集当前完整信号序列；参照批准参考 + 固定配置比较 | 条件全部成立时 `PASS`；终态需 RECORD 全证据门禁 | 实际捕获、原始 CAN、参考 ID/Rev/SHA、算法/阈值版本、StepOccurrence、正式报告 |
| `W-F02` **受控真实偏差 FAIL** | 在不冒险损坏 DUT 的前提下，以受控的真实操作条件/已知不合格样件制造可观察且确定违反工程规范的偏差；全程保持采样质量足够 | `FAIL`，而不是因为掉帧造成的“伪 FAIL” | 同上+违反的具体窗口、最大偏差/响应时刻、物理条件确认 |
| `W-F03` **目标 CAN 丢失** | 有控制地禁用目标帧发送或重放单次异常采样条件；确保系统能够记录丢失/停止的原始事实。不能因注入动作损坏 ECU | 缺失覆盖证明 → `INCONCLUSIVE`，正式不 PASS | drop delta、原始记录、质量门禁代码 |
| `W-F04` **错误参考** | 切换到其他已存在但不匹配的生产参考 ID/Rev/SHA 或被篡改参考文件 | `ERROR`，不可比对成 PASS | 参考 hash/ID 不符错误码、Step/Run 证据 |
| `W-F05` **错误 DBC** | 改变 DBC revision/SHA、单位/信号名或使用失配的映射副本；不可污染已锁定 Run | `ERROR` / Prepare 阻断 | DBC 原/新 SHA、配置锁定和拒绝代码 |
| `W-F06` **时钟无法证明** | 在目标对时间精度有要求而实际只具有 HostArrival、没有有效标定值/可靠 trigger 的条件下启动受控用例 | `INCONCLUSIVE`，正式不 PASS | TimeSource/Quality、clock budget 状态 |
| `W-F07` **重连/手动停止** | 捕获中合法断开 USB 或结束采集，在可恢复安全工位环境进行 | `INCONCLUSIVE/ERROR`，不会残留伪完整 capture | Connection epoch、StopReason、未完成归档 |
| `W-F08` **重启/重开** | 已完成的真实 Run 导出后关掉并重新启动 AutoTest，再打开项目和报告 | 参考/捕获/比较 SHA 一致、可追溯，不能用同名最新替代历史参考 | report HTML/JSON/CSV、RunRecord、ArtifactRepository 实物校验 |

**说明**：若 `W-F03` 使用的动作只能证明设备断开而不形成目标报文的精确缺失，则记录为 `DISCONNECTED / INCONCLUSIVE`，不要写“已验证特定丢帧计数”。异常必须由确实观察到的事实描述，不以测试脚本的意图代替采样证据。

## 6. 现场测试证据包格式（实施者逐项填写，不得代填）

```yaml
test_id: W-F01                   # W-F01...W-F08
execution_state: NOT_RUN         # NOT_RUN / EXECUTED / REJECTED
source_type: REAL_WINUSB_CAN     # never REPLAY for a field acceptance
utc_start: null
utc_end: null
operator_confirmation: null
repository:
  r1_commit: null
  wave_implementation_commit: null
  prepared_test_plan_sha256: null
station:
  station_id: null
  main_control_hw_revision: null
  main_control_firmware_version: null
  main_control_firmware_commit: null
  ecu_can_port: "A12(H)/A1(L)"
  ecu_can_bitrate_requested_kbps: null
  ecu_can_bitrate_readback_kbps: null
  usb_session_generation: null
dut:
  ecu_model: null
  ecu_hardware_revision: null
  ecu_firmware_version: null
  dut_identity: null
resource:
  dbc_asset_id: null
  dbc_revision: null
  dbc_sha256: null
  bus_role: null
  can_id: null
  extended_frame: null
  signal_name: null
  unit: null
  reference_asset_id: null
  reference_revision: null
  reference_sha256: null
  approval_record_sha256: null
  comparison_profile_sha256: null
  algorithm_version: null
capture:
  time_source: null
  time_quality: null
  trigger_provenance_id: null
  clock_uncertainty_evidence: null
  sample_count: null
  valid_time_coverage_ratio: null
  min_median_p95_p99_max_gap_ms: null
  usb_predecode_drop_delta: null
  can_hub_drop_delta: null
  decoded_signal_drop_delta: null
  recorder_dropped_event_delta: null
record:
  run_id: null
  case_attempt_id: null
  capture_start_occurrence_id: null
  capture_stop_occurrence_id: null
  comparison_occurrence_id: null
  raw_can_timeline_sha256: null
  actual_wave_evidence_id: null
  actual_wave_sha256: null
  comparison_evidence_id: null
  comparison_sha256: null
  archived_reference_sha256: null
  artifact_integrity: null
  verdict: null
  reason_code: null
  report_html_path_sha256: null
  report_json_path_sha256: null
  report_steps_csv_path_sha256: null
validation:
  all_source_can_evidence_matched: false
  report_reopen_verified: false
  approval: PENDING
  notes: null
```

所有 `null` 是**未测**，不是默认通过。正式记录与报告必须包含或能引用绝大多数上述字段；缺失项在 Gate 评估时标示为 `NOT_PROVEN`。存路径或证据 ID 而无 SHA/独立校验，不足以证明原始数据持久化。

## 7. 最终审查（Go/No-Go）

### Gate G-WAVE-0：候选代码审查

- W-00 各缺陷的具体 diff + 单元/集成“先红后绿”测试留档；
- 所有新/旧 WAVE、TEST、RECORD 接口兼容路径明确；
- 无新建模拟 USB / 第二 DBC / 第二 RECORD；没有新的固件要求；
- 没有把旧实验参考悄悄提升生产、没有把时间自动对齐作为生产默认；
- `R1 HEAD` 更新后的冲突复查完成；
- 现有 `AssertSignal/WaitUntil` 和普通 TEST 运行回归通过。

### Gate G-WAVE-1：真实 Windows+ECU 场景验收

- `W-F01` 真实 PASS、`W-F02` 真实可证明 FAIL、`W-F03~F06` 质量/身份/时间不可判或错误完整；
- `W-F07/08` 中断和重开结果有证据，必要时执行；
- 每一个 WAVE 结果均可由当前 Run/Case/Occurrence 回查真实 CAN 帧、完整波形、参考 ID/修订/SHA、比较参数/版本/异常区间；
- 真实接收负载/计时能力已实测，且“本用例阈值 ≥ 可证明测量能力”；
- 由用户实际体验审查步骤选择/属性编辑/报告可读性。

**当前 Gate**：`G-WAVE-0 NOT_RUN / G-WAVE-1 NOT_RUN / FIELD_NOT_VERIFIED`。本文件是验收蓝图，不是虚构现场报告。

## 8. 提交与协同规则

- 本研究分支只保存调查、技术方案和审查依据，不向 `R1`、`main` 推代码或自动合并；
- 待 R1 接受接口合同后，WAVE 专项才可能提交最小垂直源代码候选，必须随提交提供精确 diff、编译日志、离线测试证明、回归说明；
- R1 整机实施者负责将合同接入真实 HOST/RECORD，用户负责真实台架验证与体验确认；
- UI/UX 专项可使用 W-02 字段合同设计白底现代工具窗口与右侧 Inspector，但图形/视图设计**不得影响正式裁判逻辑**；
- 真机未执行前，所有“PASS”只能明示其为**算法级**或**离线接口级**，不能写作“真实 ECU 验收通过”。
