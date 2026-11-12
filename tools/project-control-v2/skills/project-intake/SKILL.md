---
name: project-intake
description: Discover and understand an existing or new project, including its purpose, users, historical decisions, constraints, evidence gaps and current work, before proposing an implementation plan.
---

# Project intake and project context recovery

Use when receiving a project for the first time, continuing a previously managed project with incomplete context, or recovering from a large project change.

1. Read START_HERE.md, portable/AI_WORK_MODES.md and portable/CONTEXT_DISCOVERY.md from the ProjectControl package. Confirm the exact target repository and the role assigned by the user; do not appoint yourself.
2. Read portable/EVIDENCE_AND_REMEDIATION.md. Separate missing search results from established missing project information: inspect alternative indexes, old revisions, decisions, and original materials before declaring an artifact absent. If a necessary artifact truly cannot be found, propose a bounded research, analysis, or drafting action with responsible role and review path.
3. Build a source map of goals, users, work scenarios, constraints, requirements, design reasons, historical decisions and reversals, research, tests and actual validation. Discover both current and historical sources before narrowing to a current implementation question.
4. Trace decisive claims back to specific material; distinguish observed sources, user-reported facts, inference, approvals, unknowns and conflicts. Do not mistake latest code for business purpose or old decisions for current authorization.
5. If an important source domain is unexamined or conflicting, continue investigating; make a bounded partial assessment rather than claiming full project understanding.
6. Report project purpose, important findings, unknowns and the next justified activity in everyday language. Keep repository references and the coverage audit as technical evidence.
7. Hand off evidence and remediation proposals to the existing revisioned project management ledger, when available; retain exact source references and their status. New evidence must trigger reassessment of dependent plans, not silent adoption of an old Gate. If target management is unavailable, report that the result remains an unsynchronized candidate.
8. Do not create an implementation READY task, declare Gate success, alter product scope or request approval merely because a discovery report is structurally valid.

For structured evidence, use portable/context_assessment.py on a project-specific v2 assessment record. Old context_discovery.py /1 records are historical source-coverage evidence only. Review source applicability and subsequent changes before treating material as a current candidate. Its result only checks coverage structure and cannot prove semantic understanding. This skill is a portable source file; the host client must actually load it before it influences AI behavior.
