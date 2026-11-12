"""End-to-end local HTTP boundaries: draft writes, restricted reads, owner decisions."""
import json
import pathlib
import subprocess
import sys
import tempfile
import threading
import unittest
from http.server import ThreadingHTTPServer
from urllib.error import HTTPError
from urllib.request import Request, urlopen

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "portable"))
from project_init import inspect_target, create_preview, apply_preview
from web_server import Target, handler

REPO = "https://github.com/example/pc-delivery-test.git"

def git(root, *args):
    return subprocess.run(["git","-C",str(root),*args],capture_output=True,
                          text=True,check=True).stdout.strip()

def doc():
    return {"id":"ART-001","kind":"REQUIREMENTS","title":"G1 complete needs",
            "why":"The owner should review the actual requirements before publication.",
            "body":"# Complete G1 candidate\nFull requirements and their conditions.",
            "scope":"Only whole-system requirements; no implementation authorization.",
            "changes":"Corrects historical test assumptions; owner scope unchanged.",
            "limitations":"Hardware power behavior needs physical verification.",
            "owner_question":"Allow this exact text to be a GitHub review candidate?",
            "publication_type":"GITHUB_CANDIDATE_COMMENT",
            "publication_target":"issue:22",
            "source_refs":["approved charter@commit","G1 research@revision"]}

class PrivateDeliveryServerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        git(self.root,"init","-q")
        git(self.root,"config","user.email","test@example.org")
        git(self.root,"config","user.name","Test")
        git(self.root,"remote","add","origin",REPO)
        (self.root/"README.md").write_text("source",encoding="utf-8")
        git(self.root,"add","README.md")
        git(self.root,"commit","-qm","baseline")
        preview = create_preview(inspect_target(self.root,REPO),"adopt",project_id="demo")
        apply_preview(self.root,inspect_target(self.root,REPO),preview)
        git(self.root,"add",".project-control","PROJECT_START_HERE.md")
        git(self.root,"commit","-qm","init")
        self.target=Target(self.root)
        self.original=(self.root/".project-control/management.json").read_bytes()
        self.web=ThreadingHTTPServer(("127.0.0.1",0),
                    handler({"demo":self.target},"MANAGER_SECRET",
                            review_token="REVIEW_SECRET"))
        thread=threading.Thread(target=self.web.serve_forever,daemon=True)
        thread.start()
        self.addCleanup(self.web.server_close)
        self.addCleanup(self.web.shutdown)

    def request(self,path,payload=None,token=None,review=False):
        headers={"Origin":"http://127.0.0.1:"+str(self.web.server_port)}
        if payload is not None:headers["Content-Type"]="application/json"
        if token:headers["X-Review-Token" if review else "X-Owner-Token"]=token
        req=Request("http://127.0.0.1:"+str(self.web.server_port)+path,
              headers=headers,
              data=(json.dumps(payload).encode("utf-8") if payload is not None else None),
              method=("POST" if payload is not None else "GET"))
        try:
            with urlopen(req,timeout=5) as response:
                return response.status,json.loads(response.read())
        except HTTPError as err:
            return err.code,json.loads(err.read())

    def test_private_draft_never_in_tracked_management_or_public_model(self):
        stage={"project_id":"demo","action":"stage_delivery",
               "payload":{**doc(),"expected_store_revision":1}}
        code,_=self.request("/api/delivery-draft-action",stage,"MANAGER_SECRET")
        self.assertEqual(code,200)
        self.assertEqual((self.root/".project-control/management.json").read_bytes(),
                         self.original)
        self.assertFalse(git(self.root,"status","--porcelain"))
        code,public=self.request("/api/model?project_id=demo")
        self.assertEqual(code,200)
        self.assertNotIn("Full requirements and their conditions.",json.dumps(public))
        code,_=self.request("/api/delivery-reviews?project_id=demo")
        self.assertEqual(code,403)
        code,private=self.request("/api/delivery-reviews?project_id=demo",
                                   token="REVIEW_SECRET",review=True)
        self.assertEqual(code,200)
        self.assertIn("Full requirements",private["delivery_reviews"][0]["body"])

    def test_manager_cannot_decide_owner_review(self):
        stage={"project_id":"demo","action":"stage_delivery",
               "payload":{**doc(),"expected_store_revision":1}}
        self.assertEqual(self.request("/api/delivery-draft-action",stage,
                                      "MANAGER_SECRET")[0],200)
        row=self.target.read_delivery_store()["delivery_reviews"][0]
        offer={"project_id":"demo","action":"offer_delivery_review",
               "payload":{"id":row["id"],"delivery_revision":row["revision"],
                          "expected_sha256":row["sha256"],"expected_store_revision":2}}
        self.assertEqual(self.request("/api/delivery-draft-action",offer,
                                      "MANAGER_SECRET")[0],200)
        owner={"project_id":"demo","action":"owner_decide_delivery",
               "payload":{"id":row["id"],"delivery_revision":row["revision"],
                          "expected_sha256":row["sha256"],"expected_store_revision":3,
                          "decision":"ALLOW_CANDIDATE_PUBLICATION",
                          "reason":"Owner has read the exact packet",
                          "confirm_exact_content":True}}
        self.assertEqual(self.request("/api/action",owner,"MANAGER_SECRET")[0],400)
        self.assertEqual(self.request("/api/owner-delivery-decision",
                                      owner,"MANAGER_SECRET")[0],403)
        self.assertEqual(self.request("/api/owner-delivery-decision",
                                      owner,"REVIEW_SECRET",review=True)[0],200)
        store=self.target.read_delivery_store()
        self.assertEqual(store["delivery_reviews"][0]["status"],
                         "RELEASED_FOR_CANDIDATE_PUBLICATION")
        self.assertEqual((self.root/".project-control/management.json").read_bytes(),
                         self.original)
        self.assertFalse(git(self.root,"status","--porcelain"))

if __name__=="__main__":
    unittest.main()
