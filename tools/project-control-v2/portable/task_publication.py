#!/usr/bin/env python3
"""Read-only preflight for promoting a target project's Codex task to READY.

For a ChatGPT manager with a connected GitHub writer:
  - Read the current target GitHub project, task and decisions records.
  - Apply human-authorized decision edits in a *separate* reviewed commit.
  - Run or reproduce this preflight for the proposed task against its pinned
    source branch / SHA / approved design. Never infer an approval.
  - Publish the returned tasks.json candidate with GitHub Contents API using
    the *current source blob SHA* (optimistic content compare-and-swap).
No push/checkout/merge/delete/automatic remote writes performed here.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import urlsplit

HERE = Path(__file__).resolve().parent
SHA40 = re.compile(r"^[0-9a-f]{40}$")
IDENT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
BRANCH = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,180}$")
READY = "READY_FOR_LOCAL_IMPLEMENTATION"
FORBIDDEN = (".project-control/", "PROJECT_START_HERE.md")
DENIALS = frozenset({"NO_FORCE_PUSH", "NO_MERGE", "NO_RESET", "NO_UNAUTHORIZED_DELETE"})


class PublicationError(ValueError):
    pass


def canonical_url(value):
    if not isinstance(value, str):
        raise PublicationError("target GitHub URL is required")
    u = urlsplit(value)
    if u.scheme != "https" or u.hostname != "github.com" or u.username or u.password or u.port or u.query or u.fragment:
        raise PublicationError("only explicit HTTPS github.com owner/repo is supported")
    pieces = u.path.strip("/").split("/")
    if len(pieces) != 2:
        raise PublicationError("expected owner/repo")
    pieces[1] = pieces[1].removesuffix(".git")
    if any(not re.fullmatch("[A-Za-z0-9_.-]+", p) for p in pieces):
        raise PublicationError("invalid GitHub repository")
    return "/".join(pieces).lower()


def read_git(root, *args, missing_ok=False):
    try:
        result = subprocess.run(["git", "-C", str(root), *args],
                                capture_output=True, timeout=15)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise PublicationError("local Git verification is unavailable") from exc
    if result.returncode:
        if missing_ok:
            return None
        raise PublicationError("Git read failed: " + result.stderr.decode(errors="replace")[:160])
    return result.stdout.decode("utf-8", errors="replace").strip()


def valid_repo_path(value):
    if (not isinstance(value, str) or not 1 <= len(value) <= 400
            or value.startswith("/") or "\\" in value or ":" in value
            or any(p in ("", ".", "..") for p in value.split("/"))):
        raise PublicationError("unsafe or ambiguous project-relative path")
    return value


def validate_task(project, tasks, decisions, task_id, root):
    """Validate explicit task and separate human approval. Return the task."""
    if project.get("schema") != "project-control-target-project/1":
        raise PublicationError("unsupported project schema")
    project_id = project.get("project_id")
    if not isinstance(project_id, str) or not IDENT.fullmatch(project_id):
        raise PublicationError("invalid project identity")
    if tasks.get("schema") != "project-control-target-tasks/1" or tasks.get("project_id") != project_id:
        raise PublicationError("invalid task registry for project")
    if (decisions.get("schema") != "project-control-target-decisions/1"
            or decisions.get("project_id") != project_id
            or not isinstance(decisions.get("decisions"), list)):
        raise PublicationError("invalid project decision registry")
    entries = tasks.get("tasks")
    if not isinstance(entries, list) or not all(isinstance(t, dict) for t in entries):
        raise PublicationError("invalid task list")
    matches = [t for t in entries if t.get("id") == task_id]
    if len(matches) != 1:
        raise PublicationError("expected exactly one named task")
    task = matches[0]
    if task.get("state") not in ("DRAFT", "AWAITING_AUTHORIZATION"):
        raise PublicationError("task must start as DRAFT or AWAITING_AUTHORIZATION")
    if task.get("project_id") != project_id or task.get("executor") != "local_codex":
        raise PublicationError("task does not belong to this project's Codex executor")
    if task.get("mode") not in ("EXPLORATORY", "CONTROLLED"):
        raise PublicationError("task kind must be explicit")
    source = task.get("source", {})
    if not isinstance(source, dict):
        raise PublicationError("missing Git source")
    if canonical_url(source.get("repo_url")) != canonical_url(project.get("repo_url")):
        raise PublicationError("task remote differs from registered project")
    if (not SHA40.fullmatch(str(source.get("pinned_sha", ""))) or
            not BRANCH.fullmatch(str(source.get("branch", ""))) or
            not BRANCH.fullmatch(str(source.get("work_branch", ""))) or
            source["branch"] == source["work_branch"] or
            source["work_branch"].lower() in ("main", "master")):
        raise PublicationError("invalid task branch/source pin")
    if canonical_url(read_git(root, "remote", "get-url", "origin")) != canonical_url(project["repo_url"]):
        raise PublicationError("current Git origin is not the task repository")
    if Path(read_git(root, "rev-parse", "--show-toplevel")).resolve() != root:
        raise PublicationError("must validate target project root, not another worktree")
    if read_git(root, "branch", "--show-current") != source["branch"]:
        raise PublicationError("wrong publishing branch")
    head = read_git(root, "rev-parse", "HEAD")
    # Approval decisions and task drafts are committed *after* the source pin.
    # Requiring HEAD == pinned_sha creates an impossible self-reference when
    # a human approval record itself includes that pinned_sha.
    try:
        ancestry = subprocess.run(
            ["git", "-C", str(root), "merge-base", "--is-ancestor", source["pinned_sha"], head],
            capture_output=True, timeout=15)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise PublicationError("cannot verify pinned Git history") from exc
    if ancestry.returncode:
        raise PublicationError("pinned source is not an ancestor of current approved branch")
    if read_git(root, "status", "--porcelain", "--untracked-files=all"):
        raise PublicationError("source worktree is dirty; review and commit before publishing")
    paths = task.get("allowed_paths")
    if not isinstance(paths, list) or not paths or len(paths) > 100:
        raise PublicationError("task requires bounded file scope")
    for path in paths:
        if not isinstance(path, str):
            raise PublicationError("invalid allowed path")
        if path.endswith("/**"):
            valid_repo_path(path[:-3])
        else:
            valid_repo_path(path)
        if path == FORBIDDEN[1] or path.startswith(FORBIDDEN[0]) or path in ("*", "**"):
            raise PublicationError("task attempts to rewrite governance")
    if len(set(paths)) != len(paths):
        raise PublicationError("duplicate file scope")
    if not isinstance(task.get("inputs"), list) or not task["inputs"]:
        raise PublicationError("task lacks research/design input")
    if not isinstance(task.get("acceptance"), list) or not task["acceptance"] or any(
        not isinstance(s, str) or not s.strip() for s in task["acceptance"]
    ):
        raise PublicationError("task lacks acceptance")
    if not DENIALS.issubset(set(task.get("restrictions", []))):
        raise PublicationError("task lacks mandatory safety rules")

    auth = task.get("authorization", {})
    if not isinstance(auth, dict) or not auth.get("approved_by") or not auth.get("approval_reference"):
        raise PublicationError("task lacks human authorization reference")
    matching = [d for d in decisions["decisions"] if isinstance(d, dict)
                and d.get("kind") == "TASK_EXECUTION_APPROVAL"
                and d.get("state") == "APPROVED"
                and d.get("task_id") == task_id
                and d.get("source_sha") == source["pinned_sha"]
                and d.get("approved_by") == auth["approved_by"]
                and d.get("approval_reference") == auth["approval_reference"]]
    if len(matching) != 1:
        raise PublicationError("no unique separately recorded task execution approval")
    if task["mode"] == "CONTROLLED":
        if project.get("formal_product_identity") != "APPROVED" or project.get("scope_decision") != "APPROVED":
            raise PublicationError("controlled implementation requires approved product identity and scope")
        proven = False
        for item in task["inputs"]:
            if not isinstance(item, dict) or item.get("type") != "DESIGN_BASELINE":
                continue
            path, blob = item.get("path"), item.get("blob_sha")
            valid_repo_path(path)
            if not SHA40.fullmatch(str(blob)):
                raise PublicationError("design input requires exact Git blob SHA")
            actual = read_git(root, "rev-parse", "--verify", source["pinned_sha"] + ":" + path, missing_ok=True)
            approved = [d for d in decisions["decisions"] if isinstance(d, dict)
                        and d.get("kind") == "DESIGN_APPROVAL" and d.get("state") == "APPROVED"
                        and d.get("path") == path and d.get("blob_sha") == blob
                        and d.get("approved_by") and d.get("approval_reference")]
            if actual == blob and len(approved) == 1:
                proven = True
        if not proven:
            raise PublicationError("controlled implementation lacks matching approved design blob")
    # A manager cannot publish several unrelated READY tasks and then tell
    # Codex to choose the unique task. Explicit IDs remain supported separately.
    other_ready = [t for t in entries if t.get("id") != task_id
                   and t.get("state") == READY and t.get("executor") == "local_codex"]
    if other_ready:
        raise PublicationError("other READY Codex tasks exist; use an explicit assignment strategy")
    return task


def make_plan(root, task_id):
    root = Path(root).resolve(strict=True)
    control = root / ".project-control"
    source_files = {}
    for name in ("project.json", "tasks.json", "decisions.json"):
        file = control / name
        if not file.is_file() or file.is_symlink():
            raise PublicationError("missing or symlinked management source file")
        source_files[name] = file.read_bytes()
    project, tasks, decisions = (json.loads(source_files[x]) for x in ("project.json", "tasks.json", "decisions.json"))
    task = validate_task(project, tasks, decisions, task_id, root)
    raw_blob = read_git(root, "hash-object", ".project-control/tasks.json")
    if not SHA40.fullmatch(raw_blob):
        raise PublicationError("cannot verify Git blob for original task registry")
    candidate = json.loads(source_files["tasks.json"])
    index = next(i for i,t in enumerate(candidate["tasks"]) if t["id"] == task_id)
    candidate["tasks"][index]["state"] = READY
    candidate["tasks"][index]["published_source_sha"] = task["source"]["pinned_sha"]
    content = json.dumps(candidate, ensure_ascii=False, indent=2) + "\n"
    return {
        "schema": "project-control-task-publication-plan/1",
        "project_id": project["project_id"], "task_id": task_id,
        "repo_url": project["repo_url"], "source_branch": task["source"]["branch"],
        "source_commit": task["source"]["pinned_sha"],
        "preflight_branch_head": read_git(root, "rev-parse", "HEAD"),
        "expected_tasks_blob_sha": raw_blob,
        "original_tasks_sha256": hashlib.sha256(source_files["tasks.json"]).hexdigest(),
        "desired_file": ".project-control/tasks.json",
        "proposed_content": content,
        "status": "AWAITING_REMOTE_CONTENTS_API_CAS",
        "note": "Read-only publication plan. Human approval reference must be authentic. Do not mark published until GitHub confirms a compare-and-swap update.",
    }


def main(argv=None):
    # Python on Windows may inherit a legacy charmap console. Every management
    # command writes UTF-8 Chinese project names and JSON, without requiring
    # users to change the system code page.
    for output in (sys.stdout, sys.stderr):
        if hasattr(output, "reconfigure"):
            output.reconfigure(encoding="utf-8", errors="replace")
    p = argparse.ArgumentParser(description="Preflight human-approved Codex task publication")
    p.add_argument("--target", type=Path, required=True)
    p.add_argument("--task-id", required=True)
    options = p.parse_args(argv)
    try:
        print(json.dumps(make_plan(options.target, options.task_id), ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as exc:
        print(json.dumps({"status": "BLOCKED", "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
