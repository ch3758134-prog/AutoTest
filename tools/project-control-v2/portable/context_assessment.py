#!/usr/bin/env python3
"""Project evidence assessment v2: source discovery is not current validity or authorization."""
from __future__ import annotations
import argparse
import json
from pathlib import Path

SCHEMA = "project-control-context-assessment/2"
TOPICS = ("purpose","users_scenarios","scope_constraints","history_decisions",
          "requirements_design","evidence_validation","current_work_risks")
TOPIC_STATES = {"NOT_SEARCHED","SEARCH_INCOMPLETE","SOURCES_FOUND","ASSESSED",
                "GAP_CONFIRMED","NOT_APPLICABLE"}
CLAIM_STATES = {"UNASSESSED","SUPPORTED_CANDIDATE","HISTORICAL_ONLY",
                "SUPERSEDED","CONFLICT","UNKNOWN"}
OUTCOMES = {"FOUND","NO_MATCH","ERROR","INACCESSIBLE"}
REMEDIATIONS = {"RESEARCH_SOURCE","REVIEW_EXISTING_ARTIFACT","RECONCILE_HISTORY",
                "DRAFT_MISSING_ARTIFACT","VERIFY_REALITY","OWNER_DECISION"}
ROLES = {"coordinator","specialist","integration","reviewer","local_executor"}

class AssessmentError(ValueError):
    pass

def text_field(row, field):
    if not isinstance(row,dict) or not isinstance(row.get(field),str) or not row[field].strip():
        raise AssessmentError("missing "+field)
    return row[field].strip()

def indexed(rows, name, fields):
    if not isinstance(rows,list):
        raise AssessmentError("invalid "+name)
    result = {}
    for row in rows:
        for field in ("id",)+tuple(fields):
            text_field(row,field)
        if row["id"] in result:
            raise AssessmentError("duplicate "+name+" id")
        result[row["id"]] = row
    return result

def links(row,key,items):
    value = row.get(key)
    if not isinstance(value,list) or any(not isinstance(x,str) or x not in items for x in value):
        raise AssessmentError("invalid references "+key)
    return value

