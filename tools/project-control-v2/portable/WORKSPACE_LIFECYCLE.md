# Project Control｜Codex 本地目录与 Git 工作区生命周期（候选）

## 基本原则

**本地目录由用户选择；目录创建与源码定位由 Codex 读取目标 GitHub 仓库自行完成。** 用户不承担任务 SHA、PR HEAD、文件结构或分支选择工作。项目管理系统不得在 GitHub 中写死某台机器的绝对路径，也不能把历史临时目录当作可删除物。

三种类型：**PERMANENT** 正式工程绝不自动清理；**TASK** 持续开发工作区保留至审核；**TEMP** 只适用于明确登记的 Git linked worktree，符合退出审查后才可使用普通 Git worktree remove。

默认登记簿位于本机用户目录的 .project-control-local/workspaces.json；也可通过 PROJECT_CONTROL_REGISTRY_HOME 指定持久化位置。登记簿不属于项目 Git 文件，**不自动上传真实本地绝对路径、日志和私有文件**。远端任务不能替用户决定允许的本地目录根。

## Codex 首次接入

1. 用户在 Codex 中选择一个**空目录**，目标 GitHub 仓库由管理 AI 已明确关联。
2. Codex 自行读取目标仓库的 PROJECT_START_HERE.md，核实 source branch、task ID、Git HEAD、设计及准入后，直接在用户选择的目录本身 clone；不得另建无登记目录。
3. 已初始化的目标仓库包含 .project-control/workspace_manager.py。Codex clone 完毕后运行：

~~~powershell
python .project-control/workspace_manager.py register-existing --help
~~~

4. Codex 自行填入目标仓库中真实的 project_id、task_id、repo_url、source_ref、pinned_sha 等信息；--workspace 是用户选择的目录，--allowed-root 是用户已授权的上级目录；--apply 才写本地登记簿。register-existing **只登记当前真实工作区，不创建新 clone，不改工程源码**。
5. 正式编码前运行 .project-control/resolve.py select / claim。结束后运行 verify-scope / report，并保留整个工作区到管理 AI 审查完成。

如旧目标项目缺少 workspace_manager.py，或 resolve.py 不再满足最新版任务准入机制，先运行管理包 portable/project_init.py 的 upgrade-runtime-preview，只在核对旧版 SHA 与备份计划后执行 upgrade-runtime。备份保存于**本机 Git 元数据**，不得静默覆盖。

## TEMP 只读查看与复现

由已登记的 Git 工程作为 --parent-repo，固定任务所用准确 commit SHA，在用户授权根下准备空目录。执行 workspace_manager.py create --type TEMP --parent-repo ... --apply 后，内核登记 CREATING，并使用普通 Git worktree add --detach。失败记 CREATION_FAILED_KEEP_FOR_REVIEW，**保留未完成目录供审查**。

退出时先只读运行 close，获得最新 inspection_digest；负责人检查 status、忽略和未跟踪文件、stash、独有提交、共享 Git 关系、是否仍有服务或程序占用、日志和工作证据。仅 TEMP linked worktree、无任何危险或未知变更、未偏离 pin SHA、明确人类审查并确认无人占用，才允许：

~~~powershell
python .project-control/workspace_manager.py close --workspace-id "<登记ID>" --inspection-digest "<刚得到的摘要>" --reviewed-by "<实际负责人>" --idle-confirmed --apply
~~~

默认 HOLD_FOR_REVIEW。不能使用 --force、reset --hard、git clean -fdx、worktree prune、Windows 强制结束进程或系统级递归删除。**普通独立 clone 永不自动清理**；需要结项时使用 archive，登记保留并保存历史。scan 只发现未知目录，绝不自动收编或删除。

## 测试日志

Codex 可在当前 worktree 的 Git 元数据目录 project-control/ 下保存 test-runs.json 和 logs/ 下的原始日志。test-runs.json 是以下 JSON 数组：

~~~json
[
  {
    "name": "unit tests",
    "exit_code": 0,
    "log_file": "logs/unit-tests.log"
  }
]
~~~

执行 .project-control/resolve.py report 时计算每个日志的 SHA-256，写入待审回执；管理服务可以在真实可访问的登记工作区中对照原始日志检查哈希。

**原始日志匹配不等于测试真实执行通过**：exit_code 仍是执行者自报，真正 Test PASS 需独立重跑或对应 CI 原始检查。私有原始日志不会自动上传 GitHub。

## Windows 安全边界

当前通过了临时 Git 测试夹具，但尚未在真实 Windows 11 工程目录、联接点、占用进程、杀毒软件、WPF 应用、测试硬件或旧断链 worktree 上完成现场验收。操作系统无法独立核实占用时，必须由负责人确认。未知目录只读盘点并保留。
