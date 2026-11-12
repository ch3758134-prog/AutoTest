import copy
import importlib.util
import pathlib
import unittest

source = pathlib.Path(__file__).resolve().parents[1] / "portable" / "delivery_review.py"
spec = importlib.util.spec_from_file_location("delivery_review", source)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def payload():
    return {
        "id": "ART-01", "kind": "REQUIREMENTS", "title": "G1 whole-system needs",
        "why": "Avoid design-first development without user-facing requirements.",
        "body": "# Entire reviewable requirement\nActual complete content and acceptance boundary.",
        "scope": "Whole system behavior, not implementation permission.",
        "changes": "Compared with prior approved context; no new baseline claimed.",
        "limitations": "Hardware power output not physically verified.",
        "owner_question": "May the exact candidate be posted to GitHub for review?",
        "publication_type": "GITHUB_CANDIDATE_COMMENT",
        "publication_target": "owner/example issue #22",
        "source_refs": ["product definition@commit", "G1 study@revision"],
    }

def stage(row=None):
    r={"delivery_reviews":[]}
    d=payload() if row is None else row
    out=m.apply(r,"stage_delivery",d,at="2026-11-12T00:00:00Z")
    return r,out

def next_payload(row):
    return {"id":row["id"], "delivery_revision":row["revision"],
            "expected_sha256":row["sha256"]}

def offer(r):
    row=r["delivery_reviews"][0]
    return m.apply(r,"offer_delivery_review",next_payload(row),
                   at="2026-11-12T00:01:00Z")

def decide(r,decision="ALLOW_CANDIDATE_PUBLICATION",authenticated=True):
    row=r["delivery_reviews"][0]
    return m.apply(r,"owner_decide_delivery",{**next_payload(row),
              "decision":decision,"reason":"Reviewed actual content and limits",
              "confirm_exact_content":True},
              at="2026-11-12T00:02:00Z",owner_channel=authenticated)

class DeliveryLifecycleTests(unittest.TestCase):
    def test_saved_internal_draft_not_delivered(self):
        r,out=stage()
        self.assertEqual(out["status"],"DRAFT_INTERNAL")
        self.assertFalse(m.candidate_publication_permitted(r,"ART-01",out["sha256"]))

    def test_full_artifact_and_scope_required(self):
        for field in ("body","source_refs","changes","scope","limitations","owner_question",
                      "publication_target"):
            with self.subTest(field=field):
                p=payload();p[field]=[] if field=="source_refs" else ""
                with self.assertRaises(m.DeliveryError):stage(p)

    def test_offer_only_marks_pending_not_owner_has_read(self):
        r,_=stage()
        out=offer(r)
        self.assertEqual(out["status"],"AWAITING_OWNER_REVIEW")
        self.assertFalse(out["owner_has_read"])
        self.assertFalse(out["publicly_delivered"])

    def test_general_manager_cannot_approve_candidate_publication(self):
        r,_=stage();offer(r)
        with self.assertRaises(m.DeliveryError):decide(r,authenticated=False)
        self.assertEqual(r["delivery_reviews"][0]["status"],"AWAITING_OWNER_REVIEW")

    def test_review_requires_exact_revision_digest_and_confirmation(self):
        r,_=stage();offer(r)
        good=next_payload(r["delivery_reviews"][0])
        for wrong in ({"delivery_revision":99},{"expected_sha256":"x"*64}):
            with self.subTest(wrong=wrong):
                with self.assertRaises(m.DeliveryError):
                    m.apply(r,"owner_decide_delivery",{
                        **good,**wrong,"decision":"ALLOW_CANDIDATE_PUBLICATION",
                        "reason":"examined", "confirm_exact_content":True},
                        at="t",owner_channel=True)
        with self.assertRaises(m.DeliveryError):
            m.apply(r,"owner_decide_delivery",{
              **good, "decision":"ALLOW_CANDIDATE_PUBLICATION","reason":"examined"},
              at="t",owner_channel=True)

    def test_owner_release_is_candidate_only_not_formal_approval(self):
        r,_=stage();offer(r)
        out=decide(r)
        self.assertEqual(out["status"],"RELEASED_FOR_CANDIDATE_PUBLICATION")
        self.assertFalse(out["formal_product_approval"])
        self.assertFalse(out["execution_authorization"])
        self.assertTrue(m.candidate_publication_permitted(r,"ART-01",out["sha256"]))
        self.assertFalse(m.candidate_publication_permitted(r,"ART-01","a"*64))
        self.assertFalse(m.candidate_publication_permitted(r,"OTHER",out["sha256"]))

    def test_return_for_changes_never_allows_publication(self):
        r,out=stage();offer(r)
        result=decide(r,"REQUEST_CHANGES")
        self.assertEqual(result["status"],"RETURNED_FOR_REVISION")
        self.assertFalse(m.candidate_publication_permitted(r,"ART-01",out["sha256"]))

    def test_hold_is_not_approval(self):
        r,out=stage();offer(r)
        self.assertEqual(decide(r,"HOLD")["status"],"ON_HOLD")
        self.assertFalse(m.candidate_publication_permitted(r,"ART-01",out["sha256"]))

    def test_revise_changes_digest_and_invalidates_prior_release(self):
        r,_=stage();offer(r)
        approval=decide(r)
        old=copy.deepcopy(r["delivery_reviews"][0])
        update={**next_payload(old),**payload(),"body":"Changed complete new document"}
        result=m.apply(r,"revise_delivery",update,at="2026-11-12T00:03:00Z")
        self.assertEqual(result["status"],"DRAFT_INTERNAL")
        self.assertNotEqual(result["sha256"],approval["sha256"])
        self.assertEqual(len(r["delivery_reviews"][0]["history"]),1)
        self.assertFalse(m.candidate_publication_permitted(r,"ART-01",result["sha256"]))
        self.assertFalse(m.candidate_publication_permitted(r,"ART-01",approval["sha256"]))

    def test_unannounced_rewrite_invalidates_record(self):
        r,_=stage()
        r["delivery_reviews"][0]["body"]="silently replaced"
        with self.assertRaises(m.DeliveryError):m.items(r)

    def test_duplicate_id_and_review_replay_rejected(self):
        r,_=stage()
        with self.assertRaises(m.DeliveryError):m.apply(r,"stage_delivery",payload(),at="t")
        offer(r);decide(r)
        with self.assertRaises(m.DeliveryError):decide(r)

    def test_manager_never_generates_formal_approval_even_after_release(self):
        r,_=stage();offer(r);decide(r)
        row=r["delivery_reviews"][0]
        self.assertNotIn("APPROVED",row["status"])
        self.assertEqual(row["owner_release"]["authority"],
                         "LOCAL_REVIEW_TOKEN_NOT_VERIFIED_FORMAL_IDENTITY")

if __name__ == "__main__":
    unittest.main()
