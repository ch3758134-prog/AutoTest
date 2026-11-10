"""SAEF catalog and evidence excerpts; read-only and never treats archives as live acceptance."""
from __future__ import annotations
from pathlib import Path
import re
from lifecycle import Workspace, ModelError, inside, content_hash

ORDER = ("P0", "G0", "G1", "G2", "G3", "G4", "G5")
LABELS = {"P0":"产品背景与研发范围","G0":"硬件与现场事实","G1":"系统需求","G2":"总体系统架构","G3":"详细设计","G4":"实施规划","G5":"验证与交付"}

def title_of(raw: str, fallback: str) -> str:
    for line in raw.splitlines():
        match = re.match(r"^#\s+(.+)", line.strip())
        if match:
            return match.group(1).strip()[:180]
    return fallback

def teaser(raw: str) -> str:
    for line in raw.splitlines():
        s = line.strip()
        if not s or s.startswith(("#", "|", ">", "-", "```")):
            continue
        s = s.replace("**", "").replace("*", "")
        if len(s) > 18:
            return s[:300]
    return ""

def catalog(workspace: Workspace) -> dict:
    records = []
    for key, rec in sorted(workspace.work_products.items()):
        relative = rec.get("path", "")
        path = "SAEF/" + relative
        state, title, excerpt = "MISSING", key, ""
        approved_hash = rec.get("accepted_sha256")
        match_state = "NOT_CHECKED"
        if relative:
            source = inside(workspace.root, path)
            if source.is_file():
                source_bytes = source.read_bytes()
                raw = source_bytes.decode("utf-8", errors="replace")
                state, title, excerpt = "PRESENT", title_of(raw, key), teaser(raw)
                match_state = ("MATCH" if content_hash(source_bytes) == approved_hash else "DRIFT") if approved_hash else "NO_ACCEPTED_HASH"
        records.append({"id":key,"phase":key.split("-",1)[0],"title":title,
                        "path":path,"status":rec.get("status","UNKNOWN"),
                        "revision":rec.get("revision"),"excerpt":excerpt,"source_state":state,
                        "approval_content_match":match_state})
    counts = {}
    for rec in records:
        counts[rec["status"]] = counts.get(rec["status"],0)+1
    groups = [{"id":phase,"title":LABELS[phase],
               "gate":workspace.project_state.get("gates",{}).get(phase,{}).get("status","UNKNOWN"),
               "items":[rec for rec in records if rec["phase"]==phase]} for phase in ORDER]
    return {"project_id":workspace.project_state.get("project_id","AutoTest"),
            "working_phase":workspace.project_state.get("working_phase","UNKNOWN"),
            "total":len(records),"status_counts":counts,"groups":groups,
            "content_drift_count":sum(1 for r in records if r["approval_content_match"]=="DRIFT"),
            "scope_note":"Registered historical work products, not a real-time product completion score"}

def document(workspace: Workspace, key: str) -> dict:
    if key not in workspace.work_products:
        raise ModelError(f"Unknown registered work product: {key}")
    rec = workspace.work_products[key]
    relative = rec.get("path","")
    if not relative:
        raise ModelError(f"Missing source path: {key}")
    path = "SAEF/" + relative
    src = inside(workspace.root,path)
    if not src.is_file():
        raise ModelError(f"Missing file: {path}")
    source_bytes = src.read_bytes()
    raw = source_bytes.decode("utf-8", errors="replace")
    approved_hash = rec.get("accepted_sha256")
    match_state = ("MATCH" if content_hash(source_bytes)==approved_hash else "DRIFT") if approved_hash else "NO_ACCEPTED_HASH"
    phase = key.split("-",1)[0]
    return {"id":key,"title":title_of(raw,key),"body":raw,"path":path,
            "status":rec.get("status","UNKNOWN"),"revision":rec.get("revision"),
            "approval_content_match":match_state,"gate":workspace.project_state.get("gates",{}).get(phase,{}).get("status","UNKNOWN"),
            "note":"Historic approval does not establish current implementation or physical acceptance"}

def excerpt_for(node: dict, root: Path) -> dict:
    path = node.get("path")
    if not path:
        return {"text":"","line":None}
    src = inside(root,path)
    if not src.is_file():
        return {"text":"","line":None}
    anchor = node.get("anchor")
    if not anchor:
        return {"text":"","line":None}
    lines = src.read_text(encoding="utf-8", errors="replace").splitlines()
    for i,line in enumerate(lines):
        if anchor in line:
            start = i if line.strip().startswith("|") else max(0,i-1)
            end = i+1 if line.strip().startswith("|") else min(len(lines),i+4)
            return {"text":"\n".join(lines[start:end]).strip()[:1500],"line":i+1}
    return {"text":"","line":None}

def evidence(workspace: Workspace) -> dict:
    nodes=[]
    for key,node in workspace.nodes.items():
        base=workspace.source(key)
        base.update(excerpt_for(node,workspace.root))
        base["description"]=node.get("description","")
        nodes.append(base)
    return {"nodes":nodes,"edges":workspace.manifest["edges"],
            "changes":workspace.manifest.get("changes",[]),"case_id":"KEY_SIM",
            "note":"Review-required relations are only potential links, not confirmed traceability"}
