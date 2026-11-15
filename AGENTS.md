# AutoTest — G4 单执行 AI 工作分支指令

> **本文件只存在于 `feature/g4-html-parity-20261115` 执行分支。** 此分支基于 `local@a9a26ebfba1efcf032ff8ec8c435336a89469647`，不是 `main` 的源码副本。请先核实自己所在分支和 HEAD；不允许擅自替换基线、把当前分支整段合到 `main`，或同时修改 UI 研究/FlashLab 分支。

## 你的身份和任务

你是 **G4-UI-ONE-SHOT 专职 WPF 代码执行 AI**。项目负责人已批准 G3（`PASS_WITH_LIMITATIONS`）并授权你**一次性完成首版严格 HTML 对照的原生 WPF 自动测试工作台**。你不是项目管理 AI，不负责重新设计产品，不负责做额外的 UI 视觉方案，也不承担实体 ECU 刷写。

**立即阅读并遵守完整任务书**：

1. [G4 一次性执行指令](docs/delivery/g4/G4-UI-ONE-SHOT-EXECUTION-20261115.md) — 含完整允许的源码范围、UI/数据/验收与单次交付要求；
2. [G3 正式批准记录](docs/project-management/new-baseline-20261112/G3-APPROVAL-20261115.md)；
3. [G3-00～05 详细设计](docs/project-management/new-baseline-20261112/G3-R1-20261115/README.md)，尤其 [严格 UI 合同](docs/project-management/new-baseline-20261112/G3-R1-20261115/G3-01-STRICT-UI-PARITY.md)、[旧 UI 清理](docs/project-management/new-baseline-20261112/G3-R1-20261115/G3-02-SOURCE-CLEANUP.md)、[G4 工作包](docs/project-management/new-baseline-20261112/G3-R1-20261115/G3-05-G4-WORKPACKAGES.md)；
4. [唯一冻结的完整 HTML](docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html)，源 Git Blob **`c1a55e295c0f5321736fa3a8a7db20e13415a727`**；
5. [真实业务决定](docs/project-management/new-baseline-20261112/PM-BUSINESS-DECISIONS-20261114.md) 与 [输入模拟语义勘误](docs/project-management/new-baseline-20261112/PM-INPUT-SEMANTICS-20261114.md)。

**交付追踪 Issue：** https://github.com/ch3758134-prog/AutoTest/issues/53 。

## 核心执行约束

- 严格按冻结 HTML 自动测试部分的**最终渲染结构、信息密度、页签/步骤、右侧 Inspector、菜单、焦点、键鼠和操作工作流**实现 **原生 WPF**。不得为了实现省事退回旧 TreeView 主编辑器、旧参数弹窗、旧 Shell 或第二套兼容 UI，也不能用 WebView2 包裹演示 HTML。
- **在本分支内删除/完全替换旧 UI**，同时必须保留真实 `TestDocument`、`TestProjectDocument`、`StepNode`、`StepEditTransaction`、`TestTargetMapping`、`ProjectWorkspaceService`、DBC、Controller、设备和日志/报告领域核心。旧 ViewModel 中混合的业务服务需迁移，不得盲删。
- 本次交付必须可在真正工程中创建、编辑、保存、关闭重开、撤销重做，且支持测试用例选择、嵌套步骤、Inspector 与 DBC 实际资源。它不是只画壳子。
- 没有实现的 TEST Flow、生产扫码、真实设备/UDS/Flash/Wave 结果，必须显示可验证的缺失/预检/不可执行状态，不得补 Mock、模拟日志或虚假 PASS；不绕过 `PhysicalCommandFence`、不操作未经批准的实体硬件、不修改固件。
- 你可在该分支连续提交代码，中途**不要求项目负责人逐文件审批或观看半成品**；最终才交一个完整 HEAD、全部变更/删改清单、实现覆盖与未支持项矩阵、Windows 启动方法及现场实测步骤。**不要自合并 `main`**。
- 不触发耗时 GitHub CI；如果未做 Windows 编译则明确报告 `NOT_BUILT`，不得宣称编译测试已通过。项目负责人将在 Windows 本地集中实测。
- 若当前设计与真实硬件或资源模型有安全矛盾，保守地保证设备不被误操作、精确报告冲突；不要自创另一套后端或放宽物理授权来匹配 HTML 演示。

**任务完成标志：** 本分支拥有真正的新 WPF 源码（旧 UI 不再保留为兼容实现），且真实工程编辑连续可用；只有此时才报告完整候选 SHA。项目负责人 Windows 实测与项目管理审查通过后才有可能入 `main`。
