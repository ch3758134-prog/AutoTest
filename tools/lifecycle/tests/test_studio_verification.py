"""Verification evidence persistence and approval separation tests."""
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store, RuleError
from studio_verification import Verification


class VerificationTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store=Store(Path(self.tmp.name)/"verification.sqlite3")
        self.p=self.store.create_project({"name":"Test Product"})["project"]["id"]
        self.p2=self.store.create_project({"name":"Other Product"})["project"]["id"]
        self.ver=Verification(self.store)
        self.test=self.store.create_object({"project_id":self.p,"kind":"test",
                     "title":"Physical or software specification","body":"criteria"})["item"]

    def payload(self):
        return {"test_object_id":self.test["id"],"test_revision":self.test["revision"],
                "level":"BENCH","outcome":"FAIL","configuration":"variant A, ECU variant",
                "source_sha":"a"*40,"environment":"MCU version, DUT, station 1",
                "evidence_ref":"local://bench/session.jsonl","actor":"Operator"}

    def test_run_never_means_accepted_product(self):
        run=self.ver.record(self.payload())
        self.assertEqual(run["outcome"],"FAIL")
        self.assertIsNone(run["review_decision"])
        self.assertEqual(run["test_revision"],1)
        self.assertEqual(len(self.ver.list(self.p)["runs"]),1)
        self.assertEqual(len(self.ver.list(self.p2)["runs"]),0)
        reviewed=self.ver.review(run["id"],"CONFIRMED","Reviewer",
                                 "Confirmed failure evidence for this configuration only")
        self.assertEqual(reviewed["review_decision"],"CONFIRMED")
        self.assertEqual(reviewed["outcome"],"FAIL")
        with self.assertRaisesRegex(RuleError,"already been reviewed"):
            self.ver.review(run["id"],"REJECTED","Reviewer","Override")
        self.assertEqual(self.store.object(self.test["id"])["item"]["state"],"DRAFT")

    def test_required_evidence_identity_and_revision(self):
        data=self.payload()
        data["source_sha"]="bad"
        with self.assertRaisesRegex(RuleError,"40-character"):
            self.ver.record(data)
        data=self.payload()
        data["evidence_ref"]=""
        with self.assertRaisesRegex(RuleError,"cannot be empty"):
            self.ver.record(data)
        self.store.update_object(self.test["id"],{"expected_revision":1,"body":"Revised criteria"})
        with self.assertRaisesRegex(RuleError,"revision conflict"):
            self.ver.record(self.payload())
        self.assertEqual(self.ver.list(self.p)["runs"],[])

    def test_unreviewed_and_rejected_are_distinct(self):
        p=self.payload()
        p["outcome"]="PASS"
        first=self.ver.record(p)
        p["level"]="INTEGRATION"
        second=self.ver.record(p)
        self.ver.review(first["id"],"REJECTED","Reviewer",
                        "No authentic raw data available")
        listed=self.ver.list(self.p)["runs"]
        self.assertEqual(len(listed),2)
        self.assertEqual({r["id"]:r["review_decision"] for r in listed},
                         {first["id"]:"REJECTED",second["id"]:None})

    def test_test_specification_required_and_no_cross_project_leak(self):
        candidate=self.store.create_object({"project_id":self.p2,
                       "kind":"requirement","title":"Other","body":"not test"})["item"]
        payload=self.payload()
        payload["test_object_id"]=candidate["id"]
        with self.assertRaisesRegex(RuleError,"test specification"):
            self.ver.record(payload)
        with self.assertRaisesRegex(RuleError,"not found"):
            self.ver.review("nonexistent","CONFIRMED","Reviewer","Reason")

    def test_ui_route_and_asset(self):
        base=Path(__file__).resolve().parents[1]
        server=(base/"studio_server.py").read_text(encoding="utf-8")
        html=(base/"studio.html").read_text(encoding="utf-8")
        self.assertIn('if path == "/api/studio/verification":',server)
        self.assertIn('elif path == "/api/studio/verification/record":',server)
        self.assertIn('elif path == "/api/studio/verification/review":',server)
        self.assertIn('if path == "/verification.js":',server)
        self.assertIn('<script src="/verification.js"></script>',html)
        self.assertIn("verificationHistoryModal(selected)",html)


if __name__=="__main__":
    unittest.main()
