"""V0.9 governance research ledger: AI candidates never authorize product changes."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_governance import DefinitionGovernance,DOMAINS
from studio_definition_research import DefinitionResearch
from studio_research import Research
from studio_restore import preview,restore_to_empty


class GovernedResearchTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.store=Store(self.root/"project.db")
        self.project=self.store.create_project({"name":"Generic product",
           "summary":"Product for engineering workflows",
           "body":"Product has engineering, test and operator usage with configurable interfaces.",
           "version":"R0","variant":"generic"})["project"]["id"]
        self.g=DefinitionGovernance(self.store)
        self.research=DefinitionResearch(self.store)
        current=self.g.status(self.project)
        draft=dict(current["definition"])
        draft["summary"]="Engineering IDE with integrated test execution"
        self.change=self.g.propose(self.project,draft,
           "Change definition and inspect downstream user and implementation behavior",
           ["review://human-discussion"],"Owner",current["definition_digest"])
        self.change_id=self.change["change"]["id"]

    def task(self,domain="POSITIONING"):
        return next(x for x in self.research.list(self.change_id)["tasks"]
                    if x["domain"]==domain)

    def submit(self,task,role="AI_CANDIDATE"):
        return self.research.submit(task["id"],task["revision"],"Research worker",role,
            "Observed product requirements and tested assumptions",
            ["repo://docs/product.md"],"Review actual user expectations separately")

    def confirm(self,task):
        sub=self.submit(task)
        return self.research.review(task["id"],sub["submission_id"],
            task["revision"],"ACCEPT","Human",
            "Reviewed the cited source and checked stated limitations")

    def complete_research(self):
        for task in self.research.list(self.change_id)["tasks"]:
            if task["state"]!="REVIEWED":self.confirm(task)

    def test_proposal_creates_exactly_ten_mandatory_research_tasks(self):
        tasks=self.research.list(self.change_id)["tasks"]
        self.assertEqual({x["domain"] for x in tasks},{x for x,_ in DOMAINS})
        self.assertEqual(len(tasks),10)
        self.assertTrue(all(x["state"]=="OPEN" for x in tasks))
        self.assertEqual(self.change["gate"]["research_required"],10)
        self.assertFalse(self.change["gate"]["ready"])

    def test_ai_submission_remains_candidate_until_owner_review(self):
        task=self.task()
        submitted=self.submit(task)
        self.assertEqual(submitted["status"],"CANDIDATE_ONLY")
        self.assertEqual(self.task()["state"],"OPEN")
        with self.assertRaisesRegex(RuleError,"Domain research must receive"):
            self.g.review(self.change_id,"POSITIONING","NO_IMPACT",
                "Old status",["test://unreviewed"],[],"Human",
                self.change["change"]["base_digest"])
        review=self.research.review(task["id"],submitted["submission_id"],
            task["revision"],"ACCEPT","Human","Checked actual source")
        self.assertEqual(review["revision"],2)
        after=self.task()
        self.assertEqual(after["state"],"REVIEWED")
        self.g.review(self.change_id,"POSITIONING","NO_IMPACT",
            "No change in target role after examining current baseline",
            ["repo://docs/product.md"],[],"Human",self.change["change"]["base_digest"])
        self.assertTrue(any(x["domain"]=="POSITIONING"
                     for x in self.g.detail(self.change_id)["reviews"]))

    def test_reopen_invalidates_already_completed_domain_impact_review(self):
        self.complete_research()
        self.g.review(self.change_id,"POSITIONING","NO_IMPACT",
            "Reviewed role and context boundaries",["repo://product/roles"],[],
            "Human",self.change["change"]["base_digest"])
        first=self.task()
        self.assertEqual(first["revision"],2)
        self.research.reopen(first["id"],2,"Human","New independent evidence")
        self.assertTrue(any("POSITIONING" in reason
            for reason in self.g.gate(self.change_id)["reasons"]))
        reopened=self.task()
        self.assertEqual(reopened["revision"],3)
        self.confirm(reopened)
        # Research has been reapproved, but old domain impact conclusion remains stale.
        self.assertTrue(any("not pinned" in reason for reason in
                            self.g.gate(self.change_id)["reasons"]))
        self.g.review(self.change_id,"POSITIONING","NO_IMPACT",
            "Re-evaluated with latest source",["repo://product/new-source"],[],
            "Human",self.change["change"]["base_digest"])
        self.assertFalse(any("POSITIONING" in reason for reason
                             in self.g.gate(self.change_id)["reasons"]))

    def test_request_more_increments_revision_and_old_findings_cannot_be_reused(self):
        task=self.task()
        submission=self.submit(task)
        review=self.research.review(task["id"],submission["submission_id"],
              task["revision"],"REQUEST_MORE","Human","Evidence incomplete")
        self.assertEqual(review["revision"],2)
        with self.assertRaisesRegex(RuleError,"current task revision"):
            self.research.review(task["id"],submission["submission_id"],2,
                                 "ACCEPT","Human","Use outdated candidate")
        with self.assertRaisesRegex(RuleError,"revision conflict"):
            self.submit(task)
        revised=self.task()
        self.confirm(revised)
        self.assertEqual(self.task()["state"],"REVIEWED")

    def test_no_cross_change_or_cross_project_submission(self):
        task=self.task()
        candidate=self.submit(task)
        another_project=self.store.create_project({"name":"Second"})["project"]["id"]
        snapshot=self.g.status(another_project)
        other=dict(snapshot["definition"]);other["summary"]="Other scoped definition"
        # Other product has no body; fill it before proposal.
        other["body"]="Other contextual system boundary"
        second=self.g.propose(another_project,other,"Other change",
             ["other://review"],"Human",snapshot["definition_digest"])
        other_task=self.research.list(second["change"]["id"])["tasks"][0]
        with self.assertRaisesRegex(RuleError,"does not belong"):
            self.research.review(other_task["id"],candidate["submission_id"],1,
                                 "ACCEPT","Human","Cross-change attempt")

    def test_unreviewed_and_stale_technical_study_cannot_justify_definition(self):
        studies=Research(self.store)
        technical=studies.create({"project_id":self.project,
            "title":"CAN timing investigation","question":"CAN scheduling semantics",
            "constraints":"Real hardware evidence required",
            "minimum_level":"ANALYSIS"})["study"]["id"]
        task=self.task()
        with self.assertRaisesRegex(RuleError,"no current reviewed decision"):
            self.research.submit(task["id"],1,"AI","AI_CANDIDATE",
               "Technical claims",["lab://run"],"Not independently reviewed",
               technical)

    def test_reopened_technical_decision_invalidates_product_research_gate(self):
        engine=Research(self.store)
        opened=engine.create({"project_id":self.project,
             "title":"Independent interface study","question":"What timing behavior is observed?",
             "constraints":"Only test sources, no physical hardware",
             "minimum_level":"ANALYSIS"})
        study=opened["study"]["id"]
        opt=engine.add_option(study,{"title":"Profile A",
            "approach":"Describe measured behavior",
            "tradeoffs":"Limited to tested configuration"})
        option_id=opt["options"][0]["id"]
        planned=engine.add_experiment(study,{"option_id":option_id,
            "title":"Inspect recorded interface contract",
            "method":"Review source constraints and test specifications",
            "success_criteria":"All expected fields are accounted for",
            "environment":"Recorded configuration, no physical execution",
            "level":"ANALYSIS","required":True})
        eid=planned["experiments"][0]["id"]
        engine.authorize(eid,"Owner","Authorization for document analysis")
        result=engine.record_result(eid,{"outcome":"PASS",
             "observations":"All fields matched the fixture contract",
             "raw_reference":"test://source/reference",
             "configuration":"document revision 1",
             "source_revision":"fixture-revision-1",
             "performed_by":"Analyst"})
        rid=result["experiments"][0]["results"][0]["id"]
        engine.review_result(rid,"Owner","Observed evidence independently checked")
        engine.decide(study,{"option_id":option_id,"approver":"Owner",
              "rationale":"Document evidence supports this limited route",
              "restrictions":"Only the recorded configuration"})
        task=self.task("INTERFACES_HARDWARE")
        found=self.research.submit(task["id"],task["revision"],"AI","AI_CANDIDATE",
             "Interface findings from reviewed investigation",
             ["study://"+study],"No hardware validation",
             study)
        self.research.review(task["id"],found["submission_id"],
             task["revision"],"ACCEPT","Owner","Checked the active study decision")
        self.complete_research()
        for domain,_ in DOMAINS:
            self.g.review(self.change_id,domain,"NO_IMPACT",
                "Compared current baseline and scope",["test://fixture-review"],[],
                "Owner",self.change["change"]["base_digest"])
        self.assertTrue(self.g.gate(self.change_id)["ready"])
        engine.reopen(study,{"actor":"Owner","reason":"New evidence contradicts source contract"})
        self.assertFalse(self.g.gate(self.change_id)["ready"])
        self.assertTrue(any("technical investigation decision is stale" in reason
                            for reason in self.g.gate(self.change_id)["reasons"]))

    def test_export_and_restore_research_history_and_reject_broken_linkage(self):
        self.complete_research()
        for domain,_ in DOMAINS:
            self.g.review(self.change_id,domain,"NO_IMPACT",
                "Documented domain-specific scope review",["repo://product/analysis"],[],
                "Human",self.change["change"]["base_digest"])
        result=self.g.publish(self.change_id,"Human",
              "Published after all research and domain reviews",
              self.change["change"]["base_digest"])
        self.assertEqual(result["status"],"PUBLISHED")
        archived=self.store.export_project(self.project)
        self.assertEqual(len(archived["governance"]["research_tasks"]),10)
        self.assertEqual(len(archived["governance"]["research_submissions"]),10)
        self.assertEqual(len(archived["governance"]["research_decisions"]),10)
        self.assertEqual(preview(archived)["status"],"STRUCTURAL_PRECHECK_PASS")
        dest=self.root/"restored.db"
        restore_to_empty(archived,dest)
        reopened=Store(dest)
        self.assertEqual(reopened.export_project(self.project)["governance"],
                         archived["governance"])
        self.assertEqual(DefinitionGovernance(reopened).status(self.project)["baseline_status"],
                         "BASELINED")
        forged=json.loads(json.dumps(archived))
        forged["governance"]["research_submissions"][0]["task_id"]="fake"
        with self.assertRaisesRegex(RuleError,"missing task"):
            preview(forged)


if __name__=="__main__":
    unittest.main()
