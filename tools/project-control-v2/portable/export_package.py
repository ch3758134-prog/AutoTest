#!/usr/bin/env python3
"""Export only the self-contained Project Control portable manager.

Avoids copying AutoTest demonstration state, old UI, .git, caches and local logs.
Never overwrites any existing destination. No network or Git writes.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE_MANIFEST = ROOT / "PORTABLE_MANIFEST.json"
CHECKSUM_NAME = "PACKAGE_FILES.sha256"


class PackageError(ValueError):
    pass


def safe_paths(root=ROOT):
    m = json.loads((root / "PORTABLE_MANIFEST.json").read_text(encoding="utf-8"))
    if m.get("schema") != "project-control-portable-manifest/1":
        raise PackageError("invalid portable manifest")
    files = m.get("required_files")
    if not isinstance(files, list) or not files or len(files) > 200 or len(files) != len(set(files)):
        raise PackageError("manifest has missing or duplicate file entries")
    paths = []
    for name in files:
        if not isinstance(name, str) or "\\" in name:
            raise PackageError("nonportable manifest path")
        p = PurePosixPath(name)
        if p.is_absolute() or any(part in ("", ".", "..") for part in p.parts):
            raise PackageError("unsafe manifest path: " + name)
        file = root.joinpath(*p.parts)
        if file.is_symlink() or not file.is_file() or not file.resolve().is_relative_to(root.resolve()):
            raise PackageError("missing or unsafe required file: " + name)
        if any(part in (".git", "local-logs", "__pycache__", "sources", "workspace")
               for part in p.parts):
            raise PackageError("project-specific or local data cannot be exported: " + name)
        paths.append((name, file))
    return paths


def build_zip(destination, root=ROOT):
    destination = Path(destination).absolute()
    if destination.exists() or destination.is_symlink():
        raise PackageError("output already exists; refusing overwrite")
    if not destination.parent.is_dir():
        raise PackageError("output parent must already exist")
    entries = safe_paths(root)
    checksums = {}
    # Zip is a convenient single artifact to upload to ChatGPT; contents are
    # deterministic except the original source files changing.
    try:
        with destination.open("xb") as stream:
            with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED,
                                 compresslevel=8) as archive:
                for name, source in entries:
                    data = source.read_bytes()
                    checksums[name] = hashlib.sha256(data).hexdigest()
                    zi = zipfile.ZipInfo("ProjectControl/" + name, (2026, 1, 1, 0, 0, 0))
                    zi.compress_type = zipfile.ZIP_DEFLATED
                    zi.external_attr = 0o644 << 16
                    archive.writestr(zi, data)
                proof = "".join(checksums[name] + "  " + name + "\n"
                                for name in sorted(checksums))
                zi = zipfile.ZipInfo("ProjectControl/" + CHECKSUM_NAME, (2026, 1, 1, 0, 0, 0))
                zi.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(zi, proof.encode("utf-8"))
    except BaseException:
        # Only remove a file just opened by this invocation; never remove an existing one.
        destination.unlink(missing_ok=True)
        raise
    return {"status": "PORTABLE_PACKAGE_EXPORTED", "archive": str(destination),
            "files": len(entries), "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
            "project_specific_data_included": False,
            "note": "This is the generic management package, not project initialization or approval."}


def main(argv=None):
    parser = argparse.ArgumentParser(description="Export self-contained Project Control manager ZIP")
    parser.add_argument("--out", required=True, help="New ZIP path that does not exist")
    args = parser.parse_args(argv)
    try:
        value = build_zip(args.out)
        print(json.dumps(value, ensure_ascii=False, indent=2))
        return 0
    except (PackageError, OSError, ValueError) as exc:
        print(json.dumps({"status": "BLOCKED", "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
