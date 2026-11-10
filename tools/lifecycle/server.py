#!/usr/bin/env python3
"""Read-only, localhost-only UI for the AutoTest engineering lifecycle pilot."""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

from lifecycle import ModelError, Workspace, read_json
from catalog import catalog, document, evidence

UI = Path(__file__).with_name('ui.html')


def make_handler(workspace: Workspace):
    class Handler(BaseHTTPRequestHandler):
        server_version = 'AutoTestLifecycle/0.1'

        def write(self, status: int, body: bytes, kind: str) -> None:
            self.send_response(status)
            self.send_header('Content-Type', kind)
            self.send_header('Content-Length', str(len(body)))
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'")
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:
            url = urlsplit(self.path)
            params = parse_qs(url.query)
            try:
                if url.path in {'/', '/index.html'}:
                    body = UI.read_bytes()
                    return self.write(200, body, 'text/html; charset=utf-8')
                if url.path == '/api/catalog':
                    response = catalog(workspace)
                elif url.path == '/api/document':
                    response = document(workspace, params.get('id', [''])[0])
                elif url.path == '/api/evidence':
                    response = evidence(workspace)
                elif url.path == '/api/overview':
                    nodes = [{**workspace.source(k), 'description': v.get('description', '')}
                             for k, v in workspace.nodes.items()]
                    response = {'schema': workspace.manifest['schema'], 'nodes': nodes,
                                'edges': workspace.manifest['edges'], 'changes': workspace.manifest.get('changes', []),
                                'gate_states': workspace.inspect()['historical_gate_states']}
                elif url.path == '/api/impact':
                    response = workspace.impact(params.get('node', [''])[0])
                elif url.path == '/api/readiness':
                    response = workspace.readiness(params.get('package', [''])[0])
                elif url.path == '/api/node':
                    node = params.get('id', [''])[0]
                    if node not in workspace.nodes:
                        raise ModelError(f'Unknown node: {node}')
                    response = {**workspace.source(node), 'description': workspace.nodes[node].get('description', '')}
                else:
                    return self.write(404, b'Not found', 'text/plain; charset=utf-8')
                body = json.dumps(response, ensure_ascii=False).encode('utf-8')
                return self.write(200, body, 'application/json; charset=utf-8')
            except (ModelError, OSError, ValueError) as error:
                body = json.dumps({'error': str(error)}, ensure_ascii=False).encode('utf-8')
                return self.write(400, body, 'application/json; charset=utf-8')

        def do_POST(self) -> None:
            self.write(405, b'Read-only workbench', 'text/plain; charset=utf-8')

    return Handler


def main() -> int:
    parser = argparse.ArgumentParser(description='Read-only local AutoTest lifecycle workbench')
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    parser.add_argument('--model', type=Path, default=Path(__file__).parent / 'data/keysim-trace.json')
    parser.add_argument('--port', type=int, default=8765)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('port must be 1–65535')
    workspace = Workspace(args.repo, read_json(args.model))
    server = ThreadingHTTPServer(('127.0.0.1', args.port), make_handler(workspace))
    print(f'AutoTest Lifecycle: http://127.0.0.1:{args.port}/ (local read-only; Ctrl+C to stop)', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
