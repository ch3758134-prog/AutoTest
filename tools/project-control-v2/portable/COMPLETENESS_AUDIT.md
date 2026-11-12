# Project Control V2｜可移植使用方式完整性审查

审查对象：管理 AI 收到“项目管理系统文件夹 + 任意目标 GitHub 仓库 URL”，管理目标项目，向用户提供 Codex 极简启动指令。

| 子系统 | 当前结论 | 主要缺口 |
|---|---|---|
| 上传文件夹内部统一入口 | 本候选新增；配套只导出通用文件的 ZIP 和 SHA-256 校验 | 用户实际附件可读性、不同客户端上传及真实使用需验证 |
| 管理 AI 首次身份确认 | 有角色卡，现补充已明确协调身份无需再问 | 跨对话身份无自动绑定 |
| 新项目 NEW 接管 | 本候选有流程与中立模板 | 尚无端到端远程建仓授权与验收 |
| 既有项目 ADOPT 接管 | 本候选有只读盘点与保留历史要求 | GitHub PR/Issue、外部资产需要管理 AI 实际核对 |
| 工程资产分类/批准追溯 | 有概念规则与初始登记 | 未实现全面的跨来源证明体系 |
| 多项目数据模型 | 新增目标仓库独立 `management.json` 及多项目显式加载 | 旧单项目试点模型没有整体迁移，新旧模型字段尚需长期治理 |
| 通用本地 Web UI | 新增候选 `portable/web_server.py` 和 `portable/web/*` | 需真实 Windows 浏览器、人机操作及跨项目数据迁移验收；旧 `server.py` 仍是 旧单项目试点 |
| 正式计划/变更/风险 | 新增候选需求、WBS 依赖、计划快照与修订、变化影响图和 Gate 审查请求 | 正式批准/复杂资源规划与旧模型迁移尚不完整 |
| Codex 远程任务发现 | 原试点 CODEX_START_HERE+dispatch+resolve；本候选新增目标仓库通用 resolve.py | 目标仓库仍需独立任务合同与真实授权；通用校验器仅做本地检查 |
| 空目录工作区创建/清理 | 实现可移植 workspace_manager.py：登记自有 clone、linked TEMP 创建/审查/正常退出、历史目录只读扫描、跨已登记 worktree 回执收集 | 真实 Windows 占用进程、联接点与断链目录现场验收未完成；普通 clone 不自动删除 |
| GitHub 多仓库支持与安全写入 | 已提供登记项目的显式只读 `ls-remote` 检查与候选 Codex 回执复核 | 已有受控任务发布与 PR 接收状态的版本比对候选；**真正跨 GitHub PR 发布/接收、CAS 竞态与跨机器同步未作实测** |
| 测试与验证 | 新增跨项目回执、Git 文件核验、远端只读版本检查、写入锁、项目隔离及 HTTP 授权回归 | 未提供实际 Windows + 真实新/旧项目全链路验收 |

## 最近推进的 GitHub 任务交接候选

- `portable/task_publication.py`：读取本地已核对目标 Git 快照，对任务设计、独立人类批准、任务唯一性、来源 SHA 与文件范围作只读预检，并生成带原文件 blob SHA 的候选 GitHub Contents 更新计划；真正远端发布需要管理 AI 的 GitHub 写连接及最新文件并发核对。
- `portable/pr_evidence.py`：接受通过 GitHub 连接取得的 PR 元数据，检查目标仓库、分支和候选提交是否与 Codex 回执相符；该检查**不等于 PR 内容审查、CI 验收或项目 Gate**。
- `portable/task_submission.py`：将取得正确 PR 与 Codex 回执的 READY 任务转为 `SUBMITTED_FOR_REVIEW` 的候选 GitHub 文件修改，保留独立审查与 Gate 边界；本身不自动提交远端。
- `portable/REMOTE_HANDOFF.md`：管理 AI 主动读取的完整任务发布、Codex 领取及远端 PR 复核协议。
- 新增 `portable/github_bridge.py` 可选显式 HTTPS GitHub 非强制 Git ref-CAS 工具（保留显式兼容 Contents SHA-CAS），重新核对真实批准记录、设计 blob、任务版本、PR 文件范围及准确 HEAD 的 check-runs；端到端真实 GitHub 写入仍未通过独立目标仓库实测。
- 新增 `portable/workspace_manager.py` 本机工作区登记与审查退出，和 `portable/governance.py` 通用计划/变更候选；所有未能确认的旧目录仍保留。
- 已增加无网络的 NEW/ADOPT→授权→本地 Codex→回执→PR 候选的完整测试夹具，但这并非真实 Windows 或 Github 账号级别的验收。
- 仍缺少实际跨机器 GitHub 写入/PR 端到端验证以及可靠 Windows 工作区回收。要避免把受控协作的文本约束当成系统级权限。

## 分发包边界

`portable/export_package.py` 依据清单只打包通用入口、初始化/任务工具及 Web 服务，明确排除 旧单项目试点 试点数据。导出包不带完整源码仓库历史，也不代表目标项目被创建、GitHub 已同步或正式审批已通过。实际分享时仍需将目标仓库地址作为单独输入。

## 禁止的完成宣称

- “初始化模板已生成”不代表目标 GitHub 仓库已写入；
- “已有源码”不代表 G4 通过；
- “本地 HEAD 存在”不代表与 GitHub 同步；
- “48 项测试通过”仅证明早先 Codex 任务准入测试，不证明通用运行；
- 项目管理包能被上传、AI 能够阅读，并不意味着 ChatGPT 可以自动控制用户本地 Windows；
- 管理端职责可以识别，但权限不能只依靠提示词构成真实安全隔离。

## 剩余正式研发工作

1. 将本候选初始化协议与 CLI 在独立新项目、已有资产项目各做一次真实远端—本地端到端验收。
2. 将新增通用 Web 候选的模型完整度扩展到复杂计划、正式变更和审查；核对原 旧单项目试点 试点资料的迁移，不能静默覆盖。
3. 统一目标仓库任务合同、工作空间登记及 Codex claim/verify-scope，在每个项目都可运行，不能从试点复制死绑定。
4. 建立可核验的 Codex 测试日志与跨工作区回执关联，随后完善文档来源、角色/授权、正式项目基线与审批跟踪，实施审查、测试和真实用户验收。
5. 执行 GitHub 分支保护/权限检查；单机 token 不可冒充 OS 级隔离。
