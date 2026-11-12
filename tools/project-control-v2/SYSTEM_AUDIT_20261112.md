# Project Control 4.6.1｜全系统审查、修订与集成核对（2026-11-12）

## 审查结论和范围

本次审查由负责人要求：“项目管理 AI 角色扮演验证可接受，全面审查管理系统，设计修改后直接接入 main。”这项指令**授权管理工具源码修订及集成**，不构成 AutoTest 产品需求批准、任何待审工程成果的内容批准、受管项目 GitHub 记录公开、正式软件发布验收或 Codex 执行授权。

审查对象为 `tools/project-control-v2` 的入口、完整候选流程及 4.6 工程分支（原自 main `4142ef8b6f64508b8b25d7d4ce4ea140c96264a7` 起），不包含 AutoTest 业务源文件。

## 一、覆盖矩阵

| 系统子域 | 实际检查与判断 | 本轮动作或限制 |
| --- | --- | --- |
| 初次接管和产品目标 | START_HERE、INITIALIZATION_PROTOCOL、project-init 的 Git 身份/来源边界；不应从源码倒推产品定义 | 保持先调查项目目的、再判断当前工作；新对话真正冷启动仍待验证 |
| 岗位与 AI 工作方法 | `ai/roles.json`、两个 Skill、REPORTING_PROTOCOL；岗位不自动安装，不能用“继续”取得额外权限 | 延续协调者组织研究与审查、专业 AI 限定工作、Codex 机械执行的责任分离 |
| 来源与现行有效性 | `context_assessment.py`、`context_workflow.py`、原 `context_discovery.py` | 仍区分搜索无结果、旧资料存在和当前生效；旧 /1 只供历史读取 |
| 证据变动对工作影响 | 旧实现只要产生新 context_review 就标记全部已有来源工作过期 | **已修复**：按相关主题的来源、主张、查询与补全行动比较，只影响真正变化的下游工作和计划；保留未变更工作的原始来源 |
| 证据与工作链接 | 旧检查可能容许 action_id 虚构，但挂在某个真实 review_id 与工作上 | **已修复**：补全行动必须存在于它所关联的原始评价，不能伪造追溯来源 |
| 工程过程 | process_catalog 1.2.0、process_control、PROCESS_SYSTEM | 保留通用可迭代工作过程、不固定 AutoTest 的 P0～G5 为通用唯一模板；原目录 SHA 不变，避免隐式政策迁移 |
| WBS/计划/变更 | governance 的任务依赖循环、当前证据有效性及计划审查请求 | 候选可审不等于正式批准；仍有跨文件事务与可信身份缺口 |
| 成果候审、负责人审阅 | 4.6 `delivery_review.py`、独立审阅入口、私有存储、前端全文审阅 | **已修复**：私有候审稿可从 AI JSON 导入/预览/修订；增加“已阅读但不发布/不批准”；禁止 NO_EXTERNAL_PUBLICATION 获得公开候审许可 |
| GitHub 远端同步 | management_sync / git_ref_atomic / github_bridge 与 GitHub connector 的实际写能力 | 继续实施显式 Git 来源/文件 SHA/CAS，**不能**把本机审阅令牌理解成对 GitHub 连接的强制限制 |
| Codex 任务/回执 | task_publication 的唯一 READY、design blob/人类决策先决、workspaces/receipts/PR 观察 | 软件可结构性核对，不能独立证明决策真实性或物理验收；不自动发任务 |
| 用户工作台 | web_server、app.js/index.html/css，首页/过程/候审/证据视图 | 修复无入口导入 AI 候审稿；管理读权限、独立候审决定权限分开；默认无远端发布 |
| 发行包 | PORTABLE_MANIFEST 与 export_package 安全路径/ZIP 策略 | 运行时必需文件逐项核对；包不应包含 AutoTest 数据或本地私有候审 |

## 二、主要失效与修复

### 1. 已批准不等于已向用户展示

