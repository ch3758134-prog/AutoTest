# AutoTest / Project Control｜P 阶段归类与产品定义分离规则批准记录

**决定日期**：2026-11-13  
**来源**：本次 ChatGPT 项目管理对话；负责人在收到两份完整 Markdown 文件及一次性交付 ZIP 后，回复：**“批准”**。  
**记录类型**：负责人对准确交付内容的批准归档；未使用独立电子签名或外部强身份认证。

## 本次获准内容与准确版本

1. **AutoTest P 阶段一次性交付汇总与归类说明（2026-11-13）**  
   - 文件：[AutoTest_P阶段_一次性交付总览_20261113.md](AutoTest_P阶段_一次性交付总览_20261113.md)
   - SHA-256：`6fae8b45df7339fae9fe4bc6bd4a98ee9ce7006f20a811ce990dc4dcdf13dec7`
   - Git Blob：`4f129421cab17e0a344389374e179a101726d62d`
   - 批准范围：对已批准 P0-01～P0-08、P0 Gate 及其版本、角色、审批状态的集中整理、导航与分类；说明产品定义、项目目标和研发过程之间的关系。该汇总**不是替换历史已批准工作产品的新版本**。

2. **Project Control 产品定义与研发生命周期分离：系统行为规则（2026-11-13）**  
   - 文件：[PRODUCT_DEFINITION_LIFECYCLE_RULES_20261113.md](../../../tools/project-control-v2/portable/PRODUCT_DEFINITION_LIFECYCLE_RULES_20261113.md)
   - SHA-256：`79afbf3828189d15040f151dcade07a7f80fd192fad0d3a8b03ecc233a889345`
   - Git Blob：`c03f2f9e4ab51d84d7186c5686e6fc9bfbfd240c`
   - 批准范围：Project Control 系统应分别管理产品定义、项目章程与研发生命周期活动；正式内容批准在对话进行；批准后同轮落档并回读；未批准稿默认不提交 GitHub；管理工具普通代码在 `Project_Management` 分支直接研发。此项属于**治理及系统行为规则的批准**，不是软件已经实现/验收的证明。

原始一次性交付 ZIP 只打包以上两份 Markdown；本次 GitHub 以两份准确 Markdown 原文及此独立记录作为正式可追溯存储对象。

## 对既有批准与后续工作的边界

- **已获批且不重新审批**：`docs/project-management/new-baseline-20261112/P0-01.md`～`P0-08.md` 的原对应版本，及 P0 Gate `PASS_WITH_LIMITATIONS` 与 L-01～L-06 限制；此前事实见 [原始 P0 批准登记](../new-baseline-20261112/APPROVAL_RECORD.md)。
- **本次未批准**：G0-01 v0.1/v0.2/v0.3 候审报告、P1 或任何后续 Gate；没有批准 HOST 或双固件代码修改、Codex READY、烧录或真实设备动作。
- 本次原始交付文本虽然含“建议”“不自动构成批准”等编写时声明，但在本次决定后，**仅其已明确列明的整理内容与分类规则**获得本记录的批准；其余正式工程事项仍遵循各自的授权过程。
- `Project_Management` 为管理规则与档案归档位置；`main`、`Main_Control`、`Key_Sim` 的源码和历史批准内容未因此被重新定义。
- 后续规则实施/软件功能通过需单独的代码变更和测试证据；本次 GitHub 内容提交与回读不构成整个 Project Control 的正式发行验收。

**落档验证**：准确正文 SHA-256 和 Git Blob 如上。GitHub 远端提交 SHA 以本批准记录所在的 commit 和分支回读结果为准。
