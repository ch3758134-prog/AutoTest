#!/usr/bin/env python3
"""SHA-locked, reversible retirement for obsolete AutoTest branches.

Usage: python tools/maintenance/retire_branches.py prepare|retire|verify MANIFEST
This never deletes a branch without first creating & verifying an immutable tag
pointing to exactly its original commit, and requiring a passing bundle upload.
"""
import json
import re
import subprocess
import sys
from pathlib import Path


def git(*args):
    p = subprocess.run(["git", *args], check=False, text=True,
                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if p.returncode:
        raise RuntimeError(f"git {' '.join(args)} exited {p.returncode}: {p.stdout[-3000:]}")
    return p.stdout.strip()


def remote_refs(ref_type):
    values = {}
    for line in git("ls-remote", f"--{ref_type}", "origin").splitlines():
        if not line:
            continue
        sha, ref = line.split("\t", 1)
        prefix = f"refs/{ref_type}/"
        if ref.startswith(prefix):
            values[ref[len(prefix):]] = sha.lower()
    return values


def read_manifest(path):
    obj = json.loads(Path(path).read_text(encoding="utf-8"))
    if obj.get("repository") != "ch3758134-prog/AutoTest":
        raise ValueError("Unexpected repository")
    rows = obj.get("retirements")
    if not isinstance(rows, list) or len(rows) != 21:
        raise ValueError("Unexpected retirement count; review the manifest first")
    forbidden = {
        "main", "maintenance/archive-retire-legacy-20261109",
        "recovery/product-led-baseline-20261109",
    }
    names = []
    for row in rows:
        name, sha, tag = row["name"], row["sha"].lower(), row["archive_tag"]
        if (name in forbidden or
            not re.fullmatch(r"[A-Za-z0-9._/-]+", name) or
            ".." in name or "//" in name or name.startswith("/") or
            not re.fullmatch(r"[0-9a-f]{40}", sha) or
            tag != "archive/20261109/" + name):
            raise ValueError(f"Invalid or protected branch/tag: {name}")
        names.append(name)
    if len(names) != len(set(names)):
        raise ValueError("Duplicate retirement branch names")
    if not re.fullmatch(r"[0-9a-f]{40}", obj["verified_main_sha"]):
        raise ValueError("Invalid main SHA")
    return obj, rows


def ensure_main(obj, heads):
    if heads.get("main") != obj["verified_main_sha"]:
        raise RuntimeError("Main moved since verification. Do not retire anything.")


def prepare(obj, rows):
    heads = remote_refs("heads")
    ensure_main(obj, heads)
    for row in rows:
        if heads.get(row["name"]) != row["sha"]:
            raise RuntimeError(f"HEAD changed: {row['name']} expected={row['sha']} actual={heads.get(row['name'])}")

    git("fetch", "origin", "+refs/heads/*:refs/remotes/origin/*", "--no-tags")
    for row in rows:
        actual = git("rev-parse", "refs/remotes/origin/" + row["name"]).lower()
        if actual != row["sha"]:
            raise RuntimeError(f"Remote-tracking mismatch: {row['name']}")

    tags = remote_refs("tags")
    for row in rows:
        tag, sha = row["archive_tag"], row["sha"]
        if tag in tags and tags[tag] != sha:
            raise RuntimeError(f"Existing archive tag points elsewhere: {tag}")
        present = subprocess.run(["git", "show-ref", "--verify", "--quiet", "refs/tags/" + tag])
        if present.returncode == 0:
            if git("rev-parse", "refs/tags/" + tag).lower() != sha:
                raise RuntimeError(f"Local tag collision: {tag}")
        else:
            git("tag", tag, sha)

    new_tags = [f"refs/tags/{x['archive_tag']}:refs/tags/{x['archive_tag']}"
                for x in rows if x["archive_tag"] not in tags]
    if new_tags:
        git("push", "origin", *new_tags)
    tags = remote_refs("tags")
    for row in rows:
        if tags.get(row["archive_tag"]) != row["sha"]:
            raise RuntimeError(f"Archive tag verification failed: {row['archive_tag']}")

    output = Path("maintenance-output")
    output.mkdir(exist_ok=True)
    bundle = output / "AutoTest-before-branch-retirement-20261109.bundle"
    git("bundle", "create", str(bundle), "--all")
    git("bundle", "verify", str(bundle))
    print(f"ARCHIVE_READY tags={len(rows)} git_bundle={bundle} bytes={bundle.stat().st_size}")


def retire(obj, rows):
    heads = remote_refs("heads")
    ensure_main(obj, heads)
    tags = remote_refs("tags")
    for row in rows:
        if tags.get(row["archive_tag"]) != row["sha"]:
            raise RuntimeError(f"Refusing to delete unarchived branch: {row['name']}")
        if heads.get(row["name"]) != row["sha"]:
            raise RuntimeError(f"Refusing to delete changed branch: {row['name']}")
    for row in rows:
        name, sha = row["name"], row["sha"]
        git("push", "--force-with-lease=refs/heads/" + name + ":" + sha,
            "origin", ":refs/heads/" + name)
        print(f"RETIRED {name} @ {sha} -> tag {row['archive_tag']}")


def verify(obj, rows):
    heads = remote_refs("heads")
    ensure_main(obj, heads)
    tags = remote_refs("tags")
    failures = []
    for row in rows:
        if row["name"] in heads:
            failures.append("BRANCH STILL PRESENT: " + row["name"])
        if tags.get(row["archive_tag"]) != row["sha"]:
            failures.append("TAG DOES NOT MATCH: " + row["archive_tag"])
    if failures:
        raise RuntimeError("; ".join(failures))
    print(f"BRANCH_RETIREMENT_PASS removed={len(rows)} archive_tags_verified={len(rows)}")


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] not in ("prepare", "retire", "verify"):
        raise SystemExit("Usage: retire_branches.py prepare|retire|verify MANIFEST")
    document, items = read_manifest(sys.argv[2])
    {"prepare": prepare, "retire": retire, "verify": verify}[sys.argv[1]](document, items)
