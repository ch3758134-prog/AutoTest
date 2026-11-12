"""Windows-compatible UTF-8 JSON output with a forced legacy stdout encoding."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
INIT = HERE / "portable/project_init.py"
EXPORT = HERE / "portable/export_package.py"


def git(root, *args):
    p = subprocess.run(["git", "-C", str(root), *args], capture_output=True,
                       text=True, encoding="utf-8", errors="replace", timeout=20)
    if p.returncode:
        raise AssertionError(p.stderr)


class Utf8ConsoleTests(unittest.TestCase):
    def test_init_preview_emits_chinese_json_when_parent_requests_cp1252(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "research"
            root.mkdir()
            git(root, "init", "-b", "main")
            git(root, "remote", "add", "origin",
                "https://github.com/owner/research.git")
            env = dict(os.environ)
            env["PYTHONIOENCODING"] = "cp1252"
            cmd = [
                sys.executable, str(INIT), "preview", "--target", str(root),
                "--repo-url", "https://github.com/owner/research.git",
                "--mode", "new", "--project-id", "research",
                "--project-name", "中文研究项目",
            ]
            p = subprocess.run(cmd, capture_output=True, env=env,
                               timeout=30)
            self.assertEqual(p.returncode, 0,
                             p.stderr.decode("utf-8", errors="replace"))
            data = json.loads(p.stdout.decode("utf-8"))
            self.assertEqual(
                data["candidate_files"][".project-control/project.json"].count("中文研究项目"),
                1,
            )

    def test_archive_export_emits_utf8_chinese_filename_in_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            filename = Path(tmp) / "管理系统.zip"
            env = dict(os.environ)
            env["PYTHONIOENCODING"] = "cp1252"
            p = subprocess.run(
                [sys.executable, str(EXPORT), "--out", str(filename)],
                capture_output=True, env=env, timeout=45)
            self.assertEqual(p.returncode, 0,
                             p.stderr.decode("utf-8", errors="replace"))
            status = json.loads(p.stdout.decode("utf-8"))
            self.assertEqual(status["status"], "PORTABLE_PACKAGE_EXPORTED")
            self.assertEqual(status["archive"], str(filename))
            self.assertTrue(filename.is_file())


if __name__ == "__main__":
    unittest.main()
