"""Canonical product identity and controlled engineering impact tests."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_product_identity import ProductIdentity
from studio_knowledge import EngineeringKnowledge
from studio_restore import preview,restore_to_empty


class CanonicalIdentityTests(unittest.TestCase):
    def setUp(self):
        temp=tempfile.TemporaryDirectory();self.addCleanup(temp.cleanup)
        self.root=Path(temp.name)
        self.store=Store(self.root/"source.sqlite")
        self.project=self.store.create_project({
            "name":"AutoTest",
            "summary":"Legacy software-only definition (not approved)",
            "body":"R1 first uses EPB and currently has CAN1 RX observations",
            "version":"R1","variant":"EPB prototype"})["project"]["id"]
        self.k=EngineeringKnowledge(self.store)
        self.identity=ProductIdentity(self.store)
        self.base=self.identity.status(self.project)

    def draft(self,statement="A hardware-software ECU stimulus and response testing system",
              knowledge_ids=None):
        return self.identity.propose(
            self.project,statement,
            "The product contains host, main board and keyboard. R1 and EPB are stage context.",
            ["discussion://2026-11-10/autotest-product-boundary"],
            knowledge_ids or [],"AI manager","AI_CANDIDATE",self.base["digest"])

    def scope(self,proposal):
        return self.identity.review_scope(proposal["id"],self.base["digest"],
            "Owner","Compared the user's corrected product identity against the current engineering scope",
            ["discussion://product-identity-review"])

    def test_legacy_project_text_is_never_the_canonical_identity(self):
        self.assertEqual(self.base["state"],"UNBASELINED")
        self.assertIsNone(self.base["baseline"])
        self.assertEqual(self.base["legacy_project_context"]["version"],"R1")
        proposed=self.draft()
        self.assertEqual(proposed["state"],"CANDIDATE")
        self.assertEqual(self.identity.status(self.project)["state"],"UNBASELINED")
        self.scope(proposed)
        published=self.identity.publish(proposed["id"],self.base["digest"],
                                        "Owner","Reviewed product identity only")
        self.assertEqual(published["state"],"BASELINED")
        self.assertEqual(published["baseline"]["statement"],
           "A hardware-software ECU stimulus and response testing system")
        self.assertEqual(self.store.project(self.project)["project"]["summary"],
                         "Legacy software-only definition (not approved)")
        self.assertEqual(self.store.project(self.project)["project"]["version"],"R1")
        self.assertEqual(self.store.project(self.project)["project"]["variant"],"EPB prototype")

    def test_owner_review_must_precede_publication_and_unknown_impact_blocks(self):
        proposed=self.draft()
        with self.assertRaisesRegex(RuleError,"scope assessment"):
            self.identity.publish(proposed["id"],self.base["digest"],"Owner","Too early")
        affected=self.store.create_object({"project_id":self.project,"kind":"requirement",
                         "title":"CAN response analysis requirement","body":"Analyze ECU CAN behavior"})["item"]
        self.identity.assess_impact(proposed["id"],affected["id"],affected["revision"],
            "UNKNOWN","Timing behavior not fully researched",["review://missing-coverage"],"Owner")
        self.scope(proposed)
        with self.assertRaisesRegex(RuleError,"Unresolved engineering impact"):
            self.identity.publish(proposed["id"],self.base["digest"],"Owner","Still unknown")
        self.identity.assess_impact(proposed["id"],affected["id"],affected["revision"],
            "IMPACT","CAN behavior needs new requirement revision",
            ["discussion://ecus-can-analysis"],"Owner")
        self.assertTrue(self.identity.detail(proposed["id"])["gate"]["ready"])
        self.identity.publish(proposed["id"],self.base["digest"],"Owner","Scoped review complete")
        with self.assertRaisesRegex(RuleError,"scope assessment"):
            self.identity.publish(proposed["id"],self.base["digest"],"Owner","Repeat release")

    def test_specific_implementation_impact_blocks_readiness_without_global_stop(self):
        a=self.store.create_object({"project_id":self.project,"kind":"work_package",
                 "title":"Affected test engine","body":"Implementation package"})["item"]
        b=self.store.create_object({"project_id":self.project,"kind":"work_package",
                 "title":"Unrelated help documents","body":"Unrelated work"})["item"]
        proposal=self.draft()
        self.scope(proposal)
        self.identity.assess_impact(proposal["id"],a["id"],a["revision"],
             "IMPACT","Must revisit mapping after product identity revision",
             ["source://reviewed-impact"],"Owner")
        self.identity.publish(proposal["id"],self.base["digest"],"Owner",
                              "Confirmed specific impact, no fabricated global stop")
        invalid=self.identity.invalidations(self.project)
        self.assertEqual(len(invalid),1)
        self.assertEqual(invalid[0]["package_id"],a["id"])
        affected=self.store.readiness(a["id"])["reasons"]
        unaffected=self.store.readiness(b["id"])["reasons"]
        self.assertTrue(any("产品身份修订" in x for x in affected))
        self.assertFalse(any("产品身份修订" in x for x in unaffected))

    def test_stale_identity_digest_cannot_approve_two_competing_proposals(self):
        first=self.draft()
        second=self.draft("A different proposal considered in parallel")
        self.scope(first)
        self.scope(second)
        self.identity.publish(first["id"],self.base["digest"],"Owner","Only first adopted")
        with self.assertRaisesRegex(RuleError,"baseline"):
            self.identity.publish(second["id"],self.base["digest"],"Owner","Old decision")
        self.assertEqual(self.identity.status(self.project)["revision"],1)

    def test_referenced_knowledge_requires_owner_review(self):
        entry={"title":"System boundary","statement":"Main and key boards are inside AutoTest",
               "interpretation":"User correction applies to product scope",
               "concept_ids":["product_identity","system_boundary"],
               "provenance":"USER_INPUT","source_refs":["discussion://board-membership"],
               "related_objects":[]}
        knowledge=self.k.propose(self.project,entry,"AI manager","AI_CANDIDATE")
        proposal=self.draft(knowledge_ids=[knowledge["id"]])
        self.scope(proposal)
        with self.assertRaisesRegex(RuleError,"Linked knowledge must be reviewed"):
            self.identity.publish(proposal["id"],self.base["digest"],"Owner","Not yet")
        self.k.review(knowledge["id"],1,"ACCEPT","Owner",
                      "Checked input is an actual user correction")
        self.identity.publish(proposal["id"],self.base["digest"],"Owner","Review complete")

    def test_ai_linked_engineering_knowledge_appears_as_reviewable_impact_candidate(self):
        requirement=self.store.create_object({"project_id":self.project,
            "kind":"requirement","title":"Observe CAN response states",
            "body":"CAN response conditions require clarification"})["item"]
        knowledge=self.k.propose(self.project,{
            "title":"CAN response usage","statement":"CAN RX supports more than waveform display",
            "interpretation":"The existing requirement may need explicit timing and state logic",
            "concept_ids":["capability","requirements","architecture"],
            "provenance":"AI_INTERPRETATION",
            "source_refs":["discussion://rx-analysis-correction"],
            "related_objects":[requirement["id"]]
        },"AI management","AI_CANDIDATE")
        proposal=self.draft(knowledge_ids=[])
        detail=self.identity.detail(proposal["id"])
        suggested=detail["knowledge_impact_candidates"]
        self.assertEqual(len(suggested),1)
        self.assertEqual(suggested[0]["knowledge_id"],knowledge["id"])
        self.assertEqual(suggested[0]["object_id"],requirement["id"])
        self.assertEqual(suggested[0]["knowledge_state"],"CANDIDATE")
        self.assertEqual(detail["impacts"],[])
        self.identity.assess_impact(proposal["id"],requirement["id"],
              requirement["revision"],"UNKNOWN",
              "Original AI hypothesis needs actual requirement review",
              ["discussion://rx-analysis-correction"],"Owner")
        self.assertEqual(self.identity.detail(proposal["id"])["impacts"][0]
                         ["disposition"],"UNKNOWN")

    def test_exact_archive_roundtrip_and_invalid_impact_is_rejected(self):
        package=self.store.create_object({"project_id":self.project,"kind":"work_package",
                           "title":"Reevaluate CAN device integration","body":"Work"})["item"]
        proposal=self.draft()
        self.scope(proposal)
        self.identity.assess_impact(proposal["id"],package["id"],package["revision"],
            "IMPACT","Current DUT paths might change",["evidence://hardware-read"],"Owner")
        self.identity.publish(proposal["id"],self.base["digest"],"Owner","Scoped impact reviewed")
        archive=self.store.export_project(self.project)
        self.assertEqual(len(archive["product_identity"]["baselines"]),1)
        self.assertEqual(preview(archive)["status"],"STRUCTURAL_PRECHECK_PASS")
        path=self.root/"restored.sqlite"
        restore_to_empty(archive,path)
        self.assertEqual(Store(path).export_project(self.project)["product_identity"],
                         archive["product_identity"])
        forged=json.loads(json.dumps(archive))
        forged["product_identity"]["impacts"][0]["object_id"]="nonexistent"
        with self.assertRaisesRegex(RuleError,"invalid object revision"):
            preview(forged)

    def test_stage_and_project_context_change_without_identity_version_change(self):
        proposal=self.draft()
        self.scope(proposal)
        self.identity.publish(proposal["id"],self.base["digest"],"Owner",
                              "Product identity reviewed separately")
        before=self.identity.status(self.project)
        self.store.update_project(self.project,{
            "version":"R2","variant":"next bench",
            "body":"New stage now considers another ECU","actor":"Project owner"})
        after=self.identity.status(self.project)
        self.assertEqual(after["digest"],before["digest"])
        self.assertEqual(after["revision"],before["revision"])
        self.assertEqual(after["legacy_project_context"]["version"],"R2")
        history=self.store.project_context_history(self.project)
        self.assertEqual(len(history),2)
        self.assertEqual(history[0]["revision"],1)
        self.assertEqual(history[1]["revision"],2)
        archive=self.store.export_project(self.project)
        path=self.root/"context.sqlite"
        restore_to_empty(archive,path)
        restored=Store(path)
        self.assertEqual(restored.project_context_history(self.project),history)
        self.assertEqual(ProductIdentity(restored).status(self.project)["baseline"],
                         before["baseline"])

    def test_old_archive_is_readable_but_not_silently_migrated(self):
        archive=self.store.export_project(self.project)
        archive.pop("product_identity",None)
        path=self.root/"legacy-restore.sqlite"
        restore_to_empty(archive,path)
        restored=Store(path)
        status=ProductIdentity(restored).status(self.project)
        self.assertEqual(status["state"],"UNBASELINED")
        self.assertIn("Legacy",status["warning"])


if __name__=="__main__":
    unittest.main()
