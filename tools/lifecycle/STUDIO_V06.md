# Engineering Workbench V0.6｜统一工程控制内核（候选）

> **PR #28：Draft，仅在独立管理分支实现。** V0.6 不替换现行 AutoTest `docs/delivery/` 交付规则，也未改变正式 main/固件/生产源码。V0.6 属于可测试的管理基础设施，不是完整自主 AI 项目经理。

## 为什么不做四套独立接口

项目负责人、管理 AI、需求/架构设计 AI、研究 AI、纯编码 AI 与 Codex 是**同一工程事实的不同参与者**。系统应当只有一个统一的工程对象关系、审查历史和基线；不同参与者只得到所需数据及受控操作。临时任务 Markdown、AGENTS.md、Skill 和 GitHub PR 不能成为互不一致的第二套规范。

权威划分：
- Git 中受控发布的工程配置/设计版本与协议：正式工程基线（目前仍在实现完整恢复）。
- GitHub：Issue、PR、Commit 与 CI 的真实执行记录。
- 独立的台架/试验记录：适用设备版本、原始数据与结论。
- 本地 SQLite：编辑工作区、草稿、快照索引和待审查建议，不得通过导出宣称自动得到正式批准。

## V0.6 已经落地的实际能力

### 1. 项目管理 AI 的事实简报及建议收件箱

浏览器新增 **AI 项目管理与审批**：

- 工程需求、架构、详细设计及修订引用；
- 当前工作包 CODE_READY、研究未决问题、变更和已失效的任务；
- GitHub 的精确仓库、最近同步时间、可用的 Issue / PR / CI 缓存（未同步则明确未知）；
- **项目管理 AI 的 PENDING 建议**：类别（需求澄清、研究、架构审查、详细设计、编码、集成、验证、计划、阻塞）、理由、来源对象、优先级和建议行动。
- 负责人可选择 `ACCEPT_FOR_PLANNING`、`NEEDS_INFORMATION`、`REJECT`；该选择**不会自动批准需求、设计、技术研究、实测，也不会派发代码**。

专门的管理 AI 命令工具可以读取事实简报：

```powershell
py -3 tools/lifecycle/manager_agent.py --server http://127.0.0.1:8766 --project <PROJECT_ID> --brief --output management-brief.json
```

提交建议需要另外的受限提案凭据（与浏览器管理令牌不同）：

```powershell
py -3 tools/lifecycle/manager_agent.py --server http://127.0.0.1:8766 --project <PROJECT_ID> --propose proposal.json --manager-token-file <PRIVATE_TOKEN_FILE>
```

其中 `proposal.json` 示例（请替换实际项目 ID）：

```json
{
  "project_id": "32位真实项目ID",
  "category": "TECHNICAL_RESEARCH",
  "priority": "P1",
  "title": "调查共享通信总线的实际并发约束",
  "rationale": "当前软件架构依赖尚未测量的时序条件",
  "next_action": "提出受控实验方案，明确环境、可判定指标和原始证据要求",
  "source_objects": [],
  "proposed_by": "AI-PROJECT-MANAGER"
}
```

启动管理服务时需事先生成足够长的本地秘密文件，然后提供 `--manager-token-file <PRIVATE_TOKEN_FILE>`。缺失时 AI 提案提交默认关闭；浏览器负责人仍可以手工登记待审查建议。**此令牌只作为受控 API 的能力分界，不能视为针对同一 Windows 用户下恶意 AI 进程的强安全隔离。** 不得把凭据放入 Git 或发给不可信 AI。

### 2. Git 已提交工程快照对照

管理页面可点击 **核对 Git 工程版本**，对指定的完整提交 SHA 或 HEAD 中的

`docs/engineering-control/projects/<PROJECT_ID>/snapshot.json`

进行只读比较。逐一识别 `LOCAL_ONLY` / `GIT_ONLY` / `LOCAL_NEWER` / `GIT_NEWER` / `SAME_REVISION_CONFLICT` / `SAME`。

