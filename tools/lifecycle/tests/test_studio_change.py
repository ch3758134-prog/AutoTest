"""Revision-pinned change review end-to-end business tests (stdlib only)."""
from pathlib import Path
import tempfile
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from studio_store import Store, RuleError
from studio_change import ChangeReview


class ChangeReviewTest(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store=Store(Path(self.tmp.name)/"changes.sqlite3")
        self.project=self.store.create_project({"name":"Physical Engineering","summary":"Real evidence"})["project"]["id"]
        self.review=ChangeReview(self.store)

    def obj(self,kind,name,project=None):
        return self.store.create_object({"project_id":project or self.project,
                "kind":kind,"title":name,"body":"reason and evidence"})["item"]

    def link(self,change,target,approved=True):
        r=self.store.create_relation({"source_id":change["id"],"target_id":target["id"],"kind":"impacts"})
        if approved:self.store.approve_relation(r["id"],"Human","Reviewed relationship")
        return r

    def test_requires_confirmed_relationship_and_explicit_reason(self):
        change=self.obj("change","Evidence change")
        target=self.obj("requirement","Affected requirement")
        self.link(change,target,False)
        with self.assertRaisesRegex(RuleError,"explicitly approved"):
            self.review.assess(change["id"],target["id"],1,1,"REVISE","Human","Reason")
        self.store.approve_relation(self.store.project(self.project)["relations"][0]["id"],
                                   "Human","Reviewed evidence")
        with self.assertRaises(RuleError):
            self.review.assess(change["id"],target["id"],1,1,"REVISE","Human","")
        result=self.review.assess(change["id"],target["id"],1,1,"REVISE","Human","Must change behavior")
        self.assertEqual(result["change"]["revision"],2)
        self.assertEqual(result["targets"][0]["assessment"]["target_revision"],1)
        self.assertFalse(result["targets"][0]["stale"])

    def test_target_edit_invalidates_closed_gate(self):
        change=self.obj("change","Experiment contradicts design")
        target=self.obj("requirement","Original requirement")
        self.link(change,target)
        result=self.review.assess(change["id"],target["id"],1,1,"NO_CHANGE","Human","Current revision is sufficient")
        self.store.update_object(target["id"],{"expected_revision":1,"body":"Changed requirement"})
        detail=self.review.detail(change["id"])
        self.assertTrue(detail["targets"][0]["stale"])
        with self.assertRaisesRegex(RuleError,"missing or stale"):
            self.store.close_change(change["id"],result["change"]["revision"],"Human","Closed")
        with self.assertRaisesRegex(RuleError,"Target revision conflict"):
            self.review.assess(change["id"],target["id"],2,1,"REVISE","Human","Stale snapshot")
        refreshed=self.review.assess(change["id"],target["id"],2,2,"REVISE","Human","New revision now needs change")
        closed=self.store.close_change(change["id"],refreshed["change"]["revision"],
                                        "Human","Reviewed updated revision")
        self.assertEqual(closed["item"]["state"],"CLOSED")

    def test_multi_target_review_and_optimistic_concurrency(self):
        change=self.obj("change","Mixed impact")
        first=self.obj("interface","Impact one")
        second=self.obj("design","Impact two")
        self.link(change,first)
        self.link(change,second)
        self.review.assess(change["id"],first["id"],1,1,"REVISE","Human","Interface changes")
        with self.assertRaisesRegex(RuleError,"Change revision conflict"):
            self.review.assess(change["id"],second["id"],1,1,"INVESTIGATE","Human","Old change revision")
        with self.assertRaisesRegex(RuleError,"Assess each"):
            self.store.close_change(change["id"],2,"Human","Premature")
        record=self.review.assess(change["id"],second["id"],2,1,"INVESTIGATE","Human","Needs experiment")
        self.assertEqual(len(record["targets"]),2)
        self.store.close_change(change["id"],record["change"]["revision"],"Human","Both reviewed")
        with self.assertRaisesRegex(RuleError,"Only open changes"):
            self.review.assess(change["id"],first["id"],4,1,"NO_CHANGE","Human","Too late")

    def test_cross_project_and_wrong_kind_are_rejected(self):
        other=self.store.create_project({"name":"Unrelated"})["project"]["id"]
        change=self.obj("change","A")
        foreign=self.obj("requirement","B",other)
        with self.assertRaisesRegex(RuleError,"different projects"):
            self.review.assess(change["id"],foreign["id"],1,1,"REVISE","Human","Reason")
        requirement=self.obj("requirement","C")
        with self.assertRaisesRegex(RuleError,"Only open changes"):
            self.review.assess(requirement["id"],change["id"],1,1,"REVISE","Human","Reason")

    def test_malformed_and_legacy_unpinned_assessments_cannot_close(self):
        change=self.obj("change","Legacy impact")
        target=self.obj("requirement","Target")
        self.link(change,target)
        self.store.update_object(change["id"],{"expected_revision":1,
                     "meta":{"assessments":{target["id"]:"NO_CHANGE"}}})
        with self.assertRaisesRegex(RuleError,"missing or stale"):
            self.store.close_change(change["id"],2,"Human","Legacy approval incomplete")

    def test_capture_is_atomic_and_candidate_only(self):
        target=self.obj("capability","Monitor output")
        created=self.review.capture(target["id"],1,"Observed wrong display",
                                    "Observed frames were not refreshed",
                                    "lab://session/123","Human")
        change=created["change"]
        self.assertEqual(change["kind"],"change")
        self.assertEqual(change["state"],"OPEN")
        self.assertEqual(change["meta"]["origin_target_revision"],1)
        self.assertEqual(len(created["targets"]),1)
        self.assertEqual(created["targets"][0]["relation"]["status"],"CANDIDATE")
        with self.assertRaisesRegex(RuleError,"explicitly approved"):
            self.review.assess(change["id"],target["id"],1,1,"REVISE","Human","Confirmed")
        self.assertEqual(len(self.store.project(self.project)["objects"]),2)
        self.assertTrue(any(x["action"]=="CAPTURE_FINDING"
                            for x in self.store.project(self.project)["events"]))

    def test_capture_stale_target_does_not_create_partial_change(self):
        target=self.obj("design","Original design")
        self.store.update_object(target["id"],{"expected_revision":1,"body":"Revised"})
        before=self.store.project(self.project)
        with self.assertRaisesRegex(RuleError,"Target revision conflict"):
            self.review.capture(target["id"],1,"Wrong report","Detail",
                                "lab://report","Human")
        after=self.store.project(self.project)
        self.assertEqual(len(after["objects"]),len(before["objects"]))
        self.assertEqual(len(after["relations"]),len(before["relations"]))

    def test_change_review_ui_routes_are_wired(self):
        server=(Path(__file__).resolve().parents[1]/"studio_server.py").read_text(encoding="utf-8")
        html=(Path(__file__).resolve().parents[1]/"studio.html").read_text(encoding="utf-8")
        self.assertIn('if path == "/api/studio/change/review":',server)
        self.assertIn('elif path == "/api/studio/change/capture":',server)
        self.assertIn('elif path == "/api/studio/change/assess":',server)
        self.assertIn('if not owner_request:',server)
        self.assertIn('if path == "/change.js":',server)
        self.assertIn('<script src="/change.js"></script>',html)
        self.assertIn("changeReviewModal(selected.id)",html)
        self.assertIn("captureFindingModal(selected)",html)


if __name__=="__main__":
    unittest.main()
