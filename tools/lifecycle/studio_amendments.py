"""Reviewable AI-proposed amendments to existing engineering objects.

Existing objects remain the sole source for requirements, system decomposition,
interfaces, architectures and detailed designs. This is a *proposal inbox*,
not a competing engineering object hierarchy or automatic design authority.
"""
from __future__ import annotations

import json

from studio_store import Store, RuleError, ident, now, text_value, json_object
from studio_knowledge import EngineeringKnowledge

ALLOWED_PATCH = frozenset(("title","body","meta","parent_id"))
SCOPE_CHOICES = frozenset(("REVISE","RESEARCH","REJECT"))


def _source_refs(values):
    if (not isinstance(values,list) or not values or len(values)>60 or
        any(not isinstance(x,str) or not x.strip() or len(x)>2000 for x in values)):
        raise RuleError("Amendment requires traceable nonempty source references")
    return [x.strip() for x in values]


class EngineeringAmendments:
    def __init__(self,store: Store):
        self.store=store
        EngineeringKnowledge(store)
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS engineering_amendments(
                  id TEXT PRIMARY KEY,
                  project_id TEXT NOT NULL REFERENCES projects(id),
                  target_id TEXT NOT NULL REFERENCES objects(id),
                  base_revision INTEGER NOT NULL,
                  base_snapshot TEXT NOT NULL,
                  proposed_patch TEXT NOT NULL,
                  reasoning TEXT NOT NULL,
                  source_refs TEXT NOT NULL,
                  knowledge_pins TEXT NOT NULL,
                  origin_role TEXT NOT NULL,
                  proposed_by TEXT NOT NULL,
                  state TEXT NOT NULL,
                  scope_rationale TEXT,
                  scope_evidence TEXT,
                  scope_decision TEXT,
                  reviewed_by TEXT,
                  reviewed_at TEXT,
                  applied_revision INTEGER,
                  applied_at TEXT,
                  created TEXT NOT NULL,
                  updated TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_amendments_project
                    ON engineering_amendments(project_id,state);
                CREATE INDEX IF NOT EXISTS ix_amendments_target
                    ON engineering_amendments(target_id,state);
            """)

    @staticmethod
    def _get(db,key):
        row=db.execute("SELECT * FROM engineering_amendments WHERE id=?",(key,)).fetchone()
        if not row:
            raise RuleError("Engineering amendment does not exist")
        return dict(row)

    @staticmethod
    def _json_field(record,key):
        return json.loads(record[key]) if record.get(key) is not None else None

    @staticmethod
    def _decode(record):
        row=dict(record)
        for field in ("base_snapshot","proposed_patch","source_refs",
                      "knowledge_pins","scope_evidence"):
            row[field]=EngineeringAmendments._json_field(row,field)
        return row

    def _pins(self,db,project_id,ids):
        if (not isinstance(ids,list) or len(ids)>100 or
            len(set(ids))!=len(ids) or any(not isinstance(x,str) or not x for x in ids)):
            raise RuleError("Knowledge links must be unique existing IDs")
        pins=[]
        for key in ids:
            row=db.execute("""SELECT project_id,revision,state FROM engineering_knowledge
                    WHERE id=?""",(key,)).fetchone()
            if not row or row["project_id"]!=project_id:
                raise RuleError("Cannot link unknown or cross-project knowledge")
            pins.append({"id":key,"revision":row["revision"],"state":row["state"]})
        return pins

    @staticmethod
    def _check_pins(db,pins):
        for p in pins:
            row=db.execute("SELECT revision,state FROM engineering_knowledge WHERE id=?",
                           (p["id"],)).fetchone()
            if not row or row["revision"]!=p["revision"]:
                raise RuleError("Referenced knowledge changed; reassess proposed amendment")

    def _patch(self,db,obj,value):
        if not isinstance(value,dict) or not value or set(value)-ALLOWED_PATCH:
            raise RuleError("Proposed edit may change only title, body, metadata or parent")
        patch={}
        if "title" in value:
            patch["title"]=text_value(value["title"],"amended title",limit=200,mandatory=True)
        if "body" in value:
            patch["body"]=text_value(value["body"],"amended content",limit=50000)
        if "meta" in value:
            patch["meta"]=json_object(value["meta"],"complete metadata replacement")
        if "parent_id" in value:
            parent=value["parent_id"] or None
            patch["parent_id"]=self.store._validate_parent(
                db,obj["project_id"],parent,moving_id=obj["id"])
        if all(obj[k]==value for k,value in patch.items()):
            raise RuleError("Amendment patch does not change the engineering object")
        return patch

    def propose(self,project_id,target_id,base_revision,proposed_patch,reasoning,
                source_refs,knowledge_ids,actor,origin_role):
        if origin_role not in ("AI_CANDIDATE","OWNER_CANDIDATE"):
            raise RuleError("Candidate origin role must come from authenticated capability")
        actor=text_value(actor,"proposer",limit=150,mandatory=True)
        reasoning=text_value(reasoning,"engineering change reasoning",
                             limit=20000,mandatory=True)
        source_refs=_source_refs(source_refs)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            self.store._require_project(db,project_id)
            target=self.store._require_object(db,target_id)
            if target["project_id"]!=project_id:
                raise RuleError("Cannot propose edits across project boundaries")
            if target["kind"]=="change" and target["state"]=="CLOSED":
                raise RuleError("Closed engineering changes cannot be rewritten")
            if type(base_revision) is not int or base_revision!=target["revision"]:
                raise RuleError("Target version changed; inspect its latest revision")
            patch=self._patch(db,target,proposed_patch)
            pins=self._pins(db,project_id,knowledge_ids)
            key,stamp=ident(),now()
            db.execute("""INSERT INTO engineering_amendments(
                  id,project_id,target_id,base_revision,base_snapshot,
                  proposed_patch,reasoning,source_refs,knowledge_pins,
                  origin_role,proposed_by,state,created,updated)
                  VALUES(?,?,?,?,?,?,?,?,?,? ,?,'CANDIDATE',?,?)""",
                  (key,project_id,target_id,base_revision,
                   json.dumps(target,sort_keys=True,ensure_ascii=False),
                   json.dumps(patch,ensure_ascii=False),
                   reasoning,json.dumps(source_refs,ensure_ascii=False),
                   json.dumps(pins,ensure_ascii=False),origin_role,actor,stamp,stamp))
            self.store._audit(db,project_id,target_id,
                "PROPOSE_ENGINEERING_AMENDMENT",actor,key+" / "+reasoning[:800])
        return {"id":key,"target_id":target_id,"state":"CANDIDATE",
                "base_revision":base_revision}

    def _suggestions(self,db,project_id,target_id):
        # Traverses only approved engineering links. The graph may be incomplete;
        # absence of a link must never be described as verified no-impact.
        result=[]
        for r in db.execute("""SELECT source_id,target_id,kind,status FROM relations
                WHERE project_id=? AND status='APPROVED'
                  AND (source_id=? OR target_id=?)
                ORDER BY created,id""",(project_id,target_id,target_id)):
            related=r["target_id"] if r["source_id"]==target_id else r["source_id"]
            obj=db.execute("SELECT title,kind,revision,state FROM objects WHERE id=?",
                           (related,)).fetchone()
            if obj:
                result.append({"object_id":related,"title":obj["title"],"kind":obj["kind"],
                               "revision":obj["revision"],"state":obj["state"],
                               "relation":r["kind"],
                               "direction":"downstream" if r["source_id"]==target_id else "upstream"})
        return result

    def list(self,project_id):
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            rows=[self._decode(dict(x)) for x in db.execute(
                "SELECT * FROM engineering_amendments WHERE project_id=? ORDER BY created DESC,id DESC",
                (project_id,))]
            for record in rows:
                obj=self.store._require_object(db,record["target_id"])
                record["target"]={"id":obj["id"],"kind":obj["kind"],"title":obj["title"],
                                  "revision":obj["revision"],"state":obj["state"]}
                record["stale"]=obj["revision"]!=record["base_revision"]
            return {"schema":"engineering-amendments/1","project_id":project_id,
                    "items":rows,
                    "note":"AI proposals do not alter engineering objects or permissions. Human scope assessment is not acceptance or verification of sources."}

    def detail(self,key):
        with self.store.db() as db:
            item=self._decode(self._get(db,key))
            obj=self.store._require_object(db,item["target_id"])
            item["target"]={"id":obj["id"],"kind":obj["kind"],
                            "title":obj["title"],"revision":obj["revision"],
                            "state":obj["state"],"body":obj["body"],"meta":obj["meta"],
                            "parent_id":obj["parent_id"]}
            item["stale"]=obj["revision"]!=item["base_revision"]
            item["related_candidates"]=self._suggestions(
                db,item["project_id"],item["target_id"])
            return item

    def review(self,key,expected_target_revision,decision,reviewer,reason,evidence_refs):
        if decision not in SCOPE_CHOICES:
            raise RuleError("Use REVISE, RESEARCH, or REJECT")
        reviewer=text_value(reviewer,"engineering reviewer",limit=150,mandatory=True)
        reason=text_value(reason,"engineering scope and impact reasoning",
                          limit=16000,mandatory=True)
        evidence_refs=_source_refs(evidence_refs)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            proposal=self._get(db,key)
            if proposal["state"]!="CANDIDATE":
                raise RuleError("Amendment already reviewed; start a new candidate")
            target=self.store._require_object(db,proposal["target_id"])
            if (type(expected_target_revision) is not int or
                proposal["base_revision"]!=expected_target_revision or
                target["revision"]!=expected_target_revision):
                raise RuleError("Target changed; re-evaluate the amendment")
            self._check_pins(db,json.loads(proposal["knowledge_pins"]))
            status={"REVISE":"READY_TO_APPLY","RESEARCH":"RESEARCH_NEEDED",
                    "REJECT":"REJECTED"}[decision]
            db.execute("""UPDATE engineering_amendments SET state=?,
                scope_decision=?,scope_rationale=?,scope_evidence=?,
                reviewed_by=?,reviewed_at=?,updated=? WHERE id=?""",
                (status,decision,reason,json.dumps(evidence_refs,ensure_ascii=False),
                 reviewer,now(),now(),key))
            self.store._audit(db,proposal["project_id"],proposal["target_id"],
                "REVIEW_ENGINEERING_AMENDMENT",reviewer,key+" / "+decision+
                " / "+reason[:600])
        return self.detail(key)

    def apply(self,key,expected_target_revision,reviewer,reason):
        reviewer=text_value(reviewer,"human amendment authorizer",
                            limit=150,mandatory=True)
        reason=text_value(reason,"amendment authorization explanation",
                          limit=12000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            proposal=self._get(db,key)
            if proposal["state"]!="READY_TO_APPLY":
                raise RuleError("Amendment must receive human scope review before applying")
            target=self.store._require_object(db,proposal["target_id"])
            if (type(expected_target_revision) is not int or
                proposal["base_revision"]!=expected_target_revision or
                target["revision"]!=expected_target_revision):
                raise RuleError("Engineering object revision conflict; no edits applied")
            self._check_pins(db,json.loads(proposal["knowledge_pins"]))
            patch=json.loads(proposal["proposed_patch"])
            self._patch(db,target,patch)
            after=self.store._update_object_tx(
                db,target["id"],{**patch,"expected_revision":expected_target_revision},reviewer)
            stamp=now()
            db.execute("""UPDATE engineering_amendments SET state='APPLIED',
                applied_revision=?,applied_at=?,updated=? WHERE id=?""",
                (after["revision"],stamp,stamp,key))
            self.store._audit(db,proposal["project_id"],target["id"],
                "APPLY_ENGINEERING_AMENDMENT",reviewer,
                key+" / "+reason[:800]+ " / returned to DRAFT if previously approved")
        return {"id":key,"state":"APPLIED","target_id":target["id"],
                "applied_revision":after["revision"],"object_state":after["state"],
                "note":"The engineering object was revised. It is not automatically approved or CODE_READY."}
