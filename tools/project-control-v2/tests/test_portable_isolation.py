"""The portable manager bootstrap must work after copying ONLY project-control-v2 folder."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]


class IsolatedPackageTests(unittest.TestCase):
    def test_portable_entry_and_initializers_without_parent_autotest_repo(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "independent-management-folder"
            manifest = json.loads((HERE / "PORTABLE_MANIFEST.json").read_text(encoding="utf-8"))
            for rel in manifest["required_files"]:
                source = HERE / rel
                self.assertTrue(source.is_file(), rel)
                destination = root / rel
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, destination)
            # No ancestor has the AutoTest root docs, source code or any Git metadata.
            self.assertFalse((root.parent / "AI_START_HERE.md").exists())
            self.assertFalse((root.parent / ".git").exists())
            self.assertTrue((root / manifest["entrypoint"]).is_file())
            for name in ("project_init.py", "target_resolver.py"):
                script = root / "portable" / name
                p = subprocess.run([sys.executable, str(script), "--help"],
                                   cwd=tmp, capture_output=True, text=True, encoding="utf-8")
                self.assertEqual(p.returncode, 0, p.stderr)
            start = (root / "START_HERE.md").read_text(encoding="utf-8")
            self.assertIn("目标 GitHub 仓库", start)
            self.assertIn("codex", start.lower())

    def test_manifest_honestly_reports_web_ui_not_generic(self):
        manifest = json.loads((HERE / "PORTABLE_MANIFEST.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["capabilities"]["generic_web_ui"], "NOT_IMPLEMENTED")
        self.assertEqual(manifest["capabilities"]["multi_project_store"], "NOT_IMPLEMENTED")
        audit = (HERE / "portable/COMPLETENESS_AUDIT.md").read_text(encoding="utf-8")
        self.assertIn("server.py", audit)
        self.assertIn("AutoTest", audit)


if __name__ == "__main__":
    unittest.main()
