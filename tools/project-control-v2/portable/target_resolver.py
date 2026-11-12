#!/usr/bin/env python3
"""Target-repository Codex task validator (portable, no third-party packages).

The manager writes tasks into this project's .project-control/tasks.json.
Codex chooses a local folder, clones this exact repository and calls this tool.
This tool NEVER clones, executes implementation commands, pushes or deletes.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent.parent
STATE = ROOT / ".project-control"
SHA = re.compile(r"^[a-f0-9]{40}$")
BRANCH = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9./_-]{0,180}$")
TASK_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
READY = "READY_FOR_LOCAL_IMPLEMENTATION"
PROTECTED = ("PROJECT_START_HERE.md", ".project-control/")


class TaskError(ValueError):
    pass


def load():
    project_path = STATE / "project.json"
    tasks_path = STATE / "tasks.json"
    project = json.loads(project_path.read_text(encoding="utf-8"))
    raw = tasks_path.read_bytes()
    registry = json.loads(raw)
    if project.get("schema") != "project-control-target-project/1":
        raise TaskError("unknown target project schema")
    if (registry.get("schema") != "project-control-target-tasks/1"
            or registry.get("project_id") != project.get("project_id")):
        raise TaskError("task registry and project identity mismatch")
    entries = registry.get("tasks")
    if not isinstance(entries, list) or any(not isinstance(t, dict) for t in entries):
        raise TaskError("invalid tasks")
    ids = [t.get("id") for t in entries]
    if any(not isinstance(x, str) or not TASK_ID.fullmatch(x) for x in ids):
        raise TaskError("invalid task identifiers")
    if len(ids) != len(set(ids)):
        raise TaskError("duplicate task identifiers")
    return project, registry, hashlib.sha256(raw).hexdigest()


def repo_url(value):
    if not isinstance(value, str):
        raise TaskError("repository URL is missing")
    u = urlsplit(value)
    if (u.scheme != "https" or u.hostname != "github.com" or u.username
            or u.password or u.port or u.query or u.fragment):
        raise TaskError("expected explicit https://github.com/owner/repo")
    parts = u.path.strip("/").split("/")
    if len(parts) != 2:
        raise TaskError("wrong repository identity")
    owner, name = parts
    if name.endswith(".git"):
        name = name[:-4]
    if not owner or not name or any(not re.fullmatch(r"[A-Za-z0-9_.-]+", s) for s in (owner, name)):
        raise TaskError("invalid repository path")
    return ("https://github.com/" + owner + "/" + name + ".git").lower()


def git(*args, allow_failure=False):
    try:
        result = subprocess.run(["git", "-C", str(ROOT), *args], capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=20)
    except (OSError, subprocess.TimeoutExpired) as e:
        raise TaskError("git unavailable: " + str(e)) from e
    if result.returncode:
        if allow_failure:
            return None
        raise TaskError("Git operation failed: " + " ".join(args) + " " + result.stderr.strip()[:180])
    return result.stdout.strip()


def repo_state(project):
    origin = git("remote", "get-url", "origin")
    if repo_url(origin) != repo_url(project.get("repo_url")):
        raise TaskError("current Git origin is not the registered target")
    if Path(git("rev-parse", "--show-toplevel")).resolve() != ROOT:
        raise TaskError("not at the target repository root")
    head = git("rev-parse", "--verify", "HEAD", allow_failure=True)
    if head is None or not SHA.fullmatch(head):
        raise TaskError("target repository has no valid committed HEAD")
    return {
        "repository": repo_url(origin),
        "head": head,
        "branch": git("branch", "--show-current"),
        "root": str(ROOT),
    }


def pick(project, registry, task_id=None):
    entries = registry["tasks"]
    if task_id is not None:
        entries = [t for t in entries if t["id"] == task_id]
    else:
        entries = [t for t in entries
                   if t.get("state") == READY and t.get("executor") == "local_codex"]
    if len(entries) != 1:
        raise TaskError("no unique authorized Codex task; stop and report")
    t = entries[0]
    if t.get("state") != READY or t.get("executor") != "local_codex":
        raise TaskError("task is not ready for local Codex execution")
    if t.get("project_id") != project.get("project_id"):
        raise TaskError("task belongs to another project")
    source = t.get("source", {})
    if (repo_url(source.get("repo_url")) != repo_url(project["repo_url"])
            or not SHA.fullmatch(str(source.get("pinned_sha", "")))
            or not BRANCH.fullmatch(str(source.get("branch", "")))
            or not BRANCH.fullmatch(str(source.get("work_branch", "")))
            or source["branch"] == source["work_branch"]
            or source["work_branch"] in ("main", "master")):
        raise TaskError("task source or branch pin is invalid")
    auth = t.get("authorization", {})
    if (not isinstance(auth, dict) or not auth.get("approved_by")
            or not auth.get("approval_reference")):
        raise TaskError("task lacks a human-confirmed implementation authorization")
    required = {"NO_FORCE_PUSH", "NO_MERGE", "NO_RESET", "NO_UNAUTHORIZED_DELETE"}
    if not required.issubset(set(t.get("restrictions", []))):
        raise TaskError("mandatory operation restrictions are missing")
    paths = t.get("allowed_paths")
    if not isinstance(paths, list) or not paths:
        raise TaskError("no file scope")
    for path in paths:
        if (not isinstance(path, str) or not path or path.startswith("/")
                or "\\" in path or ".." in path.split("/") or path in ("*", "**")):
            raise TaskError("unsafe file scope")
        if path.startswith(PROTECTED[1]) or path == PROTECTED[0]:
            raise TaskError("task cannot rewrite its own governance entry")
    if not t.get("inputs") or not t.get("acceptance"):
        raise TaskError("missing engineering inputs or acceptance")
    return t


def assert_source_state(state, task):
    source = task["source"]
    if state["branch"] != source["branch"]:
        raise TaskError("not on the designated source branch")
    p = subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor",
                        source["pinned_sha"], state["head"]], capture_output=True, timeout=20)
    if p.returncode:
        raise TaskError("pinned source SHA is not an ancestor of current HEAD")


def claim_path(task):
    rel = git("rev-parse", "--git-path", "project-control/claim-" + task["id"] + ".json")
    path = Path(rel)
    return path if path.is_absolute() else ROOT / path


def claim(state, task, digest):
    assert_source_state(state, task)
    if git("status", "--porcelain", "--untracked-files=all"):
        raise TaskError("working tree is dirty")
    target = claim_path(task)
    if target.exists():
        raise TaskError("already claimed; must not overwrite original task baseline")
    payload = {
        "schema": "project-control-target-claim/1",
        "task_id": task["id"],
        "task_registry_sha256": digest,
        "source_head": state["head"],
        "workspace_root": str(ROOT),
        "created_at": datetime.now(timezone.utc).isoformat(),
        "disposition": "KEEP_FOR_REVIEW",
    }
    target.parent.mkdir(parents=True, exist_ok=True)
    with os.fdopen(os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600),
                   "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return payload


def changed_files(source_head):
    changed = set()
    for args in (("diff", "--no-renames", "--name-only", "-z", source_head, "HEAD"),
                 ("diff", "--no-renames", "--name-only", "-z"),
                 ("diff", "--cached", "--no-renames", "--name-only", "-z"),
                 ("ls-files", "--others", "--exclude-standard", "-z")):
        try:
            r = subprocess.run(["git", "-C", str(ROOT), *args], capture_output=True,
                               timeout=20, check=False)
        except (OSError, subprocess.TimeoutExpired) as e:
            raise TaskError("unable to inspect file scope: " + str(e)) from e
        if r.returncode:
            raise TaskError("unable to inspect changed files")
        changed.update(p.decode("utf-8", errors="replace")
                       for p in r.stdout.split(b"\0") if p)
    return sorted(changed)


def path_allowed(name, patterns):
    return any((name.startswith(p[:-3] + "/") if p.endswith("/**") else name == p)
               for p in patterns)


def verify(state, task, digest):
    if state["branch"] != task["source"]["work_branch"]:
        raise TaskError("not on the designated task branch")
    p = claim_path(task)
    if not p.is_file():
        raise TaskError("missing original task claim")
    old = json.loads(p.read_text(encoding="utf-8"))
    if (old.get("task_id") != task["id"]
            or old.get("task_registry_sha256") != digest
            or old.get("workspace_root") != str(ROOT)
            or not SHA.fullmatch(str(old.get("source_head", "")))):
        raise TaskError("claim or remote task registry changed")
    parent = old["source_head"]
    check = subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor",
                            parent, state["head"]], capture_output=True, timeout=20)
    if check.returncode:
        raise TaskError("task commit history no longer descends from claim")
    modifications = changed_files(parent)
    outside = [x for x in modifications
               if x == PROTECTED[0] or x.startswith(PROTECTED[1])
               or not path_allowed(x, task["allowed_paths"])]
    return {"task_id": task["id"], "start_head": parent, "current_head": state["head"],
            "modified_paths": modifications, "outside_scope": outside,
            "status": "BLOCKED_SCOPE" if outside else "SCOPE_OK",
            "note": "Scope check does not approve results, merges or local cleanup."}


def main(argv=None):
    p = argparse.ArgumentParser(description="Validate the target project's remote Codex task")
    p.add_argument("action", choices=["select", "claim", "verify-scope"])
    p.add_argument("--task")
    args = p.parse_args(argv)
    try:
        project, registry, digest = load()
        task = pick(project, registry, args.task)
        state = repo_state(project)
        if args.action == "select":
            assert_source_state(state, task)
            result = {"task": task, "state": state, "registry_sha256": digest}
        elif args.action == "claim":
            result = claim(state, task, digest)
        else:
            result = verify(state, task, digest)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 3 if result.get("status") == "BLOCKED_SCOPE" else 0
    except (TaskError, OSError, ValueError, TypeError, json.JSONDecodeError) as e:
        print(json.dumps({"status": "BLOCKED", "error": str(e)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
