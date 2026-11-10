# 研发执行档案｜GitHub 与 AI 工作包协议 V1

> **候选制度，随 Draft PR #28 审查；未经合并批准不替代现行 `docs/delivery/` 规则。**

本目录存放**能够通过 Git 版本管理并由专业编码 AI 接受的工程任务合同**，而不是聊天记录或临时摘要。

## 三个权威来源（禁止混淆）

1. **工程设计基线**：产品/系统要求、接口、架构、详细设计、研究结果和负责人批准。当前 V0.5 的编辑界面使用本机 SQLite；导出的任务合同带全部准确修订指纹。
2. **GitHub 执行事实**：Issue、专业 Draft PR、提交、GitHub Actions。通过 GitHub REST 拉取真实状态；UI 中的同步时间必须显示。GitHub 不代替工程批准和实物验收。
3. **现场验证证据**：本地真实设备实验、原始日志/波形、软件固件版本和审查记录；不得把真实客户资料、Seed/Key 或未经脱敏的原始数据提交公开仓库。

## 任务管理与分工

- 产品与系统设计 AI：研究产品要求、系统功能分配、接口和方案。普通编码 AI **不得**代行。
- 技术预研 AI：研究技术未知并设计实验，实测由授权的现场人员独立执行。
- 软件架构和详细设计 AI：明确软件组件、单元行为、接口、状态、异常、并发和测试合同，提出需要负责人批准的设计修订。
- AI 项目管理：根据目标、设计准入、依赖和 GitHub/CI 事实维护队列、标记阻塞和提出下一步工作。**不可凭空假设完成率或代替负责人批准设计/合并。**
- 专业编码 AI：只领取 `CODER` 且 `CODE_READY` 的工作包；仅编辑授权范围、补齐合同测试，提交 Draft PR 和结果证据。
- 本地 Codex 验证执行：按明确的 PR + 精确 HEAD Build/Test/收集日志；失败只报告不自行做需求研究或改架构。

## 稳定状态语义

`PLANNING` → `DISPATCHED` → `CLAIMED` 是**任务分配与领取**，不是代码交付状态。

`PR_OPEN`、`CI_PASSED`、`INTEGRATED`、`PHYSICAL_VERIFIED`、`USER_ACCEPTED` 必须独立记录并绑定 Git SHA、适用系统配置和证据；不得从领取事件自动推断。

GitHub 的 Issue/PR 与自动化测试结果来自远端真实同步。未绑定设计的 Issue 不能自动被认定 `CODE_READY`。即使 Issue 标记关闭或 PR 合并，也不自动宣称系统功能真正完成。

编码 AI 领取时要求：
- 负责人已经批准工作包、输入的详细设计和所有显式关联；
- 当前软件单元详细设计六项关键合同齐全；
- 目标 Git 源码基线是完整 SHA，文件边界和验收条件已固定；
- 没有受当前变更或技术研究失效影响的设计；
- 工作包绑定当前 GitHub Issue 并明确专业编码角色；
- 服务端原子记录领取者，不能两个 AI 同时领取同一份任务。

如果任意设计修订更新，旧合同失效，禁止继续发放；必须重新审查并重新派发。**这比反复给 AI 发“你要记住”的提示词可靠。**

## Git 文件归档结构

在一个非 `main` 的专业分支中，负责人确认工作包后通过工作台生成：

```text
docs/engineering-control/
  README.md
  agent-work-protocol.json
  projects/<project-id>/snapshot.json  # 项目全量结构化归档（不复制 GitHub 的全部 API 缓存）
  tasks/
    <work-package-id>.json  # 完整机器可读任务清单，设计指纹+Git/Issue 引用
    <work-package-id>.md    # 供 AI 直接阅读的详细上下文
```

这一步只生成**本地 Git 工作树文件**。AI 或用户必须按照既定 Git 交付制度提交到专业分支、创建 Draft PR。只有 Commit + Push 后，其他 AI 对话才能从 GitHub 查阅；本工具不会把“本地导出成功”冒充远端已经归档。

## 接入方式

1. 在 V0.5 工作台中选择 **GitHub 与 AI 执行**，填入 `owner/repo`，点击“从 GitHub 获取真实状态”。公开仓库可直接读取，私有仓库需由本地服务设置 `GITHUB_TOKEN` 环境变量；令牌不保存在页面和 SQLite。
2. GitHub Issue 可以创建待设计编码工作包，但不能直接跳过设计。研究型 Issue 必须进入技术预研体系，不可误派给编码角色。
3. 在需求与架构页面关联具体的已批准详细设计、研究证据和验证条件；完成工作包准入。
4. 负责人选择编码角色并派发。
5. 本地 Codex 接收（工作树根目录执行）：

```powershell
py -3 tools/lifecycle/agent_tasks.py --server http://127.0.0.1:8766 --project <PROJECT_ID> --role DEVICE --worker CODEX-DEVICE --output task-context.md
```

6. 接收成功会记录 `CLAIMED`、领取者、工作包修订和代码基线。Codex 按任务合同编码，创建 Draft PR，随后在执行中心关联真实 PR 并核对准确 HEAD/CI。
7. 用“归档到 Git 工作树”创建版本化任务包。Git 需要显式提交和 Push；工程工作台不直接修改 main。

## 尚未完成的关键能力

当前**没有**自动启动 ChatGPT 其他对话或真实的常驻 AI 项目经理。不存在通过仓库一条 Issue 就自动驱动多个 AI、自动关闭所有测试的机制。

接下来仍需 GitHub 写回 Issue/PR 状态（授权策略、角色和审计）、执行计划/里程碑/依赖调度、各专业 Agent 的可靠异步调度与失败恢复、CI 精确版本对应、现场验证自动采集、从 Git 直接恢复全部工程设计基线。所有这些不能用 UI 展示的一句“AI 自动管理中”冒充完成。
