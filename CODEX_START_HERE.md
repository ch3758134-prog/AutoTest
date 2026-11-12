# CODEX_START_HERE｜本地 Codex 统一入场入口（候选）

本文件供**用户主动启动的本地 Codex**读取，不适用于网页端 ChatGPT 的工程协调岗位。当前只存在于 Project Control V2 的候选分支 `management/project-control-redesign-20261111`，**尚未合并 main**。

## 使用方式

用户只需为本次任务指定一个**空目录**并让 Codex 按已关联的 GitHub 项目管理系统开始；不必转发完整的任务描述、分支、SHA 或测试命令。

**前提**：Codex 已能定位这一个 GitHub 项目管理仓库（例如由已连接的 GitHub 项目上下文、固定入口或一次性仓库绑定提供）。一个完全空白的本地目录本身不可能告诉任何程序对应哪个远端仓库；如果远端身份有多个候选或无法确认，先询问一次仓库，不可猜测。

## 远程读取顺序

1. 先在 GitHub 确认仓库身份为 `ch3758134-prog/AutoTest`。当前管理系统试验入口在分支 `management/project-control-redesign-20261111`，不要误用其他 AutoTest 软件 UI 的 PR（例如 PR #36）。
2. 读取 [Codex 执行入口说明](tools/project-control-v2/ai/codex/README.md)。
3. 读取 [可执行任务登记](tools/project-control-v2/ai/codex/dispatch.json)，仅选择**唯一**的 `READY_FOR_LOCAL_IMPLEMENTATION` 且角色为 `local_executor` 的任务。用户指定明确任务时仅核对该任务；无唯一任务时停止并报告。
4. 阅读该任务的 `inputs`、`scope`、`acceptance`、`restrictions`，核对当前项目管理记录及工程资料，不能把 AutoTest 的既有产品任务当成 Codex 当前任务。
5. 在用户指定的空目录中，按任务配置从 GitHub 检出其指定来源分支。克隆应放入**该目录本身**，不要另建随意命名的多份副本。确认 Git origin、来源分支、HEAD 祖先关系以及确切任务文件都来自同一个提交。
6. 运行 `python tools/project-control-v2/ai/codex/resolve.py select` 校验任务（也可按任务 ID 选择）；运行 `claim` 记录本地确切起点，然后在任务限定的文件范围内创建工作分支与实施。没有通过校验不能自行转入实施。
7. 最终执行 `verify-scope` 检查全部本地变更及提交未越界；记录真实命令、测试结果、Git HEAD 与失败日志，向工程协调者提交结果。不得自动 push、合并、批准或删除目录。

## 工作区要求

- **首次准备**：要求真实空目录、没有其他 Git 工程或已有用户文件；不允许覆盖。
- **本次状态**：任务执行工作区持久保留，等待后续交接和审查。临时查看/测试工作区另有生命周期记录及安全回收规则，不能直接 `rmdir /s`。
- **错误处理**：远端不可达、分支错误、任务未授权、范围不明、目录不空、历史 SHA 不匹配、运行服务或未保存成果，都应阻止危险操作并报告。
- **权威边界**：GitHub 中的任务只是受控指派记录；不能代替服务器端仓库权限、Windows 文件 ACL 或用户的正式审批。

## 与项目协调的关系

ChatGPT 网页端协调入口仍是 [AI_START_HERE.md](AI_START_HERE.md)。协调者应先将任务、来源、范围、验收条件和权限**写进 GitHub**；本地 Codex 读取并执行。用户不再承担 AI 之间复制实施细节的工作。
