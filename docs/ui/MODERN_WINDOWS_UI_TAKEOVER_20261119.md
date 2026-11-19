# AutoTest｜现代 Windows UI 专项接管与阶段边界（2026-11-19）

> 状态：UI_DESIGN_DIRECTION_CONFIRMED / TAKEOVER_RECORDED / DESIGN_NOT_ACCEPTED / BACKEND_NOT_CONNECTED
> 工作分支：ui/ide-design；此记录仅为 UI 专项职责和用户新方向的受控交接，不修改现行 AutoTest_UI.html，也不宣称 G4 或实际硬件验收。

## 一、用户本轮明确决定
- 接受目前 feature/g4-html-parity-20261115 的阶段性开发成果；先接入正式上位机 main，再推进新一轮 UI 设计。
- 用户明确要求直接改为现代 Windows 风格、浅色主题，保留现有 Windows 原生 WPF/Prism 与正式领域后端。
- 当前新的 UI 专项由本研究对话接管 ui/ide-design，先设计并让用户可操作验收，再由后续受控任务实施真实后端接线。
- 当前功能优先级为手动台架和自动测试，需并行组织不同验收：手动台架必须真实设备接线实测；自动测试先完成真实工程文件/TEST 模型的全量编辑及保存验收，允许执行器后续接入；UDS 交互实验不阻断前两项。

## 二、GitHub 整合依赖
- G4→main 源/目标：feature/g4-html-parity-20261115 → main。
- 交接入口：Draft PR #61 https://github.com/ch3758134-prog/AutoTest/pull/61
- 2026-11-19 检查：PR mergeable_state=dirty，存在 Git 合并冲突；尚未合并。
- G4 与 main 已分叉；审查与冲突消解由项目管理/正式集成责任方落实，不允许 UI 研究线程覆盖主线或绕过物理安全合同。
- 项目负责人对 G4 阶段性成果的认可不能自动转为 FIELD_G4_PASS。G4 保留未验的正式执行、KEY、公共流程、USB/FLASH 行为限制。

## 三、现代 UI 设计目标（候选技术方案）
- 已由用户确认直接选择 WPF UI 4.3.0 作为 Windows 11 Fluent Light 主视觉/控件库，不采用并列的 .NET 原生 Fluent ThemeMode 全局主题，不替换现有 Prism 导航与 TEST/DEVICE 领域层；见 WPF_UI_DUAL_TRACK_IMPLEMENTATION_RESEARCH_20261119.md。
- 以任务为中心采用高密度 IDE 布局，不堆叠大面积彩色卡片，专业工具保持自身工作区。
- 建立统一交互合同：Inline Edit（单元格快速改）、Context Popover（锚定临时编辑）、Inspector（持续复杂参数编辑）、Context Menu（右键对象命令）、Command Search、字段草稿/撤销/校验/持久化状态。
- 不把右键固定为属性对话框，也不强制所有编辑都进入浮窗。根据字段复杂度和连续操作情境选择，并避免嵌套弹窗。
- UDS 第一组真实操作设计任务：选择已保存操作→快速改 DID/子功能→查看完整属性→在合法授权下发送并关联响应→继续改草稿→保存关闭重开。已阻断的实体服务不得由 UI 解除。
- 手动台架继承已批准的 CAN 发送布局：左发送/中 KEY/IGN/夹具/右监控。不能因 UDS 试验推翻此前验收。
- 自动测试继承唯一 TEST DSL 和 StepId，围绕准备/测试/清理、行内编辑、步骤 Inspector、嵌套流程和运行调试任务设计，不生成第二套执行器。

## 四、开发与验收边界
1. PM/集成先处理 PR #61，确定 G4 的正式接收和主线冲突消解，保护 G4 成果及 main 最新事实。
2. UI 专项在本分支仅进行调查、真实任务设计、互动原型和交互合同，不修改 main 的设备或后端接口。
3. UDS 交互原型至少验证：直接编辑、浮层/Inspector 协同、菜单、草稿/保存、历史回填、连续任务、异常和窗口/DPI；不得以静态 HTML 截图代替鼠标操作试验。
4. 用户批准精确 UI 设计版本，项目管理执行领域兼容审查；之后安排单独 WPF 开发和真实后端接线。
5. 使用 Windows Codex 机械构建/启动；实机验证只能由明确实体权限与现场证据支持，不以 Mock 或编译通过冒充真实行为。

## 五、待验及不越界事项
- WPF UI 4.3.0 已在 ui/ide-design 的中央版本、Desktop 项目引用和 App.xaml Light 主题中最小引入（提交 0e49dfd）；尚未 Windows 编译或完成全套现代 UI，更不意味着 G4/main 已集成。
- 当前 UI 专项分支引入依赖已获用户技术方向授权，但进入 main 仍需先解决 G4 PR #61 冲突，并完成 Windows 构建、控件/主题兼容、离线包和许可证复核。
- 现有 AutoTest_UI.html、用户已批准的手动台架 CAN 发送区界面不在本记录中被自动替换。

后续 UI 研究的每次变更以本分支精确 commit 及用户真实操作反馈追踪。