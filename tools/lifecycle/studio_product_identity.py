"""Canonical product *identity* lifecycle (V2).

Only the stable identity statement is baselined here. Project background,
release version, hardware configuration, system decomposition and test evidence
are distinct engineering information, not fields of a 'complete definition'.

Legacy definition_changes/baselines are historical records only. Old data is
never silently reclassified or promoted into this authority.
"""
from __future__ import annotations
import hashlib
import json
from studio_store import Store, RuleError, ident, now, text_value

RELATIONS = frozenset(("derives","allocated","defines","depends","implements"))
OUTCOMES = frozenset(("IMPACT","NO_IMPACT","UNKNOWN"))


def sha(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,ensure_ascii=False,
               separators=(",",":")).encode("utf-8")).hexdigest()


def refs(value,field="source references",required=True):
    if (not isinstance(value,list) or len(value)>100 or
        any(not isinstance(x,str) or not x.strip() or len(x)>2000 for x in value)):
        raise RuleError(field+" must be a list of source reference strings")
    if required and not value:
        raise RuleError(field+" must not be empty")
    return [x.strip() for x in value]


class ProductIdentity:
    def __init__(self,store:Store):
        self.store=store
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS product_identity_baselines(
                  project_id TEXT NOT NULL REFERENCES projects(id),
                  revision INTEGER NOT NULL,
                  statement TEXT NOT NULL,
                  digest TEXT NOT NULL,
                  proposal_id TEXT NOT NULL,
                  rationale TEXT NOT NULL,
                  approved_by TEXT NOT NULL,
                  approved_at TEXT NOT NULL,
                  PRIMARY KEY(project_id,revision));
                CREATE TABLE IF NOT EXISTS product_identity_proposals(
                  id TEXT PRIMARY KEY,
                  project_id TEXT NOT NULL REFERENCES projects(id),
                  base_revision INTEGER NOT NULL,
                  base_digest TEXT NOT NULL,
                  statement TEXT NOT NULL,
                  reasoning TEXT NOT NULL,
                  source_refs TEXT NOT NULL,
                  knowledge_ids TEXT NOT NULL,
                  proposed_by TEXT NOT NULL,
                  origin_role TEXT NOT NULL,
                  state TEXT NOT NULL,
                  scope_rationale TEXT,
                  scope_evidence TEXT,
                  reviewer TEXT,
                  decision_note TEXT,
                  reviewed_at TEXT,
                  created TEXT NOT NULL,
                  updated TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_identity_proposals_project
                  ON product_identity_proposals(project_id,state);
                CREATE TABLE IF NOT EXISTS product_identity_impacts(
                  proposal_id TEXT NOT NULL REFERENCES product_identity_proposals(id),
                  object_id TEXT NOT NULL REFERENCES objects(id),
                  object_revision INTEGER NOT NULL,
                  disposition TEXT NOT NULL,
                  rationale TEXT NOT NULL,
                  evidence_refs TEXT NOT NULL,
                  reviewed_by TEXT NOT NULL,
                  reviewed_at TEXT NOT NULL,
                  PRIMARY KEY(proposal_id,object_id));
                CREATE TABLE IF NOT EXISTS product_identity_invalidations(
                  project_id TEXT NOT NULL REFERENCES projects(id),
                  proposal_id TEXT NOT NULL REFERENCES product_identity_proposals(id),
                  package_id TEXT NOT NULL REFERENCES objects(id),
                  pinned_revision INTEGER NOT NULL,
                  state TEXT NOT NULL,
                  resolved_by TEXT,
                  resolution TEXT,
                  created TEXT NOT NULL,
                  resolved_at TEXT,
                  PRIMARY KEY(proposal_id,package_id));
            """)

    @staticmethod
    def _baseline(db,project_id):
        x=db.execute("""SELECT * FROM product_identity_baselines
             WHERE project_id=? ORDER BY revision DESC LIMIT 1""",(project_id,)).fetchone()
        return dict(x) if x else None

    @staticmethod
    def _proposal(db,proposal_id):
        x=db.execute("SELECT * FROM product_identity_proposals WHERE id=?",
                     (proposal_id,)).fetchone()
        if not x:raise RuleError("Product identity proposal does not exist")
        return dict(x)

    @staticmethod
    def _decode_proposal(item):
        result=dict(item)
        result["source_refs"]=json.loads(result["source_refs"])
        result["knowledge_ids"]=json.loads(result["knowledge_ids"])
        if result.get("scope_evidence") is not None:
            result["scope_evidence"]=json.loads(result["scope_evidence"])
        return result

    @staticmethod
    def _snapshot(db,project_id):
        b=ProductIdentity._baseline(db,project_id)
        return b,(b["revision"] if b else 0),(b["digest"] if b else sha({"statement":""}))

    @staticmethod
    def _check_knowledge(db,project_id,ids,accepted=False):
        if (not isinstance(ids,list) or len(ids)>200 or
            any(not isinstance(x,str) or not x.strip() for x in ids) or
            len(set(ids))!=len(ids)):
            raise RuleError("Linked knowledge IDs must be a unique list")
        for key in ids:
            row=db.execute("""SELECT project_id,state FROM engineering_knowledge
                    WHERE id=?""",(key,)).fetchone()
            if not row or row["project_id"]!=project_id:
                raise RuleError("Product identity references unknown or cross-project knowledge")
            if accepted and row["state"]!="ACCEPTED":
                raise RuleError("Linked knowledge must be reviewed before identity publication")

    def status(self,project_id):
        with self.store.db() as db:
            project=self.store._require_project(db,project_id)
            baseline,rev,d=self._snapshot(db,project_id)
            proposals=[{"id":x["id"],"statement":x["statement"],
                        "state":x["state"],"reasoning":x["reasoning"],
                        "created":x["created"],"origin_role":x["origin_role"]}
                       for x in db.execute("""SELECT id,statement,state,reasoning,
                         created,origin_role FROM product_identity_proposals
                         WHERE project_id=? ORDER BY created DESC,id DESC""",(project_id,))]
            return {"project_id":project_id,"schema":"product-identity/2",
                    "state":"BASELINED" if baseline else "UNBASELINED",
                    "baseline":baseline,"revision":rev,"digest":d,
                    "proposals":proposals,
                    "legacy_project_context":{"summary":project["summary"],
                      "body":project["body"],"version":project["version"],
                      "variant":project["variant"]},
                    "warning":"Legacy project text and old four-field definition approvals are historical context only; none is automatically a canonical product identity."}

    def propose(self,project_id,statement,reasoning,source_refs,knowledge_ids,actor,origin_role,
                expected_digest):
        if origin_role not in ("AI_CANDIDATE","OWNER_CANDIDATE"):
            raise RuleError("Unrecognized capability role")
        statement=text_value(statement,"product identity",limit=12000,mandatory=True)
        reasoning=text_value(reasoning,"semantic reasoning",limit=20000,mandatory=True)
        actor=text_value(actor,"proposer",limit=150,mandatory=True)
        source_refs=refs(source_refs)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            self.store._require_project(db,project_id)
            self._check_knowledge(db,project_id,knowledge_ids)
            base,revision,d=self._snapshot(db,project_id)
            if d!=expected_digest:raise RuleError("Product identity baseline changed; refresh")
            if base and statement==base["statement"]:
                raise RuleError("Identity proposal does not change the current statement")
            key,stamp=ident(),now()
            db.execute("""INSERT INTO product_identity_proposals
                (id,project_id,base_revision,base_digest,statement,reasoning,
                 source_refs,knowledge_ids,proposed_by,origin_role,state,
                 scope_rationale,scope_evidence,reviewer,decision_note,
                 reviewed_at,created,updated)
                 VALUES(?,?,?,?,?,?,?,?,?,?,?,NULL,NULL,NULL,NULL,NULL,?,?)""",
                 (key,project_id,revision,d,statement,reasoning,
                  json.dumps(source_refs,ensure_ascii=False),
                  json.dumps(knowledge_ids,ensure_ascii=False),actor,origin_role,
                  "CANDIDATE",stamp,stamp))
            self.store._audit(db,project_id,None,"PROPOSE_PRODUCT_IDENTITY",actor,key)
        return {"id":key,"state":"CANDIDATE","base_revision":revision}

    def detail(self,proposal_id):
        with self.store.db() as db:
            proposal=self._proposal(db,proposal_id)
            impacts=[dict(x) for x in db.execute(
                "SELECT * FROM product_identity_impacts WHERE proposal_id=? ORDER BY object_id",
                (proposal_id,))]
            for item in impacts:item["evidence_refs"]=json.loads(item["evidence_refs"])
            baseline,revision,d=self._snapshot(db,proposal["project_id"])
            return {"proposal":self._decode_proposal(proposal),"impacts":impacts,
                    "gate":self._gate(db,proposal),"current_revision":revision,
                    "current_digest":d}

    def review_scope(self,proposal_id,expected_digest,reviewer,rationale,evidence_refs):
        reviewer=text_value(reviewer,"scope reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"scope assessment",limit=20000,mandatory=True)
        evidence_refs=refs(evidence_refs)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            p=self._proposal(db,proposal_id)
            if p["state"] not in ("CANDIDATE","SCOPED"):
                raise RuleError("Proposal is no longer open for scope review")
            _,rev,d=self._snapshot(db,p["project_id"])
            if expected_digest!=d or p["base_digest"]!=d or p["base_revision"]!=rev:
                raise RuleError("Product identity baseline changed")
            db.execute("""UPDATE product_identity_proposals SET state='SCOPED',
                scope_rationale=?,scope_evidence=?,reviewer=?,updated=? WHERE id=?""",
                (rationale,json.dumps(evidence_refs,ensure_ascii=False),reviewer,now(),proposal_id))
            self.store._audit(db,p["project_id"],None,"REVIEW_IDENTITY_SCOPE",reviewer,proposal_id)
        return self.detail(proposal_id)

    def assess_impact(self,proposal_id,object_id,expected_object_revision,
                      disposition,rationale,evidence_refs,reviewer):
        if disposition not in OUTCOMES:raise RuleError("Invalid impact decision")
        rationale=text_value(rationale,"impact reasoning",limit=12000,mandatory=True)
        reviewer=text_value(reviewer,"impact reviewer",limit=150,mandatory=True)
        evidence_refs=refs(evidence_refs)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            p=self._proposal(db,proposal_id)
            if p["state"] not in ("CANDIDATE","SCOPED"):
                raise RuleError("Cannot edit impact after identity publication")
            obj=self.store._require_object(db,object_id)
            if obj["project_id"]!=p["project_id"]:
                raise RuleError("Impact must reference an object in the same project")
            if type(expected_object_revision) is not int or obj["revision"]!=expected_object_revision:
                raise RuleError("Engineering object revision changed")
            db.execute("""INSERT INTO product_identity_impacts
                (proposal_id,object_id,object_revision,disposition,
                 rationale,evidence_refs,reviewed_by,reviewed_at)
                 VALUES(?,?,?,?,?,?,?,?)
                 ON CONFLICT(proposal_id,object_id) DO UPDATE SET
                   object_revision=excluded.object_revision,
                   disposition=excluded.disposition,rationale=excluded.rationale,
                   evidence_refs=excluded.evidence_refs,
                   reviewed_by=excluded.reviewed_by,reviewed_at=excluded.reviewed_at""",
                (proposal_id,object_id,obj["revision"],disposition,rationale,
                 json.dumps(evidence_refs,ensure_ascii=False),reviewer,now()))
            self.store._audit(db,p["project_id"],object_id,"ASSESS_IDENTITY_IMPACT",
                              reviewer,proposal_id+" / "+disposition)
        return self.detail(proposal_id)

    def _gate(self,db,p):
        reasons=[]
        _,revision,d=self._snapshot(db,p["project_id"])
        if p["state"]!="SCOPED":reasons.append("Owner scope assessment is not complete")
        if p["base_revision"]!=revision or p["base_digest"]!=d:
            reasons.append("Identity baseline has changed")
        if not p["scope_rationale"] or not p["scope_evidence"]:
            reasons.append("Scope rationale and supporting context are required")
        links=json.loads(p["knowledge_ids"])
        try:self._check_knowledge(db,p["project_id"],links,accepted=True)
        except RuleError as e:reasons.append(str(e))
        impacts=[dict(x) for x in db.execute(
            "SELECT * FROM product_identity_impacts WHERE proposal_id=?",(p["id"],))]
        for impact in impacts:
            obj=db.execute("SELECT project_id,revision FROM objects WHERE id=?",
                           (impact["object_id"],)).fetchone()
            if not obj or obj["project_id"]!=p["project_id"] or obj["revision"]!=impact["object_revision"]:
                reasons.append("Engineering object changed since impact assessment: "+impact["object_id"])
            if impact["disposition"]=="UNKNOWN":
                reasons.append("Unresolved engineering impact: "+impact["object_id"])
        return {"ready":not reasons,"reasons":reasons,
                "assessed_objects":len(impacts),
                "impacted_objects":sum(x["disposition"]=="IMPACT" for x in impacts),
                "note":"Scope is human-reviewed and traceable, not mechanically exhaustive. An empty impact list is not proof that no engineering elements are affected."}

    def publish(self,proposal_id,expected_digest,reviewer,decision_note):
        reviewer=text_value(reviewer,"approver",limit=150,mandatory=True)
        decision_note=text_value(decision_note,"approval justification",limit=20000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            p=self._proposal(db,proposal_id)
            if expected_digest!=p["base_digest"]:
                raise RuleError("Identity baseline revision changed")
            gate=self._gate(db,p)
            if not gate["ready"]:raise RuleError("Cannot publish identity: "+"; ".join(gate["reasons"]))
            stamp=now()
            rev=p["base_revision"]+1
            identity={"statement":p["statement"]}
            value_hash=sha(identity)
            db.execute("""INSERT INTO product_identity_baselines
                (project_id,revision,statement,digest,proposal_id,rationale,
                 approved_by,approved_at) VALUES(?,?,?,?,?,?,?,?)""",
                (p["project_id"],rev,p["statement"],value_hash,proposal_id,
                 decision_note,reviewer,stamp))
            db.execute("""UPDATE product_identity_proposals SET state='PUBLISHED',
                reviewed_at=?,decision_note=?,reviewer=?,updated=? WHERE id=?""",
                (stamp,decision_note,reviewer,stamp,proposal_id))
            objects={x["object_id"] for x in db.execute(
                """SELECT object_id FROM product_identity_impacts
                   WHERE proposal_id=? AND disposition='IMPACT'""",(proposal_id,))}
            queue=list(objects)
            while queue:
                source=queue.pop()
                for edge in db.execute("SELECT target_id,kind,status FROM relations WHERE source_id=?",
                                       (source,)):
                    if edge["status"]!="APPROVED" or edge["kind"] not in RELATIONS:
                        continue
                    if edge["target_id"] not in objects:
                        objects.add(edge["target_id"]);queue.append(edge["target_id"])
            if objects:
                marks=",".join("?" for _ in objects)
                rows=db.execute("""SELECT id,revision FROM objects
                   WHERE project_id=? AND kind='work_package' AND id IN ("""+
                   marks+")",(p["project_id"],*objects)).fetchall()
                for item in rows:
                    db.execute("""INSERT INTO product_identity_invalidations
                       (project_id,proposal_id,package_id,pinned_revision,
                        state,resolved_by,resolution,created,resolved_at)
                       VALUES(?,?,?,?,'OPEN',NULL,NULL,?,NULL)""",
                       (p["project_id"],proposal_id,item["id"],item["revision"],stamp))
            self.store._audit(db,p["project_id"],None,"PUBLISH_PRODUCT_IDENTITY",reviewer,
                              proposal_id+" / revision "+str(rev))
        return self.status(p["project_id"])

    def reject(self,proposal_id,reviewer,rationale):
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"rejection reasoning",limit=12000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            p=self._proposal(db,proposal_id)
            if p["state"] not in ("CANDIDATE","SCOPED"):
                raise RuleError("Identity proposal already finalized")
            db.execute("""UPDATE product_identity_proposals SET state='REJECTED',
                reviewer=?,decision_note=?,reviewed_at=?,updated=? WHERE id=?""",
                (reviewer,rationale,now(),now(),proposal_id))
            self.store._audit(db,p["project_id"],None,"REJECT_PRODUCT_IDENTITY",reviewer,
                              proposal_id+" / "+rationale)
        return {"id":proposal_id,"state":"REJECTED"}

    def invalidations(self,project_id):
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            return [dict(x) for x in db.execute(
                """SELECT * FROM product_identity_invalidations
                   WHERE project_id=? ORDER BY created DESC,package_id""",(project_id,))]

    def resolve(self,proposal_id,package_id,expected_revision,reviewer,rationale):
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"revalidation rationale",limit=12000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            entry=db.execute("""SELECT * FROM product_identity_invalidations
                   WHERE proposal_id=? AND package_id=?""",(proposal_id,package_id)).fetchone()
            if not entry or entry["state"]!="OPEN":
                raise RuleError("Identity impact invalidation is not open")
            obj=self.store._require_object(db,package_id)
            if (obj["project_id"]!=entry["project_id"] or
                type(expected_revision) is not int or obj["revision"]!=expected_revision or
                obj["revision"]<=entry["pinned_revision"] or obj["state"]!="APPROVED"):
                raise RuleError("Affected work package needs a newer approved revision")
            db.execute("""UPDATE product_identity_invalidations SET state='RESOLVED',
                resolved_by=?,resolution=?,resolved_at=? WHERE proposal_id=? AND package_id=?""",
                (reviewer,rationale,now(),proposal_id,package_id))
            self.store._audit(db,entry["project_id"],package_id,
                              "RESOLVE_IDENTITY_IMPACT",reviewer,proposal_id)
        return {"proposal_id":proposal_id,"package_id":package_id,"state":"RESOLVED"}
