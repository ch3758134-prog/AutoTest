"""Milestone, risk, iteration and HTTP owner-operation safeguards."""
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from http.server import ThreadingHTTPServer

HERE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(HERE))
from control import Control, ControlError
from server import handler


class ManagementFeaturesTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        path=Path(self.tmp.name)/"autotest.json"
        path.write_bytes((HERE/"workspace/autotest.json").read_bytes())
        self.ctrl=Control(path,HERE/"sources/autotest_engineering.json")

    def do(self,name,**data):
        data.setdefault("reason","来源和决定由负责人明确记录")
        data["expected_revision"]=self.ctrl.read()["revision"]
        return self.ctrl.apply(name,data,"OwnerTest")

    def test_milestone_requires_grounded_date(self):
        item={"title":"R1 集成检查点","deliverable_id":"d-real-autotest-loop",
              "exit_condition":"有可核实的集成结果","target_date":"2027-04-10","date_basis":None}
        with self.assertRaisesRegex(ControlError,"schedule basis"):
            self.do("create_milestone",item=item)
        item["date_basis"]="已批准的工程排期决定"
        receipt=self.do("create_milestone",item=item)
        self.assertEqual(len(self.ctrl.read()["planning"]["milestones"]),2)
        mid=receipt["event"]["subject"]
        self.do("edit_milestone",id=mid,patch={"target_date":None,"date_basis":None})
        self.assertEqual(self.ctrl.read()["planning"]["milestones"][-1]["target_date"],None)

    def test_risk_cannot_be_closed_on_ai_claim_alone(self):
        outcome=self.do("raise_risk",title="物理输出状态未知",
                        description="执行停止后硬件输出可能仍处于未知状态",
                        likelihood="MEDIUM",severity="HIGH",
                        mitigation="增加现场实测及明确的停止状态观察",
                        owner="DEVICE 方向",source_refs=["field-experiment"],
                        affected_work=["w-physical-verification"])
        rid=outcome["result"]["risk_id"]
        with self.assertRaisesRegex(ControlError,"requires supporting evidence"):
            self.do("review_risk",id=rid,status="CLOSED",evidence_refs=[])
        self.do("review_risk",id=rid,status="MITIGATING",evidence_refs=[])
        self.do("review_risk",id=rid,status="MITIGATED",evidence_refs=["git:record/123"])
        self.assertEqual(self.ctrl.read()["risks"][-1]["status"],"MITIGATED")

    def test_spiral_cycle_records_scope_not_blanket_reset(self):
        before=self.ctrl.read()
        added=self.do("start_cycle",title="真实台架偏差修订",
                      trigger="新的物理事实推翻了按键输入假设",
                      scope="只检查 G1/G2 中受影响的按键机制")
        current=self.ctrl.read()
        self.assertEqual(len(current["cycles"]),len(before["cycles"])+1)
        self.assertEqual(current["planning"],before["planning"])
        cid=added["result"]["cycle_id"]
        self.do("record_phase_review",cycle_id=cid,phase_id="G1",
                decision="DEFER",evidence_ids=[])
        self.assertEqual(self.ctrl.read()["phase_reviews"][-1]["cycle_id"],cid)
        self.do("close_cycle",id=cid,reason="轮次问题处理与历史保持一致")
        self.assertEqual(self.ctrl.read()["cycles"][-1]["state"],"CLOSED")

    def test_http_mutation_needs_authorization_and_revision(self):
        server=ThreadingHTTPServer(("127.0.0.1",0),handler(control=self.ctrl,owner_token="test-owner-secret"))
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        thread=threading.Thread(target=server.serve_forever,daemon=True)
        thread.start()
        base="http://127.0.0.1:"+str(server.server_port)
        url=base+"/api/control/action"
        body={"action":"update_progress","actor":"OwnerTest","payload":{
             "id":"w-g1-review","state":"IN_PROGRESS",
             "detail":"已开始调查并核对参考资料",
             "evidence_refs":[],"expected_revision":1,
             "reason":"负责人批准登记实际工作"}}
        encoded=json.dumps(body).encode("utf-8")
        request=lambda token:Request(url,data=encoded,method="POST",headers={
            "Content-Type":"application/json","X-Owner-Token":token,"Origin":base})
        with self.assertRaises(HTTPError) as denied:
            urlopen(request("wrong"),timeout=6)
        self.assertEqual(denied.exception.code,403)
        self.assertNotIn("w-g1-review",self.ctrl.read()["progress"])
        with urlopen(request("test-owner-secret"),timeout=6) as response:
            payload=json.loads(response.read().decode("utf-8"))
            self.assertEqual(response.status,200)
            self.assertEqual(payload["revision"],2)
        self.assertEqual(self.ctrl.read()["progress"]["w-g1-review"]["state"],"IN_PROGRESS")
        with self.assertRaises(HTTPError) as stale:
            urlopen(request("test-owner-secret"),timeout=6)
        self.assertEqual(stale.exception.code,400)


if __name__=="__main__":
    unittest.main()
