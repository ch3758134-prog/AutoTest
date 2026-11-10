# Engineering Workbench V0.5｜GitHub 执行事实、AI 任务领取与 Git 工程归档

**状态：PR #28 候选，尚未正式产品验收。**

V0.5 保留 V0.4 的需求/架构、技术预研/实测证据、批准的详细设计与纯编码准入，新增**真实 GitHub 项目执行入口**。这是一个可用于工程协作的集成增量，但目前不是常驻自主 AI 项目经理，也不会自动启动其他 ChatGPT 对话。

## 首次操作

在 PR #28 独立非 main 工作树中更新后：

```powershell
git fetch origin pull/28/head
git merge --ff-only FETCH_HEAD
py -3 -m unittest discover -s tools/lifecycle/tests -v
py -3 tools/lifecycle/server.py --repo . --port 8766
```

打开 `http://127.0.0.1:8766/`。左侧新增 **GitHub 与 AI 执行**。

1. 选择已有工程项目（例如 AutoTest）；点击 **连接 GitHub 仓库**，输入 `ch3758134-prog/AutoTest`。
2. 点击 **从 GitHub 获取真实状态**。首次联网会真实读取该仓库 Issues、Pull Requests 和 GitHub Actions。右侧显示最新同步时间、源 HEAD、Draft 状态和可匹配的 CI。
3. 选择一条**确实需要编码**的 Issue，创建待设计工作包。不要把 `[RESEARCH]` 技术预研 Issue 直接派给只负责实现的 AI。
4. 在需求/架构页完成并批准详细设计；工作包引用该设计，人工审查依赖关系、固定修订、Git SHA、可修改文件及验收合同。
5. 返回 **GitHub 与 AI 执行**，把工作包关联真实 Issue 和专业角色，满足 `CODE_READY` 才能批准派发。
6. 本地编码 AI / Codex 通过 CLI 真正领取，而不是点开 UI 就算“已开始”：

```powershell
py -3 tools/lifecycle/agent_tasks.py --server http://127.0.0.1:8766 --project <PROJECT_ID> --role DEVICE --worker CODEX-DEVICE --list
py -3 tools/lifecycle/agent_tasks.py --server http://127.0.0.1:8766 --project <PROJECT_ID> --role DEVICE --worker CODEX-DEVICE --output received-task.md
```

第一条只列出可领取任务；第二条原子记录实际领取者并导出批准设计、研究证据、任务文件边界及完整准确基线。若 CODE_READY 失效或任务被其他 AI 领取，则拒绝派发。

7. 任务页面可显式绑定对应的 GitHub Draft PR；提交、具体 HEAD 和 CI 状态从 GitHub 读取，不自动宣称通过产品或台架验收。
8. 点击 **归档到 Git 工作树**，将该受控任务写入 `docs/engineering-control/tasks/<id>.json` 与 `<id>.md`。也可以点击 **将工程项目归档到 Git 分支**，将项目全量工程对象、研究实验、修订与工作包执行状态写入 `docs/engineering-control/projects/<id>/snapshot.json`。

以上归档只写**本地非 main Git 工作树**，不会 Commit、Push 或合并。发布前必须审查任务上下文是否包含私人身份、密钥、未脱敏现场日志等。提交/推送到专业 PR 后，才是真正的 GitHub 版本化工程资料。

## 实现边界

| 能力 | V0.5 |
|---|---|
| 真实读取 GitHub Issue、PR 与 Actions | 已实现，用户触发刷新；不支持后台自动刷新 |
| 本地按项目配置 GitHub 仓库 | 已实现，支持多个不同仓库 |
| GitHub Issue ↔ 工作包 ↔ AI 专业角色 | 已实现显式关联 |
| 独立设计准入后派发、Codex CLI 领取 | 已实现；实际领取有身份与记录 |
| 设计修订或代码基线改变后停止发放旧任务 | 已实现 |
| 项目和任务写入 Git 工作树 | 已实现；不自动 Commit/Push |
| Issue、PR、CI 自动写回 GitHub | **未实现**，当前只读同步 |
| ChatGPT 多个对话自动接任务、持续执行 | **未实现**，需独立代理运行器和权限设计 |
| 定时 AI 项目经理、自动里程碑/计划调整 | **未实现** |
| 远端 Git 资料直接覆盖/恢复本地 SQLite | **未实现**，当前归档单向导出 |
| 完整 GitHub 分页、Branch Protection/Rulesets 审计 | **未实现**，最多读取各类前 100 条并显示截断告警 |
| 现场物理测试状态自动获取及验证 | **未实现**，必须另有真实证据 |

公开仓库通常可匿名读取 GitHub REST；如果是私有仓库，由本地服务进程提供 `GITHUB_TOKEN` 环境变量，**不要把令牌输入网页或上传仓库**。读取失败时保留旧快照、标记同步失败，绝不能把旧状态冒充实时状态。

现有仓库规则 `docs/delivery/` 仍是正式交付流程；`docs/engineering-control/` 当前属于待审查的 V1 候选协议。不修改 Git main、已批准 SAEF 或 ECU 固件。
