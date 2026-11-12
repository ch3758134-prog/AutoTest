"""Black-box smoke test: use only the *extracted ZIP*, no AutoTest checkout.

Tests new- and existing-project initialization and local manager operations
using the same self-contained folder intended for a new ChatGPT conversation.
"""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE / "portable"))
from export_package import build_zip


def git(root, *arguments):
    command = subprocess.run(["git", "-C", str(root), *arguments],
                             capture_output=True, text=True, encoding="utf-8",
                             errors="replace", timeout=20)
    if command.returncode != 0:
        raise AssertionError(command.stderr)
    return command.stdout.strip()


class ExtractedPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        package_file = self.home / "ProjectControl.zip"
        build_zip(package_file)
        with zipfile.ZipFile(package_file) as archive:
            # All entries were explicitly checked by the manifest builder.
            archive.extractall(self.home)
        self.package = self.home / "ProjectControl"
        self.init_script = self.package / "portable" / "project_init.py"
        self.web_script = self.package / "portable" / "web_server.py"

    def script(self, path, *arguments, expected=0):
        proc = subprocess.run(
            [sys.executable, str(path), *map(str, arguments)],
            cwd=self.home, capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=30)
        self.assertEqual(proc.returncode, expected, proc.stderr + proc.stdout)
        return json.loads(proc.stdout if expected == 0 else proc.stderr)

    def make_target(self, suffix, with_asset):
        folder = self.home / suffix
        folder.mkdir()
        remote = f"https://github.com/sample/{suffix}.git"
        git(folder, "init", "-b", "main")
        git(folder, "config", "user.email", "fixture@example.invalid")
        git(folder, "config", "user.name", "Package Fixture")
        git(folder, "remote", "add", "origin", remote)
        if with_asset:
            (folder / "README.md").write_text("Existing source asset\n", encoding="utf-8")
            git(folder, "add", "README.md")
            git(folder, "commit", "-m", "existing project history")
        return folder, remote

    def test_new_project_from_zip_has_self_contained_runtime_and_web_manager(self):
        folder, remote = self.make_target("fresh-project", False)
        proposal = self.script(
            self.init_script, "preview", "--target", folder, "--repo-url", remote,
            "--mode", "new", "--project-id", "fresh-project")
        self.assertIn(".project-control/workspace_manager.py", proposal["file_paths"])
        self.assertIn(".project-control/resolve.py", proposal["file_paths"])
        self.assertFalse((folder / ".project-control").exists())
        self.script(self.init_script, "apply", "--target", folder, "--repo-url", remote,
                    "--mode", "new", "--project-id", "fresh-project")
        manager_script = self.package / "portable" / "web_server.py"
        command = ("import json,sys; sys.path.insert(0,sys.argv[1]);"
                   "from web_server import Target;"
                   "t=Target(sys.argv[2]); m=t.model();"
                   "assert m['tasks']['tasks']==[];"
                   "assert m['project']['phase_gate']=='NOT_VERIFIED';"
                   "r=t.apply('create_deliverable',"
                   "{'expected_revision':1,'reason':'Owner review candidate',"
                   "'id':'D1','title':'Research output',"
                   "'success_condition':'Traceable source evidence'});"
                   "assert r['result']=='LOCAL_DRAFT_SAVED_NOT_REMOTE_PUBLISHED';"
                   "print(json.dumps({'revision':t.model()['management']['revision'],"
                   "'product':m['project']['formal_product_identity']}))")
        runner = self.home / "portable_smoke.py"
        runner.write_text(command + "\n", encoding="utf-8")
        ran = self.script(runner, str(manager_script.parent), str(folder))
        self.assertEqual(ran["revision"], 2)
        self.assertEqual(ran["product"], "UNDECIDED")
        self.assertTrue((folder / ".project-control/workspace_manager.py").is_file())
        self.assertEqual(git(folder, "status", "--short").splitlines()[0][:2], "??")

    def test_existing_project_from_zip_preserves_original_asset(self):
        folder, remote = self.make_target("existing-project", True)
        before = git(folder, "rev-parse", "HEAD")
        self.script(self.init_script, "apply", "--target", folder, "--repo-url", remote,
                    "--mode", "adopt", "--project-id", "existing-project")
        self.assertEqual(git(folder, "rev-parse", "HEAD"), before)
        self.assertEqual((folder / "README.md").read_text(encoding="utf-8"),
                         "Existing source asset\n")
        self.assertTrue((folder / ".project-control/workspace_manager.py").is_file())
        self.assertEqual(json.loads((folder / ".project-control/project.json").read_text())[
                         "initialization_state"], "CANDIDATE_NOT_APPROVED")

    def test_extracted_zip_starts_real_local_http_api_with_owner_auth(self):
        folder, remote = self.make_target("web-fixture", False)
        self.script(self.init_script, "apply", "--target", folder,
                    "--repo-url", remote, "--mode", "new",
                    "--project-id", "web-fixture")
        harness = self.home / "portable_http_smoke.py"
        harness.write_text(
            "import json,sys,threading,urllib.request,urllib.error\n"
            "sys.path.insert(0,sys.argv[1])\n"
            "from web_server import Target,handler,ThreadingHTTPServer\n"
            "target=Target(sys.argv[2])\n"
            "server=ThreadingHTTPServer(('127.0.0.1',0),handler({target.project_id:target},'fixture-token'))\n"
            "thread=threading.Thread(target=server.serve_forever,daemon=True)\n"
            "thread.start()\n"
            "try:\n"
            "    base='http://127.0.0.1:'+str(server.server_port)\n"
            "    with urllib.request.urlopen(base+'/api/projects',timeout=5) as r:\n"
            "        projects=json.load(r)['projects']\n"
            "    assert len(projects)==1 and projects[0]['project_id']=='web-fixture'\n"
            "    body=json.dumps({'project_id':'web-fixture','action':'create_requirement',"
            "'payload':{'expected_revision':1,'reason':'Actual local API smoke',"
            "'id':'REQ1','title':'Verified study','statement':'Collect original sources',"
            "'source_refs':['source:fixture'],'acceptance':'Review complete'}}).encode()\n"
            "    def post(auth):\n"
            "        headers={'Content-Type':'application/json'}\n"
            "        if auth: headers['X-Owner-Token']='fixture-token'\n"
            "        return urllib.request.Request(base+'/api/action',data=body,"
            "headers=headers,method='POST')\n"
            "    try:\n"
            "        urllib.request.urlopen(post(False),timeout=5)\n"
            "        raise AssertionError('unauthorized mutation succeeded')\n"
            "    except urllib.error.HTTPError as error:\n"
            "        assert error.code==403\n"
            "    with urllib.request.urlopen(post(True),timeout=5) as r:\n"
            "        changed=json.load(r)\n"
            "    assert changed['result']=='LOCAL_DRAFT_SAVED_NOT_REMOTE_PUBLISHED'\n"
            "    with urllib.request.urlopen(base+'/api/model?project_id=web-fixture',timeout=5) as r:\n"
            "        data=json.load(r)\n"
            "    assert data['management']['requirements'][0]['state']=='PROPOSED_NOT_APPROVED'\n"
            "    print(json.dumps({'status':'EXTRACTED_HTTP_E2E_PASS',"
            "'revision':data['management']['revision']}))\n"
            "finally:\n"
            "    server.shutdown();server.server_close();thread.join(5)\n",
            encoding="utf-8",
        )
        result = self.script(harness, str(self.web_script.parent), str(folder))
        self.assertEqual(result["status"], "EXTRACTED_HTTP_E2E_PASS")
        self.assertEqual(result["revision"], 2)

    def test_wrong_repo_and_duplicate_init_are_blocked_without_asset_removal(self):
        folder, remote = self.make_target("protected-project", True)
        wrong = "https://github.com/another/other-project.git"
        blocked = self.script(self.init_script, "apply", "--target", folder,
                              "--repo-url", wrong, "--mode", "adopt",
                              "--project-id", "protected-project", expected=2)
        self.assertEqual(blocked["status"], "BLOCKED")
        self.assertFalse((folder / ".project-control").exists())
        self.script(self.init_script, "apply", "--target", folder, "--repo-url", remote,
                    "--mode", "adopt", "--project-id", "protected-project")
        blocked = self.script(self.init_script, "apply", "--target", folder,
                              "--repo-url", remote, "--mode", "adopt",
                              "--project-id", "protected-project", expected=2)
        self.assertEqual(blocked["status"], "BLOCKED")
        self.assertEqual((folder / "README.md").read_text(encoding="utf-8"),
                         "Existing source asset\n")


if __name__ == "__main__":
    unittest.main()
