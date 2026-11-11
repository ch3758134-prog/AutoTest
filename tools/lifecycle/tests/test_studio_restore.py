"""A full engineering archive must restore exactly or not change the workspace."""
from pathlib import Path
import copy
import tempfile
import unittest
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_research import Research
from studio_execution import Execution
from studio_management import Management
from studio_verification import Verification
from studio_restore import preview,restore_to_empty
from studio_governance import DefinitionGovernance,DOMAINS
from studio_definition_research import DefinitionResearch


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root=Path(self.tmp.name)
        self.original=Store(root/"original.sqlite3")
        self.research=Research(self.original)
        self.execution=Execution(self.original)
        self.manager=Management(self.original,self.execution,self.research)
        self.verification=Verification(self.original)
        self.target=root/"recovered.sqlite3"
        self.pid=self.original.create_project({
            "name":"Generic safety product","summary":"Includes non-ECU fields",
            "body":"Full design and evidence","version":"v1","variant":"B"})["project"]["id"]
        self.requirement=self.original.create_object({
            "project_id":self.pid,"kind":"requirement","title":"Pressure control",
            "body":"Detailed requirement and limits"})["item"]
        self.approved=self.original.approve(self.requirement["id"],1,"Owner","Reviewed source")
        self.test=self.original.create_object({"project_id":self.pid,
             "kind":"test","title":"Real bench test","body":"Measure output"})["item"]

    def archive(self):
        return self.original.export_project(self.pid)

    def test_roundtrip_preserves_project_relations_history_and_evidence(self):
        rel=self.original.create_relation({"source_id":self.requirement["id"],
            "target_id":self.test["id"],"kind":"verifies"})
        self.original.approve_relation(rel["id"],"Owner","Confirmed coverage")
        run=self.verification.record({
            "test_object_id":self.test["id"],"test_revision":1,
            "level":"BENCH","outcome":"FAIL","configuration":"board rev A",
            "source_sha":"c"*40,"evidence_ref":"private://logs/test.json",
            "environment":"real sensor input","actor":"Operator"})
        self.verification.review(run["id"],"CONFIRMED","Reviewer","Bench log confirmed")
        self.manager.propose({"project_id":self.pid,"category":"VERIFICATION",
            "priority":"P0","title":"Review failed test",
            "rationale":"Physical evidence not accepted","next_action":"Investigate cause",
            "proposed_by":"AI","source_objects":[self.test["id"]]})
        study=self.research.create({"project_id":self.pid,
            "title":"Pressure sensor variation","question":"Which sensor tolerance?",
            "constraints":"Low voltage","minimum_level":"ANALYSIS"})
        archive=self.archive()
        details=preview(archive)
        self.assertEqual(details["status"],"STRUCTURAL_PRECHECK_PASS")
        self.assertEqual(details["records"]["runs"],1)
        restored=restore_to_empty(archive,self.target)
        self.assertEqual(restored["project"]["id"],self.pid)
        rebuilt=Store(self.target)
        new_archive=rebuilt.export_project(self.pid)
        for key in ("project","objects","revisions","relations","events",
                    "technical_research","manager_proposals","verification"):
            self.assertEqual(new_archive[key],archive[key],key)
        self.assertEqual(new_archive["execution"]["repository"],None)
        self.assertEqual(len(new_archive["technical_research"]["studies"]),1)

    def test_governed_product_definition_roundtrip_and_tamper_rejection(self):
        gov=DefinitionGovernance(self.original)
        status=gov.status(self.pid)
        proposed=dict(status["definition"])
        proposed["summary"]="Versioned evidence-led test product"
        change=gov.propose(self.pid,proposed,
                           "Review product definition before engineering authorization",
                           ["study://product-inputs"],"Human",status["definition_digest"])
        coordinator=DefinitionResearch(self.original)
        for task in coordinator.list(change["change"]["id"])["tasks"]:
            submission=coordinator.submit(task["id"],task["revision"],"Analyst",
                "AI_CANDIDATE","Reviewed definition effect for "+task["domain"],
                ["study://product-inputs"],"Bound to the fixture")
            coordinator.review(task["id"],submission["submission_id"],
                task["revision"],"ACCEPT","Human","Checked source")
        for domain,_ in DOMAINS:
            gov.review(change["change"]["id"],domain,"NO_IMPACT",
                "This test fixture records a complete human domain review",
                ["study://product-inputs"],[],"Human",change["change"]["base_digest"])
        gov.publish(change["change"]["id"],"Human","Full review signed in test",
                    change["change"]["base_digest"])
        archive=self.archive()
        self.assertEqual(len(archive["governance"]["reviews"]),len(DOMAINS))
        restore_to_empty(archive,self.target)
        rebuilt=Store(self.target)
        self.assertEqual(rebuilt.export_project(self.pid)["governance"],
                         archive["governance"])
        self.assertEqual(DefinitionGovernance(rebuilt).status(self.pid)["baseline_status"],
                         "BASELINED")
        tampered=copy.deepcopy(archive)
        tampered["governance"]["baselines"][0]["digest"]="0"*64
        with self.assertRaisesRegex(RuleError,"digest"):
            preview(tampered)

    def test_corrupt_snapshot_is_rejected_without_writing(self):
        archive=self.archive()
        archive["objects"][0]["title"]="Tampered"
        with self.assertRaisesRegex(RuleError,"snapshot disagrees"):
            restore_to_empty(archive,self.target)
        self.assertFalse(self.target.exists())

    def test_unknown_cross_project_link_is_rejected(self):
        archive=self.archive()
        archive["relations"].append({
            "id":"fake","project_id":self.pid,"source_id":self.requirement["id"],
            "target_id":"unrelated","kind":"defines","status":"APPROVED"})
        with self.assertRaisesRegex(RuleError,"relationship"):
            preview(archive)

    def test_existing_workspace_cannot_be_overwritten(self):
        archive=self.archive()
        destination=Store(self.target)
        different=destination.create_project({"name":"Existing project"})["project"]["id"]
        with self.assertRaisesRegex(RuleError,"empty workspace"):
            restore_to_empty(archive,self.target)
        self.assertEqual(len(destination.projects()),1)
        self.assertEqual(destination.projects()[0]["id"],different)

    def test_export_recovery_with_github_work_package(self):
        self.execution.configure(self.pid,"ch3758134-prog/AutoTest")
        archive=self.archive()
        recovered=restore_to_empty(archive,self.target)
        self.assertEqual(recovered["project"]["name"],"Generic safety product")
        saved=Store(self.target).export_project(self.pid)
        self.assertEqual(saved["execution"]["repository"],"ch3758134-prog/AutoTest")

    def test_browser_recovery_is_explicitly_wired(self):
        base=Path(__file__).resolve().parents[1]
        server=(base/"studio_server.py").read_text(encoding="utf-8")
        html=(base/"studio.html").read_text(encoding="utf-8")
        wizard=(base/"studio_restore_ui.js").read_text(encoding="utf-8")
        self.assertIn('if path == "/restore.js":',server)
        self.assertIn('if path == "/api/studio/restore/preview":',server)
        self.assertIn('elif path == "/api/studio/restore/apply":',server)
        self.assertIn('RESTORE_TO_EMPTY_WORKSPACE',server)
        self.assertIn('<script src="/restore.js"></script>',html)
        self.assertIn('restoreWorkspaceModal',html)
        self.assertIn('restore/apply',wizard)
        self.assertIn('restore/preview',wizard)

    def test_wrong_schema_is_rejected(self):
        archive=self.archive()
        archive["schema"]="unsafe/2"
        with self.assertRaisesRegex(RuleError,"Unsupported"):
            preview(archive)


if __name__=="__main__":
    unittest.main()
