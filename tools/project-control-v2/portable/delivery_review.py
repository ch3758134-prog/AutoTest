#!/usr/bin/env python3
"""Human-readable candidate delivery review with explicit owner release.

A saved draft is NOT a delivered artifact. Owner review of the complete immutable
version allows only candidate publication, never product approval or execution.
Actual authenticated publication is outside this module.
"""
from __future__ import annotations
import copy
import hashlib
import json
import re

SCHEMA = "project-control-delivery-review/1"
MAX_BODY = 30000
MAX_RECORDS = 64
MAX_HISTORY = 12
ID = re.compile(r"^[A-Za-z][A-Za-z0-9_.-]{1,99}$")
KINDS = {"RESEARCH", "REQUIREMENTS", "DESIGN", "PLAN", "MANAGEMENT", "TEST_RESULT", "RELEASE_CANDIDATE"}
PUBLICATION = {"NO_EXTERNAL_PUBLICATION", "GITHUB_CANDIDATE_COMMENT",
               "GITHUB_DRAFT_PR", "VERSIONED_PROJECT_ARTIFACT"}
STATES = {"DRAFT_INTERNAL", "AWAITING_OWNER_REVIEW", "RETURNED_FOR_REVISION",
          "ON_HOLD", "RELEASED_FOR_CANDIDATE_PUBLICATION"}
OWNER_DECISIONS = {"REQUEST_CHANGES", "HOLD", "ALLOW_CANDIDATE_PUBLICATION"}
CONTENT_FIELDS = ("kind", "title", "why", "body", "scope", "changes", "limitations",
                  "owner_question", "publication_type", "publication_target", "source_refs")

class DeliveryError(ValueError):
    pass

def required(value, label, limit=3000):
    if not isinstance(value, str) or not value.strip() or len(value) > limit:
        raise DeliveryError("missing or oversized " + label)
    return value.strip()

def ident(value, label):
    x = required(value, label, 100)
    if not ID.fullmatch(x):
        raise DeliveryError("invalid " + label)
    return x

