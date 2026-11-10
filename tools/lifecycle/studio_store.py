"""General, single-user engineering lifecycle store. Python stdlib / SQLite only.

An engineering object is NOT a document pathname. Edits create independent
revisions; human approval is explicit; evidence and implementation remain separate.
"""
from __future__ import annotations
from contextlib import contextmanager
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import sqlite3
import uuid

TYPES = ("context", "capability", "system", "requirement", "architecture",
         "interface", "design", "decision", "change", "risk", "work_package",
         "test", "milestone")
LINKS = ("derives", "allocated", "defines", "depends", "implements", "verifies", "impacts")
STATES = ("DRAFT", "IN_REVIEW", "APPROVED", "SUPERSEDED", "OPEN", "CLOSED")
SCHEMA = "engineering-workbench/1"


class RuleError(ValueError):
    pass


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def ident():
    return uuid.uuid4().hex


def text_value(value, field, *, limit=50000, mandatory=False):
    if not isinstance(value, str):
        raise RuleError(f"{field} must be text")
    value = value.strip()
    if mandatory and not value:
        raise RuleError(f"{field} cannot be empty")
    if len(value) > limit:
        raise RuleError(f"{field} is too long")
    return value


def json_object(obj, field="metadata"):
    if not isinstance(obj, dict):
        raise RuleError(f"{field} must be an object")
    if len(json.dumps(obj, ensure_ascii=False)) > 200000:
        raise RuleError(f"{field} exceeds size limit")
    return obj


