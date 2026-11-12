# Project Control｜目标 GitHub 仓库正式任务与 Codex 候选 PR 交接协议

本协议由 ChatGPT 网页端**工程协调 AI**从管理系统包主动读取。用户只需提供管理系统文件夹和目标 GitHub 仓库 URL；不负责替 AI 整理分支、SHA、任务细节或技术方案。

**这是待正式验收的协作候选协议。** GitHub 是目标项目的可核对协作中心，但不是本地目录和运行日志的自动镜像。运行中的 Web 服务、Codex 本地进程与各 ChatGPT 对话没有隐含的自动后台协同。

## 1. 三种 GitHub 内容，不能混在一起

- **管理软件来源仓库**：提供通用入口、发行包和程序；不包含新项目的正式批准。
- **用户目标 GitHub 仓库**：包含实际工程源码和独立的 `PROJECT_START_HERE.md`、`.project-control/project.json`、`tasks.json`、`decisions.json`、`management.json`。
- **任务候选 PR**：Codex 在目标工程的被允许实施范围内形成的源码变更；PR 的存在不等于完成，更不是正式合并授权。

如果管理 AI 没有真实 GitHub 连接写权限：可以研究并提出候选记录，但**不能宣称任务已写入 GitHub**。不得用另一仓库的 PR 代替目标项目。

## 2. 先完成授权依据，再发布 READY

1. 网页端管理 AI 用 GitHub 连接**主动读取**目标仓库当前默认分支、目标管理分支的 HEAD、真实文件、审批记录、设计依据、任务登记与 PR 状态。相同项目的历史 Git 证据不能被一张模板覆盖。
2. 与用户确认是否有充分的研究/设计依据，属于 `EXPLORATORY` 预研还是 `CONTROLLED` 正式实施。预研允许目的明确、范围受限，不强制伪造已经完成的 P0/G3；正式实施必须核对已批准的范围和设计 blob。
3. 在目标仓库的 `.project-control/decisions.json` 中写入**真实的人类批准决定**，其中 `TASK_EXECUTION_APPROVAL` 至少明确：
   - `kind: TASK_EXECUTION_APPROVAL`、`state: APPROVED`；
   - `task_id`、`source_sha`（正式指定的源码基线提交）；
   - `approved_by`（真实授权人）与 `approval_reference`（可核验的原始批准依据）。
4. `CONTROLLED` 另需目标仓库 `project.json` 的产品身份/范围批准记录及 `DESIGN_APPROVAL`（设计文件路径、Git blob SHA、真实批准来源），并核对源提交中的实际设计内容。不凭文本写了 APPROVED 就假设身份学验证已完成。
5. 先把独立任务写为 `DRAFT` 或 `AWAITING_AUTHORIZATION`。必须有唯一任务 ID、目标项目和仓库、执行角色 `local_codex`、来源分支及 pin SHA、任务开发分支、有效输入、允许变更文件范围、验收标准和禁止操作。不能把无设计工作直接发布给 Codex 编码。
6. 此时目标仓库的分支 HEAD 可能比任务的来源 pin SHA 晚：因为批准记录和任务草案本身必须在其后提交。应验证 pin SHA 是当前基线分支 HEAD 的**祖先**，而不是要求二者完全相同。

## 3. 管理 AI 向 GitHub 发布任务：防止并发覆盖

通用包中的 `portable/task_publication.py` 在有目标仓库本地干净检出时可以执行只读预检：

```powershell
python portable/task_publication.py --target "<用户指定的目标项目目录>" --task-id "IMPL-001"
```

它不执行写入，而是生成 `project-control-task-publication-plan/1`，包含 `source_commit`、`preflight_branch_head`、`expected_tasks_blob_sha`、目标相对文件 `.project-control/tasks.json` 及新的候选内容。它只能证明一份**本地已取得的 Git 源码快照**通过预检，不能伪装成已经看到 GitHub 远端实时版本。

**网页端管理 AI 无须要求用户运行此命令**。它应使用已经连接的 GitHub 工具自行读取同等字段、核验决策/源码/文件版本，并直接执行对应的远程审查和写入。对于无法在网页端运行的预检，管理 AI 应明确列出未验证项目，不得自行假定 PASS。

发布时：
- 重新从目标 GitHub 读取 `.project-control/tasks.json` 的当前**文件 blob SHA**、目标分支 HEAD、任务状态和批准记录；
- 优先使用 **Git Data 原子分支引用校验**：以已审查 HEAD 为唯一父提交，创建只修改 `tasks.json` 的候选树/提交，再以 `force=false` 非强制更新远端分支。并发写入导致非快进时必须拒绝；同时核对原文件 blob SHA 与当前批准记录，不使用旧版本覆盖他人新编辑；
- 如果内容或审批在审查后发生变化、版本冲突、网络失败或无写权限，**停止并重新评估**，不能暴力重试、改主分支或宣称成功；
- GitHub 返回新 commit 后再次读取任务文件，确认该唯一任务确实为 `READY_FOR_LOCAL_IMPLEMENTATION` 且仍属准确项目、批准输入及任务范围。记录真实提交 SHA。
- 一个没有明确指定任务 ID 的本地 Codex 会话只能领取**唯一** READY 任务；多个任务并行时，管理 AI 只需让用户选择稳定任务 ID，不需转发完整详细设计。

