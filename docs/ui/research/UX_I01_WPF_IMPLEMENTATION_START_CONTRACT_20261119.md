# UX-I01｜首轮全产品 UX WPF 实现启动合同（待正式执行）

身份：UI 实施专项。依据 R01～R06 研究及用户已确认的浅色、现代 Windows、真实正式模型、整体重设计方向。

## 1. 实施位置与保护

实施时从 GitHub 最新 main 建立全新短期 UI feature 分支。ui/ide-design 继续只归档研究与用户 UX 评审。本任务不在旧 feature/ui-rebuild-20261119（已暂停）、旧 G4 XAML 或 feature/host-fluent-integration-20261119 上直接续补。main 未获批准不写入。

## 2. 代码必须具备的真实交互闭环

- 启动整个真实 WPF/Prism Shell；没有工程时仍能查看完整软件工作入口；打开工程、创建工程、未归档编辑入口明确。
- 真实 TestDocument.Create + StepEditTransaction 完成添加步骤、三阶段流程编辑、复制/移动/撤销/重做，不创建第二 DSL。
- 展示层具有 Unbound/Bound 的合法会话状态；不伪造 TestEditorSession 的 ProjectId、AssetId、FileHash，正式存盘与工程归属使用受审查的 PROJECT 接口。
- 编辑与运行页共享当前正式工程/项目/用例；普通编辑离线可用，真实运行未经服务授权完全禁用。
- 工程资源浏览有搜索/选择/预览/打开/关闭/保存状态，面向实际操作而不是先新建才能看到编辑功能。
- 取消/关闭时保留未保存数据的风险提示与恢复可能；技术错误定位当前字段和对象，不能用弹窗锁死导航。
- 原生 Windows 标题/最大化/任务栏，WPF UI 4.3.0 Light，单 Prism Shell、正确焦点、右键/快捷键、各屏幕/DPI 下可用。
- 工具窗口生命周期/主窗口操作关系完整，未接线硬件执行按钮不伪造请求/ACK/CAN/报告。

## 3. 直接拒收情形

只有 Fluent 换皮；仍使用旧只读表格+多弹窗；强迫工程取名才能编辑；为试用另外新造一套测试模型；正式保存绕开 PROJECT 版本/指纹校验；任一工具打开就自行重连/抢占 USB；没有实际数据却显示测试 PASS。

## 4. 首轮实施验收（Windows 真机）

Codex 只按指定精确提交拉取、还原、编译、启动和汇报完整错误；UI 专项亲自修复设计/代码，用户提供实际鼠标键盘操作体验与截图。至少逐项验收 R06 A01～A10，并记录主窗口/工具窗/资源/字段/草稿及恢复的行为。编译 PASS ≠ UX PASS ≠ FIELD PASS。

## 5. 实施计划与全产品后续范围

UX-I01：Shell/会话/正式 TEST 领域编辑/资源导航/视觉与键鼠闭环。
UX-I02：TEST 完整专业编辑 + PROJECT/DBC + 综合台架 + UDS + Flash + Wave + 输入配置 + SCAN + RECORD + DEVICE 的专业编辑/工作窗口交互，跨工具数据与状态统一。
UX-I03：从已验收 UI 到真实服务的受控集成与实机测试，不允许 UI 造第二执行器/协议栈。

专业工具当前涉及旧源码与实际接口的可复用部分需逐个评估，但不以它们的旧 UI 形状约束新设计。不要把只有几个空工具窗口的版本标记为完整交付。

状态：READY_FOR_UI_IMPLEMENTATION / WPF_NOT_STARTED / USER_ACCEPTANCE_NOT_RUN / MAIN_UNMODIFIED。
