"""AI planning suggestions are advisory; never mutate approved engineering baselines."""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store, RuleError
from studio_research import Research
from studio_execution import Execution
from studio_management import Management


class ManagerProposalTests(unittest.TestCase):
    def setUp(self):
        temp=tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.store=Store(Path(temp.name)/"test.sqlite3")
        self.research=Research(self.store)
        self.execution=Execution(self.store)
        self.manager=Management(self.store,self.execution,self.research)
        self.project=self.store.create_project({
            "name":"Modular Control Product","summary":"PC software and MCU firmware",
            "version":"R1"})["project"]["id"]
        self.other=self.store.create_project({
            "name":"Separate Product","summary":"Control board"})["project"]["id"]

    def propose(self,**kwargs):
        data={"project_id":self.project,"category":"TECHNICAL_RESEARCH",
              "priority":"P1","title":"Investigate hardware limitation",
              "rationale":"Subsystem behavior is unknown, cannot approve software interface",
              "next_action":"Plan a real bench experiment and record raw evidence",
              "proposed_by":"AI-MANAGER","source_objects":[]}
        data.update(kwargs)
        return self.manager.propose(data)

    def test_manager_brief_separates_real_evidence_from_unknowns(self):
        req=self.store.create_object({"project_id":self.project,
            "kind":"requirement","title":"Physical behavior",
            "body":"Read real device input","meta":{}})["item"]
        brief=self.manager.context(self.project)
        self.assertEqual(brief["schema"],"engineering-manager-brief/2")
        self.assertTrue(any(x["id"]==req["id"] for x in brief["engineering_objects"]))
        self.assertIsNone(brief["github"]["fetched_at"])
        self.assertIsNone(brief["github"]["snapshot"])
        self.assertEqual(len(brief["content_sha256"]),64)
        self.assertIn("may propose", " ".join(brief["advisory_instructions"]))

    def test_manager_reads_verification_failures_without_auto_acceptance(self):
        from studio_verification import Verification
        verification=Verification(self.store)
        test=self.store.create_object({"project_id":self.project,
            "kind":"test","title":"Bench current monitoring",
            "body":"Measure under real loads","meta":{}})["item"]
        run=verification.record({
            "test_object_id":test["id"],"test_revision":1,
            "level":"BENCH","outcome":"FAIL","configuration":"variant A",
            "source_sha":"b"*40,"evidence_ref":"bench://raw/event-001",
            "environment":"real station","actor":"Technician"})
        before=self.manager.context(self.project)
        self.assertEqual(len(before["verification_runs"]),1)
        self.assertTrue(any(x["type"]=="EVIDENCE_REVIEW" for x in before["blockers"]))
        verification.review(run["id"],"CONFIRMED","Owner","Raw trace inspected")
        after=self.manager.context(self.project)
        self.assertFalse(any(x["type"]=="EVIDENCE_REVIEW" for x in after["blockers"]))
        self.assertTrue(any(x["type"]=="CONFIRMED_TEST_FAILURE" for x in after["blockers"]))
        self.assertEqual(self.store.object(test["id"])["item"]["state"],"DRAFT")
        self.assertEqual(self.store.project(self.other)["objects"],[])

    def test_proposal_does_not_create_approved_design_or_work_package(self):
        first=self.manager.propose({
            "project_id":self.project,"category":"CODING","priority":"P0",
            "title":"Implement CAN routing","rationale":"Design candidate not yet accepted",
            "next_action":"Complete detailed design and seek approval first",
            "proposed_by":"AI-MANAGER","source_objects":[]})
        self.assertEqual(first[0]["status"],"PENDING")
        project=self.store.project(self.project)
        self.assertEqual(project["objects"],[])
        after=self.manager.review(first[0]["id"],"ACCEPT_FOR_PLANNING",
                                  "Project owner","Good proposal, must design first")
        self.assertEqual(after[0]["status"],"ACCEPT_FOR_PLANNING")
        self.assertEqual(self.store.project(self.project)["objects"],[])
        with self.assertRaisesRegex(RuleError,"already reviewed"):
            self.manager.review(first[0]["id"],"REJECT","Owner","Duplicate")
        archive=self.store.export_project(self.project)
        self.assertEqual(len(archive["manager_proposals"]),1)
        self.assertEqual(archive["manager_proposals"][0]["status"],"ACCEPT_FOR_PLANNING")
        self.assertEqual(archive["manager_proposals"][0]["reviewer"],"Project owner")

    def test_cross_project_and_nonexistent_sources_rejected(self):
        other=self.store.create_object({"project_id":self.other,
           "kind":"requirement","title":"Other requirement","body":"not this project"})["item"]
        with self.assertRaisesRegex(RuleError,"different project"):
            self.propose(source_objects=[other["id"]])
        with self.assertRaisesRegex(RuleError,"does not exist"):
            self.propose(source_objects=["does-not-exist"])
        with self.assertRaises(RuleError):
            self.propose(source_objects=["abc","abc"])

    def test_invalid_category_and_impersonation_not_an_approval(self):
        with self.assertRaisesRegex(RuleError,"category"):
            self.propose(category="APPROVE_ARCHITECTURE")
        fake=self.propose(proposed_by="Owner")
        self.assertEqual(fake[0]["status"],"PENDING")
        self.assertFalse(any(x["kind"]=="architecture" for x in
                             self.store.project(self.project)["objects"]))

    def test_proposal_sources_have_trusted_snapshot_digest(self):
        design=self.store.create_object({"project_id":self.project,
            "kind":"design","title":"Detailed Software Unit",
            "body":"Complete design required","meta":{}})["item"]
        data=self.propose(source_objects=[design["id"]],
                          source_manifest_sha256="made-up-sha")
        self.assertEqual(data[0]["source_objects"],[design["id"]])
        self.assertEqual(len(data[0]["source_manifest_sha256"]),64)
        self.assertNotEqual(data[0]["source_manifest_sha256"],"made-up-sha")
        self.assertEqual(data[0]["status"],"PENDING")

    def test_proposal_review_has_explicit_outcome(self):
        pid=self.propose(priority="P2")[0]["id"]
        reviewed=self.manager.review(pid,"NEEDS_INFORMATION",
                                     "Owner","Evidence not yet sufficient")
        self.assertEqual(reviewed[0]["reviewer"],"Owner")
        self.assertEqual(reviewed[0]["status"],"NEEDS_INFORMATION")


if __name__=="__main__":
    unittest.main()
