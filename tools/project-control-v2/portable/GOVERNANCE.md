# Project Control｜通用研发计划、变更与审查语义

管理 AI 拿到通用系统文件夹和目标 GitHub 项目后，应先进行实际工程调查，组织需求、研究、设计、实施、验证与交付。软件的角色、工程阶段、任务状态及用户权限是不同维度，不能互相替代。

## 核心实体

- **Project**：项目身份、被管理 GitHub 仓库以及真实资产边界。产品定义应简洁稳定；任务、路线图与具体型号背景不应硬塞进产品定义。
- **Asset & Evidence**：已有工程文件、历史提交、设计、CI、数据和外部来源。OBSERVED、CLAIMED、APPROVED 和 UNKNOWN 不能混淆。
- **Requirement**：需求陈述、依据引用与验收标准；默认 PROPOSED_NOT_APPROVED。
- **Deliverable**：交付成果及其完成条件，不等于已经交付。
- **Work / WBS**：工作性质、预期结果、负责人和前置依赖。研究与设计各有自己的责任，不允许管理 AI 随意替其他专业 AI 完成全部工作。
- **Plan**：一组可追溯的 Work，包含目标、退出条件及 WBS/交付快照；可提交给负责人审查，不代表实施授权。
- **Change & Impact**：变化的事实和来源，从确切受影响的工作出发，沿依赖关系计算下游工作及关联交付，不全项目重置。
- **Gate Review**：阶段审查请求及证据，不是 PASS；正式批准只能依据真实人类决定和可核对的证据。
- **Codex Task**：实际执行合同，在目标 GitHub 仓库独立授权后进入 READY；Codex 不自改任务权限、设计或审批文件。
- **Receipt / PR**：执行者回执、真实候选提交和 GitHub PR；代码范围核对、CI 核对、成果验收、正式合并分别记录。

## 本地管理内核已经提供的受控操作

通用管理 Web 服务的 governance.py 可记录候选需求、建立工作依赖并拒绝循环、创建和提交计划、计划审查与修订、发起变更并计算影响、对受影响计划要求重审、提出 Gate 审查请求。所有写入仍受本地令牌、修订号、跨进程协作锁和 Git 数据来源校验约束。

**此处的 REVIEWED_PENDING_EXTERNAL_APPROVAL 并不是批准；Gate 请求的 REQUESTED_NOT_APPROVED 更不是 PASS。** 管理 AI 必须由目标项目的实际人类负责人批准正式决策，再通过受控 GitHub 交付入口记录依据。

## 与 GitHub 的关系

通用工具不默认自动把本地 management.json 推送到 GitHub。管理 AI 在真正的目标 GitHub 仓库维护项目最新记录，GitHub 文件 SHA 和分支 HEAD 是并发与来源校验依据。Codex 任务发布可由 task_publication.py 本地预检及可选 github_bridge.py 执行单文件 SHA-CAS；提交 PR 的状态候选由 task_submission.py 准备。**这些都不保证跨多个文件的完整事务原子性。**

AI 不应该只靠 JSON 里的 APPROVED 字样授予自己权限，必须核对真实责任人/批准来源和 GitHub 连接所允许的操作。未能核对的状态保留 UNKNOWN 或待审批，不应写 PASS。

## 当前不足

- GitHub 原生的身份/权限/Review/分支保护目前没有形成完整的可验证闭环；
- 旧 V2 的复杂资源与计划模型尚未全部迁移进通用数据模型；
- 真实项目对审批层级、设计决策、专业分工和交付责任的要求可能不同，不能硬编码成固定的 P0–G4 Gate 模板；
- Web UI 的本地草案不等于跨机器实时状态；Windows 真机和两个真实 GitHub 目标项目的长周期完整验收仍未完成。

**以上不足不应该被提示词硬禁止而隐藏，应当作为实际未完成工作继续推进。**
