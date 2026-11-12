"""The distributable manager ZIP contains only generic, verifiable files."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

HERE = Path(__file__).resolve().parents[1]
source = HERE / "portable/export_package.py"
spec = importlib.util.spec_from_file_location("project_control_export", source)
exporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exporter)


class ExportPackageTests(unittest.TestCase):
    def test_zip_is_portable_and_excludes_autotest_specific_state(self):
        manifest = json.loads((HERE / "PORTABLE_MANIFEST.json").read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as tmp:
            zip_file = Path(tmp) / "ProjectControl.zip"
            result = exporter.build_zip(zip_file)
            self.assertEqual(result["status"], "PORTABLE_PACKAGE_EXPORTED")
            self.assertEqual(result["files"], len(manifest["required_files"]))
            self.assertFalse(result["project_specific_data_included"])
            self.assertEqual(hashlib.sha256(zip_file.read_bytes()).hexdigest(), result["sha256"])
            with zipfile.ZipFile(zip_file) as z:
                names = set(z.namelist())
                expected = {"ProjectControl/" + x for x in manifest["required_files"]}
                self.assertEqual(names, expected | {"ProjectControl/PACKAGE_FILES.sha256"})
                proof = z.read("ProjectControl/PACKAGE_FILES.sha256").decode("utf-8")
                for item in manifest["required_files"]:
                    data = z.read("ProjectControl/" + item)
                    self.assertIn(hashlib.sha256(data).hexdigest() + "  " + item, proof)
                self.assertNotIn("ProjectControl/workspace/autotest.json", names)
                self.assertNotIn("ProjectControl/sources/autotest_engineering.json", names)
                self.assertNotIn("ProjectControl/app.js", names)
                self.assertNotIn("ProjectControl/ai/BOOTSTRAP.md", names)
                self.assertIn("ProjectControl/portable/web/app.js", names)
                z.extractall(tmp)
            root = Path(tmp) / "ProjectControl"
            self.assertTrue((root / "START_HERE.md").is_file())
            self.assertFalse((root / ".git").exists())
            for rel in ("portable/project_init.py", "portable/target_resolver.py",
                        "portable/web_server.py"):
                p = subprocess.run([sys.executable, str(root / rel), "--help"],
                                   cwd=tmp, capture_output=True, text=True)
                self.assertEqual(p.returncode, 0, p.stderr)

    def test_never_overwrites_existing_archive(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "ProjectControl.zip"
            path.write_bytes(b"untouched")
            with self.assertRaises(exporter.PackageError):
                exporter.build_zip(path)
            self.assertEqual(path.read_bytes(), b"untouched")

    def test_rejects_manifest_traversal_and_missing_content(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            m = {"schema": "project-control-portable-manifest/1",
                 "required_files": ["../outside"]}
            (root / "PORTABLE_MANIFEST.json").write_text(json.dumps(m), encoding="utf-8")
            with self.assertRaises(exporter.PackageError):
                exporter.safe_paths(root)
            m["required_files"] = ["nonexistent.json"]
            (root / "PORTABLE_MANIFEST.json").write_text(json.dumps(m), encoding="utf-8")
            with self.assertRaises(exporter.PackageError):
                exporter.safe_paths(root)


if __name__ == "__main__":
    unittest.main()
