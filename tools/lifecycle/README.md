# Engineering Workbench｜现役工程研发管理工作台

**当前候选 V1.3｜[Draft PR #28](https://github.com/ch3758134-prog/AutoTest/pull/28)，尚未合并 main。** 这是当前唯一使用入口；[STUDIO_V13.md](STUDIO_V13.md) 是本候选版的具体功能和权限说明。V0.x 至 V1.2 文档仅作版本历史，不再作为执行指令；正式批准记录仍以此前已批准的工程基线为准。

## 产品边界

本工具管理从工程认识、需求、系统组成、架构、接口、详细设计，到实施、验证和变更反馈的完整研发活动。项目管理 AI、研究与设计 AI、编码 AI、本地 Codex 和负责人是同一工程控制体系的不同受限参与者，**不是四套各自独立的项目数据**。

简洁的正式产品身份、项目背景/版本、工程事实与知识、各级设计对象、实施工作包及实验证据是不同概念，不得在“产品定义”中混杂。AI 可提出附来源的知识、影响与精确对象修订候选，不可批准。负责人审批设计和现场验收，审批本身仍受版本控制。

## 安全的第一次使用

1. 按 [START_HERE.md](START_HERE.md) 在独立 Git worktree 检出 PR #28，禁止在现役 AutoTest main/开发目录直接覆盖。
2. 先运行自动化测试，然后用**独立空试用数据库**开启本地 HTML：
   
   ```powershell
   py -3 -m unittest discover -s tools/lifecycle/tests -v
   py -3 tools/lifecycle/server.py --repo . --data "$env:TEMP/engineering-workbench-v13-trial.sqlite3" --port 8766
   ```
   
   浏览器打开 http://127.0.0.1:8766/。若此临时文件已存在会被复用；需要干净体验应指定不存在的新文件名。
3. Git 工程候选的**只读浏览**不需要负责人密钥，也不会自动弹出授权窗。真正的本地设计写入、审查和批准仍使用现有受控接口（授权按钮位于“历史与配置”中）；密钥不交给 AI、Codex 或 GitHub。详见 [V1.3 说明](STUDIO_V13.md)。
4. 若要用真实 Windows 数据，必须先停止旧服务，安全备份 %LOCALAPPDATA%/EngineeringWorkbench/workspace.sqlite3，在**副本**演练迁移、修订和恢复；GitHub 提交不会修改本机 SQLite。

## 项目管理 AI → GitHub → 原生工程对象｜首轮实测

**目前不再用独立的 GitHub 状态报告页冒充工程管理。** 项目管理 AI 将 AutoTest 的真实工程调查结果写为现有 `Store.export_project` 的完整工程快照，归档在 `docs/engineering-control/projects/autotest/snapshot.json`（原有归档路径）。内部是同一套 project/object/revision/relation/knowledge/governance 结构；当前档案包含 15 个待审工程对象及 5 条候选关联，**没有已批准设计、产品身份基线或真实完成率**。

当前 Git HEAD 的候选档案仅可恢复到**空的、独立试用数据库**。本地 Codex 检出 PR #28 指定完整 SHA 后，在已有工作树中运行：

```powershell
py -3 tools/lifecycle/studio_server.py --repo . --git-workspace --data "$env:TEMP/engineering-git-review-<HEAD短SHA>-new.sqlite3" --port 8766
```

上述 `<HEAD短SHA>` 是说明用占位符，执行前替换成实际 SHA 或其它不会与已有试用库重名的文件名；不能使用默认工作库。启动读取指定检出 HEAD 的 Git 文件，经现有 `studio_restore.preview` 完整校验后恢复到原有 SQLite 工程模型。**不会覆盖任何非空工作区**。浏览器的项目选择应直接出现 AutoTest，原有项目驾驶舱、产品与系统、需求与架构和对象修订功能均可读取这些对象；不需要让用户重新建项目或填表。

首次浏览 Git 候选工程对象不需要 Owner 密钥，也不强制弹出授权；真正的本地工程写操作及批准仍须走受保护接口。相关本地授权入口移至“历史与配置”中的次要操作，避免把登录仪式当作项目管理步骤。

本阶段仅证明 Git `snapshot.json` 能进入**同一套**工程对象模型。**暂未完成**：向已存在本地工作区的无损增量更新、冲突合并、用户 HTML 反向写回 GitHub、受保护的远程工程基线签发，以及管理 AI 直接操作 Windows 的 localhost。每次新 SHA 的受控试用应采用新的独立 SQLite；不能声称直接刷新旧 DB 就会同步。

## 已有功能

工程对象与明确修订、审批、受控关系；技术预研、证据审查和不同验证层级；工程认识及来源/语义关系；独立产品身份候选/签发/影响；项目背景单独修订；AI 只读项目简报和受限提案；AI 针对真实需求、系统、能力、架构、接口、详细设计和工作包的差异候选与负责人审查；GitHub Issue/PR/CI 关联、详细设计 CODE_READY、Codex 任务包、Git 精确文件范围检查，以及工程 JSON 导出/空工作区恢复。

**不意味着已完成**：原 Windows SQLite 实际迁移和逐页验收、真实 AI 语义评价、多跳动态影响分析、完整可信 Git 工程批准基线发布/恢复、操作系统级 AI 身份隔离、GitHub Required Checks 强制门禁及 AutoTest 真实硬件验证。HTTP token 无法防止同 Windows 用户直接读取本地数据库或密钥；CI 通过不代表用户验收。

## 现行与历史的区别

- 原四字段产品定义、固定十领域研究强制矩阵及其旧 HTML 已退役；历史记录只读保留，但**不能授权编码**。不得恢复为正式模型。
- 已批准工程对象不允许通过普通编辑覆盖，修改经独立修订候选/人工判断/形成新草稿，旧修订与失效关联保留；不应一项候选讨论冻结所有工作包。
- [RFC-001](../../docs/lifecycle-pilot/RFC-001_可信研发控制架构与AI协作_候选.md) 及 [Agent 工作协议](../../docs/engineering-control/agent-work-protocol.json) 仍为候选，不能冒充已批准治理协议。
- 历史 KEY_SIM 只读原型须显式通过 server.py --legacy 启动，不是现行可编辑工程工作台。

下一条必须以真实 AutoTest 工程材料演练“发现/知识 → 精确工程对象变更 → 人工审查 → 相关设计与工作包复核 → 实施/验证反馈”。优先补上真实证据、语义理解和可信交付链路，而不是添加新的固定表单或空泛流程。
