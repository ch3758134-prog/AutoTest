"""Contract tests for scope→deliverable→work reconciliation (no approvals or writes)."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from studio_plan_integrity import assess, PlanError


class ProjectPlanIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plan = json.loads((HERE / "examples/autotest_plan_control_pilot.json").read_text(encoding="utf-8"))
        cls.archive = json.loads((ROOT / "docs/engineering-control/projects/autotest/snapshot.json").read_text(encoding="utf-8"))

    def test_candidate_plan_reconciles_real_source_without_claiming_approval(self):
        raw_plan, raw_source = copy.deepcopy(self.plan), copy.deepcopy(self.archive)
        result = assess(raw_plan, raw_source)
        self.assertTrue(result["product_anchor"]["verified"])
        self.assertEqual(result["plan_status"], "PROPOSED_NOT_APPROVED")
        self.assertEqual(result["deliverables"], 1)
        self.assertEqual(len(result["work"]), 5)
        self.assertEqual(raw_plan, self.plan)
        self.assertEqual(raw_source, self.archive)
        issues = result["issues"]
        codes = {i["code"] for i in issues}
        self.assertIn("UNAPPROVED_INPUT", codes)
        self.assertIn("UPSTREAM_WORK_BLOCKED", codes)
        work = {w["id"]: w for w in result["work"]}
        self.assertEqual(work["w-g1-review"]["assessment"], "NO_RECORDED_BLOCKER")
        self.assertEqual(work["w-device-facts"]["assessment"], "NO_RECORDED_BLOCKER")
        self.assertEqual(work["w-g2-design"]["assessment"], "NEEDS_REVIEW")
        self.assertEqual(work["w-core-implementation"]["assessment"], "NEEDS_REVIEW")
        self.assertEqual(work["w-physical-verification"]["assessment"], "NEEDS_REVIEW")
        self.assertTrue(all(w["note"].startswith("NO_RECORDED_BLOCKER is not") for w in result["work"]))

    def test_stale_product_scope_is_not_silently_accepted_or_globally_deleted(self):
        plan = copy.deepcopy(self.plan)
        plan["product_anchor"]["revision"] = 1
        report = assess(plan, self.archive)
        codes = {issue["code"] for issue in report["issues"]}
        self.assertIn("ANCHOR_REVISION_CHANGED", codes)
        self.assertIn("SCOPE_REBASE_REQUIRED", codes)
        self.assertFalse(report["product_anchor"]["verified"])
        self.assertEqual(len(report["work"]), 5)

    def test_research_context_does_not_impose_formal_design_gate(self):
        plan = copy.deepcopy(self.plan)
        plan["work"] = plan["work"][:2]
        report = assess(plan, self.archive)
        self.assertEqual(report["counts"]["blockers"], 0)
        self.assertTrue(all(w["assessment"] == "NO_RECORDED_BLOCKER" for w in report["work"]))

    def test_unapproved_candidate_cannot_be_required_formal_input(self):
        plan = copy.deepcopy(self.plan)
        plan["work"] = [plan["work"][2]]
        plan["work"][0]["depends_on"] = []
        report = assess(plan, self.archive)
        self.assertIn("UNAPPROVED_INPUT", {i["code"] for i in report["issues"]})
        self.assertFalse(any(i["code"] == "ANCHOR_NOT_APPROVED" for i in report["issues"]))

    def test_stale_work_source_revision_is_flagged(self):
        plan = copy.deepcopy(self.plan)
        plan["work"][0]["sources"][1]["revision"] = 2
        report = assess(plan, self.archive)
        self.assertIn("SOURCE_STALE", {i["code"] for i in report["issues"]})
        self.assertEqual(report["work"][0]["assessment"], "NEEDS_REVIEW")

    def test_no_made_up_dates_and_no_completion_from_text_only(self):
        plan = copy.deepcopy(self.plan)
        plan["work"][0]["planned_finish"] = "2026-12-31"
        plan["work"][1]["state"] = "REPORTED_COMPLETE"
        report = assess(plan, self.archive)
        issues = {(i["code"], i["at"]) for i in report["issues"]}
        self.assertIn(("UNSUPPORTED_DATE", "w-g1-review"), issues)
        self.assertIn(("NO_COMPLETION_EVIDENCE", "w-device-facts"), issues)

    def test_dependency_cycle_fails_even_on_draft_plan(self):
        plan = copy.deepcopy(self.plan)
        plan["work"][0]["depends_on"] = ["w-g2-design"]
        with self.assertRaisesRegex(PlanError, "cycle"):
            assess(plan, self.archive)

    def test_missing_link_does_not_disappear_from_plan(self):
        plan = copy.deepcopy(self.plan)
        plan["work"][0]["deliverable_id"] = "unknown-deliverable"
        result = assess(plan, self.archive)
        self.assertIn("UNLINKED_WORK", {i["code"] for i in result["issues"]})
        self.assertEqual(len(result["work"]), 5)

    def test_no_promotion_of_candidate_plan_or_cross_project_source(self):
        plan = copy.deepcopy(self.plan)
        plan["status"] = "APPROVED"
        with self.assertRaisesRegex(PlanError, "never publishes"):
            assess(plan, self.archive)
        plan["status"] = "PROPOSED"
        plan["project_id"] = "unrelated"
        with self.assertRaisesRegex(PlanError, "different projects"):
            assess(plan, self.archive)


if __name__ == "__main__":
    unittest.main()
