# Project Control V2｜重新设计的项目研发管理工作台

这是一套面向 **ChatGPT 网页端 Vibe Coding** 的项目研发管理系统，而不是自动多 Agent Loop，也不是旧版草稿/工程对象列表的升级。

本候选分支直接从 main 创建，**不依赖旧 PR #28 的 studio.html、SQLite 或其旧信息架构**。旧 P0 批准和 G1 候审事实引用精确旧 Git 提交，不会因此自动变为 main 的审批或 Gate。

## 在本地运行

本地 Codex 自行检查现有 AutoTest Git 仓库，拉取本 PR 对应的准确 HEAD 后，从仓库根目录执行：

~~~powershell
py -3 tools/project_control/server.py --port 8771
~~~

浏览器访问 http://127.0.0.1:8771/

不需要额外 pip 或 npm，也不依赖旧 SQLite 工作区。页面的数据来自**本地 Git HEAD 中已提交的 project.json**，不会读取同名文件的未提交修改，也不会自动宣称已与 GitHub 远端最新提交同步。每个页面显示本地分支、Git SHA、数据来源及尚未提交的建议。

## 用户能做什么

1. **项目总览**：看到当前值得关注的审查事项、交付目标、近期工作和后续安排，而不是杂乱的草稿。
2. **目标与交付**：项目目标、近期交付和较细的交付结果分别展示，所有候选拆分明确标记为未批准。
3. **工作与计划**：按照当前、下一步、后续组织研究、设计、编码和验证工作，显示责任、依赖、阻碍、交付和完成条件，不虚构日期。
4. **研发流程**：展示 P0、G0、G1～G4 的真实已知状态，区分文档批准、Gate 决定和代码/实体验收。正式流程规范仍需按受控版本接入。
5. **决策与问题**：只把需要负责人判断的事情呈现为工作；不把 AI 内部过程草稿当作正式送审事项。
6. **协作与证据**：复制包含当前 Git SHA、来源链接和退出条件的专业 ChatGPT/Codex 任务交接文本，查看已提交的执行回执。

### 任务修改：本地建议，不是正式审批

任务详情中可填写建议负责人、状态、优先级和理由。保存于 Git 忽略的 .local/suggestions.json，**不会修改正式项目范围、阶段状态、项目计划或源码**。可以通过顶栏导出本地建议文件，由负责人/工程总协调再按 Git 评审程序发布。

如果 Git HEAD 已经变化，服务器将拒绝静默覆盖旧建议。

### Codex 执行回执

执行结果必须从真实本地命令、退出码、Git HEAD、日志与证据产生，不能复制一句“测试通过”当作事实。Codex 可将实际回执组织成下述 JSON：

~~~json
{
  "schema": "project-control-receipt/1",
  "project_id": "autotest",
  "work_id": "W-103",
  "input_sha": "<40位真实输入Git SHA>",
  "actual_head_sha": "<40位真实结果Git SHA>",
  "branch": "<真实分支>",
  "executor": "local Codex",
  "outcome": "INCONCLUSIVE",
  "summary": "确切工作、结果与未决问题",
  "commands": [{"command": "<真实执行命令>", "exit_code": 0, "observation": "<真实输出摘要>"}],
  "evidence_refs": ["<仓库相对路径或 GitHub 证据链接>"]
}
~~~

提交到本地证据工作文件（**不会自动 commit 或 push**）：

~~~powershell
py -3 tools/project_control/receipt.py --receipt <真实回执JSON文件>
~~~

Codex 需要检查回执是否包含密码、个人路径、敏感数据，之后按已授权的 Git 工作流提交并推送。只有进入**当前 Git HEAD** 的 receipts.json 才显示为已同步的执行报告。PASS 不等于产品/实体验收，不能隐藏任何失败退出码。

## 文件及边界

- project.json：版本化项目目标、交付、工作、流程关联、决策及证据来源样本
- server.py：Git HEAD 读取、静态资源和隔离的本地建议 API
- index.html、style.css、app.js：全新的项目管理交互界面
- receipts.json、receipt.py：执行回执登记与有效性检查
- tests/、GitHub Actions：数据来源、HTTP、未同步建议、真实执行回执验证

**当前尚未实现的部分：**正式项目计划版本的批准和发布；真实 GitHub 远端增量同步与冲突合并；已批准流程规范的完整机器化执行；Codex 会话自动采集；Windows 浏览器实测验收。它们绝不可在界面上冒充已完成。

运行自动化测试：

~~~powershell
py -3 -m unittest discover -s tools/project_control/tests -v
node --check tools/project_control/app.js
~~~

**审批边界：**PR 保持 Draft；不合并 main，不改变已批准的 P0 或任何 Gate，也不发出真实设备动作。
