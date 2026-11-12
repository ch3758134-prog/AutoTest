import unittest,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"portable"))
from process_control import apply,catalog,initial_state,state_for,policy_is_current,ProcessError
class WholeLifecycleTests(unittest.TestCase):
 def setUp(self):
  self.r={}
  apply(self.r,"record_ai_intake",{"id":"intake","role":"coordinator","role_source":"USER_EXPLICIT","source_refs":["owner"],"next_action":"调查"})
  apply(self.r,"open_process_activity",{"id":"a","process_id":"needs","role":"coordinator","objective":"用户需求","source_refs":["source"]})
 def payload(self,**over):
  d={"id":"r","intake_id":"intake","activity_id":"a","audience":"OWNER","focus":"业务需求","why_this_work":"真实使用依据不足","judgement":"当前仍是候选","summary":"调查完成部分","next_action":"核对场景","evidence_refs":["source"],"owner_action":"NONE","owner_request":""}
  d.update(over);return d
 def test_catalog_has_all_processes(self):
  p=catalog()["processes"];self.assertEqual(len(p),11)
  self.assertTrue(all(x["owner_guidance"]["why"] and x["review_checks"] for x in p))
 def test_focus_not_approval(self):
  x=apply(self.r,"set_process_focus",{"activity_id":"a","reason":"项目重点","source_refs":["owner"]})
  self.assertFalse(x["authorized"])
  self.assertEqual(x["process_control"]["current_focus"]["activity_id"],"a")
 def test_report_audience_and_link(self):
  apply(self.r,"record_ai_work_report",self.payload())
  x=state_for(self.r)["reports"][0]
  self.assertEqual(x["audience"],"OWNER");self.assertEqual(x["activity_revision"],1)
 def test_stale_report_on_revision(self):
  apply(self.r,"record_ai_work_report",self.payload())
  apply(self.r,"set_process_obligation",{"activity_id":"a","obligation_id":"scenarios","disposition":"SATISFIED_CANDIDATE","reason":"核实","evidence_refs":["src"]})
  self.assertEqual(state_for(self.r)["reports"][0]["freshness"],"STALE_AFTER_ACTIVITY_REVISION")
 def test_bad_audience_or_bad_ref(self):
  for p in ({"audience":"EVERYONE"},{"activity_id":"unknown"}):
   with self.assertRaises(ProcessError): apply(self.r,"record_ai_work_report",self.payload(**p))
 def test_stale_policy_read_only(self):
  self.r["process_control"]=initial_state()
  self.r["process_control"]["catalog_version"]="1.0.0"
  self.assertFalse(policy_is_current(self.r["process_control"]))
  self.assertEqual(state_for(self.r,allow_stale=True)["catalog_version"],"1.0.0")
  with self.assertRaises(ProcessError):apply(self.r,"record_ai_work_report",self.payload())
if __name__=="__main__":unittest.main()
