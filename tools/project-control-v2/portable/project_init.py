#!/usr/bin/env python3
"""Portable, fail-closed project intake scaffold for a *target* Git repository.

No git checkout, network calls, git commits, push, merge, reset or cleanup.
'preview' and 'inspect' are read only; 'apply' creates ONLY missing files.
This is a bootstrap aid, not a project approval or a full web UI.
"""
from __future__ import annotations

import argparse
import hashlib
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
from urllib.parse import urlsplit

ID_RE = re.compile(r"^[a-z][a-z0-9-]{1,62}$")
SLUG_RE = re.compile(r"^[A-Za-z0-9_.-]+$")
MAX_INVENTORY = 2000
SCAFFOLD = (
    "PROJECT_START_HERE.md",
    ".project-control/README.md",
    ".project-control/project.json",
    ".project-control/assets.json",
    ".project-control/decisions.json",
    ".project-control/tasks.json",
    ".project-control/management.json",
    ".project-control/resolve.py",
    ".project-control/workspace_manager.py",
)


class InitError(ValueError):
    pass


def canonical_github_repo(url):
    """Normalize a user-supplied HTTPS GitHub repository URL; never infer it."""
    parsed = urlsplit(url)
    if (parsed.scheme != "https" or parsed.hostname != "github.com"
            or parsed.username or parsed.password or parsed.port
            or parsed.query or parsed.fragment):
        raise InitError("target must be an explicit HTTPS github.com owner/repo URL")
    parts = parsed.path.strip("/").split("/")
    if len(parts) != 2:
        raise InitError("target URL must identify exactly owner/repo")
    owner, repo = parts
    if repo.endswith(".git"):
        repo = repo[:-4]
    if not SLUG_RE.fullmatch(owner) or not SLUG_RE.fullmatch(repo):
        raise InitError("invalid target owner/repository name")
    return "https://github.com/" + owner + "/" + repo + ".git"


def run_git(root, *args, allow_failure=False):
    try:
        result = subprocess.run(
            ["git", "-C", str(root), *args], capture_output=True,
            text=True, encoding="utf-8", errors="replace", timeout=15, check=False)
    except (OSError, subprocess.TimeoutExpired) as e:
        raise InitError("git unavailable or timed out: " + str(e)) from e
    if result.returncode and not allow_failure:
        raise InitError("git " + " ".join(args) + " failed: " + result.stderr.strip()[:240])
    return result.stdout.strip() if not result.returncode else None


def inspect_target(directory, expected_url):
    root = Path(directory).resolve(strict=True)
    if not root.is_dir():
        raise InitError("target checkout is not a directory")
    git_root = run_git(root, "rev-parse", "--show-toplevel")
    if Path(git_root).resolve() != root:
        raise InitError("target must be the root of its own Git checkout")
    remote = run_git(root, "remote", "get-url", "origin")
    if canonical_github_repo(remote) != canonical_github_repo(expected_url):
        raise InitError("Git origin does not match the selected target repository")
    head = run_git(root, "rev-parse", "--verify", "HEAD", allow_failure=True)
    branch = run_git(root, "symbolic-ref", "-q", "--short", "HEAD", allow_failure=True)
    result = subprocess.run(["git", "-C", str(root), "ls-files", "-z"], capture_output=True,
                            timeout=20, check=False)
    if result.returncode:
        raise InitError("unable to inspect tracked project assets")
    names = sorted(x.decode("utf-8", errors="replace") for x in result.stdout.split(b"\0") if x)
    status = run_git(root, "status", "--porcelain", "--untracked-files=all")
    return {
        "root": str(root), "repo_url": canonical_github_repo(expected_url),
        "git_head": head, "branch": branch,
        "tracked_files_count": len(names),
        "tracked_files": names[:MAX_INVENTORY],
        "inventory_truncated": len(names) > MAX_INVENTORY,
        "worktree_dirty": bool(status),
        "remote_branches_prs_issues_ci_checked": False,
        "note": "Git facts are local-only; no remote fetch or approval review was performed.",
    }


