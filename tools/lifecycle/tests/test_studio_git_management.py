"""Git-managed AI project briefs stay read-only, version-pinned and non-authoritative."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from studio_git_management import current_views
from studio_store import RuleError

INDEX = "docs/engineering-control/projects/index.json"
BRIEF = "docs/engineering-control/projects/fixture/management-view.json"


class GitManagedBriefTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.repo = Path(temp.name)
        self.git("init", "-q")
        self.git("config", "user.name", "CI Tester")
        self.git("config", "user.email", "tester@example.invalid")
        self.write("README.md", "# Test\n")
        self.git("add", ".")
        self.git("commit", "-qm", "initial")

    def git(self, *args):
        return subprocess.run(["git", "-C", str(self.repo), *args], check=True,
                              text=True, capture_output=True).stdout.strip()

    def write(self, path, text):
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    def prepare(self):
        index = {"schema": "engineering-manager-view-index/1", "projects": [
            {"id": "fixture", "path": BRIEF}]}
        brief = {"schema": "engineering-manager-view/1",
                 "project_id": "fixture", "project_name": "Fixture",
                 "status": "AI_CANDIDATE_NOT_APPROVED",
                 "evidence": [{"title": "Source evidence", "status": "UNKNOWN"}],
                 "judgments": [{"title": "Needs review", "priority": "P1"}],
                 "open_questions": ["Can this claim be verified?"]}
        self.write(INDEX, json.dumps(index))
        self.write(BRIEF, json.dumps(brief))
        self.git("add", ".")
        self.git("commit", "-qm", "first candidate")

    def test_committed_brief_is_visible_without_sqlite_or_owner_login(self):
        self.prepare()
        result = current_views(self.repo)
        self.assertEqual(result["git_commit"], self.git("rev-parse", "HEAD"))
        self.assertEqual(result["authority"], "AI_CANDIDATE_NOT_APPROVED")
        self.assertEqual(result["projects"][0]["project_name"], "Fixture")
        self.assertEqual(result["projects"][0]["evidence"][0]["status"], "UNKNOWN")

    def test_uncommitted_changes_are_not_falsely_shown_as_published(self):
        self.prepare()
        self.write(BRIEF, '{"tampered":true}')
        result = current_views(self.repo)
        self.assertEqual(result["projects"][0]["project_name"], "Fixture")

    def test_missing_index_is_not_an_approved_project(self):
        result = current_views(self.repo)
        self.assertEqual(result["projects"], [])

    def test_index_path_traversal_rejected(self):
        self.prepare()
        self.write(INDEX, json.dumps({
            "schema": "engineering-manager-view-index/1",
            "projects": [{"id": "fixture", "path": "../private.json"}]}))
        self.git("add", ".")
        self.git("commit", "-qm", "bad index")
        with self.assertRaisesRegex(RuleError, "Unsafe"):
            current_views(self.repo)

    def test_brief_must_be_explicitly_unapproved(self):
        self.prepare()
        candidate = json.loads((self.repo / BRIEF).read_text(encoding="utf-8"))
        candidate["status"] = "APPROVED"
        self.write(BRIEF, json.dumps(candidate))
        self.git("add", ".")
        self.git("commit", "-qm", "invalid status")
        with self.assertRaisesRegex(RuleError, "AI candidate"):
            current_views(self.repo)


if __name__ == "__main__":
    unittest.main()
