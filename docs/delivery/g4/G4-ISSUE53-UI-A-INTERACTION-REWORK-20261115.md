# AutoTest | Issue #53 | 自动测试 G4 核心编辑交互整改交付

**状态：CODE_CANDIDATE / WINDOWS_BUILD_UNVERIFIED / UI-DIFF_OPEN / NOT_GATE_ACCEPTED**

## 依据
- 用户交付审查报告：`AutoTest_G4_自动测试WPF_审查与整改建议_20261115(1).md`，审查起点为 `e20590354398e3f1e8094aa9c1b45672bb2dc4b9`。
- 正式授权：Issue #53 comment 6906604305，允许 Desktop UI、导航投影、编辑适配以及桌面测试整改。
- 冻结 HTML：`docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html` Blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`，未改。
- 本次迭代起始修订：`73b98a33e818386860134e84ec5ddaf126227fc8`。
- 本文核查源码：`5f559f08228e564cf1b0c9fe5ac18197ff56d9f2`；写入本文后以本分支 HEAD 为精确交付版本。

## 整改完成的源码路径与操作

| 审查号 | 修复/代码 | 用户操作与安全语义 |
|---|---|---|
| EDIT-01 | `TestEditorViewModel.SelectedRow/SelectItem`、`TestEditorView.TryLeaveInspector`、Shell 激活页签守卫 | 点击其他行/页签、鼠标导航前先将未应用属性通过原有校验与 `StepEditTransaction` 自动应用为**可撤销 TEST 草稿**。输入不合法就拒绝离开，原字段及错误保持可见；尚需 Ctrl+S 正式保存 |
| EDIT-02 | 新增 `StepInsertionTarget`：位置由 Phase、ParentStepId、Slot、Index、Append/After 构成；右键动态显示子分支 | “下方插入”严格是 sibling；在 THEN、ELSE、CASE、LOOP body 中添加严格指定 slot。采用最新树 StepId 定位，避免属性应用重建后使用旧坐标 |
| EDIT-03 | `StepInsertionTarget.ValidateNode` 与 Inspector action filter | CASE 只在 SWITCH 的 cases slot 可插入，其他位置不再开放 CASE；cases 区只提供 CASE 类型 |
| DATA-01 | `TestProjectPackageRow` 保存旧 Revision/SHA，另外显示当前正式 TEST 修订，`AdoptCurrentRevision` 要求明确操作 | 测试用例保存新修订后，项目行显示“绑定 rN → 当前 rN+1”；点击“采用当前修订”显示两个完整 SHA/Revision 并二次确认；**只更改选中的 Entry**，项目再次 Save 才持久化。绝不自动追新、不改历史 Run 或生产 Enabled |
| EDIT-04/A3 | TEST 编辑器增加“常用步骤”直达菜单，Inspector 保留真实 DBC 信号编辑及规范字段，添加 CAN/UDS 预留字段中文含义 | 必须先选阶段或合法分支后添加；支持 CAN、IGN、按键、信号读取、Wait、IF、Loop、Expect。未经领域审批的 CAN/Read 扩展类型仍只作为不可执行配置 |
| A5 | 工程概览新增一键创建首个真实测试项目+空白 TEST；测试项目页新增“＋新建用例” | 明确操作时真实创建持久工程资产、固定 TEST 引用、打开 TEST 编辑器；**不塞任何模拟测试步骤、演示数据或假 PASS** |
| UI-A | 用例表把修订信息整合到用例名下方，避免单独大列挤压测试用例名称；更新操作保持在该用例行 | 先检查版本差异，再单独采用；正式默认启用与本次勾选仍互相独立 |

## 安全与模型边界
- 保留唯一 `TestDocument` / `StepNode` / `StepEditTransaction` / `TestProjectDocument`；未增加第二种持久格式或执行器。
- 未修改 `main`、冻结 HTML、`ui/ide-design`、主控板固件、KEY_SIM 固件、DEVICE 安全约束和 Flash 实体编程路径。
- 审查报告新增的 **KEY-SEQ-01 / CAN-STEP-01 / READ-STEP-01 / FLOW-EDIT-01** 涉及正式 Controller/TEST/Runtime/Device 合同，必须由项目管理/对应专业明确批准；本轮没有伪装成已实现。
- 当前项目级 Run/StationLease、生产扫码真实执行仍被正式后端阻断，绝不以 UI 的预检替代 ECU 测试 PASS。

## 验证事实及后续验收
- GitHub 远程分支提交与回读完成。
- 静态检查：整改涉及的主要 XAML 元素成对检查通过，事件方法引用存在；新增/变更的 10 个 C# 文件大括号平衡检查通过；新增 `StepInsertionTargetTests` 与 `TestProjectPinnedRevisionTests` 源码，均未在 Windows 实际执行。
- 本工具环境 **没有 Windows/.NET SDK**，因此没有 Debug Build、WPF 启动截图或像素级 HTML/WPF 差异证据；**不宣称 G4 UI Gate PASS**。

工程师 Windows 实测建议依次检查：无连接创建工程 → 一键创建真实空白用例 → 准备/测试/清理插入常用步骤 → 修改属性未应用直接点另一行 → IF THEN/ELSE、SWITCH CASE 与 LOOP body 分别插入 → Ctrl+S、关闭重开 → 保存 TEST 新修订后打开项目，审阅并采用新修订再保存。失败时只收原始错误及屏幕截图，不要求 Codex 自主修复。

**结论：本轮针对报告 WP-A 的核心代码改进已提交；Windows 视觉与功能验收仍待项目负责人实测。**
