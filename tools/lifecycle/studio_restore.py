"""Conservative, transactional recovery from a structured engineering-project export.

Only restores one complete project into an empty local workspace. It never
overwrites another project and never interprets imported approvals as fresh
human approval or imported evidence links as restored raw files.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import sqlite3

from studio_store import Store, RuleError, SCHEMA
from studio_research import Research
from studio_execution import Execution
from studio_management import Management
from studio_verification import Verification
from studio_governance import DefinitionGovernance, definition, digest

TABLES = (
    "projects","objects","revisions","relations","events",
    "studies","study_options","study_experiments","study_results","study_decisions",
    "github_projects","execution_tasks","manager_proposals",
    "verification_runs","verification_reviews",
    "definition_baselines","definition_changes","definition_reviews",
    "definition_package_reviews","definition_invalidations",
    "definition_research_tasks","definition_research_submissions",
    "definition_research_decisions"
)
RESEARCH = ("studies","options","experiments","results","decisions")
TABLE_KEYS = ("objects","revisions","relations","events","manager_proposals")


def _array(value, where):
    if not isinstance(value,list) or any(not isinstance(x,dict) for x in value):
        raise RuleError(where+" must be an array of records")
    return value


def _validate(archive):
    if not isinstance(archive,dict) or archive.get("schema")!=SCHEMA:
        raise RuleError("Unsupported project export schema")
    project=archive.get("project")
    if not isinstance(project,dict) or not isinstance(project.get("id"),str) or not project["id"]:
        raise RuleError("Archive has no valid project identity")
    pid=project["id"]
    for key in ("name","summary","body","version","variant","created","updated"):
        if key not in project:
            raise RuleError("Project definition missing "+key)
    sections={key:_array(archive.get(key),key) for key in TABLE_KEYS}
    research=archive.get("technical_research")
    if not isinstance(research,dict):
        raise RuleError("Missing technical research section")
    for key in RESEARCH:
        sections[key]=_array(research.get(key),key)
    execution=archive.get("execution")
    if not isinstance(execution,dict):
        raise RuleError("Missing execution section")
    sections["tasks"]=_array(execution.get("tasks"),"tasks")
    verification=archive.get("verification",{"runs":[],"reviews":[]})
    if not isinstance(verification,dict):
        raise RuleError("Invalid verification section")
    sections["runs"]=_array(verification.get("runs"),"runs")
    sections["reviews"]=_array(verification.get("reviews"),"reviews")
    governance=archive.get("governance",{"baselines":[],"changes":[],"reviews":[],
        "package_reviews":[],"invalidations":[],"research_tasks":[],
        "research_submissions":[],"research_decisions":[]})
    if not isinstance(governance,dict):
        raise RuleError("Invalid definition governance section")
    for key in ("baselines","changes","reviews","package_reviews","invalidations",
                "research_tasks","research_submissions","research_decisions"):
        optional=key in ("package_reviews","research_tasks","research_submissions",
                         "research_decisions")
        sections["g_"+key]=_array(governance.get(key,[] if optional else None),
                                 "governance."+key)
    objects=sections["objects"]
    ids={x.get("id") for x in objects}
    if (None in ids or len(ids)!=len(objects) or
        any(x.get("project_id")!=pid for x in objects)):
        raise RuleError("Object IDs are missing, duplicated or cross-project")
    for obj in objects:
        if obj.get("parent_id") and obj["parent_id"] not in ids:
            raise RuleError("Parent object missing from archive")
        if not isinstance(obj.get("meta"),dict):
            raise RuleError("Engineering object metadata must be JSON object")
        seen={obj.get("id")}
        cursor=obj.get("parent_id")
        by_id={x["id"]:x for x in objects}
        while cursor:
            if cursor in seen:
                raise RuleError("Engineering hierarchy contains a cycle")
            seen.add(cursor)
            cursor=by_id[cursor].get("parent_id")
    snapshots={(x.get("object_id"),x.get("revision")):x for x in sections["revisions"]}
    for obj in objects:
        revision=obj.get("revision")
        snap=snapshots.get((obj["id"],revision))
        if snap is None or not isinstance(snap.get("snapshot"),str):
            raise RuleError("Latest engineering revision snapshot missing")
        try:
            old=json.loads(snap["snapshot"])
        except (ValueError,TypeError) as exc:
            raise RuleError("Invalid engineering revision JSON") from exc
        if not isinstance(old,dict) or any(old.get(k)!=obj.get(k) for k in
            ("id","revision","kind","title","body","state","meta")):
            raise RuleError("Latest snapshot disagrees with current engineering object")
    for rel in sections["relations"]:
        if (rel.get("project_id")!=pid or
            rel.get("source_id") not in ids or rel.get("target_id") not in ids):
            raise RuleError("Invalid or cross-project engineering relationship")
    for evt in sections["events"]:
        if evt.get("project_id")!=pid or (
            evt.get("object_id") is not None and evt.get("object_id") not in ids):
            raise RuleError("Audit event belongs to missing/different project")
    for task in sections["tasks"]:
        if task.get("project_id")!=pid or task.get("object_id") not in ids:
            raise RuleError("Execution task references missing/different work package")
    for proposal in sections["manager_proposals"]:
        if proposal.get("project_id")!=pid:
            raise RuleError("Management proposal belongs to another project")
        if not isinstance(proposal.get("source_objects"),list) or any(
            x not in ids for x in proposal["source_objects"]):
            raise RuleError("Management proposal contains unknown source objects")
    studies={x.get("id") for x in sections["studies"]}
    if any(x.get("project_id")!=pid for x in sections["studies"]):
        raise RuleError("Research study belongs to a different project")
    if any(x.get("study_id") not in studies for x in sections["options"]+sections["experiments"]+
           sections["decisions"]):
        raise RuleError("Research study reference is missing")
    options={x.get("id") for x in sections["options"]}
    if any(x.get("option_id") not in options for x in sections["experiments"]+
           sections["decisions"]):
        raise RuleError("Research option reference is missing")
    experiments={x.get("id") for x in sections["experiments"]}
    if any(x.get("experiment_id") not in experiments for x in sections["results"]):
        raise RuleError("Research experiment reference is missing")
    runs={x.get("id") for x in sections["runs"]}
    for run in sections["runs"]:
        if (run.get("project_id")!=pid or run.get("test_object_id") not in ids):
            raise RuleError("Execution evidence references another project or unknown test")
        target=next(x for x in objects if x["id"]==run["test_object_id"])
        if target["kind"]!="test":
            raise RuleError("Execution evidence references a non-test object")
    if any(x.get("run_id") not in runs for x in sections["reviews"]):
        raise RuleError("Evidence review references an unknown run")
    # A recovered archive must not be able to forge a different live
    # product definition by inserting a mismatched approved snapshot.
    baseline_rows=sections["g_baselines"]
    baseline_revisions=[x.get("revision") for x in baseline_rows]
    if any(x.get("project_id")!=pid for x in baseline_rows) or (
        len(set(baseline_revisions))!=len(baseline_rows)):
        raise RuleError("Invalid product definition baseline identities")
    change_ids={c.get("id") for c in sections["g_changes"]}
    if None in change_ids or len(change_ids)!=len(sections["g_changes"]):
        raise RuleError("Duplicate or missing governed change identity")
    for c in sections["g_changes"]:
        if c.get("project_id")!=pid:raise RuleError("Cross-project governed change")
        for key in ("proposed_json","source_refs"):
            try:json.loads(c[key])
            except (ValueError,KeyError,TypeError) as exc:
                raise RuleError("Invalid governed change JSON") from exc
    for b in baseline_rows:
        if b.get("change_id") not in change_ids:
            raise RuleError("Definition baseline references unknown change")
        try:record=json.loads(b["definition_json"])
        except (ValueError,KeyError,TypeError) as exc:
            raise RuleError("Invalid definition baseline JSON") from exc
        if digest(record)!=b.get("digest"):
            raise RuleError("Definition baseline digest does not match recorded content")
    if baseline_rows:
        latest=max(baseline_rows,key=lambda b:b["revision"])
        if latest["digest"]!=digest(definition(project)):
            raise RuleError("Archived active product definition differs from latest baseline")
    for review in sections["g_reviews"]:
        if review.get("change_id") not in change_ids:
            raise RuleError("Domain review references missing governed change")
        for key in ("affected_revisions","evidence_refs"):
            try:value=json.loads(review[key])
            except (ValueError,KeyError,TypeError) as exc:
                raise RuleError("Invalid governed impact review JSON") from exc
            if key=="affected_revisions":
                if not isinstance(value,dict) or any(
                   (oid,rev) not in snapshots for oid,rev in value.items()):
                    raise RuleError("Governed review references unknown engineering revision")
    research_tasks=sections["g_research_tasks"]
    task_ids={t.get("id") for t in research_tasks}
    if None in task_ids or len(task_ids)!=len(research_tasks):
        raise RuleError("Duplicate definition research task identities")
    for task in research_tasks:
        if task.get("change_id") not in change_ids or task.get("state") not in ("OPEN","REVIEWED"):
            raise RuleError("Invalid definition research task")
    submissions=sections["g_research_submissions"]
    submission_ids={x.get("id") for x in submissions}
    if None in submission_ids or len(submission_ids)!=len(submissions):
        raise RuleError("Duplicate research submission identities")
    task_map={x["id"]:x for x in research_tasks}
    submission_map={x["id"]:x for x in submissions}
    for finding in submissions:
        task=task_map.get(finding.get("task_id"))
        if task is None or not isinstance(finding.get("task_revision"),int):
            raise RuleError("Research submission references missing task/revision")
        if finding["task_revision"]>task["revision"]:
            raise RuleError("Research submission references future task revision")
        if finding.get("linked_study_id") and finding["linked_study_id"] not in studies:
            raise RuleError("Research submission references unknown technical study")
        try:refs=json.loads(finding["evidence_refs"])
        except (KeyError,ValueError,TypeError) as exc:
            raise RuleError("Invalid definition research evidence references") from exc
        if not isinstance(refs,list):
            raise RuleError("Definition research evidence must be a list")
    for task in research_tasks:
        accepted=task.get("accepted_submission_id")
        if task["state"]=="REVIEWED" and (
            accepted not in submission_ids or
            submission_map[accepted]["task_id"]!=task["id"]):
            raise RuleError("Reviewed research has no linked accepted finding")
        if task["state"]=="OPEN" and accepted is not None:
            raise RuleError("Open research must not retain accepted submission")
    decisions=sections["g_research_decisions"]
    if len({x.get("id") for x in decisions})!=len(decisions):
        raise RuleError("Duplicate research review decisions")
    for decision in decisions:
        if (decision.get("task_id") not in task_ids or
            decision.get("submission_id") not in submission_ids or
            submission_map[decision["submission_id"]]["task_id"]!=decision["task_id"]):
            raise RuleError("Research decision references mismatched task/submission")
    for review in sections["g_package_reviews"]:
        if (review.get("change_id") not in change_ids or
            review.get("package_id") not in ids):
            raise RuleError("Work-package impact review references missing engineering record")
        package=next(x for x in objects if x["id"]==review["package_id"])
        if (package["kind"]!="work_package" or
            (review["package_id"],review.get("reviewed_revision")) not in snapshots):
            raise RuleError("Work-package impact review references invalid historical revision")
        try:refs=json.loads(review["evidence_refs"])
        except (KeyError,ValueError,TypeError) as exc:
            raise RuleError("Invalid work-package review evidence") from exc
        if not isinstance(refs,list):
            raise RuleError("Work-package review evidence references must be a list")
    for pending in sections["g_invalidations"]:
        if (pending.get("project_id")!=pid or
            pending.get("change_id") not in change_ids or
            pending.get("package_id") not in ids):
            raise RuleError("Invalid definition revalidation reference")
        target=next(x for x in objects if x["id"]==pending["package_id"])
        if target["kind"]!="work_package":
            raise RuleError("Invalidation target must be a work package")
    repository=execution.get("repository")
    if repository is None and sections["tasks"]:
        raise RuleError("Execution tasks require a GitHub configuration")
    return pid,sections,execution


def preview(archive):
    pid,sections,execution=_validate(archive)
    counts={x:len(y) for x,y in sections.items()}
    return {"project_id":pid,"project_name":archive["project"]["name"],
            "records":counts,"repository":execution.get("repository"),
            "mode":"EMPTY_WORKSPACE_ONLY","status":"STRUCTURAL_PRECHECK_PASS",
            "caution":"Identity and links checked; approvals are historical, raw evidence files are NOT included."}


def _insert(db,table,rows):
    # Only call with fixed TABLES constants. No user-provided SQL identifiers.
    if table not in TABLES:
        raise RuleError("Unsupported archive table")
    columns={row["name"] for row in db.execute("PRAGMA table_info("+table+")")}
    for record in rows:
        unknown=set(record)-columns
        if unknown:
            raise RuleError("Archive contains unsupported columns in "+table)
        keys=list(record)
        if not keys:
            raise RuleError("Empty archive row in "+table)
        sql="INSERT INTO "+table+" ("+",".join(keys)+") VALUES ("+(",".join("?" for _ in keys))+")"
        try:
            db.execute(sql,[record[k] for k in keys])
        except (sqlite3.IntegrityError,sqlite3.OperationalError,sqlite3.ProgrammingError) as exc:
            raise RuleError("Archive record failed integrity validation: "+table) from exc


def restore_to_empty(archive,output_database):
    pid,sec,execution=_validate(archive)
    store=Store(output_database)
    research=Research(store)
    engine=Execution(store)
    Management(store,engine,research)
    Verification(store)
    DefinitionGovernance(store)
    del engine,research
    with store.db() as db:
        db.execute("BEGIN IMMEDIATE")
        if db.execute("SELECT COUNT(*) FROM projects").fetchone()[0]:
            raise RuleError("Restore is allowed only into an empty workspace")
        _insert(db,"projects",[archive["project"]])
        objects=[]
        parents=[]
        for obj in sec["objects"]:
            row=dict(obj)
            if row.get("parent_id"):
                parents.append((row["parent_id"],row["id"]))
                row["parent_id"]=None
            row["meta"]=json.dumps(row["meta"],ensure_ascii=False)
            objects.append(row)
        _insert(db,"objects",objects)
        for parent,oid in parents:
            db.execute("UPDATE objects SET parent_id=? WHERE id=?",(parent,oid))
        _insert(db,"revisions",sec["revisions"])
        _insert(db,"relations",sec["relations"])
        _insert(db,"events",sec["events"])
        for table,key in (("studies","studies"),("study_options","options"),
                          ("study_experiments","experiments"),("study_results","results"),
                          ("study_decisions","decisions")):
            _insert(db,table,sec[key])
        if execution.get("repository") is not None:
            _insert(db,"github_projects",[{
                "project_id":pid,"repository":execution["repository"],
                "fetched_at":execution.get("synced_at"),
                "snapshot":json.dumps(execution["github_snapshot"],ensure_ascii=False)
                           if execution.get("github_snapshot") is not None else None,
                "last_error":None
            }])
        _insert(db,"execution_tasks",sec["tasks"])
        proposals=[]
        for p in sec["manager_proposals"]:
            row=dict(p)
            row["source_objects"]=json.dumps(row["source_objects"],ensure_ascii=False)
            proposals.append(row)
        _insert(db,"manager_proposals",proposals)
        _insert(db,"verification_runs",sec["runs"])
        _insert(db,"verification_reviews",sec["reviews"])
        for table,key in (("definition_changes","g_changes"),
                          ("definition_research_tasks","g_research_tasks"),
                          ("definition_research_submissions","g_research_submissions"),
                          ("definition_research_decisions","g_research_decisions"),
                          ("definition_baselines","g_baselines"),
                          ("definition_reviews","g_reviews"),
                          ("definition_package_reviews","g_package_reviews"),
                          ("definition_invalidations","g_invalidations")):
            _insert(db,table,sec[key])
        errors=db.execute("PRAGMA foreign_key_check").fetchall()
        if errors:
            raise RuleError("Foreign-key validation failed; entire restore rolled back")
    return store.project(pid)


def main(argv=None):
    parser=argparse.ArgumentParser(description="Preview or restore a single project archive to empty SQLite")
    parser.add_argument("--archive",type=Path,required=True)
    parser.add_argument("--data",type=Path,required=True)
    parser.add_argument("--apply",action="store_true")
    args=parser.parse_args(argv)
    if args.archive.stat().st_size>12_000_000:
        parser.error("Archive exceeds maximum 12 MB")
    data=json.loads(args.archive.read_text(encoding="utf-8"))
    print(json.dumps(preview(data),ensure_ascii=False,indent=2))
    if args.apply:
        result=restore_to_empty(data,args.data)
        print("Restored project ID: "+result["project"]["id"]+
              " (historical approval state retained; not newly approved)")


if __name__=="__main__":
    main()