def create_preview(inspected, mode, project_id=None, project_name=None):
    if mode not in ("new", "adopt"):
        raise InitError("mode must be new or adopt; do not auto-decide from folder names")
    if mode == "new" and inspected["tracked_files_count"]:
        raise InitError("NEW requested but target has existing tracked assets; inspect ADOPT")
    if mode == "adopt" and not inspected["tracked_files_count"] and not inspected["git_head"]:
        raise InitError("ADOPT requested without observable Git history/assets")
    if inspected["worktree_dirty"]:
        raise InitError("target checkout has local changes; refuse to initialize over them")
    if not project_id:
        name = inspected["repo_url"].rsplit("/", 1)[-1].removesuffix(".git")
        project_id = re.sub(r"[^a-z0-9-]", "-", name.lower())
        project_id = ("project-" + project_id) if len(project_id) < 2 or not project_id[0].isalpha() else project_id
    if not ID_RE.fullmatch(project_id):
        raise InitError("invalid project_id; use lower-case letters, numbers, hyphens (2-63)")
    provisional_name = project_name is None
    if project_name is None:
        project_name = inspected["repo_url"].rsplit("/", 1)[-1].removesuffix(".git")
    if not isinstance(project_name, str) or not project_name.strip() or len(project_name) > 160:
        raise InitError("invalid project name")
    created = datetime.now(timezone.utc).isoformat(timespec="seconds")
    project = {
        "schema": "project-control-target-project/1", "project_id": project_id,
        "display_name": project_name.strip(), "name_status": "PROVISIONAL" if provisional_name else "PROPOSED",
        "repo_url": inspected["repo_url"], "intake_mode": mode.upper(),
        "initialization_state": "CANDIDATE_NOT_APPROVED",
        "formal_product_identity": "UNDECIDED", "phase_gate": "NOT_VERIFIED",
        "scope_decision": "NOT_RECORDED", "management_role": "coordinator",
        "observed_head": inspected["git_head"], "observed_branch": inspected["branch"],
        "created_at": created,
        "warning": "Scaffold presence never implies P0/requirements/design approval.",
    }
    asset = {
        "schema": "project-control-target-assets/1", "project_id": project_id,
        "state": "UNREVIEWED", "observation_source": "LOCAL_GIT_INDEX",
        "observed_head": inspected["git_head"],
        "tracked_file_count": inspected["tracked_files_count"],
        "entries": [{"path": p, "confidence": "OBSERVED", "approval": "UNKNOWN"}
                    for p in inspected["tracked_files"]],
        "truncated": inspected["inventory_truncated"],
        "remote_history_pr_ci": "NOT_CHECKED",
        "warning": "File existence is not evidence of formal Gate or product acceptance.",
    }
    tasks = {
        "schema": "project-control-target-tasks/1", "project_id": project_id,
        "tasks": [], "selection_policy": "EXACTLY_ONE_READY_OR_EXPLICIT_TASK_ID",
        "warning": "No task is authorized merely because this bootstrap was generated.",
    }
    decisions = {
        "schema": "project-control-target-decisions/1", "project_id": project_id,
        "decisions": [], "warning": "No approvals or historic Gate decisions are inferred.",
    }
    instruction = f"""# PROJECT_START_HERE｜{project_name.strip()}（项目实例入口）

本文件属于目标 GitHub 仓库：\`{inspected["repo_url"]}\`。
这是一个**候选管理初始化**，不是正式立项、需求/设计批准或编码授权。

**ChatGPT 工程协调 AI：**先读取 \`.project-control/README.md\`、\`project.json\`、\`assets.json\`、\`decisions.json\`、\`tasks.json\`；自行调查此仓库的真实源码、分支、PR、CI、正式审批资料和现有资产。已有项目先做接管审查，新项目先与负责人确定目标和范围。不得把文件存在当作 Gate PASS。

**本地 Codex：**在用户指定的工作目录中确认本仓库与准确分支/HEAD；读取 \`.project-control/tasks.json\` 并运行 \`python .project-control/resolve.py select\`。仅在存在唯一 READY 且明确授权的任务（或者用户明确指定的已授权任务）时，才读取工作范围、有效设计输入及验收标准并执行。若任务为空/冲突/设计缺失，停止并报告。不得擅自 push、merge、reset、删除其他本地目录或修改正式批准状态。

本项目的源码和管理软件的通用发行包相互独立。
"""
    readme = f"""# 项目管理登记（候选）

项目 ID：\`{project_id}\`。接管模式：\`{mode.upper()}\`。
本目录的全部初始文件都是 **CANDIDATE_NOT_APPROVED**。

阅读顺序：\`project.json\` → \`assets.json\` → \`decisions.json\` → \`tasks.json\` → 仓库中实际工程资产。
\`assets.json\` 只来自当前本地 Git 索引，远端 PR/Issues/CI 和未入 Git 的资产需管理 AI 继续调查。发现已有 \`.project-control\` 时不能覆盖。
\`tasks.json\` 初始为空：后续必须由工程协调者记录可审查的任务合同和授权状态，不能把整个项目初始化当成一条编码指令。

本目录的本地绝对路径、个人 token、原始日志不应默认被提交 GitHub。
"""
    def json_text(value):
        return json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    management = {"schema": "project-control-target-management/1", "project_id": project_id,
                  "revision": 1, "status": "DRAFT", "deliverables": [], "work": [],
                  "milestones": [], "risks": [], "progress": {}, "events": []}
    resolver_source = (Path(__file__).resolve().parent / "target_resolver.py").read_text(encoding="utf-8")
    workspace_source = (Path(__file__).resolve().parent / "workspace_manager.py").read_text(encoding="utf-8")
    return {
        "repo": inspected["repo_url"], "mode": mode.upper(), "project_id": project_id,
        "source_head": inspected["git_head"], "source_branch": inspected["branch"],
        "files": {
            "PROJECT_START_HERE.md": instruction,
            ".project-control/README.md": readme,
            ".project-control/project.json": json_text(project),
            ".project-control/assets.json": json_text(asset),
            ".project-control/decisions.json": json_text(decisions),
            ".project-control/tasks.json": json_text(tasks),
            ".project-control/management.json": json_text(management),
            ".project-control/resolve.py": resolver_source,
            ".project-control/workspace_manager.py": workspace_source,
        },
        "warning": "Candidate only. Requires human authorization for actual Git/GitHub changes.",
    }


