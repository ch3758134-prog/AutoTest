# Project Control｜通用项目研发管理系统

**管理系统在 `Project_Management` 分支直接研发和提交，不为普通修改额外创建分支或 PR；不再向 AutoTest HOST `main` 提交管理工具源码。** 本仓库同时保存受管理项目，但不同分支承担不同职责。

本目录与 AutoTest ECU 产品源码彼此独立：AutoTest 是可被管理的项目之一，不是管理系统的固定业务模型。

## 开始使用

管理 AI 首先阅读 [START_HERE.md](START_HERE.md)、[portable/CONTEXT_DISCOVERY.md](portable/CONTEXT_DISCOVERY.md)、[portable/AI_WORK_MODES.md](portable/AI_WORK_MODES.md) 和 [PORTABLE_MANIFEST.json](PORTABLE_MANIFEST.json)。使用时，用户提供本文件夹和目标 GitHub 仓库地址。Codex 的本地目录由用户自己指定，其余工作来源、任务和 Git 版本由管理 AI 与 Codex 从目标仓库读取。

本机管理界面（Python 3.11+，Git）：

```powershell
python tools/project-control-v2/portable/web_server.py --port 8791
```

浏览器打开 `http://127.0.0.1:8791`。即使没有任何目标项目，管理界面也可以启动。首次使用请在首页选择“新建 / 接管”，指定真实项目目录和 GitHub 仓库，先预览再确认；也可登记已经初始化的项目。登记保存在本机用户目录下的 `.project-control-local/targets.json`，不会自动向 GitHub 推送。更详细的初始化、角色和权限边界见 [portable/WEB_WORKSPACE.md](portable/WEB_WORKSPACE.md)。

生成给新管理 AI 的独立 ZIP：

```powershell
python tools/project-control-v2/portable/export_package.py --out ProjectControl.zip
```

## 代码状态

`portable/module_control.py`、`portable/remote_module_status.py` 提供模块归属、本地 Git 提交和未提交修改的观察，以及手动核对 GitHub 模块提交和 PR。观察到代码不等于通过集成或现场测试；正式发布和审批依赖真实证据。

## 工作规则

- **管理工具只改 `Project_Management`**：普通工具代码可直接在本分支研发提交，不额外建立分支或 PR，不写入 HOST `main`。**正式研究/需求/设计/阶段成果先在对话展示全文**；用户批准准确版本后同轮直接正式落档并远端回读，不二次索要“允许上传”许可。未获批准的正式成果默认不提交 GitHub 草稿。代码提交不等于工程内容批准。
- **不自动跑管理工具 CI**：需要时人工执行 `python -m unittest discover -s tools/project-control-v2/tests -v`。
- 不动 AutoTest ECU 产品其他目录；不要把旧试点 UI、示例产品数据或旧工作区带入通用发行包。
- 已有工作区和正式工程默认保留，严禁自动清理。
- **候选状态**：真实目标 GitHub 仓库的端到端交付、完整人类授权、Windows 实际现场验收尚未完成。不能把本地脚本测试等同正式交付。


## 体系审查与追踪

完整问题分析、外部方案比较、跨文件修订范围及尚未完成的验证，见 [AI_CONTEXT_ARCHITECTURE_REVIEW_20261112.md](AI_CONTEXT_ARCHITECTURE_REVIEW_20261112.md)。

## 4.4 候选｜证据评价与主动补全

首次项目接管不能把关键词搜索无结果直接当作资料不存在，也不能把找到的历史文档当作当前生效的产品定义。新方法 [portable/EVIDENCE_AND_REMEDIATION.md](portable/EVIDENCE_AND_REMEDIATION.md) 将来源发现、当前适用性审查、历史变更和缺失工程依据的补全责任分开。[portable/context_assessment.py](portable/context_assessment.py) 校验新版结构化调查记录，旧 `context_discovery.py` 仅保留历史格式的可读性。

当前仍为候选：源文件和文档存在不等于 ChatGPT 网页原生 Skill 已安装，结构审查通过不等于系统理解/批准；所有真实工作授权仍使用现有 Task Contract/人类批准机制。


## 4.5 候选｜证据评价纳入本地工程管理台账

`portable/context_workflow.py` 允许 AI 的证据评价通过工作台受控导入 `management.json.context_reviews`（保留历史/校验摘要/版本）；补全提案经审查可关联到 `management.work` 中的 `PROPOSED` 工作，准确保留来源评价 ID、补全 action ID，且 `execution_authorized=false`。本地 CAS 检查和审批/执行权限保持独立，旧的离散候选 JSON 不会自动获得批准或迁移。参见 `portable/WEB_WORKSPACE.md`。

新增 `tests/test_context_workflow.py`，需在真实发行包环境执行回归及 Windows UI 试验；本次 GitHub 修改本身不等于单测已通过。

## 4.6 候选｜成果提交与负责人审阅

本轮真实 AutoTest 管理 AI 试验发现，过去缺少“完整成果正文向负责人展示→明确决定→候选发布”的受控环节；AI 曾直接写 GitHub Issue 并称已交付。新增 [portable/DELIVERY_REVIEW_PROTOCOL.md](portable/DELIVERY_REVIEW_PROTOCOL.md) 和 `portable/delivery_review.py`，用独立本地私有审阅区及两个不同令牌管理全文、差异、范围和准确版本。

**重要**：正式研究/设计/需求批准与仅允许候审公开仍独立。管理工具源码保存在 `Project_Management` 或其已明确授权的候审分支，不自动合入 HOST `main`。详见单独审查报告；GitHub 直接宽权限、Windows 完整端到端、正式人类身份认证仍不能仅靠本地令牌实现。

## 4.6.1 系统审查与主线候选集成

本轮系统级复核覆盖接管、岗位、证据、治理、候审交付、远端同步、任务交接与用户界面。候审包现在能够通过本地 UI 导入/全文预览/修订；负责人可选择只确认内部阅读或精确版本候审公开；项目证据变化按受影响主题传播，不全局重置。完整审查及实际验证范围见 `SYSTEM_AUDIT_20261112.md`。历史版本的本系统代码曾经进入 main，权限来自用户当时明确的管理工具研发要求，**并不构成 AutoTest 需求/设计或 Project Control 正式发行验收**。