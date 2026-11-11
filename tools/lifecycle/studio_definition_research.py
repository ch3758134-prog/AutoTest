"""V0.9: evidence-driven research coordination for governed product changes.

AI may submit candidate analysis but cannot mark required research as reviewed,
change a domain assessment, authorize experiments, or publish a baseline.
"""
from __future__ import annotations
import json
from studio_store import Store, RuleError, ident, now, text_value

# These are check prompts, not claims about the product or completed research.
RESEARCH_PROMPTS = {
 "POSITIONING": "Compare current product goal with the proposed positioning; identify contradictions, target value and excluded claims.",
 "STAKEHOLDERS": "Identify actual user roles, tasks, operational context and unmet needs; separate observations from assumptions.",
 "WORKFLOWS": "Trace the complete user journey from engineering setup through execution, results, exceptions and handoff.",
 "CAPABILITIES": "Compare required product capabilities and boundaries against existing requirements and validation evidence.",
 "SYSTEM_BOUNDARY": "Identify subsystem ownership, configuration versus permanent product boundaries, and external dependencies.",
 "ARCHITECTURE": "Inspect current architecture decisions, affected contracts, alternatives, constraints and migration consequences.",
 "INTERFACES_HARDWARE": "Check hardware, interfaces, protocols, real-world constraints and external compatibility with evidence.",
 "VERIFICATION": "Map expected behavior to test plans, actual runs and missing physical / user acceptance evidence.",
 "IMPLEMENTATION": "Inspect current source baselines, work packages, GitHub PRs, integration and source/design drift.",
 "DELIVERY_RISK": "Check impact to delivery ordering, dependencies, risks, contingency and next admissible decisions.",
}

REVIEW_OUTCOMES = ("ACCEPT", "REQUEST_MORE")


