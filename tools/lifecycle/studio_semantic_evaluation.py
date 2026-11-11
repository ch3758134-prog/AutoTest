"""Human-graded evaluation runner for AI engineering semantic judgments.

This tool cannot invoke an LLM or certify output correctness. It can prepare
review worklists and validate explicit external reviewer scores; it deliberately
avoids keyword scoring, length quotas and model self-approval.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path

EVAL_PATH = Path(__file__).with_name("semantic_evaluation_cases.json")


def load_cases():
    data=json.loads(EVAL_PATH.read_text(encoding="utf-8"))
    if data.get("schema")!="engineering-semantic-evaluation/1":
        raise ValueError("Unrecognized semantic evaluation schema")
    cases=data["cases"]
    identifiers=[x["id"] for x in cases]
    if len(set(identifiers))!=len(identifiers):
        raise ValueError("Repeated semantic evaluation case")
    dimensions=[x["id"] for x in data["scoring"]]
    if len(set(dimensions))!=len(dimensions):
        raise ValueError("Repeated review dimension")
    for case in cases:
        for key in ("user","context","reference","pitfalls"):
            if not case.get(key):
                raise ValueError(case["id"]+" missing review information "+key)
    return data


def review_template():
    data=load_cases()
    return {
        "schema":"engineering-semantic-human-review/1",
        "evaluation_edition":data["edition"],
        "reviewer":"",
        "responses":[{"case_id":case["id"],"answer":"","scores":{
            dimension["id"]:None for dimension in data["scoring"]},
            "notes":"","evidence_quote":""} for case in data["cases"]],
        "note":"Fill model answer and human assessment after actual evaluation. This file is not a record of tests being run.",
    }


def summarize_reviews(payload):
    data=load_cases()
    if payload.get("schema")!="engineering-semantic-human-review/1":
        raise ValueError("Expected human semantic review format")
    if payload.get("evaluation_edition")!=data["edition"]:
        raise ValueError("Evaluation suite version mismatch")
    if not isinstance(payload.get("reviewer"),str) or not payload["reviewer"].strip():
        raise ValueError("Human reviewer identity required")
    expected={x["id"] for x in data["cases"]}
    dimensions={x["id"] for x in data["scoring"]}
    checked=set()
    individual=[]
    for response in payload.get("responses",[]):
        case_id=response.get("case_id")
        if case_id not in expected or case_id in checked:
            raise ValueError("Unknown or repeated case ID")
        checked.add(case_id)
        scores=response.get("scores")
        if not isinstance(scores,dict) or set(scores)!=dimensions:
            raise ValueError(case_id+" is missing scoring dimensions")
        if any(type(score) is not int or not 0<=score<=3 for score in scores.values()):
            raise ValueError(case_id+" requires explicit human scores (0..3)")
        if not all(isinstance(response.get(k),str) and response[k].strip()
                   for k in ("answer","notes","evidence_quote")):
            raise ValueError(case_id+" needs actual AI answer, specific reviewer notes and quoted evidence")
        individual.append({"case_id":case_id,"scores":scores,"total":sum(scores.values()),
                           "maximum":3*len(dimensions),"notes":response["notes"]})
    if checked!=expected:
        raise ValueError("Evaluation incomplete: "+",".join(sorted(expected-checked)))
    totals={dim:sum(x["scores"][dim] for x in individual) for dim in dimensions}
    return {"schema":"engineering-semantic-review-summary/1",
            "reviewer":payload["reviewer"],"suite":data["edition"],
            "assessed_cases":len(individual),
            "by_dimension":{dim:{"score":total,"maximum":3*len(individual)}
                            for dim,total in totals.items()},
            "cases":individual,
            "note":"These are recorded reviewer judgments, not machine-verified correctness or production approval."}


def main(argv=None):
    p=argparse.ArgumentParser(description="Prepare or summarize semantic AI-review cases without word matching")
    p.add_argument("--template",action="store_true",help="emit an unfilled human review template")
    p.add_argument("--review",type=Path,help="human-reviewed answers JSON")
    args=p.parse_args(argv)
    if args.template and args.review:
        p.error("Choose template or reviewed report")
    if args.review:
        output=summarize_reviews(json.loads(args.review.read_text(encoding="utf-8")))
    else:
        output=review_template() if args.template else load_cases()
    print(json.dumps(output,ensure_ascii=False,indent=2))


if __name__=="__main__":
    main()