**重要**：Git 分支引用的非强制更新能阻止同时推进同一分支的写入覆盖，但**不自动原子更新多个项目管理文件，也不证明审批人的真实身份**。仍需 GitHub 分支保护、可追溯审批和正确的仓库写权限。GitHub Contents 文件 SHA 写入仍保留为显式兼容选项，不能当成同等级别的分支引用事务。

## 4. Codex 实施与回执

用户在 Codex 中指定一个空目录，管理 AI 只给很短的启动指令，含**目标仓库身份**（连接可唯一定位时省略）和可选任务 ID：

> 在当前空目录中读取已关联目标 GitHub 仓库的 `PROJECT_START_HERE.md`，领取当前唯一获授权的 Codex 任务，自行取得版本、设计和变更范围后实施；完成后交回实际 Git 提交、回执和未解决事项。仓库或任务不唯一则停止。

Codex 验证目录为空、仓库 origin、分支和来源 SHA；使用 `python .project-control/resolve.py select` 与 `claim`；只在任务范围内开发，执行 `verify-scope`。候选代码全部形成实际 Git commit 后执行 `python .project-control/resolve.py report`，回执保存于该 worktree 的 Git 元数据中。

**本地 commit 不是远端 PR。** 只有额外获得对应开发分支 push 与创建 PR 的授权，Codex 才能通过真实 GitHub 权限发布候选；否则必须报告本地候选 HEAD 和所需发布动作，等待工程协调者的受控远端操作。不得自动推送主分支或合并。

## 5. 管理 AI 的 PR 验证与审查

在 GitHub 上接收候选后，管理 AI 应**自己从 GitHub 读取** PR 实时信息，而不是让用户复制长篇截图：

1. 核对 PR URL/编号与目标仓库一致、PR 源仓库/工作分支/目标分支、`head_sha` 与 Codex 回执的 `candidate_head` 完全一致，且 PR 尚未合并/关闭。
2. 利用 `portable/pr_evidence.py` 的规则对这些元数据做一致性检查；它只判断**提供的 PR 信息**是否相互匹配，若未通过连接真实获取，则不能宣布远程核对成功。
3. 检查**准确 head_sha** 的 GitHub PR 文件清单/补丁、必要 CI 运行及日志；CI 成功只能证明对应检查已成功，不等于目标 ECU/硬件/项目成果已真实验收。任何 PR HEAD 变化都要求重新比对及重审。
4. 区分 `REPORTED`、`PR_METADATA_MATCHED`、`SUBMITTED_FOR_REVIEW`、`CODE_SCOPE_REVIEWED`、`CI_CHECKED`、`ACCEPTED` 和 `MERGED`。`portable/task_submission.py` 已能校验一份真实来源的 Codex 回执与最新 PR 信息，在本地目标仓库生成只读的 `tasks.json` 提交审核候选（包含原文件 blob SHA，供 GitHub CAS 写入）。这**只标记已提交待审**，并不会接受测试或 Gate。
5. 变更和决定应回写**目标项目** `.project-control` 管理记录，并优先使用目标仓库最新 HEAD 的非强制 Git 引用条件和文件 blob 校验；不可把一个工作项自报完成视为产品 Gate 通过。

当前软件已实现本地回执、Git diff 校验、GitHub PR 元数据及文件一致性核对、显式只读远端 HEAD 检查、已登记 worktree 回执归集和原始日志 SHA 校验。**仍没有完整的 GitHub PR/CI 一体化自动验收和可核实的人类批准闭环**，也没有独立目标仓库的真实远端交付验收；这些必须继续实测。

## 6. 本地 Web 项目记录的远程发布

管理 AI 不得将 Web 服务已保存 `management.json` 直接当成 GitHub 已更新。可使用 GitHub 连接自行读取当前目标文件 SHA 并按真实决定提交候选；在本机具备显式授权时，也可从独立系统包使用 `portable/management_sync.py` 对**该单个文件**做只读预检与受控上传。它只允许审查中的草案数据，不自动批准阶段或 Codex 任务。若目标分支提交改变、原文件 blob SHA 改变或事件历史分叉，必须停止并重新评估，不许覆盖。

## 7. 关于本地代码临时工作区

一次只读 GitHub 研究无需新建本地 clone。需要执行/编译/调试时，Codex 应把任务的本地工作区登记到管理系统，明确“正式/持续开发/临时验证”的归属及关闭计划。未知历史目录绝不可自动删除。

现已有工作区登记、临时 linked worktree 创建、查看、回执归集和审查后退出程序，并通过 GitHub Actions Windows 测试夹具。**仍未完成真实用户磁盘上的 Win11 占用进程、旧断链 worktree 与异常文件现场验收**；不得把模拟夹具通过等同于真实目录可安全删除。
