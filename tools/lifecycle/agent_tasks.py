#!/usr/bin/env python3
"""Codex/worker task receiver for the local engineering workbench.

This program receives ONLY tasks explicitly dispatched with CODE_READY.
It records the actual worker claim, emits the immutable coding manifest and
design context, then exits. It does not launch an AI model or run code.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import urllib.error
import urllib.parse
import urllib.request


def load_json(base, path, payload=None, token=None):
    url=base.rstrip("/") + path
    data=None if payload is None else json.dumps(payload,ensure_ascii=False).encode("utf-8")
    headers={"Accept":"application/json"}
    if data is not None:
        headers.update({"Content-Type":"application/json","X-Local-Token":token or ""})
    request=urllib.request.Request(url,data=data,headers=headers)
    try:
        with urllib.request.urlopen(request,timeout=15) as response:
            result=json.loads(response.read())
    except urllib.error.HTTPError as error:
        try:
            details=json.loads(error.read()).get("error","")
        except Exception:
            details="HTTP "+str(error.code)
        raise RuntimeError("Work queue refused: "+str(details)) from error
    except (urllib.error.URLError,TimeoutError,OSError,ValueError) as error:
        raise RuntimeError("Work queue unavailable or invalid response") from error
    return result


def main(argv=None):
    parser=argparse.ArgumentParser(description="Receive only an approved, role-matched coding task")
    parser.add_argument("--server",default="http://127.0.0.1:8766")
    parser.add_argument("--project",required=True,help="Project id shown in management workbench")
    parser.add_argument("--role",required=True,help="Authorized software role, e.g. DEVICE")
    parser.add_argument("--worker",required=True,help="Actual Codex/agent session identity")
    parser.add_argument("--list",action="store_true",help="Show ready tasks; do not claim")
    parser.add_argument("--output",type=Path,help="Write received context Markdown to a local file")
    args=parser.parse_args(argv)
    parsed=urllib.parse.urlsplit(args.server)
    if parsed.scheme!="http" or parsed.hostname not in ("127.0.0.1","localhost"):
        parser.error("--server must point to the local workbench")
    role=args.role.strip().upper()
    query=urllib.parse.urlencode({"project":args.project,"role":role})
    try:
        candidates=load_json(args.server,"/api/studio/execution/queue?"+query)
        if args.list:
            print(json.dumps(candidates,ensure_ascii=False,indent=2))
            return 0
        if not candidates:
            print("No dispatched CODE_READY tasks for role "+role,file=sys.stderr)
            return 2
        boot=load_json(args.server,"/api/studio/bootstrap")
        value=load_json(args.server,"/api/studio/execution/claim",{
            "object_id":candidates[0]["object_id"],
            "agent_role":role,"worker":args.worker
        },token=boot["csrf"])
        task=value["manifest"]
        if args.output:
            args.output.parent.mkdir(parents=True,exist_ok=True)
            args.output.write_text(value["markdown"],encoding="utf-8")
            print("Coding task claimed and written to "+str(args.output))
        else:
            print(value["markdown"])
        print("\nTASK MANIFEST:",json.dumps(task,ensure_ascii=False,sort_keys=True),file=sys.stderr)
        print("TASK_CLAIMED="+task["work_package_id"],file=sys.stderr)
        return 0
    except RuntimeError as error:
        print(str(error),file=sys.stderr)
        return 1


if __name__=="__main__":
    raise SystemExit(main())
