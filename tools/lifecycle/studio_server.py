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
from studio_import import preview as saef_preview, import_saef
from studio_research import Research
from studio_execution import Execution
from studio_management import Management
from studio_baseline import GitBaseline
from studio_change import ChangeReview
from studio_verification import Verification
from studio_governance import DefinitionGovernance
from studio_definition_research import DefinitionResearch
from studio_definition_suggestions import DefinitionSuggestions
from studio_semantics import semantic_context
from studio_knowledge import EngineeringKnowledge
from studio_restore import preview as restore_preview, restore_to_empty

HERE = Path(__file__).resolve().parent
WEB = HERE / "studio.html"
PORT = 8765


def handler_factory(store, token, repo=None, manager_token=None):
    source_root = Path(repo).resolve() if repo else Path.cwd()
    research = Research(store)
    execution = Execution(store)
    management = Management(store, execution, research)
    baseline = GitBaseline(store, source_root, execution)
    change_review = ChangeReview(store)
    verification = Verification(store)
    governance = DefinitionGovernance(store)
    definition_research = DefinitionResearch(store)
    definition_suggestions = DefinitionSuggestions(store)
    knowledge = EngineeringKnowledge(store)
    class Handler(BaseHTTPRequestHandler):
        server_version = "EngineeringWorkbench/1.0"

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
                if path in ("/", "/index.html", "/**"):
                    return self.write(WEB.read_bytes(), content_type="text/html; charset=utf-8")
                if path == "/research.js":
                    return self.write((HERE / "studio_research_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/execution.js":
                    return self.write((HERE / "studio_execution_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/management.js":
                    return self.write((HERE / "studio_management_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/capability.js":
                    return self.write((HERE / "studio_capability_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/change.js":
                    return self.write((HERE / "studio_change_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/verification.js":
                    return self.write((HERE / "studio_verification_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/plan.js":
                    return self.write((HERE / "studio_plan_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/governance.js":
                    return self.write((HERE / "studio_governance_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/definition-research.js":
                    return self.write((HERE / "studio_definition_research_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/definition-suggestions.js":
                    return self.write((HERE / "studio_definition_suggestions_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/semantics.js":
                    return self.write((HERE / "studio_semantics_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/knowledge.js":
                    return self.write((HERE / "studio_knowledge_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/restore.js":
                    return self.write((HERE / "studio_restore_ui.js").read_bytes(),
                                      content_type="application/javascript; charset=utf-8")
                if path == "/api/studio/semantics":
                    return self.write(semantic_context(params.get("focus", "")))
                if path == "/api/studio/version":
                    return self.write({"application": "Engineering Workbench",
                                       "version": "1.0", "mode": "editable",
                                       "legacy": False})
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
                if path == "/api/studio/change/review":
                    return self.write(change_review.detail(params.get("id", "")))
                if path == "/api/studio/verification":
                    return self.write(verification.list(params.get("project", "")))
                if path == "/api/studio/governance/status":
                    return self.write(governance.status(params.get("project", "")))
                if path == "/api/studio/governance/detail":
                    return self.write(governance.detail(params.get("id", "")))
                if path == "/api/studio/governance/research":
                    return self.write(definition_research.list(params.get("change", "")))
                if path == "/api/studio/governance/suggestions":
                    return self.write(definition_suggestions.list(params.get("project", "")))
                if path == "/api/studio/governance/gate":
                    return self.write(governance.gate(params.get("id", "")))
                if path == "/api/studio/governance/invalidations":
                    return self.write(governance.invalidations(params.get("project", "")))
                if path == "/api/studio/readiness":
                    return self.write(store.readiness(params.get("id", "")))
                if path == "/api/studio/context":
                    return self.write(store.context(params.get("id", "")))
                if path == "/api/studio/baseline/preview":
                    return self.write(baseline.preview(params.get("project", ""),
                                                       params.get("revision", "HEAD")))
                if path == "/api/studio/knowledge":
                    return self.write(knowledge.list(params.get("project","")))
                if path == "/api/studio/management/context":
                    return self.write(management.context(params.get("project", "")))
                if path == "/api/studio/management/proposals":
                    return self.write(management.list(params.get("project", "")))
                if path == "/api/studio/execution":
                    return self.write(execution.overview(params.get("project", "")))
                if path == "/api/studio/execution/queue":
                    return self.write(execution.queue(params.get("project", ""),
                                                      params.get("role", "")))
                if path == "/api/studio/execution/packet":
                    return self.write(execution.packet(params.get("id", "")))
                if path == "/api/studio/research":
                    return self.write(research.list(params.get("project", "")))
                if path == "/api/studio/research/detail":
                    return self.write(research.detail(params.get("id", "")))
                if path == "/api/studio/research/evaluate":
                    return self.write(research.evaluate(params.get("study", ""),
                                                        params.get("option", "")))
                if path == "/api/studio/saef-preview":
                    return self.write(saef_preview(source_root, store, params.get("project", "")))
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
            if not self.host_ok():
                return self.write({"error": "Invalid local host"}, 403)
            request_path, _ = self.route()
            owner_request = secrets.compare_digest(self.headers.get("X-Local-Token", ""), token)
            manager_request = (request_path in (
                                   "/api/studio/management/propose",
                                   "/api/studio/governance/research/submit",
                                   "/api/studio/governance/suggest",
                                   "/api/studio/knowledge/intake",
                                   "/api/studio/knowledge/propose",
                                   "/api/studio/knowledge/revise",
                                   "/api/studio/knowledge/link/propose")
                               and manager_token is not None and
                               secrets.compare_digest(self.headers.get("X-Manager-Token", ""),
                                                      manager_token))
            if not (owner_request or manager_request):
                return self.write({"error": "Write permission denied for this role"}, 403)
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
                max_size = 12_500_000 if request_path.startswith("/api/studio/restore/") else 250000
                if size < 1 or size > max_size:
                    return self.write({"error": "Invalid request length"}, 413)
                data = json.loads(self.rfile.read(size).decode("utf-8"))
                if not isinstance(data, dict):
                    raise RuleError("Request body must be an object")
                path, params = self.route()
                pieces = path.split("/")
                if path == "/api/studio/restore/preview":
                    if not owner_request:
                        return self.write({"error":"Only owner may preview project recovery"},403)
                    answer = restore_preview(data.get("archive"))
                elif path == "/api/studio/restore/apply":
                    if not owner_request:
                        return self.write({"error":"Only owner may restore workspaces"},403)
                    if data.get("confirmation")!="RESTORE_TO_EMPTY_WORKSPACE":
                        raise RuleError("Explicit empty-workspace recovery confirmation required")
                    result = restore_to_empty(data.get("archive"),store.path)
                    answer = {"restored_project_id":result["project"]["id"],
                              "warning":"Historical approvals/evidence restored as records, not revalidated."}
                elif path == "/api/studio/management/propose":
                    answer = management.propose(data)
                elif path == "/api/studio/management/review":
                    if not owner_request:
                        return self.write({"error":"Only owner may review proposals"},403)
                    answer = management.review(data.get("id", ""),data.get("decision", ""),
                                               data.get("reviewer", ""),data.get("note", ""))
                elif path == "/api/studio/project":
                    answer = store.create_project(data)
                elif path == "/api/studio/project/update":
                    answer = store.update_project(data.get("id", ""), data)
                elif path == "/api/studio/object":
                    answer = store.create_object(data)
                elif path == "/api/studio/relation":
                    answer = store.create_relation(data)
                elif path == "/api/studio/governance/propose":
                    if not owner_request:
                        return self.write({"error":"Only owner may register a product-definition proposal"},403)
                    answer = governance.propose(data.get("project_id",""),
                        data.get("proposed"),data.get("reason",""),
                        data.get("source_refs"),data.get("proposer",""),
                        data.get("expected_digest",""))
                elif path == "/api/studio/knowledge/intake":
                    answer = knowledge.intake(
                        data.get("project_id",""),data.get("bundle"),
                        data.get("author",""),
                        "OWNER_CANDIDATE" if owner_request else "AI_CANDIDATE")
                elif path == "/api/studio/knowledge/propose":
                    answer = knowledge.propose(
                        data.get("project_id",""),data.get("entry"),
                        data.get("author",""),
                        "OWNER_CANDIDATE" if owner_request else "AI_CANDIDATE")
                elif path == "/api/studio/knowledge/revise":
                    answer = knowledge.revise(
                        data.get("id",""),data.get("expected_revision"),
                        data.get("entry"),data.get("author",""),
                        "OWNER_CANDIDATE" if owner_request else "AI_CANDIDATE")
                elif path == "/api/studio/knowledge/review":
                    if not owner_request:
                        return self.write({"error":"Only owner may review knowledge"},403)
                    answer = knowledge.review(
                        data.get("id",""),data.get("expected_revision"),
                        data.get("decision",""),data.get("reviewer",""),
                        data.get("reason",""))
                elif path == "/api/studio/knowledge/link/propose":
                    answer = knowledge.propose_link(
                        data.get("project_id",""),data.get("source_id",""),
                        data.get("target_id",""),data.get("relation",""),
                        data.get("rationale",""),data.get("author",""),
                        "OWNER_CANDIDATE" if owner_request else "AI_CANDIDATE")
                elif path == "/api/studio/knowledge/link/review":
                    if not owner_request:
                        return self.write({"error":"Only owner may review knowledge links"},403)
                    answer = knowledge.review_link(
                        data.get("id",""),data.get("decision",""),
                        data.get("reviewer",""),data.get("reason",""))
                elif path == "/api/studio/governance/suggest":
                    answer = definition_suggestions.suggest(
                        data.get("project_id",""),data.get("proposed"),
                        data.get("reason",""),data.get("source_refs"),
                        data.get("proposer",""),
                        "OWNER_CANDIDATE" if owner_request else "AI_CANDIDATE",
                        data.get("expected_digest",""),
                        data.get("semantic_notes",""))
                elif path == "/api/studio/governance/suggest/accept":
                    if not owner_request:
                        return self.write({"error":"Only owner may accept product suggestions"},403)
                    answer = definition_suggestions.accept(
                        data.get("id",""),data.get("reviewer",""),
                        data.get("rationale",""),data.get("expected_base_digest",""))
                elif path == "/api/studio/governance/suggest/reject":
                    if not owner_request:
                        return self.write({"error":"Only owner may reject product suggestions"},403)
                    answer = definition_suggestions.reject(
                        data.get("id",""),data.get("reviewer",""),
                        data.get("rationale",""))
                elif path == "/api/studio/governance/research/activate":
                    if not owner_request:
                        return self.write({"error":"Only owner may activate a suggested research topic"},403)
                    answer = definition_research.activate(
                        data.get("task_id",""),data.get("expected_revision"),
                        data.get("reviewer",""),data.get("rationale",""))
                elif path == "/api/studio/governance/research/scope-out":
                    if not owner_request:
                        return self.write({"error":"Only owner may decide research scope"},403)
                    answer = definition_research.set_not_applicable(
                        data.get("task_id",""),data.get("expected_revision"),
                        data.get("reviewer",""),data.get("rationale",""),
                        data.get("evidence_refs"))
                elif path == "/api/studio/governance/research/add-topic":
                    if not owner_request:
                        return self.write({"error":"Only owner may extend research scope"},403)
                    answer = definition_research.add_topic(
                        data.get("change_id",""),data.get("question",""),
                        data.get("reviewer",""),data.get("rationale",""))
                elif path == "/api/studio/governance/research/submit":
                    answer = definition_research.submit(
                        data.get("task_id",""),data.get("expected_revision"),
                        data.get("author",""),
                        "OWNER_SUBMISSION" if owner_request else "AI_CANDIDATE",
                        data.get("finding",""),data.get("evidence_refs"),
                        data.get("limitations",""),data.get("linked_study_id"))
                elif path == "/api/studio/governance/research/review":
                    if not owner_request:
                        return self.write({"error":"Only owner may review definition research"},403)
                    answer = definition_research.review(
                        data.get("task_id",""),data.get("submission_id",""),
                        data.get("expected_revision"),data.get("decision",""),
                        data.get("reviewer",""),data.get("rationale",""))
                elif path == "/api/studio/governance/research/reopen":
                    if not owner_request:
                        return self.write({"error":"Only owner may reopen definition research"},403)
                    answer = definition_research.reopen(
                        data.get("task_id",""),data.get("expected_revision"),
                        data.get("reviewer",""),data.get("rationale",""))
                elif path == "/api/studio/governance/review":
                    if not owner_request:
                        return self.write({"error":"Only owner may submit impact-domain review"},403)
                    answer = governance.review(data.get("change_id",""),
                        data.get("domain",""),data.get("outcome",""),
                        data.get("rationale",""),data.get("evidence_refs"),
                        data.get("affected_ids"),data.get("reviewer",""),
                        data.get("expected_base_digest",""))
                elif path == "/api/studio/governance/review-package":
                    if not owner_request:
                        return self.write({"error":"Only owner may assess implementation impact"},403)
                    answer = governance.review_package(
                        data.get("change_id",""),data.get("package_id",""),
                        data.get("outcome",""),data.get("rationale",""),
                        data.get("evidence_refs"),data.get("reviewer",""),
                        data.get("expected_base_digest",""),
                        data.get("expected_package_revision"))
                elif path == "/api/studio/governance/publish":
                    if not owner_request:
                        return self.write({"error":"Only owner may publish product definition"},403)
                    answer = governance.publish(data.get("change_id",""),
                        data.get("reviewer",""),data.get("decision_note",""),
                        data.get("expected_base_digest",""))
                elif path == "/api/studio/governance/reject":
                    if not owner_request:
                        return self.write({"error":"Only owner may reject product changes"},403)
                    answer = governance.reject(data.get("change_id",""),
                        data.get("reviewer",""),data.get("reason",""))
                elif path == "/api/studio/governance/resolve":
                    if not owner_request:
                        return self.write({"error":"Only owner may revalidate a work package"},403)
                    answer = governance.resolve(data.get("package_id",""),
                        data.get("change_id",""),data.get("expected_package_revision"),
                        data.get("reviewer",""),data.get("rationale",""))
                elif path == "/api/studio/verification/record":
                    if not owner_request:
                        return self.write({"error":"Only owner may record execution evidence"},403)
                    answer = verification.record(data)
                elif path == "/api/studio/verification/review":
                    if not owner_request:
                        return self.write({"error":"Only owner may review execution evidence"},403)
                    answer = verification.review(
                        data.get("id",""),data.get("decision",""),
                        data.get("reviewer",""),data.get("rationale",""))
                elif path == "/api/studio/change/capture":
                    if not owner_request:
                        return self.write({"error":"Only owner may capture findings"},403)
                    answer = change_review.capture(
                        data.get("target_id",""),data.get("target_revision"),
                        data.get("title",""),data.get("body",""),
                        data.get("source",""),data.get("actor",""))
                elif path == "/api/studio/change/assess":
                    if not owner_request:
                        return self.write({"error":"Only owner may assess change impacts"},403)
                    answer = change_review.assess(
                        data.get("change_id",""),data.get("target_id",""),
                        data.get("expected_change_revision"),
                        data.get("expected_target_revision"),
                        data.get("outcome",""),data.get("reviewer",""),
                        data.get("rationale",""))
                elif path == "/api/studio/relation/delete":
                    answer = store.delete_relation(data.get("id", ""))
                elif path == "/api/studio/relation/approve":
                    answer = store.approve_relation(data.get("id", ""),
                                                    data.get("actor", ""), data.get("note", ""))
                elif path == "/api/studio/import/saef":
                    answer = import_saef(source_root, store, data.get("project_id", ""))
                elif path == "/api/studio/execution/configure":
                    answer = execution.configure(data.get("project_id", ""),
                                                 data.get("repository", ""))
                elif path == "/api/studio/execution/refresh":
                    answer = execution.refresh(data.get("project_id", ""))
                elif path == "/api/studio/execution/import-issue":
                    answer = execution.import_issue(data.get("project_id", ""),
                                                    data.get("issue_number"))
                elif path == "/api/studio/execution/bind":
                    answer = execution.bind(data.get("object_id", ""),
                                            data.get("issue_number"),
                                            data.get("agent_role", "UNASSIGNED"))
                elif path == "/api/studio/execution/link-pr":
                    answer = execution.link_pr(data.get("object_id", ""),
                                               data.get("pr_number"))
                elif path == "/api/studio/execution/dispatch":
                    answer = execution.dispatch(data.get("object_id", ""),
                                                data.get("actor", ""),
                                                data.get("note", ""))
                elif path == "/api/studio/execution/revoke":
                    answer = execution.revoke(data.get("object_id", ""),
                                              data.get("actor", ""),
                                              data.get("reason", ""))
                elif path == "/api/studio/execution/claim":
                    answer = execution.claim(data.get("object_id", ""),
                                             data.get("agent_role", ""),
                                             data.get("worker", ""))
                elif path == "/api/studio/execution/publish":
                    answer = execution.publish(data.get("object_id", ""),source_root)
                elif path == "/api/studio/execution/archive-project":
                    answer = execution.archive_project(data.get("project_id", ""),source_root,
                                                       data.get("actor", ""), data.get("note", ""))
                elif path == "/api/studio/research/create":
                    answer = research.create(data)
                elif path == "/api/studio/research/option":
                    answer = research.add_option(data.get("study_id", ""), data)
                elif path == "/api/studio/research/experiment":
                    answer = research.add_experiment(data.get("study_id", ""), data)
                elif path == "/api/studio/research/authorize":
                    answer = research.authorize(data.get("experiment_id", ""),
                                                data.get("actor", ""), data.get("note", ""))
                elif path == "/api/studio/research/result":
                    answer = research.record_result(data.get("experiment_id", ""), data)
                elif path == "/api/studio/research/review":
                    answer = research.review_result(data.get("result_id", ""),
                                                    data.get("actor", ""), data.get("note", ""))
                elif path == "/api/studio/research/decide":
                    answer = research.decide(data.get("study_id", ""), data)
                elif path == "/api/studio/research/reopen":
                    answer = research.reopen(data.get("study_id", ""), data)
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
    parser.add_argument("--manager-token-file", type=Path, default=None,
                        help="Existing secret file handed only to a restricted manager CLI. "
                             "Disabled when absent. Never commit token files.")
    args = parser.parse_args(argv)
    if not 1 <= args.port <= 65535:
        parser.error("Invalid port")
    store = Store(args.data)
    manager_token = None
    if args.manager_token_file:
        secret_path = args.manager_token_file.expanduser().resolve()
        if not secret_path.is_file():
            parser.error("Manager token file must already exist")
        manager_token = secret_path.read_text(encoding="utf-8").strip()
        if len(manager_token) < 32:
            parser.error("Manager token must be at least 32 characters")
    server = ThreadingHTTPServer(("127.0.0.1", args.port),
                                 handler_factory(store, secrets.token_urlsafe(32),
                                                 args.repo, manager_token))
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