class Store:
    def __init__(self, filename):
        self.path = Path(filename).expanduser().resolve()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self.db() as db:
            db.executescript("""
              CREATE TABLE IF NOT EXISTS projects(
                id TEXT PRIMARY KEY, name TEXT NOT NULL, summary TEXT NOT NULL,
                body TEXT NOT NULL, version TEXT NOT NULL, variant TEXT NOT NULL,
                created TEXT NOT NULL, updated TEXT NOT NULL);
              CREATE TABLE IF NOT EXISTS objects(
                id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                kind TEXT NOT NULL, title TEXT NOT NULL, body TEXT NOT NULL,
                parent_id TEXT REFERENCES objects(id), state TEXT NOT NULL,
                revision INTEGER NOT NULL, meta TEXT NOT NULL,
                approved_by TEXT, approved_at TEXT, created TEXT NOT NULL, updated TEXT NOT NULL);
              CREATE INDEX IF NOT EXISTS ix_objects_project ON objects(project_id);
              CREATE TABLE IF NOT EXISTS revisions(
                object_id TEXT NOT NULL, revision INTEGER NOT NULL,
                snapshot TEXT NOT NULL, action TEXT NOT NULL, actor TEXT NOT NULL,
                timestamp TEXT NOT NULL, PRIMARY KEY(object_id,revision));
              CREATE TABLE IF NOT EXISTS relations(
                id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                source_id TEXT NOT NULL REFERENCES objects(id),
                target_id TEXT NOT NULL REFERENCES objects(id),
                kind TEXT NOT NULL, created TEXT NOT NULL,
                UNIQUE(source_id,target_id,kind));
              CREATE INDEX IF NOT EXISTS ix_rel_source ON relations(source_id);
              CREATE INDEX IF NOT EXISTS ix_rel_target ON relations(target_id);
              CREATE TABLE IF NOT EXISTS events(
                id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                object_id TEXT, action TEXT NOT NULL, actor TEXT NOT NULL,
                note TEXT NOT NULL, timestamp TEXT NOT NULL);
            """)

    @contextmanager
    def db(self):
        connection = sqlite3.connect(str(self.path), timeout=10)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys=ON")
        connection.execute("PRAGMA busy_timeout=5000")
        try:
            yield connection
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    @staticmethod
    def _row(row):
        if row is None:
            return None
        result = dict(row)
        if "meta" in result:
            result["meta"] = json.loads(result["meta"])
        return result

    def _require_project(self, db, key):
        row = db.execute("SELECT * FROM projects WHERE id=?", (key,)).fetchone()
        if not row:
            raise RuleError("Project does not exist")
        return dict(row)

    def _require_object(self, db, key):
        obj = self._row(db.execute("SELECT * FROM objects WHERE id=?", (key,)).fetchone())
        if not obj:
            raise RuleError("Engineering object does not exist")
        return obj

    def _audit(self, db, project, object_id, action, actor, note=""):
        db.execute("INSERT INTO events VALUES(?,?,?,?,?,?,?)",
                   (ident(), project, object_id, action, actor, note[:3000], now()))

    def projects(self):
        with self.db() as db:
            return [dict(r) for r in db.execute("SELECT * FROM projects ORDER BY updated DESC")]

    def create_project(self, data):
        name = text_value(data.get("name", ""), "project name", limit=140, mandatory=True)
        summary = text_value(data.get("summary", ""), "summary", limit=6000)
        body = text_value(data.get("body", ""), "product background")
        version = text_value(data.get("version", ""), "product version", limit=80)
        variant = text_value(data.get("variant", ""), "variant", limit=80)
        key, instant = ident(), now()
        with self.db() as db:
            db.execute("INSERT INTO projects VALUES(?,?,?,?,?,?,?,?)",
                       (key, name, summary, body, version, variant, instant, instant))
            self._audit(db, key, None, "CREATE_PROJECT", "owner", name)
        return self.project(key)

    def update_project(self, key, data):
        with self.db() as db:
            old = self._require_project(db, key)
            fields = {}
            for field, maximum in (("name", 140), ("summary", 6000), ("body", 50000),
                                   ("version", 80), ("variant", 80)):
                if field in data:
                    fields[field] = text_value(data[field], field, limit=maximum,
                                               mandatory=field == "name")
            if not fields:
                raise RuleError("No project changes submitted")
            fields["updated"] = now()
            updates = ", ".join(f"{f}=?" for f in fields)
            db.execute(f"UPDATE projects SET {updates} WHERE id=?", (*fields.values(), key))
            self._audit(db, key, None, "UPDATE_PROJECT", "owner",
                        "Changed: " + ", ".join(fields))
        return self.project(key)

    def project(self, key):
        with self.db() as db:
            pr = self._require_project(db, key)
            objects = [self._row(r) for r in db.execute(
                "SELECT * FROM objects WHERE project_id=? ORDER BY updated DESC", (key,))]
            relations = [dict(r) for r in db.execute(
                "SELECT * FROM relations WHERE project_id=? ORDER BY created", (key,))]
            events = [dict(r) for r in db.execute(
                "SELECT * FROM events WHERE project_id=? ORDER BY timestamp DESC LIMIT 100", (key,))]
            counts = {k: sum(o["kind"] == k for o in objects) for k in TYPES}
            attention = [o for o in objects if o["kind"] in ("change", "decision", "risk")
                         and o["state"] in ("OPEN", "DRAFT", "IN_REVIEW")]
            unapproved = [o for o in objects if o["kind"] in
                          ("requirement", "architecture", "interface", "design")
                          and o["state"] not in ("APPROVED", "SUPERSEDED")]
            return {"project": pr, "objects": objects, "relations": relations,
                    "events": events, "counts": counts,
                    "attention": attention[:30], "design_review": unapproved[:30],
                    "scope": "All counts are current records in this local project, not measured completion"}

    def object(self, key):
        with self.db() as db:
            item = self._require_object(db, key)
            hist = [json.loads(r["snapshot"]) | {"action": r["action"], "actor": r["actor"],
                                                   "recorded_at": r["timestamp"]}
                    for r in db.execute(
                        "SELECT * FROM revisions WHERE object_id=? ORDER BY revision DESC", (key,))]
            relations = [dict(r) for r in db.execute(
                "SELECT * FROM relations WHERE source_id=? OR target_id=? ORDER BY created",
                (key, key))]
            return {"item": item, "history": hist, "relations": relations}

    def _record(self, db, obj, action, actor):
        db.execute("INSERT INTO revisions VALUES(?,?,?,?,?,?)",
                   (obj["id"], obj["revision"],
                    json.dumps(obj, ensure_ascii=False, sort_keys=True),
                    action, actor, now()))

    def _validate_parent(self, db, project_id, parent_id, moving_id=None):
        if not parent_id:
            return None
        parent = self._require_object(db, parent_id)
        if parent["project_id"] != project_id:
            raise RuleError("Parent belongs to a different project")
        if moving_id and parent_id == moving_id:
            raise RuleError("Object cannot be its own parent")
        cursor = parent
        while cursor.get("parent_id"):
            if cursor["parent_id"] == moving_id:
                raise RuleError("Parent would create a hierarchy cycle")
            cursor = self._require_object(db, cursor["parent_id"])
        return parent_id

    def create_object(self, data):
        project_id = text_value(data.get("project_id", ""), "project", mandatory=True)
        kind = data.get("kind")
        if kind not in TYPES:
            raise RuleError("Unknown engineering object type")
        title = text_value(data.get("title", ""), "title", limit=200, mandatory=True)
        body = text_value(data.get("body", ""), "description")
        metadata = json_object(data.get("meta", {}))
        parent_id = data.get("parent_id") or None
        key, instant = ident(), now()
        state = "OPEN" if kind == "change" else "DRAFT"
        with self.db() as db:
            self._require_project(db, project_id)
            self._validate_parent(db, project_id, parent_id)
            db.execute("""INSERT INTO objects
                (id,project_id,kind,title,body,parent_id,state,revision,meta,
                 approved_by,approved_at,created,updated)
                 VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                 (key,project_id,kind,title,body,parent_id,state,1,
                  json.dumps(metadata,ensure_ascii=False),None,None,instant,instant))
            self._record(db, self._require_object(db, key), "CREATE", "owner")
            self._audit(db, project_id, key, "CREATE_OBJECT", "owner", title)
            db.execute("UPDATE projects SET updated=? WHERE id=?", (instant, project_id))
        return self.object(key)

    def update_object(self, key, data):
        with self.db() as db:
            obj = self._require_object(db, key)
            if "expected_revision" not in data or data["expected_revision"] != obj["revision"]:
                raise RuleError("Revision conflict: refresh before saving")
            changed = {}
            for field, maximum in (("title", 200), ("body", 50000)):
                if field in data:
                    changed[field] = text_value(data[field], field, limit=maximum,
                                                mandatory=field=="title")
            if "meta" in data:
                changed["meta"] = json.dumps(json_object(data["meta"]), ensure_ascii=False)
            if "parent_id" in data:
                changed["parent_id"] = self._validate_parent(
                    db, obj["project_id"], data["parent_id"] or None, moving_id=key)
            if not changed:
                raise RuleError("No object edits submitted")
            changed.update(revision=obj["revision"]+1, updated=now())
            if obj["state"] == "APPROVED":
                changed.update(state="DRAFT", approved_by=None, approved_at=None)
            # Revisions are append-only snapshots. An approved predecessor remains accessible.
            patch = ", ".join(f"{f}=?" for f in changed)
            db.execute(f"UPDATE objects SET {patch} WHERE id=?", (*changed.values(), key))
            current = self._require_object(db, key)
            self._record(db, current, "REVISE", "owner")
            self._audit(db, obj["project_id"], key, "REVISE_OBJECT", "owner",
                        "Revised " + obj["title"])
        return self.object(key)

    def submit(self, key, expected_revision):
        return self._transition(key, expected_revision, "IN_REVIEW", "SUBMIT", "owner", "")

    def approve(self, key, expected_revision, actor, note):
        actor = text_value(actor, "approver", limit=150, mandatory=True)
        note = text_value(note, "decision rationale", limit=3000, mandatory=True)
        with self.db() as db:
            obj = self._require_object(db,key)
            if obj["revision"] != expected_revision:
                raise RuleError("Revision conflict: review latest object first")
            if obj["state"] not in ("DRAFT", "IN_REVIEW"):
                raise RuleError("Only drafts or submitted objects can be approved")
            if obj["kind"] == "change":
                raise RuleError("A change must be closed after assessing impacts, not approved as a design")
            if obj["kind"] == "work_package":
                result = self._readiness(db, key, ignore_approval=True)
                if result["reasons"]:
                    raise RuleError("Cannot approve work package: " + "; ".join(result["reasons"]))
            db.execute("""UPDATE objects SET state='APPROVED',approved_by=?,
                approved_at=?,revision=?,updated=? WHERE id=?""",
                (actor,now(),obj["revision"]+1,now(),key))
            self._record(db, self._require_object(db,key), "APPROVE", actor)
            self._audit(db,obj["project_id"],key,"APPROVE",actor,note)
        return self.object(key)

    def _transition(self, key, revision, state, action, actor, note):
        with self.db() as db:
            obj=self._require_object(db,key)
            if obj["revision"]!=revision:
                raise RuleError("Revision conflict: refresh before changing state")
            if state=="IN_REVIEW" and obj["state"]!="DRAFT":
                raise RuleError("Only draft objects can be submitted for review")
            db.execute("UPDATE objects SET state=?,revision=?,updated=? WHERE id=?",
                       (state,obj["revision"]+1,now(),key))
            self._record(db,self._require_object(db,key),action,actor)
            self._audit(db,obj["project_id"],key,action,actor,note)
        return self.object(key)

    def close_change(self, key, expected_revision, actor, note):
        actor=text_value(actor,"reviewer",limit=150,mandatory=True)
        note=text_value(note,"closure decision",limit=3000,mandatory=True)
        with self.db() as db:
            obj=self._require_object(db,key)
            if obj["kind"]!="change" or obj["state"]!="OPEN":
                raise RuleError("Only open changes can be closed")
            if obj["revision"]!=expected_revision:
                raise RuleError("Revision conflict: refresh before closing")
            edges=[dict(r) for r in db.execute(
                "SELECT * FROM relations WHERE source_id=? AND kind='impacts'",(key,))]
            if not edges:
                raise RuleError("Add at least one assessed impact relation before closing")
            assessments=obj["meta"].get("assessments", {})
            if any(assessments.get(e["target_id"]) not in ("REVISE","NO_CHANGE","INVESTIGATE")
                   for e in edges):
                raise RuleError("Assess each linked target before closing the change")
            db.execute("""UPDATE objects SET state='CLOSED',revision=?,updated=? WHERE id=?""",
                       (obj["revision"]+1,now(),key))
            self._record(db,self._require_object(db,key),"CLOSE",actor)
            self._audit(db,obj["project_id"],key,"CLOSE_CHANGE",actor,note)
        return self.object(key)

    def create_relation(self, data):
        source_id=text_value(data.get("source_id",""),"source",mandatory=True)
        target_id=text_value(data.get("target_id",""),"target",mandatory=True)
        kind=data.get("kind")
        if kind not in LINKS:
            raise RuleError("Unknown relation type")
        if source_id==target_id:
            raise RuleError("An item cannot link to itself")
        with self.db() as db:
            source=self._require_object(db,source_id)
            target=self._require_object(db,target_id)
            if source["project_id"]!=target["project_id"]:
                raise RuleError("Cross-project links are not available in this version")
            if kind=="impacts" and source["kind"]!="change":
                raise RuleError("Only a change object can create an impact relation")
            if kind=="depends" and self._reachable(db, target_id, source_id, {"depends"}):
                raise RuleError("Dependency would create a cycle")
            key=ident()
            try:
                db.execute("INSERT INTO relations VALUES(?,?,?,?,?,?)",
                           (key,source["project_id"],source_id,target_id,kind,now()))
            except sqlite3.IntegrityError as exc:
                raise RuleError("Relation already exists") from exc
            self._audit(db,source["project_id"],source_id,"LINK", "owner",
                        kind+": "+source_id+" → "+target_id)
        return {"id":key,"source_id":source_id,"target_id":target_id,"kind":kind}

    def delete_relation(self, key):
        with self.db() as db:
            row=db.execute("SELECT * FROM relations WHERE id=?", (key,)).fetchone()
            if not row:
                raise RuleError("Relation not found")
            r=dict(row)
            db.execute("DELETE FROM relations WHERE id=?", (key,))
            self._audit(db,r["project_id"],r["source_id"],"UNLINK","owner",
                        r["kind"]+": "+r["target_id"])
        return {"deleted":key}

    @staticmethod
    def _reachable(db, origin, destination, types=None):
        visited={origin}
        queue=[origin]
        while queue:
            current=queue.pop(0)
            sql="SELECT target_id,kind FROM relations WHERE source_id=?"
            for r in db.execute(sql,(current,)):
                if types is not None and r["kind"] not in types:
                    continue
                if r["target_id"]==destination:
                    return True
                if r["target_id"] not in visited:
                    visited.add(r["target_id"]);queue.append(r["target_id"])
        return False

    def impact(self, key):
        with self.db() as db:
            root=self._require_object(db,key)
            visited={key}
            queue=[(key, [key])]
            results=[]
            while queue:
                current,trail=queue.pop(0)
                for r in db.execute("SELECT * FROM relations WHERE source_id=?",(current,)):
                    target=r["target_id"]
                    if target in visited:
                        continue
                    visited.add(target)
                    obj=self._require_object(db,target)
                    path=trail+[target]
                    results.append({"id":target,"title":obj["title"],"kind":obj["kind"],
                                    "state":obj["state"],"trace":path,
                                    "relation":r["kind"],"status":"POTENTIALLY_IMPACTED"})
                    queue.append((target,path))
            return {"origin":{"id":key,"title":root["title"]},"affected":results,
                    "note":"Relationship reachability is a review candidate, not automatic proof of impact"}

    def _readiness(self, db, key, ignore_approval=False):
        obj=self._require_object(db,key)
        if obj["kind"]!="work_package":
            raise RuleError("Selected object is not an implementation work package")
        why=[]
        if not ignore_approval and obj["state"]!="APPROVED":
            why.append("实施工作包尚未获得负责人批准")
        meta=obj["meta"]
        for field,label in (("acceptance","可验证验收条件"),("source_sha","目标源码提交身份"),
                            ("file_scope","允许修改的文件范围")):
            if not meta.get(field):
                why.append("缺少"+label)
        upstream=[self._require_object(db,r["source_id"]) for r in db.execute(
            "SELECT source_id FROM relations WHERE target_id=? AND kind='depends'",(key,))]
        if not upstream:
            why.append("尚未关联上游已审查的需求或设计")
        revisions=meta.get("input_revisions",{})
        if not isinstance(revisions,dict):
            why.append("设计输入版本清单格式无效")
            revisions={}
        for input_item in upstream:
            if input_item["state"]!="APPROVED":
                why.append("上游对象尚未批准："+input_item["title"])
            if str(revisions.get(input_item["id"],""))!=str(input_item["revision"]):
                why.append("设计输入修订已变化或未固定："+input_item["title"])
        # OPEN changes targeting a work package or a design input block readiness.
        ids={key,*[i["id"] for i in upstream]}
        for r in db.execute("""SELECT c.title, l.target_id FROM relations l
            JOIN objects c ON c.id=l.source_id WHERE l.kind='impacts'
            AND c.kind='change' AND c.state='OPEN'"""):
            if r["target_id"] in ids:
                why.append("存在未关闭的受影响变更："+r["title"])
        return {"id":key,"ready":not why,"reasons":list(dict.fromkeys(why)),
                "upstream":[{"id":o["id"],"title":o["title"],"revision":o["revision"],
                             "state":o["state"]} for o in upstream]}

    def readiness(self,key):
        with self.db() as db:
            return self._readiness(db,key)

    def context(self,key):
        with self.db() as db:
            status=self._readiness(db,key)
            if not status["ready"]:
                raise RuleError("工作包尚未准入："+"；".join(status["reasons"]))
            obj=self._require_object(db,key)
            upstream=[self._require_object(db,r["source_id"]) for r in db.execute(
                "SELECT source_id FROM relations WHERE target_id=? AND kind='depends'",(key,))]
            lines=[f"# 实施上下文：{obj['title']}","",
                   f"项目：{self._require_project(db,obj['project_id'])['name']}",
                   f"工作包：{key} / r{obj['revision']}",
                   f"代码基线：{obj['meta'].get('source_sha')}","",
                   "## 产品目标及实施范围",obj["body"],"",
                   "## 已批准、固定修订的工程设计"]
            for dep in upstream:
                lines += [f"### {dep['kind']}: {dep['title']} / r{dep['revision']}",dep["body"],""]
            lines += ["## 可修改文件范围",*[
                "- "+p for p in obj["meta"].get("file_scope",[])],
                "","## 验收合同",str(obj["meta"].get("acceptance")),"",
                "## 纪律","发现设计缺口时先记录变更，不由实施 AI 自行改变批准的接口或系统架构。",
                "测试、实测和用户验收必须分别回写证据；本文件不授权物理危险操作。"]
            return {"work_package":key,"markdown":"\n".join(lines),
                    "manifest":{"project_id":obj["project_id"],"work_package_id":key,
                        "revision":obj["revision"],"source_sha":obj["meta"].get("source_sha"),
                        "inputs":[{"id":p["id"],"revision":p["revision"]} for p in upstream]}}

    def export_project(self,key):
        with self.db() as db:
            project=self._require_project(db,key)
            objects=[self._row(r) for r in db.execute("SELECT * FROM objects WHERE project_id=?",(key,))]
            links=[dict(r) for r in db.execute("SELECT * FROM relations WHERE project_id=?",(key,))]
            ids=[o["id"] for o in objects]
            revisions=[]
            for ident_ in ids:
                revisions.extend(dict(r) for r in db.execute(
                    "SELECT * FROM revisions WHERE object_id=? ORDER BY revision",(ident_,)))
            events=[dict(r) for r in db.execute(
                "SELECT * FROM events WHERE project_id=? ORDER BY timestamp",(key,))]
            return {"schema":SCHEMA,"exported_at":now(),"project":project,
                    "objects":objects,"relations":links,"revisions":revisions,"events":events}
