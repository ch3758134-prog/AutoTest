"""AI proposal-only product-definition suggestions.

A suggestion is inert: it cannot pause coding, change an approved baseline,
create formal review tasks, or approve anything. Only an owner action can
promote it into a governed change transaction.
"""
from __future__ import annotations
import json
from studio_store import Store,RuleError,ident,now,text_value
from studio_definition_research import DefinitionResearch

class DefinitionSuggestions:
    def __init__(self,store:Store):
        self.store=store
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS definition_suggestions(
                   id TEXT PRIMARY KEY,
                   project_id TEXT NOT NULL REFERENCES projects(id),
                   base_digest TEXT NOT NULL,
                   proposed_json TEXT NOT NULL,
                   reason TEXT NOT NULL,source_refs TEXT NOT NULL,
                   proposed_by TEXT NOT NULL,origin_role TEXT NOT NULL,
                   state TEXT NOT NULL,reviewed_by TEXT,review_note TEXT,
                   accepted_change_id TEXT REFERENCES definition_changes(id),
                   created TEXT NOT NULL,updated TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_definition_suggestions_project
                    ON definition_suggestions(project_id,state);
            """)

    def _get(self,db,key):
        row=db.execute("SELECT * FROM definition_suggestions WHERE id=?",(key,)).fetchone()
        if not row:raise RuleError("Product-definition suggestion not found")
        return dict(row)

    @staticmethod
    def _decode(record):
        item=dict(record)
        item["proposed"]=json.loads(item.pop("proposed_json"))
        item["source_refs"]=json.loads(item["source_refs"])
        return item

    def list(self,project_id):
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            return [self._decode(x) for x in db.execute(
                 "SELECT * FROM definition_suggestions WHERE project_id=? ORDER BY created DESC,id DESC",
                 (project_id,))]

    def suggest(self,project_id,proposed,reason,source_refs,author,origin_role,
                expected_digest):
        from studio_governance import _text_fields,digest,definition
        proposed=_text_fields(proposed)
        reason=text_value(reason,"product-definition proposal rationale",
                          limit=12000,mandatory=True)
        author=text_value(author,"candidate proposer",limit=150,mandatory=True)
        if origin_role not in ("AI_CANDIDATE","OWNER_CANDIDATE"):
            raise RuleError("Role must be assigned by the trusted server route")
        if (not isinstance(source_refs,list) or not source_refs or
            len(source_refs)>30 or any(not isinstance(x,str) or
                not x.strip() or len(x)>1500 for x in source_refs)):
            raise RuleError("Product suggestion requires traceable source references")
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            project=self.store._require_project(db,project_id)
            current=definition(project)
            current_digest=digest(current)
            if expected_digest!=current_digest:
                raise RuleError("Product definition changed; refresh candidate before suggesting")
            if proposed==current:raise RuleError("Suggestion makes no product-definition change")
            pending=db.execute("""SELECT COUNT(*) FROM definition_suggestions WHERE
                      project_id=? AND state='PENDING'""",(project_id,)).fetchone()[0]
            if pending>=30:
                raise RuleError("Too many pending suggestions; triage existing proposals")
            key,stamp=ident(),now()
            db.execute("""INSERT INTO definition_suggestions
              (id,project_id,base_digest,proposed_json,reason,source_refs,
               proposed_by,origin_role,state,reviewed_by,review_note,
               accepted_change_id,created,updated)
              VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
              (key,project_id,current_digest,json.dumps(proposed,ensure_ascii=False),
               reason,json.dumps(source_refs,ensure_ascii=False),
               author,origin_role,"PENDING",None,None,None,stamp,stamp))
            self.store._audit(db,project_id,None,"SUGGEST_PRODUCT_DEFINITION",
                              author,key+" / "+origin_role+" / "+reason)
        return {"id":key,"status":"PENDING","note":"Candidate only; formal engineering state unchanged."}

    def accept(self,suggestion_id,reviewer,rationale,expected_base_digest):
        from studio_governance import POLICY_VERSION,DOMAINS,digest,definition
        reviewer=text_value(reviewer,"owner reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"owner acceptance rationale",limit=12000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            suggestion=self._get(db,suggestion_id)
            if suggestion["state"]!="PENDING":
                raise RuleError("Product-definition suggestion has already been reviewed")
            project=self.store._require_project(db,suggestion["project_id"])
            current_digest=digest(definition(project))
            if (expected_base_digest!=suggestion["base_digest"] or
                current_digest!=suggestion["base_digest"]):
                raise RuleError("Product baseline has changed; re-evaluate suggested definition")
            baseline=db.execute("""SELECT digest,revision FROM definition_baselines
                WHERE project_id=? ORDER BY revision DESC LIMIT 1""",
                (suggestion["project_id"],)).fetchone()
            if baseline and baseline["digest"]!=current_digest:
                raise RuleError("Approved product definition drifted from local project")
            key,stamp=ident(),now()
            db.execute("""INSERT INTO definition_changes
                (id,project_id,base_digest,base_revision,proposed_json,reason,
                 source_refs,proposer,state,policy_version,created,updated)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?)""",
                (key,suggestion["project_id"],current_digest,
                 baseline["revision"] if baseline else 0,
                 suggestion["proposed_json"],suggestion["reason"]+"\\n"+
                 "Suggestion accepted by "+reviewer+": "+rationale,
                 suggestion["source_refs"],suggestion["proposed_by"],
                 "REVIEW",POLICY_VERSION,stamp,stamp))
            DefinitionResearch.seed(db,key,[domain for domain,_ in DOMAINS])
            db.execute("""UPDATE definition_suggestions SET state='ACCEPTED_FOR_RESEARCH',
                reviewed_by=?,review_note=?,accepted_change_id=?,updated=? WHERE id=?""",
                (reviewer,rationale,key,stamp,suggestion_id))
            self.store._audit(db,suggestion["project_id"],None,
                "ACCEPT_PRODUCT_SUGGESTION",reviewer,
                suggestion_id+" -> "+key+" / "+rationale)
        return {"suggestion_id":suggestion_id,"change_id":key,
                "state":"ACCEPTED_FOR_RESEARCH",
                "note":"Owner created a formal change; the current definition is unchanged."}

    def reject(self,suggestion_id,reviewer,rationale):
        reviewer=text_value(reviewer,"owner reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"rejection reason",limit=12000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            suggestion=self._get(db,suggestion_id)
            if suggestion["state"]!="PENDING":
                raise RuleError("Suggestion has already been reviewed")
            db.execute("""UPDATE definition_suggestions SET state='REJECTED',
                      reviewed_by=?,review_note=?,updated=? WHERE id=?""",
                      (reviewer,rationale,now(),suggestion_id))
            self.store._audit(db,suggestion["project_id"],None,
                              "REJECT_PRODUCT_SUGGESTION",reviewer,
                              suggestion_id+" / "+rationale)
        return {"suggestion_id":suggestion_id,"state":"REJECTED"}

