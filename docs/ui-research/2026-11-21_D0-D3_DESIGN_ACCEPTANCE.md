# AutoTest｜D0–D3 独立产品 UI 设计验收候选（2026-11-21）

## 交付状态
- 本轮完成 D0 产品调研、D1 信息架构、D2 视觉与交互规范、D3 五工具 + 自动测试 + 运行调试 + 工程/生产工作流的整体 **交互原型**。
- 离线单文件 HTML 和附源码、报告、截图的 ZIP 已在本次 ChatGPT 对话提供。**GitHub 此文件是规范归档；完整前端原型暂作为对话附件，不谎称代码已提交 GitHub。**
- Gate：待用户 **设计验收**。没有宣称 WPF Build PASS、正式 TEST/PROJECT 联调、ECU 测试成功。
- 保留此前用户认可的 UI-01/02 Fluent 浅色方向，但**不得把 R1 原 XAML 当新 UI 设计**。历史 Draft PR #73 不作为本设计完成的依据。

## D0：需求与参考研究
业务边界：
1. 通用 Windows 离线 ECU 开发/测试上位机，单 PC / 单工位，EPB 为首样。
2. 主窗口连续完成工程→测试用例→准备/测试/清理→步骤添加→右侧参数→调试/运行→真实证据。
3. 五种独立工具：DBC / UDS / 烧录 / 波形 / 手动台架，子窗口同主进程、Owner 绑定、主窗关闭一起关闭；**不做独立 CAN 工具**。
4. CAN 原始 8/64 字节为核心，DBC 只是可选辅助；20 Key 为 5 路模拟电压输出的电阻组合，Key 编辑只涉及组合与保持时间。
5. 本次勾选≠Enabled≠断点≠实际运行状态；扫码枪只输入唯一 DUT ID，不得阻断工程开发/调试。
6. 所有实体反馈、UDS 响应、CAN 报文、波形、PASS/FAIL 都必须来自真实设备/执行器/RECORD，禁止模拟。

研究对象与借鉴：
- [NI TestStand Step Settings](https://www.ni.com/docs/en-US/bundle/teststand-api-ref/page/tsref/step-settings-pane.html)：步骤当前选择与设置面板上下文一致，兼顾调试。
- [tracetronic ecu.test Editor](https://docs.tracetronic.com/help/ecu.test/en/Main-program/Editor/index.html)：工程、Package、配置资源与文档标签组织，项目引用和执行工作流分离。
- [Visual Studio windows](https://learn.microsoft.com/en-us/visualstudio/ide/customizing-window-layouts-in-visual-studio?view=visualstudio)：Document Window 与 Tool Window 分工、布局恢复和 floating ownership。
- [NI DIAdem VIEW](https://www.ni.com/en/shop/data-acquisition-and-control/application-software-for-data-acquisition-and-control-category/what-is-diadem/interactively-visualize--analyze--and-report-data-with-diadem.html)：测量曲线、缩放、游标、参考叠加与分析/报告分层。
以上仅引用交互原则，不复制它们的界面外观，也不把旧 R1 UI 当竞品样板。

## D1：产品信息架构
主窗口：菜单/命令→工程树（用例/文件/资源引用）→文档标签→中央步骤编辑/运行视图→右侧常驻 Inspector→底部问题/时间线/CAN/变量。
自动测试不是弹窗。切编辑/运行共享用例、步骤、断点与资源上下文。
工具窗口各具不同布局，不复制主树：
- DBC：报文列表 + 信号可连续编辑的表格 + 右侧完整参数/别名，可从空数据库直接添加。
- UDS：诊断服务序列 + 当前服务完整请求参数 + 响应/NRC/变量观察。
- 烧录：三文件 + Flash Profile / Boot ID + 阶段/日志/证据，实体执行前检查。
- 波形：真实参考/测量数据源 + 宽曲线画布 + 缩放/双游标 + 质量、对齐、容差配置。
- 手动台架：20 Key/IGN/夹具 + 按键浮窗 + 多条 CAN 发送编辑 + 右侧 CAN 日志。
生产/扫码：外部编号→版本化规则匹配→冻结运行配置→预检→执行/记录；研发调试无需扫码。

## D2：设计规范与交互规则
- 主色：#226ed0，背景：#f6f8fb，主内容 #ffffff，文字 #223247，次级 #65758d，边界 #e3e9f1；准备/测试/清理以低饱和色带区分。
- 主窗口推荐：标题 38px，命令 51px，一级导航 41px，左树 230px，右 Inspector 344px，步骤行 40px，底部观察 158px（可折叠）。
- 新增步骤必须先插到列表并自动选中，再右侧编辑；搜索只高亮不破坏树；F9 断点、Ctrl+Space 添加、Ctrl+Z/Y 撤销重做、Ctrl+S 保存。
- Key 20 组合和保持时间一组操作；CAN 字节编辑优先并提供可选 DBC 辅助；“目标状态”和“设备实际确认”必须严格分离。

## D3：原型当前交互覆盖与用户可验收内容
已完成离线可操作界面（网页原型，不代替 WPF）：
- 3 个明确标为设计样例的用例、准备/测试/清理、测试项目本次运行复选框、文档标签与步骤列表。
- 搜索步骤高亮、添加步骤选择器、IF/ELSE/循环子步骤、右键操作、复制、移动、禁用、断点、撤销/重做。
- Inspector：CAN ID + 8/16/32/64 字节 + 单次/周期 + DBC 可选引用；20 Key + 保持时间；DBC 信号判定、UDS 请求、Flash/WAVE 配置。
- DBC 独立窗口：直接新建报文/信号、编辑信号表与中文别名、DBC 文本预览与导出。
- UDS 序列编辑与空态响应；Flash 文件与前置条件；波形 CSV 两条曲线、放缩、双游标；手动台架按键浮窗与 CAN 编辑/监视空态。
- 主窗口运行/调试模式、当前步骤/用例/勾选范围的非实体预检，未连接的硬件按键禁用，**不产生 PASS/FAIL**。
- 离线设计 JSON 的浏览器保存/导入/导出，仅用于设计体验，不是正式 AutoTest PROJECT/TEST 数据。

### 验收门槛
1. 主窗口测试编辑器始终内嵌；工具五窗独立，主窗关闭工具随之关闭。
2. 真正能走通“用例→步骤→常驻参数→资源编辑工具→回到用例→预检”的交互链。
3. 不重复开发正式 PROJECT/TEST/DEVICE/RECORD 模型；WPF 实现必须服从新设计，不允许回退旧 R1 UI。
4. 设计验收通过后另立 Gate-UI，才允许把它转换为正式 WPF 并通过 Codex Windows Build/启动检验；再做 Gate-Integration 实体接线。

## 正式后端边界（未来 E1–E5）
- PROJECT：`HOST/src/AutoTest.Desktop/Projects/ProjectWorkspaceService.cs`（唯一工作区、资源版本、指纹）。
- TEST：`HOST/src/AutoTest.Testing/Authoring/TestDocument.cs`、`TestDocumentCodec.cs`、`StepDescriptorCatalog.cs`、`StepEditTransaction`（单套测试执行 DSL）。
- DEVICE/UDS/FLASH/WAVE/RECORD：各专项正式合同与真实证据，不复制执行器、不造虚拟响应。
- UI 原型中的内存设计数据不可进入正式执行器，也不能伪装为现场实测工程。

**本次是 Gate-Design 交付候选，等待用户就整体操作与视觉进行设计验收。**
