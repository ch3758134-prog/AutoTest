"""Engineering knowledge ledger: traceable statements and semantic links.

The ledger is additive and never rewrites legacy product columns, publishes a
product definition, approves design or dispatches coding. Semantics guide
reasoning; there is no keyword or sentence-length eligibility check.
"""
from __future__ import annotations
import json
import hashlib
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
              CREATE TABLE IF NOT EXISTS engineering_knowledge_intakes(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id),
                payload_digest TEXT NOT NULL,
                origin_role TEXT NOT NULL,
                proposed_by TEXT NOT NULL,
                result_json TEXT NOT NULL,
                created TEXT NOT NULL,
                UNIQUE(project_id,payload_digest));
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

    def intake(self,project_id,payload,actor,origin_role):
        """Atomic, duplicate-safe candidate import; never auto-reviews data."""
        actor=text_value(actor,"intake author",limit=150,mandatory=True)
        if origin_role not in ("AI_CANDIDATE","OWNER_CANDIDATE"):
            raise RuleError("Unknown intake capability role")
        if not isinstance(payload,dict) or payload.get("schema")!="engineering-knowledge-intake/1":
            raise RuleError("Expected engineering-knowledge-intake/1")
        entries=payload.get("entries",[])
        links=payload.get("relations",[])
        if (not isinstance(entries,list) or not 1<=len(entries)<=50 or
            not isinstance(links,list) or len(links)>100):
            raise RuleError("Knowledge intake requires 1–50 entries and up to 100 links")
        prepared=[]
        aliases=set()
        for entry in entries:
            if not isinstance(entry,dict):
                raise RuleError("Intake entry must be a structured claim")
            alias=text_value(entry.get("alias",""),"local intake alias",limit=80,mandatory=True)
            if alias in aliases:raise RuleError("Duplicate intake alias")
            aliases.add(alias)
            prepared.append((alias,self._fields(entry)))
        prepared_links=[]
        for relation in links:
            if not isinstance(relation,dict):
                raise RuleError("Relation must be an object")
            start,end=relation.get("from"),relation.get("to")
            kind=relation.get("relation")
            if start not in aliases or end not in aliases or start==end:
                raise RuleError("Relation requires two distinct intake aliases")
            if kind not in RELATIONS:raise RuleError("Unknown knowledge relation")
            why=text_value(relation.get("rationale",""),"relationship reasoning",
                           limit=8000,mandatory=True)
            prepared_links.append((start,end,kind,why))
        raw=json.dumps(payload,sort_keys=True,ensure_ascii=False,
                       separators=(",",":")).encode("utf-8")
        fingerprint=hashlib.sha256(raw).hexdigest()
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            self.store._require_project(db,project_id)
            previous=db.execute("""SELECT result_json FROM engineering_knowledge_intakes
                      WHERE project_id=? AND payload_digest=?""",
                      (project_id,fingerprint)).fetchone()
            if previous:
                result=json.loads(previous["result_json"])
                result["already_imported"]=True
                return result
            alias_ids={}
            stamp=now()
            for alias,fields in prepared:
                title,statement,interpretation,concepts,provenance,refs,objects=fields
                self._check_project_objects(db,project_id,objects)
                key=ident();alias_ids[alias]=key
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
                self._history(db,self._item(db,key),"BATCH_PROPOSE",actor)
            link_ids=[]
            for source_alias,target_alias,kind,why in prepared_links:
                key=ident();link_ids.append(key)
                db.execute("""INSERT INTO engineering_knowledge_links
                  (id,project_id,source_id,target_id,relation,rationale,
                   source_revision,target_revision,origin_role,proposed_by,
                   state,reviewed_by,review_reason,created,reviewed_at)
                   VALUES(?,?,?,?,?,?,?,?,?,?,?,NULL,NULL,?,NULL)""",
                  (key,project_id,alias_ids[source_alias],alias_ids[target_alias],
                   kind,why,1,1,origin_role,actor,"CANDIDATE",stamp))
            result={"schema":"engineering-knowledge-intake-result/1",
                "project_id":project_id,"status":"CANDIDATE_ONLY",
                "items":[{"alias":alias,"id":key} for alias,key in alias_ids.items()],
                "links":link_ids,"already_imported":False,
                "note":"No product definition, engineering approval or coding permission was changed."}
            db.execute("""INSERT INTO engineering_knowledge_intakes
                (id,project_id,payload_digest,origin_role,proposed_by,result_json,created)
                VALUES(?,?,?,?,?,?,?)""",
                (ident(),project_id,fingerprint,origin_role,actor,
                 json.dumps(result,ensure_ascii=False),stamp))
            self.store._audit(db,project_id,None,"INTAKE_KNOWLEDGE_CANDIDATES",
                              actor,fingerprint+" / "+str(len(entries))+
                              " entries / "+str(len(links))+" relations")
        return result

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
            if origin_role=="AI_CANDIDATE" and item["origin_role"]=="OWNER_CANDIDATE":
                raise RuleError("AI cannot revise a human-origin candidate")
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
            pinned_source,pinned_target=link["source_revision"],link["target_revision"]
            if decision=="CONFIRM":
                source=self._item(db,link["source_id"]);target=self._item(db,link["target_id"])
                if source["state"]!="ACCEPTED" or target["state"]!="ACCEPTED":
                    raise RuleError("Link endpoints must be currently accepted")
                # Owner acceptance advances the audit revision but does not
                # change the claim. A relation proposed during research must
                # survive that purely administrative transition.
                semantic_fields=("title","statement","interpretation","concept_ids",
                                 "provenance","source_refs","related_objects")
                for endpoint,pinned in ((source,pinned_source),(target,pinned_target)):
                    if endpoint["revision"]!=pinned:
                        prior=db.execute("""SELECT snapshot FROM engineering_knowledge_revisions
                            WHERE item_id=? AND revision=?""",(endpoint["id"],pinned)).fetchone()
                        if not prior:
                            raise RuleError("Link references missing source snapshot")
                        old=json.loads(prior["snapshot"])
                        if any(old.get(k)!=endpoint[k] for k in semantic_fields):
                            raise RuleError("Knowledge semantic content changed after relation proposal")
                pinned_source,pinned_target=source["revision"],target["revision"]
            state="CONFIRMED" if decision=="CONFIRM" else "REJECTED"
            db.execute("""UPDATE engineering_knowledge_links SET
                state=?,source_revision=?,target_revision=?,
                reviewed_by=?,review_reason=?,reviewed_at=? WHERE id=?""",
                (state,pinned_source,pinned_target,reviewer,reason,now(),link_id))
            self.store._audit(db,link["project_id"],None,"REVIEW_KNOWLEDGE_LINK",
                              reviewer,link_id+" / "+decision)
        return {"id":link_id,"state":state}
