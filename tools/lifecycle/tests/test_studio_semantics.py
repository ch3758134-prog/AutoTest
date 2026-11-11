"""Semantic guidance is AI-readable interpretation, never keyword policing."""
from pathlib import Path
import json
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_semantics import semantic_context, legacy_project_projection
from studio_semantic_evaluation import load_cases,review_template,summarize_reviews
from studio_store import Store
from studio_research import Research
from studio_execution import Execution
from studio_management import Management


class SemanticGuidanceTests(unittest.TestCase):
    def test_handbook_describes_concepts_and_counterexamples(self):
        handbook=semantic_context("product_identity,system_boundary,release_scope")
        self.assertEqual(len(handbook["sha256"]),64)
        self.assertEqual(len(handbook["concepts"]),3)
        for concept in handbook["concepts"]:
            self.assertTrue(concept["purpose"])
            self.assertTrue(concept["not_substitutes"])
            self.assertTrue(concept["questions"])
            self.assertTrue(concept["counterexamples"])
            self.assertTrue(concept["related"])
        # 'EPB' is an illustrative example, NOT a forbidden phrase list.
        self.assertIn("EPB",json.dumps(handbook,ensure_ascii=False))
        with self.assertRaisesRegex(ValueError,"Unknown engineering"):
            semantic_context("invented_concept")

    def test_legacy_projection_explicitly_separates_project_identity_from_metadata(self):
        project={"summary":"Integrated ECU test system",
                 "body":"G3/G4 reference and historical context",
                 "version":"R1","variant":"EPB bench"}
        view=legacy_project_projection(project)
        self.assertEqual(view["product_identity_candidate"],project["summary"])
        self.assertEqual(view["project_background_and_context"],project["body"])
        self.assertEqual(view["release_or_product_version_metadata"],"R1")
        self.assertIn("Do not interpret",view["warning"])

    def test_ai_management_context_includes_semantics_without_claiming_approval(self):
        with tempfile.TemporaryDirectory() as folder:
            store=Store(Path(folder)/"engineering.db")
            project=store.create_project({"name":"Test system",
               "summary":"Integrated ECU stimulus and response platform",
               "body":"R1 uses EPB as initial example","version":"R1"})["project"]["id"]
            manager=Management(store,Execution(store),Research(store))
            ctx=manager.context(project)
            self.assertEqual(ctx["engineering_semantics"]["schema"],
                             "engineering-semantic-handbook/1")
            self.assertEqual(ctx["legacy_project_interpretation"]
                             ["release_or_product_version_metadata"],"R1")
            self.assertIn("counterexamples",ctx["engineering_semantics"]["concepts"][0])
            self.assertTrue(any("Read engineering_semantics" in i
                                for i in ctx["advisory_instructions"]))

    def test_evaluation_cases_are_not_keyword_scores_and_require_real_human_review(self):
        cases=load_cases()
        self.assertGreaterEqual(len(cases["cases"]),12)
        template=review_template()
        self.assertEqual(len(template["responses"]),len(cases["cases"]))
        with self.assertRaisesRegex(ValueError,"Human reviewer identity"):
            summarize_reviews(template)
        template["reviewer"]="Explicit human reviewer"
        with self.assertRaisesRegex(ValueError,"human scores"):
            summarize_reviews(template)
        for entry in template["responses"]:
            entry["answer"]="Model response manually captured"
            entry["notes"]="Evidence and context were carefully reviewed"
            entry["evidence_quote"]="Manually quoted model response"
            for dimension in entry["scores"]:
                entry["scores"][dimension]=2
        summary=summarize_reviews(template)
        self.assertEqual(summary["assessed_cases"],len(cases["cases"]))
        self.assertTrue(all(v["score"]==2*len(cases["cases"])
                            for v in summary["by_dimension"].values()))
        template["responses"][0]["scores"]["intent_and_facts"]=99
        with self.assertRaisesRegex(ValueError,"human scores"):
            summarize_reviews(template)


if __name__=="__main__":
    unittest.main()
