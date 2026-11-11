#!/usr/bin/env python3
"""Project Control v2: independent, read-only project management UI.

Loads committed source project records and a separate proposed development plan.
Never writes project state, signs approvals, runs tools, or changes Git.
"""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import subprocess
from urllib.parse import urlsplit

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ARCHIVE = REPO / "docs/engineering-control/projects/autotest/snapshot.json"
PLAN = REPO / "tools/lifecycle/examples/autotest_plan_control_pilot.json"
PROCESS = HERE / "process.json"


def git_head():
    try:
        return subprocess.check_output(["git", "-C", str(REPO), "rev-parse", "HEAD"],
                                       stderr=subprocess.DEVNULL, timeout=5).decode().strip()
    except (OSError, subprocess.SubprocessError):
        return None


def load_model():
    source = json.loads(ARCHIVE.read_text(encoding="utf-8"))
    archive = source["engineering"]
    plan = json.loads(PLAN.read_text(encoding="utf-8"))
    process = json.loads(PROCESS.read_text(encoding="utf-8"))
    if archive["project"]["id"] != plan["project_id"]:
        raise ValueError("project id mismatch between source and plan")
    if process["project_id"] != plan["project_id"]:
        raise ValueError("project id mismatch between process and plan")
    from sys import path
    path.insert(0, str(REPO / "tools/lifecycle"))
    from studio_plan_integrity import assess
    assessment = assess(plan, source)
    return {
        "project": archive["project"],
        "objects": archive["objects"],
        "relations": archive["relations"],
        "plan": plan,
        "assessment": assessment,
        "process": process,
        "source": {
            "git_head": git_head(),
            "project_archive": str(ARCHIVE.relative_to(REPO)).replace("\\", "/"),
            "plan_file": str(PLAN.relative_to(REPO)).replace("\\", "/"),
            "process_file": str(PROCESS.relative_to(REPO)).replace("\\", "/"),
            "source": "CURRENT_LOCAL_WORKTREE",
            "note": "本页读取当前本地仓库文件；是否与 GitHub 远端最新一致尚未核验。"
        }
    }


def handler():
    paths = {
        "/": ("index.html", "text/html; charset=utf-8"),
        "/app.js": ("app.js", "application/javascript; charset=utf-8"),
        "/app.css": ("app.css", "text/css; charset=utf-8"),
    }
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            uri = urlsplit(self.path).path
            if uri == "/api/model":
                try:
                    raw = json.dumps(load_model(), ensure_ascii=False).encode("utf-8")
                    self._send(raw, "application/json; charset=utf-8")
                except (OSError, ValueError, KeyError) as exc:
                    body = json.dumps({"error": str(exc)}, ensure_ascii=False).encode("utf-8")
                    self._send(body, "application/json; charset=utf-8", 500)
                return
            entry = paths.get(uri)
            if entry is None:
                self._send(b"Not found", "text/plain; charset=utf-8", 404)
                return
            file, content_type = entry
            self._send((HERE / file).read_bytes(), content_type)

        def do_POST(self):
            self._send(b"Read-only review build", "text/plain; charset=utf-8", 405)

        def _send(self, content, content_type, status=200):
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy",
                             "default-src 'self'; script-src 'self'; style-src 'self'; "
                             "connect-src 'self'; object-src 'none'; base-uri 'none'")
            self.end_headers()
            self.wfile.write(content)
    return Handler


def main(argv=None):
    parser = argparse.ArgumentParser(description="Independent project control redesign")
    parser.add_argument("--port", type=int, default=8790)
    args = parser.parse_args(argv)
    server = ThreadingHTTPServer(("127.0.0.1", args.port), handler())
    print("Project Control v2 → http://127.0.0.1:" + str(server.server_port), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
