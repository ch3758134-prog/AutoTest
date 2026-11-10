#!/usr/bin/env python3
"""Restricted project-management AI handoff.

Management AI may READ the project brief and SUBMIT proposals. It cannot
approve design, dispatch coders, change rules, modify Git, or claim that
a proposal has been applied.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, urlopen


def request(server, route, payload=None, manager_token=None):
    body=None if payload is None else json.dumps(payload,ensure_ascii=False).encode("utf-8")
    headers={"Accept":"application/json"}
    if body is not None:
        headers["Content-Type"]="application/json"
        if manager_token:
            headers["X-Manager-Token"]=manager_token
    req=Request(server.rstrip("/") + route, data=body, headers=headers)
    try:
        with urlopen(req,timeout=15) as response:
            return json.loads(response.read())
    except HTTPError as error:
        try:
            detail=json.loads(error.read()).get("error","HTTP "+str(error.code))
        except (ValueError,TypeError):
            detail="HTTP "+str(error.code)
        raise RuntimeError("Engineering control refused request: "+str(detail)) from error
    except (URLError,TimeoutError,OSError,ValueError) as error:
        raise RuntimeError("Local engineering control service unavailable") from error


def main(argv=None):
    parser=argparse.ArgumentParser(description="Read engineering facts and submit non-binding manager proposals")
    parser.add_argument("--server", default="http://127.0.0.1:8766")
    parser.add_argument("--project",required=True,help="Existing project UUID from UI")
    operation=parser.add_mutually_exclusive_group(required=True)
    operation.add_argument("--brief",action="store_true",help="Read structured project status (no write permission)")
    operation.add_argument("--propose",type=Path,help="Submit a proposal JSON (does not change product design)")
    parser.add_argument("--output",type=Path,help="Save brief or server result to an explicit file")
    parser.add_argument("--manager-token-file",type=Path,
                        help="Locally provisioned manager-only credential; cannot approve or dispatch")
    args=parser.parse_args(argv)
    u=urlsplit(args.server)
    if u.scheme!="http" or u.hostname not in ("127.0.0.1","localhost") or u.username or u.password:
        parser.error("Only localhost HTTP is permitted")
    try:
        if args.brief:
            data=request(args.server,"/api/studio/management/context?"+urlencode({"project":args.project}))
        else:
            if not args.manager_token_file:
                parser.error("Submitting requires --manager-token-file (not the browser token)")
            token=args.manager_token_file.expanduser().read_text(encoding="utf-8").strip()
            if len(token)<32:
                parser.error("Management token is not valid")
            payload=json.loads(args.propose.read_text(encoding="utf-8"))
            if not isinstance(payload,dict):
                raise RuntimeError("Proposal must be a JSON object")
            if payload.get("project_id")!=args.project:
                raise RuntimeError("Proposal project_id does not match --project")
            data=request(args.server,"/api/studio/management/propose",payload,token)
        text=json.dumps(data,ensure_ascii=False,indent=2)+"\n"
        if args.output:
            args.output.parent.mkdir(parents=True,exist_ok=True)
            args.output.write_text(text,encoding="utf-8")
            print("Wrote "+str(args.output))
        else:
            print(text)
        return 0
    except (RuntimeError, OSError, ValueError) as error:
        print(str(error),file=sys.stderr)
        return 1

if __name__=="__main__":
    raise SystemExit(main())
