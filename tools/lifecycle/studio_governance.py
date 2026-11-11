"""V0.8 project-definition governance kernel.

Policy is code-owned and versioned. AI proposals cannot publish product
definitions; human review is a record, not an authenticated identity claim.
Never equate the local owner token with strong identity isolation.
"""
from __future__ import annotations
import hashlib
import json

from studio_store import Store, RuleError, ident, now, text_value
from studio_definition_research import DefinitionResearch
from studio_semantics import legacy_project_projection

POLICY_VERSION = "product-definition-control/2"
FIELDS = ("summary", "body", "version", "variant")
DOMAINS = (
    ("POSITIONING", "产品定位与价值目标"),
    ("STAKEHOLDERS", "用户、角色与使用环境"),
    ("WORKFLOWS", "端到端业务场景与操作流程"),
    ("CAPABILITIES", "产品能力与功能边界"),
    ("SYSTEM_BOUNDARY", "系统分解及外部约束"),
    ("ARCHITECTURE", "系统与软件架构"),
    ("INTERFACES_HARDWARE", "硬件、外部接口与通信"),
    ("VERIFICATION", "验证范围、证据和验收"),
    ("IMPLEMENTATION", "代码、工作包与集成"),
    ("DELIVERY_RISK", "进度、风险和交付"),
)
DOMAIN_KEYS = {key for key, _ in DOMAINS}
DECISIONS = ("IMPACT", "NO_IMPACT", "UNKNOWN")
FOLLOW_LINKS = ("derives", "allocated", "defines", "depends", "implements")


def definition(project):
    return {k: project[k] for k in FIELDS}


def digest(value):
    raw=json.dumps(value,sort_keys=True,ensure_ascii=False,
                   separators=(",",":")).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def _text_fields(value):
    if not isinstance(value,dict) or set(value)!=set(FIELDS):
        raise RuleError("Product definition must supply summary/body/version/variant")
    result={k:text_value(value[k],k,limit=50000 if k=="body" else
                         6000 if k=="summary" else 80) for k in FIELDS}
    if not result["summary"].strip() or not result["body"].strip():
        raise RuleError("Controlled definition requires concrete product goal and boundaries")
    return result


def _json(value,name):
    try:return json.loads(value)
    except (ValueError,TypeError) as exc:
        raise RuleError("Invalid stored "+name) from exc


