# AutoTest｜视觉与操作一体设计研究（独立专题）

## 专题目标
不是给旧 WPF 换皮，而是将原 R01～R06 的操作研究与视觉层级、布局、组件、信息密度和状态反馈双向验证，形成现代专业 Windows 工程应用的可审查设计依据。

## 已完成的第一轮
- [V01 原则研究](V01_OPERATION_VISUAL_INTEGRATED_PRINCIPLES_20261119.md) —— 10 条互相佐证的操作/视觉设计原则、取舍、TEST 截图反例与下一轮任务。
- [V01 官方来源登记](V01_SOURCE_REGISTER_20261119.md) —— Fluent、Windows WPF、WCAG、JetBrains、NI、Ignition、ecu.test、Carbon、NN/g 的证据与适用边界。
- 状态：RESEARCH_FOUNDATION_CANDIDATE；PAGE_DESIGN_NOT_FROZEN；USER_UX_NOT_VERIFIED；NO_WPF_CHANGE。

## 已经确认的来源缺口
R01 为研究交接摘要，原始《现代复杂工业级 PC 软件的产品与交互设计理念研究报告》v1.0 尚未在本轮文件或连接器中取得原文，不得宣称逐字重新审阅。R02/R04/R05 关键内容已在本轮打开；R03/R06 的既有归档可继续作为后续输入。成熟产品的公开文档与截图不等同于本专项安装后实际使用测试。

## 下一轮 V02（不得跳过）
1. 从三个具体连续任务开始：TEST 编排、DBC 信号编辑、Wave 分析；收集官方 GUI 的完整场景与窗口状态，不用宣传截图代替任务研究。
2. 每项任务都制作“对象—操作—视觉信号—错误/恢复—屏幕空间”的对照；识别冲突并做布局替代方案比较。
3. 首先完成 TEST 至少两套明显不同的完整页面候选，含有真实数量的步骤、参数和错误状态；检查 1366×768/150% 等边界。
4. 再推出初步语义化字体/颜色/间距/状态 token 和针对 TEST 的专业组件规格。
5. 进入 WPF 前完成静态视觉审查和具体任务演示的内部质量检验。

## V01 研究结果不授权
- 不授权直接修旧 MainWindow、UiKit、TreeView 的按钮或字号；
- 不构成全产品页面设计与 WPF 实施验收；
- 不把规范中某个 px 值强制复制为 AutoTest 的控件尺寸；
- 不改变已有设备和业务合同，也不允许假 ACK/假 PASS。

## V02 必须回答的反证问题
- 若控件变得漂亮，用户是否更快找到对象及命令？若没有，外观改造不成立。
- 若按任务提升了交互效率，屏幕是否清楚区分选择、编辑、保存与实际执行？若没有，交互设计不成立。
- 若 1920×1080 看起来舒适，1366×768 或 150% DPI 下是否仍能操作核心对象？若不能，布局设计不成立。
- 若多个专业工具使用同一设计语言，波形/DBC/TEST 的主要工作区是否仍有专业性？若没有，共同组件系统过度泛化。

## V02-01 结构化测试编辑器：分批交付（2026-11-19）

- [研究：TEST/ecu.test/JetBrains 实际机制、视觉承载、可迁移边界](V02-01/RESEARCH_TEST_EDITOR_20261119.md)
- [设计候选：中央步骤 + 右侧属性 / 宽步骤表 + 下方参数的比较](V02-01/DESIGN_COMPARISON_TEST_EDITOR_20261119.md)
- 状态：研究与方案比较已留档；两种完整可操作视觉方案尚未完成；**无 HTML、无 WPF 修改、未冻结页面**。
- 用户允许按批次验收；后续继续以真实内容与相同任务脚本检查版式，不用空白页面决定视觉。

## V02-02｜TEST 步骤结构、键鼠命令、字体层级与复杂内容（2026-11-19）

- [研究证据：阶段/分支/叶节点、快捷键、对象选择与视觉对应](V02-02/RESEARCH_STEP_HIERARCHY_COMMANDS_VISUAL_20261119.md)
- [统一高密度样本：12 用例、40 步、X01～X16 操作脚本](V02-02/TEST_TASK_AND_DENSE_CONTENT_FIXTURE_20261119.md)
- [详细设计候选：对象结构、Inspector、移动/插入/Undo、响应式布局、文字/颜色层级](V02-02/DESIGN_STEP_EDITOR_VISUAL_INTERACTION_SPEC_20261119.md)
- [研究—设计—验证追溯与 V02-03 交接](V02-02/REVIEW_TRACE_AND_HANDOFF_20261119.md)
- 状态：RESEARCH_RECORDED / DESIGN_CANDIDATE / VISUAL_PROTOTYPE_NOT_RUN / NO_CODE_CHANGE / USER_BATCH_REVIEW_AVAILABLE。
- **下一轮 V02-03**：以同一 40 步内容验证 A/B 两种不同可视方案及窄屏状态，防止在真实视觉未检查前直接实现 WPF。仅用 Markdown 归档，不制作 HTML。
