# Project Control｜独立管理系统完整性与正式交付审查

## 1. 用户实际使用方式

用户将独立 `ProjectControl/` 文件夹交给新 ChatGPT 工程协调对话，同时提供一个**被管理目标 GitHub 仓库地址**。管理 AI 自己读取 `START_HERE.md`、岗位、项目资产、实际分支/PR、研究/设计和审批来源；自行编写受控的 Codex 启动指令。用户仅在 Codex 选择本地工作目录，不负责整理任务文件、SHA、设计输入或临时工作区。

**管理系统来源仓库、目标工程仓库、本机执行目录是三个不同边界。** 没有 GitHub 实际写权限或正式批准的管理 AI 不能假称完成远端修改。

## 2. 已实现并进入候选验收的工程能力

| 子系统 | 可执行实现 | 验证边界 |
|---|---|---|
| 独立发行包 | `export_package.py` 依据 manifest 创建固定文件集 ZIP 和逐文件 SHA-256 校验 | 自动化检查 ZIP 内容、排除项目特定资产，并在解压目录运行 CLI |
| 管理 AI 初始化 | 文件夹内 `START_HERE.md`、`INITIALIZATION_PROTOCOL.md`、`ai/roles.json` | 角色不能凭初始化文件取得真实仓库权限 |
| 新项目 / 已有项目 | `project_init.py` 的 NEW/ADOPT 调查、最小脚手架与 hash-pinned 旧运行工具迁移 | 旧源码、历史和已有同名文件不得被无声覆盖 |
| 工程管理数据 | `governance.py` 的需求、WBS/依赖、计划快照/修订、变更影响图及 Gate 审查请求 | 只是候选管理记录；正式审批必须另行取得 |
| 中文 Web 工作台 | `web_server.py`、`portable/web/*`，多项目显示、操作、回执、工作区与版本状态 | 只在 localhost 开放，写入需负责人令牌；拒绝无效 Host 与跨来源写请求 |
| Codex 任务准入 | `target_resolver.py`、`task_publication.py`，独立执行批准、有效设计 blob、来源 SHA、工作分支及文件范围 | 候选 READY 只有取得真实 GitHub 写入确认后才算发布 |
| Codex 成果 | `report`、`receipt_evidence.py`，真实 Git diff/提交祖先与可选原始测试日志 SHA-256 | 日志退出码仍可能为执行者自报；不自动变成 Test PASS |
| GitHub PR 审查 | `pr_evidence.py`、`github_bridge.py` 对仓库、PR HEAD、分支、变更文件与准确 HEAD 的 check-runs 做比对 | CI 观察不等于必需检查已批准，更不等于人类验收 |
| 状态流转 | `task_submission.py` 将 READY 任务提出 SUBMITTED_FOR_REVIEW 候选 | 不允许自动晋级 ACCEPTED、MERGED 或 Gate PASS |
| 远端写入防竞态 | `git_ref_atomic.py` 非强制 fast-forward Git ref 更新；兼容 Contents 文件 SHA 校验仅在显式选择时使用 | 不是跨多个文件的完整事务，也不证明批准人身份 |
| 本地工程目录 | `workspace_manager.py` 登记现有 clone、创建有来源的 TEMP linked worktree、只读盘点与审查退出 | 未知旧目录、dirty、ignored、stash、独有提交和在用进程均先保留 |
| 多工作区回执 | 仅扫描**已明确登记**的同一项目 worktree，收集回执并复核本地 Git 及可用原始日志 | 不能跨机器自动读取本地文件或猜测未登记目录 |
| 项目数据远端同步 | `management_sync.py` 为单个 management.json 提供预览及受控 Git ref 传输 | 成功远端写入之后，本地克隆仍须独立受控更新 |

## 3. 自动化验证覆盖什么

CI 在 Linux 与 Windows runner 执行 Python 回归测试、前端脚本语法/行为测试和独立 ZIP 导出。自动化夹具已覆盖：NEW/ADOPT、旧工具升级备份、目录不越界、临时 worktree 退出条件、Codex 任务准入、回执/日志校验、GitHub 远端 API 伪实现的竞态拒绝、跨项目数据隔离、真实 localhost HTTP 授权及解压包启动。

**以上是软件回归证据，不能把模拟的 GitHub API 或临时目录当作真实目标仓库、真实权限和用户 Windows 磁盘验收。** 具体通过数量及构建产物以当前 PR HEAD 对应的 GitHub Actions 最新运行结果为准，不采用文档里固定的旧数字。

## 4. 正式验收前仍须独立证明的项目

1. 使用一个真实新项目和一个真实已有资产项目完成 GitHub 远端全过程：合法授权、任务 READY 发布、Codex 独立领取、提交 Draft PR、PR/CI 审查、管理记录回写、用户最终批准。
2. Windows 真实交互验收：中文界面、路径/联接点、应用及服务占用、既有断链 worktree 和实际工作目录安全退出；GitHub Windows runner 只代表 CI 环境。
3. 核对目标 GitHub 仓库角色权限、分支规则、保护分支和审查要求；管理系统内声明的权限不能替代 GitHub 或操作系统的授权。
4. 由项目负责人审查复杂计划、正式变更基线与 Gate 证据，确定目标项目专属流程。不得自动将通用候选当作所有行业场景的正式标准。
5. 接收真实用户体验反馈并审查独立包在 ChatGPT 上传、网页端读取和本地 Codex 中的可操作性。

## 5. 明确禁止的误报

- `ZIP 已生成` ≠ 目标 GitHub 仓库已经初始化。
- `本地 HEAD 已存在` ≠ 远端 HEAD 已一致。
- `Codex 回执存在` ≠ PR 已提交或测试已通过。
- `check-runs 全绿` ≠ 必需 CI、硬件实测、项目交付或 Gate PASS。
- `管理计划已审查` ≠ 获得用户正式实施授权。
- `TEMP 目录干净` ≠ 操作系统没有占用；没有确切批准不得删除。
- `测试夹具全部通过` ≠ 可以合并正式主分支或宣布产品最终交付。

只有正式环境的来源、授权、测试、交付结果全部有证据，才能申请最终发布审批。
