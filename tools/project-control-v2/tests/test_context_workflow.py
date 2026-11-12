import copy
import importlib.util
import json
from pathlib import Path
import sys
import unittest

HERE = Path(__file__).resolve().parents[1] / "portable"
sys.path.insert(0, str(HERE))
spec = importlib.util.spec_from_file_location("context_workflow", HERE / "context_workflow.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def assessment(label="first"):
    return {
        "schema": "project-control-context-assessment/2", "role": "coordinator",
        "project_ref": "sample", "task_focus": "recover " + label,
        "source_revision": "some checked revision",
        "sources": [], "search_attempts": [], "claims": [],
        "topics": {x: {"status": "NOT_SEARCHED", "source_ids": [],
                       "claim_ids": [], "search_attempt_ids": [],
                       "action_ids": (["gap-a"] if x=="purpose" else []),
                       "limitation": "not yet searched"}
                   for x in ("purpose", "users_scenarios", "scope_constraints",
                             "history_decisions", "requirements_design",
                             "evidence_validation", "current_work_risks")},
        "proposed_actions": [{
            "id": "gap-a", "topic": "purpose", "kind": "RESEARCH_SOURCE",
            "reason": "need to investigate original charter",
            "expected_output": "source review", "status": "PROPOSED_NOT_AUTHORIZED"}],
        "next_disposition": "CONTINUE_DISCOVERY"
    }

def record():
    return {"project_id": "sample", "revision": 1,
            "deliverables": [{"id": "DOC-1", "title": "recovered product context"}],
            "work": [], "context_reviews": [], "context_action_links": []}

def submit(r, value):
    return m.apply(r, "record_context_assessment", {"assessment": value},
                   project_id="sample", repo_url="https://github.com/example/sample.git",
                   recorded_at="2026-11-12T00:00:00Z")

def link(r, review_id, kind="RESEARCH", action_id="gap-a", work_id="WORK-1"):
    return m.apply(r, "propose_context_remediation_work",
                   {"review_id": review_id, "action_id": action_id,
                    "work_id": work_id, "deliverable_id": "DOC-1",
                    "kind": kind, "title": "investigate product context",
                    "expected_result": "candidate reviewed sources"},
                   project_id="sample", repo_url="https://github.com/example/sample.git",
                   recorded_at="2026-11-12T00:00:00Z")

class ContextWorkflowTests(unittest.TestCase):
    def test_capture_candidate_and_no_authority(self):
        r = record()
        rv = submit(r, assessment())
        self.assertEqual(len(r["context_reviews"]), 1)
        self.assertEqual(r["work"], [])
        self.assertEqual(rv["status"], "CANDIDATE_NOT_APPROVED")
        self.assertFalse(m.current_view(r)["audit"]["authorization_granted"])

    def test_duplicate_review_rejected_without_change(self):
        r = record()
        submit(r, assessment())
        before = copy.deepcopy(r)
        with self.assertRaises(m.ContextWorkflowError):
            submit(r, assessment())
        self.assertEqual(r, before)

    def test_wrong_project_rejected(self):
        r = record()
        v = assessment()
        v["project_ref"] = "other/repo"
        with self.assertRaises(m.ContextWorkflowError):
            submit(r, v)
        self.assertFalse(r["context_reviews"])

    def test_proposed_work_links_and_no_execution(self):
        r = record()
        v = submit(r, assessment())
        link(r, v["review_id"])
        w = r["work"][0]
        self.assertEqual(w["state"], "PROPOSED")
        self.assertIs(w["execution_authorized"], False)
        self.assertEqual(w["context_action_id"], "gap-a")
        self.assertEqual(r["context_action_links"][0]["status"], "PROPOSED_NOT_AUTHORIZED")
        self.assertEqual(m.current_view(r)["action_links"][0]["work_id"], "WORK-1")

    def test_duplicate_action_rejected(self):
        r=record()
        v=submit(r,assessment())
        link(r,v["review_id"])
        with self.assertRaises(m.ContextWorkflowError):
            link(r,v["review_id"],work_id="WORK-2")
        self.assertEqual(len(r["work"]),1)

    def test_incompatible_specialty_rejected(self):
        r=record()
        v=submit(r,assessment())
        with self.assertRaises(m.ContextWorkflowError):
            link(r,v["review_id"],kind="IMPLEMENT")
        self.assertEqual(r["work"],[])

    def test_stale_revision_cannot_link(self):
        r=record()
        v=submit(r,assessment())
        link(r,v["review_id"])
        v2=submit(r,assessment("second"))
        self.assertEqual(r["work"][0]["context_evidence_status"], "STALE_AFTER_NEW_ASSESSMENT")
        with self.assertRaises(m.ContextWorkflowError):
            link(r,v["review_id"],work_id="WORK-2")
        self.assertNotEqual(v2["review_id"],v["review_id"])

    def test_missing_deliverable_refused(self):
        r=record()
        v=submit(r,assessment())
        r["deliverables"]=[]
        with self.assertRaises(m.ContextWorkflowError):
            link(r,v["review_id"])
        self.assertFalse(r["work"])

    def test_review_data_tamper_detected(self):
        r=record()
        submit(r,assessment())
        r["context_reviews"][0]["assessment"]["task_focus"]="silently overwritten"
        with self.assertRaises(m.ContextWorkflowError):
            m.check_collections(r)

    def test_no_update_of_old_project_state(self):
        r=record()
        original=copy.deepcopy(r)
        with self.assertRaises(m.ContextWorkflowError):
            link(r,"CTX-missing")
        self.assertEqual(r,original)

if __name__=="__main__":
    unittest.main()
