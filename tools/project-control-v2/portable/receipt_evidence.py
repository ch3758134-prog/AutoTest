"""Independent assessment of local Codex result receipts.

Receipt content is untrusted. Local Git objects are checked where present.
No fetch, push, checkout, shell commands, merge or deletion is performed.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import subprocess

HEX40 = re.compile(r"^[0-9a-f]{40}$")
HEX64 = re.compile(r"^[0-9a-f]{64}$")
IDENT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
MAX_PATHS = 500


class ReceiptError(ValueError):
    pass


def safe_path(path):
    if (not isinstance(path, str) or not path or len(path) > 500 or path.startswith("/")
            or "\\" in path or ":" in path or any(x in ("", ".", "..") for x in path.split("/"))):
        raise ReceiptError("unsafe or ambiguous repository-relative path")
    return path


def run_git(root, *args, binary=False):
    try:
        p = subprocess.run(["git", "-C", str(root), *args],
                           capture_output=True, timeout=15)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise ReceiptError("local Git verification unavailable") from exc
    if p.returncode:
        return None
    return p.stdout if binary else p.stdout.decode("utf-8", errors="replace").strip()


def is_ancestor(root, ancestor, descendant):
    try:
        p = subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor",
                            ancestor, descendant], capture_output=True, timeout=15)
    except (OSError, subprocess.TimeoutExpired):
        return False
    return p.returncode == 0


def path_allowed(path, patterns):
    return any(path.startswith(p[:-3] + "/") if p.endswith("/**") else path == p
               for p in patterns)


def validate_report(project, tasks, report):
    if not isinstance(report, dict) or report.get("schema") != "project-control-codex-result/1":
        raise ReceiptError("unrecognized Codex result schema")
    for key in ("receipt_id", "task_id"):
        if not isinstance(report.get(key), str) or not IDENT.fullmatch(report[key]):
            raise ReceiptError("invalid " + key)
    if report.get("project_id") != project.get("project_id"):
        raise ReceiptError("cross-project receipt is not allowed")
    try:
        # Avoid validating origin by substring: only exact registered project URLs.
        from urllib.parse import urlsplit
        def normalize(value):
            u = urlsplit(value)
            if (u.scheme != "https" or u.hostname != "github.com"
                    or u.username or u.password or u.port or u.query or u.fragment):
                raise ReceiptError("invalid receipt source URL")
            parts = u.path.strip("/").removesuffix(".git").split("/")
            if len(parts) != 2 or not all(re.fullmatch(r"[A-Za-z0-9_.-]+", p) for p in parts):
                raise ReceiptError("invalid repository origin")
            return "/".join(parts).lower()
        if normalize(report.get("repo_url")) != normalize(project.get("repo_url")):
            raise ReceiptError("receipt repository does not match target project")
    except (TypeError, AttributeError, ValueError) as error:
        raise ReceiptError("invalid receipt repository") from error
    for key in ("source_head", "candidate_head"):
        if not HEX40.fullmatch(str(report.get(key, ""))):
            raise ReceiptError("invalid " + key)
    if report["source_head"] == report["candidate_head"]:
        raise ReceiptError("receipt does not contain a new candidate commit")
    if not HEX64.fullmatch(str(report.get("task_registry_sha256", ""))):
        raise ReceiptError("missing task-registry digest")
    if report.get("scope_status") != "SCOPE_OK":
        raise ReceiptError("Codex did not submit a passing file-scope check")
    if report.get("workspace_clean") is not True:
        raise ReceiptError("Codex worktree has uncommitted changes; preserve work")
    paths = report.get("changed_paths")
    if (not isinstance(paths, list) or not paths or len(paths) > MAX_PATHS
            or len(set(x for x in paths if isinstance(x, str))) != len(paths)):
        raise ReceiptError("missing, duplicate, excessive or invalid changed paths")
    for p in paths:
        safe_path(p)
    evidence = report.get("test_evidence", [])
    if not isinstance(evidence, list) or len(evidence) > 25:
        raise ReceiptError("invalid test evidence")
    for item in evidence:
        if (not isinstance(item, dict) or not isinstance(item.get("name"), str)
                or not 1 <= len(item["name"]) <= 160
                or type(item.get("exit_code")) is not int
                or not HEX64.fullmatch(str(item.get("log_sha256", "")))):
            raise ReceiptError("test entry needs a bounded name, exit code and SHA-256")
    matching = [t for t in tasks.get("tasks", [])
                if isinstance(t, dict) and t.get("id") == report["task_id"]]
    if len(matching) != 1:
        raise ReceiptError("receipt task does not exist uniquely in this project")
    task = matching[0]
    if task.get("executor") != "local_codex":
        raise ReceiptError("receipt task is not a Codex task")
    source = task.get("source", {})
    if report.get("work_branch") != source.get("work_branch"):
        raise ReceiptError("receipt branch does not match allocated task branch")
    if not HEX40.fullmatch(str(source.get("pinned_sha", ""))):
        raise ReceiptError("task pinned source is missing")
    if report.get("task_registry_sha256") != hashlib.sha256(
            json.dumps(tasks, ensure_ascii=False, indent=2).encode("utf-8")).hexdigest():
        # Raw on-disk SHA may contain formatting differences; the caller should
        # pass its actual registry content digest instead of this fallback.
        pass
    return task


def assess(root, project, tasks, tasks_raw_sha256, report):
    task = validate_report(project, tasks, report)
    if report["task_registry_sha256"] != tasks_raw_sha256:
        raise ReceiptError("receipt was produced against a different task registry version")
    scope = task.get("allowed_paths")
    if not isinstance(scope, list) or not scope:
        raise ReceiptError("task file scope is missing")
    for path in report["changed_paths"]:
        if path == "PROJECT_START_HERE.md" or path.startswith(".project-control/"):
            raise ReceiptError("Codex cannot modify task governance files")
        if not path_allowed(path, scope):
            raise ReceiptError("reported change exceeds task scope")
    if not is_ancestor(root, task["source"]["pinned_sha"], report["source_head"]):
        # No guarantee the manager checkout has the Codex source yet.
        if run_git(root, "cat-file", "-e", report["source_head"] + "^{commit}") is None:
            status = "SOURCE_OBJECT_NOT_LOADED"
        else:
            raise ReceiptError("Codex source is not derived from its pinned task version")
    else:
        status = "SOURCE_AVAILABLE"
    if run_git(root, "cat-file", "-e", report["candidate_head"] + "^{commit}") is None:
        status = "RESULT_OBJECT_NOT_LOADED"
        actual_changes = None
    else:
        if status == "SOURCE_OBJECT_NOT_LOADED":
            raise ReceiptError("result exists but claimed source does not; cannot assess lineage")
        if not is_ancestor(root, report["source_head"], report["candidate_head"]):
            raise ReceiptError("result commit does not descend from claimed source")
        raw = run_git(root, "diff", "--no-renames", "--name-only", "-z",
                      report["source_head"], report["candidate_head"], binary=True)
        if raw is None:
            raise ReceiptError("failed to examine commit file changes")
        actual_changes = sorted(x.decode("utf-8", errors="replace") for x in raw.split(b"\0") if x)
        if actual_changes != sorted(report["changed_paths"]):
            raise ReceiptError("reported file list does not match actual Git candidate diff")
        for path in actual_changes:
            safe_path(path)
            if path == "PROJECT_START_HERE.md" or path.startswith(".project-control/") or not path_allowed(path, scope):
                raise ReceiptError("actual result commit modifies forbidden files")
        status = "LOCAL_COMMIT_SCOPE_VERIFIED"
    return {
        "id": report["receipt_id"], "task_id": report["task_id"],
        "source_head": report["source_head"], "candidate_head": report["candidate_head"],
        "work_branch": report["work_branch"], "changed_paths": sorted(report["changed_paths"]),
        "test_evidence": report.get("test_evidence", []),
        "git_assessment": status,
        "test_assessment": "SELF_REPORTED_LOG_HASHES_NOT_INDEPENDENTLY_VERIFIED",
        "review_status": "PENDING_INDEPENDENT_REVIEW",
        "safety": "NO_AUTOMATIC_TASK_COMPLETION_OR_GATE",
    }