def apply_preview(directory, inspected, preview):
    root = Path(directory).resolve(strict=True)
    if str(root) != inspected["root"]:
        raise InitError("target root changed")
    # Re-inspect immediately before write so no stale HEAD, origin or dirty tree slips in.
    latest = inspect_target(root, inspected["repo_url"])
    for key in ("git_head", "branch", "tracked_files_count", "worktree_dirty", "repo_url"):
        if latest[key] != inspected[key]:
            raise InitError("target changed during review: " + key)
    if (root / ".project-control").exists() or (root / "PROJECT_START_HERE.md").exists():
        raise InitError("existing project management record; refuse to overwrite")
    files = preview["files"]
    if tuple(files) != SCAFFOLD:
        raise InitError("unexpected scaffold file set")
    (root / ".project-control").mkdir()
    created = []
    try:
        for relative, text in files.items():
            path = root / relative
            with path.open("x", encoding="utf-8", newline="\n") as handle:
                handle.write(text)
            created.append(relative)
    except (OSError, FileExistsError) as exc:
        # Never erase the partial result or unrelated files automatically.
        raise InitError("partial scaffold; manual review required; created=" +
                        ",".join(created) + "; error=" + str(exc)) from exc
    return {"status": "LOCAL_CANDIDATE_WRITTEN_NOT_COMMITTED",
            "paths": created, "repo": inspected["repo_url"],
            "note": "No Git commits, push or approval have been performed."}


