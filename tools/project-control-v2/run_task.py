#!/usr/bin/env python3
"""Codex execution receipt helper; explicit operator-invoked command only.

Tee command output to a private local log and append a clearly UNVERIFIED
execution receipt to the Git-tracked project-control record. Never calls git
push/commit, never approves a task, and never asserts ECU acceptance.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "tools/lifecycle"))
from control import Control, ControlError
from sync import status as git_status

LOCAL_LOG_DIR = HERE / "local-logs"


def timestamp():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def execute(args, output=sys.stdout):
    command = list(args.command)
    if command and command[0] == "--":
        command.pop(0)
    if not command:
        raise ControlError("explicit command must follow --")
    if any(not x or "\x00" in x for x in command):
        raise ControlError("invalid command argument")
    ctl = Control(HERE / "workspace/autotest.json",
                  REPO / "docs/engineering-control/projects/autotest/snapshot.json")
    before = ctl.read()
    w = next((x for x in before["planning"]["work"] if x["id"] == args.work), None)
    if w is None:
        raise ControlError("unknown work identifier")
    git = git_status(REPO)
    initial_head = git.get("local_head")
    if not initial_head or len(initial_head) != 40:
        raise ControlError("cannot verify local Git HEAD")
    LOCAL_LOG_DIR.mkdir(parents=True, exist_ok=True)
    run_id = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + os.urandom(4).hex()
    log_path = LOCAL_LOG_DIR / (run_id + ".log")
    started = timestamp()
    started_mono = time.monotonic()
    exit_code, failure_note = None, ""
    try:
        # stdout and stderr are merged in actual arrival order into one local log.
        with log_path.open("x", encoding="utf-8", errors="replace") as log:
            p = subprocess.Popen(command, cwd=str(REPO), stdout=subprocess.PIPE,
                                 stderr=subprocess.STDOUT, text=True, encoding="utf-8",
                                 errors="replace", bufsize=1, shell=False)
            try:
                deadline = started_mono + args.timeout
                assert p.stdout is not None
                for line in p.stdout:
                    log.write(line)
                    output.write(line)
                    if time.monotonic() > deadline:
                        p.kill()
                        failure_note = "operator timeout exceeded; process killed"
                        break
                exit_code = p.wait(timeout=8)
            finally:
                if p.poll() is None:
                    p.kill()
                    p.wait()
    except OSError as exc:
        failure_note = "could not run command: " + str(exc)
        exit_code = 127
        log_path.write_text(failure_note + "\n", encoding="utf-8")
    finally:
        duration_ms = int((time.monotonic() - started_mono) * 1000)

    digest = hashlib.sha256(log_path.read_bytes()).hexdigest()
    git_after = git_status(REPO)
    reported = {
        "work_id": args.work,
        "head_sha": initial_head,
        "commands": [{"command": json.dumps(command, ensure_ascii=False),
                      "exit_code": int(exit_code), "started_at": started,
                      "duration_ms": duration_ms}],
        "evidence_refs": ["local-log-sha256:" + digest],
        "note": ("实际 Codex 本地执行记录；完整 stdout/stderr 默认只留在本机。"
                 "开始 Git HEAD=" + initial_head +
                 "；结束 Git HEAD=" + str(git_after.get("local_head") or "UNKNOWN") +
                 "；工作树修改=" + str(git_after.get("worktree_dirty")) +
                 "；本地日志=" + log_path.name + "（未发布到 GitHub）" +
                 ("；" + failure_note if failure_note else "")),
        "reason": args.reason
    }
    outcome = ctl.apply("record_receipt", dict(reported, expected_revision=before["revision"]),
                        actor="CodexLocal:" + args.operator)
    summary = {"recorded": True, "receipt": outcome["result"]["receipt_id"],
               "work": args.work, "initial_head": initial_head,
               "ending_head": git_after.get("local_head"),
               "exit_code": int(exit_code), "duration_ms": duration_ms,
               "local_log_path": str(log_path), "log_sha256": digest,
               "git_control_record": "tools/project-control-v2/workspace/autotest.json",
               "note": "回执仍需人工核实并 Git 提交；自动记录不等于工程验收"}
    output.write("\nPROJECT_CONTROL_EXECUTION_RECEIPT=" + json.dumps(summary, ensure_ascii=False) + "\n")
    return int(exit_code)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Execute an explicit local command and record its evidence")
    parser.add_argument("--work", required=True, help="Exact ID of a managed work item")
    parser.add_argument("--operator", default="codex", help="Local operator/session label")
    parser.add_argument("--reason", default="负责人已要求执行本条命令，记录实际结果")
    parser.add_argument("--timeout", type=int, default=1800)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args(argv)
    if args.timeout < 1 or args.timeout > 21600:
        parser.error("--timeout must be between 1 and 21600 seconds")
    try:
        return execute(args)
    except (ControlError, ValueError, OSError) as exc:
        print("EXECUTION_RECEIPT_ERROR: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
