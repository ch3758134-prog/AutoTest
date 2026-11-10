#!/usr/bin/env python3
"""AutoTest lifecycle pilot: read-only SAEF indexing, change impact, and work-package gate.

Python 3.10+, standard library only. No network, no product-code or SAEF writes.
This is an engineering-review helper, not an approval authority.
"""
from __future__ import annotations

import argparse
from collections import deque
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
from typing import Any

SCHEMA = "autotest-lifecycle/0.1"
KINDS = {"fact", "requirement", "interface", "architecture", "detailed_design", "source", "test", "work_package"}
EDGE_KINDS = {"derives_from", "allocated_to", "defined_by", "implements", "verified_by", "depends_on"}


class ModelError(Exception):
    pass


def read_json(path: Path) -> dict[str, Any]:
    try:
        with path.open("r", encoding="utf-8") as stream:
            data = json.load(stream)
    except (OSError, ValueError) as exc:
        raise ModelError(f"Cannot read JSON {path}: {exc}") from exc
    if not isinstance(data, dict):
        raise ModelError(f"Expected JSON object: {path}")
    return data


def inside(root: Path, relative: str) -> Path:
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute():
        raise ModelError(f"Invalid relative source path: {relative!r}")
    result = (root / relative).resolve()
    if not result.is_relative_to(root.resolve()):
        raise ModelError(f"Path escapes repository: {relative}")
    return result


