"""Project plan, Gate, and scoped change-impact semantics independent of AutoTest."""
from pathlib import Path
import importlib.util
import unittest

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("pc_domain",ROOT/"portable/governance.py")
g=importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)


def fixture():
    return {
        "deliverables":[{"id":"D1","title":"Source review"},
                        {"id":"D2","title":"Software test"},
                        {"id":"D3","title":"Independent research"}],
        "work":[
            {"id":"W1","title":"Input research","deliverable_id":"D1","depends_on":[]},
            {"id":"W2","title":"Design system","deliverable_id":"D1","depends_on":["W1"]},
            {"id":"W3","title":"Verify system","deliverable_id":"D2","depends_on":["W2"]},
            {"id":"W4","title":"Independent archive","deliverable_id":"D3","depends_on":[]}
        ],
        "requirements":[],"plans":[],"changes":[],"gate_reviews":[],"trace_links":[]
    }


class GovernanceTests(unittest.TestCase):
    def setUp(self):
        self.record=fixture()

    def call(self,action,**kwargs):
        return g.apply(self.record,action,kwargs)

    def test_requirements_have_evidence_and_are_not_approved(self):
        self.call("create_requirement",id="REQ1",title="Traceable requirement",
                  statement="Capture test inputs accurately",
                  source_refs=["docs/research.md"],acceptance="Observed test evidence")
        self.assertEqual(self.record["requirements"][0]["state"],"PROPOSED_NOT_APPROVED")
        with self.assertRaises(g.GovernanceError):
            self.call("create_requirement",id="REQ2",title="Unsupported statement",
                      statement="unverified",source_refs=[],acceptance="None")

    def test_affected_downstream_work_and_unrelated_plans_are_preserved(self):
        self.call("create_plan",id="PLAN1",title="Linked plan",objective="Engineering process",
                  work_ids=["W1","W2","W3"],exit_condition="Evidence")
        self.call("create_plan",id="PLAN2",title="Independent plan",
                  objective="Archive prior research",work_ids=["W4"],exit_condition="Archive")
        self.call("submit_plan",id="PLAN1",reason="Ready for review")
        self.call("submit_plan",id="PLAN2",reason="Ready for review")
        self.call("create_change",id="CR1",title="Reconsider input source",
                  justification="New contradictory evidence",
                  source_refs=["docs/new-evidence.md"],root_work_ids=["W1"])
        self.call("assess_change",id="CR1",impact_summary="Revisit design and verification only")
        change=self.record["changes"][0]
        self.assertEqual(change["affected_work_ids"],["W1","W2","W3"])
        self.assertEqual(change["affected_deliverables"],["D1","D2"])
        self.assertEqual(change["state"],"AWAITING_HUMAN_CHANGE_DECISION")
        self.assertEqual(change["decision"],"NOT_APPROVED")
        self.assertEqual(self.record["plans"][0]["state"],"REASSESSMENT_REQUIRED")
        self.assertEqual(self.record["plans"][1]["state"],"SUBMITTED_FOR_HUMAN_REVIEW")

    def test_plan_review_does_not_grant_execution_authorization(self):
        self.call("create_plan",id="PLAN1",title="Trial plan",objective="Trial",
                  work_ids=["W1"],exit_condition="Review all evidence")
        self.call("submit_plan",id="PLAN1",reason="Submitted for owner review")
        self.call("record_plan_review",id="PLAN1",
                  decision="REVIEWED_PENDING_FORMAL_APPROVAL",
                  reviewer="Owner",evidence_refs=["owner-review-001"],
                  reason="No objections pending official decision")
        plan=self.record["plans"][0]
        self.assertEqual(plan["state"],"REVIEWED_PENDING_EXTERNAL_APPROVAL")
        self.assertFalse(any("APPROVED" in str(x.get("state","")) for x in self.record["plans"]))
        with self.assertRaises(g.GovernanceError):
            self.call("record_plan_review",id="PLAN1",decision="APPROVED",
                      reviewer="Owner",evidence_refs=["x"],reason="No")

    def test_gate_review_request_cannot_mark_pass(self):
        self.call("request_gate_review",id="GATE1",stage="G3",
                  purpose="Design qualification",work_ids=["W2"],
                  evidence_refs=["https://github.com/example/repo/pull/8"])
        review=self.record["gate_reviews"][0]
        self.assertEqual(review["state"],"REQUESTED_NOT_APPROVED")
        self.assertEqual(review["decision"],"NONE")

    def test_dependency_cycles_fail(self):
        with self.assertRaisesRegex(g.GovernanceError,"cyclic"):
            self.call("link_dependency",id="W1",depends_on="W3")
        # Direct domain function receives a mutable copy. The service never persists
        # a copy when an operation raises a validation exception.
        self.assertEqual(self.record["work"][0]["depends_on"],["W3"])

    def test_scope_snapshot_changes_block_unchanged_approval(self):
        self.call("create_plan",id="PLAN1",title="Versioned plan",
                  objective="Verify mechanism",work_ids=["W1"],exit_condition="Evidence")
        self.call("submit_plan",id="PLAN1",reason="Proposal")
        self.record["work"][0]["title"]="Modified outside plan review"
        with self.assertRaisesRegex(g.GovernanceError,"re-evaluation"):
            self.call("record_plan_review",id="PLAN1",decision="REVIEWED_PENDING_FORMAL_APPROVAL",
                      reviewer="Owner",evidence_refs=["record-1"],reason="Changed")
        self.assertEqual(self.record["plans"][0]["state"],"REASSESSMENT_REQUIRED")

    def test_dependency_change_reopens_affected_plan_only(self):
        self.call("create_plan",id="A",title="Plan A",objective="A",work_ids=["W2"],exit_condition="A")
        self.call("create_plan",id="B",title="Plan B",objective="B",work_ids=["W4"],exit_condition="B")
        self.call("submit_plan",id="A",reason="Proposal")
        self.call("submit_plan",id="B",reason="Proposal")
        self.call("link_dependency",id="W2",depends_on="W4")
        self.assertEqual(self.record["plans"][0]["state"],"REASSESSMENT_REQUIRED")
        self.assertEqual(self.record["plans"][1]["state"],"SUBMITTED_FOR_HUMAN_REVIEW")


if __name__=="__main__":
    unittest.main()
