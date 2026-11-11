"""Engineering amendment tests: AI hypotheses, owner review and atomic drafts."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_knowledge import EngineeringKnowledge
from studio_amendments import EngineeringAmendments
from studio_restore import preview,restore_to_empty


class AmendmentTests(unittest.TestCase):
    def setUp(self):
        temp=tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root=Path(temp.name)
        self.store=Store(self.root/"source.sqlite")
        self.project=self.store.create_project({
           "name":"Engineering",
           "summary":"Historical unapproved text",
           "body":"R1 EPB stage context","version":"R1"})["project"]["id"]
        self.other=self.store.create_project({"name":"Other"})["project"]["id"]
        self.knowledge=EngineeringKnowledge(self.store)
        self.a=EngineeringAmendments(self.store)
        self.obj=self.store.create_object({"project_id":self.project,
            "kind":"requirement","title":"CAN events",
            "body":"Decode DBC messages only","meta":{"source":"discussion://old"}})["item"]

    def propose(self,object_id=None,revision=None,knowledge_ids=None,patch=None):
        return self.a.propose(self.project,object_id or self.obj["id"],
            revision if revision is not None else self.obj["revision"],
            patch or {"body":"Decode and evaluate CAN events and timing"},
            "CAN response needs additional behavior analysis, not just DBC display",
            ["discussion://user-correction/can-processing"],
            knowledge_ids or [],"AI management","AI_CANDIDATE")

    def test_candidate_does_not_rewrite_existing_requirement(self):
        before=self.store.object(self.obj["id"])["item"]
        p=self.propose()
        self.assertEqual(p["state"],"CANDIDATE")
        self.assertEqual(self.store.object(self.obj["id"])["item"],before)
        detail=self.a.detail(p["id"])
        self.assertEqual(detail["base_snapshot"]["body"],before["body"])
        self.assertEqual(detail["proposed_patch"]["body"],
                         "Decode and evaluate CAN events and timing")
        self.assertEqual(detail["related_candidates"],[])

    def test_human_review_then_atomic_apply_to_new_draft_revision(self):
        approved=self.store.approve(self.obj["id"],self.obj["revision"],
                                     "Owner","Requirements review")["item"]
        proposal=self.propose(revision=approved["revision"])
        with self.assertRaisesRegex(RuleError,"scope review"):
            self.a.apply(proposal["id"],approved["revision"],"Human","No review")
        reviewed=self.a.review(proposal["id"],approved["revision"],"REVISE",
            "Human","Original requirement lacked observable timing behavior",
            ["review://requirements-and-field-feedback"])
        self.assertEqual(reviewed["state"],"READY_TO_APPLY")
        result=self.a.apply(proposal["id"],approved["revision"],"Human",
                            "Approved proposal to create an engineering draft")
        self.assertEqual(result["applied_revision"],approved["revision"]+1)
        self.assertEqual(result["object_state"],"DRAFT")
        self.assertEqual(self.store.object(self.obj["id"])["item"]["body"],
                         "Decode and evaluate CAN events and timing")
        self.assertIsNone(self.store.object(self.obj["id"])["item"]["approved_by"])
        with self.assertRaisesRegex(RuleError,"scope review"):
            self.a.apply(proposal["id"],approved["revision"],"Human","Twice")
        with self.store.db() as db:
            old=db.execute("SELECT snapshot FROM revisions WHERE object_id=? AND revision=?",
                           (self.obj["id"],approved["revision"])).fetchone()
            self.assertEqual(json.loads(old["snapshot"])["state"],"APPROVED")
            logs=[r["action"] for r in db.execute(
                "SELECT action FROM events WHERE project_id=?",(self.project,))]
            self.assertIn("APPLY_ENGINEERING_AMENDMENT",logs)

    def test_research_or_rejection_does_not_authorize_edits(self):
        x=self.propose()
        z=self.a.review(x["id"],self.obj["revision"],"RESEARCH",
                        "Owner","Need reference frames and timing trace",
                        ["study://physical-can-trace"])
        self.assertEqual(z["state"],"RESEARCH_NEEDED")
        with self.assertRaisesRegex(RuleError,"scope review"):
            self.a.apply(x["id"],self.obj["revision"],"Owner","Not complete")
        q=self.propose(patch={"title":"Alternative candidate title"})
        self.a.review(q["id"],self.obj["revision"],"REJECT","Owner",
             "Not justified for this ECU configuration",["source://scope"])
        self.assertEqual(self.store.object(self.obj["id"])["item"]["body"],
                         "Decode DBC messages only")

    def test_stale_version_or_changed_source_knowledge_never_applies(self):
        p=self.propose()
        self.store.update_object(self.obj["id"],{
            "expected_revision":self.obj["revision"],"body":"New field test revision"})
        with self.assertRaisesRegex(RuleError,"Target changed"):
            self.a.review(p["id"],self.obj["revision"],"REVISE","Owner",
                          "Old candidate",["review://version"])
        entry=self.knowledge.propose(self.project,{
            "title":"Additional timing issue",
            "statement":"CAN state sequence needs observation",
            "interpretation":"May affect runtime and test program",
            "concept_ids":["requirements","architecture"],
            "provenance":"AI_INTERPRETATION",
            "source_refs":["discussion://new-observation"],"related_objects":[]
        },"AI","AI_CANDIDATE")
        fresh=self.store.object(self.obj["id"])["item"]
        linked=self.propose(revision=fresh["revision"],knowledge_ids=[entry["id"]])
        self.knowledge.revise(entry["id"],1,{
            "title":"Additional timing issue","statement":"CAN state with timeout",
            "interpretation":"Additional qualifier",
            "concept_ids":["requirements","architecture"],"provenance":"AI_INTERPRETATION",
            "source_refs":["discussion://revised-observation"],"related_objects":[]
        },"AI","AI_CANDIDATE")
        with self.assertRaisesRegex(RuleError,"knowledge changed"):
            self.a.review(linked["id"],fresh["revision"],"REVISE","Owner",
                "Source changed",["review://new"])

    def test_cross_project_and_unsafe_patch_rejected(self):
        another=self.store.create_object({"project_id":self.other,"kind":"requirement",
                     "title":"Other requirement","body":"Unrelated"})["item"]
        with self.assertRaisesRegex(RuleError,"project boundaries"):
            self.propose(object_id=another["id"],revision=another["revision"])
        for patch in ({"state":"APPROVED"},{"approved_by":"AI"},{"meta":[]}):
            with self.assertRaises(RuleError):
                self.propose(patch=patch)
        with self.assertRaisesRegex(RuleError,"does not change"):
            self.propose(patch={"body":self.obj["body"]})

    def test_approved_links_surface_hypotheses_not_prejudged_impacts(self):
        target=self.store.create_object({"project_id":self.project,"kind":"design",
             "title":"CAN analysis design","body":"Uses decoded input"})["item"]
        link=self.store.create_relation({
            "source_id":self.obj["id"],"target_id":target["id"],"kind":"defines"})
        self.store.approve_relation(link["id"],"Owner","Reviewed requirement allocation")
        p=self.propose()
        detail=self.a.detail(p["id"])
        self.assertEqual(len(detail["related_candidates"]),1)
        self.assertEqual(detail["related_candidates"][0]["direction"],"downstream")
        self.assertEqual(detail["state"],"CANDIDATE")
        self.assertEqual(self.store.object(target["id"])["item"]["revision"],
                         target["revision"])

    def test_archive_roundtrip_preserves_candidates_and_applied_revisions(self):
        review=self.propose()
        self.a.review(review["id"],self.obj["revision"],"REVISE","Owner",
            "Changes required by the updated requirement scope",
            ["review://real-scope"])
        self.a.apply(review["id"],self.obj["revision"],"Owner","Create new draft")
        next_item=self.store.object(self.obj["id"])["item"]
        self.propose(revision=next_item["revision"],
                     patch={"title":"Alternative new candidate"})
        archive=self.store.export_project(self.project)
        self.assertEqual(len(archive["engineering_amendments"]),2)
        self.assertEqual(preview(archive)["status"],"STRUCTURAL_PRECHECK_PASS")
        target=self.root/"restored.sqlite"
        restore_to_empty(archive,target)
        restored=Store(target).export_project(self.project)
        self.assertEqual(restored["engineering_amendments"],
                         archive["engineering_amendments"])
        forged=json.loads(json.dumps(archive))
        forged["engineering_amendments"][0]["base_revision"]=999
        with self.assertRaisesRegex(RuleError,"missing project object revision"):
            preview(forged)

    def test_old_archives_have_no_implicit_amendment_approval(self):
        archive=self.store.export_project(self.project)
        archive.pop("engineering_amendments")
        restored=self.root/"old.sqlite"
        restore_to_empty(archive,restored)
        self.assertEqual(Store(restored).export_project(self.project)
                         ["engineering_amendments"],[])


if __name__=="__main__":
    unittest.main()
