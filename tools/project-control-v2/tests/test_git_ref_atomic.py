"""Race tests for non-force Git Data ref publication, without network."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("atomic_git", ROOT / "portable/git_ref_atomic.py")
atomic = importlib.util.module_from_spec(spec)
spec.loader.exec_module(atomic)


class FakeGitData:
    def __init__(self):
        self.head = "a" * 40
        self.parent = self.head
        self.older_tree = "b" * 40
        self.new_tree = "c" * 40
        self.candidate_sha = "d" * 40
        self.content = b"old management contents"
        self.proposed = None
        self.mutations = []
        self.race = False
        self.advance_after = False

    def branch_head(self, parts, branch):
        return self.head

    def file(self, parts, path, branch):
        return atomic.git_blob_sha(self.content.decode("utf-8")), self.content

    def commit_parents(self, parts, sha):
        if sha == self.candidate_sha:
            return [self.parent]
        raise AssertionError("unexpected commit")

    def request(self, verb, uri, data=None):
        if verb == "GET" and "/git/commits/" in uri:
            return {"tree": {"sha": self.older_tree}}
        if verb == "POST" and uri.endswith("/git/trees"):
            self.mutations.append("created-tree-not-published")
            self.proposed = data["tree"][0]["content"]
            self.assert_tree(data)
            return {"sha": self.new_tree}
        if verb == "POST" and uri.endswith("/git/commits"):
            self.mutations.append("created-commit-not-published")
            if data["parents"] != [self.parent]:
                raise AssertionError("not based on reviewed source")
            if self.race:
                self.head = "e" * 40
            return {"sha": self.candidate_sha}
        if verb == "PATCH" and "/git/refs/heads/" in uri:
            if data["force"] is not False:
                raise AssertionError("dangerous force ref update")
            if self.head != self.parent:
                raise atomic.AtomicGitError("GitHub rejected non-fast-forward ref update")
            self.mutations.append("fast-forward-ref-updated")
            self.head = data["sha"]
            self.content = self.proposed.encode("utf-8")
            if self.advance_after:
                self.head = "f" * 40
            return {"ref": uri}
        raise AssertionError("unexpected Git operation: " + verb + " " + uri)

    def assert_tree(self, data):
        assert data["base_tree"] == self.older_tree
        assert len(data["tree"]) == 1
        assert data["tree"][0]["path"] == ".project-control/tasks.json"
        assert data["tree"][0]["mode"] == "100644"
        assert data["tree"][0]["type"] == "blob"


class AtomicGitTests(unittest.TestCase):
    def setUp(self):
        self.client = FakeGitData()

    def send(self, path=".project-control/tasks.json", branch="feature/task"):
        return atomic.commit_exact_file(
            self.client, ("owner", "repo"), branch, "a" * 40, path,
            '{"schema": "manager-task"}', "chore(project-control): publish approved task"
        )

    def test_atomic_branch_ref_fast_forward_and_file_verification(self):
        result = self.send()
        self.assertEqual(result["status"], "ATOMIC_GIT_REF_CAS_PUBLISHED_AND_VERIFIED")
        self.assertEqual(result["commit_sha"], self.client.candidate_sha)
        self.assertFalse(result["force"])
        self.assertEqual(self.client.head, self.client.candidate_sha)
        self.assertEqual(self.client.content.decode(), '{"schema": "manager-task"}')
        self.assertEqual(self.client.mutations[-1], "fast-forward-ref-updated")

    def test_race_after_commit_object_is_created_never_updates_branch(self):
        self.client.race = True
        with self.assertRaisesRegex(atomic.AtomicGitError, "non-fast-forward"):
            self.send()
        self.assertEqual(self.client.head, "e" * 40)
        self.assertNotIn("fast-forward-ref-updated", self.client.mutations)
        self.assertEqual(self.client.content, b"old management contents")

    def test_source_head_changed_before_any_objects_are_created(self):
        self.client.head = "e" * 40
        with self.assertRaisesRegex(atomic.AtomicGitError, "before atomic"):
            self.send()
        self.assertEqual(self.client.mutations, [])

    def test_other_writer_after_publication_requires_reconciliation(self):
        self.client.advance_after = True
        with self.assertRaisesRegex(atomic.AtomicGitError, "advanced again"):
            self.send()
        self.assertIn("fast-forward-ref-updated", self.client.mutations)

    def test_path_traversal_and_invalid_branch_are_rejected_before_writes(self):
        for path in ("../secret", "/etc/passwd", ".git/config",
                     ".project-control/../../else", "a\\b"):
            with self.subTest(path=path), self.assertRaises(atomic.AtomicGitError):
                self.send(path=path)
        for branch in ("../main", "bad..branch", "-bad", "a//b", ""):
            with self.subTest(branch=branch), self.assertRaises(atomic.AtomicGitError):
                self.send(branch=branch)
        self.assertEqual(self.client.mutations, [])

    def test_content_blob_sha_is_git_object_sha_not_plain_file_digest(self):
        self.assertEqual(atomic.git_blob_sha("hello"), "b6fc4c620b67d95f953a5c1c1230aaab5db5a1b0")


if __name__ == "__main__":
    unittest.main()
