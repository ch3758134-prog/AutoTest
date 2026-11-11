"""Non-bypassable local product-definition governance business contract tests."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_governance import DefinitionGovernance,DOMAINS,POLICY_VERSION


class ProductDefinitionGovernanceTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store=Store(Path(self.tmp.name)/"governance.db")
        self.project=self.store.create_project({
            "name":"Generic platform","summary":"Engineering workspace for test and validation",
            "body":"Supports developer and operator workflows, offline, no specific ECU requirement.",
            "version":"R0","variant":"generic"})["project"]
        self.g=DefinitionGovernance(self.store)

    def proposal(self,project_id=None,reference=None):
        project_id=project_id or self.project["id"]
        status=self.g.status(project_id)
        proposed=dict(status["definition"])
        proposed["summary"]="IDE oriented test and validation development platform"
        proposed["body"]="Users prepare projects, develop and debug tests, then run validated production recipes."
        return self.g.propose(project_id,proposed,
            "Product positioning requires structured revision and downstream re-evaluation",
            ["repo://design/research","discussion://product/decision"],
            "Owner",reference or status["definition_digest"])

    def fill(self,change,impacts=None):
        impacts=impacts or {}
        for domain,_ in DOMAINS:
            ids=impacts.get(domain,[])
            self.g.review(change["change"]["id"],domain,"IMPACT" if ids else "NO_IMPACT",
                          "Reviewed current product design and the domain boundary",
                          ["engineering://review/"+domain],
                          ids,"Owner",change["change"]["base_digest"])

    def test_imported_project_is_not_silently_approved(self):
        info=self.g.status(self.project["id"])
        self.assertEqual(info["baseline_status"],"UNBASELINED")
        self.assertIsNone(info["baseline"])
        with self.assertRaisesRegex(RuleError,"governed"):
            self.store.update_project(self.project["id"],{"body":"Unchecked replacement"})
        change=self.proposal()
        self.assertFalse(change["gate"]["ready"])
        self.assertEqual(len(change["gate"]["missing_domains"]),len(DOMAINS))
        with self.assertRaisesRegex(RuleError,"blocked"):
            self.g.publish(change["change"]["id"],"Owner","Approve without study",
                           change["change"]["base_digest"])
        self.assertEqual(self.store.project(self.project["id"])["project"]["version"],"R0")

    def test_complete_review_publishes_versioned_baseline_once(self):
        change=self.proposal()
        self.fill(change)
        gate=self.g.gate(change["change"]["id"])
        self.assertEqual(gate["reviewed"],len(DOMAINS))
        self.assertTrue(gate["ready"])
        publication=self.g.publish(change["change"]["id"],"Human","Approved after domain reviews",
                                   change["change"]["base_digest"])
        self.assertEqual(publication["status"],"PUBLISHED")
        info=self.g.status(self.project["id"])
        self.assertEqual(info["baseline_status"],"BASELINED")
        self.assertEqual(info["baseline"]["revision"],1)
        self.assertEqual(info["baseline"]["digest"],publication["digest"])
        self.assertEqual(info["policy_version"],POLICY_VERSION)
        self.assertEqual(info["definition"]["summary"],
                         "IDE oriented test and validation development platform")
        with self.assertRaisesRegex(RuleError,"blocked"):
            self.g.publish(change["change"]["id"],"Human","Try publishing twice",
                           change["change"]["base_digest"])
        with self.assertRaisesRegex(RuleError,"governed"):
            self.store.update_project(self.project["id"],{"summary":"Unauthorized edit"})

    def test_unknown_and_unmapped_impact_block_release(self):
        change=self.proposal()
        self.fill(change)
        self.g.review(change["change"]["id"],"ARCHITECTURE","UNKNOWN",
                      "Insufficient design evidence",["study://pending"],[],
                      "Reviewer",change["change"]["base_digest"])
        reasons=self.g.gate(change["change"]["id"])["reasons"]
        self.assertTrue(any("ARCHITECTURE" in x for x in reasons))
        with self.assertRaisesRegex(RuleError,"cannot be published without"):
            self.g.review(change["change"]["id"],"ARCHITECTURE","IMPACT",
                          "Detected architecture change",["study://mapping"],[],
                          "Reviewer",change["change"]["base_digest"])
        with self.assertRaisesRegex(RuleError,"Evidence|reference|traceable"):
            self.g.review(change["change"]["id"],"ARCHITECTURE","NO_IMPACT",
                          "All good",[],[],"Reviewer",change["change"]["base_digest"])
        self.assertFalse(self.g.gate(change["change"]["id"])["ready"])

    def test_review_stales_when_affected_object_revision_changes(self):
        target=self.store.create_object({"project_id":self.project["id"],
             "kind":"requirement","title":"Target requirement","body":"Old behavior"})["item"]
        change=self.proposal()
        self.fill(change,{"CAPABILITIES":[target["id"]]})
        self.assertTrue(self.g.gate(change["change"]["id"])["ready"])
        self.store.update_object(target["id"],{"expected_revision":1,"body":"New requirement"})
        self.assertFalse(self.g.gate(change["change"]["id"])["ready"])
        with self.assertRaisesRegex(RuleError,"blocked"):
            self.g.publish(change["change"]["id"],"Owner","Decision",
                           change["change"]["base_digest"])
        self.g.review(change["change"]["id"],"CAPABILITIES","IMPACT",
                      "Updated requirement checked",["log://new"],[target["id"]],
                      "Owner",change["change"]["base_digest"])
        self.assertTrue(self.g.gate(change["change"]["id"])["ready"])

    def test_two_concurrent_changes_cannot_overwrite_one_another(self):
        first=self.proposal()
        second=self.proposal()
        self.fill(first)
        self.fill(second)
        self.g.publish(first["change"]["id"],"Owner","First accepted",
                       first["change"]["base_digest"])
        self.assertFalse(self.g.gate(second["change"]["id"])["ready"])
        with self.assertRaisesRegex(RuleError,"blocked"):
            self.g.publish(second["change"]["id"],"Owner","Try stale",
                           second["change"]["base_digest"])
        with self.assertRaisesRegex(RuleError,"baseline changed"):
            self.g.review(second["change"]["id"],"VERIFICATION","NO_IMPACT",
                          "Stale review",["evidence://x"],[],"Owner",
                          second["change"]["base_digest"])

    def test_reject_seals_proposal_without_changing_product(self):
        change=self.proposal()
        detail=self.g.reject(change["change"]["id"],"Owner","Research not sufficient")
        self.assertEqual(detail["change"]["state"],"REJECTED")
        self.assertEqual(self.g.status(self.project["id"])["baseline_status"],"UNBASELINED")
        with self.assertRaisesRegex(RuleError,"sealed"):
            self.g.review(change["change"]["id"],"ARCHITECTURE","NO_IMPACT",
                          "test",["reference://1"],[],"Owner",change["change"]["base_digest"])

    def test_impact_invalidation_targets_only_reviewed_downstream(self):
        req1=self.store.create_object({"project_id":self.project["id"],
            "kind":"requirement","title":"Impacted requirement","body":"A"})["item"]
        req2=self.store.create_object({"project_id":self.project["id"],
            "kind":"requirement","title":"Unaffected requirement","body":"B"})["item"]
        for item in (req1,req2):
            self.store.approve(item["id"],1,"Owner","Approved requirement")
        work=[]
        for idx,req in enumerate((req1,req2)):
            obj=self.store.create_object({"project_id":self.project["id"],
             "kind":"work_package","title":"Implementation "+str(idx),
             "body":"Implementation","meta":{"source_sha":"a"*40,
                "file_scope":["src/module"+str(idx)+".cs"],
                "acceptance":"Unit and integration test pass",
                "input_revisions":{req["id"]:2}}})["item"]
            r=self.store.create_relation({"source_id":req["id"],
                  "target_id":obj["id"],"kind":"depends"})
            self.store.approve_relation(r["id"],"Owner","Reviewed product requirement")
            self.store.approve(obj["id"],1,"Owner","CODE_READY inputs reviewed")
            work.append(obj)
        self.assertTrue(self.store.readiness(work[0]["id"])["ready"])
        self.assertTrue(self.store.readiness(work[1]["id"])["ready"])
        change=self.proposal()
        self.fill(change,{"CAPABILITIES":[req1["id"]]})
        for obj,outcome in ((work[0],"IMPACT"),(work[1],"NO_IMPACT")):
            self.g.review_package(change["change"]["id"],obj["id"],outcome,
                "Independent product impact evaluation for this implementation",
                ["test://implementation-review"],"Owner",
                change["change"]["base_digest"],2)
        result=self.g.publish(change["change"]["id"],"Owner","Impacted path confirmed",
                              change["change"]["base_digest"])
        self.assertEqual([x["id"] for x in result["invalidated_work_packages"]],
                         [work[0]["id"]])
        self.assertFalse(self.store.readiness(work[0]["id"])["ready"])
        self.assertTrue(self.store.readiness(work[1]["id"])["ready"])
        with self.assertRaisesRegex(RuleError,"revision changed"):
            self.g.resolve(work[0]["id"],change["change"]["id"],1,
                           "Owner","Old revision cannot be accepted")
        self.g.resolve(work[0]["id"],change["change"]["id"],2,
                       "Owner","Reviewed approved work package against new product definition")
        self.assertTrue(self.store.readiness(work[0]["id"])["ready"])

    def test_missing_implementation_review_is_a_hard_gate_even_without_graph(self):
        orphan=self.store.create_object({"project_id":self.project["id"],
            "kind":"work_package","title":"Orphan implementation","body":"No mapping recorded"})["item"]
        change=self.proposal()
        self.fill(change)
        gate=self.g.gate(change["change"]["id"])
        self.assertFalse(gate["ready"])
        self.assertEqual(gate["package_total"],1)
        self.assertTrue(any("individually reviewed" in r for r in gate["reasons"]))
        with self.assertRaisesRegex(RuleError,"blocked"):
            self.g.publish(change["change"]["id"],"Owner","Unsafe approval",
                           change["change"]["base_digest"])
        self.g.review_package(change["change"]["id"],orphan["id"],"UNKNOWN",
            "Not enough information",["review://pending"],"Owner",
            change["change"]["base_digest"],1)
        self.assertFalse(self.g.gate(change["change"]["id"])["ready"])
        self.g.review_package(change["change"]["id"],orphan["id"],"NO_IMPACT",
            "Checked actual implementation and found no product semantic coupling",
            ["review://independent-module"],"Owner",
            change["change"]["base_digest"],1)
        self.assertTrue(self.g.gate(change["change"]["id"])["ready"])
        self.store.update_object(orphan["id"],{"expected_revision":1,"body":"Revised source boundary"})
        self.assertFalse(self.g.gate(change["change"]["id"])["ready"])

    def test_relationship_conflict_rejects_false_no_impact(self):
        req=self.store.create_object({"project_id":self.project["id"],
            "kind":"requirement","title":"Affected business rule","body":"Old rule"})["item"]
        wp=self.store.create_object({"project_id":self.project["id"],
            "kind":"work_package","title":"Downstream implementation","body":"Old integration"})["item"]
        link=self.store.create_relation({"source_id":req["id"],"target_id":wp["id"],"kind":"depends"})
        self.store.approve_relation(link["id"],"Owner","Reviewed requirement dependency")
        change=self.proposal()
        self.fill(change,{"CAPABILITIES":[req["id"]]})
        self.g.review_package(change["change"]["id"],wp["id"],"NO_IMPACT",
            "Contradicts graph",["repo://affected-code"],"Owner",
            change["change"]["base_digest"],1)
        reasons=self.g.gate(change["change"]["id"])["reasons"]
        self.assertTrue(any("conflicts with reviewed dependency graph" in x for x in reasons))
        self.g.review_package(change["change"]["id"],wp["id"],"IMPACT",
            "Corrected after dependency review",["repo://affected-code"],"Owner",
            change["change"]["base_digest"],1)
        self.assertTrue(self.g.gate(change["change"]["id"])["ready"])

    def test_cross_project_impact_is_forbidden(self):
        other=self.store.create_project({"name":"Other","summary":"Other product",
                                           "body":"Other project history"})["project"]
        foreign=self.store.create_object({"project_id":other["id"],
               "kind":"requirement","title":"Foreign","body":"Not linked"})["item"]
        change=self.proposal()
        with self.assertRaisesRegex(RuleError,"Cross-project"):
            self.g.review(change["change"]["id"],"CAPABILITIES","IMPACT",
                 "Potential external impact",["link://outside"],[foreign["id"]],
                 "Owner",change["change"]["base_digest"])

    def test_export_contains_history_and_revalidation(self):
        change=self.proposal()
        self.fill(change)
        self.g.publish(change["change"]["id"],"Owner","Approved complete definition review",
                       change["change"]["base_digest"])
        exported=self.store.export_project(self.project["id"])
        self.assertEqual(len(exported["governance"]["baselines"]),1)
        self.assertEqual(len(exported["governance"]["changes"]),1)
        self.assertEqual(len(exported["governance"]["reviews"]),len(DOMAINS))
        self.assertEqual(exported["governance"]["baselines"][0]["revision"],1)


if __name__=="__main__":
    unittest.main()
