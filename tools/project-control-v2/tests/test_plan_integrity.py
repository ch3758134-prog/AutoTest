"""Plan integrity against the curated independent project source."""
import copy
from pathlib import Path
import sys
import unittest

HERE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(HERE))
from control import Control
from plan_integrity import assess, PlanError


class PlanIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ctl=Control(HERE/"workspace/autotest.json",HERE/"sources/autotest_engineering.json")
        cls.plan=cls.ctl.plan(cls.ctl.read())
        cls.archive=cls.ctl.archive()

    def review(self,plan):
        return assess(plan,self.archive)

    def test_approved_p0_and_unapproved_g1_are_distinct(self):
        r=self.review(self.plan)
        self.assertTrue(r["product_anchor"]["verified"])
        self.assertEqual(r["plan_status"],"PROPOSED_NOT_APPROVED")
        self.assertEqual(r["deliverables"],1)
        self.assertEqual(len(r["work"]),5)
        self.assertIn("UNAPPROVED_INPUT",{e["code"] for e in r["issues"]})

    def test_stale_scope_requires_reconciliation_without_deleting_work(self):
        plan=copy.deepcopy(self.plan);plan["product_anchor"]["revision"] = 1
        result=self.review(plan)
        self.assertIn("SCOPE_REBASE_REQUIRED",{x["code"] for x in result["issues"]})
        self.assertEqual(len(result["work"]),5)

    def test_research_work_accepts_unapproved_context(self):
        plan=copy.deepcopy(self.plan);plan["work"]=plan["work"][:2]
        result=self.review(plan)
        self.assertEqual(result["counts"]["blockers"],0)

    def test_direct_formal_input_invalidates_only_affected_work_and_dependencies(self):
        result=self.review(self.plan)
        indexed={w["id"]:w for w in result["work"]}
        self.assertEqual(indexed["w-g1-review"]["assessment"],"NO_RECORDED_BLOCKER")
        self.assertEqual(indexed["w-g2-design"]["assessment"],"NEEDS_REVIEW")
        self.assertEqual(indexed["w-core-implementation"]["assessment"],"NEEDS_REVIEW")

    def test_date_without_basis_must_be_identified(self):
        plan=copy.deepcopy(self.plan)
        plan["work"][0]["planned_finish"]="2030-01-01"
        result=self.review(plan)
        self.assertIn("UNSUPPORTED_DATE",{i["code"] for i in result["issues"]})

    def test_completion_without_evidence_rejected(self):
        plan=copy.deepcopy(self.plan)
        plan["work"][0]["state"]="REPORTED_COMPLETE"
        result=self.review(plan)
        self.assertIn("NO_COMPLETION_EVIDENCE",{i["code"] for i in result["issues"]})

    def test_cycle_rejected(self):
        plan=copy.deepcopy(self.plan)
        plan["work"][0]["depends_on"]=["w-g2-design"]
        with self.assertRaisesRegex(PlanError,"cycle"):
            self.review(plan)

    def test_cross_project_reference_rejected(self):
        plan=copy.deepcopy(self.plan);plan["project_id"]="wrong"
        with self.assertRaisesRegex(PlanError,"different projects"):
            self.review(plan)


if __name__=="__main__":
    unittest.main()
