# Project Control｜AI 项目认识与工程补全体系审查（2026-11-12）

状态：**4.4 候选设计与 GitHub 源码修订**。此文不批准新的目标项目范围、不修改既有 P0/Gate，也不是正式软件发布。

## 一、审查目标

真实 AutoTest 接管实测暴露了两个独立问题：
1. GitHub 关键词检索无匹配，AI 未及时区分“搜索索引/范围局限”和“工程资料确实缺失”；没有承担组织查缺补全的职责。
2. AI 找到 SAEF 历史 P0/设计与 `ACCEPTED` 登记后，过快把它们描述为当前有效项目定义；没有完整核对编写依据、版本、批准身份、撤回和后来变化。

4.3 的结构校验器只要求 `COVERED` 有一个 source_id，事实上无法证明当前项目认识有效。若据此安排研发，就会重新制造“先开发、后发现目标错误”的管理失效。

## 二、外部研究与选择

- OpenAI Codex 官方 Customization：AGENTS、Skills、MCP、Subagents 分别承担常驻规则、按需工作方法、外部信息和专业分工；Skills 按需加载，仓库文件不等于所有 ChatGPT 客户端自动安装。
  https://learn.chatgpt.com/docs/customization/overview
- GitHub Spec Kit：把需求为何存在、方案、任务及实施分开，研究/澄清和一致性检查可单独执行；不意味着每个任务必须走一条固定瀑布。
  https://github.com/github/spec-kit/blob/main/docs/reference/agentic-sdd.md
- Anthropic Claude Code 指南：项目指导、Skills、Subagents、Hooks 分层，避免以一份长提示词承担确定性执行控制。
  https://claude.com/resources/articles/steering-claude-code-skills-hooks-rules-subagents-and-more
- OpenAI Skill Creator：渐进披露，Skill 入口保留核心行为，长方法和领域材料放按需参考。
  https://github.com/openai/skills/blob/main/skills/.system/skill-creator/SKILL.md

**决策**：保留 Project Control 已有岗位、过程目录、任务审批和通用管理内核，增加“资料发现—证据评价—补全行动”三项协作责任。只对真正有副作用的动作施加程序/权限控制，不将研究和讨论变成固定流水线或凭字段机械批准。

## 三、已审查的既有系统及缺陷

| 子系统 | 原有问题 | 4.4 改动 |
| --- | --- | --- |
| START_HERE 与 project-intake | 强调搜索广度，但有资料就算覆盖 | 首先区分检索无结果与资料缺失、历史有效与当前适用 |
| context_discovery.py /1 | COVERED 只看来源引用 | 保留旧格式，只读解释为历史检索；新增 /2 评价器 |
| INITIALIZATION_PROTOCOL | ADOPT 主要偏向资产与源码 | 增加产品目标/使用情境、形成理由、争议与补全责任 |
| AI roles | 协调职责没有明确补齐工程依据 | 协调者组织研究、需求/设计候选及审查；专业 AI 向上回报缺口 |
| PROCESS_SYSTEM 与 GOVERNANCE | 过程责任与知识评价衔接弱 | 信息不足先组织相关工程工作，不能自动跨入编码 |
| REPORTING_PROTOCOL | 允许只报告来源和未知 | 必须解释来源的当前有效性、影响和补齐安排 |
| TASK_CONTRACT | 原有审批独立性正确 | 强化发现报告不能发放 READY / APPROVAL |
| Web 资产页 | “资产/批准”易被误看成当前事实 | 可选只读 v2 候选视图，缺失/无效时明确展示 |
| PORTABLE_MANIFEST | 4.3 缺完整评价方法与样例 | 加入新评价器、示例、指南；仍为 CANDIDATE |

未更改 process_catalog.json 的已有过程目录内容和 SHA：此前目录变更会令现有 `management.json` 的显式政策迁移检查失败。本次跨过程方法补充采用独立指导与评价器，保留现有项目政策版本安全。

## 四、数据模型与判断

资料检索尝试：`search_attempts`，记录查询方式、来源域、结果 FOUND/NO_MATCH/ERROR/INACCESSIBLE；异常不算“资料不存在”的证据。

资料来源：`sources`，记录 locator/type，便于定位，不能自行赋予权威。

关键主张：`claims`，采用 UNASSESSED、SUPPORTED_CANDIDATE、HISTORICAL_ONLY、SUPERSEDED、CONFLICT、UNKNOWN。支持当前候选需填写 reasoning、current_applicability_reason、historical_changes_checked，但这些字段**仍需专业语义审查**。

主题状态：NOT_SEARCHED → SEARCH_INCOMPLETE / SOURCES_FOUND；在形成合理评价后为 ASSESSED，或在充分找寻并提出补齐行动后为 GAP_CONFIRMED；不适用以理由描述。**非自动线性状态机**，新的反证可以退回调查。

