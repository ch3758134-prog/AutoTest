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
3. 终端打印的 Owner access key 只输入本机 HTML 的“负责人授权”，不要交给任何 AI、Codex 或提交到 Git。需要受限管理 AI HTTP 提案时，使用仓库外的独立 owner/manager token 文件；参阅 [V1.3 说明](STUDIO_V13.md)。
4. 若要用真实 Windows 数据，必须先停止旧服务，安全备份 %LOCALAPPDATA%/EngineeringWorkbench/workspace.sqlite3，在**副本**演练迁移、修订和恢复；GitHub 提交不会修改本机 SQLite。

## 已有功能

工程对象与明确修订、审批、受控关系；技术预研、证据审查和不同验证层级；工程认识及来源/语义关系；独立产品身份候选/签发/影响；项目背景单独修订；AI 只读项目简报和受限提案；AI 针对真实需求、系统、能力、架构、接口、详细设计和工作包的差异候选与负责人审查；GitHub Issue/PR/CI 关联、详细设计 CODE_READY、Codex 任务包、Git 精确文件范围检查，以及工程 JSON 导出/空工作区恢复。

**不意味着已完成**：原 Windows SQLite 实际迁移和逐页验收、真实 AI 语义评价、多跳动态影响分析、完整可信 Git 工程批准基线发布/恢复、操作系统级 AI 身份隔离、GitHub Required Checks 强制门禁及 AutoTest 真实硬件验证。HTTP token 无法防止同 Windows 用户直接读取本地数据库或密钥；CI 通过不代表用户验收。

## 现行与历史的区别

- 原四字段产品定义、固定十领域研究强制矩阵及其旧 HTML 已退役；历史记录只读保留，但**不能授权编码**。不得恢复为正式模型。
- 已批准工程对象不允许通过普通编辑覆盖，修改经独立修订候选/人工判断/形成新草稿，旧修订与失效关联保留；不应一项候选讨论冻结所有工作包。
- [RFC-001](../../docs/lifecycle-pilot/RFC-001_可信研发控制架构与AI协作_候选.md) 及 [Agent 工作协议](../../docs/engineering-control/agent-work-protocol.json) 仍为候选，不能冒充已批准治理协议。
- 历史 KEY_SIM 只读原型须显式通过 server.py --legacy 启动，不是现行可编辑工程工作台。

下一条必须以真实 AutoTest 工程材料演练“发现/知识 → 精确工程对象变更 → 人工审查 → 相关设计与工作包复核 → 实施/验证反馈”。优先补上真实证据、语义理解和可信交付链路，而不是添加新的固定表单或空泛流程。
