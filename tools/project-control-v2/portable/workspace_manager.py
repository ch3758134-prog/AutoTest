#!/usr/bin/env python3
"""Local Project Control workspace ownership and retirement.

The user chooses the working directory. A single per-user registry stays
OUTSIDE repositories and temporary worktrees (overridable with --registry-dir).
No workspace is deleted merely because a task is complete. No force/reset,
prune, recursive filesystem cleanup, termination of processes or auto-push.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys

SCHEMA = "project-control-local-workspaces/1"
IDENT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
HASH = re.compile(r"^[0-9a-f]{40}$")
BRANCH = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,180}$")
WORKSPACE_TYPES = {"PERMANENT", "TASK", "TEMP"}
REPO_PATTERN = re.compile(r"^https://github[.]com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:[.]git)?/?$", re.I)


class WorkspaceError(ValueError):
    pass


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def registry_location(location=None):
    candidate = location or os.environ.get("PROJECT_CONTROL_REGISTRY_HOME")
    root = Path(candidate).expanduser() if candidate else Path.home() / ".project-control-local"
    return root.absolute()


def registry_path(location=None):
    return registry_location(location) / "workspaces.json"


def canonical(value):
    if not isinstance(value, str) or not REPO_PATTERN.fullmatch(value):
        raise WorkspaceError("remote URL must be explicit https://github.com/owner/repo.git")
    match = REPO_PATTERN.fullmatch(value)
    return ("https://github.com/" + match.group(1) + "/" + match.group(2) + ".git").lower()


def assert_id(value, label):
    if not isinstance(value, str) or not IDENT.fullmatch(value):
        raise WorkspaceError("invalid " + label)
    return value


def run_git(root, *args, missing=False, timeout=25):
    try:
        result = subprocess.run(["git", "-C", str(root), *args], capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=timeout)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise WorkspaceError("Git unavailable or timed out") from exc
    if result.returncode:
        if missing:
            return None
        raise WorkspaceError("Git failed (" + " ".join(args[:3]) + "): " + result.stderr.strip()[:200])
    return result.stdout.strip()


def ensure_path_inside(path, allowed_root):
    root = Path(allowed_root).expanduser().resolve(strict=True)
    if not root.is_dir() or root.is_symlink():
        raise WorkspaceError("authorized workspace parent is unsafe")
    raw = Path(path).expanduser().absolute()
    if raw == root or not raw.is_relative_to(root):
        raise WorkspaceError("workspace must be a child of explicitly authorized root")
    # Resolve each ancestor to reject symlink/junction escape; Windows junctions
    # are normally resolved by Path.resolve().
    destination = raw.resolve(strict=True)
    if destination == root or not destination.is_relative_to(root):
        raise WorkspaceError("workspace escapes authorized root")
    if not destination.is_dir() or raw.is_symlink():
        raise WorkspaceError("workspace is not a safe real directory")
    return destination, root


def ensure_empty(path):
    if any(path.iterdir()):
        raise WorkspaceError("selected workspace folder is not empty; never overwrite it")


def read_registry(location=None):
    path = registry_path(location)
    if not path.exists():
        return {"schema": SCHEMA, "revision": 0, "workspaces": [], "events": []}
    if path.is_symlink() or not path.is_file():
        raise WorkspaceError("unsafe registry file")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise WorkspaceError("unreadable registry; do not overwrite") from exc
    if (value.get("schema") != SCHEMA or type(value.get("revision")) is not int
            or not isinstance(value.get("workspaces"), list)
            or not isinstance(value.get("events"), list)):
        raise WorkspaceError("invalid registry schema")
    ids = [x.get("workspace_id") for x in value["workspaces"] if isinstance(x, dict)]
    if len(ids) != len(value["workspaces"]) or len(ids) != len(set(ids)):
        raise WorkspaceError("registry contains duplicate or malformed records")
    return value


@contextmanager
def registry_lock(location=None):
    root = registry_location(location)
    root.mkdir(parents=True, exist_ok=True)
    if root.is_symlink():
        raise WorkspaceError("unsafe registry directory")
    path = root / "write.lock"
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError as exc:
        raise WorkspaceError("registry held by another process; do not overwrite lock") from exc
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            stream.write("pid=" + str(os.getpid()) + " at=" + now() + "\n")
        yield
    finally:
        path.unlink(missing_ok=True)


def persist(value, location=None):
    path = registry_path(location)
    raw = json.dumps(value, ensure_ascii=False, indent=2).encode("utf-8") + b"\n"
    tmp = path.with_name(".registry." + secrets.token_hex(8) + ".tmp")
    try:
        with tmp.open("xb") as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(tmp, path)
    finally:
        tmp.unlink(missing_ok=True)


def commit_event(value, action, workspace_id, note):
    value["revision"] += 1
    value["events"].append({"revision": value["revision"], "at": now(),
                            "action": action, "workspace_id": workspace_id, "note": note})
    if len(value["events"]) > 20000:
        raise WorkspaceError("registry event log requires archiving")


def find(value, workspace_id):
    matches = [x for x in value["workspaces"] if x["workspace_id"] == workspace_id]
    if len(matches) != 1:
        raise WorkspaceError("workspace ID is not registered exactly once")
    return matches[0]


def validate_registration(registry, workspace_id, path):
    assert_id(workspace_id, "workspace ID")
    if any(x["workspace_id"] == workspace_id for x in registry["workspaces"]):
        raise WorkspaceError("workspace ID already exists")
    if any(Path(x["canonical_path"]) == path for x in registry["workspaces"]):
        raise WorkspaceError("workspace path already registered, even if archived")


def make_record(workspace_id, project_id, task_id, kind, purpose, path, root,
                repo_url, source_ref, pinned_sha, relation, parent_repo=None):
    assert_id(workspace_id, "workspace ID")
    assert_id(project_id, "project ID")
    if task_id:
        assert_id(task_id, "task ID")
    if kind not in WORKSPACE_TYPES:
        raise WorkspaceError("invalid workspace classification")
    if kind in ("TASK", "TEMP") and not task_id:
        raise WorkspaceError("task-scoped workspaces require a task ID")
    if not isinstance(purpose, str) or not purpose.strip() or len(purpose) > 400:
        raise WorkspaceError("human-readable purpose required")
    if not isinstance(source_ref, str) or not BRANCH.fullmatch(source_ref):
        raise WorkspaceError("explicit source branch required")
    if not HASH.fullmatch(str(pinned_sha)):
        raise WorkspaceError("exact source commit required")
    return {
        "workspace_id": workspace_id, "project_id": project_id,
        "task_id": task_id, "type": kind, "purpose": purpose.strip(),
        "canonical_path": str(path), "allowed_root": str(root),
        "repo_url": canonical(repo_url), "source_ref": source_ref,
        "pinned_sha": pinned_sha, "relation": relation,
        "parent_repo": str(parent_repo) if parent_repo else None,
        "created_at": now(), "state": "CREATING",
        "disposition": "KEEP_UNTIL_REVIEWED", "last_checked_at": None,
        "last_check": None, "log_refs": [], "receipt_ids": [],
        "cleanup_policy": "NO_AUTOMATIC_DELETE",
    }


def create(workspace_id, project_id, task_id, kind, purpose, path, allowed_root,
           repo_url, source_ref, pinned_sha, parent_repo=None, location=None, apply=False):
    """Only create within the explicitly selected empty folder.

    Linked TEMP uses git worktree add --detach on an explicit existing parent.
    Other kinds clone the specified GitHub repository into the selected folder.
    """
    target, root = ensure_path_inside(path, allowed_root)
    ensure_empty(target)
    register_at = registry_location(location)
    if register_at == target or register_at.is_relative_to(target):
        raise WorkspaceError("registry must live outside the selected workspace")
    relation = "LINKED_WORKTREE" if parent_repo else "INDEPENDENT_CLONE"
    if relation == "LINKED_WORKTREE" and kind != "TEMP":
        raise WorkspaceError("linked worktrees in this interface are TEMP only")
    if kind == "TEMP" and not parent_repo:
        raise WorkspaceError("TEMP must be a linked worktree; independent clones are never automatically removed")
    parent = None
    if parent_repo:
        parent = Path(parent_repo).expanduser().resolve(strict=True)
        if not parent.is_dir() or run_git(parent, "rev-parse", "--show-toplevel") != str(parent):
            raise WorkspaceError("invalid parent Git repository")
        if canonical(run_git(parent, "remote", "get-url", "origin")) != canonical(repo_url):
            raise WorkspaceError("parent Git origin mismatches requested repository")
        if run_git(parent, "cat-file", "-t", pinned_sha, missing=True) != "commit":
            raise WorkspaceError("pinned source commit not present in parent repository")
    result = make_record(workspace_id, project_id, task_id, kind, purpose,
                         target, root, repo_url, source_ref, pinned_sha, relation, parent)
    if not apply:
        return {"status": "PREVIEW_ONLY", "workspace": result,
                "will_modify": str(target), "registry": str(registry_path(location))}
    with registry_lock(location):
        data = read_registry(location)
        validate_registration(data, workspace_id, target)
        ensure_empty(target)
        data["workspaces"].append(result)
        commit_event(data, "CREATION_STARTED", workspace_id, "user-selected empty directory")
        persist(data, location)
    try:
        if parent:
            # git worktree add can create into existing empty directory.
            run_git(parent, "worktree", "add", "--detach", str(target), pinned_sha, timeout=80)
        else:
            try:
                p = subprocess.run(["git", "clone", "--single-branch", "--branch", source_ref,
                                    canonical(repo_url), str(target)], text=True,
                                   capture_output=True, timeout=150)
            except (OSError, subprocess.TimeoutExpired) as exc:
                raise WorkspaceError("git clone failed or timed out; preserve partial folder") from exc
            if p.returncode:
                raise WorkspaceError("git clone failed; preserve partial folder: " + p.stderr.strip()[:200])
        if canonical(run_git(target, "remote", "get-url", "origin")) != canonical(repo_url):
            raise WorkspaceError("resulting origin does not match registration")
        head = run_git(target, "rev-parse", "--verify", "HEAD")
        if parent and head != pinned_sha:
            raise WorkspaceError("temporary worktree is not pinned exactly")
        if run_git(target, "merge-base", pinned_sha, "HEAD", missing=True) != pinned_sha:
            raise WorkspaceError("resulting HEAD does not descend from task pin")
        with registry_lock(location):
            data = read_registry(location)
            item = find(data, workspace_id)
            if item["state"] != "CREATING":
                raise WorkspaceError("concurrent workspace state change")
            item["state"] = "ACTIVE"
            item["creation_head"] = head
            item["git_common_dir"] = run_git(target, "rev-parse", "--git-common-dir")
            commit_event(data, "WORKSPACE_CREATED", workspace_id, "verified origin/source")
            persist(data, location)
        return {"status": "ACTIVE_REGISTERED", "workspace_id": workspace_id,
                "canonical_path": str(target), "head": head, "relation": relation}
    except Exception as error:
        with registry_lock(location):
            data = read_registry(location)
            item = find(data, workspace_id)
            item["state"] = "CREATION_FAILED_KEEP_FOR_REVIEW"
            item["error"] = str(error)[:300]
            commit_event(data, "CREATION_FAILED", workspace_id, "partial content kept for review")
            persist(data, location)
        raise


def register_existing(workspace_id, project_id, task_id, kind, purpose,
                      path, allowed_root, repo_url, source_ref, pinned_sha,
                      location=None, apply=False):
    """Adopt a Git checkout created by Codex in the USER-selected directory.

    This is intentionally different from create(): no new clone, checkout,
    worktree or file modifications. Crucial for first-time Codex bootstrap,
    because workspace_manager.py is itself obtained from the target repo.
    """
    if kind == "TEMP":
        raise WorkspaceError("existing checkouts cannot silently become disposable TEMP")
    target, root = ensure_path_inside(path, allowed_root)
    registry_home = registry_location(location)
    if registry_home == target or registry_home.is_relative_to(target):
        raise WorkspaceError("local registry must be outside the existing checkout")
    if str(target) != run_git(target, "rev-parse", "--show-toplevel"):
        raise WorkspaceError("existing folder is not its Git repository root")
    if canonical(run_git(target, "remote", "get-url", "origin")) != canonical(repo_url):
        raise WorkspaceError("existing checkout belongs to another remote")
    head = run_git(target, "rev-parse", "--verify", "HEAD")
    if run_git(target, "branch", "--show-current") != source_ref:
        raise WorkspaceError("existing checkout on wrong source branch")
    if run_git(target, "merge-base", pinned_sha, head, missing=True) != pinned_sha:
        raise WorkspaceError("existing checkout does not descend from task pin")
    record = make_record(workspace_id, project_id, task_id, kind, purpose, target,
                         root, repo_url, source_ref, pinned_sha, "EXISTING_CHECKOUT_RETAIN")
    record["state"] = "ACTIVE"
    record["creation_head"] = head
    record["git_common_dir"] = run_git(target, "rev-parse", "--git-common-dir")
    record["disposition"] = "PRESERVE_EXISTING_UNTIL_HUMAN_REVIEW"
    record["cleanup_policy"] = "NEVER_AUTO_DELETE_EXISTING"
    record["initial_dirty"] = bool(run_git(target, "status", "--porcelain", "--untracked-files=all"))
    if not apply:
        return {"status": "REGISTRATION_PREVIEW_ONLY", "workspace": record,
                "note": "Never changes the existing project checkout."}
    with registry_lock(location):
        data = read_registry(location)
        validate_registration(data, workspace_id, target)
        data["workspaces"].append(record)
        commit_event(data, "EXISTING_WORKSPACE_REGISTERED", workspace_id,
                     "read-only verified identity, no files altered")
        persist(data, location)
    return {"status": "ACTIVE_EXISTING_REGISTERED", "workspace_id": workspace_id,
            "canonical_path": str(target), "initial_dirty": record["initial_dirty"]}


def inspect_one(record):
    """Read-only classification; do not infer a workspace is safe to remove."""
    root = Path(record["canonical_path"])
    observed = {"workspace_id": record["workspace_id"], "path": str(root),
                "registered_state": record["state"], "exists": root.exists(),
                "cleanup_eligible": False, "issues": [], "process_state": "NOT_VERIFIED"}
    if not root.is_dir() or root.is_symlink():
        observed["issues"].append("MISSING_OR_UNSAFE_PATH")
        return observed
    if str(root.resolve()) != str(root):
        observed["issues"].append("PATH_REALLOCATION")
        return observed
    try:
        if canonical(run_git(root, "remote", "get-url", "origin")) != record["repo_url"]:
            observed["issues"].append("WRONG_REPOSITORY")
        status = run_git(root, "status", "--porcelain=v1", "--untracked-files=all",
                         "--ignored=matching")
        observed["status_dirty_or_ignored"] = bool(status)
        if status:
            observed["issues"].append("TRACKED_UNTRACKED_OR_IGNORED_FILES")
        current = run_git(root, "rev-parse", "HEAD")
        observed["head"] = current
        if run_git(root, "merge-base", record["pinned_sha"], current, missing=True) != record["pinned_sha"]:
            observed["issues"].append("SOURCE_LINEAGE_MISMATCH")
        if current != record["pinned_sha"]:
            observed["issues"].append("HEAD_CHANGED_OR_UNREVIEWED_COMMITS")
        stashes = run_git(root, "stash", "list")
        observed["stash_present"] = bool(stashes)
        if stashes:
            observed["issues"].append("STASH_REVIEW_REQUIRED")
        observed["git_common_dir"] = run_git(root, "rev-parse", "--git-common-dir")
        if record["relation"] == "LINKED_WORKTREE":
            parent = record.get("parent_repo")
            if not parent or not Path(parent).is_dir():
                observed["issues"].append("PARENT_REPOSITORY_MISSING")
            else:
                active = run_git(parent, "worktree", "list", "--porcelain")
                if ("worktree " + str(root)) not in active.splitlines():
                    observed["issues"].append("GIT_WORKTREE_METADATA_MISMATCH")
    except WorkspaceError as exc:
        observed["issues"].append("GIT_INSPECTION_FAILED: " + str(exc))
    observed["retirement_proposal"] = (
        "HOLD_FOR_REVIEW" if observed["issues"] or record["type"] != "TEMP"
        else "CANDIDATE_AFTER_EXPLICIT_HUMAN_AND_PROCESS_CHECK")
    stable = {k:v for k,v in observed.items() if k not in ("process_state", "retirement_proposal")}
    observed["inspection_digest"] = hashlib.sha256(
        json.dumps(stable, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    return observed


def status(workspace_id=None, location=None):
    reg = read_registry(location)
    objects = [find(reg, workspace_id)] if workspace_id else reg["workspaces"]
    return {"revision": reg["revision"], "registered": len(reg["workspaces"]),
            "workspaces": [inspect_one(item) for item in objects]}


def scan(allowed_root, location=None):
    root = Path(allowed_root).expanduser().resolve(strict=True)
    reg = read_registry(location)
    known = {w["canonical_path"] for w in reg["workspaces"]}
    unknown = []
    for child in sorted(root.iterdir(), key=lambda p:p.name):
        if not child.is_dir() or child.is_symlink():
            continue
        if str(child.resolve()) not in known and (child / ".git").exists():
            unknown.append({"path": str(child), "status": "UNREGISTERED_HOLD_FOR_REVIEW"})
    return {"root": str(root), "unregistered": unknown, "note": "No automatic adoption or removal."}


def close(workspace_id, digest, reviewed_by, idle_confirmed, location=None, apply=False):
    reg = read_registry(location)
    item = find(reg, workspace_id)
    check = inspect_one(item)
    eligible = (item["state"] == "ACTIVE" and item["type"] == "TEMP"
                and item["relation"] == "LINKED_WORKTREE"
                and not check["issues"]
                and check["retirement_proposal"] == "CANDIDATE_AFTER_EXPLICIT_HUMAN_AND_PROCESS_CHECK")
    preview = {"status": "CANDIDATE_FOR_REVIEW" if eligible else "HOLD_FOR_REVIEW",
               "workspace_id": workspace_id, "inspection": check,
               "required": "fresh digest, explicit human approval and confirmation no process is using it"}
    if not apply:
        return preview
    if not eligible:
        raise WorkspaceError("workspace cannot be retired safely; preserve for review")
    if not isinstance(reviewed_by, str) or not reviewed_by.strip():
        raise WorkspaceError("explicit reviewer required")
    if idle_confirmed is not True:
        raise WorkspaceError("reviewer must confirm no active process; tool cannot prove this")
    if digest != check["inspection_digest"]:
        raise WorkspaceError("stale or incorrect inspection digest")
    with registry_lock(location):
        data = read_registry(location)
        item = find(data, workspace_id)
        fresh = inspect_one(item)
        if (fresh["inspection_digest"] != digest or item["state"] != "ACTIVE"
                or fresh["issues"]):
            raise WorkspaceError("workspace changed since close preview")
        parent = Path(item["parent_repo"])
        # Never use --force, prune, shutil.rmtree or reset. Git will refuse
        # removal of dirty worktrees. Retention is the default.
        run_git(parent, "worktree", "remove", str(Path(item["canonical_path"])), timeout=50)
        item["state"] = "RETIRED"
        item["retired_at"] = now()
        item["retired_by"] = reviewed_by.strip()
        item["os_process_check"] = "HUMAN_ATTESTED_NOT_AUTOMATICALLY_VERIFIED"
        commit_event(data, "TEMP_WORKTREE_RETIRED", workspace_id, "git worktree remove without force")
        persist(data, location)
    return {"status": "RETIRED", "workspace_id": workspace_id,
            "history_preserved": True, "note": "Git worktree removed; registry retained."}


def archive(workspace_id, reason, location=None, apply=False):
    if not isinstance(reason, str) or not reason.strip():
        raise WorkspaceError("archival decision needs a reason")
    record = find(read_registry(location), workspace_id)
    if not apply:
        return {"status": "PREVIEW_ONLY", "workspace_id": workspace_id,
                "action": "MARK_RETAINED", "path": record["canonical_path"]}
    with registry_lock(location):
        data = read_registry(location)
        item = find(data, workspace_id)
        if item["state"] not in ("ACTIVE", "CREATION_FAILED_KEEP_FOR_REVIEW"):
            raise WorkspaceError("cannot archive this workspace state")
        item["state"] = "ARCHIVED_RETAINED"
        item["archive_reason"] = reason
        commit_event(data, "ARCHIVED_RETAINED", workspace_id, reason)
        persist(data, location)
    return {"status": "ARCHIVED_RETAINED", "workspace_id": workspace_id,
            "deleted_files": False}


def cli(argv=None):
    p = argparse.ArgumentParser(description="Registered Codex Git workspaces; safe retirement")
    p.add_argument("action", choices=["create", "register-existing", "inspect", "scan", "close", "archive"])
    p.add_argument("--registry-dir")
    p.add_argument("--workspace-id")
    p.add_argument("--project-id")
    p.add_argument("--task-id")
    p.add_argument("--type", choices=sorted(WORKSPACE_TYPES), default="TASK")
    p.add_argument("--purpose")
    p.add_argument("--workspace")
    p.add_argument("--allowed-root")
    p.add_argument("--repo-url")
    p.add_argument("--source-ref")
    p.add_argument("--pinned-sha")
    p.add_argument("--parent-repo")
    p.add_argument("--inspection-digest")
    p.add_argument("--reviewed-by")
    p.add_argument("--idle-confirmed", action="store_true")
    p.add_argument("--reason")
    p.add_argument("--apply", action="store_true")
    args = p.parse_args(argv)
    try:
        if args.action == "create":
            result = create(args.workspace_id, args.project_id, args.task_id, args.type,
                            args.purpose, args.workspace, args.allowed_root, args.repo_url,
                            args.source_ref, args.pinned_sha, args.parent_repo,
                            args.registry_dir, args.apply)
        elif args.action == "register-existing":
            result = register_existing(args.workspace_id, args.project_id, args.task_id,
                                       args.type, args.purpose, args.workspace, args.allowed_root,
                                       args.repo_url, args.source_ref, args.pinned_sha,
                                       args.registry_dir, args.apply)
        elif args.action == "scan":
            result = scan(args.allowed_root, args.registry_dir)
        elif args.action == "inspect":
            result = status(args.workspace_id, args.registry_dir)
        elif args.action == "archive":
            result = archive(args.workspace_id, args.reason, args.registry_dir, args.apply)
        else:
            result = close(args.workspace_id, args.inspection_digest, args.reviewed_by,
                           args.idle_confirmed, args.registry_dir, args.apply)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except (WorkspaceError, OSError, TypeError, ValueError) as exc:
        print(json.dumps({"status": "BLOCKED", "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(cli())
