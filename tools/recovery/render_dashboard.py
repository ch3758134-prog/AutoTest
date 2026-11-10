#!/usr/bin/env python3
"""Render AutoTest's incremental offline engineering HTML from registered facts.

Read-only inputs:
  docs/recovery/current_status.json                 - current evidence/observations
  SAEF/engineering/control/project_state.json       - approved Gate states
  SAEF/engineering/control/work_products.json       - registered work products

The script does not approve Gates, invent CI results or rewrite SAEF histories.
Run: python tools/recovery/render_dashboard.py [--check]
"""
from __future__ import annotations

import argparse
from collections import Counter
from html import escape
import json
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]
STATUS = ROOT / "docs/recovery/current_status.json"
GATES = ROOT / "SAEF/engineering/control/project_state.json"
WORK = ROOT / "SAEF/engineering/control/work_products.json"
DEST = ROOT / "docs/recovery/overview.html"

STYLE = """
:root{font:13px/1.65 "Segoe UI","Microsoft YaHei",system-ui,sans-serif;color:#26333e;background:#f5f7fa}
*{box-sizing:border-box}body{margin:0}a{color:#1766b1;text-decoration:none}a:hover{text-decoration:underline}
header{background:#fff;border-bottom:1px solid #d7dfe7;position:sticky;top:0;z-index:1}
.header{max-width:1200px;margin:auto;padding:10px 28px;display:flex;align-items:center;gap:15px;flex-wrap:wrap}
.brand{font-size:18px;font-weight:700;color:#1f3244}.sub{font-size:12px;color:#667b8d}
nav{margin-left:auto;display:flex;gap:14px;font-size:12px}
main{max-width:1200px;margin:auto;padding:24px 28px 60px}
h1{font-size:24px;line-height:1.3;margin:4px 0 10px}h2{font-size:18px;border-bottom:1px solid #d5dfe8;padding-bottom:8px;margin:34px 0 14px}
h3{font-size:15px;margin:0 0 6px}p{margin:8px 0;color:#425367;max-width:950px}
.small{color:#6b7b89;font-size:12px}.label{font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:#667988}
.strip{display:flex;gap:12px;flex-wrap:wrap;margin:22px 0}.metric{min-width:185px;flex:1;background:#fff;border:1px solid #d4dce4;border-radius:4px;padding:13px 15px}
.metric b{display:block;font-size:20px;color:#1d344a;margin-top:3px}
.ok{color:#19754c}.pending{color:#8b5b12}.unknown{color:#6a7784}
.note{border-left:3px solid #276eaa;background:#edf4fa;padding:12px 16px;margin:14px 0}
.warning{border-left:3px solid #c08327;background:#fcf8ef;padding:12px 16px;margin:14px 0}
table{width:100%;border-collapse:collapse;background:#fff;font-size:12px}th{text-align:left;background:#eef2f6;color:#495e70}
th,td{padding:10px;border-bottom:1px solid #e0e6ec;vertical-align:top}
.module{background:#fff;border:1px solid #d5dfe7;border-radius:4px;margin:9px 0;padding:14px 17px}
.module .id{display:inline-block;color:#60768c;font-size:11px;background:#edf2f7;padding:1px 7px;margin-right:8px}
.module dl{display:grid;grid-template-columns:84px 1fr;gap:4px 10px;margin:8px 0 0}
.module dt{color:#627486}.module dd{margin:0;color:#394a5a}
ul{padding-left:20px;color:#425367}li{margin:7px 0}
footer{border-top:1px solid #dce4eb;margin-top:38px;padding-top:13px;color:#647888;font-size:11px}
code{font-family:Consolas,monospace;color:#294766;overflow-wrap:anywhere}
@media(max-width:700px){.header{padding:10px 14px}main{padding:16px 14px}.module dl{grid-template-columns:1fr;gap:1px}.module dt{font-weight:700}nav{margin:0}}
"""


def load_json(path: Path) -> dict:
    doc = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(doc, dict):
        raise ValueError(f"Invalid JSON root in {path}")
    return doc


