"""M1 local tests. All fixtures are generated; no AutoTest product code is executed."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lifecycle import ModelError, Workspace, main, content_hash  # noqa: E402


class LifecycleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for folder in ("SAEF/engineering/control", "SAEF/engineering/work-products/G1/G1-03", "SAEF/engineering/work-products/G3/G3-01", "src", "tests"):
            (self.root / folder).mkdir(parents=True, exist_ok=True)
        self.req = "SAEF/engineering/work-products/G1/G1-03/index.md"
        self.design = "SAEF/engineering/work-products/G3/G3-01/index.md"
        (self.root / self.req).write_text("AT-SYS-011: safely emulate keys (revision 1)", encoding="utf-8")
        (self.root / self.design).write_text("Design K1: use KeypadCommandCodec", encoding="utf-8")
        (self.root / "src/keypad.cs").write_text("class KeypadCommandCodec {}", encoding="utf-8")
        (self.root / "tests/keypad.cs").write_text("Verify_A2_T04", encoding="utf-8")
        (self.root / "SAEF/engineering/control/work_products.json").write_text(json.dumps({"G1-03": {"status": "ACCEPTED"}, "G3-01": {"status": "ACCEPTED"}}), encoding="utf-8")
        (self.root / "SAEF/engineering/control/project_state.json").write_text(json.dumps({"gates": {"G1": {"status": "PASS_WITH_LIMITATIONS"}, "G3": {"status": "PASS_WITH_LIMITATIONS"}}}), encoding="utf-8")
        self.model = {"schema": "autotest-lifecycle/0.1", "nodes": [
            {"id": "R", "kind": "requirement", "path": self.req, "anchor": "AT-SYS-011", "work_product": "G1-03", "maturity": "CONFIRMED"},
            {"id": "D", "kind": "detailed_design", "path": self.design, "anchor": "KeypadCommandCodec", "work_product": "G3-01"},
            {"id": "SRC", "kind": "source", "path": "src/keypad.cs", "anchor": "KeypadCommandCodec"},
            {"id": "TEST", "kind": "test", "path": "tests/keypad.cs", "anchor": "Verify_A2_T04"},
            {"id": "WP", "kind": "work_package", "status": "DRAFT", "approved_by": "", "approved_at": "", "source_commit": "a" * 40,
             "required_inputs": ["R", "D"], "baseline": {}, "allowed_paths": ["src/keypad.cs"], "verification": ["masked CAN encoding"]}
        ], "edges": [
            {"from": "R", "to": "D", "kind": "defined_by", "certainty": "documented"},
            {"from": "D", "to": "SRC", "kind": "implements", "certainty": "documented"},
            {"from": "SRC", "to": "TEST", "kind": "verified_by", "certainty": "documented"},
            {"from": "D", "to": "WP", "kind": "depends_on", "certainty": "review_required"}
        ], "changes": []}

    def workspace(self):
        return Workspace(self.root, self.model)

    def approve(self):
        wp = self.model["nodes"][-1]
        wp["status"] = "APPROVED"
        wp["approved_by"] = "Product owner"
        wp["approved_at"] = "2026-11-09T20:00:00Z"
        wp["baseline"] = {k: self.workspace().source(k)["sha256"] for k in wp["required_inputs"]}

    def test_historical_gate_and_maturity_stay_separate(self):
        source = self.workspace().source("R")
        self.assertEqual(source["maturity"], "CONFIRMED")
        self.assertEqual(source["gate_state"], "PASS_WITH_LIMITATIONS")
        self.assertEqual(source["work_product_state"], "ACCEPTED")
        self.assertEqual(source["source_state"], "ANCHORED")

    def test_impact_upstream_to_all_downstream(self):
        impact = self.workspace().impact("R")["affected"]
        self.assertEqual({x["id"] for x in impact}, {"D", "SRC", "TEST", "WP"})
        self.assertEqual(next(x for x in impact if x["id"] == "WP")["trace_certainty"], "REVIEW_REQUIRED_PATH")

    def test_unknown_root_fails(self):
        with self.assertRaises(ModelError):
            self.workspace().impact("X")

    def test_draft_package_not_ready(self):
        result = self.workspace().readiness("WP")
        self.assertEqual(result["status"], "NOT_READY")
        self.assertTrue(any("approved" in x for x in result["reasons"]))
        with self.assertRaises(ModelError):
            self.workspace().context("WP")

    def test_positive_ready_and_context(self):
        self.approve()
        w = self.workspace()
        self.assertEqual(w.readiness("WP")["status"], "READY_TO_CODE")
        exported = w.context("WP")
        self.assertIn(self.req, exported)
        self.assertIn("Source commit", exported)
        self.assertIn(content_hash((self.root / self.req).read_bytes()), exported)
        self.assertIn("not authorize real hardware", exported)

    def test_modified_input_invalidates_approval(self):
        self.approve()
        (self.root / self.req).write_text("AT-SYS-011 revision 2", encoding="utf-8")
        report = self.workspace().readiness("WP")
        self.assertEqual(report["status"], "NOT_READY")
        self.assertIn("Input revision not pinned/current: R", report["reasons"])

    def test_open_change_invalidates_only_impacted_package(self):
        self.approve()
        self.model["changes"].append({"id": "CR-X", "status": "OPEN", "origin": "R"})
        result = self.workspace().readiness("WP")
        self.assertEqual(result["status"], "NOT_READY")
        self.assertTrue(any("CR-X" in reason for reason in result["reasons"]))
        self.model["changes"][0]["status"] = "CLOSED"
        self.assertEqual(self.workspace().readiness("WP")["status"], "READY_TO_CODE")

    def test_missing_anchor_blocks(self):
        self.approve()
        (self.root / self.design).write_text("new text", encoding="utf-8")
        self.assertEqual(self.workspace().source("D")["source_state"], "ANCHOR_MISSING")
        self.assertEqual(self.workspace().readiness("WP")["status"], "NOT_READY")

    def test_missing_source_and_report_only_are_not_verified(self):
        (self.root / self.req).unlink()
        self.assertEqual(self.workspace().source("R")["source_state"], "MISSING")
        self.model["nodes"].append({"id": "FIELD", "kind": "fact", "description": "reported by engineer"})
        self.assertEqual(self.workspace().source("FIELD")["source_state"], "REPORTED_ONLY")

    def test_refuses_path_traversal(self):
        self.model["nodes"][0]["path"] = "../../secrets.txt"
        with self.assertRaises(ModelError):
            self.workspace()

    def test_refuses_broken_relations(self):
        self.model["edges"][0]["to"] = "NONEXISTENT"
        with self.assertRaises(ModelError):
            self.workspace()

    def test_independent_implementation_and_verification(self):
        w = self.workspace()
        src, test = w.source("SRC"), w.source("TEST")
        self.assertEqual(src["source_state"], "ANCHORED")
        self.assertIsNone(src["work_product_state"])
        self.assertEqual(test["source_state"], "ANCHORED")
        self.assertFalse(any(x.get("physical_state") == "PASS" for x in w.inspect()["nodes"]))

    def test_capture_is_not_approval(self):
        captured = self.workspace().capture(["R"])
        self.assertEqual(captured["sources"]["R"]["sha256"], hashlib.sha256((self.root / self.req).read_bytes()).hexdigest())
        self.assertIn("does not approve", captured["warning"])

    def test_cli_not_ready_exit_is_reported_not_false_success(self):
        model_path = self.root / "manifest.json"
        model_path.write_text(json.dumps(self.model), encoding="utf-8")
        self.assertEqual(main(["--repo", str(self.root), "--model", str(model_path), "readiness", "WP"]), 0)
        self.assertEqual(main(["--repo", str(self.root), "--model", str(model_path), "context", "WP"]), 2)


if __name__ == "__main__":
    unittest.main()
