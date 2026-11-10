#!/usr/bin/env python3
"""Independent Git diff and approved coding scope verifier.

Must run as a required CI check using a trusted contract from the approved
base revision. This checker does NOT itself establish who approved the
contract; supplying a PR-editable JSON file is not a trusted authorization.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

SHA=re.compile(r"^[0-9a-fA-F]{40}$")
PROTECTED=(
    ".github/workflows/", "docs/delivery/",
    "docs/engineering-control/agent-work-protocol.json",
    "docs/engineering-control/README.md",
    "tools/lifecycle/",
    "AGENTS.md",
)


class ContractViolation(ValueError):
    pass


def git(repository,*args):
    try:
        result=subprocess.run(["git","-C",str(repository),*args],capture_output=True,
                              timeout=20,check=True)
    except (subprocess.CalledProcessError,OSError,subprocess.TimeoutExpired) as exc:
        raise ContractViolation("Git verification command failed: "+args[0]) from exc
    return result.stdout


def validate_scope(paths):
    if not isinstance(paths,list) or not paths or len(paths)>100:
        raise ContractViolation("Allowed paths must be a non-empty list")
    allowed=[]
    for entry in paths:
        if not isinstance(entry,str) or not entry or "\\" in entry or "\x00" in entry:
            raise ContractViolation("Unsafe allowed path")
        value=entry.strip()
        chunks=value.rstrip("/").split("/")
        if (not value or value.startswith("/") or
            any(x in ("", ".", "..") for x in chunks) or
            any(x in value for x in ("*", "?", "["))):
            raise ContractViolation("Wildcard, traversal and root grants are forbidden")
        if entry != value:
            raise ContractViolation("Whitespace in allowed path")
        if any(value==item.rstrip("/") or value.startswith(item)
               for item in PROTECTED):
            raise ContractViolation("Governance files cannot be delegated to coding AI")
        allowed.append(value)
    return allowed


def allowed_file(file, scope):
    return any(file==entry or (entry.endswith("/") and file.startswith(entry))
               for entry in scope)


def verify(repository, manifest, expected_head):
    root=Path(repository).expanduser().resolve()
    if not root.is_dir():
        raise ContractViolation("Git repository not found")
    if not isinstance(manifest,dict) or manifest.get("schema")!="engineering-coding-task/1":
        raise ContractViolation("Not an engineering coding task manifest")
    if manifest.get("agent_role") in ("",None) or not isinstance(manifest.get("agent_role"),str):
        raise ContractViolation("Missing executor role")
    design=manifest.get("design_manifest")
    if not isinstance(design,dict) or design.get("execution_role")!="CODER":
        raise ContractViolation("Contract has no coding-only execution role")
    base=manifest.get("source_sha")
    if not isinstance(base,str) or not SHA.fullmatch(base):
        raise ContractViolation("Invalid pinned baseline SHA")
    if design.get("source_sha")!=base:
        raise ContractViolation("Manifest and detailed-design code baseline differ")
    if not isinstance(expected_head,str) or not SHA.fullmatch(expected_head):
        raise ContractViolation("Expected PR HEAD must be the complete SHA")
    scope=validate_scope(design.get("file_scope"))
    actual_head=git(root,"rev-parse","HEAD").decode().strip()
    if actual_head.lower()!=expected_head.lower():
        raise ContractViolation("Checked-out HEAD differs from exact PR HEAD")
    actual_base=git(root,"rev-parse",base+"^{commit}").decode().strip()
    if actual_base.lower()!=base.lower():
        raise ContractViolation("Pinned baseline does not resolve to exact commit")
    try:
        subprocess.run(["git","-C",str(root),"merge-base","--is-ancestor",base,expected_head],
                       check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=20)
    except (subprocess.CalledProcessError,OSError,subprocess.TimeoutExpired) as exc:
        raise ContractViolation("Pinned base is not an ancestor of reviewed HEAD") from exc
    raw=git(root,"diff","--name-only","--no-renames","-z",base,expected_head)
    changed=[x.decode("utf-8","surrogateescape") for x in raw.split(b"\0") if x]
    if not changed:
        raise ContractViolation("Candidate has no source changes to verify")
    forbidden=[path for path in changed if any(
        path==p or (p.endswith("/") and path.startswith(p)) for p in PROTECTED)]
    if forbidden:
        raise ContractViolation("Coding changes protected governance path(s): "+", ".join(forbidden))
    outside=[path for path in changed if not allowed_file(path,scope)]
    if outside:
        raise ContractViolation("Changes outside approved coding paths: "+", ".join(outside))
    return {"result":"SCOPE_PASS","source_sha":base,"head_sha":expected_head,
            "changed_files":changed,"allowed_paths":scope,
            "warning":"Scope check is not software quality, CI, hardware or product acceptance."}


def main(argv=None):
    parser=argparse.ArgumentParser(description="Verify exact Git coding scope against task manifest")
    parser.add_argument("--repo",type=Path,default=Path("."))
    parser.add_argument("--manifest",required=True,type=Path)
    parser.add_argument("--head",required=True,help="PR HEAD exact 40-digit git SHA")
    args=parser.parse_args(argv)
    try:
        result=verify(args.repo,json.loads(args.manifest.read_text(encoding="utf-8")),
                      args.head)
        print(json.dumps(result,ensure_ascii=False,indent=2))
        return 0
    except (ContractViolation,ValueError,OSError) as error:
        print("SCOPE_FAIL: "+str(error),file=sys.stderr)
        return 2

if __name__=="__main__":
    raise SystemExit(main())
