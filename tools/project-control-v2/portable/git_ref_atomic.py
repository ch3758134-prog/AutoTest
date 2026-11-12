"""GitHub Git Data fast-forward-only reference CAS.

Unlike GitHub Contents updates (which CAS a *file* SHA), the Git Data API
can create an immutable candidate commit parented to an EXACT branch HEAD
and then use non-force ref update. If another writer advances the branch,
that candidate is not a descendant of the new branch HEAD, so PATCH fails.
No force push, merge, reset, checkout, retry or credential handling here.

Requires an authenticated GitHub client with .request(), .branch_head(),
.file() and .commit_parents(). All paths must be literal file paths, not URLs.
"""
from __future__ import annotations
import hashlib
import re
from urllib.parse import quote

HEX = re.compile(r"^[0-9a-f]{40}$")
VALID_PATH = re.compile(r"^[^\x00-\x1f:][^\x00-\x1f]*$")
VALID_BRANCH = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,180}$")


class AtomicGitError(ValueError):
    pass


def validate_path(path):
    if (not isinstance(path, str) or not path or len(path) > 350
            or path.startswith("/") or "\\" in path or ":" in path
            or not VALID_PATH.fullmatch(path)
            or any(seg in ("", ".", "..", ".git") for seg in path.split("/"))):
        raise AtomicGitError("unsafe GitHub Git Data target file path")


def validate_branch(branch):
    if (not isinstance(branch, str) or not VALID_BRANCH.fullmatch(branch)
            or branch.startswith("-") or branch.endswith("/")
            or "//" in branch or ".." in branch or "@{" in branch):
        raise AtomicGitError("invalid target Git branch")


def git_blob_sha(payload):
    raw = payload.encode("utf-8")
    header = ("blob " + str(len(raw)) + "\0").encode("ascii")
    return hashlib.sha1(header + raw).hexdigest()


def commit_exact_file(client, repo_parts, branch, reviewed_head, path, content, message):
    """Return verifiable result. A failed ref update never claims publication."""
    if (not isinstance(repo_parts, (tuple, list)) or len(repo_parts) != 2
            or not all(isinstance(s, str) and re.fullmatch(r"[A-Za-z0-9_.-]+", s)
                       for s in repo_parts)):
        raise AtomicGitError("invalid target repository identity")
    validate_branch(branch)
    validate_path(path)
    if not isinstance(content, str) or len(content.encode("utf-8")) > 2_000_000:
        raise AtomicGitError("invalid proposed file size")
    if not isinstance(message, str) or not 3 <= len(message) <= 240:
        raise AtomicGitError("invalid commit message")
    if not isinstance(reviewed_head, str) or not HEX.fullmatch(reviewed_head):
        raise AtomicGitError("invalid reviewed source HEAD")
    parts = tuple(repo_parts)
    owner, repo = parts
    endpoint = "/repos/" + quote(owner) + "/" + quote(repo)
    if client.branch_head(parts, branch) != reviewed_head:
        raise AtomicGitError("remote branch changed before atomic publication")
    source_commit = client.request("GET", endpoint + "/git/commits/" + reviewed_head)
    try:
        base_tree = source_commit["tree"]["sha"]
    except (TypeError, KeyError) as exc:
        raise AtomicGitError("source Git tree unavailable") from exc
    if not isinstance(base_tree, str) or not HEX.fullmatch(base_tree):
        raise AtomicGitError("untrusted remote Git tree SHA")
    new_tree = client.request("POST", endpoint + "/git/trees", {
        "base_tree": base_tree,
        "tree": [{"path": path, "mode": "100644", "type": "blob", "content": content}]
    })
    tree_sha = new_tree.get("sha")
    if not isinstance(tree_sha, str) or not HEX.fullmatch(tree_sha):
        raise AtomicGitError("candidate Git tree not confirmed")
    candidate = client.request("POST", endpoint + "/git/commits", {
        "message": message, "tree": tree_sha, "parents": [reviewed_head]
    })
    candidate_sha = candidate.get("sha")
    if not isinstance(candidate_sha, str) or not HEX.fullmatch(candidate_sha):
        raise AtomicGitError("candidate Git commit not confirmed")
    # No force: a concurrent branch advance must reject the sibling commit.
    # Creating an unreachable Git object before a failed PATCH is acceptable;
    # it cannot alter the branch or overwrite user files.
    client.request("PATCH", endpoint + "/git/refs/heads/" + quote(branch, safe="/"), {
        "sha": candidate_sha, "force": False
    })
    # Verify commit lineage, exact branch destination and file content.
    parents = client.commit_parents(parts, candidate_sha)
    if parents != [reviewed_head]:
        raise AtomicGitError("published Git commit parent does not match reviewed HEAD")
    remote_head = client.branch_head(parts, branch)
    live_blob, remote_data = client.file(parts, path, branch)
    if live_blob != git_blob_sha(content) or remote_data != content.encode("utf-8"):
        raise AtomicGitError("remote file differs from authorized candidate; reconcile")
    if remote_head != candidate_sha:
        raise AtomicGitError("remote branch advanced again after publication; reconcile")
    return {
        "status": "ATOMIC_GIT_REF_CAS_PUBLISHED_AND_VERIFIED",
        "commit_sha": candidate_sha, "previous_head": reviewed_head,
        "branch": branch, "path": path, "file_blob_sha": live_blob,
        "force": False, "note": "One Git commit ref updated; no formal Gate or test approval.",
    }
