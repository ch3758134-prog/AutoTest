"""General lifecycle workbench acceptance tests. No external services required."""
from pathlib import Path
import json
import tempfile
import unittest

from studio_store import Store, RuleError


class EngineeringStoreTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.file = Path(self.tmp.name) / "workbench.sqlite3"
        self.store = Store(self.file)
        self.p1 = self.store.create_project({
            "name": "台架工具系统", "summary": "跨设备、软件与测试数据",
            "body": "离线工程台架，包含 PC、固件与外部 DUT",
            "version": "1.0", "variant": "A"})["project"]
        self.p2 = self.store.create_project({
            "name": "园艺温控设备", "summary": "温室环境控制器",
            "body": "独立 MCU 产品，没有 ECU 或双 CAN", "version": "0.2"})["project"]

    def create(self, kind, title, project=None, meta=None, parent_id=None):
        return self.store.create_object({
            "project_id": project or self.p1["id"], "kind": kind,
            "title": title, "body": title + " 的详细依据和行为",
            "meta": meta or {}, "parent_id": parent_id})["item"]

    def approve(self, obj, by="Owner"):
        latest = self.store.object(obj["id"])["item"]
        return self.store.approve(latest["id"], latest["revision"], by,
                                  "对当前设计条款及依据完成审查")["item"]

    def review_link(self, source, target, kind="depends"):
        link = self.store.create_relation(
            {"source_id": source["id"], "target_id": target["id"], "kind":kind})
        self.store.approve_relation(link["id"], "Owner", "核对上游设计定义与下游约束")
        return link

    def test_two_independent_products_and_no_ecu_hardcoding(self):
        self.create("requirement", "温度超限自动关闭", self.p2["id"])
        self.create("system", "双 CAN 台架主控", self.p1["id"])
        a = self.store.project(self.p1["id"])
        b = self.store.project(self.p2["id"])
        self.assertEqual(len(a["objects"]), 1)
        self.assertEqual(len(b["objects"]), 1)
        self.assertNotIn("ECU", b["project"]["body"])
        self.assertEqual(len(self.store.projects()), 2)

    def test_project_definitions_are_editable(self):
        p = self.store.update_project(self.p2["id"], {"summary": "增加记录功能", "version": "0.3"})
        self.assertEqual(p["project"]["version"], "0.3")

    def test_revision_history_and_explicit_approval(self):
        req = self.create("requirement", "可验证用户要求")
        approved = self.approve(req)
        self.assertEqual(approved["state"], "APPROVED")
        self.assertEqual(approved["revision"], 2)
        edited = self.store.update_object(req["id"], {
            "expected_revision": approved["revision"], "body": "新需求，待评审"})["item"]
        self.assertEqual(edited["state"], "DRAFT")
        self.assertEqual(edited["revision"], 3)
        self.assertEqual(len(self.store.object(req["id"])["history"]), 3)
        old = self.store.object(req["id"])["history"][1]
        self.assertEqual(old["state"], "APPROVED")
        self.assertIn("可验证用户要求", old["body"])

    def test_revision_conflict_does_not_write(self):
        req = self.create("requirement", "原始设计")
        with self.assertRaisesRegex(RuleError, "Revision conflict"):
            self.store.update_object(req["id"], {"expected_revision": 0, "body": "覆盖"})
        self.assertEqual(self.store.object(req["id"])["item"]["revision"], 1)

    def test_approval_requires_identity_and_reason(self):
        req = self.create("requirement", "设计输入")
        with self.assertRaises(RuleError):
            self.store.approve(req["id"], 1, "", "已批准")
        with self.assertRaises(RuleError):
            self.store.approve(req["id"], 1, "Owner", "")
        self.assertEqual(self.store.object(req["id"])["item"]["state"], "DRAFT")

    def test_project_separation_rejects_links_and_parent(self):
        a = self.create("system", "系统 A")
        b = self.create("system", "系统 B", self.p2["id"])
        with self.assertRaisesRegex(RuleError, "different project"):
            self.store.create_relation({"source_id":a["id"],"target_id":b["id"],"kind":"defines"})
        with self.assertRaisesRegex(RuleError, "different project"):
            self.store.create_object({"project_id":self.p1["id"],"kind":"requirement",
                "title":"不允许错挂","body":"bad","parent_id":b["id"],"meta":{}})

    def test_hierarchy_cycle_fails(self):
        a=self.create("system", "系统 A")
        b=self.create("system", "子系统 B", parent_id=a["id"])
        with self.assertRaisesRegex(RuleError,"cycle"):
            self.store.update_object(a["id"],{"expected_revision":1,"parent_id":b["id"]})

    def test_dependency_cycle_fails(self):
        a=self.create("architecture","A")
        b=self.create("design","B")
        self.store.create_relation({"source_id":a["id"],"target_id":b["id"],"kind":"depends"})
        with self.assertRaisesRegex(RuleError,"cycle"):
            self.store.create_relation({"source_id":b["id"],"target_id":a["id"],"kind":"depends"})

    def test_relations_require_review_not_just_a_click(self):
        req=self.approve(self.create("requirement", "审查需求"))
        work=self.create("work_package","编码工作包",meta={
            "source_sha":"a"*40,"file_scope":["src/demo.cs"],
            "acceptance":"可运行实测", "input_revisions":{req["id"]:req["revision"]}})
        rel=self.store.create_relation({"source_id":req["id"],"target_id":work["id"],"kind":"depends"})
        self.assertTrue(any("关系审查" in r for r in self.store.readiness(work["id"])["reasons"]))
        self.store.approve_relation(rel["id"],"Owner","引用需求条款")
        self.assertFalse(any("关系审查" in r for r in self.store.readiness(work["id"])["reasons"]))

    def test_implementation_gate_and_ai_context(self):
        req=self.approve(self.create("requirement","运行时必须保留正确状态"))
        design=self.approve(self.create("design","单一状态所有者"))
        wp=self.create("work_package","实施状态管理",meta={
            "source_sha":"f"*40,"file_scope":["src/state.cs"],
            "acceptance":"相关异常测试通过",
            "input_revisions":{req["id"]:req["revision"],design["id"]:design["revision"]}})
        self.review_link(req,wp)
        self.review_link(design,wp)
        self.assertFalse(self.store.readiness(wp["id"])["ready"])
        with self.assertRaisesRegex(RuleError,"尚未准入"):
            self.store.context(wp["id"])
        approved=self.approve(wp)
        self.assertTrue(self.store.readiness(wp["id"])["ready"])
        bundle=self.store.context(wp["id"])
        self.assertIn("src/state.cs",bundle["markdown"])
        self.assertIn("单一状态所有者",bundle["markdown"])
        self.assertEqual(bundle["manifest"]["revision"],approved["revision"])

    def test_upstream_revision_invalidates_work_package(self):
        req=self.approve(self.create("requirement","第一版条件"))
        wp=self.create("work_package","编码",meta={"source_sha":"a"*40,
            "file_scope":["src/main.cs"],"acceptance":"单测",
            "input_revisions":{req["id"]:req["revision"]}})
        self.review_link(req,wp)
        self.approve(wp)
        self.store.update_object(req["id"],{"expected_revision":2,"body":"新的需求"})
        result=self.store.readiness(wp["id"])
        self.assertFalse(result["ready"])
        self.assertTrue(any("上游对象尚未批准" in r for r in result["reasons"]))
        self.assertTrue(any("修订已变化" in r for r in result["reasons"]))

    def test_change_impact_feedback_and_closure(self):
        req=self.approve(self.create("requirement","环境测量"))
        design=self.approve(self.create("design","ADC 采样"))
        self.review_link(req,design,"defines")
        change=self.create("change","现场发现温度漂移",meta={"source":"实际电压和日志"})
        self.assertEqual(change["state"],"OPEN")
        impact=self.store.create_relation({"source_id":change["id"],
            "target_id":req["id"],"kind":"impacts"})
        result=self.store.impact(change["id"])
        self.assertEqual({a["id"] for a in result["affected"]},{req["id"],design["id"]})
        with self.assertRaises(RuleError):
            self.store.close_change(change["id"],1,"Owner","处理完成")
        self.store.approve_relation(impact["id"],"Owner","确认为需求行为的变更")
        self.store.update_object(change["id"],{"expected_revision":1,
            "meta":{"source":"实际电压和日志",
                    "assessments":{req["id"]:"REVISE"}}})
        updated=self.store.close_change(change["id"],2,"Owner","已审查所有影响")["item"]
        self.assertEqual(updated["state"],"CLOSED")

    def test_change_blocks_approved_work_package(self):
        req=self.approve(self.create("requirement","基础条件"))
        work=self.create("work_package","功能实施",meta={"source_sha":"a"*40,
            "file_scope":["src/core.cs"],"acceptance":"单测",
            "input_revisions":{req["id"]:2}})
        self.review_link(req,work);self.approve(work)
        change=self.create("change","发现需求边界变化")
        self.review_link(change,req,"impacts")
        self.assertTrue(any("未关闭" in r for r in self.store.readiness(work["id"])["reasons"]))

    def test_structured_export_contains_audit_and_revisions(self):
        req=self.approve(self.create("requirement","用户行为"))
        exported=self.store.export_project(self.p1["id"])
        self.assertEqual(exported["schema"],"engineering-workbench/1")
        self.assertTrue(exported["events"])
        self.assertGreaterEqual(len(exported["revisions"]),2)
        self.assertEqual(exported["project"]["id"],self.p1["id"])
        self.assertFalse(any(x["id"] in {o["id"] for o in exported["objects"]}
                            for x in self.store.project(self.p2["id"])["objects"]))

    def test_database_reopens_with_same_data(self):
        req=self.create("requirement","断电重启后保留")
        reopened=Store(self.file)
        self.assertEqual(reopened.object(req["id"])["item"]["title"],"断电重启后保留")


if __name__=="__main__":
    unittest.main()