class DefinitionGovernance:
    def __init__(self,store:Store):
        self.store=store
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS definition_baselines(
                  project_id TEXT NOT NULL REFERENCES projects(id),
                  revision INTEGER NOT NULL, definition_json TEXT NOT NULL,
                  digest TEXT NOT NULL, policy_version TEXT NOT NULL,
                  change_id TEXT NOT NULL, approved_by TEXT NOT NULL,
                  rationale TEXT NOT NULL, created TEXT NOT NULL,
                  PRIMARY KEY(project_id,revision));
                CREATE TABLE IF NOT EXISTS definition_changes(
                  id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                  base_digest TEXT NOT NULL, base_revision INTEGER NOT NULL,
                  proposed_json TEXT NOT NULL, reason TEXT NOT NULL,
                  source_refs TEXT NOT NULL, proposer TEXT NOT NULL,
                  state TEXT NOT NULL, policy_version TEXT NOT NULL,
                  reviewer TEXT, decision_note TEXT, created TEXT NOT NULL,
                  updated TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_definition_changes
                  ON definition_changes(project_id,state);
                CREATE TABLE IF NOT EXISTS definition_reviews(
                  change_id TEXT NOT NULL REFERENCES definition_changes(id),
                  domain TEXT NOT NULL, outcome TEXT NOT NULL,
                  rationale TEXT NOT NULL, evidence_refs TEXT NOT NULL,
                  affected_revisions TEXT NOT NULL, reviewer TEXT NOT NULL,
                  reviewed_at TEXT NOT NULL, PRIMARY KEY(change_id,domain));
                CREATE TABLE IF NOT EXISTS definition_package_reviews(
                  change_id TEXT NOT NULL REFERENCES definition_changes(id),
                  package_id TEXT NOT NULL REFERENCES objects(id),
                  outcome TEXT NOT NULL, rationale TEXT NOT NULL,
                  evidence_refs TEXT NOT NULL, reviewed_revision INTEGER NOT NULL,
                  reviewer TEXT NOT NULL, reviewed_at TEXT NOT NULL,
                  PRIMARY KEY(change_id,package_id));
                CREATE TABLE IF NOT EXISTS definition_invalidations(
                  project_id TEXT NOT NULL REFERENCES projects(id),
                  package_id TEXT NOT NULL REFERENCES objects(id),
                  change_id TEXT NOT NULL REFERENCES definition_changes(id),
                  pinned_revision INTEGER NOT NULL, status TEXT NOT NULL,
                  created TEXT NOT NULL, resolved_by TEXT,
                  resolution TEXT, resolved_at TEXT,
                  PRIMARY KEY(package_id,change_id));
            """)
            DefinitionResearch.ensure(db)
            package_columns={r["name"] for r in db.execute(
                "PRAGMA table_info(definition_package_reviews)")}
            if "research_manifest_sha" not in package_columns:
                db.execute("ALTER TABLE definition_package_reviews ADD COLUMN research_manifest_sha TEXT")

    def _baseline(self,db,project_id):
        row=db.execute("""SELECT * FROM definition_baselines WHERE project_id=?
                  ORDER BY revision DESC LIMIT 1""",(project_id,)).fetchone()
        return dict(row) if row else None

    def _change(self,db,change_id):
        row=db.execute("SELECT * FROM definition_changes WHERE id=?",(change_id,)).fetchone()
        if not row:
            raise RuleError("Definition change not found")
        return dict(row)

    def _snapshot(self,db,project_id):
        project=self.store._require_project(db,project_id)
        baseline=self._baseline(db,project_id)
        current=definition(project)
        return project,baseline,current,digest(current)

    def status(self,project_id):
        with self.store.db() as db:
            project,baseline,current,current_hash=self._snapshot(db,project_id)
            rows=[dict(x) for x in db.execute(
                """SELECT * FROM definition_changes WHERE project_id=?
                   ORDER BY created DESC,id DESC""",(project_id,))]
            pending=[]
            for r in rows:
                pending.append({"id":r["id"],"state":r["state"],
                  "reason":r["reason"],"proposer":r["proposer"],
                  "created":r["created"],"updated":r["updated"]})
            return {"project_id":project_id,"policy_version":POLICY_VERSION,
              "domains":[{"key":k,"label":v} for k,v in DOMAINS],
              "definition":current,"definition_digest":current_hash,
              "interpretation":legacy_project_projection(current),
              "baseline":{"revision":baseline["revision"],"digest":baseline["digest"],
                  "approved_by":baseline["approved_by"],"approved_at":baseline["created"]}
                  if baseline else None,
              "baseline_status":("UNBASELINED" if baseline is None else
                 "DRIFT" if baseline["digest"]!=current_hash else "BASELINED"),
              "changes":pending,
              "warning":"Historic imported project content is not auto-approved; identity and evidence references require independent verification."}

    def detail(self,change_id):
        with self.store.db() as db:
            change=self._change(db,change_id)
            reviews=[dict(x) for x in db.execute(
                "SELECT * FROM definition_reviews WHERE change_id=? ORDER BY domain",
                (change_id,))]
            for review in reviews:
                review["evidence_refs"]=_json(review["evidence_refs"],"evidence references")
                review["affected_revisions"]=_json(review["affected_revisions"],"affected revisions")
            change["proposed"]=_json(change.pop("proposed_json"),"proposed definition")
            change["source_refs"]=_json(change.pop("source_refs"),"source refs")
            package_reviews=[dict(x) for x in db.execute(
                "SELECT * FROM definition_package_reviews WHERE change_id=? ORDER BY package_id",
                (change_id,))]
            for entry in package_reviews:
                entry["evidence_refs"]=_json(entry["evidence_refs"],"package references")
            packages=[{"id":x["id"],"title":x["title"],"revision":x["revision"],
                       "state":x["state"]} for x in db.execute(
                "SELECT id,title,revision,state FROM objects WHERE project_id=? AND kind='work_package' ORDER BY title,id",
                (change["project_id"],))]
            return {"change":change,"reviews":reviews,"package_reviews":package_reviews,
                    "packages":packages,"gate":self._gate(db,change)}

    def propose(self,project_id,proposed,reason,source_refs,proposer,expected_digest):
        proposed=_text_fields(proposed)
        reason=text_value(reason,"reason",limit=12000,mandatory=True)
        proposer=text_value(proposer,"proposer",limit=150,mandatory=True)
        if (not isinstance(source_refs,list) or not source_refs or
            len(source_refs)>30 or any(not isinstance(v,str) or
            not v.strip() or len(v)>1000 for v in source_refs)):
            raise RuleError("At least one identifiable source reference is required")
        if not isinstance(expected_digest,str) or len(expected_digest)!=64:
            raise RuleError("Exact current project definition digest is required")
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            project,baseline,current,hash_now=self._snapshot(db,project_id)
            if expected_digest!=hash_now:
                raise RuleError("Project definition changed; refresh before proposing")
            if baseline and baseline["digest"]!=hash_now:
                raise RuleError("Project definition drift detected; restore trusted baseline")
            if proposed==current:
                raise RuleError("Proposed definition has no substantive differences")
            cid,stamp=ident(),now()
            db.execute("""INSERT INTO definition_changes
              (id,project_id,base_digest,base_revision,proposed_json,reason,
               source_refs,proposer,state,policy_version,created,updated)
               VALUES(?,?,?,?,?,?,?,?,?,?,?,?)""",
               (cid,project_id,hash_now,baseline["revision"] if baseline else 0,
                json.dumps(proposed,ensure_ascii=False),reason,
                json.dumps(source_refs,ensure_ascii=False),proposer,
                "REVIEW",POLICY_VERSION,stamp,stamp))
            DefinitionResearch.seed(db,cid,[domain for domain,_ in DOMAINS])
            self.store._audit(db,project_id,None,"PROPOSE_DEFINITION_CHANGE",proposer,
                              cid+" / "+reason)
        return self.detail(cid)

    def review(self,change_id,domain,outcome,rationale,evidence_refs,affected_ids,
               reviewer,expected_base_digest):
        if domain not in DOMAIN_KEYS or outcome not in DECISIONS:
            raise RuleError("Unsupported domain or impact outcome")
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"impact reasoning",limit=12000,mandatory=True)
        if (not isinstance(evidence_refs,list) or not evidence_refs or
            len(evidence_refs)>30 or any(not isinstance(x,str) or not x.strip()
                  or len(x)>1000 for x in evidence_refs)):
            raise RuleError("Every domain needs a traceable evidence/reference")
        if not isinstance(affected_ids,list) or len(affected_ids)>100 or any(
                not isinstance(x,str) or not x for x in affected_ids):
            raise RuleError("Affected object IDs must be a list")
        if len(set(affected_ids))!=len(affected_ids):
            raise RuleError("Duplicate impacted engineering objects")
        if outcome=="IMPACT" and not affected_ids:
            raise RuleError("Impact cannot be published without a mapped engineering object; investigate mapping gaps")
        if outcome!="IMPACT" and affected_ids:
            raise RuleError("Only an IMPACT assessment can list impacted objects")
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            change=self._change(db,change_id)
            if change["state"]!="REVIEW":
                raise RuleError("Definition change is sealed")
            _,baseline,current,current_hash=self._snapshot(db,change["project_id"])
            if (current_hash!=change["base_digest"] or
                change["base_revision"]!=(baseline["revision"] if baseline else 0) or
                expected_base_digest!=current_hash):
                raise RuleError("Product definition baseline changed; review must restart")
            task=db.execute("""SELECT state,revision,accepted_submission_id
                    FROM definition_research_tasks
                    WHERE change_id=? AND domain=?""",(change_id,domain)).fetchone()
            if (not task or task["state"]!="REVIEWED" or
                not task["accepted_submission_id"]):
                raise RuleError("Domain research must receive human review before impact assessment")
            # Even previously accepted findings cannot be used when a linked
            # technical investigation has since reopened or changed revision.
            evidence=db.execute("""SELECT linked_study_id,linked_study_revision
                    FROM definition_research_submissions WHERE id=?""",
                    (task["accepted_submission_id"],)).fetchone()
            if evidence and evidence["linked_study_id"]:
                study=db.execute("SELECT revision,status FROM studies WHERE id=?",
                                 (evidence["linked_study_id"],)).fetchone()
                if (not study or study["status"]!="DECIDED" or
                    study["revision"]!=evidence["linked_study_revision"]):
                    raise RuleError("Linked technical investigation is stale; reopen domain research")
            affected={}
            for oid in affected_ids:
                obj=self.store._require_object(db,oid)
                if obj["project_id"]!=change["project_id"]:
                    raise RuleError("Cross-project impact is forbidden")
                affected[oid]=obj["revision"]
            db.execute("""INSERT INTO definition_reviews
              (change_id,domain,outcome,rationale,evidence_refs,
               affected_revisions,reviewer,reviewed_at,research_revision)
              VALUES(?,?,?,?,?,?,?,?,?)
              ON CONFLICT(change_id,domain) DO UPDATE SET
                outcome=excluded.outcome,rationale=excluded.rationale,
                evidence_refs=excluded.evidence_refs,
                affected_revisions=excluded.affected_revisions,
                reviewer=excluded.reviewer,reviewed_at=excluded.reviewed_at,
                research_revision=excluded.research_revision""",
               (change_id,domain,outcome,rationale,
                json.dumps(evidence_refs,ensure_ascii=False),
                json.dumps(affected,ensure_ascii=False),reviewer,now(),task["revision"]))
            db.execute("UPDATE definition_changes SET updated=? WHERE id=?",(now(),change_id))
            self.store._audit(db,change["project_id"],None,"REVIEW_DEFINITION_DOMAIN",reviewer,
                              change_id+" / "+domain+" / "+outcome+" / "+rationale)
        return self.detail(change_id)

    @staticmethod
    def _research_manifest(db,change_id):
        failures,tasks=DefinitionResearch.gate_rows(
            db,change_id,[domain for domain,_ in DOMAINS])
        if failures:
            raise RuleError("Required research must be reviewed before implementation impact: "+
                            "; ".join(failures))
        return digest([(domain,tasks[domain]["revision"],
                        tasks[domain]["accepted_submission_id"])
                       for domain,_ in DOMAINS])

    def review_package(self,change_id,package_id,outcome,rationale,evidence_refs,
                       reviewer,expected_base_digest,expected_package_revision):
        if outcome not in ("IMPACT","NO_IMPACT","UNKNOWN"):
            raise RuleError("Unknown implementation package impact decision")
        rationale=text_value(rationale,"implementation review",limit=12000,mandatory=True)
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        if (not isinstance(evidence_refs,list) or not evidence_refs or len(evidence_refs)>30
            or any(not isinstance(x,str) or not x.strip() or len(x)>1000 for x in evidence_refs)):
            raise RuleError("Work package impact review requires referenced evidence")
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            change=self._change(db,change_id)
            if change["state"]!="REVIEW":raise RuleError("Definition change is sealed")
            _,baseline,_,current_hash=self._snapshot(db,change["project_id"])
            if (change["base_digest"]!=current_hash or
                expected_base_digest!=current_hash or
                change["base_revision"]!=(baseline["revision"] if baseline else 0)):
                raise RuleError("Product definition baseline changed; refresh before review")
            obj=self.store._require_object(db,package_id)
            if obj["kind"]!="work_package" or obj["project_id"]!=change["project_id"]:
                raise RuleError("Review target must be a work package in this project")
            if type(expected_package_revision) is not int or obj["revision"]!=expected_package_revision:
                raise RuleError("Work package revision conflict; review latest implementation")
            research_manifest=self._research_manifest(db,change_id)
            db.execute("""INSERT INTO definition_package_reviews
               (change_id,package_id,outcome,rationale,evidence_refs,
                reviewed_revision,reviewer,reviewed_at,research_manifest_sha)
               VALUES(?,?,?,?,?,?,?,?,?)
               ON CONFLICT(change_id,package_id) DO UPDATE SET
                 outcome=excluded.outcome,rationale=excluded.rationale,
                 evidence_refs=excluded.evidence_refs,
                 reviewed_revision=excluded.reviewed_revision,
                 reviewer=excluded.reviewer,reviewed_at=excluded.reviewed_at,
                 research_manifest_sha=excluded.research_manifest_sha""",
               (change_id,package_id,outcome,rationale,
                json.dumps(evidence_refs,ensure_ascii=False),obj["revision"],
                reviewer,now(),research_manifest))
            self.store._audit(db,change["project_id"],package_id,
                "REVIEW_IMPLEMENTATION_IMPACT",reviewer,change_id+" / "+outcome+" / "+rationale)
        return self.detail(change_id)

    def _gate(self,db,change):
        project,baseline,current,current_hash=self._snapshot(db,change["project_id"])
        reasons=[]
        if change["state"]!="REVIEW":reasons.append("Change is no longer under review")
        if change["policy_version"]!=POLICY_VERSION:reasons.append("Governance policy version changed")
        if change["base_digest"]!=current_hash:reasons.append("Product definition content changed since proposal")
        if change["base_revision"]!=(baseline["revision"] if baseline else 0):
            reasons.append("Approved product definition revision changed")
        if baseline and baseline["digest"]!=current_hash:
            reasons.append("Approved baseline differs from project data")
        reviews={x["domain"]:dict(x) for x in db.execute(
            "SELECT * FROM definition_reviews WHERE change_id=?",(change["id"],))}
        research_reasons,research_tasks=DefinitionResearch.gate_rows(
            db,change["id"],[domain for domain,_ in DOMAINS])
        reasons.extend(research_reasons)
        research_manifest=None
        if not research_reasons:
            research_manifest=digest([(domain,research_tasks[domain]["revision"],
                   research_tasks[domain]["accepted_submission_id"])
                   for domain,_ in DOMAINS])
        missing=[]
        for domain,_ in DOMAINS:
            r=reviews.get(domain)
            if r is None:missing.append(domain);continue
            task=research_tasks.get(domain)
            if (not task or r.get("research_revision")!=task["revision"] or
                task["state"]!="REVIEWED"):
                reasons.append(domain+": impact decision not pinned to current reviewed research")
            if r["outcome"]=="UNKNOWN":reasons.append(domain+": unresolved impact")
            if not r["rationale"] or not _json(r["evidence_refs"],"evidence references"):
                reasons.append(domain+": review reasoning/evidence missing")
            affected=_json(r["affected_revisions"],"pinned object revisions")
            if r["outcome"]=="IMPACT" and not affected:
                reasons.append(domain+": impact lacks mapped objects")
            for oid,revision in affected.items():
                obj=db.execute("SELECT revision,project_id FROM objects WHERE id=?",(oid,)).fetchone()
                if not obj or obj["project_id"]!=change["project_id"] or obj["revision"]!=revision:
                    reasons.append(domain+": referenced engineering object changed "+oid)
        if missing:reasons.append("Mandatory impact domains not reviewed: "+", ".join(missing))
        packages=[dict(x) for x in db.execute(
            "SELECT id,title,revision FROM objects WHERE project_id=? AND kind='work_package'",
            (change["project_id"],))]
        reviews_by_package={x["package_id"]:dict(x) for x in db.execute(
            "SELECT * FROM definition_package_reviews WHERE change_id=?",(change["id"],))}
        # Unknown edges cannot silently exempt implementation work. Require an
        # explicit disposition of EVERY work package at a product-level change.
        affected_by_graph={p["id"] for p in self._affected_packages(db,change["id"])}
        missing_packages=[]
        for package in packages:
            review=reviews_by_package.get(package["id"])
            if review is None:
                missing_packages.append(package["title"]);continue
            if review["outcome"]=="UNKNOWN":
                reasons.append("Unresolved work package impact: "+package["title"])
            if review["reviewed_revision"]!=package["revision"]:
                reasons.append("Work package review is stale: "+package["title"])
            if not research_manifest or review.get("research_manifest_sha")!=research_manifest:
                reasons.append("Work package impact review does not reflect current domain research: "+
                               package["title"])
            if not review["rationale"] or not _json(review["evidence_refs"],"package evidence"):
                reasons.append("Work package impact lacks evidence: "+package["title"])
            if review["outcome"]=="NO_IMPACT" and package["id"] in affected_by_graph:
                reasons.append("Work package no-impact conflicts with reviewed dependency graph: "+
                               package["title"])
        implementation=reviews.get("IMPLEMENTATION")
        if (implementation and implementation["outcome"]=="NO_IMPACT" and
            (affected_by_graph or any(r["outcome"]=="IMPACT" for r in reviews_by_package.values()))):
            reasons.append("Implementation domain no-impact contradicts impacted work packages")
        if missing_packages:
            reasons.append("Implementation work packages not individually reviewed: "+
                           ", ".join(missing_packages[:20]))
        return {"ready":not reasons,"reasons":reasons,"missing_domains":missing,
                "reviewed":len(reviews),"required":len(DOMAINS),
                "research_reviewed":sum(t["state"]=="REVIEWED" for t in research_tasks.values()),
                "research_required":len(DOMAINS),
                "package_reviewed":len(reviews_by_package),"package_total":len(packages),
                "new_baseline_revision":change["base_revision"]+1}

    def gate(self,change_id):
        with self.store.db() as db:
            return self._gate(db,self._change(db,change_id))

    def _affected_packages(self,db,change_id):
        targets=set()
        for row in db.execute("""SELECT affected_revisions FROM definition_reviews
                  WHERE change_id=? AND outcome='IMPACT'""",(change_id,)):
            targets.update(_json(row["affected_revisions"],"impacted objects"))
        # Follow only previously reviewed semantic engineering links; a missing link
        # remains an explicit review gap, not a fictitious confirmed relationship.
        reached=set(targets)
        queue=list(targets)
        while queue:
            source=queue.pop()
            for link in db.execute(
              "SELECT target_id,kind,status FROM relations WHERE source_id=?",(source,)):
                if link["status"]!="APPROVED" or link["kind"] not in FOLLOW_LINKS:continue
                if link["target_id"] not in reached:
                    reached.add(link["target_id"]);queue.append(link["target_id"])
        for row in db.execute("""SELECT package_id FROM definition_package_reviews
                  WHERE change_id=? AND outcome='IMPACT'""",(change_id,)):
            reached.add(row["package_id"])
        if not reached:return []
        return [dict(x) for x in db.execute(
            "SELECT id,title,revision FROM objects WHERE project_id=? AND kind='work_package'",
            (self._change(db,change_id)["project_id"],))
            if x["id"] in reached]

    def publish(self,change_id,reviewer,decision_note,expected_base_digest):
        reviewer=text_value(reviewer,"approver",limit=150,mandatory=True)
        decision_note=text_value(decision_note,"approval explanation",limit=12000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            change=self._change(db,change_id)
            if expected_base_digest!=change["base_digest"]:
                raise RuleError("Expected baseline digest changed; refresh approval")
            decision=self._gate(db,change)
            if not decision["ready"]:
                raise RuleError("Definition release blocked: "+"; ".join(decision["reasons"]))
            new_data=_json(change["proposed_json"],"definition proposal")
            baseline_rev=change["base_revision"]+1
            stamp=now()
            db.execute("""UPDATE projects SET summary=?,body=?,version=?,variant=?,updated=?
                          WHERE id=?""",(*[new_data[k] for k in FIELDS],stamp,change["project_id"]))
            new_digest=digest(new_data)
            db.execute("""INSERT INTO definition_baselines
              (project_id,revision,definition_json,digest,policy_version,
               change_id,approved_by,rationale,created)
              VALUES(?,?,?,?,?,?,?,?,?)""",
              (change["project_id"],baseline_rev,
               json.dumps(new_data,ensure_ascii=False),new_digest,
               POLICY_VERSION,change_id,reviewer,decision_note,stamp))
            db.execute("""UPDATE definition_changes SET state='PUBLISHED',reviewer=?,
                          decision_note=?,updated=? WHERE id=?""",
                       (reviewer,decision_note,stamp,change_id))
            packages=self._affected_packages(db,change_id)
            for p in packages:
                db.execute("""INSERT INTO definition_invalidations
                   (project_id,package_id,change_id,pinned_revision,status,created)
                   VALUES(?,?,?,?,?,?)""",
                   (change["project_id"],p["id"],change_id,p["revision"],"OPEN",stamp))
            self.store._audit(db,change["project_id"],None,
                "PUBLISH_PRODUCT_DEFINITION",reviewer,
                "rev "+str(baseline_rev)+" / "+change_id+" / "+decision_note)
        return {"project_id":change["project_id"],"revision":baseline_rev,
                "digest":new_digest,"invalidated_work_packages":packages,
                "status":"PUBLISHED","note":"Published only in local workspace; no Git approved baseline or authenticated identity implied."}

    def reject(self,change_id,reviewer,reason):
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        reason=text_value(reason,"rejection reason",limit=3000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            change=self._change(db,change_id)
            if change["state"]!="REVIEW":raise RuleError("Definition change already sealed")
            db.execute("""UPDATE definition_changes SET state='REJECTED',
                  reviewer=?,decision_note=?,updated=? WHERE id=?""",
                  (reviewer,reason,now(),change_id))
            self.store._audit(db,change["project_id"],None,
                              "REJECT_PRODUCT_DEFINITION",reviewer,change_id+" / "+reason)
        return self.detail(change_id)

    def invalidations(self,project_id):
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            return [dict(x) for x in db.execute(
                """SELECT * FROM definition_invalidations WHERE project_id=?
                   ORDER BY created DESC,package_id""",(project_id,))]

    def resolve(self,package_id,change_id,expected_package_revision,reviewer,rationale):
        reviewer=text_value(reviewer,"reviewer",limit=150,mandatory=True)
        rationale=text_value(rationale,"revalidation rationale",limit=6000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            row=db.execute("""SELECT * FROM definition_invalidations WHERE
               package_id=? AND change_id=? AND status='OPEN'""",(package_id,change_id)).fetchone()
            if not row:raise RuleError("No open definition revalidation for work package")
            obj=self.store._require_object(db,package_id)
            if obj["revision"]!=expected_package_revision:
                raise RuleError("Work package revision changed; inspect latest design")
            if obj["state"]!="APPROVED":
                raise RuleError("Revalidation requires an approved work package revision")
            # Only the baseline-triggered invalidation can be cleared here; every
            # other CODE_READY condition continues to run independently.
            db.execute("""UPDATE definition_invalidations SET status='REVIEWED',
                resolved_by=?,resolution=?,resolved_at=? WHERE package_id=? AND change_id=?""",
                (reviewer,rationale,now(),package_id,change_id))
            self.store._audit(db,obj["project_id"],package_id,"REVALIDATE_DEFINITION_IMPACT",
                              reviewer,change_id+" / "+rationale)
        return {"status":"REVIEWED","package_id":package_id,"change_id":change_id}