系统读取 Git 的**已提交字节**，而不是容易被临时修改的本地文件。会检查所配置 GitHub origin 是否对应项目仓库、快照项目 ID 是否一致；展示提交 SHA、源文件哈希与本地 main 历史关联。即使文件已经位于 main，仍需看真实仓库保护/审批证据，不能认定“main 里存在”就是批准了全部设计。

**当前只比较、不覆盖/合并/恢复；完整可信的 Git → SQLite 批准基线恢复仍未完成。**

### 3. 编码 AI 的 Git 改动范围核查

新增 `tools/lifecycle/verify_coding_scope.py`：

```powershell
py -3 tools/lifecycle/verify_coding_scope.py --repo . --manifest docs/engineering-control/tasks/<TASK_ID>.json --head <EXACT_PR_HEAD_SHA>
```

检查工作包的 `engineering-coding-task/1` 格式、执行角色、源码基线的 40 位 SHA、代码基线一致性、允许路径、真实 Git HEAD、基线是否是 HEAD 的祖先、实际提交差异。禁止普通编码 AI 修改受保护的协议文件、CI、治理目录或 `AGENTS.md`，即使提交方伪装为已授权。

**重要：这还不是正式 GitHub 保护门禁**。必须将校验器与经过批准且不能由 PR 自改的任务合同绑定，配置为目标仓库的 Required Check 才能强制执行。直接给校验器传一份 PR 作者自己可以修改的 manifest，并不能证明该工作包已被负责人批准。

## 可以怎样实测

```powershell
git fetch origin pull/28/head
git merge --ff-only FETCH_HEAD
py -3 -m unittest discover -s tools/lifecycle/tests -v
py -3 tools/lifecycle/server.py --repo . --port 8766
```

若 8766 已被旧进程占用，应先在原服务终端 `Ctrl+C` 后重新启动。网页左侧应出现 **AI 项目管理与审批**；先保证有项目数据，再查看工程阻塞和复制 AI 管理上下文。对于本机有 Git 专业工作树且已归档项目的情况，可再做 Git 版本差异比较。

## 必须继续解决的缺口

- **真实身份隔离**：V0.5 浏览器 Bootstrap 仍给浏览器写入令牌，具有同 Windows 用户权限的其他本地进程可能取得该令牌，因此不能宣称已实现隔离的自动管理/自主审批；应使用独立受信服务/操作系统账号或代理隔离，负责人批准应通过专门身份渠道。
- **Git 权威基线发布/恢复**：还需建立经审查的 baseline manifest、严格发布事务、冲突合并和从可信 Git 提交恢复（非覆写）。
- **GitHub Required Check**：代码范围检查已可单独运行，但尚未作为正式全仓必需检查生效。
- **项目进度推进**：尚没有完整的里程碑、任务依赖调度、计划基线与预测；管理 AI 只能提议下一步，不能自动调度多个 ChatGPT 对话。
- **Codex 独立执行**：现有 `agent_tasks.py` 仍借用浏览器 Bootstrap token 领取任务；在权限隔离完成前，只限可信单机使用，不得把它当作受限独立执行凭据。
- **测试**：这次测试验证的是管理逻辑、Git 工作树和 API，仍需 Windows 浏览器端操作与现场软硬件测试；不能据此宣布 AutoTest 产品已通过验收。

## 审查依据

- 具体方案与不变量：`docs/lifecycle-pilot/RFC-001_可信研发控制架构与AI协作_候选.md`
- 固定交付旧制度：`docs/delivery/01_交付协议与审批门禁.md`
- Git 工程候选协议：`docs/engineering-control/README.md`
- 完整当前流程：`tools/lifecycle/STUDIO_V05.md`

下一轮应优先完成**受信审批身份 + Git 批准基线恢复**，然后才允许可信的自动项目管理或无人值守派工。