def audit(record):
    if not isinstance(record,dict) or record.get("schema") != SCHEMA:
        raise AssessmentError("unknown assessment schema")
    if record.get("role") not in ROLES:
        raise AssessmentError("invalid role")
    for key in ("project_ref","task_focus","source_revision"):
        text_field(record,key)
    sources = indexed(record.get("sources"),"sources",("locator","type"))
    attempts = indexed(record.get("search_attempts"),"search_attempts",
                       ("domain","method","query_or_path"))
    for a in attempts.values():
        if a.get("outcome") not in OUTCOMES:
            raise AssessmentError("invalid search outcome")
        if a["outcome"] in ("ERROR","INACCESSIBLE"):
            text_field(a,"limitation")
    claims = indexed(record.get("claims"),"claims",("statement","assessment"))
    for c in claims.values():
        refs=links(c,"source_ids",sources)
        if c["assessment"] not in CLAIM_STATES:
            raise AssessmentError("invalid claim assessment")
        if c["assessment"] in ("SUPPORTED_CANDIDATE","HISTORICAL_ONLY","SUPERSEDED","CONFLICT"):
            if not refs:
                raise AssessmentError("claim without source")
            text_field(c,"reasoning")
        if c["assessment"] == "SUPPORTED_CANDIDATE":
            text_field(c,"current_applicability_reason")
            if c.get("historical_changes_checked") is not True:
                raise AssessmentError("claim lacks historical applicability review")
        if c["assessment"] in ("SUPERSEDED","CONFLICT"):
            text_field(c,"resolution_needed")
        if c.get("formally_approved_now") is True:
            raise AssessmentError("discovery cannot grant approval")
    actions=indexed(record.get("proposed_actions"),"proposed_actions",
                    ("topic","kind","reason","expected_output"))
    for a in actions.values():
        if a["topic"] not in TOPICS or a["kind"] not in REMEDIATIONS:
            raise AssessmentError("invalid proposed remediation")
        if a.get("status")!="PROPOSED_NOT_AUTHORIZED":
            raise AssessmentError("discovery action is not authorized")
    topics=record.get("topics")
    if not isinstance(topics,dict) or set(topics)!=set(TOPICS):
        raise AssessmentError("missing topic map")
    unresolved=[]
    awaiting_assessment=[]
    gaps=[]
    linked_actions=set()
    for topic,row in topics.items():
        if not isinstance(row,dict) or row.get("status") not in TOPIC_STATES:
            raise AssessmentError("invalid topic")
        src=links(row,"source_ids",sources)
        clm=links(row,"claim_ids",claims)
        attempts_used=links(row,"search_attempt_ids",attempts)
        act=links(row,"action_ids",actions)
        linked_actions.update(act)
        if any(actions[a]["topic"]!=topic for a in act):
            raise AssessmentError("action topic mismatch")
        if any(not set(claims[c]["source_ids"]).issubset(set(src)) for c in clm):
            raise AssessmentError("topic claim source not referenced")
        status=row["status"]
        if status=="NOT_SEARCHED":
            text_field(row,"limitation")
            unresolved.append(topic)
        elif status=="SEARCH_INCOMPLETE":
            if not attempts_used:
                raise AssessmentError("search without attempt record")
            text_field(row,"limitation")
            unresolved.append(topic)
        elif status=="SOURCES_FOUND":
            if not src:
                raise AssessmentError("sources found without source")
            text_field(row,"limitation")
            awaiting_assessment.append(topic)
        elif status=="ASSESSED":
            if not src or not clm:
                raise AssessmentError("assessment missing claim or source")
            if not any(claims[c]["assessment"]=="SUPPORTED_CANDIDATE" for c in clm):
                raise AssessmentError("no current candidate")
            if any(claims[c]["assessment"] in ("UNASSESSED","UNKNOWN","CONFLICT") for c in clm):
                raise AssessmentError("unresolved claim in assessed topic")
            text_field(row,"assessment_summary")
        elif status=="GAP_CONFIRMED":
            methods={attempts[x]["method"] for x in attempts_used}
            if len(attempts_used)<2 or len(methods)<2:
                raise AssessmentError("one empty search does not establish missing artifact")
            if any(attempts[x]["outcome"] in ("ERROR","INACCESSIBLE") for x in attempts_used):
                raise AssessmentError("search failure is not artifact absence")
            if not act:
                raise AssessmentError("gap needs proposed remediation")
            text_field(row,"gap_rationale")
            gaps.append(topic)
        else:
            text_field(row,"not_applicable_reason")
    if set(actions)!=linked_actions:
        raise AssessmentError("orphan proposed action")
    disposition=record.get("next_disposition")
    if disposition not in ("CONTINUE_DISCOVERY","PREPARE_REMEDIATION",
                           "CANDIDATE_SCOPED_WORK","ASK_OWNER"):
        raise AssessmentError("invalid next disposition")
    if disposition=="CANDIDATE_SCOPED_WORK":
        text_field(record,"readiness_rationale")
        required=record.get("work_prerequisite_topics")
        if not isinstance(required,list) or not required or any(x not in TOPICS for x in required):
            raise AssessmentError("missing work prerequisites")
        if any(topics[x]["status"]!="ASSESSED" for x in required):
            raise AssessmentError("unassessed prerequisite")
    if record.get("claims_project_complete") is True and (unresolved or awaiting_assessment or gaps):
        raise AssessmentError("cannot claim complete with unresolved matters")
    return {"schema":"project-control-context-assessment-result/2",
            "structurally_valid":True,"not_searched_or_incomplete":unresolved,
            "found_not_assessed":awaiting_assessment,"confirmed_gaps":gaps,
            "proposed_actions":len(actions),"next_disposition":disposition,
            "authorization_granted":False,"formal_approval_granted":False,
            "semantic_validity_proved":False,
            "warning":"Sources found != current applicability; human review and permissions remain separate."}

def main():
    p=argparse.ArgumentParser()
    p.add_argument("record",type=Path)
    args=p.parse_args()
    try:
        print(json.dumps(audit(json.loads(args.record.read_text(encoding="utf-8"))),
                         ensure_ascii=False,indent=2))
    except (ValueError,OSError) as exc:
        p.exit(2,"ASSESSMENT_BLOCKED: "+str(exc)+"\n")

if __name__=="__main__":
    main()
