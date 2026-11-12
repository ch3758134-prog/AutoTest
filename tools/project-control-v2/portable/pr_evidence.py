"""Validate GitHub PR metadata for a Codex candidate, without accepting its work.

The manager AI must fetch PR metadata from the *connected GitHub provider*
immediately before calling this helper, rather than copying claims from a
report. This checker validates consistency only; it cannot attest that a
supplied dictionary really came from GitHub.
"""
from __future__ import annotations
import re
from urllib.parse import urlsplit


class PrEvidenceError(ValueError):
    pass


SHA40 = re.compile(r"^[0-9a-f]{40}$")
PR_PATH = re.compile(r"^/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)/pull/([1-9][0-9]*)/?$")


def repo_identity(url):
    if not isinstance(url, str):
        raise PrEvidenceError("repository URL missing")
    u = urlsplit(url)
    if (u.scheme != "https" or u.hostname != "github.com"
            or u.username or u.password or u.port or u.query or u.fragment):
        raise PrEvidenceError("invalid GitHub repository URL")
    bits = u.path.strip("/").removesuffix(".git").split("/")
    if len(bits) != 2 or not all(re.fullmatch(r"[A-Za-z0-9_.-]+", v) for v in bits):
        raise PrEvidenceError("invalid GitHub repository identity")
    return "/".join(bits).lower()


def validate(project, task, receipt, pr):
    if not all(isinstance(x, dict) for x in (project, task, receipt, pr)):
        raise PrEvidenceError("project, task, receipt and GitHub PR metadata are required")
    if (task.get("project_id") != project.get("project_id")
            or receipt.get("project_id") != project.get("project_id")
            or receipt.get("task_id") != task.get("id")):
        raise PrEvidenceError("PR result belongs to a different project or task")
    if repo_identity(task.get("source", {}).get("repo_url")) != repo_identity(project.get("repo_url")):
        raise PrEvidenceError("task source repository mismatch")
    if repo_identity(receipt.get("repo_url")) != repo_identity(project.get("repo_url")):
        raise PrEvidenceError("receipt repository mismatch")
    url = pr.get("url") or pr.get("display_url")
    if not isinstance(url, str):
        raise PrEvidenceError("GitHub PR URL missing")
    split = urlsplit(url)
    if (split.scheme != "https" or split.hostname != "github.com"
            or split.port or split.username or split.password or split.query or split.fragment):
        raise PrEvidenceError("GitHub PR URL host mismatch")
    match = PR_PATH.fullmatch(split.path)
    number = pr.get("number")
    if not match or type(number) is not int or number < 1 or number != int(match.group(3)):
        raise PrEvidenceError("PR URL or number does not match")
    if (match.group(1) + "/" + match.group(2)).lower() != repo_identity(project["repo_url"]):
        raise PrEvidenceError("PR URL belongs to another target repository")
    if (not isinstance(pr.get("head_repo_full_name"), str)
            or pr["head_repo_full_name"].lower() != repo_identity(project["repo_url"])):
        raise PrEvidenceError("unexpected fork or incorrect PR head repository")
    expected = task["source"]
    if pr.get("base") != expected.get("branch"):
        raise PrEvidenceError("PR base branch differs from task source")
    if pr.get("head") != expected.get("work_branch"):
        raise PrEvidenceError("PR work branch differs from task allocation")
    sha = pr.get("head_sha")
    if not SHA40.fullmatch(str(sha)) or sha != receipt.get("candidate_head"):
        raise PrEvidenceError("PR head has moved or does not match the Codex receipt")
    if pr.get("state") != "open" or pr.get("merged") is not False:
        raise PrEvidenceError("PR is no longer an open, unmerged candidate")
    if receipt.get("scope_status") != "SCOPE_OK" or receipt.get("workspace_clean") is not True:
        raise PrEvidenceError("Codex receipt did not pass local scope/cleanliness assertions")
    if not SHA40.fullmatch(str(receipt.get("source_head", ""))):
        raise PrEvidenceError("receipt lacks a source Git commit")
    if type(pr.get("draft")) is not bool:
        raise PrEvidenceError("GitHub PR Draft status unknown")
    return {
        "schema": "project-control-pr-review-observation/1",
        "project_id": project["project_id"],
        "task_id": task["id"],
        "repo_url": project["repo_url"],
        "pr_url": url,
        "pr_number": number,
        "head_sha": sha,
        "base_branch": pr["base"],
        "head_branch": pr["head"],
        "draft": pr["draft"],
        "status": "PR_METADATA_CONSISTENT_PENDING_INDEPENDENT_REVIEW",
        "code_diff_reviewed": False,
        "ci_checked": False,
        "test_results_accepted": False,
        "task_accepted": False,
        "gate_approved": False,
        "source_note": "Consistency of supplied GitHub metadata only; fetch authentic metadata through the connected provider.",
    }
