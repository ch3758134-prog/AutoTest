# G4 v0.2 实施方案批准记录（非 G4 Gate）

- 项目：AutoTest；对象：`AutoTest G4 v0.2 全量实施设计`；版本：`v0.2`。
- **批准事实**：项目负责人于 **2026-11-04** 在当前 ChatGPT 对话回复“行吧，批准了”，明确接受上一条提交的 G4 v0.2 实施方案。这是用户声明的批准，不是密码学签名。
- 本归档状态：`APPROVED_IMPLEMENTATION_PLAN`（本地项目内容标签；**不属于 SAEF Gate 枚举**）。
- **SAEF G4 Gate：`NOT_REVIEWED`；G4-01 仍是 `DRAFT`，G4-02～G4-07 不应由本次普通交付变更状态。**
- **授权范围**：G4 组织分工、201项候选文件责任、23项合同闭合、25类公共类型、旧源码迁移约束、测试和并行交付方法，作为准备实际工作包的上游依据。
- **不含**：直接授权建立新 HOST 工程、写真实 HOST 源码、修改已批准 G3 或双板固件、台架控制、真 ECU 刷写、G4-01～07 接受或 G4 Gate 通过。
- 当前源 HOST 工程及源码 Git：`NOT_CREATED`；测试：`NOT_RUN`。
- 上游 SAEF Git HEAD（用户实际本机 G3 落档回执）：`5de99332170d9434c7e5b4a15c9d091245a7302c`，此值是**本次落档前**的文档仓库基线，真正落档后需记录新的 Git HEAD。
- 已批准 G3 HTML SHA256：`4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`。
- 本次批准的完整 v0.2 原始内容 ZIP（外部交付包自带，**不复制进 SAEF 仓库**）：`AutoTest_G4_v02_REUPLOAD.zip`，SHA256：`76c375140f746b9422c4542d0f06307354f9229b3c04e6a7b833e9c1fb5dca2a`。
- 获批的主体内容 `G4_IMPLEMENTATION_MASTER.md` SHA256：`84fd3d3e087867187e9dc789d2bbf8cc45e2765002046a50f33747b5a1f7684f`；HTML SHA256：`bf1b8898cd30eddb7715b9f432527bee82743c5da0dfd393c30b7e59b46bb2ea`。
- 原始 114 个归档成员全部保留在外部参考 ZIP；其中 11 个分支简报及所需矩阵、现场流程按文字或表格形式归入当前 SAEF 工作产品附件。
- **来源时间语义**：原文中 `CANDIDATE / NOT_AUTHORIZED` 是批准之前的文档快照，不回写历史原件；以本批准记录判定“方案已批准”，以每个后续工作包的独立授权判定“代码能否开始写”。

## 权限与证据限制

SAEF 2.0 `record-authority-decision` 只支持提交评审、接受工作产品、退回草稿和批准 Gate，**不提供单独的“批准实施方案”动作**。因此本记录使用受控工作产品附件保存批准事实，不伪造 `SAEF-authority-decision/1` 审批状态，也不把未实施的 `G4-01` 填成 `ACCEPTED`。

本记录可追溯到原始交付包和负责人会话决定；G4阶段的实际实施与验证仍将单独向 G4-01～G4-07 提交证据并通过 Gate 审查。
