"""SAEF archive adapter must not silently turn historical records into approved designs."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from studio_store import Store
from studio_import import preview, import_saef


class ArchiveImportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        base = self.root/"SAEF"/"engineering"
        (base/"control").mkdir(parents=True)
        (base/"work-products"/"G1"/"G1-03").mkdir(parents=True)
        path = base/"work-products"/"G1"/"G1-03"/"index.md"
        path.write_text("# System demands\n\nActual source details.\n",encoding="utf-8")
        sha = hashlib.sha256(path.read_bytes()).hexdigest()
        (base/"control"/"work_products.json").write_text(json.dumps({
            "G1-03":{"path":"engineering/work-products/G1/G1-03/index.md",
                     "status":"ACCEPTED","accepted_sha256":sha},
            "G1-99":{"path":"../unsafe","status":"ACCEPTED"}
        }),encoding="utf-8")
        (base/"control"/"project_state.json").write_text(json.dumps({
            "gates":{"G1":{"status":"PASS_WITH_LIMITATIONS"}}}),encoding="utf-8")
        self.store=Store(self.root/"local.sqlite3")
        self.project=self.store.create_project({"name":"自有项目"})["project"]["id"]

    def test_safe_import_and_no_approval_inheritance(self):
        report=preview(self.root,self.store,self.project)
        self.assertEqual(report["available"],1)
        self.assertEqual(report["source_drift"],0)
        result=import_saef(self.root,self.store,self.project)
        self.assertEqual(result["imported"],1)
        obj=self.store.project(self.project)["objects"][0]
        self.assertEqual(obj["kind"],"reference")
        self.assertEqual(obj["state"],"DRAFT")
        self.assertEqual(obj["meta"]["registered_state"],"ACCEPTED")
        self.assertEqual(obj["meta"]["historic_gate"],"PASS_WITH_LIMITATIONS")
        self.assertEqual(obj["meta"]["content_match"],"MATCH")
        self.assertIn("Actual source details",obj["body"])

    def test_reimport_is_idempotent(self):
        import_saef(self.root,self.store,self.project)
        again=import_saef(self.root,self.store,self.project)
        self.assertEqual(again["imported"],0)
        self.assertEqual(again["skipped"],1)
        self.assertEqual(self.store.project(self.project)["counts"]["reference"],1)

    def test_source_drift_is_a_warning_not_automatic_rejection(self):
        base=self.root/"SAEF"/"engineering"/"work-products"/"G1"/"G1-03"/"index.md"
        base.write_text("# New contents\n",encoding="utf-8")
        preview_result=preview(self.root,self.store,self.project)
        self.assertEqual(preview_result["source_drift"],1)
        import_saef(self.root,self.store,self.project)
        obj=self.store.project(self.project)["objects"][0]
        self.assertEqual(obj["meta"]["content_match"],"DRIFT")
        self.assertEqual(obj["state"],"DRAFT")


if __name__=="__main__":
    unittest.main()
