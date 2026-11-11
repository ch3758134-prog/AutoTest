"""Immutable execution evidence references, separate from test definitions and acceptance."""
from __future__ import annotations
import json
import re
from studio_store import Store, RuleError, ident, now, text_value

LEVELS = ("UNIT","INTEGRATION","BENCH","USER_ACCEPTANCE")
OUTCOMES = ("PASS","FAIL","INCONCLUSIVE")
REVIEWS = ("CONFIRMED","REJECTED")


class Verification:
    def __init__(self, store: Store):
        self.store = store
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS verification_runs(
                  id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                  test_object_id TEXT NOT NULL REFERENCES objects(id),
                  test_revision INTEGER NOT NULL,
                  level TEXT NOT NULL, outcome TEXT NOT NULL, configuration TEXT NOT NULL,
                  source_sha TEXT NOT NULL, evidence_ref TEXT NOT NULL,
                  environment TEXT NOT NULL, actor TEXT NOT NULL,
                  created TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS idx_verification_test
                  ON verification_runs(project_id,test_object_id,created);
                CREATE TABLE IF NOT EXISTS verification_reviews(
                  run_id TEXT PRIMARY KEY REFERENCES verification_runs(id),
                  decision TEXT NOT NULL, reviewer TEXT NOT NULL,
                  rationale TEXT NOT NULL, reviewed_at TEXT NOT NULL);
            """)

    def list(self, project_id):
        with self.store.db() as db:
            self.store._require_project(db, project_id)
            rows = [dict(r) for r in db.execute(
                """SELECT v.*, r.decision review_decision, r.reviewer, r.rationale review_reason,
                          r.reviewed_at FROM verification_runs v
                   LEFT JOIN verification_reviews r ON r.run_id=v.id
                   WHERE v.project_id=? ORDER BY v.created DESC,v.id DESC""",(project_id,))]
            return {"project_id":project_id,"runs":rows,
                    "note":"Human-entered evidence references are not authenticated raw test data. Review is not product acceptance."}

    def record(self, data):
        test_id = text_value(data.get("test_object_id",""),"test object",mandatory=True)
        level = data.get("level")
        outcome = data.get("outcome")
        if level not in LEVELS or outcome not in OUTCOMES:
            raise RuleError("Invalid execution level or result")
        revision = data.get("test_revision")
        configuration = text_value(data.get("configuration",""),"configuration",limit=3000,mandatory=True)
        sha = text_value(data.get("source_sha",""),"source SHA",limit=40)
        if sha and not re.fullmatch(r"[0-9a-fA-F]{40}",sha):
            raise RuleError("Source SHA must be a full 40-character Git commit")
        if not sha and level in ("UNIT","INTEGRATION"):
            raise RuleError("Software test execution requires a full Git source SHA")
        evidence = text_value(data.get("evidence_ref",""),"raw evidence reference",limit=5000,mandatory=True)
        environment = text_value(data.get("environment",""),"execution environment",limit=3000,mandatory=True)
        actor = text_value(data.get("actor",""),"operator",limit=150,mandatory=True)
        with self.store.db() as db:
            target = self.store._require_object(db,test_id)
            if target["kind"]!="test":
                raise RuleError("Execution evidence must reference a test specification")
            if type(revision) is not int or target["revision"]!=revision:
                raise RuleError("Test specification revision conflict")
            key,instant=ident(),now()
            db.execute("""INSERT INTO verification_runs
                (id,project_id,test_object_id,test_revision,level,outcome,
                 configuration,source_sha,evidence_ref,environment,actor,created)
                 VALUES(?,?,?,?,?,?,?,?,?,?,?,?)""",
                 (key,target["project_id"],test_id,revision,level,outcome,
                  configuration,sha,evidence,environment,actor,instant))
            self.store._audit(db,target["project_id"],test_id,"RECORD_VERIFICATION",actor,
                              level+" "+outcome+" / run "+key+" / "+sha)
        return self.get(key)

    def get(self, run_id):
        with self.store.db() as db:
            row=db.execute("""SELECT v.*,r.decision review_decision,r.reviewer,
                    r.rationale review_reason,r.reviewed_at FROM verification_runs v
                    LEFT JOIN verification_reviews r ON r.run_id=v.id
                    WHERE v.id=?""",(run_id,)).fetchone()
            if not row:
                raise RuleError("Verification record not found")
            return dict(row)

    def review(self, run_id, decision, reviewer, rationale):
        if decision not in REVIEWS:
            raise RuleError("Invalid evidence review decision")
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"review rationale",limit=5000,mandatory=True)
        with self.store.db() as db:
            run=db.execute("SELECT * FROM verification_runs WHERE id=?",(run_id,)).fetchone()
            if not run:
                raise RuleError("Verification record not found")
            if db.execute("SELECT 1 FROM verification_reviews WHERE run_id=?",(run_id,)).fetchone():
                raise RuleError("Evidence has already been reviewed; create a new run for new evidence")
            db.execute("""INSERT INTO verification_reviews VALUES(?,?,?,?,?)""",
                       (run_id,decision,reviewer,rationale,now()))
            self.store._audit(db,run["project_id"],run["test_object_id"],
                              "REVIEW_VERIFICATION",reviewer,
                              decision+" / run "+run_id+" / "+rationale)
        return self.get(run_id)
