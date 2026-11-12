# PC-WORKSPACE-LOCAL-001｜本地工作区生命周期内核实施约束

## 问题与目标

当前跨 ChatGPT/Codex 开发中，临时 PR 检出、测试 clone 和 worktree 被随意放到本机目录，任务结束后没有统一登记、保留或安全退出。已经出现 PR #36 错被当成 Project Control V2、遗留目录和 Git worktree 元数据指向不存在路径，以及 8790 旧进程继续占用的实际案例。

本任务仅实现**通用本地工作区管理内核 + 命令行接口 + 自动化回归**。不要本轮扩展成庞大平台，不替换原有项目 UI，不修改 AutoTest 产品源码。

## 三种本地工作区

- `PERMANENT`：正式工程，不能自动回收。
- `TASK`：按专业/任务使用的持续开发工作区，完成后需审查并人工确认处置。
- `TEMP`：只读查看、临时构建/复现，创建时记录安全退出条件。仅经核对且符合既定回收合同才可自动回收。

不得把未知历史目录自动登记成 TEMP，更不能自动清理。

## 最小登记簿

独立本地文件，**放在经配置的管理根目录**（不能位于任意 TEMP 工作区内），必须包含：
- workspace_id，project_id，task_id，type，purpose
- canonical_path，repository URL/身份，source ref，pinned HEAD，所用 Git 主仓库/ worktree 关系
- created_at，created_by，allowed_root，cleanup_policy，state
- 最新检查时间、结果、风险、引用的日志/回执 ID

真实 Windows 路径和本地日志不得默认提交 GitHub。Registry 更新需原子写入并尽力防止同一进程的并发覆盖；跨进程并发要么加锁要么明确拒绝，不能默默丢更新。

## 操作与行为

- `inspect`：列登记内容与真实路径、Git/目录状态差异，默认只读。
- `create`：任务 ID、仓库/HEAD、用途、类型和计划目录均先验证；只可在配置的授权根下；真实空目录；拒绝路径穿越、符号链接/联接歧义、已登记或已存在非空目录；拒绝错仓库或错 PR。
- `scan`：只读识别允许根下未登记的 Git 目录和失效工作树候选，标为 `UNREGISTERED` 或 `STALE_METADATA_CANDIDATE`，不擅自声明可删。
- `preview-close`：检查 Git status 包括未跟踪与忽略文件、stash 和独有提交、记录中的创建时 HEAD、worktree 真实关系、可能存在的本地日志/编译产物与占用进程；危险或未知一律 `HOLD_FOR_REVIEW`。
- `close`：只允许 TEMP 工作区，必须使用本次新鲜的预检结果并重新校验状态，不能通过修改请求参数绕过。只允许正常 `git worktree remove`；不用 `--force`、`reset --hard` 或系统级递归删除。普通独立 clone 不自动清理，先 `HOLD_FOR_REVIEW`。
- `archive`：记录任务退出和历史，不能删除审计信息。

任务完成后默认保留工作区直到协调者复核。新创建的 TEMP 可在已批准规则内回收，但没有识别清楚的隐藏文件或被忽略资产不允许清理。

## 正式限制

* 不能将 `git status --porcelain` 空输出等同于可删除，ignored files 可能承载真实成果。
* `git worktree prune` 修改共享元数据，不可作为常规自动清理。
* Git worktree 共享底层对象库，不提供操作系统级访问隔离。
* 不能终止未知 PID；现有 8790 进程和错拉 PR #36 目录只做只读盘点。
* 不得因为任务完成而自动删除长期正式工程或开发区。
* 不得允许路径由远端任务任意指向本地系统目录；本地用户授权根是最后边界。

## 测试

使用临时目录创建本地 Git fixture 测试，不触碰实际 `D:\01_Projects`。包括：
1. 空目录/非空目录/重复路径/路径穿越/目录联接。
2. Git origin 与来源 SHA 不匹配。
3. dirty、untracked、ignored、有额外提交/未合并成果。
4. 运行中进程及无法确认进程状态。
5. 正常 TEMP worktree 的预览与回收，失败时工作树及登记不被静默遗弃。
6. 断链 worktree 与 Git 元数据异常仅报告不 prune。
7. 并发更新、重复 close、审计事件和本地敏感路径不推送远端。

## 验收与回报

只读盘点可以直接执行；破坏性行为在测试 sandbox 内验证，不操作用户真实工作区。结束时提供唯一任务 ID、实际 Git SHA、修改文件、测试命令/结果、未完成项目和工作区处置状态。不能把 CI 通过写成 Windows 现场验收。
