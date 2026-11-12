"""Module ownership and independently inspected local source state.

This is deliberately NOT a project-plan task board: every module snapshot is
derived from the actual Git object database and worktree. Candidate review
and approval require separate real GitHub verification. Module definitions
are management proposals; they do not create OS or GitHub file permissions.
"""
from __future__ import annotations

import fnmatch
import hashlib
import re
import subprocess
from pathlib import Path

IDENTIFIER = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
ALLOWED_ROLES = ("specialist", "integration", "coordinator", "unassigned")
MAX_MODULES = 100


class ModuleError(ValueError):
    pass


def bounded(value, label, length=300):
    if not isinstance(value, str) or not value.strip() or len(value) > length:
        raise ModuleError("invalid " + label)
    return value.strip()


def path_prefix(raw):
    if (not isinstance(raw, str) or not raw or len(raw) > 260
            or "\\" in raw or ":" in raw or raw.startswith("/")
            or not raw.endswith("/") or "//" in raw
            or any(part in ("", ".", "..") for part in raw[:-1].split("/"))):
        raise ModuleError("module ownership requires safe relative directory prefixes ending in /")
    if raw.startswith((".git/", ".project-control/")) or raw.startswith("tools/project-control-v2/"):
        raise ModuleError("project metadata/tooling cannot be owned as implementation module")
    return raw


def defined_modules(record):
    modules = record.get("modules", [])
    if not isinstance(modules, list) or len(modules) > MAX_MODULES:
        raise ModuleError("invalid module registry")
    ids, claims = set(), []
    for item in modules:
        if not isinstance(item, dict):
            raise ModuleError("invalid module entry")
        mid = item.get("id")
        if not isinstance(mid, str) or not IDENTIFIER.fullmatch(mid) or mid in ids:
            raise ModuleError("duplicated or invalid module ID")
        ids.add(mid)
        prefixes = item.get("owned_prefixes")
        if not isinstance(prefixes, list) or not prefixes or len(prefixes) > 30:
            raise ModuleError("module needs 1-30 owned directories")
        for prefix in prefixes:
            path_prefix(prefix)
            for previous, owner in claims:
                if prefix.startswith(previous) or previous.startswith(prefix):
                    raise ModuleError("overlapping module ownership: " + owner + " and " + mid)
            claims.append((prefix, mid))
        if not isinstance(item.get("contracts"), list):
            raise ModuleError("module contract references must be a list")
        for path in item["contracts"]:
            if (not isinstance(path, str) or not path or path.startswith("/")
                    or "\\" in path or ":" in path or ".." in path.split("/")
                    or not any(path.startswith(prefix) for prefix in prefixes)):
                raise ModuleError("public interface path must belong to the module")
    for item in modules:
        deps = item.get("depends_on", [])
        if (not isinstance(deps, list) or len(deps) != len(set(deps))
                or item["id"] in deps or any(x not in ids for x in deps)):
            raise ModuleError("invalid module dependency")
    graph = {m["id"]: m.get("depends_on", []) for m in modules}
    seen, stack = set(), set()
    def visit(mid):
        if mid in stack:
            raise ModuleError("module dependency cycle")
        if mid in seen:
            return
        stack.add(mid)
        for dep in graph[mid]:
            visit(dep)
        stack.remove(mid)
        seen.add(mid)
    for module_id in graph:
        visit(module_id)
    return modules


def add_module(record, payload):
    modules = record.setdefault("modules", [])
    if not isinstance(modules, list) or len(modules) >= MAX_MODULES:
        raise ModuleError("module registry full or invalid")
    mid = bounded(payload.get("id"), "module ID", 100)
    if not IDENTIFIER.fullmatch(mid):
        raise ModuleError("invalid module ID")
    if any(x.get("id") == mid for x in modules):
        raise ModuleError("module already registered")
    prefixes = payload.get("owned_prefixes")
    if not isinstance(prefixes, list) or not prefixes or len(prefixes) > 30:
        raise ModuleError("module must have explicit owned path prefixes")
    normalized = [path_prefix(x) for x in prefixes]
    if len(set(normalized)) != len(normalized):
        raise ModuleError("duplicated module path claim")
    role = payload.get("owner_role", "unassigned")
    if role not in ALLOWED_ROLES:
        raise ModuleError("unknown module owner role")
    contracts = payload.get("contracts", [])
    if not isinstance(contracts, list):
        raise ModuleError("invalid module interface files")
    deps = payload.get("depends_on", [])
    if not isinstance(deps, list):
        raise ModuleError("invalid module dependencies")
    entry = {
        "id": mid, "title": bounded(payload.get("title"), "module title", 240),
        "responsibility": bounded(payload.get("responsibility"), "module responsibility", 1500),
        "owner_role": role, "owned_prefixes": normalized,
        "contracts": contracts, "depends_on": deps,
        "boundary_state": "PROPOSED_NOT_APPROVED",
        "release_state": "NOT_ASSESSED",
        "formal_approval": False,
    }
    modules.append(entry)
    try:
        defined_modules(record)
    except Exception:
        modules.pop()
        raise
    return entry


def assign_work(record, payload):
    mid = bounded(payload.get("module_id"), "module ID", 100)
    wid = bounded(payload.get("work_id"), "work ID", 100)
    modules = defined_modules(record)
    if not any(x["id"] == mid for x in modules):
        raise ModuleError("module is not registered")
    matches = [x for x in record["work"] if x.get("id") == wid]
    if len(matches) != 1:
        raise ModuleError("work item not found uniquely")
    if matches[0].get("module_id") not in (None, mid):
        raise ModuleError("work is already assigned elsewhere; use a formally reviewed change")
    matches[0]["module_id"] = mid
    return matches[0]


