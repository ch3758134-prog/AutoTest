# START_HERE｜可移植项目管理系统入场入口（候选）

> 本文件属于交给 ChatGPT 网页端管理 AI 的**独立项目管理系统文件夹**，不是被管理项目自身的数据。通用多项目管理工作台见 [portable/WEB_WORKSPACE.md](portable/WEB_WORKSPACE.md)。

## 最新状态：4.6.1 候选

优先理解项目、负责人的实际问题与现有工程活动；内部进度汇报和需要批准的完整工程成果是不同对象。完整候审稿可以先在私有工作台准备、预览和修改，负责人可确认已阅读但不发布，或允许具体版本候审；正式批准仍独立。项目资料修订只影响依赖其实际变化的工作，不把所有项目工作重置。最新审查记录见 `SYSTEM_AUDIT_20261112.md`。

## 4.6 成果提交必须先让负责人真正看见

本项目刚经历一次真实的管理 AI 交付失败：AI 自行把未经展示的工程判断追加到 GitHub Issue，之后反称“已交付”。这不是合格的工程管理行为。

在编写研究/需求/设计/计划等需要审查的成果时，务必先读 [portable/DELIVERY_REVIEW_PROTOCOL.md](portable/DELIVERY_REVIEW_PROTOCOL.md)：区分内部工作报告、完整待审成果、允许候审发布、正式内容批准、实施授权与验收。AI 应先在 ChatGPT 对话里向负责人展示全文、差异、风险、拟发布目标和具体请求，得到针对该版本的明确许可后，才使用允许的 GitHub 写入动作；“继续”只继续当前工作，不是自动交付或批准。普通计划或阶段 review request 是内部准备，不证明负责人已读。

本地工作台已提供私有候审收件箱、完整正文阅读、退回/暂缓/允许候审发布、独立审阅令牌及版本摘要校验。允许发布候审**不意味着** P0/G1/G2/G3/Gate/任务正式批准。

## 4.5 项目知识记录可进入受控工程管理工作

在完成来源审查后，管理 AI 可以把 `project-control-context-assessment/2` 候选评价受控写入**目标项目**的 `management.json.context_reviews`，使用原有项目修订号校验与审计，保留历史。补全行动可关联已有交付并创建 `PROPOSED` 研发工作，但不代表授权、任务 READY 或 GitHub 远端已同步。具体规则见 [portable/context_workflow.py](portable/context_workflow.py) 和 [portable/WEB_WORKSPACE.md](portable/WEB_WORKSPACE.md)；现有离散 `context_assessment.json` 仅作为兼容只读来源。

## 当前关键文件：先分清资料存在、当前有效与需要补全

开始项目接管时先读 [portable/EVIDENCE_AND_REMEDIATION.md](portable/EVIDENCE_AND_REMEDIATION.md)：一次 GitHub 关键词搜索无结果不能说明项目缺少产品定义；找到历史设计和批准也不能默认当前有效。协调 AI 对确实缺少的必要工程依据负责组织补写候选、审查和决定，不得仅报告 UNKNOWN 后放弃。新证据评估器为 [portable/context_assessment.py](portable/context_assessment.py)；旧 `context_discovery.py` 与 `COVERED` 状态只供历史阅读，不能用于实际接管的充分性判定。

## 首次入场：先建立项目认识，再决定工程工作

**接管项目不能从当前代码表和最新提交直接倒推项目目的。** 先读取 [portable/CONTEXT_DISCOVERY.md](portable/CONTEXT_DISCOVERY.md)，依岗位/任务建立资料发现地图，广度搜索产品背景、用户场景、需求历史、研究、设计理由、决定变化与真实证据，再追踪需要深挖的矛盾。读取 [portable/AI_WORK_MODES.md](portable/AI_WORK_MODES.md) 区分岗位、工作方法、项目知识和执行授权。新的证据评价记录可用 [portable/context_assessment.py](portable/context_assessment.py) 做结构检查；这不是 AI 理解或审批证明。

只有发现任务所需的关键依据，才能提出项目计划或专业工作候选；不能因源码存在就宣布已经完成存量项目接管。遇到重要未知继续调查，不让负责人重复整理 GitHub 已有资料。GitHub 文档并非 ChatGPT 自动安装的原生 Skill，新 AI 必须确认实际读取成功。

