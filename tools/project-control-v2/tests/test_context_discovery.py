import importlib.util
from pathlib import Path
import unittest

path = Path(__file__).resolve().parents[1] / "portable" / "context_discovery.py"
spec = importlib.util.spec_from_file_location("context_discovery", path)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def sample():
    return {"schema": m.SCHEMA, "role": "coordinator", "project_ref": "owner/repo",
            "task_focus": "接管", "source_revision": "current",
            "sources": [], "topics": {
                k: {"status": "UNKNOWN", "source_ids": [], "reason": "unsearched"}
                for k in m.TOPICS}, "claims": [], "next_disposition": "CONTINUE_RESEARCH"}


class DiscoveryTests(unittest.TestCase):
    def test_open_unknown_not_authorized(self):
        result = m.audit(sample())
        self.assertEqual(len(result["unresolved_topics"]), len(m.TOPICS))
        self.assertFalse(result["authorization_granted"])

    def test_cannot_cover_without_source(self):
        v = sample()
        v["topics"]["purpose"] = {"status": "COVERED", "source_ids": []}
        with self.assertRaises(m.DiscoveryError):
            m.audit(v)

    def test_missing_topic_rejected(self):
        v = sample()
        v["topics"].pop("history_decisions")
        with self.assertRaises(m.DiscoveryError):
            m.audit(v)

    def test_provenance(self):
        v = sample()
        v["sources"] = [{"id": "s1", "locator": "docs/product.md@sha", "type": "document"}]
        v["topics"]["purpose"] = {"status": "COVERED", "source_ids": ["s1"]}
        self.assertEqual(m.audit(v)["unresolved_topics"].count("purpose"), 0)

    def test_false_complete(self):
        v = sample()
        v["claims_project_complete"] = True
        with self.assertRaises(m.DiscoveryError):
            m.audit(v)


if __name__ == "__main__":
    unittest.main()
