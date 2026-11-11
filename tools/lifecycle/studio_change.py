"""Human-reviewed change impact assessment; no design edits or approvals are implied."""
from __future__ import annotations
import json
from studio_store import Store, RuleError, text_value, now

OUTCOMES = ("REVISE", "NO_CHANGE", "INVESTIGATE")


class ChangeReview:
    def __init__(self, store: Store):
        self.store = store

    def detail(self, change_id):
        with self.store.db() as db:
            change = self.store._require_object(db, change_id)
            if change["kind"] != "change":
                raise RuleError("Selected object is not a change")
            rows = [dict(r) for r in db.execute(
                "SELECT * FROM relations WHERE source_id=? AND kind='impacts' ORDER BY created, id",
                (change_id,))]
            assessments = change["meta"].get("assessments", {})
            details = change["meta"].get("assessment_details", {})
            targets = []
            for rel in rows:
                target = self.store._require_object(db, rel["target_id"])
                decision = details.get(target["id"], {})
                targets.append({
                    "relation": rel, "target": {
                        "id": target["id"], "kind": target["kind"],
                        "title": target["title"], "revision": target["revision"],
                        "state": target["state"]},
                    "assessment": {
                        "outcome": assessments.get(target["id"]),
                        "rationale": decision.get("rationale"),
                        "reviewer": decision.get("reviewer"),
                        "reviewed_at": decision.get("reviewed_at"),
                        "target_revision": decision.get("target_revision"),
                    },
                    "stale": bool(decision and decision.get("target_revision") != target["revision"]),
                })
            return {"change":change, "targets":targets,
                    "note":"A reviewed relation is required for an impact decision. No automatic design revision or verification is inferred."}

    def assess(self, change_id, target_id, expected_change_revision,
               expected_target_revision, outcome, reviewer, rationale):
        if outcome not in OUTCOMES:
            raise RuleError("Unknown impact assessment outcome")
        reviewer = text_value(reviewer, "reviewer", limit=150, mandatory=True)
        rationale = text_value(rationale, "impact rationale", limit=5000, mandatory=True)
        with self.store.db() as db:
            change = self.store._require_object(db, change_id)
            target = self.store._require_object(db, target_id)
            if change["kind"] != "change" or change["state"] != "OPEN":
                raise RuleError("Only open changes can receive impact assessments")
            if change["project_id"] != target["project_id"]:
                raise RuleError("Change and target belong to different projects")
            if type(expected_change_revision) is not int or change["revision"] != expected_change_revision:
                raise RuleError("Change revision conflict: refresh before assessing")
            if type(expected_target_revision) is not int or target["revision"] != expected_target_revision:
                raise RuleError("Target revision conflict: review latest target before assessing")
            rel = db.execute(
                """SELECT id,status FROM relations WHERE source_id=? AND target_id=?
                   AND kind='impacts'""", (change_id,target_id)).fetchone()
            if not rel or rel["status"] != "APPROVED":
                raise RuleError("Impact relationship must be explicitly approved before assessment")
            meta = dict(change["meta"])
            decisions = meta.get("assessments", {})
            details = meta.get("assessment_details", {})
            if not isinstance(decisions,dict) or not isinstance(details,dict):
                raise RuleError("Existing impact assessment metadata is malformed")
            decisions = dict(decisions)
            details = dict(details)
            decisions[target_id] = outcome
            details[target_id] = {
                "outcome":outcome, "rationale":rationale,"reviewer":reviewer,
                "reviewed_at":now(),"target_revision":target["revision"]}
            meta["assessments"] = decisions
            meta["assessment_details"] = details
            db.execute("""UPDATE objects SET meta=?,revision=?,updated=?
                          WHERE id=?""",
                       (json.dumps(meta,ensure_ascii=False),change["revision"]+1,now(),change_id))
            self.store._record(db,self.store._require_object(db,change_id),"ASSESS_IMPACT",reviewer)
            self.store._audit(db,change["project_id"],change_id,"ASSESS_IMPACT",reviewer,
                              outcome+" / "+target_id+" r"+str(target["revision"])+" / "+rationale)
        return self.detail(change_id)
