"""Read-only Git baseline comparison for a multi-AI engineering workbench.

The baseline is read at an exact commit from the configured local Git checkout,
not from a mutable working-tree file. Neither a commit nor a green check proves
human approval; this module does not import or overwrite approved records.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
import subprocess

from studio_store import Store,RuleError

HEX_SHA=re.compile(r"^[0-9a-fA-F]{40}$")


def git(repo,*args):
    try:
        return subprocess.run(["git","-C",str(repo),*args],capture_output=True,
                              timeout=15,check=True).stdout
    except (OSError,subprocess.TimeoutExpired,subprocess.CalledProcessError) as exc:
        raise RuleError("Cannot read the exact Git engineering baseline: "+args[0]) from exc


def hash_json(value):
    return hashlib.sha256(json.dumps(value,ensure_ascii=False,sort_keys=True,
                       separators=(",",":")).encode("utf-8")).hexdigest()


class GitBaseline:
    def __init__(self,store:Store,worktree,execution):
        self.store=store
        self.worktree=Path(worktree).expanduser().resolve()
        self.execution=execution

    def preview(self,project_id,revision="HEAD"):
        with self.store.db() as db:
            self.store._require_project(db,project_id)
            configured=self.execution._config(db,project_id)
            if not configured:
                raise RuleError("Link GitHub repository before comparing baselines")
            repo_name=configured["repository"]
        root=self.worktree
        if not root.is_dir() or not (root/".git").exists():
            raise RuleError("A local Git worktree is required for baseline comparison")
        from studio_execution import _github_origin_matches
        origin=git(root,"remote","get-url","origin").decode().strip()
        if not _github_origin_matches(origin,repo_name):
            raise RuleError("Git checkout origin does not match configured project repository")
        if revision!="HEAD" and (not isinstance(revision,str) or not HEX_SHA.fullmatch(revision)):
            raise RuleError("Baseline must be HEAD or an exact commit SHA")
        commit=git(root,"rev-parse",revision+"^{commit}").decode().strip()
        if not HEX_SHA.fullmatch(commit):
            raise RuleError("Cannot resolve a full Git commit identity")
        path=f"docs/engineering-control/projects/{project_id}/snapshot.json"
        raw=git(root,"show",commit+":"+path)
        if len(raw)>8_000_000:
            raise RuleError("Git project archive exceeds size limit")
        try:
            archive=json.loads(raw)
        except (json.JSONDecodeError,UnicodeDecodeError) as exc:
            raise RuleError("Git project archive is not valid UTF-8 JSON") from exc
        if not isinstance(archive,dict) or archive.get("schema")!="engineering-project-archive/1":
            raise RuleError("Unsupported Git engineering archive schema")
        data=archive.get("engineering")
        if (not isinstance(data,dict) or archive.get("source_project_id")!=project_id or
            not isinstance(data.get("project"),dict) or
            data["project"].get("id")!=project_id):
            raise RuleError("Git archive does not belong to selected project")
        objs=data.get("objects")
        if not isinstance(objs,list):
            raise RuleError("Git archive lacks engineering objects")
        identifiers=[o.get("id") for o in objs if isinstance(o,dict)]
        if len(identifiers)!=len(objs) or len(set(identifiers))!=len(identifiers):
            raise RuleError("Git archive has invalid/duplicate object identifiers")
        if any(o.get("project_id")!=project_id for o in objs):
            raise RuleError("Git archive includes objects from another project")
        git_objects={o["id"]:o for o in objs}
        local=self.store.export_project(project_id)
        local_objects={o["id"]:o for o in local["objects"]}
        changes=[]
        for oid in sorted(set(git_objects)|set(local_objects)):
            remote=git_objects.get(oid)
            current=local_objects.get(oid)
            if remote is None:label="LOCAL_ONLY"
            elif current is None:label="GIT_ONLY"
            elif hash_json(remote)==hash_json(current):label="SAME"
            elif remote.get("revision")==current.get("revision"):label="SAME_REVISION_CONFLICT"
            elif remote.get("revision",0)>current.get("revision",0):label="GIT_NEWER"
            else:label="LOCAL_NEWER"
            if label!="SAME":
                changes.append({"object_id":oid,
                    "title":(current or remote).get("title",""),
                    "state_in_git":remote.get("state") if remote else None,
                    "state_local":current.get("state") if current else None,
                    "revision_in_git":remote.get("revision") if remote else None,
                    "revision_local":current.get("revision") if current else None,
                    "comparison":label})
        trusted=False
        for ref in ("refs/remotes/origin/main","refs/heads/main"):
            try:
                subprocess.run(["git","-C",str(root),"merge-base","--is-ancestor",commit,ref],
                               check=True,capture_output=True,timeout=15)
                trusted=True
                break
            except (subprocess.CalledProcessError,OSError,subprocess.TimeoutExpired):
                continue
        return {"repository":repo_name,"git_commit":commit,
                "source_path":path,"source_content_sha256":hashlib.sha256(raw).hexdigest(),
                "on_local_main_history":trusted,
                "note":("Reachability from a local main ref is not cryptographic proof of "
                        "human engineering approval. Confirm branch protection and publisher."),
                "archived_at":archive.get("exported_at"),
                "object_count_git":len(git_objects),
                "object_count_local":len(local_objects),
                "differences":changes,
                "state":"IN_SYNC" if not changes else "REVIEW_REQUIRED",
                "action":"Read-only comparison. No local or remote content was overwritten."}