def upgrade_management(directory, inspected, *, apply=False):
    """Add only management.json to a previously bootstrapped project."""
    root = Path(directory).resolve(strict=True)
    latest = inspect_target(root, inspected["repo_url"])
    for key in ("git_head", "branch", "tracked_files_count", "worktree_dirty", "repo_url"):
        if latest[key] != inspected[key]:
            raise InitError("project changed during upgrade review: " + key)
    if latest["worktree_dirty"]:
        raise InitError("cannot upgrade a worktree with uncommitted changes")
    directory = root / ".project-control"
    if directory.is_symlink() or not directory.is_dir():
        raise InitError("missing or unsafe project record directory")
    existing = directory / "project.json"
    if existing.is_symlink() or not existing.is_file():
        raise InitError("not a previously initialized portable project")
    try:
        record = json.loads(existing.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        raise InitError("unreadable prior project identity") from e
    if (record.get("schema") != "project-control-target-project/1"
            or record.get("repo_url") != inspected["repo_url"]
            or not ID_RE.fullmatch(str(record.get("project_id", "")))):
        raise InitError("project record/schema/remote mismatch")
    for item in ("assets.json", "tasks.json", "decisions.json"):
        path = directory / item
        if path.is_symlink() or not path.is_file():
            raise InitError("incomplete previous records: " + item)
        source = json.loads(path.read_text(encoding="utf-8"))
        if source.get("project_id") != record["project_id"]:
            raise InitError("conflicting project identity: " + item)
    management = directory / "management.json"
    if management.exists() or management.is_symlink():
        raise InitError("management.json already exists; refuse to replace")
    proposed = {
        "schema": "project-control-target-management/1",
        "project_id": record["project_id"], "revision": 1, "status": "DRAFT",
        "deliverables": [], "work": [], "milestones": [], "risks": [],
        "progress": {}, "events": []
    }
    if not apply:
        return {"status": "UPGRADE_PREVIEW", "project_id": record["project_id"],
                "would_create": ".project-control/management.json",
                "warning": "No existing management data or approvals will be changed."}
    # Exclusive creation protects historical assets. This is still uncommitted local data.
    with management.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(proposed, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    return {"status": "LOCAL_UPGRADE_WRITTEN_NOT_COMMITTED",
            "project_id": record["project_id"],
            "created": ".project-control/management.json"}


def upgrade_runtime(directory, inspected, *, apply=False,
                    expected_resolver_sha=None, expected_workspace_sha=None):
    """Explicit safe target tool migration; never overwrite unknown revisions.

    This does not replace engineering files, management decisions or task data.
    Old tool bytes are backed up under Git metadata before a controlled update.
    """
    root = Path(directory).resolve(strict=True)
    fresh = inspect_target(root, inspected["repo_url"])
    if fresh["worktree_dirty"] or fresh["git_head"] != inspected["git_head"]:
        raise InitError("runtime upgrade requires a stable clean Git checkout")
    control = root / ".project-control"
    if control.is_symlink() or not control.is_dir():
        raise InitError("target project management records are missing")
    project_path = control / "project.json"
    if project_path.is_symlink() or not project_path.is_file():
        raise InitError("unsafe project record")
    project = json.loads(project_path.read_text(encoding="utf-8"))
    if project.get("schema") != "project-control-target-project/1" or (
            project.get("repo_url") != inspected["repo_url"]):
        raise InitError("target project identity mismatch")
    scripts = {
        "resolve.py": "target_resolver.py",
        "workspace_manager.py": "workspace_manager.py"
    }
    plan = []
    for target_name, source_name in scripts.items():
        target = control / target_name
        source = Path(__file__).resolve().parent / source_name
        new_bytes = source.read_bytes()
        if target.is_symlink():
            raise InitError("unsafe runtime target symlink")
        old = target.read_bytes() if target.exists() else None
        old_sha = hashlib.sha256(old).hexdigest() if old is not None else None
        new_sha = hashlib.sha256(new_bytes).hexdigest()
        expectation = (expected_resolver_sha if target_name == "resolve.py"
                       else expected_workspace_sha)
        operation = ("UNCHANGED" if old == new_bytes else
                     "CREATE" if old is None else "REPLACE_WITH_BACKUP")
        if apply and operation == "REPLACE_WITH_BACKUP" and expectation != old_sha:
            raise InitError("runtime hash mismatch or missing explicit expected hash for " + target_name)
        plan.append((target_name, target, old, new_bytes, old_sha, new_sha, operation))
    preview = {
        "status": "RUNTIME_UPGRADE_PREVIEW",
        "repo_url": inspected["repo_url"],
        "source_head": inspected["git_head"],
        "actions": [{"name": name, "operation": op,
                     "current_sha256": old_sha, "desired_sha256": new_sha}
                    for name, _, _, _, old_sha, new_sha, op in plan],
        "note": "No writes unless --apply; existing runtime always backed up before replacement.",
    }
    if not apply:
        return preview
    backup_base = run_git(root, "rev-parse", "--git-path", "project-control/runtime-backup")
    backup_folder = Path(backup_base)
    if not backup_folder.is_absolute():
        backup_folder = root / backup_folder
    changed = []
    for name, target, old, new, old_sha, _, op in plan:
        if op == "UNCHANGED":
            continue
        if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() != old_sha:
            raise InitError("runtime changed during migration; partial updates require manual review")
        if op == "REPLACE_WITH_BACKUP":
            backup_folder.mkdir(parents=True, exist_ok=True)
            if backup_folder.is_symlink():
                raise InitError("unsafe backup directory")
            backup = backup_folder / (name + "." + old_sha + ".bak")
            if not backup.exists():
                with backup.open("xb") as stream:
                    stream.write(old)
                    stream.flush()
                    os.fsync(stream.fileno())
            elif backup.read_bytes() != old:
                raise InitError("existing backup conflicts with current runtime")
            tmp = target.with_name("." + name + "." + secrets.token_hex(8) + ".tmp")
            try:
                with tmp.open("xb") as stream:
                    stream.write(new)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(tmp, target)
            finally:
                tmp.unlink(missing_ok=True)
        else:
            with target.open("xb") as stream:
                stream.write(new)
        changed.append(name)
    return {
        "status": "LOCAL_RUNTIME_UPGRADED_NOT_COMMITTED",
        "created_or_replaced": changed,
        "backup_dir": str(backup_folder) if any(x[6] == "REPLACE_WITH_BACKUP" for x in plan) else None,
        "note": "Review Git diff; a runtime migration is not a GitHub push or phase approval.",
    }


def cli(argv=None):
    parser = argparse.ArgumentParser(description="Safe target GitHub project initialization (candidate)")
    parser.add_argument("action", choices=["inspect", "preview", "apply", "upgrade-preview", "upgrade",
                                      "upgrade-runtime-preview", "upgrade-runtime"])
    parser.add_argument("--target", required=True, help="Root of already-existing target Git checkout")
    parser.add_argument("--repo-url", required=True, help="User-supplied GitHub repository URL")
    parser.add_argument("--mode", choices=["new", "adopt"])
    parser.add_argument("--project-id")
    parser.add_argument("--project-name")
    parser.add_argument("--expected-resolver-sha")
    parser.add_argument("--expected-workspace-sha")
    args = parser.parse_args(argv)
    try:
        inspected = inspect_target(args.target, args.repo_url)
        if args.action == "inspect":
            result = inspected
        elif args.action in ("upgrade-preview", "upgrade"):
            result = upgrade_management(args.target, inspected, apply=args.action == "upgrade")
        elif args.action in ("upgrade-runtime-preview", "upgrade-runtime"):
            result = upgrade_runtime(
                args.target, inspected, apply=args.action == "upgrade-runtime",
                expected_resolver_sha=args.expected_resolver_sha,
                expected_workspace_sha=args.expected_workspace_sha)
        else:
            if not args.mode:
                raise InitError("--mode must be explicitly chosen after project investigation")
            preview = create_preview(inspected, args.mode, args.project_id, args.project_name)
            if args.action == "preview":
                result = {k: v for k, v in preview.items() if k != "files"}
                result["file_paths"] = list(preview["files"])
                result["candidate_files"] = preview["files"]
            else:
                result = apply_preview(args.target, inspected, preview)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except (InitError, OSError, ValueError) as exc:
        print(json.dumps({"status": "BLOCKED", "error": str(exc)}, ensure_ascii=False),
              file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(cli())
