# Engineering Workbench V1.2｜淘汰四字段“产品定义”的正式发布通路

> 2026-11-10，管理分支 Draft PR #28。此版本为候选，不等于 AutoTest 正式产品定义已经被改写。本机正在使用的 Windows SQLite 未受 GitHub 提交自动影响。

## 1. 本轮直接淘汰的旧机制

V1.1 虽然有 13 类语义手册、独立工程认识和候选关联，但 **旧 `DefinitionGovernance` 仍将 `projects.summary/body/version/variant` 四项当成同一产品定义发布**。旧 UI 强行展示十领域/全工作包矩阵，管理 AI 上下文仍输出旧定义审查队列，从而可能让 AI 重新回到错误的工作方式。

V1.2 正式不再以旧机制管理产品身份：

- `/api/studio/governance/*` **POST** 统一返回 410 Gone，旧四字段发布、研究、审批、候选都不能再从本地 HTTP 触发。保留旧记录的只读 GET，供迁移对照。
- 删除原 `studio_governance_ui.js`、`studio_definition_research_ui.js`、`studio_definition_suggestions_ui.js` 三份浏览器源文件及其脚本引入、服务端静态路由和 CI 检查。旧界面不再是正式产品入口。
- AI 项目管理摘要移除旧四字段定义和固定研究事项作为权威上下文，并改用 `product_identity` 和 `engineering_knowledge`。
- 旧 `studio_governance.py` 等业务内核暂作为**历史记录兼容与恢复结构**保留。Python 文件并非可信的访问隔离边界；可直接写 SQLite 的本地进程仍有数据库操作能力，不能声称彻底隔离。

这是切换工程真相来源，并非丢弃用户已有的批准、来源或运行记录。旧已发布数据也**不自动**成为新的产品身份版本。

## 2. 新产品身份仅管理产品身份

`studio_product_identity.py` 新增独立表：

- `product_identity_baselines`：唯一正式身份声明及修订、来源提案、签发人和理由。
- `product_identity_proposals`：AI/负责人身份候选、语义解释、来源、关联工程认识以及期待的旧身份修订。
- `product_identity_impacts`：本次身份变化对**实际工程对象**的单独判断、对象修订、原始证据与解释。
- `product_identity_invalidations`：已明确受影响的工作包及经过批准的下游依赖在身份发布后必须重新验证。

产品版本、变体、当前 EPB 验证安排、现场 CAN 通道、详细功能清单、测试证据均不与 `product_identity_baselines.statement` 一起发布。

AI 可提交身份候选，不能审核范围、签发正式身份或直接作出工程影响结论。人类可解释本次变更的实际范围、对具体对象作 `IMPACT/NO_IMPACT/UNKNOWN` 审查，并显式发布。未解决的 UNKNOWN 或受影响对象修订变化会阻止发布。**影响对象列表为空并不证明没有影响**；签发时仍需核查申请人/负责人所写研究范围与证据。

正式产品身份变化只对明确有影响的实际工作包及受控依赖路径进行下游失效处理，不应使全项目仅因“新讨论”而停工。

## 3. AI 执行影响分析不应变成用户填表

产品身份候选可关联多个独立工程认识 ID。管理 AI 已经能够提出带有 `related_objects`、来源和解释的工程知识记录。

产品身份审查页会把**与当前提案明确关联的工程知识**投影为“待核查影响线索”，并允许负责人直接采用其相关对象、解释和来源作为审核的起点。AI 不得借此把 `IMPACT` 当成自动审批事实。未与当前提案关联的历史知识不会被无差别推入影响列表。

这里没有为每个概念再造独立表单或固定流程；同一份语义知识模型服务于产品认识、研究、关系与影响分析。

## 4. 项目背景、阶段与变体不再被产品定义锁死

历史 `projects.summary` 只读封存，不再支持直接修改；它也不是现役产品身份。项目的 `name/body/version/variant` 现在可独立由 owner 更新并审计，存储到 `project_context_revisions`。旧项目首次编辑时保留编辑前的历史快照；新项目创建时记录首版。修改 R1→R2 或当前变体不需要伪造一项产品身份变化。

HTML “修改项目”打开独立的项目背景/阶段页面。原有创建项目流程继续可用，但新项目的 `summary/body` 只是初始历史文本；以后产品身份必须通过新的独立身份审查建立。

## 5. 本地 HTTP 入口

| 端点 | 受限管理 AI | 本地 owner |
|---|---|---|
| `GET /api/studio/product-identity?project=...` | 只读 | 只读 |
| `GET /api/studio/product-identity/detail?id=...` | 只读 | 只读 |
| `POST /api/studio/product-identity/propose` | 可提交候选 | 可提交候选 |
| `POST /api/studio/product-identity/review-scope` | 禁止 | 复核实际研究范围 |
| `POST /api/studio/product-identity/assess-impact` | 禁止 | 判断具体工程对象影响 |
| `POST /api/studio/product-identity/publish` | 禁止 | 发布正式身份版本 |
| `POST /api/studio/product-identity/reject` | 禁止 | 驳回 |
| `POST /api/studio/product-identity/resolve` | 禁止 | 复核受影响工作包 |
| `GET /api/studio/project/context-history?project=...` | 只读 | 只读 |
| `POST /api/studio/project/update` | 禁止 | 修改项目背景与阶段 |
| `POST /api/studio/governance/*` | 已退役 | 已退役 |

现阶段仍是本地能力令牌，不是可信独立 AI 身份。AI 人工复制上下文、提交候选或在 HTML 导入工程知识仍然是可用但未彻底自动化的交接方式。

## 6. 数据与兼容边界

- 导出增加 `product_identity`：基线、候选、已审查对象影响和待重新验证工作包。
- 导出增加 `project_context_history`：阶段和背景独立历史。
- 恢复到**空工作区**时，验证产品身份修订、批准提案、影响对象历史修订、来源关联、下游失效项及上下文版本完整性。
- 旧归档没有新表的章节仍能恢复，但不会自动产生 `product_identity` 正式身份。必须经负责人实际讨论与签发。
- 原有 `definition_invalidations` 历史仍被保留。它可能包含曾批准变更后待复核的实施风险，不应在未经显式迁移审查的情况下静默取消；这部分待单独形成清理决议。
- 数据迁移必须先在用户 SQLite **备份副本**验证。GitHub 提交不会自动修改 Windows 本地数据。

## 7. 真实场景验证与遗留工作

新增 `test_studio_product_identity.py` 及 HTTP 测试：身份签发不碰 R1/EPB 元数据；AI 无权审批；有 UNKNOWN 不允许签发；对真正受影响的工作包作精准失效；过期基线阻止重复发布；受控工程知识须已审查；新旧 JSON 往返恢复；项目上下文更新不影响身份版本；AI 知识对象关联只能给出待核查影响线索。

**仍未完成的部分**：

1. Windows 上本地完整浏览器点击、工作区原始数据迁移及现场使用体验审查。
2. 所有旧式工程对象的归一化、完整跨域影响推导和对真实 AutoTest 项目的详细需求/设计持续梳理。
3. ChatGPT 对话到本机管理系统的受限直连、可信身份分离和 GitHub 真正强制的受保护分支门禁。
4. 真实 AI 对 14 项语义评估案例的实际回答、人工评分与失败回归。
5. 旧定义历史失效项及其审批语义的逐项清理：不可以简单“删除旧表就是已释放编码”。

R1 实际软件与硬件产品完成度不是本管理系统 CI 所能证明的。
