#!/usr/bin/env python3
"""Advisory project/role bootstrap for human-triggered ChatGPT conversations.

ChatGPT Web reads AI_START_HERE.md + GitHub JSON directly; this optional helper
provides a testable example. It does NOT authenticate a conversation, appoint a
role, perform edits, approve stages, or dispatch another AI.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent


def decide(project_index, catalog, appointment_map, *, project_id=None,
           user_confirmed_role=None, user_selected_task=None):
    """Recommend one next step; only explicit user role or task appointment identifies role."""
    projects = project_index.get("projects", [])
    valid_roles = {r["id"] for r in catalog.get("roles", [])}
    if user_confirmed_role is not None and user_confirmed_role not in valid_roles:
        raise ValueError("Unknown role; clarify with the project owner, do not invent identity.")

    if project_id is None:
        if not projects:
            return {"action": "ASK_PROJECT", "project_id": None, "role_id": None,
                    "question": "你准备建立或继续哪个项目？大致要实现什么？"}
        if len(projects) != 1:
            return {"action": "ASK_PROJECT", "project_id": None, "role_id": None,
                    "question": "我已读取项目列表。这个对话要参与哪个项目？"}
        project_id = projects[0]["id"]

    project = next((p for p in projects if p.get("id") == project_id), None)
    if project is None:
        return {"action": "ASK_PROJECT_SETUP", "project_id": project_id, "role_id": None,
                "question": "这个项目目前还未登记。是要新建它吗？请先说明基本目标。"}

    if user_confirmed_role is not None:
        return {"action": "READY_WITH_EXPLICIT_ROLE", "project_id": project_id,
                "role_id": user_confirmed_role,
                "next": "Read the role card and project evidence. Role alone does not authorize code or approvals."}

    if user_selected_task:
        appointments = appointment_map.get(project_id, {}).get("appointments", [])
        matching = [a for a in appointments if user_selected_task in a.get("task_ids", [])
                    and a.get("role_id") in valid_roles
                    and a.get("human_confirmation_reference")]
        role_ids = {a["role_id"] for a in matching}
        if len(role_ids) == 1:
            return {"action": "READY_WITH_TASK_ROLE", "project_id": project_id,
                    "role_id": next(iter(role_ids)), "task_id": user_selected_task,
                    "next": "Read the exact work item and sources before beginning. This is not approval."}
        if len(role_ids) > 1:
            return {"action": "ASK_ROLE", "project_id": project_id, "role_id": None,
                    "question": "这个任务存在多个已登记的职责。你希望本对话承担哪个方向？"}
        return {"action": "ASK_ROLE", "project_id": project_id, "role_id": None,
                "question": "我已找到项目，但这个任务还没有可核实的对话角色。你希望我负责哪个方向？"}

    return {"action": "ASK_ROLE", "project_id": project_id, "role_id": None,
            "question": "我已读取项目资料。你希望这个对话由我担任工程总协调，还是负责某个专业方向、集成验证或专项审查？"}


def main(argv=None):
    p = argparse.ArgumentParser(description="Advisory AI identity check; does not appoint or act")
    p.add_argument("--project")
    p.add_argument("--role-confirmed", help="Only when the human already named the role")
    p.add_argument("--task", help="Only when the human named a task in this conversation")
    args = p.parse_args(argv)
    projects = json.loads((HERE / "projects.json").read_text(encoding="utf-8"))
    roles = json.loads((HERE / "roles.json").read_text(encoding="utf-8"))
    appointments = {}
    for project in projects["projects"]:
        path = HERE.parents[0] / "appointments" / (project["id"] + ".json")
        if path.is_file():
            appointments[project["id"]] = json.loads(path.read_text(encoding="utf-8"))
    result = decide(projects, roles, appointments, project_id=args.project,
                    user_confirmed_role=args.role_confirmed,
                    user_selected_task=args.task)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