缺口行动：RESEARCH_SOURCE、REVIEW_EXISTING_ARTIFACT、RECONCILE_HISTORY、DRAFT_MISSING_ARTIFACT、VERIFY_REALITY、OWNER_DECISION，全部以 PROPOSED_NOT_AUTHORIZED 进入审查，不能自动成立任务。

任务准入：只能在本次工作所需的主题确有候选有效依据且未被冲突覆盖时提出 CANDIDATE_SCOPED_WORK。仍须单独评审设计、源版本、真实人类任务授权。通用程序不验证“用户确实批准”，不执行 GitHub 写入或硬件操作。

## 五、测试策略及当前实际证据

新增 `tests/test_context_assessment.py` 覆盖：一次空检索不证明资料缺失、两种方法及补齐提案、检索 ERROR 不当成缺失、已发现未审查不得算当前依据、历史适用性检查、局部工作前提、拒绝伪造审批、内置示例结构。

本次已从 GitHub 重新读取和静态核对：新发行清单、示例 JSON、roles.json 解析正常；前端 app.js 可通过 JavaScript 语法解析；只读 Web 路径有项目身份、符号链接和文件大小安全检查。一次兼容脚本的错误换行转义已经修正。

**尚未在真实仓库执行**：Python 全量 unittest、ZIP 导出/解包、Windows 浏览器 Web 端到端、GitHub 远端管理记录同步，以及干净新对话的真实接管测试。不得把静态检查、另一份本地原型测试或源码提交冒充正式完整回归通过。

## 六、后续应如何验收而非无限加规则

1. 本地运行 `python -m unittest discover -s tools/project-control-v2/tests -v` 并保存失败原文，不默认启动 CI。
2. 运行 `python tools/project-control-v2/portable/export_package.py --out <new-zip-path>`，解包校验全部 42 个 manifest 必需文件，再执行新 JSON 示例审查。
3. 在没有此前聊天记忆的新 ChatGPT 对话按发布包入口接管一项具有历史变更、资料缺失、旧批准的项目；观察是否主动查源、评估有效性、组织补全，不能预设“PASS”。
4. 在受控的目标项目工作台中以可选只读 `context_assessment.json` 检查 NOT_RECORDED、INVALID_RECORD、SOURCES_FOUND 和 GAP_CONFIRMED 的显示。
5. 如后续需要从候选补全动作直接进入现有 WBS/需求/工作，则另外设计审批、CAS 写入和事务关联；当前**没有**这条自动持久化/自动任务派发闭环。

审查结论：**已完成通用候选修订，正式产品验收尚未完成。** 不因一轮真实接管失败增加 AutoTest 专用硬编码或新的固定阶段，也不将任何项目旧批准静默迁移。

## 七、4.5 后续修订：从只读评价到真实管理台账

4.4 原审查结论保留作为历史快照，**不能再把当时“尚未实现 Web 保存或 WBS 关联”理解为 4.5 的当前情况**。

- 新增 `portable/context_workflow.py`：将评价候选追加到目标项目 `management.json.context_reviews`，保留内容摘要和历史版本，使用既有本地修订号/CAS 与项目身份校验；不会自动升级旧 `COVERED`。
- `propose_context_remediation_work` 把已登记的评价行动与现有交付候选、WBS 工作联系起来，工作仍为 `PROPOSED`、`execution_authorized=false`，不下发 Codex 任务。
- 新评价会标记依赖旧评价的工作为过期，重新审查相关提交中的计划，并提醒已请求 Gate 的历史证据发生变化；`governance.py` 拒绝用过期上下文支撑新计划提交或 Gate 审查。
- Web 负责人视图增加候选评价登记与“组织为候选工作”操作，并在首页展示尚未检索或未完成评价的项目认识，不要求负责人替 AI 填资料。独立 `context_assessment.json` 保持可选兼容只读来源；有新版 CAS 评价时优先以 `management.json` 记录为准。
- 新增 `tests/test_context_workflow.py`、`tests/test_context_governance.py` 和缺失资料误判相关回归用例。

**验证边界**：本轮 GitHub 文件与发行清单复查、前端 JavaScript 语法解析完成；另在隔离环境对与新工作流一致的本地镜像执行了 Python 语法检查、候选记录/补全关联/证据过期的冒烟测试，以及八项发现语义单元测试。完整远端源码回归、真实 Web 浏览器及 Windows 使用测试未执行；不把本地镜像测试等同于 GitHub 精确 HEAD 的完整测试。正式发布仍为 `CANDIDATE_NOT_RELEASED`。
