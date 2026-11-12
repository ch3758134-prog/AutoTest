# 目标仓库 Codex 任务合同（通用候选）

目标仓库初始化后的 `.project-control/tasks.json` 初始为空；**不等于任务已批准**。

## 就绪任务示例（仅结构示例，不得原样发布为 READY）

```json
{
  "id": "IMPL-001",
  "project_id": "example-project",
  "title": "实现已明确设计的某个工作包",
  "state": "DRAFT",
  "executor": "local_codex",
  "mode": "CONTROLLED",
  "authorization": {
    "approved_by": "",
    "approval_reference": ""
  },
  "source": {
    "repo_url": "https://github.com/owner/example-project.git",
    "branch": "feature/approved-baseline",
    "pinned_sha": "40位完整SHA",
    "work_branch": "feature/impl-001"
  },
  "inputs": [
    {
      "type": "DESIGN_BASELINE",
      "path": "docs/design/approved.md",
      "blob_sha": "Git blob的40位SHA"
    }
  ],
  "allowed_paths": ["src/specialist/**", "tests/specialist/**"],
  "acceptance": ["可验证的实施结果和测试条件"],
  "restrictions": ["NO_FORCE_PUSH", "NO_MERGE", "NO_RESET", "NO_UNAUTHORIZED_DELETE"]
}
```

**在目标仓库把任务从 DRAFT 发布为 READY 以前，管理 AI 必须按 [REMOTE_HANDOFF.md](REMOTE_HANDOFF.md) 复核任务的独立人类授权记录 `TASK_EXECUTION_APPROVAL`：该决定必须包含任务 ID、固定来源 SHA、授权人和可追溯批准引用。不能通过仅填写任务 JSON 中的 `approved_by` 绕过这一决定。**

上述结构的 `DRAFT` 不能领取。工程协调 AI 须核对项目有效设计和用户批准，将任务授权信息写入 **目标仓库**、完成版本确认之后才改为 `READY_FOR_LOCAL_IMPLEMENTATION`。仅为创建一次性预研/探索原型的任务可使用 `EXPLORATORY`；必须仍有明确范围和用户授权，不能将探索代码直接作为正式产品实现。

`CONTROLLED` 模式需要在目标仓库 `.project-control/decisions.json` 有匹配的 `DESIGN_APPROVAL` 记录，至少包含：

```json
{
  "kind": "DESIGN_APPROVAL",
  "state": "APPROVED",
  "path": "docs/design/approved.md",
  "blob_sha": "与任务inputs的blob_sha一致",
  "approved_by": "真实审批者",
  "approval_reference": "正式决定的可核实引用"
}
```

本地校验器验证设计文件在任务来源提交中实际存在，且 Git blob 匹配；但 **文本中的审批者字段不是身份学证明**，仍须结合受保护的 GitHub 审批/提交权限和原始人类决定。

## Codex 实施顺序

1. 用户打开指定空工作目录。Codex 核对目标 GitHub 仓库 URL，按任务来源分支克隆到该目录本身；不得随意另建多个 clone。
2. 读取 `PROJECT_START_HERE.md`、`.project-control/project.json`、`.project-control/tasks.json`，然后执行 `python .project-control/resolve.py select`。若有多个 READY，必须明确任务 ID。
3. Clone 必须进入用户指定的本地空文件夹；在获得源仓库后，使用 `.project-control/workspace_manager.py register-existing` 登记真实目录，默认保留，绝不乱建测试目录。再在原始来源分支执行 `python .project-control/resolve.py claim`，创建任务专属分支。
4. 按任务允许范围实施并测试，最后执行 `python .project-control/resolve.py verify-scope`；失败不得自动 reset 或清理。
5. 实施中需要保留测试日志的，可放到 Git 元数据的 `project-control/logs/` 并建立 `test-runs.json`（详见 `WORKSPACE_LIFECYCLE.md`）；Code/测试日志不可混入任务未批准的源码范围。代码全部形成实际提交后，在本地运行 `python .project-control/resolve.py report`。它生成 `project-control-codex-result/1` 回执，位于该 Git worktree 的内部元数据目录，不会修改 `.project-control/tasks.json` 或自动上传。
6. 管理 AI 可使用通用 Web 服务的当前工作区回执收集接口进行独立 Git 范围审查；提交 SHA 未在管理工作区取得则保持未核实。测试日志 SHA 仍须另行核对，不因 Codex 自报就成为 PASS。
7. 提交准确 Git SHA、真实命令/日志证据和工作区处置状态给工程协调者；能否 push/创建 PR/合并由单独授权与真实权限规则决定。

原始本地执行记录和临时绝对路径不默认上传到 GitHub。未经确认的历史内容不得被管理 AI 自动标为批准。
