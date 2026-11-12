"""Read-only local/remote Git version reconciliation for registered projects.

GitHub synchronization is never inferred from local HEAD or previous fetch.
Explicit remote check uses ls-remote only: no fetch, push, pull or checkout.
"""
from __future__ import annotations

from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
from project_init import canonical_github_repo, InitError

HEX = re.compile(r"^[0-9a-f]{40}$")
BRANCH = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,240}$")


class SyncError(ValueError):
    pass


def call(root, args, timeout=15, allow_missing=False):
    try:
        result = subprocess.run(["git", "-C", str(root), *args], capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=timeout)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise SyncError("Git operation unavailable or timed out") from exc
    if result.returncode:
        if allow_missing:
            return None
        raise SyncError("Git read failed: " + result.stderr.strip()[:180])
    return result.stdout.strip()


def ancestry(root, parent, child):
    try:
        p = subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor", parent, child],
                           capture_output=True, timeout=10)
    except (OSError, subprocess.TimeoutExpired):
        return False
    return p.returncode == 0


def inspect(root, check_remote=False):
    root = Path(root).resolve(strict=True)
    try:
        project = json.loads((root / ".project-control/project.json").read_text(encoding="utf-8"))
        expected = canonical_github_repo(project["repo_url"]).lower()
        origin = canonical_github_repo(call(root, ["remote", "get-url", "origin"])).lower()
    except (OSError, ValueError, KeyError, InitError, TypeError) as exc:
        raise SyncError("registered Git origin cannot be verified") from exc
    if expected != origin:
        raise SyncError("Git origin differs from the registered target repository")
    top = Path(call(root, ["rev-parse", "--show-toplevel"])).resolve(strict=True)
    if top != root:
        raise SyncError("target path is not the Git worktree root")
    branch = call(root, ["branch", "--show-current"]) or "(detached HEAD)"
    head = call(root, ["rev-parse", "--verify", "HEAD"], allow_missing=True)
    dirty = bool(call(root, ["status", "--porcelain", "--untracked-files=normal"]))
    output = {
        "project_id": project.get("project_id"),
        "repo_url": expected, "branch": branch, "local_head": head,
        "worktree_dirty": dirty, "remote_checked": False,
        "remote_head": None, "relation": "REMOTE_NOT_CHECKED",
        "checked_at": None,
        "source": "LOCAL_GIT_ONLY",
        "note": "Local HEAD and remote-tracking branches do not establish GitHub synchronization.",
    }
    if not check_remote:
        return output
    if branch == "(detached HEAD)" or not BRANCH.fullmatch(branch):
        output["relation"] = "NO_NAMED_BRANCH"
        output["note"] = "An explicit remote check needs a valid named branch."
        return output
    ref = "refs/heads/" + branch
    try:
        lines = call(root, ["ls-remote", "--heads", "origin", ref], timeout=20)
    except SyncError as error:
        output["relation"] = "REMOTE_CHECK_FAILED"
        output["note"] = str(error)
        return output
    output["remote_checked"] = True
    output["checked_at"] = datetime.now(timezone.utc).isoformat(timespec="seconds")
    output["source"] = "EXPLICIT_GIT_LS_REMOTE"
    if not lines:
        output["relation"] = "BRANCH_NOT_PUBLISHED"
        output["note"] = "Origin does not currently publish this branch."
        return output
    matches = [line.split("\t") for line in lines.splitlines() if "\t" in line]
    matches = [pair[0] for pair in matches if len(pair) == 2 and pair[1] == ref
               and HEX.fullmatch(pair[0])]
    if len(matches) != 1:
        output["relation"] = "REMOTE_RESPONSE_UNVERIFIED"
        output["note"] = "Remote output could not be matched to exactly one branch."
        return output
    remote = matches[0]
    output["remote_head"] = remote
    if not head:
        output["relation"] = "LOCAL_UNBORN_REMOTE_EXISTS"
    elif head == remote:
        output["relation"] = "SAME_HEAD"
    elif call(root, ["cat-file", "-e", remote + "^{commit}"], allow_missing=True) is None:
        output["relation"] = "REMOTE_HISTORY_NOT_LOADED"
    elif ancestry(root, remote, head):
        output["relation"] = "LOCAL_AHEAD"
    elif ancestry(root, head, remote):
        output["relation"] = "REMOTE_AHEAD"
    else:
        output["relation"] = "DIVERGED"
    output["note"] = "Remote branch was checked without fetch, pull, push or reset."
    return output