def git(root, *args, nul=False):
    try:
        proc = subprocess.run(["git", "-C", str(root), *args],
                              capture_output=True, timeout=15)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ModuleError("Git inspection unavailable") from error
    if proc.returncode:
        raise ModuleError("Git inspection failed; no module state can be inferred")
    return proc.stdout if nul else proc.stdout.decode("utf-8", "replace").strip()


def local_source_snapshot(root, record):
    """Compare module boundaries to an actual local HEAD; never claim remote truth.

    Includes ignored unknowns only to mark dirty state, not to expose file
    contents or silently discard them. All Git commands are read only.
    """
    modules = defined_modules(record)
    root = Path(root).resolve(strict=True)
    head = git(root, "rev-parse", "--verify", "HEAD")
    branch = git(root, "branch", "--show-current") or "(detached)"
    tracked_raw = git(root, "ls-tree", "-r", "--name-only", "-z", "HEAD", nul=True)
    tracked = [x.decode("utf-8", "replace") for x in tracked_raw.split(b"\0") if x]
    status_raw = git(root, "status", "--porcelain=v1", "-z", "--untracked-files=all", nul=True)
    tokens = [x.decode("utf-8", "replace") for x in status_raw.split(b"\0") if x]
    changed, index = [], 0
    while index < len(tokens):
        entry = tokens[index]
        if len(entry) < 4 or entry[2] != " ":
            raise ModuleError("unrecognizable Git worktree status")
        code = entry[:2]
        changed.append(entry[3:])
        if "R" in code or "C" in code:
            index += 1
            if index >= len(tokens):
                raise ModuleError("incomplete Git rename status")
            changed.append(tokens[index])
        index += 1
    result = []
    claimed = []
    for module in modules:
        prefixes = tuple(module["owned_prefixes"])
        claimed.extend(prefixes)
        files = sorted(x for x in tracked if x.startswith(prefixes))
        pending = sorted(set(x for x in changed if x.startswith(prefixes)))
        # A module last changed SHA may predate the local checkout HEAD. No
        # backend may use it as evidence of a remote integration or acceptance.
        last = git(root, "log", "-1", "--format=%H", "HEAD", "--", *prefixes) if files else ""
        contracts = module.get("contracts", [])
        touched_contracts = [x for x in contracts if x in pending]
        result.append({
            "id": module["id"], "module_id": module["id"],
            "owner_role": module["owner_role"], "owned_prefixes": list(prefixes),
            "tracked_file_count": len(files),
            "last_changed_commit": last or None,
            "working_changes": pending[:100], "working_change_count": len(pending),
            "working_tree": "UNCOMMITTED_MODIFICATIONS" if pending else "NO_OBSERVED_CHANGES",
            "contract_changes": touched_contracts,
            "source_status": "LOCAL_GIT_OBJECTS_ONLY_NOT_REMOTE_VERIFIED",
            "release_status": "NOT_VERIFIED",
            "integration_status": "UNKNOWN_NOT_REMOTE_VERIFIED",
        })
    unowned = sorted(set(x for x in changed if not x.startswith(tuple(claimed))))
    return {
        "git_head": head, "branch": branch,
        "source": "LOCAL_GIT_OBJECTS_ONLY_NOT_REMOTE_VERIFIED",
        "registered_modules": result, "unowned_working_changes": unowned[:200],
        "unowned_working_change_count": len(unowned),
        "note": "Actual local Git object evidence. Do not infer PR review, CI, merge or Gate PASS.",
    }


def assess_candidate_paths(record, changed_paths, task_module_id):
    """Detect cross-module edits for an actual independently sourced Git diff.

    Does not itself prove that a supplied list is a real GitHub PR file list.
    """
    modules = defined_modules(record)
    if not isinstance(changed_paths, list) or not changed_paths:
        raise ModuleError("candidate file diff is missing")
    if task_module_id is not None and not any(x["id"] == task_module_id for x in modules):
        raise ModuleError("candidate task module is not registered")
    owned_by = {}
    shared = []
    foreign = []
    interface_changes = []
    for path in changed_paths:
        if (not isinstance(path, str) or not path or "\\" in path or ":" in path
                or path.startswith("/") or any(x in ("", ".", "..") for x in path.split("/"))):
            raise ModuleError("unsafe candidate path")
        owner = next((m for m in modules
                      if any(path.startswith(p) for p in m["owned_prefixes"])), None)
        if not owner:
            foreign.append(path)
            continue
        owned_by.setdefault(owner["id"], []).append(path)
        if path in owner.get("contracts", []):
            interface_changes.append(path)
        if task_module_id and owner["id"] != task_module_id:
            shared.append(path)
    return {
        "source": "PROVIDED_DIFF_REQUIRES_GIT_OR_GITHUB_VERIFICATION",
        "task_module_id": task_module_id,
        "affected_module_ids": sorted(owned_by),
        "touched_paths_by_module": owned_by,
        "cross_module_paths": shared, "unowned_paths": foreign,
        "interface_changes": interface_changes,
        "result": ("CROSS_MODULE_OR_UNOWNED_REVIEW_REQUIRED"
                   if shared or foreign or interface_changes else "MODULE_SCOPE_MATCHES_DECLARED_BOUNDARIES"),
        "auto_approved": False,
    }
