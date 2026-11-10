#!/usr/bin/env python3
"""Local, single-user engineering workbench HTTP adapter; no pip requirements."""
from __future__ import annotations
import argparse
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
import json
import os
from pathlib import Path
import secrets
from urllib.parse import urlsplit, parse_qs

from studio_store import Store, RuleError

HERE = Path(__file__).resolve().parent
WEB = HERE / "studio.html"
PORT = 8765


def handler_factory(store, token):
    class Handler(BaseHTTPRequestHandler):
        server_version = "EngineeringWorkbench/0.3"

        def write(self, value, status=200, content_type="application/json; charset=utf-8",
                  download=None):
            data = (value if isinstance(value, bytes)
                    else json.dumps(value, ensure_ascii=False).encode("utf-8"))
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("X-Frame-Options", "DENY")
            self.send_header("Referrer-Policy", "no-referrer")
            self.send_header("Content-Security-Policy",
                             "default-src 'self'; script-src 'self' 'unsafe-inline'; "
                             "style-src 'self' 'unsafe-inline'; connect-src 'self'; "
                             "img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'")
            if download:
                self.send_header("Content-Disposition", 'attachment; filename="' + download + '"')
            self.end_headers()
            self.wfile.write(data)

        def route(self):
            route = urlsplit(self.path)
            return route.path, {k: v[0] for k, v in parse_qs(route.query).items()}

        def host_ok(self):
            value = self.headers.get("Host", "").split(":")[0].lower()
            return value in ("127.0.0.1", "localhost")

        def do_GET(self):
            if not self.host_ok():
                return self.write({"error": "Invalid host"}, 403)
            path, params = self.route()
            try:
                if path in ("/", "/index.html"):
                    return self.write(WEB.read_bytes(), content_type="text/html; charset=utf-8")
                if path == "/api/studio/bootstrap":
                    return self.write({"schema": "engineering-workbench/1",
                                       "csrf": token, "projects": store.projects()})
                if path == "/api/studio/projects":
                    return self.write(store.projects())
                if path == "/api/studio/project":
                    return self.write(store.project(params.get("id", "")))
                if path == "/api/studio/object":
                    return self.write(store.object(params.get("id", "")))
                if path == "/api/studio/impact":
                    return self.write(store.impact(params.get("id", "")))
                if path == "/api/studio/readiness":
                    return self.write(store.readiness(params.get("id", "")))
                if path == "/api/studio/context":
                    return self.write(store.context(params.get("id", "")))
                if path == "/api/studio/export":
                    pid = params.get("project", "")
                    data = store.export_project(pid)
                    return self.write(data, download="engineering-project-" + pid[:32] + ".json")
                return self.write({"error": "Route not found"}, 404)
            except (RuleError, ValueError) as exc:
                return self.write({"error": str(exc)}, 400)
            except OSError:
                return self.write({"error": "Unable to read local workbench data"}, 500)

        def do_POST(self):
            if not self.host_ok() or self.headers.get("X-Local-Token", "") != token:
                return self.write({"error": "Local write token invalid"}, 403)
            origin = self.headers.get("Origin", "")
            if origin and origin not in (
                "http://127.0.0.1:" + str(self.server.server_port),
                "http://localhost:" + str(self.server.server_port),
            ):
                return self.write({"error": "Cross-origin writes are blocked"}, 403)
            if self.headers.get("Content-Type", "").split(";")[0] != "application/json":
                return self.write({"error": "Use application/json"}, 415)
            try:
                size = int(self.headers.get("Content-Length", "0"))
                if size < 1 or size > 250000:
                    return self.write({"error": "Invalid request length"}, 413)
                data = json.loads(self.rfile.read(size).decode("utf-8"))
                if not isinstance(data, dict):
                    raise RuleError("Request body must be an object")
                path, params = self.route()
                pieces = path.split("/")
                if path == "/api/studio/project":
                    answer = store.create_project(data)
                elif path == "/api/studio/project/update":
                    answer = store.update_project(data.get("id", ""), data)
                elif path == "/api/studio/object":
                    answer = store.create_object(data)
                elif path == "/api/studio/relation":
                    answer = store.create_relation(data)
                elif path == "/api/studio/relation/delete":
                    answer = store.delete_relation(data.get("id", ""))
                elif path == "/api/studio/relation/approve":
                    answer = store.approve_relation(data.get("id", ""),
                                                    data.get("actor", ""), data.get("note", ""))
                elif len(pieces) == 6 and pieces[:4] == ["", "api", "studio", "object"]:
                    key, verb = pieces[4:6]
                    if verb == "update":
                        answer = store.update_object(key, data)
                    elif verb == "submit":
                        answer = store.submit(key, data.get("expected_revision"))
                    elif verb == "approve":
                        answer = store.approve(key, data.get("expected_revision"),
                                               data.get("actor", ""), data.get("note", ""))
                    elif verb == "close":
                        answer = store.close_change(key, data.get("expected_revision"),
                                                    data.get("actor", ""), data.get("note", ""))
                    else:
                        return self.write({"error": "Route not found"}, 404)
                else:
                    return self.write({"error": "Route not found"}, 404)
                return self.write(answer)
            except (RuleError, ValueError, TypeError, KeyError, json.JSONDecodeError) as exc:
                return self.write({"error": str(exc)}, 400)

    return Handler


def default_data():
    base = Path(os.environ.get("LOCALAPPDATA", "")) if os.name == "nt" else Path.home() / ".local" / "share"
    return base / "EngineeringWorkbench" / "workspace.sqlite3"


def main(argv=None):
    parser = argparse.ArgumentParser(description="General offline systems engineering workbench")
    parser.add_argument("--repo", type=Path, default=Path.cwd(),
                        help="Existing project checkout for optional legacy references (no writes)")
    parser.add_argument("--data", type=Path, default=default_data(), help="SQLite workspace file")
    parser.add_argument("--port", type=int, default=PORT)
    args = parser.parse_args(argv)
    if not 1 <= args.port <= 65535:
        parser.error("Invalid port")
    store = Store(args.data)
    server = ThreadingHTTPServer(("127.0.0.1", args.port),
                                 handler_factory(store, secrets.token_urlsafe(32)))
    print("Engineering Workbench: http://127.0.0.1:%d/" % args.port, flush=True)
    print("Data: " + str(store.path), flush=True)
    print("Press Ctrl+C to stop. This server never approves automatically.", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
