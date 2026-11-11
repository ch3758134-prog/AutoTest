"""Read candidate project-manager views exclusively from committed Git data.

The payload is a read-only review surface, never an approved engineering object
or a replacement for Studio's revisioned SQLite design records.
"""
from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess

from studio_store import RuleError

INDEX = "docs/engineering-control/projects/index.json"
PROJECT_PATH = re.compile(r"docs/engineering-control/projects/([a-z0-9][a-z0-9_-]{0,62})/management-view\\.json\\Z")
SHA = re.compile(r"[a-f0-9]{40}\\Z")


def _git(repo: Path, *args: str) -> str:
    try:
        run = subprocess.run(["git", "-C", str(repo), *args], capture_output=True,
                             text=True, encoding="utf-8", errors="replace",
                             timeout=8, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise RuleError("Cannot read Git worktree: " + str(exc)) from exc
    if run.returncode:
        raise RuleError("Cannot inspect committed Git project data: " +
                        (run.stderr.strip() or "git command failed")[:350])
    return run.stdout


def _committed_json(repo: Path, sha: str, path: str, missing_ok=False):
    try:
        raw = _git(repo, "show", sha + ":" + path)
    except RuleError as exc:
        if missing_ok and "does not exist" in str(exc):
            return None
        raise
    if len(raw.encode("utf-8")) > 250_000:
        raise RuleError("Git project management record is too large")
    try:
        return json.loads(raw)
    except (ValueError, TypeError) as exc:
        raise RuleError("Invalid committed Git management JSON: " + path) from exc


def current_views(repo) -> dict:
    root = Path(repo).expanduser().resolve()
    sha = _git(root, "rev-parse", "HEAD").strip()
    if not SHA.fullmatch(sha):
        raise RuleError("Git HEAD is not a valid committed SHA")
    index = _committed_json(root, sha, INDEX, missing_ok=True)
    result = {"schema": "engineering-manager-views/1", "git_commit": sha,
              "authority": "AI_CANDIDATE_NOT_APPROVED", "projects": [],
              "note": "Read from exact Git HEAD; not a SQLite approval or live remote GitHub sync."}
    if index is None:
        return result
    if not isinstance(index, dict) or index.get("schema") != "engineering-manager-view-index/1":
        raise RuleError("Unsupported committed management index schema")
    entries = index.get("projects")
    if not isinstance(entries, list) or len(entries) > 30:
        raise RuleError("Invalid committed management index entries")
    seen = set()
    for entry in entries:
        if not isinstance(entry, dict):
            raise RuleError("Invalid management project index item")
        slug, path = entry.get("id"), entry.get("path")
        if (not isinstance(path, str) or not isinstance(slug, str) or
                not PROJECT_PATH.fullmatch(path) or
                PROJECT_PATH.fullmatch(path).group(1) != slug or slug in seen):
            raise RuleError("Unsafe or duplicate committed management project path")
        seen.add(slug)
        view = _committed_json(root, sha, path)
        if (not isinstance(view, dict) or view.get("schema") != "engineering-manager-view/1"
                or view.get("project_id") != slug
                or view.get("status") != "AI_CANDIDATE_NOT_APPROVED"):
            raise RuleError("Management view must remain a project-specific AI candidate")
        for key in ("evidence", "judgments", "open_questions"):
            if not isinstance(view.get(key), list) or len(view[key]) > 100:
                raise RuleError("Invalid management view collection: " + key)
        result["projects"].append({**view, "git_path": path})
    return result
