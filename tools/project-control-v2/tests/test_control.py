"""Real project-control transitions, plan baselines, impact and evidence tests."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
from control import Control, ControlError
from server import load_model
from sync import status as sync_status


class ControlTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.work = Path(self.tmp.name) / "autotest.json"
        self.work.write_bytes((HERE / "workspace/autotest.json").read_bytes())
        self.archive = HERE / "sources/autotest_engineering.json"
        self.ctrl = Control(self.work, self.archive)

    def action(self, name, **fields):
        return self.ctrl.apply(name, {"expected_revision": self.ctrl.read()["revision"],
                                      "reason": fields.pop("reason", "测试中的人工判断依据"),
                                      **fields}, "OwnerTest")

    def baseline(self):
        self.action("submit_plan")
        self.action("approve_plan")

    def test_live_model_never_promotes_g1(self):
        model = load_model()
        self.assertEqual(model["control"]["planning"]["status"], "DRAFT")
        self.assertFalse(model["control"]["baselines"])
        self.assertEqual(model["assessment"]["plan_status"], "PROPOSED_NOT_APPROVED")
        self.assertEqual([o["id"] for o in model["objects"] if o["state"]=="APPROVED"],
                         ["at-p0-approved-charter-v01"])
        self.assertTrue(any(i["code"]=="UNAPPROVED_INPUT" for i in model["assessment"]["issues"]))

    def test_submit_and_baseline_are_distinct_from_gate_and_code_authorization(self):
        before = self.ctrl.read()
        submit = self.action("submit_plan")
        self.assertEqual(self.ctrl.read()["planning"]["status"], "IN_REVIEW")
        self.assertTrue(submit["result"]["known_plan_issues"])
        review = self.action("approve_plan", reason="批准工作安排，不批准未完成的系统设计")
        after = self.ctrl.read()
        self.assertEqual(after["planning"]["status"], "BASELINED")
        self.assertEqual(len(after["baselines"]), 1)
        self.assertEqual(after["baselines"][0]["actor"], "OwnerTest")
        self.assertEqual(len(after["phase_reviews"]), 0)
        self.assertEqual(len(after["receipts"]), 0)
        self.assertEqual(after["baselines"][0]["digest"], submit["result"]["submitted_digest"])
        self.assertEqual(before["planning"]["product_anchor"], after["planning"]["product_anchor"])
        self.assertEqual(after["events"][-1]["revision"], after["revision"])
        self.assertIn("baseline", review["result"])

    def test_no_mutation_of_submitted_plan_and_baseline_remains_immutable(self):
        self.baseline()
        old = copy.deepcopy(self.ctrl.read()["baselines"])
        self.action("edit_work", id="w-g1-review", patch={"title": "调整后的研究任务"})
        self.assertEqual(self.ctrl.read()["planning"]["status"], "REVISION_DRAFT")
        self.assertEqual(self.ctrl.read()["baselines"], old)
        self.action("submit_plan")
        with self.assertRaisesRegex(ControlError, "withdraw"):
            self.action("edit_work", id="w-g1-review", patch={"title": "不允许编辑正在送审的计划"})
        self.action("return_plan")
        self.assertEqual(self.ctrl.read()["planning"]["status"], "REVISION_DRAFT")

    def test_edit_work_and_scope_does_not_approve_product(self):
        before = self.archive.read_bytes()
        self.action("create_deliverable", item={
            "id": "new-candidate-delivery", "title": "新候选交付目标",
            "success_condition": "完成后具备可靠的可核实验证证据",
            "commitment": "PROPOSAL"})
        self.action("create_work", item={
            "id": "work-new", "title": "调查新增能力",
            "kind": "RESEARCH", "mode": "EXPLORATORY", "horizon": "NEXT",
            "deliverable_id": "new-candidate-delivery",
            "expected_result": "提交调查结论及来源",
            "exit_condition": "对照实际工程核实重要约束",
            "depends_on": [], "sources": [],
            "priority": "NORMAL"})
        self.assertEqual(self.archive.read_bytes(), before)
        self.assertEqual(len(self.ctrl.read()["planning"]["deliverables"]), 2)
        self.assertEqual(len(self.ctrl.read()["planning"]["work"]), 6)

    def test_cyclic_dependency_is_rejected_atomically(self):
        before = self.work.read_bytes()
        with self.assertRaisesRegex(ControlError, "cycle"):
            self.action("edit_work", id="w-g1-review",
                        patch={"depends_on": ["w-g2-design"]})
        self.assertEqual(self.work.read_bytes(), before)

    def test_stale_revision_must_refresh(self):
        rev = self.ctrl.read()["revision"]
        self.action("update_progress", id="w-g1-review", state="IN_PROGRESS",
                    detail="已开始调查", evidence_refs=[])
        with self.assertRaisesRegex(ControlError, "stale"):
            self.ctrl.apply("update_progress", {
                "expected_revision": rev, "reason": "旧版本请求",
                "id": "w-g1-review", "state": "BLOCKED",
                "detail": "历史信息", "evidence_refs": []
            }, "OwnerTest")

    def test_completion_claim_needs_evidence_and_separate_verification(self):
        with self.assertRaisesRegex(ControlError, "requires evidence"):
            self.action("update_progress", id="w-g1-review", state="REPORTED_COMPLETE",
                        detail="声称完成", evidence_refs=[])
        self.action("update_progress", id="w-g1-review", state="REPORTED_COMPLETE",
                    detail="已交付完整研究成果", evidence_refs=["github:pr/123"])
        self.assertEqual(self.ctrl.read()["progress"]["w-g1-review"]["state"],
                         "REPORTED_COMPLETE")
        self.action("verify_work", id="w-g1-review", reason="已核对对应版本的实际材料")
        self.assertEqual(self.ctrl.read()["progress"]["w-g1-review"]["state"],
                         "VERIFIED_COMPLETE")
        self.assertFalse(self.ctrl.read()["baselines"])

    def test_scoped_change_requires_each_impact_and_does_not_rollback_globally(self):
        response = self.action("raise_change", kind="FACT_CORRECTION",
                               title="KEY_SIM 真实电压机制补充",
                               description="发现按键是电阻网络组合并输出五路电压",
                               affected_work=["w-g2-design"], source_refs=["field-note"])
        cid = response["result"]["change_id"]
        with self.assertRaisesRegex(ControlError, "every explicitly affected"):
            self.action("assess_change", id=cid, assessments={})
        self.action("assess_change", id=cid, assessments={
            "w-g2-design": {"disposition": "REASSESS",
                             "rationale": "该设计可能使用错误的按键通道模型"}})
        self.action("resolve_change", id=cid, decision="ACCEPT_FOR_REPLANNING")
        record = self.ctrl.read()
        self.assertIn("w-g2-design", record["invalidation"])
        self.assertNotIn("w-g1-review", record["invalidation"])
        self.assertEqual(record["planning"]["status"], "DRAFT")
        with self.assertRaisesRegex(ControlError, "unresolved"):
            self.action("update_progress", id="w-g2-design", state="IN_PROGRESS",
                        detail="尚未重新审查的设计工作", evidence_refs=[])
        self.action("reconcile_work", id="w-g2-design", disposition="NO_CHANGE",
                    reason="再次核对后不涉及该工作项，保存实际依据")
        self.assertTrue(self.ctrl.read()["invalidation"]["w-g2-design"]["resolved"])

    def test_g1_gate_cannot_pass_with_only_p0_evidence(self):
        before = self.work.read_bytes()
        with self.assertRaisesRegex(ControlError, "belong"):
            self.action("record_phase_review", phase_id="G1", decision="PASS",
                        evidence_ids=["at-p0-approved-charter-v01"])
        self.assertEqual(before, self.work.read_bytes())
        self.action("record_phase_review", phase_id="G1", decision="DEFER", evidence_ids=[])
        self.assertEqual(self.ctrl.read()["phase_reviews"][-1]["decision"], "DEFER")

    def test_local_codex_receipt_is_not_acceptance(self):
        receipt = self.action("record_receipt", work_id="w-core-implementation",
                head_sha="a"*40, commands=[{"command": "dotnet test", "exit_code": 0}],
                evidence_refs=["test-output:sha256abc"], note="来自本地手动登记")
        item = self.ctrl.read()["receipts"][0]
        self.assertEqual(item["status"], "UNVERIFIED")
        self.assertEqual(item["source"], "LOCAL_REPORTED")
        self.action("review_receipt", id=receipt["result"]["receipt_id"],
                    decision="EVIDENCE_REVIEWED", reason="核实原始测试记录")
        self.assertEqual(self.ctrl.read()["receipts"][0]["status"], "EVIDENCE_REVIEWED")
        self.assertNotIn("w-core-implementation", self.ctrl.read()["progress"])

    def test_git_status_never_claims_remote_freshness_by_default(self):
        value = sync_status(ROOT)
        self.assertFalse(value["remote_checked"])
        self.assertEqual(value["remote_relation"], "REMOTE_UNVERIFIED")


if __name__ == "__main__":
    unittest.main()
