"""GitHub execution centre for the single-user lifecycle workbench.

GitHub supplies external execution facts; approved engineering data supplies
task authority. A GitHub Issue or green CI check never implies CODE_READY or
physical verification. Sync is explicit and read-only. Worktree publication
only writes reviewable files, never commits, pushes or merges by itself.
"""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.request

from studio_store import RuleError, Store, ident, now, text_value

REPO_PATTERN = re.compile(r"^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")
AGENT_PATTERN = re.compile(r"^[A-Z][A-Z0-9_-]{1,31}$")
MAX_ITEMS = 100
MAX_RESPONSE = 5_000_000


def require_repo(value):
    if not isinstance(value, str) or not REPO_PATTERN.fullmatch(value.strip()):
        raise RuleError("Repository must be in GitHub owner/name form")
    value = value.strip()
    if ".." in value or value.endswith(".git"):
        raise RuleError("Invalid GitHub repository name")
    return value


def require_agent(value):
    if not isinstance(value, str) or not AGENT_PATTERN.fullmatch(value):
        raise RuleError("AI worker must be an uppercase role, e.g. DEVICE or UI")
    return value


def latest_run_for(pr, runs):
    candidates = [x for x in runs if x.get("head_sha") == pr.get("head_sha")]
    return candidates[0] if candidates else None


def normalize_issue(raw):
    return {
        "number": raw["number"], "title": str(raw.get("title") or ""),
        "state": raw.get("state") or "unknown",
        "url": raw.get("html_url"), "updated_at": raw.get("updated_at"),
        "assignees": [x.get("login", "") for x in raw.get("assignees", [])],
        "labels": [x.get("name","") for x in raw.get("labels",[])],
        "body": str(raw.get("body") or "")[:14000],
    }


def normalize_pr(raw):
    head = raw.get("head") or {}
    base = raw.get("base") or {}
    return {
        "number": raw["number"], "title": str(raw.get("title") or ""),
        "state": raw.get("state") or "unknown", "draft": bool(raw.get("draft")),
        "head_sha": head.get("sha"), "head_branch": head.get("ref"),
        "base_branch": base.get("ref"),
        "merged_at": raw.get("merged_at"), "url": raw.get("html_url"),
        "updated_at": raw.get("updated_at"), "body": str(raw.get("body") or "")[:14000],
    }


def normalize_run(raw):
    return {
        "id": raw.get("id"), "name": raw.get("name"),
        "head_sha": raw.get("head_sha"),
        "status": raw.get("status"), "conclusion": raw.get("conclusion"),
        "event": raw.get("event"), "updated_at": raw.get("updated_at"),
        "url": raw.get("html_url"),
    }


def _github_origin_matches(origin, repository):
    repo=require_repo(repository)
    cleaned=origin.strip().replace("\\","/")
    pattern=r"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)" + re.escape(repo) + r"(?:\.git)?$"
    return bool(re.fullmatch(pattern,cleaned,re.IGNORECASE))

class GitHubReader:
    def __init__(self, opener=None, token=None):
        self.opener = opener or urllib.request.urlopen
        self.token = token if token is not None else os.environ.get("GITHUB_TOKEN")

    def _get(self, repository, suffix):
        repo = require_repo(repository)
        url = "https://api.github.com/repos/" + repo + "/" + suffix
        headers = {"Accept":"application/vnd.github+json",
                   "X-GitHub-Api-Version":"2022-11-28",
                   "User-Agent":"engineering-workbench/0.5"}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        request = urllib.request.Request(url, headers=headers)
        try:
            with self.opener(request, timeout=15) as response:
                body = response.read(MAX_RESPONSE + 1)
                if len(body) > MAX_RESPONSE:
                    raise RuleError("GitHub response too large")
                value = json.loads(body)
        except urllib.error.HTTPError as error:
            code = error.code
            raise RuleError(f"GitHub HTTP {code}; check repository visibility, credentials or API limits") from error
        except (urllib.error.URLError, TimeoutError, OSError, json.JSONDecodeError) as error:
            raise RuleError("Could not retrieve GitHub status (check connectivity)") from error
        return value

    def snapshot(self, repository):
        repo = require_repo(repository)
        issues_raw = self._get(repo, "issues?state=all&per_page=100")
        prs_raw = self._get(repo, "pulls?state=all&per_page=100")
        runs_raw = self._get(repo, "actions/runs?per_page=100")
        for collection in (issues_raw, prs_raw):
            if not isinstance(collection, list):
                raise RuleError("Unexpected GitHub API collection response")
        if not isinstance(runs_raw, dict) or not isinstance(runs_raw.get("workflow_runs"), list):
            raise RuleError("Unexpected GitHub Actions response")
        issues = [normalize_issue(x) for x in issues_raw if "pull_request" not in x]
        prs = [normalize_pr(x) for x in prs_raw]
        runs = [normalize_run(x) for x in runs_raw["workflow_runs"]]
        for pr in prs:
            pr["ci"] = latest_run_for(pr, runs)
        return {
            "repository": repo, "fetched_at": now(),
            "source": "GitHub REST API (read-only)",
            "issues": issues, "pull_requests": prs, "workflow_runs": runs,
            "truncated": bool(len(issues_raw) >= MAX_ITEMS or len(prs_raw) >= MAX_ITEMS
                              or len(runs_raw["workflow_runs"]) >= MAX_ITEMS),
            "note": "Not a full repository audit. CI is matched by exact PR HEAD where observed; missing is UNKNOWN.",
        }


