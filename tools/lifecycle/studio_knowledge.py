"""Engineering knowledge ledger: traceable statements and semantic links.

The ledger is additive and never rewrites legacy product columns, publishes a
product definition, approves design or dispatches coding. Semantics guide
reasoning; there is no keyword or sentence-length eligibility check.
"""
from __future__ import annotations
import json
from studio_store import Store, RuleError, ident, now, text_value
from studio_semantics import load_catalog

PROVENANCE = ("USER_INPUT", "DOCUMENT", "OBSERVATION", "AI_INTERPRETATION", "OPEN_QUESTION")
RELATIONS = ("SUPPORTS", "CONTRADICTS", "REFINES", "CONTEXT_FOR", "IMPACTS", "DERIVED_FROM")
DECISIONS = ("ACCEPT", "REQUEST_RESEARCH", "REJECT")


def _refs(data, label, maximum=60):
    if (not isinstance(data, list) or len(data)>maximum or
        any(not isinstance(x,str) or not x.strip() or len(x)>2000 for x in data)):
        raise RuleError(label+" must be a list of nonempty source references")
    return [x.strip() for x in data]


def _decode(row):
    result=dict(row)
    for name in ("concept_ids","source_refs","related_objects"):
        if name in result:
            result[name]=json.loads(result[name])
    return result


