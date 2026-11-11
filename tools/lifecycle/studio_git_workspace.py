"""Hydrate an isolated trial workspace from the exact Git engineering archive.

Never alters an existing project/database. All engineering objects use the same
Store and revision tables as the normal HTML application. The Git snapshot is normally an AI candidate. A narrowly scoped owner-approved
P0 document requires an explicit matching approval record; this does not confer
implementation authority, bypass P0 Gate, or merge the PR.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import subprocess

from studio_store import RuleError, Store
from studio_restore import preview, restore_to_empty

PROJECT_ID = re.compile(r"[a-z0-9][a-z0-9_-]{0,62}\Z")
GIT_SHA = re.compile(r"[0-9a-f]{40,64}\Z")
MAX_BYTES = 8_000_000


def _git(root: Path, *args: str) -> bytes:
    try:
        result = subprocess.run(
            ["git", "-C", str(root), *args],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            timeout=12, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise RuleError("Cannot read Git worktree: " + str(exc)) from exc
    if result.returncode:
        raise RuleError("Committed engineering archive is unavailable: " +
                        result.stderr.decode("utf-8", errors="replace")[:280])
    return result.stdout


def committed_project(root, project_id="autotest"):
    root = Path(root).expanduser().resolve()
    if not PROJECT_ID.fullmatch(project_id):
        raise RuleError("Invalid Git engineering project identity")
    commit = _git(root, "rev-parse", "HEAD").decode("ascii", errors="replace").strip()
    if not GIT_SHA.fullmatch(commit):
        raise RuleError("Git checkout HEAD is not a full commit ID")
    path = "docs/engineering-control/projects/" + project_id + "/snapshot.json"
    raw = _git(root, "show", commit + ":" + path)
    if len(raw) > MAX_BYTES:
        raise RuleError("Committed engineering archive exceeds limit")
    try:
        wrapper = json.loads(raw.decode("utf-8"))
    except (ValueError, UnicodeDecodeError) as exc:
        raise RuleError("Invalid Git engineering archive encoding or JSON") from exc
    if (not isinstance(wrapper, dict) or
            wrapper.get("schema") != "engineering-project-archive/1" or
            wrapper.get("source_project_id") != project_id or
            wrapper.get("authority") not in (
                "AI_CANDIDATE_NOT_APPROVED", "OWNER_APPROVED_P0_DOCUMENT")):
        raise RuleError("Git engineering archive header does not match project")
    archive = wrapper.get("engineering")
    result = preview(archive)
    if result["project_id"] != project_id:
        raise RuleError("Git archive and restored project identity differ")
    approved = [o for o in archive.get("objects", [])
                if o.get("state") in ("APPROVED", "SUPERSEDED")]
    identity = archive.get("product_identity", {})
    baselines = identity.get("baselines", [])
    if archive.get("governance", {}).get("baselines"):
        raise RuleError("Git archive may not bootstrap unrelated governed design approval")
    authority = wrapper["authority"]
    if authority == "AI_CANDIDATE_NOT_APPROVED":
        if approved:
            raise RuleError("The Git AI candidate may not fabricate approved objects")
        if baselines:
            raise RuleError("The Git AI candidate may not issue product approvals")
    else:
        # Narrow first approval: an explicitly owner-approved P0 document and
        # its canonical identity. Reject any unrelated implementation approvals.
        # Consistency checks are not cryptographic authentication of a chat;
        # Git review/access control remains the external authority boundary.
        decision = wrapper.get("approval")
        if not isinstance(decision, dict):
            raise RuleError("Owner-approved Git archive needs explicit decision record")
        doc_id = decision.get("approved_object_id")
        identity_id = decision.get("canonical_identity_proposal_id")
        actor = decision.get("approved_by")
        source_sha = decision.get("reviewed_source_commit")
        if (decision.get("scope") != "P0_PRODUCT_DEFINITION_AND_INITIATION_V0_1" or
                decision.get("canonical_identity_revision") != 1 or
                not isinstance(actor, str) or not actor.strip() or
                not isinstance(decision.get("explicit_instruction"), str) or
                not decision["explicit_instruction"].strip() or
                not isinstance(source_sha, str) or
                not GIT_SHA.fullmatch(source_sha) or
                not isinstance(decision.get("approval_id"), str) or
                not decision["approval_id"].strip() or
                not isinstance(decision.get("approved_at"), str) or
                not decision["approved_at"].strip()):
            raise RuleError("Owner approval lacks a scoped and traceable decision")
        exclusions = decision.get("exclusions")
        if not isinstance(exclusions, list) or not {
                "P0_GATE", "ARCHITECTURE", "DETAILED_DESIGN",
                "CODE_IMPLEMENTATION", "GITHUB_PR_MERGE"
                }.issubset(set(exclusions)):
            raise RuleError("Owner P0 approval must exclude unrelated authority")
        if len(approved) != 1 or approved[0].get("id") != doc_id:
            raise RuleError("Owner P0 approval cannot authorize unrelated engineering objects")
        document = approved[0]
        if (document.get("kind") != "context" or
                document.get("state") != "APPROVED" or
                document.get("revision") != 2 or
                document.get("approved_by") != actor or
                document.get("approved_at") != decision["approved_at"] or
                document.get("meta", {}).get("approval_id") != decision["approval_id"] or
                document.get("meta", {}).get("source_review_commit") != source_sha or
                "## 一、产品定义" not in document.get("body", "")):
            raise RuleError("Approved P0 engineering document does not match decision")
        proposals = identity.get("proposals", [])
        if len(baselines) != 1 or len(proposals) != 1:
            raise RuleError("First P0 approval must have exactly one canonical identity")
        base, proposal = baselines[0], proposals[0]
        statement = decision.get("statement")
        if (not isinstance(statement, str) or not statement.strip() or
                statement not in document["body"] or
                archive.get("project", {}).get("summary") != statement or
                base.get("project_id") != project_id or
                base.get("revision") != 1 or
                base.get("proposal_id") != identity_id or
                base.get("statement") != statement or
                base.get("approved_by") != actor or
                base.get("approved_at") != decision["approved_at"] or
                proposal.get("id") != identity_id or
                proposal.get("project_id") != project_id or
                proposal.get("state") != "PUBLISHED" or
                proposal.get("statement") != statement or
                proposal.get("reviewer") != actor or
                proposal.get("reviewed_at") != decision["approved_at"]):
            raise RuleError("Canonical P0 identity differs from explicit owner approval")
        if any(x.get("status") == "APPROVED" for x in archive.get("relations", [])):
            raise RuleError("P0 document approval does not approve engineering relations")
    return {
        "commit": commit, "path": path,
        "content_sha256": hashlib.sha256(raw).hexdigest(),
        "project_id": project_id, "preview": result, "archive": archive,
        "authority": authority,
    }


def hydrate_empty(root, database, project_id="autotest"):
    snapshot = committed_project(root, project_id)
    store = Store(database)
    if store.projects():
        raise RuleError("Git checkout hydration is forbidden for a nonempty workspace; "
                        "use a fresh isolated trial database, never overwrite prior edits")
    restored = restore_to_empty(snapshot["archive"], database)
    return {k: v for k, v in snapshot.items() if k != "archive"} | {
        "restored_project_id": restored["project"]["id"]}
