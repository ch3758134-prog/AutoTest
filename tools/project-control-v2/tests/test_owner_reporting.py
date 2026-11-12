import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"portable"))
from process_control import apply, state_for, ProcessError

class OwnerReportingTests(unittest.TestCase):
    def setUp(self):
        self.record={}
        apply(self.record,"record_ai_intake",{"id":"intake-1","role":"coordinator","role_source":"USER_EXPLICIT","source_refs":["owner-request"],"next_action":"研究项目目标"})
        self.report={"id":"report-1","intake_id":"intake-1","focus":"核实立项理由","why_this_work":"未明确目的不能贸然设计","judgement":"历史材料存在冲突，需要重新分析","summary":"已阅读可用史料","evidence_refs":["source-one"],"unfinished":["待调查"],"next_action":"明确当前高层范围","owner_action":"NONE","owner_request":""}
    def test_report_is_candidate_not_approval(self):
        x=apply(self.record,"record_ai_work_report",self.report)
        r=state_for(self.record)["reports"][0]
        self.assertFalse(x["authorized"])
        self.assertEqual(r["judgement"],self.report["judgement"])
        self.assertEqual(r["state"],"REPORTED_NOT_ACCEPTED")
    def test_specific_decision_needed(self):
        self.report.update({"owner_action":"DECIDE","owner_request":"确认是否有外部交付责任"})
        apply(self.record,"record_ai_work_report",self.report)
        self.assertEqual(state_for(self.record)["reports"][0]["owner_action"],"DECIDE")
    def test_missing_why_is_rejected(self):
        self.report.pop("why_this_work")
        with self.assertRaises(ProcessError):apply(self.record,"record_ai_work_report",self.report)
    def test_request_requires_action(self):
        self.report["owner_request"]="批准所有阶段"
        with self.assertRaises(ProcessError):apply(self.record,"record_ai_work_report",self.report)
    def test_action_requires_specific_request(self):
        self.report["owner_action"]="REVIEW"
        with self.assertRaises(ProcessError):apply(self.record,"record_ai_work_report",self.report)
    def test_historic_report_remains_readable(self):
        old={"id":"old","summary":"原报告","state":"REPORTED_NOT_ACCEPTED"}
        self.record["process_control"]["reports"].append(old)
        self.assertEqual(state_for(self.record)["reports"][0],old)
if __name__=="__main__":unittest.main()