旧管理 AI 将两个 Issue #22 评论直接发送 GitHub 后称“交付”，用户未看到可阅读的成果也未批准。4.6 候审流程分离为 `DRAFT_INTERNAL`、`AWAITING_OWNER_REVIEW`、`RETURNED_FOR_REVISION`、`ON_HOLD`、`RELEASED_FOR_CANDIDATE_PUBLICATION`；4.6.1 新增 `REVIEWED_INTERNAL_ONLY`。所有负责人的本地决定绑定精确正文 SHA-256、正文修订、私有记录 CAS，不产生正式工程批准和 Codex READY。

### 2. 内部成果不是可公开 GitHub 管理文件

候审完整正文保存在目标仓库本地 Git 元数据的 `project-control/deliveries.json`，不进入可同步的 `.project-control/management.json`；普通模型 GET 仅返回数量。AI 可以保存本机候选，再通过受控私有接口展示全文；对外发布仍需要独立受控 GitHub 操作。克隆/删除 worktree 并不自动传输私有稿件，需要以后独立设计安全备份与迁移。

### 3. 研发工作不应无关地被全部重置

此前新证据记录会让所有与旧评价有关的工作失效。本轮通过 `topic_fingerprint` 和原始来源链接缩小影响范围。仅当某项工作的依据主题、具体来源/论证/检索或相关补全行动真正变化时，将该工作及关联的待审计划要求复核；未变更的工作继续保留原始凭据，且标注沿用评价 ID。新反例测试覆盖不相关变化和造假 action_id。

### 4. 审阅页应能真正使用，而不只是状态展示

此前可以打开候审列表，却缺少从 AI 成果建立/修订候选的 UI。现有“导入／修订 AI 候选成果”入口允许粘贴由 AI 准备的完整 JSON，先预览**完整正文**再写入本机私有草稿，旧审阅许可经修订自动失效。负责人不需自己撰写需求正文。纯本机工作流不能代替 ChatGPT 对话中的实际审阅和 GitHub 受控发布。

## 三、验证证据与不得越权的结论

- 4.6 候选分支相对原 main 为**前进关系**，无业务源码变更（以真实 compare 结果核对）。
- `delivery_review.py` 通过连接器读取后精确复制进本地 Python 3.13 环境，`git hash-object` **完全匹配远端 Git blob `5f2db15631fc5dd393c7ed6e74ad1ee69676a11a`**。在这个准确版本上执行 15 个围绕草稿、候审、权限、旧版本、修订、内部已读和内容摘要的本地定向测试，15/15 PASS。
- GitHub 上新增/修改了相应单元测试文件；其他部分做了源文件结构及 JavaScript 语法检查。**不将上述核对称作全部测试通过**。
- 当前环境无法直接从目标 GitHub 克隆源码（DNS 不可用）。完整 `python -m unittest discover`、真实 HTTP 集成和 Windows WPF 无关的管理 Web 浏览器操作**尚未执行**；无自动 CI 或 AutoTest 物理测试。

## 四、后续缺口与合理的下一次验收

1. 以精确 main HEAD 在本地运行 `python -m unittest discover -s tools/project-control-v2/tests -v`，保存原始失败输出，不让 Codex 自行改变设计。
2. 运行 `export_package.py` 输出 ZIP 并核对所有 manifest 文件和 SHA，再在解包目录启动本地工作台。
3. 测试无项目、新项目、旧项目接管、不同 profile、私有全文预览、退回/内部已读/候审许可、版本冲突、Git 远端预览、真实多工作树等环节。
4. 正式产品内容批准和审阅人的**可信身份验证**尚未通过本地令牌实现；GitHub 直写权限还可能绕过私有审阅器，需要仓库规则与受限发布者配合。
5. ChatGPT 网页端无法直接访问用户本机的私有审阅存储；必须继续先在对话中展示完整可审成果，获得实际针对版本/目标的许可，再调用可用 GitHub 工具。不能把内部候选状态当作用户已经阅读或批准。
6. 当前角色扮演测试曾得到用户认可，但不是无历史记忆的新对话独立冷启动；后续仍应做真正独立场景验证。

## 五、集成界限

本次由用户明确要求**修订管理工具并直接接入 main**。这项许可足以合入通用管理系统的代码候选，不提升 `PORTABLE_MANIFEST` 到“正式发行 PASS”，不修改 AutoTest 软件或旧人类批准，不建立新的正式 G1/交付/编码授权。