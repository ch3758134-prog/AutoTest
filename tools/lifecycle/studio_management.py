"""Read-only project manager brief and proposal-only intake.

This adapter deliberately has NO APIs for approving requirements, design,
experiments, coding dispatch, Git merge, or physical verification. A manager
proposal is advisory until a separate owner review; review does not silently
materialize an approved engineering object.
"""
from __future__ import annotations

import hashlib
import json
from studio_store import Store, RuleError, ident, now, text_value
from studio_verification import Verification

PROPOSAL_KINDS = ("REQUIREMENT_CLARIFICATION", "TECHNICAL_RESEARCH", "ARCHITECTURE_REVIEW",
                  "DETAILED_DESIGN", "CODING", "INTEGRATION", "VERIFICATION",
                  "SCHEDULE", "BLOCKER")
PRIORITIES = ("P0", "P1", "P2", "P3")
DECISIONS = ("ACCEPT_FOR_PLANNING", "REJECT", "NEEDS_INFORMATION")


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False,
                                     separators=(",", ":")).encode("utf-8")).hexdigest()


class Management:
    def __init__(self, store: Store, execution, research):
        self.store, self.execution, self.research = store, execution, research
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS manager_proposals(
                  id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                  category TEXT NOT NULL, priority TEXT NOT NULL,
                  title TEXT NOT NULL, rationale TEXT NOT NULL,
                  next_action TEXT NOT NULL, source_objects TEXT NOT NULL,
                  source_manifest_sha256 TEXT NOT NULL,
                  proposed_by TEXT NOT NULL, status TEXT NOT NULL,
                  reviewer TEXT, review_note TEXT, created TEXT NOT NULL,
                  reviewed_at TEXT);
                CREATE INDEX IF NOT EXISTS idx_manager_proposals
                  ON manager_proposals(project_id,status);
            """)

    def _snapshot(self, project_id):
        """A reproducible brief: data labels include source and staleness."""
        p = self.store.project(project_id)
        exe = self.execution.overview(project_id)
        studies = self.research.list(project_id)
        verification = Verification(self.store).list(project_id)
        objects = p["objects"]
        indexed = {x["id"]: x for x in objects}
        ready = {}
        for obj in objects:
            if obj["kind"] == "work_package":
                ready[obj["id"]] = self.store.readiness(obj["id"])
        open_changes = [x for x in objects if x["kind"] == "change"
                        and x["state"] != "CLOSED"]
        draft_design = [x for x in objects
                        if x["kind"] in ("architecture", "interface", "design")
                        and x["state"] != "APPROVED"]
        technical = [x for x in studies if x["status"] != "DECIDED"]
        blockers = []
        for x in open_changes:
            blockers.append({"type":"CHANGE_REVIEW","object_id":x["id"],
                             "reason":"Change is not closed: "+x["title"]})
        for x in draft_design:
            blockers.append({"type":"DESIGN_REVIEW","object_id":x["id"],
                             "reason":"Design revision is not approved: "+x["title"]})
        for x in technical:
            blockers.append({"type":"TECHNICAL_RESEARCH","study_id":x["id"],
                             "reason":"Technical decision is pending: "+x["title"]})
        for wid, check in ready.items():
            if not check["ready"]:
                blockers.append({"type":"CODE_READY","object_id":wid,
                                 "reason":"; ".join(check["reasons"])})
        for task in exe["tasks"]:
            if task["effective_state"] == "BLOCKED_BY_CHANGED_DESIGN":
                blockers.append({"type":"STALE_TASK","object_id":task["object_id"],
                                 "reason":"Dispatched coding packet became stale"})
        for run in verification["runs"]:
            if run["review_decision"] is None:
                blockers.append({"type":"EVIDENCE_REVIEW","object_id":run["test_object_id"],
                                 "reason":"Execution evidence is not reviewed: "+run["id"]})
            elif run["review_decision"]=="CONFIRMED" and run["outcome"]=="FAIL":
                blockers.append({"type":"CONFIRMED_TEST_FAILURE",
                                 "object_id":run["test_object_id"],
                                 "reason":"Reviewed failure evidence: "+run["id"]+
                                           " / "+run["configuration"]})
        return {
            "schema":"engineering-manager-brief/1",
            "project":p["project"],
            "engineering_objects":objects,
            "engineering_relations":p["relations"],
            "technical_studies":studies,
            "verification_runs":verification["runs"],
            "verification_note":verification["note"],
            "implementation_readiness":ready,
            "github":{"repository":exe["repository"],"fetched_at":exe["fetched_at"],
                      "error":exe["sync_error"],"snapshot":exe["snapshot"]},
            "execution_tasks":exe["tasks"],
            "blockers":blockers,
            "advisory_instructions":[
                "This brief is evidence, not a request to invent completion or dates.",
                "You may propose research/design/plan/verification/coding work only.",
                "Do not approve engineering revisions, invent technical evidence, dispatch coding, merge or modify policy.",
                "A coding proposal is not CODE_READY. It needs separately approved detailed software design.",
                "GitHub status is an explicitly refreshed snapshot; cached values are not live.",
                "Recorded execution PASS is an operator assertion, not product acceptance.",
                "Evidence CONFIRMED is a human review of that record, not physical certification.",
                "Return proposals with exact source object ids and plain rationale.",
            ],
        }

    def context(self, project_id):
        snapshot = self._snapshot(project_id)
        reference = digest(snapshot)
        snapshot["content_sha256"] = reference
        snapshot["generated_at"] = now()
        return snapshot

    def list(self, project_id):
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            result=[dict(r) for r in db.execute(
                "SELECT * FROM manager_proposals WHERE project_id=? ORDER BY created DESC,id",
                (project_id,))]
        for x in result:
            x["source_objects"]=json.loads(x["source_objects"])
        return result

    def propose(self, data):
        project_id = text_value(data.get("project_id",""),"project_id",
                                limit=100,mandatory=True)
        category = data.get("category")
        priority = data.get("priority")
        if category not in PROPOSAL_KINDS:
            raise RuleError("Invalid management proposal category")
        if priority not in PRIORITIES:
            raise RuleError("Invalid management priority")
        title=text_value(data.get("title",""),"proposal title",limit=220,mandatory=True)
        rationale=text_value(data.get("rationale",""),"rationale",limit=12000,mandatory=True)
        next_action=text_value(data.get("next_action",""),"next action",limit=10000,mandatory=True)
        author=text_value(data.get("proposed_by",""),"proposed by",limit=150,mandatory=True)
        ids=data.get("source_objects",[])
        if (not isinstance(ids,list) or len(ids)>40 or
                any(not isinstance(x,str) or not x for x in ids) or len(set(ids))!=len(ids)):
            raise RuleError("Source references must be unique engineering object ids")
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            for oid in ids:
                if self.store._require_object(db,oid)["project_id"]!=project_id:
                    raise RuleError("Proposal source belongs to a different project")
            # The source digest must be calculated by the trusted service, not supplied by AI.
            snapshot=self._snapshot(project_id)
            source_digest=digest(snapshot)
            pid=ident()
            db.execute("""INSERT INTO manager_proposals VALUES
                (?,?,?,?,?,?,?,?,?,?,'PENDING',NULL,NULL,?,NULL)""",
                (pid,project_id,category,priority,title,rationale,next_action,
                 json.dumps(ids,ensure_ascii=False),source_digest,author,now()))
            self.store._audit(db,project_id,None,"MANAGER_PROPOSAL",author,
                              category+" / "+title)
        return self.list(project_id)

    def review(self, proposal_id, decision, reviewer, note):
        if decision not in DECISIONS:
            raise RuleError("Invalid management review decision")
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        note=text_value(note,"decision note",limit=4000,mandatory=True)
        with self.store.db() as db:
            row=db.execute("SELECT * FROM manager_proposals WHERE id=?",
                           (proposal_id,)).fetchone()
            if not row:
                raise RuleError("Management proposal not found")
            if row["status"]!="PENDING":
                raise RuleError("Proposal already reviewed; create a new one")
            # This merely records human evaluation. It does not modify design or coding gates.
            db.execute("""UPDATE manager_proposals SET status=?, reviewer=?,
                          review_note=?,reviewed_at=? WHERE id=?""",
                       (decision,reviewer,note,now(),proposal_id))
            self.store._audit(db,row["project_id"],None,"MANAGER_REVIEW",reviewer,
                              decision+" / "+row["title"]+" / "+note)
        return self.list(row["project_id"])
