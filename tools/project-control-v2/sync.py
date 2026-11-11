"""Truthful local Git / GitHub sync inspection, no automatic push or reset."""
from __future__ import annotations
from pathlib import Path
import re
import subprocess
from datetime import datetime, timezone


def run_git(root, *args, timeout=12):
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True,
                            text=True, encoding="utf-8", errors="replace",
                            timeout=timeout, check=False)
    if result.returncode:
        raise RuntimeError(result.stderr.strip()[:350] or "git operation failed")
    return result.stdout.strip()


def status(root, file="tools/project-control-v2/workspace/autotest.json"):
    root = Path(root).resolve()
    status = {"source": "LOCAL_GIT", "remote_checked": False, "remote_head": None,
              "remote_relation": "REMOTE_UNVERIFIED", "branch": None,
              "local_head": None, "worktree_dirty": None,
              "control_file_modified": None, "remote_message": "尚未核实 GitHub 远端最新提交"}
    try:
        status["local_head"] = run_git(root, "rev-parse", "HEAD")
        status["branch"] = run_git(root, "branch", "--show-current") or "(detached HEAD)"
        changes = run_git(root, "status", "--porcelain", "--untracked-files=normal")
        status["worktree_dirty"] = bool(changes)
        record_changes = run_git(root, "status", "--porcelain", "--", file)
        status["control_file_modified"] = bool(record_changes)
        status["remote_tracking"] = None
        try:
            status["remote_tracking"] = run_git(root, "rev-parse", "refs/remotes/origin/" + status["branch"])
        except RuntimeError:
            pass
    except (OSError, RuntimeError, subprocess.TimeoutExpired) as exc:
        status["error"] = str(exc)[:400]
    return status


def check_remote(root, file="tools/project-control-v2/workspace/autotest.json"):
    result = status(root, file=file)
    branch = result.get("branch")
    if not branch or branch == "(detached HEAD)" or not re.fullmatch(r"[a-zA-Z0-9_./-]+", branch):
        result["remote_message"] = "未处于可核实的命名 Git 分支"
        return result
    try:
        output = run_git(root, "ls-remote", "--heads", "origin", "refs/heads/" + branch, timeout=20)
        result["remote_checked"] = True
        result["checked_at"] = datetime.now(timezone.utc).isoformat(timespec="seconds")
        if not output:
            result["remote_relation"] = "BRANCH_NOT_PUBLISHED"
            result["remote_message"] = "GitHub origin 没有同名分支；本地工作尚未通过该分支同步"
            return result
        remote_sha = output.split()[0]
        if not re.fullmatch(r"[0-9a-f]{40}", remote_sha):
            raise RuntimeError("GitHub origin 返回无法识别的提交号")
        result["remote_head"] = remote_sha
        local = result["local_head"]
        if local == remote_sha:
            relation = "SAME_HEAD"
        else:
            try:
                subprocess.run(["git", "-C", str(root), "cat-file", "-e", remote_sha + "^{commit}"],
                               check=True, capture_output=True, timeout=5)
                is_ancestor = lambda a,b: subprocess.run(["git", "-C", str(root),
                                                           "merge-base", "--is-ancestor", a, b],
                                                          capture_output=True, timeout=5).returncode == 0
                relation = "LOCAL_AHEAD" if is_ancestor(remote_sha,local) else (
                    "REMOTE_AHEAD" if is_ancestor(local,remote_sha) else "DIVERGED")
            except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired):
                relation = "DIFFERENT_HEAD_HISTORY_NOT_LOADED"
        result["remote_relation"] = relation
        result["remote_message"] = "已核对 GitHub origin 当前分支；未执行 fetch/pull/push"
    except (OSError, RuntimeError, subprocess.TimeoutExpired) as exc:
        result["remote_relation"] = "REMOTE_CHECK_FAILED"
        result["remote_message"] = "远端核对失败，不能宣称已同步: " + str(exc)[:220]
    return result
