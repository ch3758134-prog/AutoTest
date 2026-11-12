#!/usr/bin/env python3
"""Read-only publication plan for a Codex task's PR submission.

Does NOT accept a task, approve CI, merge PRs, or directly write to GitHub.
A manager must fetch authentic current PR metadata through their GitHub
connection and write proposed changes with a content-SHA CAS operation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
from datetime import datetime, timezone

from pr_evidence import validate as validate_pr, PrEvidenceError
from task_publication import read_git, canonical_url, SHA40, PublicationError

TASK_STATE = "READY_FOR_LOCAL_IMPLEMENTATION"
NEXT_STATE = "SUBMITTED_FOR_REVIEW"


class SubmissionError(ValueError):
    pass


def make_plan(root, task_id, receipt, pr):
    root = Path(root).resolve(strict=True)
    files = root / ".project-control"
    contents = {}
    for name in ("project.json", "tasks.json", "decisions.json"):
        path = files / name
        if not path.is_file() or path.is_symlink():
            raise SubmissionError("target project records missing or unsafe")
        contents[name] = path.read_bytes()
    project, tasks, decisions = (json.loads(contents[name]) for name in contents)
    if project.get("schema") != "project-control-target-project/1":
        raise SubmissionError("unsupported target project schema")
    if tasks.get("schema") != "project-control-target-tasks/1" or tasks.get("project_id") != project.get("project_id"):
        raise SubmissionError("task registry/project identity mismatch")
    if decisions.get("project_id") != project.get("project_id"):
        raise SubmissionError("approval history/project mismatch")
    records = tasks.get("tasks")
    if not isinstance(records, list):
        raise SubmissionError("task list invalid")
    selected = [t for t in records if isinstance(t, dict) and t.get("id") == task_id]
    if len(selected) != 1:
        raise SubmissionError("submitted task must be unique")
    task = selected[0]
    if task.get("state") != TASK_STATE:
        raise SubmissionError("task must be READY before a PR can be submitted")
    if not isinstance(receipt, dict) or not isinstance(pr, dict):
        raise SubmissionError("result receipt and GitHub PR metadata required")
    task_sha = hashlib.sha256(contents["tasks.json"]).hexdigest()
    if receipt.get("task_registry_sha256") != task_sha:
        raise SubmissionError("Codex receipt references a stale task registry; review changes")
    try:
        source = task["source"]
        if canonical_url(project.get("repo_url")) != canonical_url(source.get("repo_url")):
            raise SubmissionError("registered task repository does not match project")
        if canonical_url(read_git(root, "remote", "get-url", "origin")) != canonical_url(project["repo_url"]):
            raise SubmissionError("manager Git origin differs from target project")
        if Path(read_git(root, "rev-parse", "--show-toplevel")).resolve() != root:
            raise SubmissionError("not at the target Git repository root")
        if read_git(root, "branch", "--show-current") != source.get("branch"):
            raise SubmissionError("manager checkout is not on task source branch")
        if read_git(root, "status", "--porcelain", "--untracked-files=all"):
            raise SubmissionError("manager source worktree has uncommitted changes")
        source_sha = source.get("pinned_sha")
        if not SHA40.fullmatch(str(source_sha)):
            raise SubmissionError("invalid source pin")
        lineage = read_git(root, "merge-base", source_sha, "HEAD", missing_ok=True)
        if lineage != source_sha:
            raise SubmissionError("current manager checkout does not descend from task source")
        if task.get("published_source_sha") not in (None, source_sha):
            raise SubmissionError("task was published against another source")
        if not any(isinstance(d, dict) and d.get("kind") == "TASK_EXECUTION_APPROVAL"
                   and d.get("state") == "APPROVED" and d.get("task_id") == task_id
                   and d.get("source_sha") == source_sha
                   and d.get("approved_by") == task.get("authorization", {}).get("approved_by")
                   and d.get("approval_reference") == task.get("authorization", {}).get("approval_reference")
                   for d in decisions.get("decisions", [])):
            raise SubmissionError("task execution approval missing from management decisions")
    except (KeyError, AttributeError, TypeError) as exc:
        raise SubmissionError("invalid task source or authorization") from exc
    try:
        pr_observation = validate_pr(project, task, receipt, pr)
    except PrEvidenceError as exc:
        raise SubmissionError(str(exc)) from exc
    # Supplying a PR snapshot is not evidence of its provider origin. The
    # manager must fetch it directly through the connected GitHub provider.
    records.index(task)
    updated = json.loads(contents["tasks.json"])
    found = next(i for i, x in enumerate(updated["tasks"]) if x.get("id") == task_id)
    item = updated["tasks"][found]
    item["state"] = NEXT_STATE
    item["submission"] = {
        "pr_url": pr_observation["pr_url"],
        "pr_number": pr_observation["pr_number"],
        "candidate_head": pr_observation["head_sha"],
        "source_head": receipt["source_head"],
        "pr_branch": pr_observation["head_branch"],
        "base_branch": pr_observation["base_branch"],
        "receipt_id": receipt.get("receipt_id"),
        "status": "CANDIDATE_REQUIRES_INDEPENDENT_REVIEW",
        "code_review": "NOT_APPROVED",
        "ci_review": "NOT_CHECKED",
        "tests": "NOT_INDEPENDENTLY_VERIFIED",
        "observed_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }
    updated.setdefault("events", []).append({
        "kind": "TASK_SUBMITTED_CANDIDATE_PR",
        "task_id": task_id, "pr_number": pr_observation["pr_number"],
        "candidate_head": pr_observation["head_sha"],
        "gate": "NOT_CHANGED",
    })
    if len(updated["events"]) > 1000:
        raise SubmissionError("task event log requires archival")
    return {
        "schema": "project-control-submission-plan/1",
        "project_id": project["project_id"],
        "task_id": task_id,
        "target_repo": project["repo_url"],
        "target_branch": task["source"]["branch"],
        "expected_tasks_blob_sha": read_git(root, "hash-object", ".project-control/tasks.json"),
        "observed_source_branch_head": read_git(root, "rev-parse", "HEAD"),
        "desired_path": ".project-control/tasks.json",
        "proposed_content": json.dumps(updated, ensure_ascii=False, indent=2) + "\n",
        "status": "PR_CANDIDATE_AWAITING_GITHUB_CONTENTS_CAS",
        "requirements": [
            "Freshly fetch the PR from the connected GitHub provider immediately before publishing.",
            "Check that its head SHA and all task inputs still match this plan.",
            "Write with the current GitHub blob SHA; never overwrite concurrent changes.",
            "Do not mark tests, work item or Gate approved from a PR submission.",
        ],
    }


def main(argv=None):
    cli = argparse.ArgumentParser(description="Preflight task PR submission (no remote writes)")
    cli.add_argument("--target", type=Path, required=True)
    cli.add_argument("--task-id", required=True)
    cli.add_argument("--receipt", type=Path, required=True)
    cli.add_argument("--pr-snapshot", type=Path, required=True)
    options = cli.parse_args(argv)
    try:
        report = json.loads(options.receipt.read_text(encoding="utf-8"))
        pr = json.loads(options.pr_snapshot.read_text(encoding="utf-8"))
        plan = make_plan(options.target, options.task_id, report, pr)
        print(json.dumps(plan, ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(json.dumps({"status": "BLOCKED", "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
