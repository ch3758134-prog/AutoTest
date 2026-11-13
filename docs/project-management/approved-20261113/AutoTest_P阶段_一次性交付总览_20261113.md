# AutoTest｜P 阶段一次性交付汇总与归类说明

**整理日期**：2026-11-13  
**性质**：对已批准成果的完整导航、分类、状态复核及项目管理系统适配建议。**这不是新的 P0 版本、P1 阶段批准、Gate 决定或对已批准原文的替换。**  
**唯一仓库**：https://github.com/ch3758134-prog/AutoTest  
**原文与批准记录**：[`Project_Management/docs/project-management/new-baseline-20261112/`](https://github.com/ch3758134-prog/AutoTest/tree/Project_Management/docs/project-management/new-baseline-20261112/)  
**可编辑管理工具**：[`Project_Management/tools/project-control-v2/`](https://github.com/ch3758134-prog/AutoTest/tree/Project_Management/tools/project-control-v2/)

## 一、先把三个概念分开

### 1. 产品定义（Product Definition）——回答“这是什么产品”

**已批准原文（P0-01 v0.2 的产品定义）**：

> AutoTest 是一套面向电子控制单元（ECU）的**可配置、可扩展的软硬件一体化自动化测试与开发系统**。
>
> 系统通过真实测试硬件模拟 ECU 的外部工作条件与输入激励，采集和分析 ECU 的实际响应，提供可编辑的自动测试程序和可扩展的专业工具体系，实现 ECU 测试过程的自动化组织、执行、判定与结果追溯。

这就是产品身份，应当简洁、可长期使用。**不是**研发路线图，不应在这个定义中混入 P0/G0/G1、R1 时间表、分支名、WPF/Prism、EPB 是首个对象、CI 等背景；相关事实放在对应的项目/需求/架构/配置记录。产品定位的正式变更需要独立版本和批准，不因项目进度自然改变。

### 2. 项目定义／项目章程（Project Charter）——回答“为什么研发、交付什么、谁决定”

项目可以围绕已经定义的产品组织研发。章程应阐明：立项动因、期望业务结果、项目范围与排除项、近期里程碑与交付约束、责任和决策权、主要风险及成功判断。这些不是产品身份。即使项目进入下一阶段、改变执行计划或扩充研发队伍，也不能自动改变产品定义。

### 3. 研发生命周期（Engineering Lifecycle）——回答“通过什么工作与证据把产品做出来”

现状调查、专题研究、利益相关者需求、系统需求、架构、详细设计、实施、集成、验证、发布交接、持续治理，都是工程**过程活动**，并不天然构成一条必须严格串行通过的固定阶段编号。项目可以根据依赖和证据并行推进，并通过评审与批准来确定基线。过程不是产品功能目录，也不是产品定义。G0/G1/G2 等编号是 AutoTest 具体项目采用的管理节奏，不是通用 Project Control 的硬编码模型。

**归类规则**：一个事实可以同时关联三个对象，但只能明确写在其适当职责位置；其他地方以“来源链接 + 版本”引用。不要把产品定义写成“R1 要做的功能列表”，也不要把 P0 章程写成 G1 需求或 G3 实施设计。

## 二、已批准 P0 全套工作产品（一次性导航）

这八份工作产品已经获得负责人在对话中针对准确版本的内容批准，且已提交 GitHub。批准**不等于**功能实现或硬件验收，也不等于旧 SAEF 历史版本自动复活。

| 编号 | 正式文件 | 已批准版本 | 内容职责 | 在新管理系统中的归属 |
|---|---|---|---|---|
| P0-01 | [产品定义与立项目的](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-01.md) | v0.2 | 产品身份与立项动因分章节表达 | **拆成两个不同管理实体**：产品定义、项目立项依据；原文保持不变 |
| P0-02 | [项目目标与近期交付范围](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-02.md) | v0.1 | 总目标、近期可运行闭环、总交付范围 | 项目目标/范围/交付边界，不回填产品定义 |
| P0-03 | [项目组织、职责与决策权限](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-03.md) | v0.1 | 负责人、协调 AI、专业 AI、集成验证、Codex 的职责 | 组织与权限治理 |
| P0-04 | [配置与基线管理](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-04.md) | v0.1 | 配置项、源版本、工程资产、变更与追踪 | 配置管理/版本基线治理 |
| P0-05 | [工作成果、审查与交付控制](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-05.md) | v0.1 | 成果展示、内容批准、准确版本归档、编码与验证分离 | 成果/对话审批/交付治理 |
| P0-06 | [质量、验证与评审策略](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-06.md) | v0.1 | 代码存在、构建、软件实操、实体测试、验收的证据分级 | 质量与验证治理 |
| P0-07 | [工具、环境与可复现性](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-07.md) | v0.1 | 工具栈、环境事实、可复现性、延期补证 | 环境与资源管理 |
| P0-08 | [风险、问题、决策与变更](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-08.md) | v0.1 | 风险登记、问题闭环、决策变更与影响 | 全周期治理 |

### 完整批准与准出证据

- [APPROVAL_RECORD.md｜P0-01～P0-08 的负责人批准原话和范围](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/APPROVAL_RECORD.md)
- [P0-GATE-REVIEW.md｜阶段准出审查原文](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/P0-GATE-REVIEW.md)
- 准出结果：**2026-11-12 已批准 PASS_WITH_LIMITATIONS**；限制 **L-01～L-06** 保持有效。
- 尤其注意 P0-04～P0-08 文件正文仍保留批准前的“候审”头部文字，这不是审批撤销。系统应分别呈现“原始文档成文状态”和“独立批准事实”，不能静默重写批准版本。

## 三、对 P0 的统一解释（减少重复审批和后续丢档）

**P0-01 的产品定义**只用于确定 AutoTest 是什么、给谁创造什么价值，以及主要能力类型和外部边界。P0-01 的“立项目的”是相邻但不同的章程内容。虽然历史上两者在同一份获批文件中，新的 Project Control 应**以两个独立对象读取**，不再默认把“产品定义”与整个 P0 阶段状态绑定。

**P0-02 的项目目标**包含“优先跑通真实 ECU 自动化测试闭环”，这是本次研发项目的近期目标和工作顺序；不是说产品定义仅包含当前能验证的功能，也不等于下一版产品架构已批准。

**P0-03～P0-08**是贯穿研发周期的项目治理约定，不需要每进入 G0、G1 就重复写一套。生命周期活动应继承适用规则，但只有**明确批准的确切工作成果**才能变成正式阶段基线。

对已批准内容：直接保存准确原文 + 独立批准记录 + Git 版本并远端回读，不再请用户重复批准“允许上传”。对新研究、需求、设计或审查候选：**先在对话展示完整内容再由负责人决定**；不自行提交 GitHub 草稿。对一般管理软件源码修订：在已允许的 `Project_Management` 分支直接开发和提交，提交不产生任何工程内容批准、Gate 或 Codex READY。

## 四、研发过程与 P0 的关系：谁是输入，谁是输出

| 工程过程 / AutoTest 常用阶段 | 工作解决的问题 | 需要的主要成果 | 不得混同为 |
|---|---|---|---|
| 立项与治理／P0 | 为什么组织项目、如何管理责任与资源 | 产品定义的**引用**、章程、范围、角色、配置、质量、变更规则 | 产品详细功能需求、架构图、实施计划 |
| 现状调查／G0 | 原有硬件/软件/图纸/代码到底有什么、什么可用 | 资产与版本身份、差异、真实性、证据缺口 | 已验证全部功能或已经批准技术方案 |
| 需求与场景／G1 | 用户应能完成什么任务、如何验收 | 需求、场景、边界、质量目标、优先级、追溯 | 项目组织规则、既有源码结构 |
| 架构与接口／G2 | 系统如何分解及协作 | 系统分层、接口、数据与资源流、方案取舍 | 具体实现代码、源码已运行证据 |
| 详细设计／G3 | 指定工作包如何可实施、可验证 | 对象/状态/异常/交互/接口与测试设计 | 功能已交付、正式量产放行 |
| 实施与集成／G4 | 如何在代码和设备中实现已确定方案 | 源码、构建、集成、可复核日志和差异 | 实体验收或自动授权下一任务 |
| 验证和交付／依项目计划 | 实际软件与 ECU 是否满足需求 | 功能操作、台架、系统验收、问题和限制 | 所有未测情况一并通过 |

这些是可复用过程职责；**不要求机械走完一项后才允许另一项做准备研究**。项目另设阶段审查，只决定清楚列出的工作成果和范围，不会自动批准跨领域工程事实。

## 五、P0 批准后实际处于什么位置

- 新 P0-01～P0-08：**批准并已落档**。
- P0 阶段准出：**PASS_WITH_LIMITATIONS**，不能写成完全无条件通过。
- G0-01：已有 v0.1、v0.2 及后续 v0.3 候审稿，但**未得到针对完整版本的内容批准**。这些不是应自动补写 GitHub 的历史批准。
- 新单仓分支：`main`（HOST）、`Main_Control`（主控板）、`Key_Sim`（按键板）、`Project_Management`（治理系统及档案）。这是后来执行的仓库组织调整，不得静默改写 11 月 12 日 P0 批准原文；需以独立配置变更事实挂接现行分支。
- Project Control：已有候选管理工具源码；尚未完成 GitHub 对话审批→正式落档→管理数据同步→Codex 任务交接的可信端到端验收。不能因为 GitHub 已有分支就声称工具已建成。
- **不能凭“我记得已经批准”**推断 G0-01、P1 或后续 Gate 也获批。现有证据只能支撑上面列出的明确决定。

## 六、交给 Project Control 的正确模型（适配建议，不是新批准的章程）

产品信息至少应独立包含：`product_id`、`definition_revision`、`definition_text`、`approved_source`、`effective_scope`。它的版本和来源应与项目研发阶段解耦。

项目/章程信息至少应包含：`project_id`、`linked_product_id`、`purpose`、`objectives`、`scope`、`owner`、`approval`、`constraints`。阶段/过程信息则包含：`activity_id`、`process_type`、`inputs`、`responsible_role`、`outputs`、`review`、`handoff`、`status`。它们是可关联的三类对象，不是三个自动批准按钮。

正式成果和批准事件必须分离：`artifact_id + exact revision/blob + full_text + status`，独立的 `decision_id + human decision channel + exact approved revision + scope + limitations + committed_sha + readback_evidence`。来源有分歧时，优先展示差异、限制，不静默晋升状态。

## 七、这次一次性交付请求需要决定什么

本文件是**对已有八项批准的集中说明**，无需再次批准八份已批准原文。它也不是正式提请批准 G0-01 或 P1 的新候选。今后如需修订产品定义，应单列“产品定义修订版”，只请求产品定义本身的决定；如需修订治理/生命周期制度，应单列“治理规则修订”，不得以产品定义批准覆盖其他范围。

**此次不向 GitHub 上传任何新 P 阶段候审文档，不改动现有批准正文。**
