# Project Control Portable｜真正独立于 AutoTest 的多项目工作台（候选）

## 将通用管理系统文件夹交给新的 ChatGPT 对话

在管理系统研发仓库执行：

```powershell
python tools/project-control-v2/portable/export_package.py --out "ProjectControl.zip"
```

其内容只有 `PORTABLE_MANIFEST.json` 列出的通用系统文件及逐文件 SHA-256 校验单；不会打包 AutoTest 专用 `sources/`、`workspace/`、旧 `app.js`、Git 元数据和本地日志。上传/解压文件夹后，新的 ChatGPT 工程协调 AI 从 `START_HERE.md` 启动。

## GitHub 协作

当管理 AI 需要从新建/接管推进到向 Codex 正式交付任务时，先读取 [REMOTE_HANDOFF.md](REMOTE_HANDOFF.md)。其中规定：先记录独立人类授权、再做任务发布预检、用 GitHub Contents 当前 blob SHA 受控写入、确认目标仓库当前 HEAD，然后让 Codex 在本地执行。其 PR 成果需要按最新 Head/CI/变更范围核对，不能把本地管理界面保存视为远端已发布。

## 使用定位

管理 AI 在 ChatGPT 网页端首先读取本文件夹的 `START_HERE.md`，用户同时提供一个**目标 GitHub 仓库地址**。管理 AI 按 `portable/INITIALIZATION_PROTOCOL.md` 研究目标仓库，建立或接管**目标仓库自己的** `.project-control/`，不把 AutoTest 的产品资料复制过去。

本地 Web 管理台是项目管理系统文件夹中的一个**可选工作环境**：你可以在本机把一个或多个已经初始化的目标 Git 仓库交给它，管理台只读/写这些目标目录下的管理记录。网页端 ChatGPT 与本地 Codex 的实际协作不要求 8791 永久运行。

## 本地启动

需要 Python 3.11+、Git，并已在目标 Git 仓库中完成初始化。**不需要安装 Python 第三方依赖**。

新项目/已有项目的首次候选初始化（如已存在管理目录，请使用 upgrade-preview 判断，不要重置）：

```powershell
python portable/project_init.py inspect --target "D:\Projects\MyProject" --repo-url "https://github.com/owner/MyProject.git"
python portable/project_init.py preview --target "D:\Projects\MyProject" --repo-url "https://github.com/owner/MyProject.git" --mode adopt
# 由管理 AI 和负责人审查来源与变更后才执行
python portable/project_init.py apply --target "D:\Projects\MyProject" --repo-url "https://github.com/owner/MyProject.git" --mode adopt
```

`--mode new` 仅适用于无既有 Git 跟踪资产的新项目。已存在管理记录不得重复 apply。旧版已经有 `.project-control/`、但没有 `management.json` 时：

```powershell
python portable/project_init.py upgrade-preview --target "D:\Projects\MyProject" --repo-url "https://github.com/owner/MyProject.git"
python portable/project_init.py upgrade --target "D:\Projects\MyProject" --repo-url "https://github.com/owner/MyProject.git"
```

上述命令**仅修改本地项目文件**，从不 commit、push、merge 或签发任何工程批准。运行前请先核实文件差异和 Git origin。管理 AI 可以使用 GitHub 连接在目标远端准备这些候选文件；没有写权限时不能谎称远端已完成初始化。

Web 启动示例（在**Project Control 管理系统文件夹根目录**运行，`--target` 可重复）：

```powershell
python portable/web_server.py --target "D:\Projects\MyProject" --target "D:\Projects\AnotherProject" --port 8791
```

浏览器访问 `http://127.0.0.1:8791`。本地服务监听环回地址；第一次保存候选记录需输入服务终端打印的一次性负责人令牌，不保存进仓库。

## 实际提供的能力

- 根据目标仓库 `.project-control/project.json` 和 `git origin` 校验身份；多项目登记不允许相同项目 ID 或同一目录重复出现；
- 选择当前项目并查看 HEAD、分支、Git 未提交状态、资产来源、正式决策及 Codex 任务合同；
- 维护交付成果、WBS 工作候选、工作进展报告、风险和里程碑，均采用**实际项目的本地管理记录**，而不是 AutoTest 的静态 UI 数据；
- 本地管理记录有 revision、原子写入和事件审计。浏览器采用每次修订号进行并发冲突拒绝，不会自动覆盖旧版本；
- 不提供正式 P0/Gate/设计批准按钮，不提供一键 push/merge、任务 READY 权限提升、远端同步伪造或自动回收目录；
- 任务文本可从目标仓库入口复制给 Codex，但未登记唯一有效任务时不能假装存在已授权实施任务。
- Codex 在目标任务分支运行 `python .project-control/resolve.py report`，生成**本地 Git 元数据目录**中的待审回执，包含真实提交、文件差异及来源任务摘要；不自动推送、不宣布测试通过。
- “AI 与 Codex 交接”页面可以从**当前已经登记的 Git 工作区**自动发现这些回执并独立核对 Git 对象。若编码与管理分别在不同 worktree，需后续工作区注册/关联机制才能自动跨目录归集；当前不盲扫磁盘。
- 在同一页面按需点击“显式核对远端 HEAD”，才会执行只读 `git ls-remote` 对比准确分支。未执行时标记 `REMOTE_NOT_CHECKED`；无远端对象历史时不捏造 ahead/behind。

## 目录关系

```text
ProjectControl（管理软件文件夹）
  START_HERE.md
  PORTABLE_MANIFEST.json
  portable/project_init.py
  portable/target_resolver.py
  portable/web_server.py
  portable/web/{index.html,app.js,app.css}
任意目标项目仓库（独立 Git）
  PROJECT_START_HERE.md
  .project-control/
    project.json
    assets.json
    decisions.json
    tasks.json
    management.json
    resolve.py
```

## 真实边界和剩余缺口

1. Web 端只作用于**本地已显式登记的 Git 项目路径**。跨机器/跨 GitHub 的同步由真实受控 Git 提交和人工/AI 审查实现，不是本地保存后自动生效。
2. 初始化模板不能凭文件文本创建“正式批准”或签发 Gate。任务被标 READY 需要与有来源的人类批准、正式设计和 GitHub 权限一致。
3. 这是一套可独立运行的**候选通用 Web 管理台**，但原 `server.py` 和 `app.js` 是 AutoTest 试点，仍保留用于旧界面比较；不能把两套界面混称正式 R1。
4. 当前没有 Windows 浏览器实测或跨平台远端协作端到端验收记录，也没有可靠的系统级文件访问隔离。验证需逐步开展。
5. 同一管理记录现在使用 Git 元数据目录中的协作式跨进程排他锁、修订号和写入前内容比对。锁失效时拒绝覆盖，**不擅自清理陈旧锁**。这不是对不遵守锁协议的其他进程的操作系统安全隔离。
6. 回执中测试日志 SHA 是 Codex 自报，**并非管理系统已独立运行测试**。Git 候选提交不在管理工作区时将其标为 `RESULT_OBJECT_NOT_LOADED`；不得宣布代码已经核实。
7. 任务与项目实体关联的完整图谱、正式审批流、原有复杂计划功能的全部迁移、远端 GitHub 双向冲突处理，仍在后续设计范围内；不能宣称已经全部完成。
