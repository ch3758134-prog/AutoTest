"""Read actual GitHub module source/PR status only on demand, never write."""
import sys
from pathlib import Path
import unittest

HERE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(HERE/"portable"))
import module_control
import remote_module_status
from github_bridge import RemoteError


class GitHubFixture:
    def __init__(self):
        self.head="a"*40
        self.expected_head=self.head
        self.events=[]
        self.moved=False

    def branch_head(self,parts,branch):
        assert branch=="main"
        self.events.append(("HEAD",branch))
        if self.moved and self.events.count(("HEAD",branch))>1:
            return "b"*40
        return self.head

    def request(self,method,resource,data=None):
        self.events.append((method,resource))
        if method!="GET":
            raise AssertionError("remote inspection must never write")
        if resource=="/repos/acme/example":
            return {"default_branch":"main"}
        if "/commits?sha=" in resource:
            return [{"sha":"c"*40}]
        if "/pulls?state=all" in resource:
            return [{"number":12,"head":{"sha":"d"*40},
                     "base":{"ref":"main"},"html_url":"https://github.com/acme/example/pull/12",
                     "state":"open","draft":True,"merged_at":None}]
        raise AssertionError("unexpected GitHub call: "+resource)

    def pr_files(self,parts,num,max_pages=5):
        assert num==12
        return [{"filename":"src/device/adapter.py"},
                {"filename":"src/ui/window.py"}]


class RemoteModuleTests(unittest.TestCase):
    def setUp(self):
        self.client=GitHubFixture()
        self.record={"modules":[]}
        module_control.add_module(self.record,{
            "id":"DEVICE","title":"Device","responsibility":"CAN device adapter",
            "owner_role":"specialist","owned_prefixes":["src/device/"],
            "contracts":["src/device/contracts.py"],"depends_on":[]})
        module_control.add_module(self.record,{
            "id":"UI","title":"UI","responsibility":"Desktop module",
            "owner_role":"specialist","owned_prefixes":["src/ui/"],
            "contracts":[],"depends_on":["DEVICE"]})
        self.project={"repo_url":"https://github.com/acme/example.git"}

    def test_remote_status_contains_exact_base_commit_and_real_pr_file_impact(self):
        result=remote_module_status.inspect(self.client,self.project,self.record,
                                            recent_pr_limit=10)
        self.assertEqual(result["source"],"LIVE_GITHUB_READ_ONLY")
        self.assertEqual(result["head"],"a"*40)
        self.assertEqual(len(result["modules"]),2)
        self.assertEqual(result["modules"][0]["last_commits_by_path"][0]["commit_sha"],"c"*40)
        pr=result["recent_prs"][0]
        self.assertEqual(pr["affected_modules"],["DEVICE","UI"])
        self.assertEqual(pr["scope_review"],"CROSS_MODULE_OR_UNOWNED_REVIEW_REQUIRED")
        self.assertEqual(pr["head_sha"],"d"*40)
        self.assertEqual(pr["reviewed"],"NOT_CHECKED")
        self.assertEqual(pr["required_ci"],"NOT_CHECKED")
        self.assertFalse(pr["merged_by_github"])

    def test_moving_remote_head_aborts_instead_of_reporting_stale_state(self):
        self.client.moved=True
        with self.assertRaisesRegex(RemoteError,"changed during module snapshot"):
            remote_module_status.inspect(self.client,self.project,self.record)


if __name__=="__main__":
    unittest.main()
