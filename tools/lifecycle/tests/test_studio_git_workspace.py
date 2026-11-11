"""Real Git engineering snapshot -> existing Store integration tests."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from studio_git_workspace import committed_project, hydrate_empty
from studio_store import Store, RuleError


class GitHydrationTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.git("init", "-q")
        self.git("config", "user.email", "test@example.invalid")
        self.git("config", "user.name", "Test")
        src = Store(self.root / "source.sqlite")
        self.project_id = src.create_project({"name": "Fixture"})["project"]["id"]
        src.create_object({"project_id": self.project_id, "kind": "requirement",
                           "title": "CAN1 RX verification", "body": "Evidence must be versioned",
                           "meta": {"source": "test://case"}})
        self.archive = src.export_project(self.project_id)
        self.path = "docs/engineering-control/projects/" + self.project_id + "/snapshot.json"
        self.publish(self.archive)

    def git(self, *args):
        return subprocess.run(["git", "-C", str(self.repo), *args], check=True,
                              capture_output=True, text=True).stdout.strip()

    def publish(self, archive):
        path = self.repo / self.path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({
            "schema": "engineering-project-archive/1",
            "source_project_id": self.project_id,
            "authority": "AI_CANDIDATE_NOT_APPROVED",
            "engineering": archive
        }), encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-qm", "candidate engineering snapshot")

    def test_hydration_reuses_real_project_objects_not_another_reporting_schema(self):
        dest = self.root / "trial.sqlite"
        result = hydrate_empty(self.repo, dest, self.project_id)
        self.assertEqual(result["commit"], self.git("rev-parse", "HEAD"))
        data = Store(dest).project(self.project_id)
        self.assertEqual(data["project"]["name"], "Fixture")
        self.assertEqual(len(data["objects"]), 1)
        self.assertEqual(data["objects"][0]["state"], "DRAFT")
        self.assertEqual(data["objects"][0]["meta"]["source"], "test://case")

    def test_existing_workspace_is_not_overwritten_even_same_project(self):
        dest = self.root / "trial.sqlite"
        hydrate_empty(self.repo, dest, self.project_id)
        with self.assertRaisesRegex(RuleError, "nonempty workspace"):
            hydrate_empty(self.repo, dest, self.project_id)
        self.assertEqual(len(Store(dest).project(self.project_id)["objects"]), 1)

    def test_dirty_uncommitted_edits_do_not_change_git_snapshot(self):
        target = self.repo / self.path
        target.write_text('{"forged":"change"}', encoding="utf-8")
        result = committed_project(self.repo, self.project_id)
        self.assertEqual(result["archive"]["project"]["name"], "Fixture")

    def test_forged_approval_is_rejected(self):
        bad = json.loads(json.dumps(self.archive))
        bad["objects"][0]["state"] = "APPROVED"
        bad["revisions"][0]["snapshot"] = json.dumps(bad["objects"][0])
        self.publish(bad)
        with self.assertRaisesRegex(RuleError, "may not fabricate"):
            hydrate_empty(self.repo, self.root / "not_approved.sqlite", self.project_id)

    def test_bad_project_id_cannot_escape_git_path(self):
        with self.assertRaisesRegex(RuleError, "Invalid Git"):
            committed_project(self.repo, "../../private")

    def test_owner_approval_marker_without_record_is_rejected(self):
        target = self.repo / self.path
        record = json.loads(target.read_text(encoding="utf-8"))
        record["authority"] = "OWNER_APPROVED_P0_DOCUMENT"
        target.write_text(json.dumps(record), encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-qm", "fake owner authority")
        with self.assertRaisesRegex(RuleError, "explicit decision record"):
            hydrate_empty(self.repo, self.root / "forged_owner.sqlite", self.project_id)

    def test_repository_approved_p0_is_restorable_without_approving_other_objects(self):
        root = Path(__file__).resolve().parents[3]
        file = root / "docs/engineering-control/projects/autotest/snapshot.json"
        if not file.is_file():
            self.skipTest("Checked-in AutoTest source archive not present")
        raw = json.loads(file.read_text(encoding="utf-8"))
        from studio_restore import preview, restore_to_empty
        from studio_product_identity import ProductIdentity
        self.assertEqual(raw["authority"], "OWNER_APPROVED_P0_DOCUMENT")
        source = committed_project(root, "autotest")
        self.assertEqual(source["authority"], "OWNER_APPROVED_P0_DOCUMENT")
        self.assertEqual(source["archive"]["project"]["id"], "autotest")
        self.assertEqual(preview(raw["engineering"])["status"], "STRUCTURAL_PRECHECK_PASS")
        target = self.root / "autotest-trial.sqlite"
        restored = restore_to_empty(raw["engineering"], target)
        self.assertEqual(restored["project"]["id"], "autotest")
        self.assertGreaterEqual(len(restored["objects"]), 10)
        approved = [o for o in restored["objects"] if o["state"] == "APPROVED"]
        self.assertEqual(len(approved), 1)
        self.assertEqual(approved[0]["id"], "at-p0-approved-charter-v01")
        self.assertEqual(approved[0]["kind"], "context")
        self.assertTrue(all(r["status"] == "CANDIDATE" for r in restored["relations"]))
        identity = ProductIdentity(Store(target)).status("autotest")
        self.assertEqual(identity["state"], "BASELINED")
        self.assertEqual(identity["baseline"]["revision"], 1)
        self.assertEqual(identity["baseline"]["statement"], raw["approval"]["statement"])


if __name__ == "__main__":
    unittest.main()
