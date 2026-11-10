"""Evidence-led technology investigations for the local engineering workbench.

Architecture decisions may consume only closed, version-pinned studies with
reviewed experiment evidence. The workbench never controls hardware or executes
prototypes; investigators attach external, version-scoped evidence references.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

from studio_store import RuleError, Store, ident, now, text_value

LEVELS = {"ANALYSIS": 0, "PROTOTYPE": 1, "REAL": 2}
KINDS = {"ANALYSIS", "PROTOTYPE", "REAL"}
OUTCOMES = {"PASS", "FAIL", "LIMITED", "INCONCLUSIVE"}
STATUS = {"OPEN", "DECIDED"}


def positive_text(data, key, limit=50000):
    return text_value(data.get(key, ""), key, limit=limit, mandatory=True)


def _as_dict(value):
    return dict(value) if value is not None else None


class Research:
    def __init__(self, store: Store):
        self.store = store
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS studies (
                    id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                    title TEXT NOT NULL, question TEXT NOT NULL,
                    constraints TEXT NOT NULL, requirement_id TEXT,
                    minimum_level TEXT NOT NULL, status TEXT NOT NULL,
                    revision INTEGER NOT NULL, selected_option TEXT,
                    decision_id TEXT, created TEXT NOT NULL, updated TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_studies_project ON studies(project_id);
                CREATE TABLE IF NOT EXISTS study_options (
                    id TEXT PRIMARY KEY, study_id TEXT NOT NULL REFERENCES studies(id),
                    title TEXT NOT NULL, approach TEXT NOT NULL, tradeoffs TEXT NOT NULL,
                    assumptions TEXT NOT NULL, created TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS study_experiments (
                    id TEXT PRIMARY KEY, study_id TEXT NOT NULL REFERENCES studies(id),
                    option_id TEXT NOT NULL REFERENCES study_options(id),
                    title TEXT NOT NULL, method TEXT NOT NULL,
                    success_criteria TEXT NOT NULL, environment TEXT NOT NULL,
                    level TEXT NOT NULL, required INTEGER NOT NULL, status TEXT NOT NULL,
                    authorized_by TEXT, authorized_at TEXT, created TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS study_results (
                    id TEXT PRIMARY KEY, experiment_id TEXT NOT NULL REFERENCES study_experiments(id),
                    outcome TEXT NOT NULL, observations TEXT NOT NULL,
                    raw_reference TEXT NOT NULL, evidence_sha256 TEXT NOT NULL,
                    configuration TEXT NOT NULL, source_revision TEXT NOT NULL,
                    performed_by TEXT NOT NULL, performed_at TEXT NOT NULL,
                    reviewed_by TEXT, reviewed_at TEXT, review_note TEXT);
                CREATE TABLE IF NOT EXISTS study_decisions (
                    id TEXT PRIMARY KEY, study_id TEXT NOT NULL REFERENCES studies(id),
                    study_revision INTEGER NOT NULL, option_id TEXT NOT NULL,
                    rationale TEXT NOT NULL, restrictions TEXT NOT NULL,
                    reviewed_evidence TEXT NOT NULL, approver TEXT NOT NULL,
                    decided_at TEXT NOT NULL, state TEXT NOT NULL);
            """)

    def _study(self, db, study_id):
        row = db.execute("SELECT * FROM studies WHERE id=?", (study_id,)).fetchone()
        if not row:
            raise RuleError("Technical investigation not found")
        return dict(row)

    def _option(self, db, study_id, option_id):
        row = db.execute("SELECT * FROM study_options WHERE id=? AND study_id=?",
                         (option_id, study_id)).fetchone()
        if not row:
            raise RuleError("Candidate does not belong to the investigation")
        return dict(row)

    def _open(self, db, study_id):
        study = self._study(db, study_id)
        if study["status"] != "OPEN":
            raise RuleError("Decision is sealed; reopen with a recorded reason before editing")
        return study

    def _audit(self, db, study, action, actor, note):
        self.store._audit(db, study["project_id"], None, action, actor,
                          "STUDY " + study["title"] + ": " + note)

    def list(self, project_id):
        with self.store.db() as db:
            self.store._require_project(db, project_id)
            rows = [dict(r) for r in db.execute(
                "SELECT * FROM studies WHERE project_id=? ORDER BY updated DESC",
                (project_id,))]
            result = []
            for study in rows:
                study["option_count"] = db.execute(
                    "SELECT COUNT(*) FROM study_options WHERE study_id=?",
                    (study["id"],)).fetchone()[0]
                study["experiment_count"] = db.execute(
                    "SELECT COUNT(*) FROM study_experiments WHERE study_id=?",
                    (study["id"],)).fetchone()[0]
                study["reviewed_evidence"] = db.execute("""
                    SELECT COUNT(*) FROM study_results r
                    JOIN study_experiments e ON e.id=r.experiment_id
                    WHERE e.study_id=? AND r.reviewed_by IS NOT NULL
                    """, (study["id"],)).fetchone()[0]
                result.append(study)
            return result

    def detail(self, study_id):
        with self.store.db() as db:
            study = self._study(db, study_id)
            options = [dict(r) for r in db.execute(
                "SELECT * FROM study_options WHERE study_id=? ORDER BY created,id",
                (study_id,))]
            experiments = [dict(r) for r in db.execute(
                "SELECT * FROM study_experiments WHERE study_id=? ORDER BY created,id",
                (study_id,))]
            for experiment in experiments:
                experiment["results"] = [dict(r) for r in db.execute(
                    "SELECT * FROM study_results WHERE experiment_id=? ORDER BY performed_at,id",
                    (experiment["id"],))]
            decisions = [dict(r) for r in db.execute(
                "SELECT * FROM study_decisions WHERE study_id=? ORDER BY study_revision DESC",
                (study_id,))]
            for decision in decisions:
                decision["reviewed_evidence"] = json.loads(decision["reviewed_evidence"])
            return {"study": study, "options": options,
                    "experiments": experiments, "decisions": decisions}

    def create(self, data):
        project_id = positive_text(data, "project_id", 100)
        title = positive_text(data, "title", 200)
        question = positive_text(data, "question")
        constraints = text_value(data.get("constraints", ""), "constraints")
        minimum = data.get("minimum_level", "PROTOTYPE")
        if minimum not in LEVELS:
            raise RuleError("Minimum evidence level must be ANALYSIS, PROTOTYPE or REAL")
        requirement_id = data.get("requirement_id") or None
        with self.store.db() as db:
            self.store._require_project(db, project_id)
            if requirement_id:
                requirement = self.store._require_object(db, requirement_id)
                if requirement["project_id"] != project_id:
                    raise RuleError("Referenced requirement belongs to another project")
                if requirement["kind"] != "requirement":
                    raise RuleError("Research source must be an explicit requirement")
            key, stamp = ident(), now()
            db.execute("""INSERT INTO studies VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                       (key, project_id, title, question, constraints, requirement_id,
                        minimum, "OPEN", 1, None, None, stamp, stamp))
            self.store._audit(db, project_id, requirement_id, "OPEN_INVESTIGATION",
                              "owner", title + " / " + question[:350])
        return self.detail(key)

    def add_option(self, study_id, data):
        title = positive_text(data, "title", 200)
        approach = positive_text(data, "approach")
        tradeoffs = positive_text(data, "tradeoffs")
        assumptions = text_value(data.get("assumptions", ""), "assumptions")
        with self.store.db() as db:
            study = self._open(db, study_id)
            key = ident()
            db.execute("INSERT INTO study_options VALUES(?,?,?,?,?,?,?)",
                       (key, study_id, title, approach, tradeoffs, assumptions, now()))
            self._audit(db, study, "RESEARCH_OPTION", "owner", title)
        return self.detail(study_id)

    def add_experiment(self, study_id, data):
        option_id = positive_text(data, "option_id", 100)
        title = positive_text(data, "title", 200)
        method = positive_text(data, "method")
        success = positive_text(data, "success_criteria")
        environment = positive_text(data, "environment")
        level = data.get("level")
        if level not in KINDS:
            raise RuleError("Experiment level must be ANALYSIS, PROTOTYPE or REAL")
        required = data.get("required", True)
        if not isinstance(required, bool):
            raise RuleError("Required flag must be a boolean")
        with self.store.db() as db:
            study = self._open(db, study_id)
            self._option(db, study_id, option_id)
            key = ident()
            db.execute("""INSERT INTO study_experiments
                (id,study_id,option_id,title,method,success_criteria,environment,
                level,required,status,authorized_by,authorized_at,created)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (key, study_id, option_id, title, method, success, environment,
                 level, int(required), "PLANNED", None, None, now()))
            self._audit(db, study, "PLAN_EXPERIMENT", "owner", title)
        return self.detail(study_id)

    def authorize(self, experiment_id, actor, note):
        actor = text_value(actor, "experiment approver", limit=150, mandatory=True)
        note = text_value(note, "experiment authorization rationale",
                          limit=3000, mandatory=True)
        with self.store.db() as db:
            e = db.execute("SELECT * FROM study_experiments WHERE id=?",
                           (experiment_id,)).fetchone()
            if not e:
                raise RuleError("Experiment not found")
            study = self._open(db, e["study_id"])
            if e["status"] != "PLANNED":
                raise RuleError("Experiment already authorized")
            db.execute("""UPDATE study_experiments SET status='AUTHORIZED',
                authorized_by=?,authorized_at=? WHERE id=?""",
                (actor, now(), experiment_id))
            self._audit(db, study, "AUTHORIZE_EXPERIMENT", actor,
                        e["title"] + " / " + note)
        return self.detail(e["study_id"])

    def record_result(self, experiment_id, data):
        outcome = data.get("outcome")
        if outcome not in OUTCOMES:
            raise RuleError("Result must be PASS, FAIL, LIMITED or INCONCLUSIVE")
        observed = positive_text(data, "observations")
        raw_ref = positive_text(data, "raw_reference", 2000)
        config = positive_text(data, "configuration", 5000)
        revision = positive_text(data, "source_revision", 500)
        performed_by = positive_text(data, "performed_by", 150)
        digest = text_value(data.get("evidence_sha256", ""), "evidence_sha256", limit=64)
        if digest and not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
            raise RuleError("Evidence SHA-256 must contain exactly 64 hex characters")
        with self.store.db() as db:
            row = db.execute("SELECT * FROM study_experiments WHERE id=?",
                             (experiment_id,)).fetchone()
            if not row:
                raise RuleError("Experiment not found")
            study = self._open(db, row["study_id"])
            if row["status"] != "AUTHORIZED":
                raise RuleError("Experiment needs prior human authorization")
            # Physical execution is external to this application.
            key = ident()
            db.execute("""INSERT INTO study_results VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                       (key, experiment_id, outcome, observed, raw_ref, digest,
                        config, revision, performed_by, now(), None, None, None))
            self._audit(db, study, "RECORD_EXPERIMENT", performed_by,
                        row["title"] + " / " + outcome + " / " + raw_ref)
        return self.detail(row["study_id"])

    def review_result(self, result_id, actor, note):
        actor = text_value(actor, "evidence reviewer", limit=150, mandatory=True)
        note = text_value(note, "review decision", limit=3000, mandatory=True)
        with self.store.db() as db:
            row = db.execute("""SELECT r.*,e.study_id,e.title AS experiment_title
                FROM study_results r JOIN study_experiments e ON e.id=r.experiment_id
                WHERE r.id=?""", (result_id,)).fetchone()
            if not row:
                raise RuleError("Evidence record not found")
            study = self._open(db, row["study_id"])
            if row["reviewed_by"]:
                raise RuleError("Evidence has already been reviewed")
            db.execute("""UPDATE study_results SET reviewed_by=?,reviewed_at=?,
                review_note=? WHERE id=?""", (actor, now(), note, result_id))
            self._audit(db, study, "REVIEW_EVIDENCE", actor,
                        row["experiment_title"] + " / " + row["outcome"] + " / " + note)
        return self.detail(row["study_id"])

    def _evaluate_in_db(self, db, study_id, option_id):
        study = self._study(db, study_id)
        self._option(db, study_id, option_id)
        reasons = []
        experiments = [dict(r) for r in db.execute(
            "SELECT * FROM study_experiments WHERE study_id=? AND option_id=?",
            (study_id, option_id))]
        if not experiments:
            reasons.append("No experiment plan is attached to this candidate")
        accepted = []
        for experiment in experiments:
            reports = [dict(r) for r in db.execute(
                "SELECT * FROM study_results WHERE experiment_id=? ORDER BY rowid",
                (experiment["id"],))]
            reviewed = [r for r in reports if r["reviewed_by"]]
            # The most recent reviewed run is authoritative. Failed older runs
            # remain visible in history, but can be superseded by a reviewed retest.
            latest = reviewed[-1] if reviewed else None
            if experiment["required"] and (not latest or latest["outcome"] != "PASS"):
                reasons.append("Required experiment has no current reviewed PASS evidence: " +
                               experiment["title"])
            if latest and latest["outcome"] == "PASS":
                if LEVELS[experiment["level"]] >= LEVELS[study["minimum_level"]]:
                    accepted.append({
                        "id": latest["id"], "experiment_id":experiment["id"],
                        "level": experiment["level"],
                        "configuration": latest["configuration"],
                        "source_revision": latest["source_revision"],
                        "raw_reference": latest["raw_reference"],
                        "evidence_sha256": latest["evidence_sha256"],
                        "reviewed_by": latest["reviewed_by"],
                        "reviewed_at": latest["reviewed_at"],
                        "previous_failed_runs": sum(r["outcome"]=="FAIL" for r in reviewed[:-1])
                    })
        if not accepted:
            reasons.append("No reviewed PASS evidence meets required maturity: " +
                           study["minimum_level"])
        return {"study_id":study_id, "option_id":option_id, "ready":not reasons,
                "reasons":reasons,"accepted_evidence":accepted,
                "minimum_level":study["minimum_level"]}

    def evaluate(self, study_id, option_id):
        """Read-only evidence gate. Neither an AI claim nor a checked box is evidence."""
        with self.store.db() as db:
            return self._evaluate_in_db(db, study_id, option_id)

    def decide(self, study_id, data):
        option_id = positive_text(data, "option_id", 100)
        actor = positive_text(data, "approver", 150)
        rationale = positive_text(data, "rationale")
        restrictions = text_value(data.get("restrictions", ""), "restrictions")
        with self.store.db() as db:
            # Prevent decisions from racing with evidence writes on other requests.
            db.execute("BEGIN IMMEDIATE")
            study = self._open(db, study_id)
            self._option(db, study_id, option_id)
            assessment = self._evaluate_in_db(db, study_id, option_id)
            if not assessment["ready"]:
                raise RuleError("Cannot approve technology route: " +
                                "; ".join(assessment["reasons"]))
            stamp = now()
            decision_id = ident()
            revision = study["revision"] + 1
            db.execute("""INSERT INTO study_decisions VALUES(?,?,?,?,?,?,?,?,?,?)""",
                       (decision_id, study_id, revision, option_id, rationale, restrictions,
                        json.dumps(assessment["accepted_evidence"], ensure_ascii=False),
                        actor, stamp, "CURRENT"))
            db.execute("""UPDATE studies SET status='DECIDED',selected_option=?,
                       decision_id=?,revision=?,updated=? WHERE id=?""",
                       (option_id, decision_id, revision, stamp, study_id))
            self._audit(db, study, "DECIDE_TECHNOLOGY", actor,
                        self._option(db, study_id, option_id)["title"] + " / " + rationale)
        return self.detail(study_id)

    def reopen(self, study_id, data):
        actor = positive_text(data, "actor", 150)
        reason = positive_text(data, "reason")
        with self.store.db() as db:
            study = self._study(db, study_id)
            if study["status"] != "DECIDED":
                raise RuleError("Only a decided investigation may be reopened")
            db.execute("""UPDATE study_decisions SET state='SUPERSEDED'
                          WHERE id=?""", (study["decision_id"],))
            db.execute("""UPDATE studies SET status='OPEN',selected_option=NULL,
                       decision_id=NULL,revision=?,updated=? WHERE id=?""",
                       (study["revision"] + 1, now(), study_id))
            self._audit(db, study, "REOPEN_TECHNOLOGY", actor, reason)
        return self.detail(study_id)

    @staticmethod
    def design_check(db, obj):
        """Return blockers for a design linked to explicitly selected investigation IDs."""
        meta = obj.get("meta", {})
        ids = meta.get("study_ids", [])
        if not ids:
            return []
        if not isinstance(ids, list) or any(not isinstance(x, str) for x in ids):
            return ["Design must list investigation IDs explicitly"]
        pins = meta.get("study_revisions", {})
        if not isinstance(pins, dict):
            return ["Study baseline revision map is malformed"]
        reasons = []
        for study_id in ids:
            row = db.execute("SELECT * FROM studies WHERE id=?", (study_id,)).fetchone()
            if not row or row["project_id"] != obj["project_id"]:
                reasons.append("Linked technology study not found in this project: " + study_id)
                continue
            if row["status"] != "DECIDED" or not row["decision_id"]:
                reasons.append("Technology investigation not decided: " + row["title"])
            if str(pins.get(study_id, "")) != str(row["revision"]):
                reasons.append("Technology decision revision not pinned/current: " + row["title"])
        return reasons
