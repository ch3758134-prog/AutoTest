"""Smoke tests for the writable localhost API, host restrictions and request token."""
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from http.server import ThreadingHTTPServer

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from studio_store import Store
from studio_server import handler_factory


class StudioApiTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store = Store(Path(self.tmp.name) / "local.sqlite3")
        self.server = ThreadingHTTPServer(
            ("127.0.0.1", 0), handler_factory(self.store, "local-test-token",
                                                 manager_token="manager-secret-token-0123456789-0123456789"))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.url = "http://127.0.0.1:" + str(self.server.server_port)

    def request(self, path, data=None, *, token=None, origin=None, manager_token=None):
        headers = {}
        body = None
        if data is not None:
            headers["Content-Type"] = "application/json"
            body = json.dumps(data).encode("utf-8")
            if token is not None:
                headers["X-Local-Token"] = token
            if manager_token is not None:
                headers["X-Manager-Token"] = manager_token
            if origin:
                headers["Origin"] = origin
        req = Request(self.url + path, data=body, headers=headers)
        with urlopen(req, timeout=5) as response:
            return response.status, json.loads(response.read().decode("utf-8"))

    def test_ai_knowledge_is_candidate_and_cannot_approve_or_link_confirm(self):
        _,project=self.request("/api/studio/project",{
            "name":"AutoTest Knowledge","summary":"Legacy product text",
            "body":"Current R1 engineering data"},token="local-test-token")
        pid=project["project"]["id"]
        record={"title":"Product boundary correction",
            "statement":"AutoTest includes main board and key simulator",
            "interpretation":"System is not software alone; user claim requires system design review",
            "concept_ids":["product_identity","system_boundary"],
            "source_refs":["discussion://current-user-correction"],
            "related_objects":[],"provenance":"USER_INPUT"}
        manager="manager-secret-token-0123456789-0123456789"
        _,a=self.request("/api/studio/knowledge/propose",{
            "project_id":pid,"entry":record,"author":"AI manager"},manager_token=manager)
        self.assertEqual(a["state"],"CANDIDATE")
        _,ledger=self.request("/api/studio/knowledge?project="+pid)
        self.assertEqual(len(ledger["items"]),1)
        self.assertEqual(ledger["items"][0]["origin_role"],"AI_CANDIDATE")
        _,brief=self.request("/api/studio/management/context?project="+pid)
        self.assertEqual(len(brief["engineering_knowledge"]["items"]),1)
        with self.assertRaises(HTTPError) as denied:
            self.request("/api/studio/knowledge/review",{
                "id":a["id"],"expected_revision":1,"decision":"ACCEPT",
                "reviewer":"AI","reason":"Self-approved"},manager_token=manager)
        self.assertEqual(denied.exception.code,403)
        _,b=self.request("/api/studio/knowledge/review",{
            "id":a["id"],"expected_revision":1,"decision":"ACCEPT",
            "reviewer":"Human owner","reason":"Verified user correction, not physical function"},
            token="local-test-token")
        self.assertEqual(b["state"],"ACCEPTED")
        _,governance=self.request("/api/studio/governance/status?project="+pid)
        self.assertEqual(governance["baseline_status"],"UNBASELINED")
        with urlopen(self.url+"/knowledge.js",timeout=5) as response:
            self.assertIn(b'knowledgeWorkbench',response.read())

    def test_manager_restricted_token_can_propose_but_cannot_approve(self):
        _, project = self.request("/api/studio/project",
                                  {"name":"Embedded project"},token="local-test-token")
        project_id=project["project"]["id"]
        token="manager-secret-token-0123456789-0123456789"
        _, brief=self.request("/api/studio/management/context?project="+project_id)
        self.assertEqual(brief["project"]["id"],project_id)
        _, proposals=self.request("/api/studio/management/propose",{
            "project_id":project_id,"category":"TECHNICAL_RESEARCH","priority":"P1",
            "title":"Inspect timing","rationale":"Time budget is unknown",
            "next_action":"Design and authorize measurements",
            "source_objects":[],"proposed_by":"AI-MANAGER"
        },manager_token=token)
        self.assertEqual(proposals[0]["status"],"PENDING")
        with self.assertRaises(HTTPError) as ex:
            self.request("/api/studio/object",{
                "project_id":project_id,"kind":"requirement","title":"Unauthorised",
                "body":"invalid"
            },manager_token=token)
        self.assertEqual(ex.exception.code,403)
        with self.assertRaises(HTTPError) as ex:
            self.request("/api/studio/management/review",{
                "id":proposals[0]["id"],"decision":"ACCEPT_FOR_PLANNING",
                "reviewer":"AI","note":"not allowed"
            },manager_token=token)
        self.assertEqual(ex.exception.code,403)
        _, after=self.request("/api/studio/management/review",{
                "id":proposals[0]["id"],"decision":"ACCEPT_FOR_PLANNING",
                "reviewer":"Human owner","note":"Discuss next design steps"
            },token="local-test-token")
        self.assertEqual(after[0]["status"],"ACCEPT_FOR_PLANNING")
        with urlopen(self.url+"/management.js",timeout=5) as response:
            self.assertEqual(response.status,200)
            self.assertIn(b'renderManagementView',response.read())

    def test_version_endpoint_identifies_editable_studio(self):
        status, version = self.request("/api/studio/version")
        self.assertEqual(status, 200)
        self.assertEqual(version["version"], "1.0")
        self.assertEqual(version["mode"], "editable")
        self.assertFalse(version["legacy"])

    def test_bootstrap_no_credentials_and_empty_projects(self):
        status, boot = self.request("/api/studio/bootstrap")
        self.assertEqual(status, 200)
        self.assertEqual(boot["projects"], [])
        self.assertEqual(boot["csrf"], "local-test-token")

    def test_write_requires_token(self):
        with self.assertRaises(HTTPError) as ex:
            self.request("/api/studio/project", {"name": "Unsafe"})
        self.assertEqual(ex.exception.code, 403)

    def test_cross_origin_rejected(self):
        with self.assertRaises(HTTPError) as ex:
            self.request("/api/studio/project", {"name": "Bad"},
                         token="local-test-token", origin="http://malicious.invalid")
        self.assertEqual(ex.exception.code, 403)

    def test_real_edit_flow_over_http(self):
        status, project = self.request("/api/studio/project",
                                       {"name":"Bicycle System","summary":"Cargo bikes"},
                                       token="local-test-token")
        self.assertEqual(status, 200)
        key = project["project"]["id"]
        status, obj = self.request("/api/studio/object", {
            "project_id":key, "kind":"requirement", "title":"Battery monitoring",
            "body":"Must detect undervoltage", "meta":{"acceptance":"unit tests"}},
            token="local-test-token")
        self.assertEqual(status, 200)
        rev = obj["item"]["revision"]
        self.assertEqual(rev, 1)
        status, updated = self.request("/api/studio/object/"+obj["item"]["id"]+"/update", {
            "expected_revision":rev,"body":"Must report undervoltage and retry"},
            token="local-test-token")
        self.assertEqual(updated["item"]["revision"], 2)
        status, approved = self.request("/api/studio/object/"+obj["item"]["id"]+"/approve", {
            "expected_revision":2,"actor":"Designer","note":"Scenario reviewed"},
            token="local-test-token")
        self.assertEqual(approved["item"]["state"], "APPROVED")
        status, result = self.request("/api/studio/project?id="+key)
        self.assertEqual(result["counts"]["requirement"], 1)
        self.assertEqual(result["objects"][0]["revision"], 3)
        status, backup = self.request("/api/studio/export?project="+key)
        self.assertEqual(backup["schema"], "engineering-workbench/1")
        self.assertEqual(len(backup["revisions"]), 3)

    def test_ai_can_suggest_definition_but_only_owner_can_start_governed_research(self):
        _,project=self.request("/api/studio/project",{
            "name":"Generic Device","summary":"Current user test environment",
            "body":"Engineering users create test projects and verify real outputs",
            "version":"R1","variant":"generic"},token="local-test-token")
        pid=project["project"]["id"]
        _,state=self.request("/api/studio/governance/status?project="+pid)
        proposed=dict(state["definition"])
        proposed["summary"]="Integrated engineering IDE and test operations"
        payload={"project_id":pid,"proposed":proposed,
             "reason":"AI noticed missing engineering workflow boundaries",
             "source_refs":["review://product-analysis"],"proposer":"AI-manager",
             "expected_digest":state["definition_digest"]}
        _,candidate=self.request("/api/studio/governance/suggest",payload,
             manager_token="manager-secret-token-0123456789-0123456789")
        self.assertEqual(candidate["status"],"PENDING")
        _,before=self.request("/api/studio/governance/status?project="+pid)
        self.assertEqual(before["changes"],[])
        self.assertEqual(before["definition"],state["definition"])
        _,inbox=self.request("/api/studio/governance/suggestions?project="+pid)
        self.assertEqual(inbox[0]["origin_role"],"AI_CANDIDATE")
        with self.assertRaises(HTTPError) as denied:
            self.request("/api/studio/governance/suggest/accept",{
                "id":candidate["id"],"reviewer":"AI",
                "rationale":"Unsafe self acceptance",
                "expected_base_digest":state["definition_digest"]},
                manager_token="manager-secret-token-0123456789-0123456789")
        self.assertEqual(denied.exception.code,403)
        with self.assertRaises(HTTPError) as denied:
            self.request("/api/studio/governance/publish",{
                "change_id":"none","reviewer":"AI","decision_note":"Unsafe",
                "expected_base_digest":state["definition_digest"]},
                manager_token="manager-secret-token-0123456789-0123456789")
        self.assertEqual(denied.exception.code,403)
        _,accepted=self.request("/api/studio/governance/suggest/accept",{
            "id":candidate["id"],"reviewer":"Human owner",
            "rationale":"Research is warranted, not yet approved",
            "expected_base_digest":state["definition_digest"]},
            token="local-test-token")
        self.assertEqual(accepted["state"],"ACCEPTED_FOR_RESEARCH")
        _,research=self.request("/api/studio/governance/research?change="+accepted["change_id"])
        self.assertEqual(len(research["tasks"]),10)
        _,after=self.request("/api/studio/governance/status?project="+pid)
        self.assertEqual(after["definition"],state["definition"])
        with urlopen(self.url+"/definition-suggestions.js",timeout=5) as response:
            self.assertIn(b'definitionSuggestionInbox',response.read())

    def test_semantic_context_can_be_read_and_scope_cannot_be_set_by_ai(self):
        _,guide=self.request("/api/studio/semantics?focus=product_identity,release_scope")
        self.assertEqual([x["id"] for x in guide["concepts"]],
                         ["product_identity","release_scope"])
        self.assertTrue(guide["review_questions"])
        _,project=self.request("/api/studio/project",{
            "name":"Engineering","summary":"Generic ECU system",
            "body":"Current work includes hardware and firmware"},token="local-test-token")
        pid=project["project"]["id"]
        _,state=self.request("/api/studio/governance/status?project="+pid)
        suggested=dict(state["definition"])
        suggested["summary"]="Stimulus and response engineering test system"
        _,change=self.request("/api/studio/governance/propose",{
            "project_id":pid,"proposed":suggested,
            "reason":"Correct a semantic definition that ignored the hardware",
            "source_refs":["discussion://user-correction"],
            "proposer":"Owner","expected_digest":state["definition_digest"]},
            token="local-test-token")
        _,tasks=self.request("/api/studio/governance/research?change="+change["change"]["id"])
        self.assertEqual(sum(x["state"]=="SUGGESTED" for x in tasks["tasks"]),9)
        arch=next(x for x in tasks["tasks"] if x["domain"]=="ARCHITECTURE")
        with self.assertRaises(HTTPError) as denied:
            self.request("/api/studio/governance/research/activate",{
                "task_id":arch["id"],"expected_revision":arch["revision"],
                "reviewer":"AI","rationale":"Self-activation"},
                manager_token="manager-secret-token-0123456789-0123456789")
        self.assertEqual(denied.exception.code,403)
        _,active=self.request("/api/studio/governance/research/activate",{
            "task_id":arch["id"],"expected_revision":arch["revision"],
            "reviewer":"Owner","rationale":"Board responsibilities must be reconsidered"},
            token="local-test-token")
        self.assertEqual(active["state"],"OPEN")
        with urlopen(self.url+"/semantics.js",timeout=5) as response:
            self.assertIn(b'engineeringSemanticsModal',response.read())

    def test_product_definition_requires_owner_review_and_domain_gate(self):
        _,project=self.request("/api/studio/project",{
            "name":"Generic Device","summary":"Legacy unreviewed description",
            "body":"Imported description of a user-facing engineering product",
            "version":"R0","variant":"A"},token="local-test-token")
        pid=project["project"]["id"]
        _,status=self.request("/api/studio/governance/status?project="+pid)
        self.assertEqual(status["baseline_status"],"UNBASELINED")
        with self.assertRaises(HTTPError) as forbidden:
            self.request("/api/studio/project/update",{
                "id":pid,"summary":"Try bypassing governance"},token="local-test-token")
        self.assertEqual(forbidden.exception.code,400)
        proposal=dict(status["definition"])
        proposal["summary"]="Controlled engineering product definition"
        payload={"project_id":pid,"proposed":proposal,
                 "reason":"Product positioning requires full impact analysis",
                 "source_refs":["discussion://product-definition"],
                 "proposer":"Human","expected_digest":status["definition_digest"]}
        with self.assertRaises(HTTPError) as forbidden:
            self.request("/api/studio/governance/propose",payload,
                         manager_token="manager-secret-token-0123456789-0123456789")
        self.assertEqual(forbidden.exception.code,403)
        _,created=self.request("/api/studio/governance/propose",payload,
                              token="local-test-token")
        change=created["change"]["id"]
        self.assertFalse(created["gate"]["ready"])
        with self.assertRaises(HTTPError) as denied:
            self.request("/api/studio/governance/publish",{
                "change_id":change,"reviewer":"Owner","decision_note":"Early",
                "expected_base_digest":status["definition_digest"]},
                token="local-test-token")
        self.assertEqual(denied.exception.code,400)
        _,queue=self.request("/api/studio/governance/research?change="+change)
        self.assertEqual(len(queue["tasks"]),10)
        for task in queue["tasks"]:
            _,submission=self.request("/api/studio/governance/research/submit",{
                "task_id":task["id"],"expected_revision":task["revision"],
                "finding":"Assessed impact on "+task["domain"],
                "evidence_refs":["test://source/"+task["domain"]],
                "limitations":"The fixtures use no real hardware",
                "author":"AI Analyst"},manager_token="manager-secret-token-0123456789-0123456789")
            self.assertEqual(submission["origin_role"],"AI_CANDIDATE")
            with self.assertRaises(HTTPError) as denied:
                self.request("/api/studio/governance/research/review",{
                    "task_id":task["id"],"submission_id":submission["submission_id"],
                    "expected_revision":task["revision"],"decision":"ACCEPT",
                    "reviewer":"AI","rationale":"Self approval"},
                    manager_token="manager-secret-token-0123456789-0123456789")
            self.assertEqual(denied.exception.code,403)
            self.request("/api/studio/governance/research/review",{
                "task_id":task["id"],"submission_id":submission["submission_id"],
                "expected_revision":task["revision"],"decision":"ACCEPT",
                "reviewer":"Human","rationale":"Test-only evidence reviewed"},
                token="local-test-token")
        for domain in status["domains"]:
            self.request("/api/studio/governance/review",{
                "change_id":change,"domain":domain["key"],"outcome":"NO_IMPACT",
                "rationale":"Explicitly assessed applicability and scope",
                "evidence_refs":["study://review/"+domain["key"]],
                "affected_ids":[],"reviewer":"Owner",
                "expected_base_digest":status["definition_digest"]
            },token="local-test-token")
        _,gate=self.request("/api/studio/governance/gate?id="+change)
        self.assertTrue(gate["ready"])
        _,published=self.request("/api/studio/governance/publish",{
            "change_id":change,"reviewer":"Owner","decision_note":"Domain review accepted",
            "expected_base_digest":status["definition_digest"]},
            token="local-test-token")
        self.assertEqual(published["revision"],1)
        _,after=self.request("/api/studio/governance/status?project="+pid)
        self.assertEqual(after["baseline_status"],"BASELINED")
        self.assertEqual(after["definition"]["summary"],proposal["summary"])
        with urlopen(self.url+"/governance.js",timeout=5) as response:
            self.assertIn(b'definitionGovernanceModal',response.read())

    def test_execution_api_requires_real_project_and_exposes_worker_ui(self):
        with self.assertRaises(HTTPError) as error:
            self.request("/api/studio/execution?project=missing")
        self.assertEqual(error.exception.code,400)
        _,project=self.request("/api/studio/project",{"name":"Workspace"},
                               token="local-test-token")
        key=project["project"]["id"]
        _,empty=self.request("/api/studio/execution?project="+key)
        self.assertFalse(empty["configured"])
        self.assertIsNone(empty["snapshot"])
        _,configured=self.request("/api/studio/execution/configure",{
            "project_id":key,"repository":"ch3758134-prog/AutoTest"},
            token="local-test-token")
        self.assertEqual(configured["repository"],"ch3758134-prog/AutoTest")
        with self.assertRaises(HTTPError) as forbidden:
            self.request("/api/studio/execution/refresh",{"project_id":key})
        self.assertEqual(forbidden.exception.code,403)
        with urlopen(self.url+"/execution.js",timeout=5) as response:
            self.assertEqual(response.status,200)
            self.assertIn(b'renderExecutionView',response.read())

    def test_research_workflow_via_local_api(self):
        _, project = self.request("/api/studio/project", {"name":"Embedded System"},
                                  token="local-test-token")
        key = project["project"]["id"]
        _, research = self.request("/api/studio/research/create", {
            "project_id":key, "title":"CAN sharing feasibility",
            "question":"Will real bus consumer receive frames consistently?",
            "constraints":"one physical CAN path", "minimum_level":"REAL"},
            token="local-test-token")
        study = research["study"]["id"]
        _, option = self.request("/api/studio/research/option", {
            "study_id":study, "title":"One owner hub", "approach":"single receiver",
            "tradeoffs":"shared buffering"}, token="local-test-token")
        option_id=option["options"][0]["id"]
        _, planned = self.request("/api/studio/research/experiment", {
            "study_id":study,"option_id":option_id,"title":"bench trial",
            "method":"record real bus frames","success_criteria":"all frames correlated",
            "environment":"USB controller / firmware 1","level":"REAL","required":True},
            token="local-test-token")
        experiment_id=planned["experiments"][0]["id"]
        with self.assertRaises(HTTPError) as denied:
            self.request("/api/studio/research/result", {
                "experiment_id":experiment_id,"outcome":"PASS",
                "observations":"claimed success","raw_reference":"not-real.log",
                "configuration":"dev","source_revision":"v1","performed_by":"AI"},
                token="local-test-token")
        self.assertEqual(denied.exception.code,400)
        self.request("/api/studio/research/authorize", {
            "experiment_id":experiment_id,"actor":"Owner","note":"safe bench authorized"},
            token="local-test-token")
        _, recorded = self.request("/api/studio/research/result", {
            "experiment_id":experiment_id, "outcome":"PASS",
            "observations":"timestamped data confirmed","raw_reference":"trace-1.zip",
            "configuration":"device A / firmware 1", "source_revision":"abc123",
            "performed_by":"Tester"}, token="local-test-token")
        result_id=recorded["experiments"][0]["results"][0]["id"]
        self.request("/api/studio/research/review", {
            "result_id":result_id,"actor":"Owner","note":"inspected raw trace"},
            token="local-test-token")
        _, gate=self.request("/api/studio/research/evaluate?study="+study+"&option="+option_id)
        self.assertTrue(gate["ready"])
        _, decided=self.request("/api/studio/research/decide", {
            "study_id":study,"option_id":option_id,"approver":"Owner",
            "rationale":"real frames confirmed","restrictions":"only tested firmware"},
            token="local-test-token")
        self.assertEqual(decided["study"]["status"],"DECIDED")
        _, listing=self.request("/api/studio/research?project="+key)
        self.assertEqual(listing[0]["reviewed_evidence"],1)
        with urlopen(self.url+"/research.js",timeout=5) as response:
            self.assertEqual(response.status,200)
            self.assertIn(b'renderResearchView',response.read())

    def test_unsupported_methods_blocked(self):
        req = Request(self.url + "/api/studio/object", method="PUT", data=b'{}')
        with self.assertRaises(HTTPError) as ex:
            urlopen(req,timeout=5)
        self.assertEqual(ex.exception.code,501)


if __name__ == "__main__":
    unittest.main()
