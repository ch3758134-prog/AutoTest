"""GitHub-backed module code-status inspection; strictly read-only, opt-in.

The report exposes exact GitHub default branch HEAD, latest commit touching
each registered module, and a bounded recent PR window. It NEVER infers
formal acceptance, necessary CI coverage, or local worktree synchronization.
"""
from __future__ import annotations
import re
from urllib.parse import quote

from module_control import defined_modules, assess_candidate_paths
from github_bridge import repo_parts, assert_sha, RemoteError

BRANCH = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,180}$")


def inspect(client, project, management, *, recent_pr_limit=20):
    modules = defined_modules(management)
    parts = repo_parts(project.get("repo_url"))
    owner, name = parts
    endpoint = "/repos/" + quote(owner) + "/" + quote(name)
    repo_info = client.request("GET", endpoint)
    branch = repo_info.get("default_branch") if isinstance(repo_info, dict) else None
    if (not isinstance(branch, str) or not BRANCH.fullmatch(branch)
            or ".." in branch or "//" in branch):
        raise RemoteError("GitHub default branch identity unavailable")
    base_head = client.branch_head(parts, branch)
    if not modules:
        return {"source": "LIVE_GITHUB_READ_ONLY",
                "repository": "/".join(parts), "branch": branch,
                "head": base_head, "modules": [], "recent_prs": [],
                "coverage": "NO_MODULES_REGISTERED"}
    module_results = []
    for item in modules:
        commits = []
        for prefix in item["owned_prefixes"]:
            response = client.request(
                "GET", endpoint + "/commits?sha=" + quote(base_head)
                + "&path=" + quote(prefix, safe="") + "&per_page=1")
            if not isinstance(response, list):
                raise RemoteError("module GitHub commit history unavailable")
            if response:
                sha = assert_sha(response[0]["sha"])
                commits.append({"path": prefix, "commit_sha": sha})
        module_results.append({
            "module_id": item["id"], "title": item["title"],
            "owner_role": item["owner_role"], "last_commits_by_path": commits,
            "github_default_head": base_head,
            "integration_evidence": "GITHUB_BRANCH_COMMIT_HISTORY_OBSERVED",
            "formal_approval": "NOT_CHECKED",
        })
    if not isinstance(recent_pr_limit, int) or not 1 <= recent_pr_limit <= 100:
        raise RemoteError("invalid bounded PR review window")
    query = "/pulls?state=all&sort=updated&direction=desc&per_page=" + str(recent_pr_limit + 1)
    response = client.request("GET", endpoint + query)
    if not isinstance(response, list):
        raise RemoteError("cannot retrieve recent GitHub PR records")
    truncated = len(response) > recent_pr_limit
    recent = []
    for raw in response[:recent_pr_limit]:
        if not isinstance(raw, dict):
            raise RemoteError("invalid PR metadata")
        try:
            number = raw["number"]
            head = assert_sha(raw["head"]["sha"])
            base = raw["base"]["ref"]
            pr_url = raw["html_url"]
            state = raw["state"]
            merged_at = raw.get("merged_at")
        except (KeyError, TypeError):
            raise RemoteError("incomplete GitHub PR record")
        if type(number) is not int or number < 1 or not isinstance(pr_url, str):
            raise RemoteError("invalid GitHub PR identity")
        if base != branch:
            # Other base branches are not evidence about this module baseline.
            continue
        changes = client.pr_files(parts, number, max_pages=5)
        paths = [entry.get("filename") for entry in changes]
        if any(not isinstance(p, str) for p in paths):
            raise RemoteError("unreadable actual changed PR paths")
        if not paths:
            continue
        audit = assess_candidate_paths(management, paths, None)
        if not audit["affected_module_ids"] and not audit["unowned_paths"]:
            continue
        recent.append({
            "pr_number": number, "pr_url": pr_url, "head_sha": head,
            "base_branch": branch, "state": state,
            "merged_by_github": bool(merged_at),
            "draft": bool(raw.get("draft")),
            "affected_modules": audit["affected_module_ids"],
            "interface_changes": audit["interface_changes"],
            "unowned_paths": audit["unowned_paths"],
            "scope_review": audit["result"],
            "reviewed": "NOT_CHECKED",
            "required_ci": "NOT_CHECKED",
        })
    after_head = client.branch_head(parts, branch)
    if after_head != base_head:
        raise RemoteError("GitHub base branch changed during module snapshot")
    return {
        "source": "LIVE_GITHUB_READ_ONLY",
        "repository": "/".join(parts), "branch": branch, "head": base_head,
        "modules": module_results, "recent_prs": recent,
        "recent_pr_count": len(recent), "pr_search_truncated": truncated,
        "coverage": "RECENT_PR_WINDOW_ONLY_NOT_COMPLETE_HISTORY",
        "warning": "Observed actual GitHub commits/PRs. Merge and CI do not automatically mean test or product approval.",
    }
