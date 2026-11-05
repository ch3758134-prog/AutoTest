# G4-01 · 源码实施与配置身份

> **当前 SAEF 工作产品状态：DRAFT（归档实施输入，不表示源码已完成）。** 权威状态以 `engineering/control/work_products.json` 为准。G4 Gate 仍为 `NOT_REVIEWED`。

**目标**：维护已批准设计对应的源码与修改清单。

**必要证据**：真实源码仓库、编译/测试结果、提交 ID、文件清单和配置身份。

**准出要求**：实际实现受控且未越过批准范围；不能仅依据一份实施方案宣布本工作产品完成。

## 输入批准设计

- `G3 v0.2` 已在用户 SAEF 落档：G3-01～08 全部 `ACCEPTED`，G3 Gate `PASS_WITH_LIMITATIONS`；文档输入 HEAD `5de99332170d9434c7e5b4a15c9d091245a7302c`。
- `G4 v0.2` **实施方案**已由项目负责人在对话中批准；本次作为 **G4-01 受控输入附件**归档，而不是 G4-01 的实现完成证据。
- 附件入口：[批准对象及边界](annexes/G4_v02_approved_plan/00_APPROVAL_SCOPE.md)、[全文](annexes/G4_v02_approved_plan/01_PLAN_MASTER.md)、[分支与集成总控](annexes/G4_v02_approved_plan/02_BRANCH_COORDINATION_MASTER.md)、[矩阵导航](annexes/G4_v02_approved_plan/03_MATRIX_INDEX.md)、[验证与准入](annexes/G4_v02_approved_plan/04_VERIFICATION_AND_PHASE_CONTROL.md)、[原始参考归档定位](annexes/G4_v02_approved_plan/05_SOURCE_PROVENANCE.md)。

## 变更文件与提交

- 本轮仅登记 G4 方案为 `DRAFT` 输入，不创建新的 Windows HOST 源码 Git，也不宣称可编译。
- 真实 HOST 源码提交：`NOT_CREATED`；首次源码 Git 身份须由后续独立授权的初始化工作包提供。

## 实现关系与技术约束

- 继承 G3 九程序集架构、17 模块、147 个业务文件、23 CT、101 验证及 16 项开放实验。
- G4 v0.2 增加 54 项工程与测试责任，共 201 项候选路径；11 个开发方向及唯一共享入口所有者。
- 主对话负责模块派工、完整源码和测试代码、跨对话接口、集成决策；Codex 仅机械应用、构建、测试和原样回传，用户现场运行验收。

## 构建与测试证据

- 新 HOST 工程：`NOT_CREATED`；编译、WPF、Mock、USB、CAN、实机均 `NOT_RUN`。
- 方案的静态检查不等于源代码测试或实际台架通过。

## 差异与缺陷 / 回退

- 当前已有的 G3 16 项开放实验继续适用，必要的真实物理输出先履行专项前置条件。
- 任何测试推翻合同须经受控变更；G3 已批准正文不得在本次交付中修改。
- 若 SAEF 输入 SHA、HEAD、工作产品状态或承载文件不一致，归档立即停止，不自动修复。
