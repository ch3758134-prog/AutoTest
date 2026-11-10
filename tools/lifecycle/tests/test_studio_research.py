"""Risk-based pre-architecture investigation workflow tests.

These are process/database tests. No fake result counts as real hardware acceptance.
"""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from studio_store import Store, RuleError
from studio_research import Research


class EvidenceLedResearchTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.store = Store(Path(temp.name) / "engineering.sqlite3")
        self.research = Research(self.store)
        self.project = self.store.create_project({
            "name": "多设备软件平台", "summary": "软件与嵌入式工具链",
            "version": "R1"})["project"]["id"]
        self.other = self.store.create_project({
            "name": "无关产品", "summary": "普通服务器项目"})["project"]["id"]

    def requirement(self):
        return self.store.create_object({"project_id":self.project,
            "kind":"requirement", "title":"必须共享通信数据", "body":"同一硬件连接的多个消费者",
            "meta":{}})["item"]

    def study(self, level="REAL", requirement_id=None):
        return self.research.create({
            "project_id": self.project, "title": "共享 CAN 路径可行性",
            "question": "单一连接和订阅分发能否满足多个消费者？",
            "constraints": "不能抢读,需要真实设备支持",
            "minimum_level":level, "requirement_id":requirement_id})["study"]

    def option(self, study_id, title="单一接收者"):
        return self.research.add_option(study_id, {
            "title":title, "approach":"一个所有者收包,多个消费者订阅",
            "tradeoffs":"减少竞争;增加有限缓冲",
            "assumptions":"设备提供连续帧"})["options"][-1]

    def experiment(self, study_id, option_id, level="REAL", required=True):
        return self.research.add_experiment(study_id, {
            "option_id": option_id, "title":"真实通信复测",
            "method":"真实 USB/CAN 板卡上执行重连、并发与压力测试",
            "success_criteria":"独占读者,正确分发并记录丢帧",
            "environment":"板卡 A / 固件 F1 / Windows Host R1",
            "level":level, "required":required})["experiments"][-1]

    def evidence(self, experiment_id, result="PASS"):
        self.research.authorize(experiment_id,"负责人","已经确认隔离的实验场景")
        study = self.research.record_result(experiment_id,{
            "outcome":result, "observations":"所有消费者接收到正确序列" if result=="PASS" else "出现丢帧",
            "raw_reference":"evidence/lab-2026-11-10.zip",
            "evidence_sha256":"a"*64, "configuration":"ECU-A / Firmware-F1 / Host-R1",
            "source_revision":"c"*40, "performed_by":"现场测试员"})
        return study["experiments"][0]["results"][-1]

    def reviewed(self, evidence_id):
        self.research.review_result(evidence_id,"负责人","已复核原始日志和适用配置")

    def decide(self, study_id, option_id):
        return self.research.decide(study_id,{"option_id":option_id,
            "approver":"负责人","rationale":"真实测试支持单一收包与发布",
            "restrictions":"此结论仅适用于记录的设备和固件身份"})

    def design(self, study_id, revision):
        return self.store.create_object({
            "project_id":self.project, "kind":"architecture",
            "title":"统一通信架构", "body":"单一连接/单一读者/共享发布",
            "meta":{"study_ids":[study_id],
                    "study_revisions":{study_id:revision}}})["item"]

    def test_requirement_and_source_are_explicit(self):
        req=self.requirement()
        study=self.study(requirement_id=req["id"])
        self.assertEqual(study["requirement_id"],req["id"])
        self.assertEqual(study["status"],"OPEN")
        with self.assertRaises(RuleError):
            self.research.create({"project_id":self.other,
                "title":"外部","question":"是否可以？",
                "requirement_id":req["id"]})

    def test_study_does_not_auto_choose_option(self):
        study=self.study()
        self.option(study["id"],"方案 A")
        self.option(study["id"],"方案 B")
        actual=self.research.detail(study["id"])
        self.assertEqual(len(actual["options"]),2)
        self.assertIsNone(actual["study"]["selected_option"])
        self.assertEqual(actual["study"]["status"],"OPEN")

    def test_no_evidence_cannot_decide(self):
        study=self.study()
        option=self.option(study["id"])
        self.experiment(study["id"],option["id"])
        with self.assertRaisesRegex(RuleError,"Cannot approve"):
            self.decide(study["id"],option["id"])

    def test_unauthorized_experiment_cannot_report_success(self):
        study=self.study()
        option=self.option(study["id"])
        experiment=self.experiment(study["id"],option["id"])
        with self.assertRaisesRegex(RuleError,"prior human authorization"):
            self.research.record_result(experiment["id"],{
                "outcome":"PASS","observations":"ok","raw_reference":"log.txt",
                "configuration":"A", "source_revision":"abc", "performed_by":"AI"})

    def test_reported_but_unreviewed_pass_does_not_unlock(self):
        study=self.study()
        option=self.option(study["id"])
        experiment=self.experiment(study["id"],option["id"])
        self.evidence(experiment["id"])
        gate=self.research.evaluate(study["id"],option["id"])
        self.assertFalse(gate["ready"])
        self.assertTrue(any("reviewed" in x for x in gate["reasons"]))

    def test_lower_maturity_and_failed_bench_do_not_unlock(self):
        study=self.study(level="REAL")
        option=self.option(study["id"])
        experiment=self.experiment(study["id"],option["id"],level="PROTOTYPE")
        record=self.evidence(experiment["id"])
        self.reviewed(record["id"])
        self.assertFalse(self.research.evaluate(study["id"],option["id"])["ready"])
        failed_study=self.study(level="REAL")
        failed_option=self.option(failed_study["id"])
        ex=self.experiment(failed_study["id"],failed_option["id"])
        report=self.evidence(ex["id"],result="FAIL")
        self.reviewed(report["id"])
        self.assertFalse(self.research.evaluate(failed_study["id"],failed_option["id"])["ready"])

    def test_real_reviewed_evidence_unlocks_technical_decision(self):
        study=self.study()
        option=self.option(study["id"])
        experiment=self.experiment(study["id"],option["id"])
        result=self.evidence(experiment["id"])
        self.reviewed(result["id"])
        chosen=self.decide(study["id"],option["id"])
        self.assertEqual(chosen["study"]["status"],"DECIDED")
        self.assertEqual(chosen["study"]["revision"],2)
        self.assertEqual(chosen["decisions"][0]["approver"],"负责人")
        self.assertEqual(chosen["decisions"][0]["reviewed_evidence"][0]["raw_reference"],
                         "evidence/lab-2026-11-10.zip")
        with self.assertRaisesRegex(RuleError,"sealed"):
            self.option(study["id"],"偷偷换方案")

    def test_design_approval_requires_decided_and_pinned_study(self):
        study=self.study()
        architecture=self.design(study["id"],2)
        with self.assertRaisesRegex(RuleError,"Technical investigation gate"):
            self.store.approve(architecture["id"],1,"负责人","审查架构")
        option=self.option(study["id"])
        e=self.experiment(study["id"],option["id"])
        result=self.evidence(e["id"]);self.reviewed(result["id"])
        decision=self.decide(study["id"],option["id"])
        approved=self.store.approve(architecture["id"],1,"负责人","依据真实实验批准")["item"]
        self.assertEqual(approved["state"],"APPROVED")
        self.assertEqual(decision["study"]["revision"],2)

    def test_reopened_study_blocks_existing_architecture_in_work_package(self):
        study=self.study()
        option=self.option(study["id"])
        experiment=self.experiment(study["id"],option["id"])
        ev=self.evidence(experiment["id"]);self.reviewed(ev["id"])
        self.decide(study["id"],option["id"])
        arch=self.design(study["id"],2)
        approved=self.store.approve(arch["id"],1,"负责人","技术证据通过")["item"]
        work=self.store.create_object({"project_id":self.project,
            "kind":"work_package","title":"只实现批准架构","body":"不重新研究设计",
            "meta":{"acceptance":"共享接收验收","source_sha":"f"*40,
                    "file_scope":["src/can.cs"],"input_revisions":{arch["id"]:2}}})["item"]
        link=self.store.create_relation({"source_id":arch["id"],
            "target_id":work["id"],"kind":"depends"})
        self.store.approve_relation(link["id"],"负责人","架构与实现依赖明确")
        self.store.approve(work["id"],1,"负责人","允许编码")
        self.assertTrue(self.store.readiness(work["id"])["ready"])
        context=self.store.context(work["id"])
        self.assertIn("技术研究证据",context["markdown"])
        self.assertIn("evidence/lab-2026-11-10.zip",context["markdown"])
        self.assertEqual(len(context["manifest"]["research_decisions"]),1)
        exported=self.store.export_project(self.project)
        self.assertEqual(len(exported["technical_research"]["studies"]),1)
        self.assertEqual(len(exported["technical_research"]["results"]),1)
        self.assertEqual(len(exported["technical_research"]["decisions"]),1)
        self.research.reopen(study["id"],{"actor":"负责人",
                                        "reason":"现场出现不同固件修订，重新验证"})
        gate=self.store.readiness(work["id"])
        self.assertFalse(gate["ready"])
        self.assertTrue(any("技术" in x or "Technology" in x
                            for x in gate["reasons"]))
        self.assertEqual(self.store.object(arch["id"])["item"]["state"],"APPROVED")
        self.assertEqual(self.research.detail(study["id"])["decisions"][0]["state"],"SUPERSEDED")

    def test_unrelated_design_approval_does_not_need_spike(self):
        unrelated=self.store.create_object({"project_id":self.project,
            "kind":"design", "title":"普通 UI 文案映射", "body":"静态字符串",
            "meta":{}})["item"]
        self.assertEqual(self.store.approve(unrelated["id"],1,"负责人",
                           "不存在关键未知技术")["item"]["state"],"APPROVED")

    def test_reviewed_retest_can_resolve_previous_failed_experiment(self):
        study=self.study()
        option=self.option(study["id"])
        exp=self.experiment(study["id"],option["id"])
        failed=self.evidence(exp["id"],result="FAIL")
        self.reviewed(failed["id"])
        self.assertFalse(self.research.evaluate(study["id"],option["id"])["ready"])
        # Another real run is recorded; older failure stays in evidence history.
        later=self.research.record_result(exp["id"],{
            "outcome":"PASS","observations":"reworked buffer policy was verified",
            "raw_reference":"evidence/retest.zip","configuration":"board A/firmware F2",
            "source_revision":"e"*40,"performed_by":"现场测试员"})
        result=later["experiments"][0]["results"][-1]
        self.reviewed(result["id"])
        gate=self.research.evaluate(study["id"],option["id"])
        self.assertTrue(gate["ready"])
        self.assertEqual(gate["accepted_evidence"][0]["previous_failed_runs"],1)
        self.assertEqual(len(self.research.detail(study["id"])["experiments"][0]["results"]),2)

    def test_invalid_evidence_hash_rejected(self):
        study=self.study()
        o=self.option(study["id"])
        exp=self.experiment(study["id"],o["id"])
        self.research.authorize(exp["id"],"负责人","实验已允许")
        with self.assertRaisesRegex(RuleError,"SHA-256"):
            self.research.record_result(exp["id"],{
                "outcome":"PASS","observations":"ok","raw_reference":"test.log",
                "evidence_sha256":"123","configuration":"dev",
                "source_revision":"commit1","performed_by":"tester"})


if __name__=="__main__":
    unittest.main()
