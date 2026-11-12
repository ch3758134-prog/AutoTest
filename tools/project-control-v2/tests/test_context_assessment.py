import importlib.util
from pathlib import Path
import unittest

p=Path(__file__).resolve().parents[1]/"portable"/"context_assessment.py"
spec=importlib.util.spec_from_file_location("context_assessment",p)
mod=importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

def sample():
    return {"schema":mod.SCHEMA,"role":"coordinator","project_ref":"owner/repo",
            "task_focus":"existing project intake","source_revision":"observed main HEAD",
            "sources":[],"search_attempts":[],"claims":[],"proposed_actions":[],
            "topics":{topic:{"status":"NOT_SEARCHED","source_ids":[],"claim_ids":[],
                 "search_attempt_ids":[],"action_ids":[],"limitation":"not visited"}
                 for topic in mod.TOPICS},"next_disposition":"CONTINUE_DISCOVERY"}

class ContextAssessmentTests(unittest.TestCase):
    def test_unknown_not_authority(self):
        x=mod.audit(sample())
        self.assertFalse(x["authorization_granted"])
        self.assertEqual(len(x["not_searched_or_incomplete"]),len(mod.TOPICS))

    def test_single_empty_search_does_not_prove_missing_document(self):
        v=sample()
        v["search_attempts"]=[{"id":"q1","domain":"product","method":"code-search",
                              "query_or_path":"product definition","outcome":"NO_MATCH"}]
        v["topics"]["purpose"].update(status="GAP_CONFIRMED",
                                       search_attempt_ids=["q1"],gap_rationale="not found")
        with self.assertRaises(mod.AssessmentError):mod.audit(v)

    def test_two_distinct_methods_and_remediation_required_for_gap(self):
        v=sample()
        v["search_attempts"]=[{"id":"q1","domain":"product","method":"code-search",
                               "query_or_path":"product","outcome":"NO_MATCH"},
                              {"id":"q2","domain":"product","method":"history-inspection",
                               "query_or_path":"docs history","outcome":"NO_MATCH"}]
        v["proposed_actions"]=[{"id":"a1","topic":"purpose","kind":"DRAFT_MISSING_ARTIFACT",
                 "reason":"source not located","expected_output":"candidate charter",
                 "status":"PROPOSED_NOT_AUTHORIZED"}]
        v["topics"]["purpose"].update(status="GAP_CONFIRMED",search_attempt_ids=["q1","q2"],
                                      action_ids=["a1"],gap_rationale="investigation performed")
        x=mod.audit(v)
        self.assertEqual(x["confirmed_gaps"],["purpose"])
        self.assertFalse(x["formal_approval_granted"])

    def test_two_found_results_are_not_missing_evidence(self):
        v=sample()
        v["search_attempts"]=[{"id":"q1","domain":"product","method":"directory",
                                "query_or_path":"docs/","outcome":"FOUND"},
                               {"id":"q2","domain":"product","method":"history",
                                "query_or_path":"history/","outcome":"FOUND"}]
        v["proposed_actions"]=[{"id":"a1","topic":"purpose","kind":"RESEARCH_SOURCE",
                        "reason":"need review","expected_output":"context analysis",
                        "status":"PROPOSED_NOT_AUTHORIZED"}]
        v["topics"]["purpose"].update(status="GAP_CONFIRMED",
                   search_attempt_ids=["q1","q2"],action_ids=["a1"],gap_rationale="claimed missing")
        with self.assertRaises(mod.AssessmentError):mod.audit(v)

    def test_search_error_is_not_absence(self):
        v=sample()
        v["search_attempts"]=[{"id":"q1","domain":"p","method":"search","query_or_path":"p",
                               "outcome":"ERROR","limitation":"index unreachable"},
                              {"id":"q2","domain":"p","method":"history","query_or_path":"p",
                               "outcome":"NO_MATCH"}]
        v["topics"]["purpose"].update(status="GAP_CONFIRMED",
                                      search_attempt_ids=["q1","q2"],gap_rationale="no results")
        with self.assertRaises(mod.AssessmentError):mod.audit(v)

    def test_found_is_not_assessed(self):
        v=sample()
        v["sources"]=[{"id":"s1","type":"document","locator":"P0@sha"}]
        v["topics"]["purpose"].update(status="SOURCES_FOUND",source_ids=["s1"],
                                      limitation="historical validity unchecked")
        self.assertEqual(mod.audit(v)["found_not_assessed"],["purpose"])
        v["topics"]["purpose"]["status"]="ASSESSED"
        with self.assertRaises(mod.AssessmentError):mod.audit(v)

    def test_supported_claim_requires_history_and_applicability_reason(self):
        v=sample()
        v["sources"]=[{"id":"s1","type":"document","locator":"P0@sha"}]
        v["claims"]=[{"id":"c1","statement":"project objective","assessment":"SUPPORTED_CANDIDATE",
                      "source_ids":["s1"],"reasoning":"matched text"}]
        with self.assertRaises(mod.AssessmentError):mod.audit(v)
        v["claims"][0].update(historical_changes_checked=True,
                              current_applicability_reason="compared subsequent decisions")
        v["topics"]["purpose"].update(status="ASSESSED",source_ids=["s1"],claim_ids=["c1"],
                                     assessment_summary="still candidate, not owner approved")
        self.assertEqual(mod.audit(v)["found_not_assessed"],[])

    def test_cannot_dispatch_from_unassessed_prerequisite(self):
        v=sample()
        v.update(next_disposition="CANDIDATE_SCOPED_WORK",
                 work_prerequisite_topics=["purpose"],readiness_rationale="scope limited")
        with self.assertRaises(mod.AssessmentError):mod.audit(v)

    def test_bundled_example_is_structurally_valid(self):
        import json
        ex=Path(__file__).resolve().parents[1]/"portable"/"context_assessment_example.json"
        result=mod.audit(json.loads(ex.read_text(encoding="utf-8")))
        self.assertEqual(result["found_not_assessed"],["purpose"])
        self.assertFalse(result["authorization_granted"])

    def test_no_discovery_approval_or_unauthorized_remediation(self):
        v=sample()
        v["claims"]=[{"id":"c1","statement":"a","assessment":"UNKNOWN","source_ids":[],
                      "formally_approved_now":True}]
        with self.assertRaises(mod.AssessmentError):mod.audit(v)
        v["claims"][0].pop("formally_approved_now")
        v["proposed_actions"]=[{"id":"a1","topic":"purpose","kind":"RESEARCH_SOURCE",
                       "reason":"unknown","expected_output":"source","status":"AUTHORIZED"}]
        v["topics"]["purpose"]["action_ids"]=["a1"]
        with self.assertRaises(mod.AssessmentError):mod.audit(v)

if __name__=="__main__":
    unittest.main()
