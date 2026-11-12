"""A GitHub PR is a candidate until source, head, scope, CI and review agree."""
import copy
import importlib.util
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("pc_pr_evidence", HERE / "portable/pr_evidence.py")
pr_evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pr_evidence)

REPO = "https://github.com/acme/research-project.git"
SOURCE = "1" * 40
HEAD = "2" * 40


class PrEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.project = {"project_id": "research", "repo_url": REPO}
        self.task = {"id": "IMPL-001", "project_id": "research",
                     "source": {"repo_url": REPO, "branch": "main",
                                "work_branch": "feature/impl-001",
                                "pinned_sha": SOURCE}}
        self.receipt = {"project_id": "research", "task_id": "IMPL-001",
                        "repo_url": REPO, "source_head": SOURCE,
                        "candidate_head": HEAD, "scope_status": "SCOPE_OK",
                        "workspace_clean": True}
        self.pr = {"url": "https://github.com/acme/research-project/pull/7",
                   "number": 7, "head_repo_full_name": "acme/research-project",
                   "base": "main", "head": "feature/impl-001", "head_sha": HEAD,
                   "draft": True, "state": "open", "merged": False}

    def check(self, **modifications):
        pr = dict(self.pr, **modifications)
        return pr_evidence.validate(self.project, self.task, self.receipt, pr)

    def test_matching_draft_pr_is_still_pending_real_review(self):
        result = self.check()
        self.assertEqual(result["status"], "PR_METADATA_CONSISTENT_PENDING_INDEPENDENT_REVIEW")
        self.assertEqual(result["pr_number"], 7)
        self.assertTrue(result["draft"])
        self.assertFalse(result["task_accepted"])
        self.assertFalse(result["ci_checked"])
        self.assertFalse(result["gate_approved"])

    def test_wrong_head_or_branch_or_base_rejected(self):
        for change in ({"head_sha": "3" * 40}, {"head": "feature/other"},
                       {"base": "release"}, {"head_repo_full_name": "someone/other"}):
            with self.subTest(change=change), self.assertRaises(pr_evidence.PrEvidenceError):
                self.check(**change)

    def test_wrong_repo_pr_url_or_number_rejected(self):
        for change in ({"url": "https://github.com/acme/other/pull/7"},
                       {"url": "https://github.com.evil.test/acme/research-project/pull/7"},
                       {"url": "https://github.com/acme/research-project/pull/8"},
                       {"number": 8}, {"url": "http://github.com/acme/research-project/pull/7"}):
            with self.subTest(change=change), self.assertRaises(pr_evidence.PrEvidenceError):
                self.check(**change)

    def test_merged_closed_or_unknown_draft_not_a_candidate(self):
        for change in ({"merged": True}, {"state": "closed"},
                       {"draft": None}, {"state": "merged"}):
            with self.subTest(change=change), self.assertRaises(pr_evidence.PrEvidenceError):
                self.check(**change)

    def test_cross_project_receipt_rejected(self):
        self.receipt["project_id"] = "another-project"
        with self.assertRaisesRegex(pr_evidence.PrEvidenceError, "different project"):
            self.check()

    def test_codex_dirty_worktree_or_scope_failure_rejected(self):
        for key, value in (("scope_status", "BLOCKED_SCOPE"), ("workspace_clean", False)):
            bad = copy.deepcopy(self.receipt)
            bad[key] = value
            with self.subTest(key=key), self.assertRaises(pr_evidence.PrEvidenceError):
                pr_evidence.validate(self.project, self.task, bad, self.pr)

    def test_unmerged_ready_for_review_pr_still_not_approved(self):
        result = self.check(draft=False)
        self.assertFalse(result["draft"])
        self.assertFalse(result["task_accepted"])
        self.assertFalse(result["test_results_accepted"])


if __name__ == "__main__":
    unittest.main()
