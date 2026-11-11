# Project Control V2 — 全新项目研发管理工作台（候选）

这是独立于旧 Engineering Workbench 的**重设计候选**，不是旧 `studio.html` 的增量改造。

## 核心重新定义
项目管理面对的是：**项目目标与交付范围 → 开发工作分解与计划 → P0～G4 既定研发流程 → 实际执行与证据 → 偏差、变更与下一步工作**。

本候选采用独立的 HTML/CSS/JS 和只读 Python Web 服务器，不使用旧导航、旧弹窗、自动从工程对象标题推断 Gate 等机制。

- **项目总览**：优先显示关键管理事项、近期工作、计划差异和数据可信度
- **目标与交付**：已批准的 P0 与交付目标分开呈现，未来建议不会改变批准范围
- **开发计划**：研究、设计、实现、验证都能作为工作；CURRENT/NEXT/LATER 滚动规划，缺日期不编造甘特图
- **研发流程**：显示每个固定阶段的要求，但不会把文件批准等同于 Gate 通过
- **成果与审查**：只展示真正有管理意义的批准成果和整理后的 G1 送审材料，默认隐藏内部草稿
- **协作交接**：生成给 ChatGPT 专业对话/Codex 的可复制任务说明；用户主动触发，无 Agent 循环

## 运行

本地 Codex 从 GitHub 获取本分支 **准确 HEAD**，由它自行选择本地仓库位置；不要依据聊天历史推断 Windows 路径。

```
python tools/project-control-v2/server.py --port 8790
```

打开 `http://127.0.0.1:8790`。

依赖只有 Python 标准库；须从完整仓库运行，以便读取：

- `docs/engineering-control/projects/autotest/snapshot.json`
- `tools/lifecycle/examples/autotest_plan_control_pilot.json`
- `tools/project-control-v2/process.json`

按需运行验证：

```
python -m unittest discover -s tools/project-control-v2/tests -v
python -m unittest discover -s tools/lifecycle/tests -p test_studio_plan_integrity.py -v
node --check tools/project-control-v2/app.js
```

## 绝对边界

1. 服务器只提供 GET 数据和静态文件；POST 被拒绝。不更改正式 P0、G1/Gate、代码或 GitHub。
2. `autotest_plan_control_pilot.json` 是**未经批准的计划候选**。任何工作项不会自动成为正式任务。
3. G1 报告 r4 仍是研究候选，不被当作已批准系统需求。
4. 按钮“复制交接”只复制文字，不会自动调用 ChatGPT、Codex、创建 Issue 或派发任务。
5. 当前数据源是**本地工作树**，页面显示 HEAD 仅供核对。不会宣称与 GitHub 远端最新同步。
6. 本版用真实 AutoTest 工程记录验证新的信息架构，**不是完整项目管理产品交付**。正式计划审批、迭代回滚决策、Codex 回执入库和可靠远程同步仍待下一阶段实现。

## 旧系统处理

原 PR #28/旧工具仅作为历史候选资料存在；**不要求沿用旧 UI、数据库对象模型、导航和功能分组**。正式已批准工程资料必须保留不可修改。未来替换旧系统时，应以可审查的数据迁移实现，而非直接删除已批准档案或原始证据。
