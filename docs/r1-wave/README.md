# AutoTest R1｜WAVE 波形判定专项

- Issue：[ #71 — 真实 ECU 波形采集、参考波形与自动测试判定研究](https://github.com/ch3758134-prog/AutoTest/issues/71)
- 研究分支：`research/r1-wave-judgement-20261120`
- 最新回读的活跃 R1：`e5a9e13ad522ddaf9092f44099f2ef26bdf1e19b`（2026-11-21 研究快照；R1 如继续推进，集成前必须重新比对）
- 接管原始 R1：`94e82eddb03e69b428fd4c63c2093a570d979c4e`
- 任务入口： [START_HERE.md](START_HERE.md)
- 研究状态：`W-00~10 RESEARCH_CANDIDATE / PENDING_REVIEW`；`FIELD_NOT_VERIFIED`；`NO_SOURCE_CHANGE`

## 研究交付

| 号 | 文档 | 目标 |
| --- | --- | --- |
| W-00 | [源码能力/采样边界审查](W-00_SOURCE_CAPABILITY_AUDIT_20261121.md) | 源码证据定位，已有/接线/缺陷/真机未知，R1 阻断项 |
| W-01 | [工业方法及反误判研究](W-01_INDUSTRIAL_COMPARISON_AND_FALSE_JUDGEMENT_20261121.md) | 离散/连续信号、事件窗口、时间不确定度、三类 ECU 场景、攻击性测试矩阵 |
| W-02 | [后端接口、TEST 属性与 RECORD 合同](W-02_BACKEND_INTERFACE_AND_RECORD_CONTRACT_20261121.md) | 增量 DTO、版本冻结、质量/判定/证据门禁、最小文件改动清单 |
| W-03 | [最小纵向闭环与真机验收](W-03_REAL_ECU_ACCEPTANCE_AND_ROLLOUT_20261121.md) | 逐阶段实施、单元/集成/真实 ECU 验收、现场证据清单与停止条件 |
| **W-04** | [成熟工业产品对标、功能分层与 UX](W-04_INDUSTRIAL_PRODUCT_MODEL_AND_UX_RESEARCH_20261121.md) | NI、ecu.test、MathWorks、Vector、ASAM；定义采集/状态/范围/参考四种用途，成熟操作路径 |
| **W-05** | [时间质量、可观测性与判定数学](W-05_TIME_QUALITY_AND_JUDGEMENT_SEMANTICS_20261121.md) | HostArrival 不确定度、触发与窗口、离散/数值差异、28 个反误判向量 |
| **W-06** | [参考生命周期与正式证据合同](W-06_REFERENCE_LIFECYCLE_AND_EVIDENCE_CONTRACT_20261121.md) | 参考候选/批准/修订/撤销、Capture/StepOccurrence/raw CAN 链、RECORD 跨层阻断 |
| **W-07** | [产品级最小实施与现场验收门禁](W-07_PRODUCT_MVP_ROLLOUT_AND_ACCEPTANCE_GATE_20261121.md) | P0-A~F 实施顺序、12 个现场场景、质量与 RECORD 测试、Gate 与 NOT_RUN 模板 |

| **W-08** | [W-08｜容差来源、测量系统重复性与参考资格研究](W-08_WAVE_LIMITS_MSA_AND_REFERENCE_QUALIFICATION_20261121.md) | 以 NIST 量测系统研究方法区分产品规格、采样不确定度、样件/工位重复性与误判风险；不允许默认统一 5% 容差 |

| **W-09** | [W-09｜独立波形专业工具与实时采集工作台](W-09_LIVE_WAVE_TOOL_CAPTURE_WORKBENCH_20261121.md) | 实时监视/手动或触发录制/专业图形/参考审核/优先保存原始数据、UI 不影响记录及 15 个工具验收用例 |
| **W-10** | [W-10｜波形准确度保障与现场资格验收合同](W-10_ACCURACY_ASSURANCE_AND_FIELD_QUALIFICATION_20261121.md) | 从 CAN 字节、DBC、时间、丢帧、参考、算法到 RECORD 的 8 层准确性证明，18 个准确性故障向量和现场资格等级 |

## 管理边界

1. 研究仅在当前分支落档，未经审查不改主程序、R1、main、主控/按键板固件。
2. 当前真实输入是 WinUSB ECU CAN → DBC 信号值/时间序列（HostArrival 时间）。**不声称具备示波器模拟采样能力，也不把 ECU 上报电流值当作外部电流探头测量。**
3. 已有代码和离线算法测试不代表实际 ECU 波形 PASS；`FIELD_NOT_VERIFIED` 保持有效直到完成现场用例和原始证据复核。
4. R1 拥有整机集成/TEST/RECORD 实施；本专项只提出 WAVE 最小增量合同，禁止新建第二套解释器、DBC 解码器或 RECORD 库。
5. 现行 WAVE 的生产 PASS 证据路径有阻断项（`WaveComparisonPendingArchive` 与 `RunStepFact.Validate` 矛盾）；这不是可忽略的 UI 缺陷。

## 里程碑判据

- **研究就绪**：文档逐项映射真实源码，列出可复制的现场测试与具体增量接口。
- **实现候选就绪**：经审查后，代码编译 + 离线测试 + 完整 TEST→RECORD 归档集成证据；这仍非实物验收。
- **R1 实体验收**：使用实际 ECU/主控板/DBC、真采集、可控异常，对照通过/失败/不可判定/错误，所有 Run/Occurrence/原始 CAN/参考/算法版本/报告可追溯。

研究记录：2026-11-21 首轮 W-00~03 完整源码与工业方法调查；同日第二轮 W-04~07 完成成熟产品、时序数学、参考治理与正式验收深研，全部仅是候审研究。最新 R1 与首轮 `fa3b18e...` 相比多 54 提交，主要涉及 WAVE 证据图性能、UI、硬件安全等；核心 comparator 与跨层 RECORD 缺口仍未消除。后续实施前必须再次回读最新 R1；当前无 Windows 编译/实 ECU 现场 PASS 证据。