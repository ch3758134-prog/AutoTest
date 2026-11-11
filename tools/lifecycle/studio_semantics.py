"""Versioned engineering concept guidance for AI and human review.

This is explanatory knowledge, not a keyword classifier, approval rule or
claim about the factual state of any project. Selection is contextual: a model
may reference multiple concepts or explain why a concept is irrelevant.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path

CATALOG_PATH = Path(__file__).with_name("engineering_semantics.json")
DEFAULT_FOCUS = ("product_identity", "product_purpose", "system_boundary",
                 "release_scope", "configuration", "verification_evidence", "decision")


def load_catalog():
    raw = CATALOG_PATH.read_bytes()
    source = json.loads(raw.decode("utf-8"))
    if source.get("schema") != "engineering-semantic-handbook/1":
        raise ValueError("Unsupported engineering semantic handbook schema")
    ids = [x["id"] for x in source["concepts"]]
    if len(set(ids)) != len(ids):
        raise ValueError("Duplicate engineering semantic concept ID")
    return source, hashlib.sha256(raw).hexdigest()


def semantic_context(focus=None):
    catalog, sha = load_catalog()
    all_concepts = {x["id"]: x for x in catalog["concepts"]}
    if focus is None or focus == "":
        selected = tuple(DEFAULT_FOCUS)
    elif focus == "all":
        selected = tuple(all_concepts)
    elif isinstance(focus, str):
        selected = tuple(part.strip() for part in focus.split(",") if part.strip())
    else:
        selected = tuple(focus)
    selected = tuple(dict.fromkeys(selected))
    unknown = [key for key in selected if key not in all_concepts]
    if unknown:
        raise ValueError("Unknown engineering semantic concept(s): " + ", ".join(unknown))
    return {
        "schema": catalog["schema"],
        "edition": catalog["edition"],
        "sha256": sha,
        "purpose": catalog["purpose"],
        "principles": catalog["principles"],
        "concepts": [all_concepts[key] for key in selected],
        "ai_workflow": catalog["ai_workflow"],
        "review_questions": catalog["review_questions"],
        "context_note": (
            "These are examples and distinctions for interpretation, never forbidden "
            "words, content length quotas or evidence of project approval. "
            "Read source facts and ask why a statement belongs at a particular level."
        ),
    }


def legacy_project_projection(project):
    """Clarify legacy database columns without altering existing user records.

    Old projects store four columns together. Those columns are NOT four equally
    normative parts of the product identity. Schema migration needs separate,
    owner-reviewed versioning; this projection is non-destructive.
    """
    return {
        "product_identity_candidate": project.get("summary", ""),
        "project_background_and_context": project.get("body", ""),
        "release_or_product_version_metadata": project.get("version", ""),
        "configuration_or_variant_metadata": project.get("variant", ""),
        "warning": (
            "Legacy summary/body/version/variant were versioned together. "
            "Do not interpret stage version, configuration or lengthy background as "
            "permanent product identity. Reclassify semantics before publishing."
        ),
    }
