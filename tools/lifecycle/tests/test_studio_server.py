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
            ("127.0.0.1", 0), handler_factory(self.store, "local-test-token"))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.url = "http://127.0.0.1:" + str(self.server.server_port)

    def request(self, path, data=None, *, token=None, origin=None):
        headers = {}
        body = None
        if data is not None:
            headers["Content-Type"] = "application/json"
            body = json.dumps(data).encode("utf-8")
            if token is not None:
                headers["X-Local-Token"] = token
            if origin:
                headers["Origin"] = origin
        req = Request(self.url + path, data=body, headers=headers)
        with urlopen(req, timeout=5) as response:
            return response.status, json.loads(response.read().decode("utf-8"))

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
