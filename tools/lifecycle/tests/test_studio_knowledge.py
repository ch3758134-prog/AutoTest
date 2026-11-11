"""Ledger tests: semantic knowledge is traceable but cannot self-approve or alter baselines."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_knowledge import EngineeringKnowledge
from studio_governance import DefinitionGovernance
from studio_restore import preview,restore_to_empty
from studio_research import Research
from studio_execution import Execution
from studio_management import Management


class EngineeringKnowledgeTests(unittest.TestCase):
    def setUp(self):
        work=tempfile.TemporaryDirectory();self.addCleanup(work.cleanup)
        self.root=Path(work.name)
        self.store=Store(self.root/"work.sqlite")
        self.project=self.store.create_project({
            "name":"AutoTest knowledge fixture",
            "summary":"Legacy engineering workspace description",
            "body":"Project background includes R1 and first ECU sample",
            "version":"R1","variant":"general"})["project"]["id"]
        self.other=self.store.create_project({"name":"Unrelated product"})["project"]["id"]
        self.k=EngineeringKnowledge(self.store)

    def entry(self,title="Stimulus and response",concepts=None):
        return {"title":title,
          "statement":"AutoTest includes software, main control board and key simulation board",
          "interpretation":"User corrected a software-only boundary; device limits still need evidence",
          "concept_ids":concepts or ["product_identity","system_boundary","decision"],
          "provenance":"USER_INPUT",
          "source_refs":["discussion://product-definition/correction-001"],
          "related_objects":[]}

    def propose(self,title="Boundary",project=None):
        return self.k.propose(project or self.project,self.entry(title),"AI manager","AI_CANDIDATE")

    def test_multiconcept_record_is_candidate_and_does_not_modify_product(self):
        before=DefinitionGovernance(self.store).status(self.project)
        item=self.propose()
        snapshot=self.k.list(self.project)
        self.assertEqual(snapshot["items"][0]["concept_ids"],
                         ["product_identity","system_boundary","decision"])
        self.assertEqual(snapshot["items"][0]["state"],"CANDIDATE")
        self.assertEqual(snapshot["revisions"][0]["revision"],1)
        self.assertEqual(DefinitionGovernance(self.store).status(self.project)["definition"],
                         before["definition"])
        brief=Management(self.store,Execution(self.store),Research(self.store)).context(self.project)
        self.assertEqual(len(brief["engineering_knowledge"]["items"]),1)
        self.assertEqual(brief["engineering_knowledge"]["items"][0]["origin_role"],
                         "AI_CANDIDATE")

    def test_owner_acceptance_does_not_publish_product_definition(self):
        item=self.propose()
        reviewed=self.k.review(item["id"],1,"ACCEPT","Human owner",
            "Reviewed the original user correction; no physical certification implied")
        self.assertEqual(reviewed["state"],"ACCEPTED")
        self.assertEqual(reviewed["revision"],2)
        self.assertEqual(DefinitionGovernance(self.store).status(self.project)["baseline_status"],
                         "UNBASELINED")
        with self.assertRaisesRegex(RuleError,"immutable"):
            self.k.revise(item["id"],2,self.entry("Attempt rewrite"),"AI","AI_CANDIDATE")
        self.assertEqual([r["revision"] for r in self.k.list(self.project)["revisions"]],[1,2])

    def test_candidate_revision_is_optimistic_and_history_is_complete(self):
        item=self.propose()
        entry=self.entry("Corrected description")
        revised=self.k.revise(item["id"],1,entry,"AI","AI_CANDIDATE")
        self.assertEqual(revised["revision"],2)
        with self.assertRaisesRegex(RuleError,"revision conflict"):
            self.k.revise(item["id"],1,entry,"AI","AI_CANDIDATE")
        self.k.review(item["id"],2,"REQUEST_RESEARCH","Human","Confirm hardware profile")
        state=self.k.list(self.project)
        self.assertEqual(state["items"][0]["state"],"RESEARCH_NEEDED")
        self.assertEqual(len(state["revisions"]),3)

    def test_semantic_link_is_candidate_until_both_ends_accepted(self):
        a=self.propose("System identity")
        b=self.propose("R1 project background")
        link=self.k.propose_link(self.project,a["id"],b["id"],"CONTEXT_FOR",
            "Current EPB implementation provides context for general product identity",
            "AI","AI_CANDIDATE")
        with self.assertRaisesRegex(RuleError,"currently accepted"):
            self.k.review_link(link["id"],"CONFIRM","Human","Not reviewed")
        self.k.review(a["id"],1,"ACCEPT","Human","Checked product identity")
        self.k.review(b["id"],1,"ACCEPT","Human","Checked project context")
        # Relation was proposed against candidate revisions; new accepted revisions
        # require a fresh relation proposal so stale judgments cannot be reused.
        # Acceptance changes only review status. The proposed semantic link
        # remains valid if the claim text and sources have not changed.
        confirmed=self.k.review_link(link["id"],"CONFIRM","Human",
            "Verified both approved statements retain the proposed meaning")
        self.assertEqual(confirmed["state"],"CONFIRMED")
        self.assertEqual(self.k.list(self.project)["links"][0]["source_revision"],2)

    def test_import_bundle_is_atomic_idempotent_and_preserves_links(self):
        one=self.entry("Hardware boundary")
        two=self.entry("Release context")
        one["alias"]="K01";two["alias"]="K02"
        bundle={"schema":"engineering-knowledge-intake/1",
                "entries":[one,two],
                "relations":[{"from":"K01","to":"K02","relation":"CONTEXT_FOR",
                   "rationale":"An implementation stage provides context, not product identity"}]}
        first=self.k.intake(self.project,bundle,"AI manager","AI_CANDIDATE")
        self.assertEqual(first["status"],"CANDIDATE_ONLY")
        self.assertEqual(len(first["items"]),2)
        repeat=self.k.intake(self.project,bundle,"AI manager","AI_CANDIDATE")
        self.assertTrue(repeat["already_imported"])
        self.assertEqual(len(self.k.list(self.project)["items"]),2)
        self.assertEqual(len(self.k.list(self.project)["links"]),1)
        archive=self.store.export_project(self.project)
        self.assertEqual(len(archive["knowledge"]["intakes"]),1)
        recovered=self.root/"intake-recovered.sqlite"
        restore_to_empty(archive,recovered)
        restored=EngineeringKnowledge(Store(recovered))
        again=restored.intake(self.project,bundle,"AI manager","AI_CANDIDATE")
        self.assertTrue(again["already_imported"])
        self.assertEqual(len(restored.list(self.project)["items"]),2)
        broken=json.loads(json.dumps(bundle))
        broken["relations"][0]["to"]="OTHER"
        with self.assertRaisesRegex(RuleError,"distinct intake aliases"):
            self.k.intake(self.project,broken,"AI manager","AI_CANDIDATE")
        self.assertEqual(len(self.k.list(self.project)["items"]),2)

    def test_changed_claim_content_stales_early_relation(self):
        first=self.propose("Claim one")
        second=self.propose("Claim two")
        link=self.k.propose_link(self.project,first["id"],second["id"],"REFINES",
            "Proposal against original content","AI","AI_CANDIDATE")
        modified=self.entry("Changed interpretation")
        modified["statement"]="Entirely different source content"
        self.k.revise(first["id"],1,modified,"AI","AI_CANDIDATE")
        self.k.review(first["id"],2,"ACCEPT","Human","Reviewed revised content")
        self.k.review(second["id"],1,"ACCEPT","Human","Reviewed second content")
        with self.assertRaisesRegex(RuleError,"semantic content changed"):
            self.k.review_link(link["id"],"CONFIRM","Human","Old reasoning")

    def test_cross_project_relation_and_invalid_concept_are_not_written(self):
        a=self.propose()
        b=self.propose("Other",self.other)
        with self.assertRaisesRegex(RuleError,"same project"):
            self.k.propose_link(self.project,a["id"],b["id"],"SUPPORTS",
                "Incorrect cross-project relation","AI","AI_CANDIDATE")
        entry=self.entry("Unknown concept",["fake_category"])
        with self.assertRaisesRegex(RuleError,"semantic handbook"):
            self.k.propose(self.project,entry,"AI","AI_CANDIDATE")

    def test_roundtrip_and_tampered_relation_rejected_atomically(self):
        a=self.propose()
        b=self.propose("Communication response")
        self.k.review(a["id"],1,"ACCEPT","Human","Confirmed user-provided boundary")
        self.k.review(b["id"],1,"ACCEPT","Human","Reviewed interpretation only")
        link=self.k.propose_link(self.project,a["id"],b["id"],"REFINES",
            "CAN RX observation refines product capabilities","AI","AI_CANDIDATE")
        archive=self.store.export_project(self.project)
        self.assertEqual(len(archive["knowledge"]["items"]),2)
        self.assertEqual(len(archive["knowledge"]["revisions"]),4)
        self.assertEqual(preview(archive)["status"],"STRUCTURAL_PRECHECK_PASS")
        dest=self.root/"recovered.sqlite"
        restore_to_empty(archive,dest)
        restored=Store(dest).export_project(self.project)
        self.assertEqual(restored["knowledge"],archive["knowledge"])
        altered=json.loads(json.dumps(archive))
        altered["knowledge"]["links"][0]["target_id"]="missing"
        with self.assertRaisesRegex(RuleError,"missing/cross-project"):
            preview(altered)

    def test_legacy_archive_without_knowledge_remains_restorable(self):
        archive=self.store.export_project(self.project)
        archive.pop("knowledge")
        self.assertEqual(preview(archive)["status"],"STRUCTURAL_PRECHECK_PASS")
        dest=self.root/"legacy.sqlite"
        restore_to_empty(archive,dest)
        self.assertEqual(Store(dest).export_project(self.project)["knowledge"],
                         {"items":[],"revisions":[],"links":[],"intakes":[]})


if __name__=="__main__":
    unittest.main()
