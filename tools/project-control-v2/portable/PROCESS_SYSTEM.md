# Project Control VNext｜工程过程与 AI 协作协议（候选实现）

本文件是管理系统通用知识，不是 AutoTest 项目范围或 P0 批准。不把 SAEF 的 P0～G5 固定为唯一研发流程。

## 1. AI 首次接管

1. 读取 `START_HERE.md`、`ai/roles.json`、当前仓库和 `.project-control/{project,management,assets,decisions,tasks}.json`；核实远端 HEAD、来源和历史矛盾，不靠旧“PASS”推断当前有效。
2. 在 `process_catalog.json` 按项目 Profile 找到对应过程；明确本次职责、必要活动、责任项和不得越界的事。
3. 角色已由用户在本对话明确时不再反复询问。没有明确委任时只能记录 `UNCONFIRMED`；不得自封项目管理 AI。
4. 形成 AI 入场报告，区分 `已核实`、`来源声明`、`冲突`、`未知`；列清下一项工作。`record_ai_intake` 只存理解与来源，永不授予权限。
5. 多对话必须在具体工作或授权前刷新来源版本；`record_ai_work_report` 只提交报告，不改变批准、任务 READY 或完成状态。

## 2. 工程过程的执行

- **过程**是工程活动及应达到的结果；**阶段**是项目采用的组织与审查节奏；**工作成果**是可核实信息或实现；**项目治理**贯穿全程。
- 过程目录含立项、现状、需求、架构、详设、实施、验证、全周期治理，可按项目 Profile 裁剪。不要在 P0 预写详细技术需求或软件架构。
- `open_process_activity` 创建具体工作和逐项责任草案，不修改 Gate。
- `set_process_obligation` 只能记录 `SATISFIED_CANDIDATE`（带来源）、`NOT_APPLICABLE_PROPOSED`（只限条件性责任）、`DEFERRED_PROPOSED`、`EXCEPTION_REQUESTED`、`OPEN`、`CONFLICT`。不适用、延期、例外必须解释影响；延期/例外还要明确责任与关闭条件。
- `review_process_activity` 记录技术审查，`request_process_review` 在结构审查充分时提出人类审核请求；它们不能签发审批。独立审查角色的声明不代表身份已验证。
- 已有项目的旧管理记录采用只读迁移，新增 `process_control` 时不重写历史批准；目录版本不同则必须显式迁移审查，不能静默升级。

## 3. 机器和 AI 的责任边界

机器校验：ID 唯一、过程适用、角色适配、所有义务有合法 disposition、证据/影响/关闭条件齐全、评审版本没有过期；提交负责人审查只产生 `REQUESTED_NOT_APPROVED`。

AI/专业审查：真实业务逻辑、来源冲突、方案论证、设计是否越阶段、证据是否足够。**AI 审查并不等于独立第三方质量认证**。

人类正式决策：由可信且能审计的通道确认精确对象、版本、适用范围和结果；本模块不实现正式批准、放行、GitHub 合并和硬件权限。

外部执行网关：继续由 `TASK_CONTRACT.md`、`target_resolver.py` 和 GitHub 的来源 SHA、文件范围及审查控制执行；*不得*因为过程责任已满足就把 Codex 任务设为 READY。

## 4. 准入与异常

- `SATISFIED_CANDIDATE` 是工程工作者提出的满足证据，不是负责人已接受。
- 责任的 `NOT_APPLICABLE_PROPOSED` 或 `DEFERRED_PROPOSED` 只是提案，即使理由充分也不能自动准出；需要有权者审查后决定哪些条件适用。首个候选版本采用保守策略：存在这些提案时阻断自动提交“已齐备”审查，保留管理讨论渠道。
- 更新义务会使本次技术审查失效，旧审查记录/请求不删除，只保留按版本核对的轨迹。
- 正式任务的批准、可用设计、实施、CI、真实实机验证互不等价；上一环节未证实不能自动跳过。

## 5. 当前限制和真实验收

当前实现是**候选工程过程内核**，本地 Web 记录是草案，不是强身份系统：网页 token 不构成人类审批身份，跨 ChatGPT 对话仍需重新读取项目，GitHub 并发和权限由外部工具/仓库控制。首次试用时应在真实新项目和已有项目验证来源、角色、义务、评审和变更；不能声称所有 AI 一定服从文本约束。
