"""Review requests must not rely on evidence invalidated by a later assessment."""
import importlib.util
from pathlib import Path
import unittest

HERE=Path(__file__).resolve().parents[1]/"portable"
spec=importlib.util.spec_from_file_location("governance",HERE/"governance.py")
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def state(stale=True):
    return {"work":[{"id":"WORK-1","deliverable_id":"OUT-1","kind":"RESEARCH",
                     "title":"recover context","state":"PROPOSED","depends_on":[],
                     "context_review_id":"CTX-old",
                     "context_evidence_status":("STALE_AFTER_NEW_ASSESSMENT"
                                                if stale else "CURRENT_CANDIDATE")}],
            "deliverables":[{"id":"OUT-1","title":"reviewed project knowledge",
                             "success_condition":"context assessed"}],
            "requirements":[],"plans":[],"changes":[],"gate_reviews":[],"trace_links":[]}

class StaleContextGovernanceTests(unittest.TestCase):
    def test_submit_plan_denied_if_context_stale(self):
        r=state()
        m.apply(r,"create_plan",{"id":"PLAN-1","title":"research program",
                                  "objective":"recover project","work_ids":["WORK-1"],
                                  "exit_condition":"independent review"})
        with self.assertRaises(m.GovernanceError):
            m.apply(r,"submit_plan",{"id":"PLAN-1","reason":"submitted with old source"})
        self.assertEqual(r["plans"][0]["state"],"DRAFT")

    def test_gate_request_denied_if_context_stale(self):
        r=state()
        with self.assertRaises(m.GovernanceError):
            m.apply(r,"request_gate_review",{"id":"G-1","stage":"context",
                      "purpose":"review recovered sources", "work_ids":["WORK-1"],
                      "evidence_refs":["docs/review.md"]})
        self.assertFalse(r["gate_reviews"])

    def test_current_candidate_is_only_pending_review(self):
        r=state(stale=False)
        m.apply(r,"create_plan",{"id":"PLAN-1","title":"research program",
                                   "objective":"recover project","work_ids":["WORK-1"],
                                   "exit_condition":"review sources"})
        m.apply(r,"submit_plan",{"id":"PLAN-1","reason":"candidate for review"})
        self.assertEqual(r["plans"][0]["state"],"SUBMITTED_FOR_HUMAN_REVIEW")
        self.assertNotEqual(r["plans"][0]["state"],"APPROVED")

if __name__=="__main__":
    unittest.main()
