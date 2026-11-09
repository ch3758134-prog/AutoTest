# 02｜模块、Git、UI 与 SAEF 的恢复工作方式

> **本文件是待审治理候选，不授权修改共享接口、设备输出或物理烧录。**

## 研发组织
原 G2 的逻辑模块不直接等同一个程序集、一个 Git 分支或一个独立 EXE。各专业可以长期并行研发，但所有跨模块接口必须来自同一已经检查过的 main 基线。当前 main 先按现状复测，不强制后端重构。

- DEVICE：USB/Station/唯一 CAN Hub/收发授权及独立 DeviceLab
- PROJECT：Project/Resources/DBC/Profile/Revision
- TEST：TestDocument/PreparedPlan/Runtime/StepFact
- UDS、FLASH、WAVE、SCAN、RECORD：专属服务、专业 View 与模拟/真实测试
- UI FOUNDATION：Token/字体/间距/共享控件/主题
- UI SHELL：唯一 Prism 主窗/工作区/文档生命周期/全局导航
- UI QUALITY：真实 WPF Gallery、固定情景截图、UI 自动化和操作验收（非第二套产品 Shell）
- INTEGRATION：唯一启动入口、接口适配、CI、跨模块测试，不复制专业业务和 UI

## Git 规则
1. `main` 为唯一正式代码主线。**本次恢复不宣布 main 当前 SHA 已验证。**
2. 专业长期分支只保存尚未合入的模块工作增量；在其上不得继承另一个未合并的大型全系统集成分支作为长期基础。
3. 每个模块维护 `ModuleHeadSha / CommonBaseSha / ContractRevision / StandaloneEntrypoint / TestEvidence`。可并行研发但不凭名字假设合同兼容。
4. 每次完成一个可独立自动验证的小增量，申请 PR 回 main；出现依赖时用显式依赖图与有限 stacked PR，或先合共享最小合同。避免超大跨模块 PR。
5. 发布和现场试验使用精确 SHA + 启动程序 + 模式 + 设备 Profile + 原始证据。CI PASS、窗口可启动、真机 PASS 相互独立。
6. 模块从 main 建立/同步必须核对变更与合同，不能未经审查 force-push、覆盖他人历史。
7. 旧 R0-LAB/G4-R1 候选从现行交付队列退出，不代表旧代码全无价值。通过源码提取审计后决定复用，不再直接向这些旧 PR 追加功能。

## UI 从零重建**可用体验**，不是必须重写后端
- 先写清楚用户操作任务及参考软件，**不把 13 个导航页名当 13 个已设计页面**。
- WPF DesignSystem：统一字体、控件高度、Tree/Grid、Dock/Tab、属性/日志面板、状态/错误/高 DPI。
- WPF Gallery：真实运行每个控件/页面，模拟内容、加载、空、错误、长文本与小窗；输出自动截图。
- UI Shell：一个统一 IDE，专业 UI 独立可测，正式接入同一个资源/Session/Trace。
- 各专业拥有其业务交互；Foundation/Shell 拥有跨专业视觉与布局；同一路径只能有一个变更所有者。
- 每次 UI PR 必须附实际 Windows WPF 截图（不是 AI 生成图）、交互走查结果和差异。没有实测就记录 NOT_RUN。
- 用户没有确认的 UI 设计没有“旧页面已经做了所以必须保留”的既得地位。
- 第一轮建议仅用 PROJECT/DBC、UDS 与 WAVE 的典型工作流检验 DesignSystem，而不是一次把全套页面做成空壳。

## SAEF 恢复定位
SAEF 是项目管理、需求/架构/配置/证据和审批系统；GitHub Issues/PR/Actions 是实现活动记录；两者不能重复维护独立“当前完成率”。生成 HTML 为 **增量更新的可阅读总览**，从受控事实索引生成，展示产品功能、现场设备、架构、当前问题、决定变化和下一步；历史细节另存，不以追加无限历史段落污染当前页面。先恢复丢失的源文件/工具/批准资产完整性，再实现自动生成器。

## 暂时执行策略
- 当前旧 PR 应标记为 SUPERSEDED/REVIEW_ARCHIVE，不应直接合并。保存精确 SHA、PR 链接及未处理独有内容；旧分支可在明确确认无需远端继续引用时删除。
- 新源码开发只从复验通过的 main 开始；若 main 有问题先修复后端或最小启动问题。不要将最新旧 R1 UI 候选整体搬到 main。
- 不以发布 R1 所有功能为首次可用目标；按真实使用场景挑选下一个可验证的纵向切片。
