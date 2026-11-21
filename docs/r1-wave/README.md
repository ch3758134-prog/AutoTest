# AutoTest R1｜WAVE 波形判定专项

- Issue：[ #71 — 真实 ECU 波形采集、参考波形与自动测试判定研究](https://github.com/ch3758134-prog/AutoTest/issues/71)
- 研究分支：`research/r1-wave-judgement-20261120`
- 本次回读的活跃 R1：`fa3b18e948043a9d1960adddb627658fd40c8159`（2026-11-21 研究快照；R1 如继续推进，集成前必须重新比对）
- 接管原始 R1：`94e82eddb03e69b428fd4c63c2093a570d979c4e`
- 任务入口： [START_HERE.md](START_HERE.md)
- 研究状态：`W-00~03 RESEARCH_CANDIDATE / PENDING_REVIEW`；`FIELD_NOT_VERIFIED`；`NO_SOURCE_CHANGE`

## 研究交付

| 号 | 文档 | 目标 |
| --- | --- | --- |
| W-00 | [源码能力/采样边界审查](W-00_SOURCE_CAPABILITY_AUDIT_20261121.md) | 源码证据定位，已有/接线/缺陷/真机未知，R1 阻断项 |
| W-01 | [工业方法及反误判研究](W-01_INDUSTRIAL_COMPARISON_AND_FALSE_JUDGEMENT_20261121.md) | 离散/连续信号、事件窗口、时间不确定度、三类 ECU 场景、攻击性测试矩阵 |
| W-02 | [后端接口、TEST 属性与 RECORD 合同](W-02_BACKEND_INTERFACE_AND_RECORD_CONTRACT_20261121.md) | 增量 DTO、版本冻结、质量/判定/证据门禁、最小文件改动清单 |
| W-03 | [最小纵向闭环与真机验收](W-03_REAL_ECU_ACCEPTANCE_AND_ROLLOUT_20261121.md) | 逐阶段实施、单元/集成/真实 ECU 验收、现场证据清单与停止条件 |

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

研究记录：2026-11-21 首轮完整源码与工业方法调查。后续集成必须更新本页的 R1 基线和状态。