#!/usr/bin/env python3
"""Local Codex task resolver: remote task metadata, claim, and file-scope checks.

No network writes, code execution, git checkout, cleanup, push or approval.
Run from a verified clone of the configured source repository.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from datetime import datetime, timezone

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[3]
DISPATCH = HERE / "dispatch.json"
HEX40 = re.compile(r"^[0-9a-f]{40}$")
BRANCH = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,180}$")
READY = "READY_FOR_LOCAL_IMPLEMENTATION"
REQUIRED_DENIALS = {"NO_PUSH", "NO_MERGE", "NO_MAIN_WRITE", "NO_RESET", "NO_FORCE_CLEAN"}


class DispatchError(RuntimeError):
    pass


def read_registry(path=DISPATCH):
    raw = Path(path).read_bytes()
    obj = json.loads(raw)
    if obj.get("schema") != "project-control-codex-dispatch/1":
        raise DispatchError("unsupported dispatch schema")
    tasks = obj.get("tasks")
    if not isinstance(tasks, list):
        raise DispatchError("dispatch tasks must be a list")
    ids = [t.get("id") for t in tasks if isinstance(t, dict)]
    if len(ids) != len(tasks) or len(ids) != len(set(ids)):
        raise DispatchError("duplicate or invalid task records")
    return obj, hashlib.sha256(raw).hexdigest()


def pick(registry, task_id=None):
    tasks = registry["tasks"]
    if task_id:
        match = [t for t in tasks if t.get("id") == task_id]
    else:
        match = [t for t in tasks if t.get("status") == READY
                 and t.get("role") == "local_executor"]
    if len(match) != 1:
        raise DispatchError("no unique authorized Codex task; stop and request task selection")
    task = match[0]
    if task.get("status") != READY or task.get("role") != "local_executor":
        raise DispatchError("selected task is not ready for local Codex implementation")
    auth = task.get("authorization", {})
    if auth.get("classification") != "LIMITED_LOCAL_CANDIDATE_IMPLEMENTATION" or not auth.get("reference"):
        raise DispatchError("missing limited local authorization record")
    source = task.get("source", {})
    if (not HEX40.fullmatch(str(source.get("minimum_ancestor_sha", ""))) or
            not BRANCH.fullmatch(str(source.get("branch", ""))) or
            not BRANCH.fullmatch(str(source.get("local_task_branch", "")))):
        raise DispatchError("invalid source SHA or branch")
    if source["local_task_branch"] == source["branch"] or source["branch"] in ("main", "master"):
        raise DispatchError("local implementation must use its own non-default branch")
    if source.get("repo_full_name") != "ch3758134-prog/AutoTest":
        raise DispatchError("unexpected task source repository")
    if not task.get("workspace", {}).get("new_git_clone_in_selected_directory"):
        raise DispatchError("task lacks empty-directory clone contract")
    if task["workspace"].get("disposition") != "KEEP_UNTIL_REVIEWED":
        raise DispatchError("unsafe local workspace disposition")
    if not REQUIRED_DENIALS.issubset(set(task.get("restrictions", []))):
        raise DispatchError("missing mandatory destructive-operation restrictions")
    paths = task.get("allowed_paths", [])
    if not isinstance(paths, list) or not paths:
        raise DispatchError("no explicit file scope")
    for value in paths:
        if not isinstance(value, str) or value.startswith("/") or "\\" in value or ".." in value.split("/"):
            raise DispatchError("invalid allowed path")
    if not task.get("inputs") or not task.get("acceptance"):
        raise DispatchError("missing engineering inputs or acceptance")
    return task


def git(repo, *args):
    result = subprocess.run(["git", "-C", str(repo), *args], capture_output=True,
                            text=True, encoding="utf-8", errors="replace", timeout=20)
    if result.returncode:
        raise DispatchError("git " + " ".join(args) + " failed: " + result.stderr.strip()[:200])
    return result.stdout.strip()


def repo_identity(repo, task):
    root = Path(repo).resolve()
    if Path(git(root, "rev-parse", "--show-toplevel")).resolve() != root:
        raise DispatchError("not at repository root; wrong workspace")
    remote = git(root, "remote", "get-url", "origin")
    expected = task["source"]["repo_full_name"].lower()
    # Git URLs may be HTTPS or SSH, but must identify the exact repository.
    match = re.search(r"(?:github[.]com[:/])([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:[.]git)?/?$", remote, re.I)
    if not match or match.group(1).lower() != expected:
        raise DispatchError("wrong origin: cannot use another PR or repository")
    head = git(root, "rev-parse", "HEAD")
    if not HEX40.fullmatch(head):
        raise DispatchError("invalid local HEAD")
    ancestor = task["source"]["minimum_ancestor_sha"]
    test = subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor",
                           ancestor, head], capture_output=True, timeout=20)
    if test.returncode:
        raise DispatchError("wrong source history; expected management PR #34 ancestor")
    branch = git(root, "branch", "--show-current")
    if branch not in (task["source"]["branch"], task["source"]["local_task_branch"]):
        raise DispatchError("wrong local branch")
    return {"root": str(root), "origin": remote, "head": head, "branch": branch}


def claim_path(repo):
    raw = git(repo, "rev-parse", "--git-path", "project-control/codex-claim.json")
    path = Path(raw)
    return path if path.is_absolute() else Path(repo) / path


def create_claim(repo, task, registry_sha):
    info = repo_identity(repo, task)
    if info["branch"] != task["source"]["branch"]:
        raise DispatchError("claim must be created on the original management source branch")
    if git(repo, "status", "--porcelain", "--untracked-files=all"):
        raise DispatchError("initial checkout is dirty; refuse to claim")
    path = claim_path(repo)
    if path.exists():
        raise DispatchError("an existing task claim must not be overwritten")
    data = {"schema": "project-control-codex-claim/1", "task_id": task["id"],
            "registry_sha256": registry_sha, "start_head": info["head"],
            "workspace_root": info["root"],
            "created_at": datetime.now(timezone.utc).isoformat(),
            "disposition": "KEEP_UNTIL_REVIEWED"}
    path.parent.mkdir(parents=True, exist_ok=True)
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    with os.fdopen(os.open(path, flags, 0o600), "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    return data


def paths_changed(repo, start_head):
    args = [("diff", "--name-only", "-z", start_head, "HEAD"),
            ("diff", "--name-only", "-z"),
            ("diff", "--cached", "--name-only", "-z"),
            ("ls-files", "--others", "--exclude-standard", "-z")]
    changed = set()
    for arguments in args:
        result = subprocess.run(["git", "-C", str(repo), *arguments], capture_output=True, timeout=20)
        if result.returncode:
            raise DispatchError("cannot inspect all Git changes")
        changed.update(x.decode("utf-8", errors="replace") for x in result.stdout.split(b"\x00") if x)
    return sorted(changed)


def allowed(path, scope):
    for pattern in scope:
        if pattern.endswith("/**"):
            if path.startswith(pattern[:-3] + "/"):
                return True
        elif path == pattern:
            return True
    return False


def verify_scope(repo, task, registry_sha):
    info = repo_identity(repo, task)
    if info["branch"] != task["source"]["local_task_branch"]:
        raise DispatchError("task implementation must use its own designated local branch")
    path = claim_path(repo)
    if not path.is_file():
        raise DispatchError("missing local claim; do not fabricate execution history")
    claim = json.loads(path.read_text(encoding="utf-8"))
    if (claim.get("task_id") != task["id"] or claim.get("registry_sha256") != registry_sha
            or claim.get("workspace_root") != info["root"]
            or not HEX40.fullmatch(str(claim.get("start_head", "")))):
        raise DispatchError("task claim mismatch")
    start = claim["start_head"]
    result = subprocess.run(["git", "-C", str(repo), "merge-base", "--is-ancestor", start, "HEAD"],
                            capture_output=True, timeout=20)
    if result.returncode:
        raise DispatchError("task baseline no longer an ancestor")
    modified = paths_changed(repo, start)
    out_of_scope = [x for x in modified if not allowed(x, task["allowed_paths"])]
    return {"task_id": task["id"], "start_head": start, "current_head": info["head"],
            "modified_paths": modified, "out_of_scope": out_of_scope,
            "status": "BLOCKED_OUT_OF_SCOPE" if out_of_scope else "SCOPE_OK",
            "note": "Scope check is not a safety approval or proof of Windows execution."}


def main(argv=None):
    parser = argparse.ArgumentParser(description="Verify GitHub-dispatched Codex local task")
    parser.add_argument("action", choices=["select", "claim", "verify-scope"])
    parser.add_argument("--task")
    parser.add_argument("--repo", type=Path, default=REPO_ROOT)
    args = parser.parse_args(argv)
    try:
        registry, registry_sha = read_registry()
        task = pick(registry, args.task)
        if args.action == "select":
            result = {"task": task, "registry_sha256": registry_sha,
                      "repository": repo_identity(args.repo, task)}
        elif args.action == "claim":
            result = create_claim(args.repo, task, registry_sha)
        else:
            result = verify_scope(args.repo, task, registry_sha)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 3 if result.get("status") == "BLOCKED_OUT_OF_SCOPE" else 0
    except (DispatchError, OSError, ValueError, json.JSONDecodeError) as exc:
        print(json.dumps({"status": "BLOCKED", "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