def content_hash(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class Workspace:
    def __init__(self, root: Path, manifest: dict[str, Any]):
        self.root = root.resolve()
        if manifest.get("schema") != SCHEMA:
            raise ModelError(f"Unsupported model schema: {manifest.get('schema')!r}")
        if not isinstance(manifest.get("nodes"), list) or not isinstance(manifest.get("edges"), list):
            raise ModelError("Manifest must include nodes and edges arrays")
        self.manifest = manifest
        self.nodes: dict[str, dict[str, Any]] = {}
        for node in manifest["nodes"]:
            if not isinstance(node, dict) or not isinstance(node.get("id"), str) or not node["id"]:
                raise ModelError("Each node needs a nonempty string id")
            if node.get("kind") not in KINDS:
                raise ModelError(f"Unknown node kind: {node.get('kind')}")
            if node["id"] in self.nodes:
                raise ModelError(f"Duplicate node: {node['id']}")
            if node.get("path"):
                inside(self.root, node["path"])
            self.nodes[node["id"]] = node
        self.forward = {key: [] for key in self.nodes}
        for edge in manifest["edges"]:
            if edge.get("from") not in self.nodes or edge.get("to") not in self.nodes:
                raise ModelError(f"Dangling edge: {edge}")
            if edge.get("kind") not in EDGE_KINDS:
                raise ModelError(f"Unrecognized relation: {edge.get('kind')}")
            if edge["from"] == edge["to"]:
                raise ModelError("Self-linked relationship")
            if edge.get("certainty") not in {"documented", "review_required"}:
                raise ModelError(f"Missing relation certainty: {edge}")
            self.forward[edge["from"]].append(edge)
        self.work_products = self._control("SAEF/engineering/control/work_products.json")
        self.project_state = self._control("SAEF/engineering/control/project_state.json")

    def _control(self, relative: str) -> dict[str, Any]:
        path = inside(self.root, relative)
        return read_json(path) if path.is_file() else {}

    def source(self, node_id: str) -> dict[str, Any]:
        node = self.nodes[node_id]
        result = {"id": node_id, "kind": node["kind"], "path": node.get("path"),
                  "anchor": node.get("anchor"), "source_state": "UNVERIFIED", "sha256": None,
                  "work_product_state": None, "gate_state": None,
                  "maturity": node.get("maturity"), "notes": []}
        wp = node.get("work_product")
        if wp:
            result["work_product_state"] = self.work_products.get(wp, {}).get("status", "UNKNOWN")
            gate_name = wp.split("-", 1)[0]
            result["gate_state"] = self.project_state.get("gates", {}).get(gate_name, {}).get("status", "UNKNOWN")
        if not node.get("path"):
            result["source_state"] = "REPORTED_ONLY"
            result["notes"].append("No repository artifact; not independently verified")
            return result
        path = inside(self.root, node["path"])
        if not path.is_file():
            result["source_state"] = "MISSING"
            result["notes"].append("Repository file not found")
            return result
        data = path.read_bytes()
        result["sha256"] = content_hash(data)
        anchor = node.get("anchor", "")
        if not isinstance(anchor, str):
            raise ModelError(f"Non-string anchor: {node_id}")
        if anchor and anchor not in data.decode("utf-8", errors="replace"):
            result["source_state"] = "ANCHOR_MISSING"
            result["notes"].append("Expected textual anchor not found; no semantic link is inferred")
            return result
        result["source_state"] = "ANCHORED" if anchor else "FILE_PRESENT"
        if anchor:
            result["notes"].append("Anchor proves text occurrence, NOT implementation conformity or approval")
        return result

    def inspect(self) -> dict[str, Any]:
        return {"schema": SCHEMA, "nodes": [self.source(k) for k in self.nodes],
                "relations": len(self.manifest["edges"]),
                "historical_gate_states": {k: v.get("status") for k, v in self.project_state.get("gates", {}).items()},
                "warning": "Historical approval, requirement maturity, implementation and real-bench acceptance are independent"}

    def impact(self, start: str) -> dict[str, Any]:
        if start not in self.nodes:
            raise ModelError(f"Unknown node: {start}")
        queue = deque([(start, [start], True)])
        # First-found shortest trail, prefer fully documented links for equal distance.
        seen = {start}
        affected = []
        while queue:
            current, path, documented = queue.popleft()
            for edge in sorted(self.forward[current], key=lambda e: e["certainty"] != "documented"):
                dst = edge["to"]
                if dst in seen:
                    continue
                seen.add(dst)
                certain = documented and edge["certainty"] == "documented"
                trail = path + [dst]
                affected.append({"id": dst, "kind": self.nodes[dst]["kind"],
                                 "status": "POTENTIALLY_IMPACTED",
                                 "trace": trail,
                                 "trace_certainty": "DOCUMENTED_PATH" if certain else "REVIEW_REQUIRED_PATH"})
                queue.append((dst, trail, certain))
        return {"changed": start, "affected": affected,
                "notice": "Potential impact is NOT a declaration that requirements or verification are invalid"}

    def readiness(self, package_id: str) -> dict[str, Any]:
        wp = self.nodes.get(package_id)
        if not wp or wp.get("kind") != "work_package":
            raise ModelError(f"Not a work package: {package_id}")
        reasons = []
        if wp.get("status") != "APPROVED":
            reasons.append("Work package has no approved revision")
        if not wp.get("approved_by") or not wp.get("approved_at"):
            reasons.append("Responsible human approval identity/time missing")
        if not wp.get("source_commit") or len(wp.get("source_commit", "")) != 40:
            reasons.append("Exact source commit is not pinned")
        if not wp.get("allowed_paths"):
            reasons.append("Allowed implementation file scope not specified")
        else:
            for path in wp["allowed_paths"]:
                inside(self.root, path)
        if not wp.get("verification"):
            reasons.append("Verification contract missing")
        required = wp.get("required_inputs", [])
        if not required:
            reasons.append("No upstream inputs specified")
        baseline = wp.get("baseline", {})
        for node_id in required:
            if node_id not in self.nodes:
                reasons.append(f"Unknown required input: {node_id}")
                continue
            state = self.source(node_id)
            if state["source_state"] not in {"ANCHORED", "FILE_PRESENT"}:
                reasons.append(f"Source not verified for {node_id}: {state['source_state']}")
            if baseline.get(node_id) != state["sha256"]:
                reasons.append(f"Input revision not pinned/current: {node_id}")
            if self.nodes[node_id].get("kind") in {"requirement", "interface", "architecture", "detailed_design"}:
                if state["work_product_state"] != "ACCEPTED" or state["gate_state"] not in {"PASS", "PASS_WITH_LIMITATIONS"}:
                    reasons.append(f"No SAEF historical approval evidence: {node_id}")
        for change in self.manifest.get("changes", []):
            if change.get("status") not in {"OPEN", "UNDER_REVIEW"}:
                continue
            touched = set(change.get("affected_nodes", []))
            if change.get("origin") in self.nodes:
                touched.add(change["origin"])
                touched.update(x["id"] for x in self.impact(change["origin"])["affected"])
            overlapping = set(required) & touched
            if overlapping:
                reasons.append(f"Open change {change['id']} affects: {', '.join(sorted(overlapping))}")
        return {"package": package_id, "status": "READY_TO_CODE" if not reasons else "NOT_READY",
                "reasons": list(dict.fromkeys(reasons)),
                "required_input_states": {node_id: self.source(node_id) for node_id in required if node_id in self.nodes},
                "note": "READY means reviewed software implementation scope only; no physical output authorization"}

    def capture(self, node_ids: list[str] | None = None) -> dict[str, Any]:
        selected = node_ids if node_ids is not None else list(self.nodes)
        sources = {k: self.source(k) for k in selected}
        try:
            proc = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.root, capture_output=True, text=True, timeout=5, check=False)
            head = proc.stdout.strip() if proc.returncode == 0 else None
        except (OSError, subprocess.TimeoutExpired):
            head = None
        return {"schema": SCHEMA, "captured_utc": datetime.now(timezone.utc).isoformat(),
                "repo_head": head, "sources": sources,
                "warning": "Snapshot proves local byte identity only; does not approve source or certify hardware"}

    def context(self, package_id: str) -> str:
        check = self.readiness(package_id)
        if check["status"] != "READY_TO_CODE":
            raise ModelError("Cannot export coding context: " + "; ".join(check["reasons"]))
        wp = self.nodes[package_id]
        rows = [f"# Approved implementation context: {package_id}", "",
                f"- Design approved by: {wp['approved_by']} at {wp['approved_at']}",
                f"- Source commit: `{wp['source_commit']}`",
                "- This context does not authorize real hardware outputs or programming.",
                "", "## Pinned design inputs"]
        for node_id in wp["required_inputs"]:
            state = check["required_input_states"][node_id]
            rows.append(f"- **{node_id}** — `{state['path']}`; SHA-256 `{state['sha256']}`; "
                        f"gate `{state['gate_state']}`; work product `{state['work_product_state']}`; "
                        f"anchor `{state['anchor'] or '(none)'}`")
        rows.extend(["", "## Allowed implementation files", *[f"- `{p}`" for p in wp["allowed_paths"]],
                     "", "## Verification contract", *[f"- {s}" for s in wp["verification"]], "",
                     "## Required conduct", "- Read the full pinned source files, not only the anchors.",
                     "- If the design conflicts with the real behavior, stop and open a Finding/CR; do not invent new architecture.",
                     "- Preserve source-level history; submit implementation in a candidate PR."])
        return "\n".join(rows) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read-only AutoTest lifecycle trace / impact / readiness helper")
    parser.add_argument("--repo", type=Path, default=Path.cwd(), help="AutoTest repository checkout root")
    parser.add_argument("--model", type=Path, default=Path(__file__).resolve().parent / "data/keysim-trace.json")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("inspect")
    impact = commands.add_parser("impact"); impact.add_argument("node")
    ready = commands.add_parser("readiness"); ready.add_argument("package")
    capture = commands.add_parser("capture"); capture.add_argument("--out", type=Path)
    ctx = commands.add_parser("context"); ctx.add_argument("package"); ctx.add_argument("--out", type=Path)
    args = parser.parse_args(argv)
    try:
        workspace = Workspace(args.repo, read_json(args.model))
        if args.command == "inspect":
            result = workspace.inspect()
        elif args.command == "impact":
            result = workspace.impact(args.node)
        elif args.command == "readiness":
            result = workspace.readiness(args.package)
        elif args.command == "capture":
            result = workspace.capture()
        else:
            result = workspace.context(args.package)
        if args.command in {"capture", "context"} and args.out:
            args.out.parent.mkdir(parents=True, exist_ok=True)
            args.out.write_text(result if isinstance(result, str) else json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            print(f"Wrote {args.out}")
        elif isinstance(result, str):
            print(result)
        else:
            print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except (ModelError, OSError, ValueError) as exc:
        print(f"LIFECYCLE_ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