class Execution:
    def __init__(self, store: Store, reader=None):
        self.store = store
        self.reader = reader or GitHubReader()
        with store.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS github_projects (
                  project_id TEXT PRIMARY KEY REFERENCES projects(id),
                  repository TEXT NOT NULL,
                  fetched_at TEXT,
                  snapshot TEXT,
                  last_error TEXT);
                CREATE TABLE IF NOT EXISTS execution_tasks (
                  object_id TEXT PRIMARY KEY REFERENCES objects(id),
                  project_id TEXT NOT NULL REFERENCES projects(id),
                  issue_number INTEGER, pr_number INTEGER,
                  agent_role TEXT NOT NULL,
                  state TEXT NOT NULL,
                  dispatched_by TEXT, dispatched_at TEXT,
                  claim_actor TEXT, claimed_at TEXT, pinned_revision INTEGER,
                  pinned_source_sha TEXT, archive_path TEXT,
                  updated TEXT NOT NULL);
            """)

    def _config(self, db, project_id):
        self.store._require_project(db, project_id)
        row = db.execute("SELECT * FROM github_projects WHERE project_id=?",
                         (project_id,)).fetchone()
        return dict(row) if row else None

    def configure(self, project_id, repository):
        repo = require_repo(repository)
        with self.store.db() as db:
            self.store._require_project(db, project_id)
            previous = self._config(db, project_id)
            if previous and previous["repository"] != repo:
                linked = db.execute(
                    "SELECT COUNT(*) FROM execution_tasks WHERE project_id=?",
                    (project_id,)).fetchone()[0]
                if linked:
                    raise RuleError("Cannot change repository while work packages are bound")
            db.execute("""INSERT INTO github_projects(project_id,repository)
                          VALUES(?,?) ON CONFLICT(project_id) DO UPDATE SET
                          repository=excluded.repository, fetched_at=NULL,
                          snapshot=NULL,last_error=NULL""",(project_id,repo))
            self.store._audit(db,project_id,None,"CONFIGURE_GITHUB","owner",repo)
        return self.overview(project_id)

    def refresh(self, project_id):
        with self.store.db() as db:
            config=self._config(db,project_id)
            if not config:
                raise RuleError("Configure GitHub repository first")
            repo=config["repository"]
        try:
            snapshot=self.reader.snapshot(repo)
            if require_repo(snapshot.get("repository"))!=repo:
                raise RuleError("Repository identity mismatch")
        except RuleError as exc:
            with self.store.db() as db:
                db.execute("UPDATE github_projects SET last_error=? WHERE project_id=?",
                           (str(exc)[:500],project_id))
            raise
        with self.store.db() as db:
            current=self._config(db,project_id)
            if not current or current["repository"]!=repo:
                raise RuleError("Repository configuration changed during sync")
            db.execute("""UPDATE github_projects SET fetched_at=?,snapshot=?,last_error=NULL
                          WHERE project_id=?""",
                       (snapshot["fetched_at"],json.dumps(snapshot,ensure_ascii=False),project_id))
            self.store._audit(db,project_id,None,"SYNC_GITHUB","owner",
                              f"{repo}: {len(snapshot['issues'])} issues, {len(snapshot['pull_requests'])} PRs")
        return self.overview(project_id)

    def overview(self, project_id):
        with self.store.db() as db:
            config=self._config(db,project_id)
            tasks=[dict(x) for x in db.execute(
                "SELECT * FROM execution_tasks WHERE project_id=? ORDER BY updated DESC",
                (project_id,))]
            objects=[self.store._row(x) for x in db.execute(
                "SELECT * FROM objects WHERE project_id=? AND kind='work_package'",
                (project_id,))]
            mapping={o["id"]:o for o in objects}
            for task in tasks:
                obj=mapping.get(task["object_id"])
                task["title"]=obj["title"] if obj else "(missing)"
                task["revision"]=obj["revision"] if obj else None
                task["readiness"]=(self.store._readiness(db,task["object_id"])
                                   if obj else {"ready":False,"reasons":["Missing work package"]})
                if task["state"] in ("DISPATCHED","CLAIMED"):
                    if (not task["readiness"]["ready"]
                        or task["revision"]!=task["pinned_revision"]
                        or obj["meta"].get("source_sha")!=task["pinned_source_sha"]):
                        task["effective_state"]="BLOCKED_BY_CHANGED_DESIGN"
                    else:
                        task["effective_state"]=task["state"]
                else:
                    task["effective_state"]=task["state"]
            backlog = [o for o in objects if o["id"] not in {t["object_id"] for t in tasks}]
            return {
                "configured":bool(config),
                "repository":config["repository"] if config else None,
                "fetched_at":config["fetched_at"] if config else None,
                "sync_error":config["last_error"] if config else None,
                "snapshot":json.loads(config["snapshot"]) if config and config["snapshot"] else None,
                "tasks":tasks,
                "unlinked_work_packages":[{"id":x["id"],"title":x["title"],
                    "revision":x["revision"]} for x in backlog],
                "note":"GitHub is execution evidence; local design approvals are not inferred from Issue/PR status.",
            }

    def _task(self, db, package_id):
        row=db.execute("SELECT * FROM execution_tasks WHERE object_id=?",(package_id,)).fetchone()
        if not row:
            raise RuleError("Work package is not linked to GitHub")
        return dict(row)

    def _issue(self, config, number):
        if not config or not config["snapshot"]:
            raise RuleError("Sync GitHub before linking issues")
        snapshot=json.loads(config["snapshot"])
        for issue in snapshot["issues"]:
            if issue["number"]==number:
                return issue
        raise RuleError("Issue not present in last GitHub snapshot; refresh first")

    def _pr(self, config, number):
        if not config or not config["snapshot"]:
            raise RuleError("Sync GitHub before linking PRs")
        snapshot=json.loads(config["snapshot"])
        for pr in snapshot["pull_requests"]:
            if pr["number"]==number:
                return pr
        raise RuleError("PR not present in last GitHub snapshot; refresh first")

    def import_issue(self, project_id, number):
        if type(number) is not int or number<1:
            raise RuleError("Issue number must be a positive integer")
        with self.store.db() as db:
            config=self._config(db,project_id)
            issue=self._issue(config,number)
            row=db.execute("SELECT object_id FROM execution_tasks WHERE project_id=? AND issue_number=?",
                           (project_id,number)).fetchone()
            if row:
                raise RuleError("Issue is already linked to work package " + row["object_id"])
        obj=self.store.create_object({"project_id":project_id,"kind":"work_package",
             "title":issue["title"],"body":issue["body"],
             "meta":{"execution_role":"CODER","acceptance":"",
                     "source_sha":"","file_scope":[],"input_revisions":{},
                     "github_issue_number":number,"github_repository":config["repository"]}})["item"]
        return self.bind(obj["id"],number,"UNASSIGNED")

    def bind(self, package_id, number, agent_role):
        if type(number) is not int or number<1:
            raise RuleError("Issue number must be a positive integer")
        if agent_role!="UNASSIGNED":
            require_agent(agent_role)
        with self.store.db() as db:
            obj=self.store._require_object(db,package_id)
            if obj["kind"]!="work_package":
                raise RuleError("GitHub issue can only bind an implementation work package")
            config=self._config(db,obj["project_id"])
            self._issue(config,number)
            duplicate=db.execute("""SELECT object_id FROM execution_tasks WHERE
                 project_id=? AND issue_number=? AND object_id<>?""",
                (obj["project_id"],number,package_id)).fetchone()
            if duplicate:
                raise RuleError("Issue already bound to work package "+duplicate["object_id"])
            existing=db.execute("SELECT state FROM execution_tasks WHERE object_id=?",
                                (package_id,)).fetchone()
            if existing and existing["state"] in ("DISPATCHED","CLAIMED"):
                raise RuleError("Cannot reassign an active work package; revoke it first")
            db.execute("""INSERT INTO execution_tasks(
                object_id,project_id,issue_number,agent_role,state,updated)
                VALUES(?,?,?,?,'PLANNING',?) ON CONFLICT(object_id) DO UPDATE SET
                issue_number=excluded.issue_number,agent_role=excluded.agent_role,
                state='PLANNING',pinned_revision=NULL,pinned_source_sha=NULL,
                updated=excluded.updated""",
                       (package_id,obj["project_id"],number,agent_role,now()))
            self.store._audit(db,obj["project_id"],package_id,"LINK_GITHUB_ISSUE","owner",
                              str(number)+" / "+agent_role)
        return self.overview(obj["project_id"])

    def link_pr(self, package_id, number):
        if type(number) is not int or number<1:
            raise RuleError("PR number must be a positive integer")
        with self.store.db() as db:
            task=self._task(db,package_id)
            config=self._config(db,task["project_id"])
            pr=self._pr(config,number)
            db.execute("UPDATE execution_tasks SET pr_number=?,updated=? WHERE object_id=?",
                       (number,now(),package_id))
            self.store._audit(db,task["project_id"],package_id,"LINK_GITHUB_PR","owner",
                              str(number)+" / "+(pr["head_sha"] or "NO_SHA"))
        return self.overview(task["project_id"])

    def dispatch(self, package_id, actor, note):
        actor=text_value(actor,"dispatched by",limit=150,mandatory=True)
        note=text_value(note,"dispatch rationale",limit=3000,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            task=self._task(db,package_id)
            if task["state"] not in ("PLANNING","REVOKED"):
                raise RuleError("Task is already dispatched or claimed")
            if task["agent_role"]=="UNASSIGNED":
                raise RuleError("Assign an AI coding role before dispatch")
            config=self._config(db,task["project_id"])
            issue=self._issue(config,task["issue_number"])
            if issue["state"]!="open":
                raise RuleError("GitHub Issue is not open")
            obj=self.store._require_object(db,package_id)
            if obj["meta"].get("execution_role")!="CODER":
                raise RuleError("Only coding-only work packages can enter this AI queue")
            readiness=self.store._readiness(db,package_id)
            if not readiness["ready"]:
                raise RuleError("CODE_READY failed: "+"; ".join(readiness["reasons"]))
            db.execute("""UPDATE execution_tasks SET state='DISPATCHED',
                        dispatched_by=?,dispatched_at=?,claim_actor=NULL,claimed_at=NULL,
                        pinned_revision=?,pinned_source_sha=?,updated=?
                        WHERE object_id=?""",
                       (actor,now(),obj["revision"],obj["meta"]["source_sha"],now(),package_id))
            self.store._audit(db,task["project_id"],package_id,"DISPATCH_AI_TASK",actor,note)
        return self.overview(task["project_id"])

    def revoke(self, package_id, actor, reason):
        actor=text_value(actor,"actor",limit=150,mandatory=True)
        reason=text_value(reason,"revoke reason",limit=3000,mandatory=True)
        with self.store.db() as db:
            task=self._task(db,package_id)
            if task["state"] not in ("DISPATCHED","CLAIMED"):
                raise RuleError("Only dispatched or claimed tasks may be revoked")
            db.execute("UPDATE execution_tasks SET state='REVOKED',updated=? WHERE object_id=?",
                       (now(),package_id))
            self.store._audit(db,task["project_id"],package_id,"REVOKE_AI_TASK",actor,reason)
        return self.overview(task["project_id"])

    def queue(self, project_id, role):
        require_agent(role)
        info=self.overview(project_id)
        # Do not show stale or non-ready tasks to coding workers.
        return [{"object_id":x["object_id"],"issue_number":x["issue_number"],
                 "title":x["title"],"repository":info["repository"],
                 "revision":x["revision"],"source_sha":x["pinned_source_sha"]}
                for x in info["tasks"] if x["agent_role"]==role and
                x["effective_state"]=="DISPATCHED"]

    def claim(self, package_id, role, worker):
        role=require_agent(role)
        worker=text_value(worker,"worker identity",limit=150,mandatory=True)
        with self.store.db() as db:
            db.execute("BEGIN IMMEDIATE")
            task=self._task(db,package_id)
            if task["agent_role"]!=role or task["state"]!="DISPATCHED":
                raise RuleError("Task not dispatched to this AI role or already claimed")
            obj=self.store._require_object(db,package_id)
            readiness=self.store._readiness(db,package_id)
            if (not readiness["ready"] or obj["revision"]!=task["pinned_revision"]
                    or obj["meta"].get("source_sha")!=task["pinned_source_sha"]):
                raise RuleError("Work package design or source baseline changed since dispatch")
            db.execute("""UPDATE execution_tasks SET state='CLAIMED',
                       claim_actor=?,claimed_at=?,updated=? WHERE object_id=? AND state='DISPATCHED'""",
                       (worker,now(),now(),package_id))
            if db.execute("SELECT changes()").fetchone()[0]!=1:
                raise RuleError("Task was claimed concurrently")
            self.store._audit(db,task["project_id"],package_id,"AI_CLAIM_TASK",worker,
                              role+" / GitHub Issue #"+str(task["issue_number"]))
        return self.packet(package_id)

    def packet(self, package_id):
        with self.store.db() as db:
            task=self._task(db,package_id)
            obj=self.store._require_object(db,package_id)
            config=self._config(db,task["project_id"])
            if task["state"] not in ("DISPATCHED","CLAIMED"):
                raise RuleError("Task has not been dispatched")
            readiness=self.store._readiness(db,package_id)
            if (not readiness["ready"] or task["pinned_revision"]!=obj["revision"] or
                task["pinned_source_sha"]!=obj["meta"].get("source_sha")):
                raise RuleError("Task packet invalidated by an upstream change")
            work_context=self.store.context(package_id)
            manifest={
                "schema":"engineering-coding-task/1", "work_package_id":package_id,
                "revision":obj["revision"],"repository":config["repository"],
                "issue_number":task["issue_number"],"pr_number":task["pr_number"],
                "agent_role":task["agent_role"],"source_sha":task["pinned_source_sha"],
                "state":task["state"],"claimed_by":task["claim_actor"],
                "design_manifest":work_context["manifest"],
                "delivery":"Draft PR to main; include exact HEAD, changed files, tests and blockers; never merge without owner approval",
                "forbidden":"No research, architecture redesign, shared-interface changes or unapproved hardware actions",
            }
            markdown=(f"# Coding task: {obj['title']}\n\n"
                      f"GitHub: https://github.com/{config['repository']}/issues/{task['issue_number']}\n"
                      f"Worker role: {task['agent_role']}\n"
                      f"Task state: {task['state']}\n\n"
                      +work_context["markdown"])
            return {"manifest":manifest,"markdown":markdown}

    def archive_project(self, project_id, worktree, actor, note):
        """Write the entire authoritative engineering snapshot into a reviewable Git branch.

        GitHub issues and PRs stay authoritative remotely; avoid duplicating their
        cached bodies in the exported archive. Never commit or push automatically.
        """
        actor=text_value(actor,"archive reviewer",limit=150,mandatory=True)
        note=text_value(note,"archive review note",limit=1000,mandatory=True)
        with self.store.db() as db:
            cfg=self._config(db,project_id)
            if not cfg:
                raise RuleError("Configure GitHub repository before archiving")
            repository=cfg["repository"]
        root=Path(worktree).expanduser().resolve()
        if not root.is_dir() or not (root/".git").exists():
            raise RuleError("Project archive requires an existing Git worktree")
        try:
            branch=subprocess.run(
                ["git","-C",str(root),"branch","--show-current"],
                timeout=8,check=True,capture_output=True,text=True).stdout.strip()
            origin=subprocess.run(
                ["git","-C",str(root),"remote","get-url","origin"],
                timeout=8,check=True,capture_output=True,text=True).stdout.strip()
        except (subprocess.CalledProcessError,OSError,subprocess.TimeoutExpired) as exc:
            raise RuleError("Cannot inspect Git worktree identity") from exc
        if branch in ("", "main", "master"):
            raise RuleError("Refuse engineering archive writes on main/master")
        if not _github_origin_matches(origin,repository):
            raise RuleError("Git origin differs from configured GitHub repository")
        if not re.fullmatch(r"[a-f0-9]{32}",project_id):
            raise RuleError("Invalid project id")
        project_data=self.store.export_project(project_id)
        # GitHub remains authoritative for live execution facts.
        execution=project_data.get("execution",{})
        execution.pop("github_snapshot",None)
        archive={"schema":"engineering-project-archive/1",
                 "source_project_id":project_id,
                 "exported_at":now(),
                 "review_required_before_remote_push":True,
                 "warning":"Archive contains design records, not raw evidence files. Review for confidential content.",
                 "engineering":project_data}
        parent=(root/"docs"/"engineering-control"/"projects"/project_id).resolve()
        if not parent.is_relative_to(root):
            raise RuleError("Unsafe archive path")
        parent.mkdir(parents=True,exist_ok=True)
        target=parent/"snapshot.json"
        if target.is_symlink():
            raise RuleError("Archive target must not be a symlink")
        data=json.dumps(archive,ensure_ascii=False,sort_keys=True,indent=2)+"\n"
        target.write_text(data,encoding="utf-8")
        with self.store.db() as db:
            self.store._audit(db,project_id,None,"EXPORT_PROJECT_TO_GIT",actor,
                branch+" / "+str(target.relative_to(root))+" / "+note)
        return {"repository":repository,"branch":branch,
                "path":str(target.relative_to(root)).replace("\\","/"),
                "sha256":hashlib.sha256(data.encode("utf-8")).hexdigest(),
                "state":"WRITTEN_NOT_COMMITTED",
                "warning":"Review confidential content, then commit and push through a professional Draft PR."}

    def publish(self, package_id, worktree):
        """Create reviewable task files in a non-main Git worktree, without staging/committing."""
        value=self.packet(package_id)
        worktree=Path(worktree).expanduser().resolve()
        if not worktree.is_dir() or not (worktree/".git").exists():
            raise RuleError("Target must be an existing Git worktree")
        try:
            proc=subprocess.run(["git","-C",str(worktree),"branch","--show-current"],
                                check=True,timeout=8,capture_output=True,text=True)
            branch=proc.stdout.strip()
            if not branch or branch in ("main","master"):
                raise RuleError("Refuse to publish task files directly on main/master")
            remote=subprocess.run(["git","-C",str(worktree),"remote","get-url","origin"],
                                   check=True,timeout=8,capture_output=True,text=True).stdout.strip()
        except (subprocess.CalledProcessError, OSError, subprocess.TimeoutExpired) as exc:
            raise RuleError("Unable to verify Git worktree and origin") from exc
        repo=value["manifest"]["repository"]
        norm=remote.lower().replace("\\","/")
        expected=repo.lower()
        if not _github_origin_matches(remote,repo):
            raise RuleError("Git origin does not match the bound GitHub repository")
        out=(worktree/"docs"/"engineering-control"/"tasks").resolve()
        if not out.is_relative_to(worktree):
            raise RuleError("Unsafe task output directory")
        out.mkdir(parents=True,exist_ok=True)
        name=package_id
        if not re.fullmatch(r"[a-f0-9]{32}",name):
            raise RuleError("Invalid package identifier")
        filename=out/(name+".json")
        md=out/(name+".md")
        serial=json.dumps(value["manifest"],ensure_ascii=False,sort_keys=True,indent=2)+"\n"
        if filename.is_symlink() or md.is_symlink():
            raise RuleError("Task path must not be a symbolic link")
        filename.write_text(serial,encoding="utf-8")
        md.write_text(value["markdown"],encoding="utf-8")
        with self.store.db() as db:
            task=self._task(db,package_id)
            db.execute("UPDATE execution_tasks SET archive_path=?,updated=? WHERE object_id=?",
                       (str(filename.relative_to(worktree)).replace("\\","/"),now(),package_id))
            self.store._audit(db,task["project_id"],package_id,"EXPORT_TASK_TO_GIT",
                              "owner",branch+" / "+str(filename.relative_to(worktree)))
        return {"repository":repo,"branch":branch,"files":[
            str(filename.relative_to(worktree)).replace("\\","/"),
            str(md.relative_to(worktree)).replace("\\","/")],
            "status":"WRITTEN_NOT_COMMITTED",
            "note":"Files are in your local branch only. Commit and push under the established PR approval process.",
            "manifest_sha256":hashlib.sha256(serial.encode("utf-8")).hexdigest()}