class DefinitionResearch:
    @staticmethod
    def ensure(db):
        db.executescript("""
            CREATE TABLE IF NOT EXISTS definition_research_tasks(
                id TEXT PRIMARY KEY, change_id TEXT NOT NULL REFERENCES definition_changes(id),
                domain TEXT NOT NULL, question TEXT NOT NULL, state TEXT NOT NULL,
                revision INTEGER NOT NULL, accepted_submission_id TEXT,
                reviewed_by TEXT, reviewed_at TEXT,
                created TEXT NOT NULL, updated TEXT NOT NULL,
                UNIQUE(change_id,domain));
            CREATE TABLE IF NOT EXISTS definition_research_submissions(
                id TEXT PRIMARY KEY,
                task_id TEXT NOT NULL REFERENCES definition_research_tasks(id),
                task_revision INTEGER NOT NULL,
                author TEXT NOT NULL, origin_role TEXT NOT NULL,
                finding TEXT NOT NULL, evidence_refs TEXT NOT NULL,
                limitations TEXT NOT NULL,
                linked_study_id TEXT, linked_study_revision INTEGER,
                created TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_definition_research_submissions
                ON definition_research_submissions(task_id,created);
            CREATE TABLE IF NOT EXISTS definition_research_decisions(
                id TEXT PRIMARY KEY,
                task_id TEXT NOT NULL REFERENCES definition_research_tasks(id),
                submission_id TEXT NOT NULL REFERENCES definition_research_submissions(id),
                task_revision INTEGER NOT NULL,
                verdict TEXT NOT NULL, reviewer TEXT NOT NULL,
                rationale TEXT NOT NULL, created TEXT NOT NULL);
        """)
        cols={r["name"] for r in db.execute("PRAGMA table_info(definition_reviews)")}
        if "research_revision" not in cols:
            db.execute("ALTER TABLE definition_reviews ADD COLUMN research_revision INTEGER")

    @staticmethod
    def seed(db,change_id,domains):
        instant=now()
        for domain in domains:
            prompt=RESEARCH_PROMPTS.get(domain)
            if not prompt:
                raise RuleError("No research policy for mandatory domain "+str(domain))
            db.execute("""INSERT INTO definition_research_tasks
               (id,change_id,domain,question,state,revision,accepted_submission_id,
                reviewed_by,reviewed_at,created,updated)
               VALUES(?,?,?,?,?,?,?,?,?,?,?)""",
               (ident(),change_id,domain,prompt,"OPEN",1,None,None,None,instant,instant))

    def __init__(self,store:Store):
        self.store=store
        with self.store.db() as db:
            self.ensure(db)

    def _task(self,db,task_id):
        row=db.execute("""SELECT t.*,c.project_id,c.base_digest,c.base_revision,c.state change_state
              FROM definition_research_tasks t JOIN definition_changes c
              ON c.id=t.change_id WHERE t.id=?""",(task_id,)).fetchone()
        if row is None:raise RuleError("Product-definition research task not found")
        return dict(row)

    def _require_active(self,db,task):
        if task["change_state"]!="REVIEW":
            raise RuleError("Definition change is sealed; research is read-only")
        project=self.store._require_project(db,task["project_id"])
        baseline=db.execute("""SELECT revision,digest FROM definition_baselines
              WHERE project_id=? ORDER BY revision DESC LIMIT 1""",
              (task["project_id"],)).fetchone()
        from hashlib import sha256
        current={k:project[k] for k in ("summary","body","version","variant")}
        raw=json.dumps(current,sort_keys=True,ensure_ascii=False,
                       separators=(",",":")).encode("utf-8")
        current_sha=sha256(raw).hexdigest()
        if (task["base_digest"]!=current_sha or
            task["base_revision"]!=(baseline["revision"] if baseline else 0)):
            raise RuleError("Project product definition baseline changed; research candidate stale")

    def list(self,change_id):
        with self.store.db() as db:
            if not db.execute("SELECT 1 FROM definition_changes WHERE id=?",
                              (change_id,)).fetchone():
                raise RuleError("Definition change not found")
            rows=[dict(x) for x in db.execute(
                """SELECT * FROM definition_research_tasks WHERE change_id=?
                   ORDER BY domain""",(change_id,))]
            for task in rows:
                submissions=[dict(x) for x in db.execute(
                    "SELECT * FROM definition_research_submissions WHERE task_id=? ORDER BY created,id",
                    (task["id"],))]
                for item in submissions:
                    item["evidence_refs"]=json.loads(item["evidence_refs"])
                task["submissions"]=submissions
                task["decisions"]=[dict(x) for x in db.execute(
                    "SELECT * FROM definition_research_decisions WHERE task_id=? ORDER BY created,id",
                    (task["id"],))]
            return {"change_id":change_id,"tasks":rows,
                    "note":"Candidate findings, even when produced by AI, are not accepted research or factual proof. Owner review and source verification are separate."}

    def submit(self,task_id,expected_revision,author,origin_role,
               finding,evidence_refs,limitations,linked_study_id=None):
        author=text_value(author,"research author",limit=150,mandatory=True)
        finding=text_value(finding,"research finding",limit=16000,mandatory=True)
        limitations=text_value(limitations,"known unknowns and limits",limit=8000,mandatory=True)
        if origin_role not in ("AI_CANDIDATE","OWNER_SUBMISSION"):
            raise RuleError("Submission role must be decided by the authenticated route")
        if (not isinstance(evidence_refs,list) or not evidence_refs or
            len(evidence_refs)>40 or any(not isinstance(x,str) or
                   not x.strip() or len(x)>1500 for x in evidence_refs)):
            raise RuleError("Candidate finding requires traceable source references")
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            task=self._task(db,task_id)
            self._require_active(db,task)
            if task["state"]!="OPEN":
                raise RuleError("Reviewed research must be reopened before new submissions")
            if type(expected_revision) is not int or expected_revision!=task["revision"]:
                raise RuleError("Research task revision conflict")
            study_revision=None
            if linked_study_id:
                study=db.execute("SELECT * FROM studies WHERE id=?",(linked_study_id,)).fetchone()
                if not study or study["project_id"]!=task["project_id"]:
                    raise RuleError("Linked technical investigation is missing or cross-project")
                if study["status"]!="DECIDED":
                    raise RuleError("Linked technical investigation has no current reviewed decision")
                study_revision=study["revision"]
            sid=ident()
            db.execute("""INSERT INTO definition_research_submissions
                 (id,task_id,task_revision,author,origin_role,finding,evidence_refs,
                  limitations,linked_study_id,linked_study_revision,created)
                 VALUES(?,?,?,?,?,?,?,?,?,?,?)""",
                 (sid,task_id,task["revision"],author,origin_role,finding,
                  json.dumps(evidence_refs,ensure_ascii=False),limitations,
                  linked_study_id,study_revision,now()))
            self.store._audit(db,task["project_id"],None,"SUBMIT_DEFINITION_RESEARCH",author,
                task["domain"]+" / "+origin_role+" / "+sid)
        return {"submission_id":sid,"task_id":task_id,
                "status":"CANDIDATE_ONLY","origin_role":origin_role}

    def review(self,task_id,submission_id,expected_revision,decision,reviewer,rationale):
        if decision not in REVIEW_OUTCOMES:
            raise RuleError("Invalid research review decision")
        reviewer=text_value(reviewer,"research reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"research evidence review",limit=10000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            task=self._task(db,task_id)
            self._require_active(db,task)
            if task["state"]!="OPEN":
                raise RuleError("Research already reviewed; reopen to reassess")
            if type(expected_revision) is not int or expected_revision!=task["revision"]:
                raise RuleError("Research task revision conflict")
            result=db.execute("SELECT * FROM definition_research_submissions WHERE id=? AND task_id=?",
                              (submission_id,task_id)).fetchone()
            if not result or result["task_revision"]!=task["revision"]:
                raise RuleError("Research submission does not belong to current task revision")
            if result["linked_study_id"]:
                current=db.execute("SELECT revision,status FROM studies WHERE id=?",
                                   (result["linked_study_id"],)).fetchone()
                if (not current or current["status"]!="DECIDED" or
                    current["revision"]!=result["linked_study_revision"]):
                    raise RuleError("Linked technical investigation decision is stale")
            dec_id=ident()
            db.execute("""INSERT INTO definition_research_decisions
              (id,task_id,submission_id,task_revision,verdict,reviewer,rationale,created)
              VALUES(?,?,?,?,?,?,?,?)""",
              (dec_id,task_id,submission_id,task["revision"],decision,reviewer,rationale,now()))
            if decision=="ACCEPT":
                db.execute("""UPDATE definition_research_tasks SET state='REVIEWED',
                  accepted_submission_id=?,reviewed_by=?,reviewed_at=?,revision=?,updated=?
                  WHERE id=?""",
                  (submission_id,reviewer,now(),task["revision"]+1,now(),task_id))
            else:
                # Explicit more-research decision opens a new epoch; old evidence
                # remains in history but cannot be silently re-accepted.
                db.execute("""UPDATE definition_research_tasks SET state='OPEN',
                  accepted_submission_id=NULL,reviewed_by=NULL,reviewed_at=NULL,
                  revision=?,updated=? WHERE id=?""",
                  (task["revision"]+1,now(),task_id))
            self.store._audit(db,task["project_id"],None,"REVIEW_DEFINITION_RESEARCH",reviewer,
                              task["domain"]+" / "+decision+" / "+dec_id)
        return {"task_id":task_id,"verdict":decision,
                "revision":task["revision"]+1,
                "note":"Reviewer claim is recorded but external source authenticity is not automatically certified."}

    def reopen(self,task_id,expected_revision,reviewer,rationale):
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"reopen reason",limit=8000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            task=self._task(db,task_id)
            self._require_active(db,task)
            if task["state"]!="REVIEWED":
                raise RuleError("Only reviewed research may be reopened")
            if expected_revision!=task["revision"]:
                raise RuleError("Research task revision conflict")
            db.execute("""UPDATE definition_research_tasks SET state='OPEN',
                  accepted_submission_id=NULL,reviewed_by=NULL,reviewed_at=NULL,
                  revision=?,updated=? WHERE id=?""",
                  (task["revision"]+1,now(),task_id))
            self.store._audit(db,task["project_id"],None,"REOPEN_DEFINITION_RESEARCH",reviewer,
                              task["domain"]+" / "+rationale)
        return {"task_id":task_id,"state":"OPEN","revision":task["revision"]+1}

    @staticmethod
    def gate_rows(db,change_id,domains):
        rows={r["domain"]:dict(r) for r in db.execute(
            "SELECT * FROM definition_research_tasks WHERE change_id=?",(change_id,))}
        reasons=[]
        if len(rows)!=len(domains):
            reasons.append("Required definition research task set is incomplete")
        for domain in domains:
            task=rows.get(domain)
            if not task:
                reasons.append(domain+": mandatory research task missing");continue
            if task["state"]!="REVIEWED" or not task["accepted_submission_id"]:
                reasons.append(domain+": mandatory research remains unreviewed")
                continue
            finding=db.execute(
                "SELECT * FROM definition_research_submissions WHERE id=?",
                (task["accepted_submission_id"],)).fetchone()
            if not finding:
                reasons.append(domain+": accepted research evidence missing");continue
            if finding["linked_study_id"]:
                study=db.execute("SELECT revision,status FROM studies WHERE id=?",
                                 (finding["linked_study_id"],)).fetchone()
                if (not study or study["status"]!="DECIDED" or
                    study["revision"]!=finding["linked_study_revision"]):
                    reasons.append(domain+": technical investigation decision is stale")
        return reasons,rows
