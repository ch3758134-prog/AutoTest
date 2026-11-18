# AutoTest TEST Editor V3｜交互验收与 WPF 实施交接

状态：UI_RESEARCH_CANDIDATE / 待浏览器点击实测与用户体验审查 / 非生产实现
基线：ui/ide-design @ 52f22c29c090af208bac4986132bed24f87f5cb4
唯一演示文件：docs/ui/AutoTest_UI.html
WPF 候选对照：feature/g4-html-parity-20261115 @ a7fc6351f9fb7f048ff420c18a0280006c520d2a
研究依据：RESEARCH_AUTOTEST_UX_REDESIGN_V3_20261117.md
目标：改善浅色高密度工作台、步骤编辑连续性、数据安全与操作定位。保留唯一 TEST DSL / StepId / 物理设备接口，不改真实运行语义。

## A. 本轮真实修改（HTML 设计源，不是 WPF）
1. TEST 步骤多选：鼠标普通选择、Ctrl 增减选择、Shift 范围选择；Inspector 多选态仅显示公共操作。
2. 非相邻兄弟步骤批量复制、粘贴、复制为新步骤、删除、启用/禁用；复制的所有后代步骤分配新 ID，原 StepId 保持不变；批量事务可以整体撤销。跨不同容器的选择会拒绝批量移动，避免结构损坏。
3. 拖拽落点：鼠标在步骤行上半/下半分别前插/后插，拖到 THEN/ELSE/CASE/Loop 子槽或阶段标题则追加到该槽；蓝色落点线/虚线背景；自拖和父容器拖入自己的子孙节点拒绝。支持从同一父容器选中多个步骤一起移动；禁止不明确的跨容器多选。
4. Ctrl+C / Ctrl+V 复制粘贴，Alt+↑/↓ 移动同级选中步骤；支持 Ctrl+Z 回退原 UI 原型事务。
5. 新建直接 KEY 步骤默认只编辑两项：完整 20-bit 掩码 + 保持时间（ms），掩码支持十进制或 0x 十六进制输入与 20 路勾选同步。无自动 ReleaseAll，0 不当作 ECU 的业务“无操作”。保存的命名 KEY 序列保留可显式调用的替代交互，不把二者混成一个步骤参数。
6. 保留此前 V3 的中性浅色紧凑阶段、常用步骤快捷插入、无效字段按 CaseId/StepId/FieldKey 缓存、保存/导出前的未完成字段阻断。

## B. 静态/纯函数复核（不代表浏览器实测）
- 唯一内嵌 JavaScript 可通过 ECMAScript 解析。
- KEY 掩码解析：0、0xFFFFF、6144、192、576、0x1800、超界及非法字符按预期识别；20 位范围外拒绝。
- 字段规则：KEY 掩码、保持时间、CAN ID/HEX 数据分别检查；无效字段不能偷偷写入测试文档草稿。
- 多选模拟：跨分支复制拒绝、同一父容器复制、子树复制后重新分配 ID、父节点移入自身子分支拒绝、合法进入 THEN、批量启用/禁用与删除。纯 JS 运行正确不等于 WPF 的 StepEditTransaction 已完成。
- **待验项目：** 浏览器实际点击、跨 DPI 布局、焦点变化、长用例性能、拖放真实鼠标事件，以及所有 WPF、TEST、硬件操作验收。禁止报告 PASS。

## C. 交由本地 Codex 进行的非 Mock UI 验收
以当前 UI 分支 HTML 运行（它本身是离线设计原型，不得称为设备仿真）：
1. 在 1366×768 / 1440×900 / 1920×1080、缩放 100%/125%/150% 检查三栏、文字、输入框和 Windows 任务栏边界。附每档截图。
2. 新建空用例：准备/测试/清理均可点击添加；TEST 空状态的 CAN/KEY/IGN/等待/IF 快捷插入实际生成可撤销的步骤。
3. 插入 8 条混合步骤：连续编辑、StepId 保持；右侧修改非法 CAN ID 后切到另一条，再返回，字段原文与错误仍在；保存和导出不得丢失当前输入。
4. Ctrl/Shift 多选同级 3 步；复制/粘贴/上下移动/删除/撤销；非相邻多选与嵌套步骤不生成重复 ID。
5. IF/ELSE 拖拽：分别放到目标前、目标后、THEN、ELSE；验证可视落点与实际树一致；父 IF 拖到自身 THEN 被拒绝且文档保持不变。
6. KEY 单步骤编辑：十进制 6144 与十六进制 0x1800 同值；192、576 分别准确回填 KEY 勾选；设置 1000ms 并保存重开；0 与 576 严格区分；不会产生自动释放步骤。
7. 复制包含 THEN/ELSE 的 IF，校验所有子节点新 ID、原节点不变；撤销重做后恢复相同顺序。
8. 断开实体 USB 时仍可离线编辑；界面不能显示“实机通过”或把 UI 演示按钮当实体执行。
9. 保留专业工具的单工程树导航与独立窗口边界，操作后返回原用例，Inspector 选中与编辑状态不混乱。

以上测试未在当前云端运行，Codex 必须提交 screenshots、录屏/日志、失败重现步骤及环境版本。无需自行研究或重设业务合同。

## D. 正式 WPF 落地文件责任与门禁
- WPF 必须继续使用现有 TestDocument/StepNode/StepEditTransaction / 工程资源服务 / 真实 RunCoordinator，**不得**复制 HTML 原型状态对象当第二套 TEST DSL。
- Shell 视觉候选：src/AutoTest.Desktop/Themes/Tokens/{Colors,Metrics,Typography}.xaml；Shell/MainWindow.xaml；Testing/TestEditorView.xaml
- STEP 编辑/Inspector：Testing/{TestEditorView.xaml.cs,TestEditorViewModel.cs,TestStepInspector.xaml.cs,StepRowProjection.cs}
- 字段草稿：在现有编辑器事务边界增加按 StepId 稳定身份的缓存与字段校验；需要覆盖 Reload、Undo/Redo、保存和关闭，不能简单删掉 TryLeaveInspector 就允许丢输入。
- 多选/拖拽：WPF 需要新增可审计的原子 TEST 事务接口后再接 UI，落点结构与 StepInsertionTarget 兼容；不得直接修改 StepNode 集合绕过领域事务。
- KEY 直控：对齐 G3 批准的 CR-KEY-DIRECT-STEP 和实际主控 CAN0 KEY_SIM 帧；HTML 直控外观不是已批准的 WPF 执行命令。
- 版本准入：用户审查 HTML 交互 → 项目管理兼容审查 → G4 UI 专业分支实施 → 本地 Codex Windows Build/Test → 用户现场运行反馈 → 合并许可。未批准前不覆盖冻结 HTML 或写 main。

## E. 仍需决定
- 多选跨不同阶段/不同容器是否允许一次性复制/移动：当前原型 **禁止**不明确跨结构批量移动，建议后续研究整体顺序语义。
- KEY 单段保持 0ms 的执行意义需与硬件合同确认；原型允许编辑 0ms，但不代表实体执行已批准。
- CAN 原始帧 Data 最大长度和 CAN FD 支持受物理 CAN 合同约束；当前原型按经典 CAN 0～8 字节研究。
- 当前 V3 只是 HTML 交互样本，正式运行、报告、烧录、仪表和硬件证据仍以现有 G3/G4 正式合同为准。
