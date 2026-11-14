# AutoTest｜G3 DEVICE/WAVE/TECH-INTEGRATION 第一批专项详细设计正式批准记录

**批准日期：** 2026-11-14  
**项目负责人明确原话：** “批准”  
**关联交付：** 紧随 AutoTest 本轮 G3-DEVICE、G3-WAVE、G3-TECH-INTEGRATION 三份详细设计与集中候审包的正式请求；批准对象限定为这三份 **v0.1** 工作产品。  
**批准类型：** G3 专项详细设计通过，**并非 G3 整阶段 Gate 准出**，**并非 G4 实施授权**。  
**正式上游：** [G2 Gate v0.1](G2-GATE-REVIEW-v0.1.md)，[G2 批准记录](G2-APPROVAL_RECORD.md)；G1 v0.3、PM-DELIVERY-01 v0.1。

## 一、获批工作产品（逐字保留原候审正文）

| 工作产品 | 正式路径 | 原始文件 SHA-256 | Git Blob SHA-1 |
|---|---|---|---|
| G3-DEVICE-01 v0.1 | [G3-DEVICE-01-v0.1.md](G3-DEVICE-01-v0.1.md) | `5678d158818b6daf10831cf1268f77693989d4980ee78494612a38bc42163c6b` | `f8ea93cc9a16b3ab733b6ddc0207d3f022a8b04a` |
| G3-WAVE-01 v0.1 | [G3-WAVE-01-v0.1.md](G3-WAVE-01-v0.1.md) | `71f04c470f28a9ac13ed0eebbe4b1785f65b0aefdbd6072ba414dd2f42cfa0cc` | `926bb1d84d2d96e9a4ebc030ede8ede4c132feba` |
| G3-TECH-INTEGRATION-01 v0.1 | [G3-TECH-INTEGRATION-01-v0.1.md](G3-TECH-INTEGRATION-01-v0.1.md) | `9f98330ada909548cf2f63ab57047b51558fddbd4328464ea0ad404330ab3c17` | `a1c6822c43a597330064861f56882ae5e9d846e9` |

提交原文顶部的“候审/未批准”历史状态保留，不直接修改获批正文；正式状态以**本批准记录**、项目负责人明确决定和 Git 提交历史为准。

## 二、正式批准范围

1. **DEVICE：** 批准主控固件、KEY_SIM 与 HOST 静态事实分类、USB/CAN/IGN/KEY/SOL/电源差异调查、请求/ACK/GPIO/独立现场证据/UNKNOWN 的分层模型、工位会话与授权边界，以及 D-00～D-04 后续实施候选和现场验证矩阵。
2. **WAVE：** 批准以现有真实 CAN/DBC 观察链为依据的波形采集、参考来源审核与不可变修订、质量/时间来源判断、明确的 Pass/Fail/Inconclusive 对应 TEST 语义、A2 项目/包/步骤证据关联，以及 W-00～W-04 后续实施候选。
3. **TECH-INTEGRATION：** 批准 UI 专项与后端研发责任隔离、现有真实工程资源/会话/Run 事实源复用、跨模块接口清单、G3 缺口和进入 G4 前的审查/验收纪律。单独 UI 对话继续设计并由项目负责人实际使用后验收。

## 三、没有获得批准的行为与剩余限制

- **本批准不允许直接开始 G4 功能代码修改，也不指派任何 Codex 机械构建或真实台架操作。** 进入 G4 必须先完成相应工作包审查、实施边界与精确源码 HEAD 授权。
- 不批准 ECU 电源控制、夹具压紧/松开、IGN/KEY 的新实体试验、实体闪存擦写、SecurityAccess、固件修改或变更主控实际电源接线。
- 实际固件版本、ECU/主控供电关系、机械效果与真实采样误差尚未经现场实证；GPIO 读回不能冒充电气/机械独立确认。G2-L03/L04/L07/L08 持续有效。
- 不自动批准此前 **G3-A1、G3-A2、G3-F1** 各自 v0.1 候审设计，不视作整个 G3 Gate PASS；独立 UI 专项结果尚未交付。
- 本审批不允许整支合并 `local`、覆盖 `main` 业务源码，或凭参考/回放/模拟资料认定 DUT 实机测试通过。

## 四、立即继续的项目管理工作

- 依据本次批准落实 **工位单一所有权与 CAN1 只读诊断会话**、**A2 项目 Run/包/步骤事件**、**WAVE 比较结果与 TEST 三态结论** 的交叉详细接口审查。
- 整合 A1/A2/F1 后续审批准备；由独立 UI 专项交付其界面研究与用户认可结果，本对话负责真实后端接口和集成。
- 为后续 G4 准备独立工作包候选，包含精确 Git HEAD、文件责任、行为变化、编译/Windows/实机验证层级、失败恢复和回退方案。**不得以此批准记录替代进一步授权。**

**批准结果：** `G3-DEVICE-01 v0.1 APPROVED`、`G3-WAVE-01 v0.1 APPROVED`、`G3-TECH-INTEGRATION-01 v0.1 APPROVED`；`G3_GATE=NOT_YET_PASSED`、`G4_AUTHORIZED=NO`。