class EngineeringKnowledge:
    def __init__(self,store: Store):
        self.store=store
        with store.db() as db:
            db.executescript("""
              CREATE TABLE IF NOT EXISTS engineering_knowledge(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id),
                title TEXT NOT NULL,
                statement TEXT NOT NULL,
                interpretation TEXT NOT NULL,
                concept_ids TEXT NOT NULL,
                provenance TEXT NOT NULL,
                source_refs TEXT NOT NULL,
                related_objects TEXT NOT NULL,
                origin_role TEXT NOT NULL,
                created_by TEXT NOT NULL,
                revision INTEGER NOT NULL,
                state TEXT NOT NULL,
                reviewed_by TEXT,
                review_reason TEXT,
                reviewed_at TEXT,
                created TEXT NOT NULL,
                updated TEXT NOT NULL);
              CREATE INDEX IF NOT EXISTS idx_knowledge_project
                ON engineering_knowledge(project_id,state);
              CREATE TABLE IF NOT EXISTS engineering_knowledge_revisions(
                item_id TEXT NOT NULL REFERENCES engineering_knowledge(id),
                revision INTEGER NOT NULL,
                snapshot TEXT NOT NULL,
                operation TEXT NOT NULL,
                actor TEXT NOT NULL,
                created TEXT NOT NULL,
                PRIMARY KEY(item_id,revision));
              CREATE TABLE IF NOT EXISTS engineering_knowledge_links(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id),
                source_id TEXT NOT NULL REFERENCES engineering_knowledge(id),
                target_id TEXT NOT NULL REFERENCES engineering_knowledge(id),
                relation TEXT NOT NULL,
                rationale TEXT NOT NULL,
                source_revision INTEGER NOT NULL,
                target_revision INTEGER NOT NULL,
                origin_role TEXT NOT NULL,
                proposed_by TEXT NOT NULL,
                state TEXT NOT NULL,
                reviewed_by TEXT,
                review_reason TEXT,
                created TEXT NOT NULL,
                reviewed_at TEXT);
              CREATE INDEX IF NOT EXISTS idx_knowledge_links_project
                ON engineering_knowledge_links(project_id,state);
            """)

    @staticmethod
    def _item(db,item_id):
        row=db.execute("SELECT * FROM engineering_knowledge WHERE id=?",(item_id,)).fetchone()
        if not row:raise RuleError("Engineering knowledge entry does not exist")
        return dict(row)

    @staticmethod
    def _check_project_objects(db,project_id,objects):
        if not isinstance(objects,list) or len(objects)>100 or len(set(objects))!=len(objects):
            raise RuleError("Related engineering object IDs must be unique")
        for oid in objects:
            if not isinstance(oid,str) or not oid:
                raise RuleError("Related engineering object ID must be text")
            obj=db.execute("SELECT project_id FROM objects WHERE id=?",(oid,)).fetchone()
            if not obj or obj["project_id"]!=project_id:
                raise RuleError("Knowledge entry cannot reference missing or cross-project object")

    @staticmethod
    def _fields(payload):
        if not isinstance(payload,dict):
            raise RuleError("Knowledge entry must be an object")
        title=text_value(payload.get("title",""),"knowledge title",limit=200,mandatory=True)
        statement=text_value(payload.get("statement",""),"knowledge statement",limit=20000,mandatory=True)
        interpretation=text_value(payload.get("interpretation",""),"interpretation and limits",
                                  limit=20000,mandatory=True)
        provenance=payload.get("provenance")
        if provenance not in PROVENANCE:
            raise RuleError("Unknown provenance role")
        concepts=payload.get("concept_ids",[])
        catalog,_=load_catalog()
        allowed={x["id"] for x in catalog["concepts"]}
        if (not isinstance(concepts,list) or not concepts or len(concepts)>len(allowed) or
            any(not isinstance(x,str) or x not in allowed for x in concepts) or
            len(concepts)!=len(set(concepts))):
            raise RuleError("Concept IDs must refer to the current semantic handbook")
        refs=_refs(payload.get("source_refs"),"Knowledge sources")
        if not refs:
            raise RuleError("Preserve at least one source or conversation reference")
        objects=payload.get("related_objects",[])
        if not isinstance(objects,list):
            raise RuleError("Related engineering objects must be a list")
        return title,statement,interpretation,concepts,provenance,refs,objects

    @staticmethod
    def _history(db,item,operation,actor):
        db.execute("""INSERT INTO engineering_knowledge_revisions
                (item_id,revision,snapshot,operation,actor,created)
                VALUES(?,?,?,?,?,?)""",
                (item["id"],item["revision"],
                 json.dumps(item,ensure_ascii=False,sort_keys=True),operation,actor,now()))

    def list(self,project_id):
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            items=[_decode(r) for r in db.execute(
                "SELECT * FROM engineering_knowledge WHERE project_id=? ORDER BY created,id",
                (project_id,))]
            links=[dict(r) for r in db.execute(
                "SELECT * FROM engineering_knowledge_links WHERE project_id=? ORDER BY created,id",
                (project_id,))]
            revisions=[]
            for item in items:
                revisions.extend(dict(r) for r in db.execute(
                    "SELECT * FROM engineering_knowledge_revisions WHERE item_id=? ORDER BY revision",
                    (item["id"],)))
            return {
                "schema":"engineering-knowledge-ledger/1",
                "project_id":project_id,
                "items":items,"links":links,"revisions":revisions,
                "note":"Accepted means a human recorded review, not independent verification of source truth or engineering release.",
            }

    def propose(self,project_id,payload,actor,origin_role):
        actor=text_value(actor,"knowledge author",limit=150,mandatory=True)
        if origin_role not in ("AI_CANDIDATE","OWNER_CANDIDATE"):
            raise RuleError("Origin role comes from authenticated capability")
        title,statement,interpretation,concepts,provenance,refs,objects=self._fields(payload)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            self.store._require_project(db,project_id)
            self._check_project_objects(db,project_id,objects)
            key,stamp=ident(),now()
            db.execute("""INSERT INTO engineering_knowledge
                (id,project_id,title,statement,interpretation,concept_ids,
                 provenance,source_refs,related_objects,origin_role,created_by,
                 revision,state,reviewed_by,review_reason,reviewed_at,created,updated)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (key,project_id,title,statement,interpretation,
                 json.dumps(concepts,ensure_ascii=False),provenance,
                 json.dumps(refs,ensure_ascii=False),
                 json.dumps(objects,ensure_ascii=False),
                 origin_role,actor,1,"CANDIDATE",None,None,None,stamp,stamp))
            self._history(db,self._item(db,key),"PROPOSE",actor)
            self.store._audit(db,project_id,None,"PROPOSE_ENGINEERING_KNOWLEDGE",
                             actor,key+" / "+title)
        return {"id":key,"state":"CANDIDATE","revision":1}

    def revise(self,item_id,expected_revision,payload,actor,origin_role):
        actor=text_value(actor,"knowledge author",limit=150,mandatory=True)
        if origin_role not in ("AI_CANDIDATE","OWNER_CANDIDATE"):
            raise RuleError("Origin role comes from authenticated capability")
        title,statement,interpretation,concepts,provenance,refs,objects=self._fields(payload)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            item=self._item(db,item_id)
            if item["state"]!="CANDIDATE":
                raise RuleError("Reviewed knowledge is immutable; propose a new item linked by REFINES")
            if type(expected_revision) is not int or expected_revision!=item["revision"]:
                raise RuleError("Knowledge revision conflict")
            self._check_project_objects(db,item["project_id"],objects)
            revision=item["revision"]+1
            db.execute("""UPDATE engineering_knowledge SET title=?,statement=?,
                 interpretation=?,concept_ids=?,provenance=?,source_refs=?,
                 related_objects=?,revision=?,updated=? WHERE id=?""",
                (title,statement,interpretation,json.dumps(concepts,ensure_ascii=False),
                 provenance,json.dumps(refs,ensure_ascii=False),
                 json.dumps(objects,ensure_ascii=False),revision,now(),item_id))
            self._history(db,self._item(db,item_id),"REVISE",actor)
            self.store._audit(db,item["project_id"],None,
                              "REVISE_ENGINEERING_KNOWLEDGE",actor,item_id)
        return {"id":item_id,"state":"CANDIDATE","revision":revision}

    def review(self,item_id,expected_revision,decision,reviewer,reason):
        if decision not in DECISIONS:raise RuleError("Unknown knowledge review decision")
        reviewer=text_value(reviewer,"knowledge reviewer",limit=150,mandatory=True)
        reason=text_value(reason,"review explanation",limit=12000,mandatory=True)
        state={"ACCEPT":"ACCEPTED","REQUEST_RESEARCH":"RESEARCH_NEEDED",
               "REJECT":"REJECTED"}[decision]
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            item=self._item(db,item_id)
            if item["state"]!="CANDIDATE":raise RuleError("Knowledge was already reviewed")
            if type(expected_revision) is not int or item["revision"]!=expected_revision:
                raise RuleError("Knowledge revision conflict")
            revision=item["revision"]+1
            stamp=now()
            db.execute("""UPDATE engineering_knowledge SET state=?,revision=?,
                 reviewed_by=?,review_reason=?,reviewed_at=?,updated=? WHERE id=?""",
                (state,revision,reviewer,reason,stamp,stamp,item_id))
            self._history(db,self._item(db,item_id),"REVIEW_"+decision,reviewer)
            self.store._audit(db,item["project_id"],None,
                              "REVIEW_ENGINEERING_KNOWLEDGE",reviewer,
                              item_id+" / "+decision)
        return {"id":item_id,"state":state,"revision":revision}

    def propose_link(self,project_id,source_id,target_id,relation,rationale,actor,origin_role):
        if relation not in RELATIONS:raise RuleError("Unsupported engineering knowledge relation")
        rationale=text_value(rationale,"link rationale",limit=8000,mandatory=True)
        actor=text_value(actor,"link proposer",limit=150,mandatory=True)
        if origin_role not in ("AI_CANDIDATE","OWNER_CANDIDATE"):
            raise RuleError("Origin role comes from authenticated capability")
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            self.store._require_project(db,project_id)
            source=self._item(db,source_id);target=self._item(db,target_id)
            if (source_id==target_id or source["project_id"]!=project_id or
                target["project_id"]!=project_id):
                raise RuleError("Knowledge links must connect distinct items in the same project")
            key,stamp=ident(),now()
            db.execute("""INSERT INTO engineering_knowledge_links
              (id,project_id,source_id,target_id,relation,rationale,
               source_revision,target_revision,origin_role,proposed_by,
               state,reviewed_by,review_reason,created,reviewed_at)
               VALUES(?,?,?,?,?,?,?,?,?,?,?,NULL,NULL,?,NULL)""",
              (key,project_id,source_id,target_id,relation,rationale,
               source["revision"],target["revision"],origin_role,actor,"CANDIDATE",stamp))
            self.store._audit(db,project_id,None,"PROPOSE_KNOWLEDGE_LINK",actor,
                              key+" / "+relation)
        return {"id":key,"state":"CANDIDATE"}

    def review_link(self,link_id,decision,reviewer,reason):
        if decision not in ("CONFIRM","REJECT"):raise RuleError("Unknown link decision")
        reviewer=text_value(reviewer,"link reviewer",limit=150,mandatory=True)
        reason=text_value(reason,"link review explanation",limit=12000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            link=db.execute("SELECT * FROM engineering_knowledge_links WHERE id=?",
                            (link_id,)).fetchone()
            if not link:raise RuleError("Knowledge link does not exist")
            if link["state"]!="CANDIDATE":raise RuleError("Link already reviewed")
            if decision=="CONFIRM":
                source=self._item(db,link["source_id"]);target=self._item(db,link["target_id"])
                if (source["state"]!="ACCEPTED" or target["state"]!="ACCEPTED" or
                    source["revision"]!=link["source_revision"] or
                    target["revision"]!=link["target_revision"]):
                    raise RuleError("Link endpoints must be currently accepted at pinned revisions")
            state="CONFIRMED" if decision=="CONFIRM" else "REJECTED"
            db.execute("""UPDATE engineering_knowledge_links SET
                state=?,reviewed_by=?,review_reason=?,reviewed_at=? WHERE id=?""",
                (state,reviewer,reason,now(),link_id))
            self.store._audit(db,link["project_id"],None,"REVIEW_KNOWLEDGE_LINK",
                              reviewer,link_id+" / "+decision)
        return {"id":link_id,"state":state}
