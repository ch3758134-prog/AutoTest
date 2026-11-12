"""Read-first AI onboarding contract: user does not have to author a role prompt."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from ai.onboard import decide


class SelfBootstrappingAiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        self_root = ROOT / "AI_START_HERE.md"
        cls.entry = self_root.read_text(encoding="utf-8")
        cls.bootstrap = (HERE / "ai/BOOTSTRAP.md").read_text(encoding="utf-8")
        cls.roles = json.loads((HERE / "ai/roles.json").read_text(encoding="utf-8"))
        cls.projects = json.loads((HERE / "ai/projects.json").read_text(encoding="utf-8"))
        cls.appointments = json.loads((HERE / "ai/appointments/autotest.json").read_text(encoding="utf-8"))

    def test_single_entrypoint_is_in_repository_root(self):
        self.assertIn("tools/project-control-v2/ai/BOOTSTRAP.md", self.entry)
        self.assertIn("tools/project-control-v2/ai/roles.json", self.entry)
        self.assertIn("tools/project-control-v2/ai/projects.json", self.entry)
        self.assertIn("不需要用户先", self.entry)

    def test_projects_reference_existing_real_documents(self):
        self.assertEqual(self.projects["schema"], "project-control-project-index/1")
        self.assertIsNone(self.projects["project_selection"]["default_project"])
        for p in self.projects["projects"]:
            self.assertEqual(p["id"], "autotest")
            for field in ("management_record", "engineering_source", "process_reference",
                          "role_appointments"):
                self.assertTrue((ROOT / p[field]).is_file(), field)

    def test_roles_are_realistic_not_automatic_ai_agents(self):
        roles = self.roles["roles"]
        names = [r["id"] for r in roles]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual(set(names), {"coordinator", "specialist", "integration",
                                     "reviewer", "local_executor"})
        self.assertIsNone(self.roles["adoption"]["default_role"])
        self.assertTrue(self.roles["adoption"]["never_demand_user_write_a_role_prompt"])
        self.assertTrue(self.roles["adoption"]["no_implicit_code_authority"])
        self.assertTrue(self.roles["adoption"]["first_reader_does_not_become_coordinator"])
        for role in roles:
            self.assertTrue(role["responsibilities"])
            self.assertTrue(role["must_not_assume"])
            self.assertTrue(role["expected_handoffs"])

    def test_existing_pilot_has_no_confirmed_conversation_role(self):
        self.assertEqual(self.appointments["appointments"], [])
        self.assertTrue(self.appointments["identity_policy"]["confirm_with_user_when_ambiguous"])
        r = decide(self.projects, self.roles, {"autotest": self.appointments})
        self.assertEqual(r["project_id"], "autotest")
        self.assertEqual(r["action"], "ASK_ROLE")
        self.assertIsNone(r["role_id"])
        self.assertIn("希望", r["question"])

    def test_empty_project_first_asks_project_not_auto_coordinator(self):
        empty = {"projects": []}
        r = decide(empty, self.roles, {})
        self.assertEqual(r["action"], "ASK_PROJECT")
        self.assertIsNone(r["role_id"])

    def test_unknown_project_proposes_setup_but_no_created_project(self):
        r = decide(self.projects, self.roles, {}, project_id="brakesync")
        self.assertEqual(r["action"], "ASK_PROJECT_SETUP")
        self.assertIsNone(r["role_id"])

    def test_explicit_human_role_needs_no_more_identity_questions(self):
        for role in ("coordinator", "specialist", "integration", "reviewer", "local_executor"):
            with self.subTest(role=role):
                r = decide(self.projects, self.roles, {}, user_confirmed_role=role)
                self.assertEqual(r["action"], "READY_WITH_EXPLICIT_ROLE")
                self.assertEqual(r["role_id"], role)
        with self.assertRaises(ValueError):
            decide(self.projects, self.roles, {}, user_confirmed_role="super-agent")

    def test_explicit_task_can_resolve_role_only_from_confirmed_appointment(self):
        known = {"project_id": "autotest", "appointments": [
            {"appointment_id": "test-slot", "role_id": "specialist",
             "scope": "test-program", "task_ids": ["work-123"],
             "human_confirmation_reference": "confirmed conversation note"}]}
        r = decide(self.projects, self.roles, {"autotest": known},
                   user_selected_task="work-123")
        self.assertEqual(r["action"], "READY_WITH_TASK_ROLE")
        self.assertEqual(r["role_id"], "specialist")
        unconfirmed = copy.deepcopy(known)
        unconfirmed["appointments"][0]["human_confirmation_reference"] = ""
        r = decide(self.projects, self.roles, {"autotest": unconfirmed},
                   user_selected_task="work-123")
        self.assertEqual(r["action"], "ASK_ROLE")
        not_registered = decide(self.projects, self.roles, {},
                                user_selected_task="work-new")
        self.assertEqual(not_registered["action"], "ASK_ROLE")

    def test_contradictory_task_appointments_require_human_decision(self):
        slots = {"appointments": [
            {"role_id": "specialist", "task_ids": ["work-1"],
             "human_confirmation_reference": "yes"},
            {"role_id": "integration", "task_ids": ["work-1"],
             "human_confirmation_reference": "yes"}]}
        r = decide(self.projects, self.roles, {"autotest": slots},
                   user_selected_task="work-1")
        self.assertEqual(r["action"], "ASK_ROLE")
        self.assertIsNone(r["role_id"])

    def test_bootstrap_explicitly_requires_agent_self_initiative(self):
        for term in ("空项目", "身份未确认", "任务已指明", "主动询问",
                     "不自动成为项目经理", "不需要向用户重复整份手册"):
            self.assertIn(term, self.bootstrap)


if __name__ == "__main__":
    unittest.main()