def content_digest(obj):
    data = {key: obj[key] for key in CONTENT_FIELDS}
    raw = json.dumps(data, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()

def normalize(payload):
    if not isinstance(payload, dict):
        raise DeliveryError("delivery payload must be object")
    kind = payload.get("kind")
    if kind not in KINDS:
        raise DeliveryError("unsupported delivery kind")
    publication_type = payload.get("publication_type")
    if publication_type not in PUBLICATION:
        raise DeliveryError("unsupported publication plan")
    refs = payload.get("source_refs")
    if not isinstance(refs, list) or not refs or len(refs) > 32:
        raise DeliveryError("delivery requires source references")
    refs = [required(x, "source reference", 500) for x in refs]
    target = required(payload.get("publication_target"), "intended audience and location", 400)
    obj = {
        "kind": kind,
        "title": required(payload.get("title"), "title", 240),
        "why": required(payload.get("why"), "why this work matters", 2000),
        "body": required(payload.get("body"), "full candidate body", MAX_BODY),
        "scope": required(payload.get("scope"), "precise review scope", 2000),
        "changes": required(payload.get("changes"), "difference from prior approved version", 3000),
        "limitations": required(payload.get("limitations"), "unverified facts and risks", 3000),
        "owner_question": required(payload.get("owner_question"), "specific owner decision or review request", 1500),
        "publication_type": publication_type,
        "publication_target": target,
        "source_refs": refs,
    }
    return obj

def items(record):
    values = record.get("delivery_reviews", [])
    if not isinstance(values, list) or len(values) > MAX_RECORDS:
        raise DeliveryError("invalid delivery collection")
    seen = set()
    for row in values:
        if not isinstance(row, dict):
            raise DeliveryError("invalid delivery record")
        rid = ident(row.get("id"), "delivery id")
        if rid in seen:
            raise DeliveryError("duplicate delivery id")
        seen.add(rid)
        if row.get("status") not in STATES or type(row.get("revision")) is not int or row["revision"] < 1:
            raise DeliveryError("invalid delivery revision/state")
        if len(row.get("history", [])) > MAX_HISTORY or not isinstance(row.get("history"), list):
            raise DeliveryError("invalid delivery history")
        if not isinstance(row.get("review_history"), list) or len(row["review_history"]) > 32:
            raise DeliveryError("invalid owner review history")
        if content_digest(row) != row.get("sha256"):
            raise DeliveryError("delivery content differs from recorded review fingerprint")
        if row.get("status") == "RELEASED_FOR_CANDIDATE_PUBLICATION":
            v = row.get("owner_release")
            if (not isinstance(v, dict) or v.get("sha256") != row["sha256"]
                    or v.get("revision") != row["revision"]
                    or v.get("decision") != "ALLOW_CANDIDATE_PUBLICATION"):
                raise DeliveryError("candidate released without matching owner review")
        if row.get("status") != "RELEASED_FOR_CANDIDATE_PUBLICATION" and row.get("owner_release"):
            raise DeliveryError("unreleased delivery must not retain active owner release")
    return values

def find(values, delivery_id):
    return next((x for x in values if x["id"] == delivery_id), None)

def apply(record, action, payload, *, at, owner_channel=False):
    if not isinstance(payload, dict):
        raise DeliveryError("delivery payload must be object")
    values = items(record)
    if action == "stage_delivery":
        rid = ident(payload.get("id"), "delivery id")
        if find(values, rid):
            raise DeliveryError("delivery id already used")
        if len(values) >= MAX_RECORDS:
            raise DeliveryError("delivery history full; archive explicitly")
        data = normalize(payload)
        row = dict(data, id=rid, revision=1, status="DRAFT_INTERNAL",
                   sha256=content_digest(data), history=[], review_history=[],
                   owner_release=None, created_at=at, updated_at=at)
        values.append(row)
        record["delivery_reviews"] = values
        return {"id": rid, "revision": 1, "sha256": row["sha256"],
                "status": row["status"], "publicly_delivered": False}
    rid = ident(payload.get("id"), "delivery id")
    row = find(values, rid)
    if row is None:
        raise DeliveryError("unknown delivery")
    if payload.get("delivery_revision") != row["revision"] or payload.get("expected_sha256") != row["sha256"]:
        raise DeliveryError("stale delivery: read exact current version before acting")
    if action == "revise_delivery":
        if row["status"] not in ("DRAFT_INTERNAL", "AWAITING_OWNER_REVIEW",
                                 "RETURNED_FOR_REVISION", "ON_HOLD",
                                 "RELEASED_FOR_CANDIDATE_PUBLICATION"):
            raise DeliveryError("delivery cannot be revised")
        if len(row["history"]) >= MAX_HISTORY:
            raise DeliveryError("version archive full")
        data = normalize(payload)
        fingerprint = content_digest(data)
        if fingerprint == row["sha256"]:
            raise DeliveryError("no content change to revise")
        # Archive all fields whose previous owner decision may have referenced them.
        row["history"].append({"revision": row["revision"], "sha256": row["sha256"],
                               "content": {k: copy.deepcopy(row[k]) for k in CONTENT_FIELDS},
                               "previous_status": row["status"], "at": at})
        row.update(data)
        row["revision"] += 1
        row["sha256"] = fingerprint
        row["status"] = "DRAFT_INTERNAL"
        row["owner_release"] = None
        row["updated_at"] = at
        return {"id": rid, "revision": row["revision"], "sha256": fingerprint,
                "status": "DRAFT_INTERNAL", "publicly_delivered": False}
    if action == "offer_delivery_review":
        if row["status"] != "DRAFT_INTERNAL":
            raise DeliveryError("only the current internal draft can enter owner review")
        row["status"] = "AWAITING_OWNER_REVIEW"
        row["updated_at"] = at
        # A queue entry is not proof that the owner has seen or read this packet.
        return {"id": rid, "revision": row["revision"], "sha256": row["sha256"],
                "status": row["status"], "owner_has_read": False, "publicly_delivered": False}
    if action == "owner_decide_delivery":
        if not owner_channel:
            raise DeliveryError("owner decision requires separate review channel")
        if row["status"] != "AWAITING_OWNER_REVIEW":
            raise DeliveryError("candidate not awaiting owner review")
        decision = payload.get("decision")
        if decision not in OWNER_DECISIONS:
            raise DeliveryError("invalid owner review decision")
        if payload.get("confirm_exact_content") is not True:
            raise DeliveryError("owner must explicitly confirm this exact candidate version")
        reason = required(payload.get("reason"), "owner review reason", 1000)
        entry = {"revision": row["revision"], "sha256": row["sha256"],
                 "decision": decision, "reason": reason, "at": at,
                 "authority": "LOCAL_REVIEW_TOKEN_NOT_VERIFIED_FORMAL_IDENTITY",
                 "formal_product_approval": False, "execution_authorization": False}
        row["review_history"].append(entry)
        row["status"] = {"REQUEST_CHANGES": "RETURNED_FOR_REVISION",
                         "HOLD": "ON_HOLD",
                         "ALLOW_CANDIDATE_PUBLICATION": "RELEASED_FOR_CANDIDATE_PUBLICATION"}[decision]
        row["owner_release"] = entry if decision == "ALLOW_CANDIDATE_PUBLICATION" else None
        row["updated_at"] = at
        return {"id": rid, "status": row["status"], "revision": row["revision"],
                "sha256": row["sha256"], "formal_product_approval": False,
                "execution_authorization": False}
    raise DeliveryError("unsupported delivery operation")

def candidate_publication_permitted(record, delivery_id, sha256):
    row = find(items(record), ident(delivery_id, "delivery id"))
    return bool(row and row["sha256"] == sha256
                and row["status"] == "RELEASED_FOR_CANDIDATE_PUBLICATION"
                and row["owner_release"] and row["owner_release"]["sha256"] == sha256)
