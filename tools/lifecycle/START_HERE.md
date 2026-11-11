# Engineering Workbench V1.3｜Windows 启动与评审入口

> 本页适用 [Draft PR #28](https://github.com/ch3758134-prog/AutoTest/pull/28) 当前管理分支候选，不等于 AutoTest 正式 main 已批准。原 V0.1～V0.3 的 KEY_SIM 演示与命令已退役为历史材料，不能再作为当前启动步骤。

## 1. 单独检出，不打扰当前项目

在**现有 AutoTest 仓库根目录**，Windows PowerShell 执行（仅首次创建该 review 分支和工作树时）：

```powershell
git fetch origin pull/28/head:refs/heads/review/lifecycle-pr28
git worktree add ../AutoTest-Lifecycle review/lifecycle-pr28
cd ../AutoTest-Lifecycle
git rev-parse HEAD
```

如果上述 review 分支或工作树已存在，**不要重复创建、不要强行 reset 或覆盖修改**。在已有 AutoTest-Lifecycle 工作树中确认没有未提交工作后，再执行：

```powershell
git fetch origin pull/28/head
git merge --ff-only FETCH_HEAD
git rev-parse HEAD
```

HEAD 必须与 PR #28 当前 HEAD 对照；不同则停止并排查，不拿 main 或过时 HEAD 假装已更新。

## 本轮真实项目管理验收优先入口（比空白示范更重要）

首次做 AutoTest 项目管理 AI 协作验收时，不要使用空白数据库创建项目。**先从当前 Git HEAD 已提交的原生工程快照恢复一份全新的、独立试用数据库**：

```powershell
# 须位于 PR #28 的准确工作树，并先确认 Git HEAD
git rev-parse HEAD
$head = (git rev-parse --short=12 HEAD).Trim()
$db = Join-Path $env:TEMP ("engineering-git-review-" + $head + ".sqlite3")
if (Test-Path $db) { throw "试用库已存在，请换一个新文件名；不得删除或覆盖" }
py -3 tools/lifecycle/studio_server.py --repo . --git-workspace --data $db --port 8766
```

如果 8766 已有服务在运行，明确是之前启动的工作台后先正常停止它，或者选择其它空闲端口；绝不终止无关进程。

此模式会对 `docs/engineering-control/projects/autotest/snapshot.json` 中的 `engineering` 做原有 `studio_restore` 全结构校验，再恢复到**同一套** SQLite 工程对象模型（不是第二个报告页），显示 Git commit 与恢复的项目 ID。HTML 项目选择直接出现 **AutoTest**；可以打开需求/架构/问题/实测证据对象。恢复的 15 个对象只是待审草稿/开放问题，关系仍是候选，并未获得批准。此功能拒绝非空工作区，不能用在默认现役 SQLite 上。

GitHub 后续更新到新 SHA 时，仍需使用新的试用 DB；当前版本**尚不支持安全地向已有工作区自动增量合并**。不要把重新检出源码当成现场资料自动同步。

## 2. 用独立试用数据库启动

先在上述候选工作树测试（Python 3.10+，标准库）：

```powershell
py -3 -m unittest discover -s tools/lifecycle/tests -v
py -3 tools/lifecycle/server.py --repo . --data "$env:TEMP/engineering-workbench-v13-trial.sqlite3" --port 8766
```

浏览器打开 `http://127.0.0.1:8766/`。这个数据库位置**不是**默认真实数据路径；如果同名试用文件已存在会被复用，可选择新的文件名以检验完全空的工作区。

启动终端显示 `Owner access key for this launch`。点击 HTML 页面上的“负责人授权”，输入密钥后才能执行负责人写操作；不要把密钥复制到对话、Issue 或任何 AI 环境。本机只读入口不再返回 owner 密钥。关闭服务用 Ctrl+C。

管理 AI HTTP 模式必须分别传入不同的、存储在仓库外的 owner 与 manager 密钥文件（至少 32 个字符），例如添加 `--owner-token-file` 和 `--manager-token-file`。同 Windows 用户无法仅靠 token 构成真正权限隔离，详见 [STUDIO_V13.md](STUDIO_V13.md)。

## 3. 真实本地数据只用副本演练

Windows 默认数据库位置：`%LOCALAPPDATA%\EngineeringWorkbench\workspace.sqlite3`。不要把演练命令改成默认参数后直接打开现役数据库。

先停止现有服务、备份数据库及存在的 SQLite WAL/SHM 或使用 SQLite 一致性备份工具，将备份副本提供给独立工作树的 `--data` 参数。在副本中检查产品身份、旧定义历史、知识、工程修订和批准历史；重新导出并**仅向另一个全新空工作区**测试 JSON 恢复。任意不能解释的数据差异都不得进入真实工作区。

## 4. 必须实际检查的事项

- 产品身份保持简洁；项目版本、EPB 样本、R1 场景、CAN/按键技术细节不被自动并入身份。
- 管理 AI 可提出知识、产品身份和具体工程对象的修订，但不能批准、应用、派发或伪造负责人权限。
- 已批准对象不能普通编辑覆盖；应用修订形成新草稿；相关工作包重审，不相关工作不因讨论而全部中断。
- 旧四字段定义及固定十领域审批入口不能再被用来授予编码；新的产品身份批准必须真正被执行门禁检查。
- GitHub 的 Build/Unit PASS、产品实际运行、原始现场证据、用户接受必须保持各自状态与来源，不相互冒充。
- 验证工作台的工程 JSON 可以从**空工作区**恢复；原始日志、源码仓库和外部资产并未由该 JSON 自动备份。

这些检查中，自动化测试通过只证明相应代码路径；本地浏览器操作和真实工程档案的审查结果必须另行记录。**不得仅因 UI 显示完整或 CI 绿灯就审批候选 PR。**

现行功能与缺口见 [README.md](README.md) 和 [STUDIO_V13.md](STUDIO_V13.md)。历史只读 KEY_SIM 仪表板只有明确执行 `py -3 tools/lifecycle/server.py --legacy --repo . --port 8767` 才会启动。