def text(value: object) -> str:
    if value is None:
        return "—"
    return escape(str(value), quote=True)


def checked_url(value: object) -> str:
    raw = str(value)
    if not raw.startswith("https://github.com/ch3758134-prog/AutoTest/"):
        raise ValueError(f"Unexpected external link: {raw}")
    return text(raw)


def href(label: str, url: object) -> str:
    return f'<a href="{checked_url(url)}">{text(label)}</a>'


def render() -> str:
    state = load_json(STATUS)
    gates_state = load_json(GATES)
    products = load_json(WORK)
    if state.get("schema") != "AutoTest-current-status/1" or state.get("product") != "AutoTest":
        raise ValueError("Unexpected status source schema or project")
    code = state["code"]
    if not re.fullmatch(r"[0-9a-f]{40}", code["main_sha"]):
        raise ValueError("Invalid main SHA")
    if code["test_passed"] > code["test_total"] or code["errors"] < 0:
        raise ValueError("Inconsistent build/test evidence")
    if gates_state.get("project_id") != "AutoTest" or gates_state.get("schema") != "SAEF-project-state/1":
        raise ValueError("Unexpected SAEF status schema")

    gate_rows = gates_state["gates"]
    counts = Counter(item.get("status", "UNKNOWN") for item in products.values())
    nav = [
        ('#summary', '总览'), ('#modules', '模块'), ('#gates', 'SAEF Gate'),
        ('#source', '配置'), ('#next', '下一步')
    ]
    s = [
        '<!doctype html>', '<html lang="zh-CN"><head>',
        '<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">',
        '<title>AutoTest · 工程总览（状态驱动）</title>',
        '<style>', STYLE, '</style></head><body>',
        '<header><div class="header"><div class="brand">AutoTest</div>',
        '<div class="sub">工程总控 · 本地 HTML · 数据驱动快照</div>',
        '<nav>' + " ".join(f'<a href="{url}">{text(name)}</a>' for url, name in nav) + '</nav>',
        '</div></header><main>',
        '<section id="summary"><div class="small">数据日期：' + text(state["as_of_local_date"]) + ' · 该页面只显示已登记证据，不自动推定实测通过</div>',
        '<h1>AutoTest 产品与研发状态</h1>',
        '<p>' + text(state["scope"]) + '</p>',
        '<div class="strip">',
        '<div class="metric"><div class="label">main 源码身份</div><b><code>' + text(code["main_sha"][:12]) + '</code></b><span class="small">精确 SHA 见下方</span></div>',
        '<div class="metric"><div class="label">Windows Release 构建</div><b class="ok">' + text(code["build"]) + '</b><span class="small">' + text(code["errors"]) + ' 错误 / ' + text(code["warnings"]) + ' 警告</span></div>',
        '<div class="metric"><div class="label">自动化测试（并非实机）</div><b class="ok">' + text(code["test_passed"]) + ' / ' + text(code["test_total"]) + '</b><span class="small">' + href("查看 Actions 证据", code["source"]) + '</span></div>',
        '<div class="metric"><div class="label">远端活动分支</div><b>' + text(len(state["git"]["active_branches"])) + '</b><span class="small">' + text(state["git"]["archival_tags"]) + ' 个已验证归档标签</span></div>',
        '</div>',
        '<div class="warning"><b>产品体验仍未通过验收。</b> ' + text(state["ui"]["decision"]) + '</div>',
        '<p>' + text(code["kind"]) + '</p>',
        '</section>',
        '<section id="modules"><h2>一、当前模块能力（逐项，非“整体完成率”）</h2>',
        '<p>每项按实际代码、自动测试、物理使用证据和下一步区分；Mock/Replay 成功不能转写为 Physical PASS。</p>',
    ]
    for m in state["capabilities"]:
        s.extend([
            '<article class="module"><h3><span class="id">' + text(m["id"]) + '</span>' + text(m["title"]) + '</h3>',
            '<dl><dt>源码现状</dt><dd>' + text(m["code"]) + '</dd>',
            '<dt>软件测试</dt><dd>' + text(m["test"]) + '</dd>',
            '<dt>现场证据</dt><dd>' + text(m["physical"]) + '</dd>',
            '<dt>下一步</dt><dd>' + text(m["next"]) + '</dd></dl></article>'
        ])
    s.extend([
        '</section>',
        '<section id="gates"><h2>二、SAEF 已登记 Gate 与工作产品</h2>',
        '<p>此处<strong>直接读取</strong> SAEF 原始控制登记，Gate 通过仅证明对应历史批准，不自动证明代码实现或产品验收。</p>',
        '<table><thead><tr><th>Gate</th><th>状态</th><th>批准日期</th><th>配置身份</th></tr></thead><tbody>',
    ])
    for key, gate in gate_rows.items():
        s.append('<tr><td><b>' + text(key) + '</b></td><td>' +
                 text(gate["status"]) + '</td><td>' +
                 text(gate.get("approved_at")) + '</td><td><code>' +
                 text(gate.get("configuration_id")) + '</code></td></tr>')
    s.extend([
        '</tbody></table>',
        '<p class="small">登记工作产品 ' + text(len(products)) + ' 项：' +
        ' / '.join(text(k) + ' ' + text(v) for k, v in sorted(counts.items())) + '。历史正文保留原始语境，不作为新的现场通过证明。</p></section>',
        '<section id="source"><h2>三、代码、Git 与原始资产</h2>',
        '<p>当前 main SHA：<code>' + text(code["main_sha"]) + '</code>。' + href("完整 Windows Run", code["source"]) + '。</p>',
        '<p>Git 已退役 ' + text(state["git"]["retired_branches"]) + ' 条旧活动分支，每条有同一 SHA 的轻量归档标签；' +
        href("归档/Bundle 上传验证", state["git"]["archive_url"]) + '，备份 Artifact ID ' + text(state["git"]["bundle_artifact_id"]) + '。</p>',
        '<p>活动分支：' + '，'.join('<code>' + text(n) + '</code>' for n in state["git"]["active_branches"]) + '。</p>',
        '<div class="warning"><b>原始资产尚未全部恢复。</b><ul>',
    ])
    s.extend('<li>' + text(x) + '</li>' for x in state["assets_missing"])
    s.extend([
        '</ul>缺失只说明尚未取得原始字节，不得依据登记 SHA 自动宣布资产已备份。</div>',
        '</section>',
        '<section id="next"><h2>四、下一步实际工作</h2><ul>',
    ])
    s.extend('<li>' + text(x) + '</li>' for x in state["next_steps"])
    s.extend([
        '</ul><div class="note">项目需求与 UI/报告格式允许受真实使用反馈修订。跨模块公共架构与真实设备副作用仍须审查；用户体验不以文档数量或 Build PASS 替代。</div>',
        '<p>' + href("恢复方案 PR", state["links"]["audit"]) + ' · ' +
        href("实际执行 Issue", state["links"]["issue"]) + '</p></section>',
        '<footer>AutoTest · SAEF 数据源 + 当前证据状态的离线投影。页面由 Python 生成；修改源数据再运行会替换当前视图，历史由 Git 保存。' +
        ' 不从网页回写 Gate 批准，也不把 UI 自动测试视为人工验收。</footer></main></body></html>\n',
    ])
    return "\n".join(s)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true",
                        help="Fail if checked-in HTML differs from the current registered facts")
    args = parser.parse_args()
    expected = render()
    actual = DEST.read_text(encoding="utf-8") if DEST.exists() else None
    if args.check:
        if actual != expected:
            raise SystemExit("SAEF_HTML_OUTDATED: run python tools/recovery/render_dashboard.py")
        print("SAEF_HTML_UP_TO_DATE")
        return
    if actual == expected:
        print("SAEF_HTML_UNCHANGED")
        return
    DEST.parent.mkdir(parents=True, exist_ok=True)
    DEST.write_text(expected, encoding="utf-8", newline="\n")
    print("SAEF_HTML_UPDATED:", DEST.relative_to(ROOT))


if __name__ == "__main__":
    main()
