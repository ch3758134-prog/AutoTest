"""Explicit opt-in SAEF archive import. Imported bytes are NOT approved designs."""
from __future__ import annotations
from pathlib import Path
import hashlib
import json
import re

from studio_store import Store, RuleError, ident, now


def _read_catalog(repo):
    root = Path(repo).resolve() / "SAEF"
    control = root / "engineering" / "control"
    registry = control / "work_products.json"
    if not registry.is_file():
        raise RuleError("No SAEF/engineering/control/work_products.json in configured checkout")
    try:
        records = json.loads(registry.read_text(encoding="utf-8"))
        gates = json.loads((control / "project_state.json").read_text(encoding="utf-8")).get("gates", {})
    except (OSError, ValueError, TypeError) as exc:
        raise RuleError("Cannot parse SAEF control files: " + str(exc)) from exc
    if not isinstance(records, dict) or len(records) > 10000:
        raise RuleError("Invalid SAEF work-product registry")
    return root, records, gates


def entries(repo):
    root, records, gates = _read_catalog(repo)
    result = []
    for code, record in sorted(records.items()):
        relative = record.get("path", "")
        if not relative or not isinstance(relative, str) or Path(relative).is_absolute():
            continue
        file = (root / relative).resolve()
        if not file.is_relative_to(root) or not file.is_file():
            continue
        if file.stat().st_size > 300000:
            continue
        raw = file.read_bytes()
        body = raw.decode("utf-8", errors="replace")
        found = re.search(r"(?m)^#\s+(.+)$", body)
        title = found.group(1).strip()[:170] if found else str(code)
        sha = hashlib.sha256(raw).hexdigest()
        saved_sha = record.get("accepted_sha256")
        gate = gates.get(str(code).split("-",1)[0],{}).get("status","UNKNOWN")
        result.append({"code":str(code), "title":title, "body":body,
            "source_path":"SAEF/"+relative.replace("\\","/"), "actual_sha256":sha,
            "registered_sha256":saved_sha, "registered_state":record.get("status","UNKNOWN"),
            "historic_gate":gate, "content_match":(
                "MATCH" if saved_sha==sha else ("DRIFT" if saved_sha else "NO_ACCEPTED_HASH"))})
    return result


def preview(repo, store: Store, project_id):
    data = entries(repo)
    with store.db() as db:
        store._require_project(db,project_id)
        existing = {json.loads(r["meta"]).get("source_path") for r in db.execute(
            "SELECT meta FROM objects WHERE project_id=? AND kind='reference'",(project_id,))}
    return {"available":len(data), "new":sum(x["source_path"] not in existing for x in data),
            "already_imported":sum(x["source_path"] in existing for x in data),
            "source_drift":sum(x["content_match"]=="DRIFT" for x in data),
            "note":"Historical documents are copied as DRAFT reference records, NEVER approved or verified automatically"}


def import_saef(repo, store: Store, project_id):
    data = entries(repo)
    inserted, skipped = 0, 0
    with store.db() as db:
        store._require_project(db,project_id)
        seen = {json.loads(r["meta"]).get("source_path") for r in db.execute(
            "SELECT meta FROM objects WHERE project_id=? AND kind='reference'",(project_id,))}
        for item in data:
            if item["source_path"] in seen:
                skipped+=1
                continue
            key, stamp = ident(), now()
            meta={k:v for k,v in item.items() if k not in ("code","title","body")}
            meta["legacy_id"]=item["code"]
            meta["imported_as"]="HISTORICAL_REFERENCE_NOT_CURRENT_APPROVAL"
            db.execute("""INSERT INTO objects
              (id,project_id,kind,title,body,parent_id,state,revision,meta,
               approved_by,approved_at,created,updated)
               VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)""",
               (key,project_id,"reference",item["code"]+" · "+item["title"],item["body"],
                None,"DRAFT",1,json.dumps(meta,ensure_ascii=False),None,None,stamp,stamp))
            store._record(db,store._require_object(db,key),"IMPORT_ARCHIVE","owner")
            seen.add(item["source_path"])
            inserted+=1
        if inserted:
            store._audit(db,project_id,None,"IMPORT_SAEF_ARCHIVE","owner",
                         "Imported "+str(inserted)+" historical references; none approved automatically")
            db.execute("UPDATE projects SET updated=? WHERE id=?",(now(),project_id))
    return {"imported":inserted,"skipped":skipped,
            "warning":"Read-only copy of historical SAEF documents; authoritative originals unchanged"}
