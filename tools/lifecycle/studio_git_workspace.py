"""Hydrate an isolated trial workspace from the exact Git engineering archive.

Never alters an existing project/database. All engineering objects use the same
Store and revision tables as the normal HTML application. The Git snapshot is
a candidate source, not a grant of approval or implementation authority.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import subprocess

from studio_store import RuleError, Store
from studio_restore import preview, restore_to_empty

PROJECT_ID = re.compile(r"[a-z0-9][a-z0-9_-]{0,62}\Z")
GIT_SHA = re.compile(r"[0-9a-f]{40,64}\Z")
MAX_BYTES = 8_000_000


def _git(root: Path, *args: str) -> bytes:
    try:
        result = subprocess.run(
            ["git", "-C", str(root), *args],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            timeout=12, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise RuleError("Cannot read Git worktree: " + str(exc)) from exc
    if result.returncode:
        raise RuleError("Committed engineering archive is unavailable: " +
                        result.stderr.decode("utf-8", errors="replace")[:280])
    return result.stdout


def committed_project(root, project_id="autotest"):
    root = Path(root).expanduser().resolve()
    if not PROJECT_ID.fullmatch(project_id):
        raise RuleError("Invalid Git engineering project identity")
    commit = _git(root, "rev-parse", "HEAD").decode("ascii", errors="replace").strip()
    if not GIT_SHA.fullmatch(commit):
        raise RuleError("Git checkout HEAD is not a full commit ID")
    path = "docs/engineering-control/projects/" + project_id + "/snapshot.json"
    raw = _git(root, "show", commit + ":" + path)
    if len(raw) > MAX_BYTES:
        raise RuleError("Committed engineering archive exceeds limit")
    try:
        wrapper = json.loads(raw.decode("utf-8"))
    except (ValueError, UnicodeDecodeError) as exc:
        raise RuleError("Invalid Git engineering archive encoding or JSON") from exc
    if (not isinstance(wrapper, dict) or
            wrapper.get("schema") != "engineering-project-archive/1" or
            wrapper.get("source_project_id") != project_id or
            wrapper.get("authority") != "AI_CANDIDATE_NOT_APPROVED"):
        raise RuleError("Git engineering candidate header does not match project")
    archive = wrapper.get("engineering")
    result = preview(archive)
    if result["project_id"] != project_id:
        raise RuleError("Git archive and restored project identity differ")
    if any(o.get("state") in ("APPROVED", "SUPERSEDED")
           for o in archive.get("objects", [])):
        raise RuleError("The Git AI candidate may not fabricate approved objects")
    if (archive.get("product_identity", {}).get("baselines") or
            archive.get("governance", {}).get("baselines")):
        raise RuleError("The Git AI candidate may not issue product approvals")
    return {
        "commit": commit, "path": path,
        "content_sha256": hashlib.sha256(raw).hexdigest(),
        "project_id": project_id, "preview": result, "archive": archive,
        "authority": "AI_CANDIDATE_NOT_APPROVED",
    }


def hydrate_empty(root, database, project_id="autotest"):
    snapshot = committed_project(root, project_id)
    store = Store(database)
    if store.projects():
        raise RuleError("Git checkout hydration is forbidden for a nonempty workspace; "
                        "use a fresh isolated trial database, never overwrite prior edits")
    restored = restore_to_empty(snapshot["archive"], database)
    return {k: v for k, v in snapshot.items() if k != "archive"} | {
        "restored_project_id": restored["project"]["id"]}
