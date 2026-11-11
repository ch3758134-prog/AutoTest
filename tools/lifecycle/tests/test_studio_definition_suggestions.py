"""Product-definition proposals can originate from AI without AI publishing a baseline."""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_governance import DefinitionGovernance
from studio_definition_suggestions import DefinitionSuggestions
from studio_definition_research import DefinitionResearch
from studio_restore import restore_to_empty

class SuggestionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder=Path(self.temp.name)
        self.store=Store(self.folder/"source.db")
        self.project=self.store.create_project({
            "name":"Engineering Product","summary":"Current generic engineering objective",
            "body":"Different users conduct research, implementation and validation",
            "version":"R1","variant":"all"})["project"]["id"]
        self.governance=DefinitionGovernance(self.store)
        self.suggestions=DefinitionSuggestions(self.store)
        self.info=self.governance.status(self.project)
        self.draft=dict(self.info["definition"])
        self.draft["summary"]="A more explicit test development and production workspace"

    def submit(self):
        return self.suggestions.suggest(
            self.project,self.draft,"Current scope omits the engineering user journey",
            ["repo://product/requirements"],"AI manager","AI_CANDIDATE",
            self.info["definition_digest"])

    def test_ai_suggestion_is_inert_until_owner_acceptance(self):
        saved=self.submit()
        self.assertEqual(saved["status"],"PENDING")
        self.assertEqual(self.governance.status(self.project)["changes"],[])
        self.assertEqual(self.governance.status(self.project)["definition"],
                         self.info["definition"])
        accepted=self.suggestions.accept(saved["id"],"Human owner",
             "Open full cross-domain impact research",self.info["definition_digest"])
        self.assertEqual(accepted["state"],"ACCEPTED_FOR_RESEARCH")
        self.assertEqual(len(self.governance.status(self.project)["changes"]),1)
        queue=DefinitionResearch(self.store).list(accepted["change_id"])
        self.assertEqual(len(queue["tasks"]),10)
        self.assertEqual(self.governance.status(self.project)["definition"],
                         self.info["definition"])
        with self.assertRaisesRegex(RuleError,"already been reviewed"):
            self.suggestions.accept(saved["id"],"Human","Repeat",
                                    self.info["definition_digest"])

    def test_rejection_preserves_history_without_activating_change(self):
        saved=self.submit()
        rejected=self.suggestions.reject(saved["id"],"Human",
              "Product behavior is not yet confirmed")
        self.assertEqual(rejected["state"],"REJECTED")
        self.assertFalse(self.governance.status(self.project)["changes"])
        with self.assertRaisesRegex(RuleError,"already been reviewed"):
            self.suggestions.reject(saved["id"],"Human","Second review")

    def test_stale_suggestion_cannot_be_promoted(self):
        first=self.submit()
        second=self.submit()
        self.suggestions.accept(first["id"],"Human","Approved for investigation",
                                self.info["definition_digest"])
        # Publishing a separately reviewed product definition will invalidate
        # suggestions bound to the old product content.
        current=self.governance.status(self.project)
        changed=dict(current["definition"])
        changed["summary"]="Different approved new baseline"
        # Trusted owner direct change route retains the same full research gate,
        # so emulate a changed local baseline only to test stale rejection.
        with self.store.db() as db:
            db.execute("UPDATE projects SET summary=? WHERE id=?",
                       ("Unexpected external edit",self.project))
        with self.assertRaisesRegex(RuleError,"baseline has changed"):
            self.suggestions.accept(second["id"],"Human","Old snapshot",
                                    self.info["definition_digest"])

    def test_archive_preserves_ai_proposal_and_owner_triage(self):
        saved=self.submit()
        self.suggestions.reject(saved["id"],"Human","Needs more factual detail")
        archive=self.store.export_project(self.project)
        self.assertEqual(len(archive["governance"]["suggestions"]),1)
        dest=self.folder/"restored.db"
        restore_to_empty(archive,dest)
        recovered=Store(dest)
        self.assertEqual(recovered.export_project(self.project)["governance"]["suggestions"],
                         archive["governance"]["suggestions"])


if __name__=="__main__":
    unittest.main()
