#!/usr/bin/env python3
"""Explicit GitHub Contents CAS and PR evidence reader for a target project.

This is an optional LOCAL bridge when a connected ChatGPT GitHub writer cannot
be used. It never clones, fetches, merges, deletes, pushes branches or approves
work. A token is read from the process environment only for --apply.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlsplit
from urllib.request import Request, urlopen

HEX40 = re.compile(r"^[0-9a-f]{40}$")
EXPECTED_FILE = ".project-control/tasks.json"
HEADINGS = ("project-control-task-publication-plan/1", "project-control-submission-plan/1")
API = "https://api.github.com"


class RemoteError(ValueError):
    pass


def repo_parts(url):
    if not isinstance(url, str):
        raise RemoteError("target repo URL missing")
    u = urlsplit(url)
    if (u.scheme != "https" or u.hostname != "github.com"
            or u.username or u.password or u.port or u.query or u.fragment):
        raise RemoteError("expected exact HTTPS github.com owner/repository URL")
    parts = u.path.strip("/").removesuffix(".git").split("/")
    if len(parts) != 2 or not all(re.fullmatch("[A-Za-z0-9_.-]+", p) for p in parts):
        raise RemoteError("invalid target repository identity")
    return tuple(parts)


def assert_sha(value):
    if not isinstance(value, str) or not HEX40.fullmatch(value):
        raise RemoteError("expected exact Git SHA-1")
    return value


def ref_component(value):
    if not isinstance(value, str) or not value or value.startswith("-") or ".." in value or "@{" in value:
        raise RemoteError("invalid branch or Git reference")
    return quote(value, safe="")


class GitHub:
    def __init__(self, token=None, exchange=None):
        self.token = token
        self.exchange = exchange

    def request(self, method, resource, data=None):
        if not resource.startswith("/repos/") or ".." in resource.split("?")[0].split("/"):
            raise RemoteError("unsafe GitHub API resource")
        if self.exchange:
            return self.exchange(method, resource, data)
        if not self.token:
            raise RemoteError("GitHub token missing; use a connected ChatGPT writer or environment token")
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8") if data is not None else None
        req = Request(
            API + resource, data=payload, method=method,
            headers={"Authorization": "Bearer " + self.token,
                     "Accept": "application/vnd.github+json",
                     "X-GitHub-Api-Version": "2022-11-28",
                     "Content-Type": "application/json",
                     "User-Agent": "ProjectControl-Candidate"},
        )
        try:
            with urlopen(req, timeout=25) as response:
                return json.loads(response.read().decode("utf-8"))
        except HTTPError as err:
            # Avoid printing request headers/token or raw errors, which can
            # contain private data. Surface stable HTTP status only.
            raise RemoteError("GitHub API rejected operation HTTP " + str(err.code)) from err
        except (URLError, OSError, ValueError) as err:
            raise RemoteError("GitHub API unavailable or returned invalid JSON") from err

    def branch_head(self, parts, branch):
        owner, repo = parts
        x = self.request("GET", "/repos/" + quote(owner) + "/" + quote(repo)
                         + "/branches/" + ref_component(branch))
        try:
            return assert_sha(x["commit"]["sha"])
        except (KeyError, TypeError) as err:
            raise RemoteError("GitHub branch response incomplete") from err

    def file(self, parts, path, branch):
        owner, repo = parts
        suffix = "?ref=" + ref_component(branch)
        data = self.request("GET", "/repos/" + quote(owner) + "/" + quote(repo)
                            + "/contents/" + quote(path, safe="/") + suffix)
        if not isinstance(data, dict) or data.get("type") != "file":
            raise RemoteError("expected GitHub file record")
        sha = assert_sha(data.get("sha"))
        try:
            if data.get("encoding") != "base64":
                raise ValueError("not base64")
            raw = base64.b64decode(data["content"], validate=False)
        except (ValueError, TypeError, KeyError) as err:
            raise RemoteError("GitHub content unavailable as base64") from err
        return sha, raw

    def is_ancestor(self, parts, ancestor, head):
        """GitHub compare API says head contains ancestor (or is identical)."""
        owner, repo = parts
        data = self.request(
            "GET", "/repos/" + quote(owner) + "/" + quote(repo) + "/compare/"
            + assert_sha(ancestor) + "..." + assert_sha(head))
        if data.get("status") not in ("ahead", "identical"):
            return False
        return True

    def commit_parents(self, parts, sha):
        owner, repo = parts
        item = self.request("GET", "/repos/" + quote(owner) + "/" + quote(repo)
                            + "/commits/" + assert_sha(sha))
        try:
            return [assert_sha(x["sha"]) for x in item["parents"]]
        except (KeyError, TypeError) as err:
            raise RemoteError("commit parent evidence unavailable") from err

    def pr(self, parts, number):
        if type(number) is not int or number < 1:
            raise RemoteError("invalid PR number")
        owner, repo = parts
        raw = self.request("GET", "/repos/" + quote(owner) + "/" + quote(repo)
                           + "/pulls/" + str(number))
        try:
            return {
                "number": number, "url": raw["html_url"],
                "head_repo_full_name": raw["head"]["repo"]["full_name"],
                "head": raw["head"]["ref"], "head_sha": assert_sha(raw["head"]["sha"]),
                "base": raw["base"]["ref"], "state": raw["state"],
                "draft": raw["draft"], "merged": bool(raw.get("merged_at")),
            }
        except (KeyError, TypeError) as err:
            raise RemoteError("GitHub PR metadata incomplete") from err

    def pr_files(self, parts, number, max_pages=20):
        owner, repo = parts
        files = []
        for page in range(1, max_pages + 1):
            batch = self.request(
                "GET", "/repos/" + quote(owner) + "/" + quote(repo)
                + "/pulls/" + str(number) + "/files?per_page=100&page=" + str(page))
            if not isinstance(batch, list):
                raise RemoteError("PR changed file response incomplete")
            files += batch
            if len(batch) < 100:
                return files
        raise RemoteError("PR contains too many changed files to verify exhaustively")


def validate_plan(plan, repo_url):
    if not isinstance(plan, dict) or plan.get("schema") not in HEADINGS:
        raise RemoteError("unrecognized publication plan")
    parts = repo_parts(repo_url)
    if repo_parts(plan.get("repo_url") or plan.get("target_repo")) != parts:
        raise RemoteError("publication plan is for a different target repository")
    branch = plan.get("source_branch") or plan.get("target_branch")
    if not isinstance(branch, str) or not branch or branch in ("(detached)",):
        raise RemoteError("invalid publication branch")
    path = plan.get("desired_file") or plan.get("desired_path")
    if path != EXPECTED_FILE:
        raise RemoteError("only target project tasks.json may be published")
    expected_blob = assert_sha(plan.get("expected_tasks_blob_sha"))
    expected_head = assert_sha(plan.get("preflight_branch_head") or plan.get("observed_source_branch_head"))
    content = plan.get("proposed_content")
    if not isinstance(content, str) or len(content.encode("utf-8")) > 200000:
        raise RemoteError("invalid or oversize tasks content")
    candidate = json.loads(content)
    if (candidate.get("schema") != "project-control-target-tasks/1"
            or candidate.get("project_id") != plan.get("project_id")):
        raise RemoteError("candidate task registry does not match project")
    target = [x for x in candidate.get("tasks", [])
              if isinstance(x, dict) and x.get("id") == plan.get("task_id")]
    if len(target) != 1:
        raise RemoteError("candidate task ID mismatch")
    required = ("READY_FOR_LOCAL_IMPLEMENTATION" if plan["schema"] == HEADINGS[0]
                else "SUBMITTED_FOR_REVIEW")
    if target[0].get("state") != required:
        raise RemoteError("candidate state does not match plan type")
    return parts, branch, expected_blob, expected_head, content


def validate_remote_authority(client, parts, branch, plan, proposed, current):
    """Revalidate *remote* decision/design and exact one-task transition.

    A locally authored plan cannot grant itself approval. Validation at the
    actual remote branch is still subject to race before the GitHub PUT.
    """
    import copy
    try:
        project = json.loads(client.file(parts, ".project-control/project.json", branch)[1])
        decisions = json.loads(client.file(parts, ".project-control/decisions.json", branch)[1])
    except (ValueError, TypeError, KeyError) as exc:
        raise RemoteError("remote project/approval records unreadable") from exc
    if (project.get("schema") != "project-control-target-project/1"
            or current.get("schema") != "project-control-target-tasks/1"
            or decisions.get("schema") != "project-control-target-decisions/1"
            or not all(x.get("project_id") == plan.get("project_id")
                        for x in (project, decisions, current))):
        raise RemoteError("remote project, tasks or decisions identity mismatch")
    if repo_parts(project.get("repo_url")) != parts:
        raise RemoteError("remote project points to another repository")
    if not isinstance(decisions.get("decisions"), list) or not isinstance(current.get("tasks"), list):
        raise RemoteError("remote decision/task registry is malformed")
    tasks = current["tasks"]
    hits = [i for i, t in enumerate(tasks) if isinstance(t, dict) and t.get("id") == plan["task_id"]]
    if len(hits) != 1:
        raise RemoteError("remote task does not exist uniquely")
    index = hits[0]
    existing = tasks[index]
    source = existing.get("source", {})
    if not isinstance(source, dict) or repo_parts(source.get("repo_url")) != parts:
        raise RemoteError("remote task source identity mismatch")
    pin = assert_sha(source.get("pinned_sha"))
    if source.get("branch") != branch:
        raise RemoteError("remote task points to another branch")
    if not client.is_ancestor(parts, pin, plan.get("preflight_branch_head") or plan.get("observed_source_branch_head")):
        raise RemoteError("task source is no longer an ancestor of the reviewed branch")
    auth = existing.get("authorization")
    if not isinstance(auth, dict) or not auth.get("approved_by") or not auth.get("approval_reference"):
        raise RemoteError("task lacks human authorization fields")
    approvals = [
        d for d in decisions["decisions"] if isinstance(d, dict)
        and d.get("kind") == "TASK_EXECUTION_APPROVAL" and d.get("state") == "APPROVED"
        and d.get("task_id") == plan["task_id"] and d.get("source_sha") == pin
        and d.get("approved_by") == auth["approved_by"]
        and d.get("approval_reference") == auth["approval_reference"]
    ]
    if len(approvals) != 1:
        raise RemoteError("no separately authorized remote task execution decision")
    if existing.get("mode") == "CONTROLLED":
        if project.get("formal_product_identity") != "APPROVED" or project.get("scope_decision") != "APPROVED":
            raise RemoteError("project scope and identity not approved")
        proven = False
        for inp in existing.get("inputs", []):
            if not isinstance(inp, dict) or inp.get("type") != "DESIGN_BASELINE":
                continue
            design_path, design_sha = inp.get("path"), inp.get("blob_sha")
            if (not isinstance(design_path, str) or not design_path
                    or design_path.startswith("/") or "\\" in design_path
                    or ":" in design_path or any(p in ("", ".", "..") for p in design_path.split("/"))
                    or design_path.startswith(".project-control/")
                    or not HEX40.fullmatch(str(design_sha))):
                continue
            try:
                remote_blob, _ = client.file(parts, design_path, pin)
            except RemoteError:
                continue
            if remote_blob == design_sha and any(
                isinstance(d, dict) and d.get("kind") == "DESIGN_APPROVAL"
                and d.get("state") == "APPROVED" and d.get("path") == design_path
                and d.get("blob_sha") == design_sha and d.get("approved_by")
                and d.get("approval_reference") for d in decisions["decisions"]
            ):
                proven = True
        if not proven:
            raise RemoteError("remote approved design blob missing or changed")
    elif existing.get("mode") != "EXPLORATORY":
        raise RemoteError("task mode missing or invalid")

    if (not isinstance(proposed.get("tasks"), list) or len(proposed["tasks"]) != len(tasks)
            or not isinstance(proposed["tasks"][index], dict)):
        raise RemoteError("candidate may not add or delete unrelated tasks")
    other_before = [t for i,t in enumerate(tasks) if i != index]
    other_after = [t for i,t in enumerate(proposed["tasks"]) if i != index]
    if other_before != other_after:
        raise RemoteError("candidate attempts to change other tasks")

    updated = proposed["tasks"][index]
    if plan["schema"] == HEADINGS[0]:
        if existing.get("state") not in ("DRAFT", "AWAITING_AUTHORIZATION"):
            raise RemoteError("remote task is not eligible for READY promotion")
        if any(t.get("state") == "READY_FOR_LOCAL_IMPLEMENTATION" and
               t.get("executor") == "local_codex" for t in other_before):
            raise RemoteError("another READY Codex task makes default selection ambiguous")
        permitted = copy.deepcopy(existing)
        permitted["state"] = "READY_FOR_LOCAL_IMPLEMENTATION"
        permitted["published_source_sha"] = pin
        if updated != permitted or proposed.get("events") != current.get("events"):
            raise RemoteError("READY candidate contains unapproved edits")
    else:
        if existing.get("state") != "READY_FOR_LOCAL_IMPLEMENTATION":
            raise RemoteError("remote task is not currently READY")
        submission = updated.get("submission")
        if not isinstance(submission, dict):
            raise RemoteError("submitted task has no PR evidence")
        if (submission.get("status") != "CANDIDATE_REQUIRES_INDEPENDENT_REVIEW"
                or submission.get("code_review") != "NOT_APPROVED"
                or submission.get("ci_review") != "NOT_CHECKED"):
            raise RemoteError("submission candidate falsely claims review or CI acceptance")
        pr_number = submission.get("pr_number")
        meta = client.pr(parts, pr_number)
        if (meta.get("head_sha") != submission.get("candidate_head")
                or meta.get("head") != source.get("work_branch")
                or meta.get("base") != branch or meta.get("state") != "open"
                or meta.get("merged") is not False
                or meta.get("head_repo_full_name", "").lower() != "/".join(parts).lower()
                or meta.get("url") != submission.get("pr_url")):
            raise RemoteError("live GitHub PR differs from submission record")
        candidate_paths = submission.get("changed_paths")
        if (not isinstance(candidate_paths, list) or not candidate_paths
                or len(candidate_paths) != len(set(candidate_paths))):
            raise RemoteError("submission missing the Codex-reported file diff")
        pr_paths = [f.get("filename") for f in client.pr_files(parts, pr_number)]
        if sorted(pr_paths) != sorted(candidate_paths):
            raise RemoteError("live PR files do not match submitted evidence")
        expected = copy.deepcopy(existing)
        expected["state"] = "SUBMITTED_FOR_REVIEW"
        expected["submission"] = submission
        if updated != expected:
            raise RemoteError("submission candidate includes unrelated task edits")
        previous_events = current.get("events", [])
        proposed_events = proposed.get("events", [])
        if not isinstance(previous_events, list) or not isinstance(proposed_events, list):
            raise RemoteError("invalid task audit history")
        if proposed_events[:-1] != previous_events or len(proposed_events) != len(previous_events)+1:
            raise RemoteError("task audit events altered during submission")
        entry = proposed_events[-1]
        if (entry.get("kind") != "TASK_SUBMITTED_CANDIDATE_PR"
                or entry.get("task_id") != plan["task_id"]
                or entry.get("candidate_head") != submission.get("candidate_head")
                or entry.get("gate") != "NOT_CHANGED"):
            raise RemoteError("invalid submitted review audit event")
    for key,value in current.items():
        if key not in ("tasks","events") and proposed.get(key) != value:
            raise RemoteError("candidate changed unrelated registry properties")

def publish_plan(client, plan, repo_url, *, apply=False):
    parts, branch, blob, head, content = validate_plan(plan, repo_url)
    result = {
        "operation": "TARGET_TASK_RECORD_CAS",
        "repository": "/".join(parts),
        "branch": branch, "expected_branch_head": head,
        "expected_file_blob": blob, "content_sha256": hashlib.sha256(content.encode()).hexdigest(),
        "status": "PREVIEW_NOT_PUBLISHED",
    }
    if not apply:
        return result
    # A branch-head check is a guard, not a multi-file transaction.
    remote_head = client.branch_head(parts, branch)
    if remote_head != head:
        raise RemoteError("remote branch HEAD changed; review source and approvals again")
    live_blob, live_raw = client.file(parts, EXPECTED_FILE, branch)
    if live_blob != blob:
        raise RemoteError("tasks.json changed; refuse to overwrite concurrent edits")
    try:
        remote_current = json.loads(live_raw.decode("utf-8"))
        proposed = json.loads(content)
    except (ValueError, UnicodeDecodeError) as exc:
        raise RemoteError("unreadable remote or proposed task JSON") from exc
    validate_remote_authority(client, parts, branch, plan, proposed, remote_current)
    owner, name = parts
    payload = {
        "message": "chore(project-control): update authorized task " + plan["task_id"],
        "branch": branch, "sha": blob,
        "content": base64.b64encode(content.encode("utf-8")).decode("ascii"),
    }
    response = client.request("PUT", "/repos/" + quote(owner) + "/" + quote(name)
                              + "/contents/" + EXPECTED_FILE, payload)
    try:
        committed_sha = assert_sha(response["commit"]["sha"])
    except (KeyError, TypeError) as exc:
        raise RemoteError("GitHub write did not return a verifiable commit; check remote manually") from exc
    live_sha, live_data = client.file(parts, EXPECTED_FILE, branch)
    if live_data != content.encode("utf-8"):
        raise RemoteError("remote tasks.json after write differs from candidate; review remote manually")
    # Verify whether the new commit's sole parent is the reviewed branch
    # version. If GitHub performed a concurrent rebase/branch update, do not
    # call it a verified atomic transaction.
    parents = client.commit_parents(parts, committed_sha)
    if parents != [head]:
        raise RemoteError("GitHub branch changed during file write; commit requires manual reconciliation")
    result.update({"status": "GITHUB_WRITE_VERIFIED_PENDING_INDEPENDENT_REVIEW",
                   "commit_sha": committed_sha, "content_blob_sha": live_sha})
    return result


def verify_remote_pr(client, project, task, receipt, number):
    """Check PR head and actual changed paths. Does not approve CI or review."""
    from pr_evidence import validate as validate_metadata, PrEvidenceError
    parts = repo_parts(project.get("repo_url"))
    meta = client.pr(parts, number)
    try:
        observation = validate_metadata(project, task, receipt, meta)
    except PrEvidenceError as err:
        raise RemoteError(str(err)) from err
    files = client.pr_files(parts, number)
    changed = []
    for item in files:
        path = item.get("filename")
        if not isinstance(path, str):
            raise RemoteError("PR file item missing filename")
        if item.get("status") == "renamed":
            raise RemoteError("renamed PR paths require additional mapping and review")
        changed.append(path)
    if sorted(changed) != sorted(receipt.get("changed_paths", [])):
        raise RemoteError("GitHub PR file list does not match the Codex result")
    observation["changed_paths"] = sorted(changed)
    observation["pr_files_checked"] = True
    observation["reviewed_head_sha"] = meta["head_sha"]
    observation["status"] = "PR_FILES_AND_IDENTITY_MATCHED_PENDING_CI_AND_REVIEW"
    # Do not trust purported CI results/approvals without fetching provider
    # data for the same SHA and explicit reviewer decisions.
    return observation


def main(argv=None):
    p = argparse.ArgumentParser(description="Explicit target GitHub Contents CAS tool")
    p.add_argument("--plan", required=True, type=Path)
    p.add_argument("--repo-url", required=True)
    p.add_argument("--apply", action="store_true")
    args = p.parse_args(argv)
    try:
        plan = json.loads(args.plan.read_text(encoding="utf-8"))
        client = GitHub(token=os.environ.get("PROJECT_CONTROL_GITHUB_TOKEN") if args.apply else None)
        result = publish_plan(client, plan, args.repo_url, apply=args.apply)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except (RemoteError, OSError, KeyError, ValueError, TypeError) as err:
        print(json.dumps({"status": "BLOCKED", "error": str(err)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