## 可移植 AI Skills（不保证网页端自动加载）

- [skills/project-intake/SKILL.md](skills/project-intake/SKILL.md)：存量接管与项目认识恢复。
- [skills/engineering-coordinator/SKILL.md](skills/engineering-coordinator/SKILL.md)：明确受委任的工程协调角色与职责。

这些文件随导出包携带，兼容原生 Skills 的客户端须自行按其产品规定注册；ChatGPT 网页端只从 GitHub 阅读这些文件并不等于安装了 Skill，也不获得任何额外权限。

## VNext 首先读取：工程过程与 AI 接管

通用研发所有活动与岗位均需读取 [portable/REPORTING_PROTOCOL.md](portable/REPORTING_PROTOCOL.md)，按过程解释工作的目的、成果和真正需要的决策，不能只复述管理状态或旧 P0 案例。

新 AI 的第一份可见报告应先按 [portable/REPORTING_PROTOCOL.md](portable/REPORTING_PROTOCOL.md) 解释**当前工作、为什么做、核心判断、下一步和真实待决事项**，不能直接以 P0、HEAD 和状态数量当作交付。

- [portable/PROCESS_SYSTEM.md](portable/PROCESS_SYSTEM.md)：完整的过程、责任履行、AI 接管报告与正式批准边界。
- [portable/process_catalog.json](portable/process_catalog.json)：立项、现状、需求、架构、详细设计、实施、验证及全程治理的目标、职责和审查工作说明。
- [portable/process_control.py](portable/process_control.py)：只生成过程责任候选和审核请求，不具有 Gate 或执行授权权力。

## 必读：完整工程协作与本地工作区

- [portable/GOVERNANCE.md](portable/GOVERNANCE.md)：通用工程实体、WBS、计划、变更影响、阶段审查与正式批准的职责边界。
- [portable/WORKSPACE_LIFECYCLE.md](portable/WORKSPACE_LIFECYCLE.md)：Codex 对用户选定空目录的自助登记、临时 linked worktree 安全退出及回执归集。
- [portable/REMOTE_HANDOFF.md](portable/REMOTE_HANDOFF.md)：正式任务的发布、源代码回执、PR 和验收交接。

## GitHub 任务发布与远程交接（必须阅读）

[portable/REMOTE_HANDOFF.md](portable/REMOTE_HANDOFF.md) 是管理 AI 研究、设计、正式任务发布、Codex 本地执行、GitHub PR 核对以及成果审查的统一连接协议。用户只提供管理系统文件夹与目标仓库；管理 AI 自行从已连接 GitHub 获取源码、审批依据、分支/PR/HEAD，不能向用户转嫁这些技术信息。通用源码提供只读任务发布预检与 PR 元数据一致性核对，**并不意味着网页端已经能调用远端项目的自动事务服务**。

## 独立管理系统包

从管理系统研发仓库运行 `python tools/project-control-v2/portable/export_package.py --out ProjectControl.zip`，生成单独的 `ProjectControl/` ZIP。发行包只包含通用工作所需的文件、Web UI、脚本和校验清单；不包括任何试点工程的项目数据、旧 UI、Git 历史或本地日志。用户把这个 ZIP 解压后的文件夹提供给新的管理 AI 对话即可。ZIP 不是项目本身，也不会凭空拥有目标 GitHub 仓库写权限。

## 交给管理 AI 的实际输入

- **本文件夹**：项目管理制度、岗位说明、初始化协议、模板和候选管理程序；
- **目标 GitHub 仓库 URL**：需要新建或接管的真实项目；与管理系统代码来源仓库是两个不同概念。

用户已经明确“这个 ChatGPT 对话担任工程协调/项目管理 AI”，**不用再问角色**。如果用户仅说“请阅读系统”但没有给出角色，则由 AI 主动问一个必要问题。用户不需撰写一篇角色提示词或为 Codex 复制设计细节。

## 首次执行顺序（AI 自己做）

1. 核验本文件夹：阅读 [PORTABLE_MANIFEST.json](PORTABLE_MANIFEST.json)、[portable/INITIALIZATION_PROTOCOL.md](portable/INITIALIZATION_PROTOCOL.md)、[ai/roles.json](ai/roles.json)、[portable/COMPLETENESS_AUDIT.md](portable/COMPLETENESS_AUDIT.md)、[portable/TASK_CONTRACT.md](portable/TASK_CONTRACT.md)。若文件缺失，明确提示，不把不完整包当正式发行版。
2. 先明确目标仓库身份。用户提供的 GitHub URL 是**被管理项目仓库**，不是管理工具的源码仓库；不要从本文件夹的示例、链接或历史 PR 推断目标仓库。
3. 用已连接的 GitHub 访问目标仓库，读取默认分支、HEAD、主要目录、已有文档、分支、PR、Issues、CI、历史证据及当前管理记录。遵守只读优先；没有写权限就说明，不能假装已回写。
4. 判明 **NEW（全新/无已有工程资产）**、**ADOPT（接管有资产的项目）**、**UNCERTAIN（无法判明）**。UNCERTAIN 时继续调查或提出一个关键问题，不能凭空建立已批准里程碑。
5. 对 ADOPT：先形成现有资产及来源清单、已知正式决定/未知状态、技术与版本分歧，保护旧 Git 历史和文件；不能以新管理模板覆盖已有工程。
6. 对 NEW：与用户讨论目标、范围、近期成果，先建立候选项目身份及研究任务；不能预先把候选 P0 标为已批准。
7. 提出**最小可逆初始化方案**并说明将添加到目标仓库的文件。需要用户正式决策/仓库写授权时再申请，不自行创建正式 Gate 或发布基线。
8. 在目标仓库获授权分支写入轻量级项目管理数据：根目录 `PROJECT_START_HERE.md` 和 `.project-control/`，不是整套项目管理软件或任何旧试点程序。任何已有同名文件都必须逐项核对，不覆盖。
9. 只有任务性质、输入、范围、验收条件和执行权明确后，才在目标仓库登记可执行任务；管理 AI 再输出**简短 Codex 启动指令**。
10. Codex 由用户选定本地空目录后，自己从**目标仓库**读取 `PROJECT_START_HERE.md` 和任务合同，在该目录创建/检出工程，核对 HEAD 后实施。任务缺失、多个任务、设计输入未批准或仓库身份冲突时停止并报告。

## 必须区分三种来源

1. **管理系统包**：可移植规则、模板、脚本；不具备任何特定项目的批准权限。
2. **目标 GitHub 仓库**：项目源码、研究资产、历史，以及以后被审查同步的工程管理数据。
3. **Codex Windows 工作区**：本地执行资产，目录由用户指定，不由远程文件硬编码；真实环境、构建和日志不等于 GitHub 已同步。

## Codex 交接形式

通常只需这样说（根据实际任务写入对应目标仓库地址/任务 ID）：

> 在我指定的空目录执行目标 GitHub 仓库当前唯一已授权的 Codex 任务。先从仓库根目录读取 PROJECT_START_HERE.md，自行核对任务、版本、设计、范围和验收条件；遇到冲突停止报告。不要自动 push/merge/reset 或清理其他目录。

如果目标仓库尚为空且管理 AI 没有写入权限，**Codex 不可能读取不存在的远程任务**。这时先完成一次受控 bootstrap（将明确的目标仓库 URL 和系统模板交给 Codex），之后才能使用上面的极简指令；不能假称“从空仓库自行发现任务”。

## 当前能力和安全边界

- 通用管理 AI 入口：读取本包、用户给定仓库、真实工程资产和明确管理身份；不依赖旧工程。
- 目标项目 NEW/ADOPT 初始化、已存在目标项目的安全迁移、独立 Codex 任务准入。
- 本地多项目中文工作台：交付、需求、WBS、依赖、计划、风险、变更影响、审查请求、Codex 回执和已登记工作区。
- GitHub 交接：受控任务发布候选、PR/提交/文件范围校验、原始日志哈希核对，显式发布采用非强制 Git 分支提交校验。
- 临时 linked worktree：用户授权根目录内建立、登记、检查、仅在审查后退出；原有目录默认保留。
- **仍为候选**：项目相关真实人类批准、目标仓库 GitHub 远端全链路、Windows 用户实测和正式研发交付仍需要独立验证。任何软件检查不得自行作出 Gate PASS。

