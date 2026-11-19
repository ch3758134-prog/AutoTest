# AutoTest｜完整纯 UI 首轮源码交付与统一操作验收
2026-11-19，用户授权全部批次连续交付源码、不逐批请求用户验收。本候选不是最终 UX_ACCEPTED。
## 实施原则与代码边界
- 只改 feature/ui-product-full-pure-20261119；main 未动。
- 删除原 Desktop 113 旧文件及 UiPreview、G4Workbench、Desktop.Tests 源代码，保留 Git 历史供追溯。
- Desktop csproj 仅 Prism.DryIoc 一项程序框架依赖，**无真实业务 ProjectReference**；保留仓库其他领域项目用于后续独立集成，不在当前 Desktop 中加载。
- 新 App + Rebuild 纯 UI；独立 UI 草稿序列化在 %LOCALAPPDATA%/AutoTest/UI-Rebuild-Study/workspace.json，导入导出 .autotest-ui.json。它们不是正式 ECU 工程、DBC 或 TEST 文件。
- 主窗口使用标准 Windows 边框/最大化，避免自绘窗口盖住任务栏；工具使用 Owner 关系。全局导航：工作台/工程资源/自动测试/专业工具/生产/历史/设备。
## 逐专业实际呈现
- 工程：资源分类/搜索、创建/重命名/删除用例和公共流程、纯 UI 草稿文件管理。
- TEST：用例本次选测独立、准备/测试/清理三阶段、树状步骤、IF/ELSE、SWITCH/CASE、WHEN、TRY/CATCH/FINALLY、Loop、Sequence、公共流程、对象属性持续编辑、DBC/公共流程选择、复制/移动、撤销/重做和 F9 编辑态断点。正式 Run 始终禁用。
- DBC：报文列表、信号表、编辑位定义/别名/系数、校验提示（非正式文件校验）。
- CAN：多报文待发配置、周期时间、DBC 引用、输入组合、RX 观察空态。
- 综合台架：CAN + 20路内部电阻开关组合/保持/动作序列 + DUT 观察，IGN/夹具不连接实体。
- 输入配置：20 Key 命名、通道草稿、组合与保持；不假设电压通路实际映射。
- UDS：服务模板、HEX/超时/请求序列、响应区；真实发送禁用。
- Flash：固件路径、Profile、编程阶段结构；真实烧录禁用。
- Wave：信号选择、空图网格、游标/参考/判定接口位置；无假采集曲线。
- 生产：扫码输入记录，未产生真实身份匹配或生产结果。
- 历史与设备：筛选/状态/诊断布局；无假 RunRecord、ACK、设备连接事实。
## Windows 一次性验收流程
1. 在 Windows 上通过 HOST/tools/run-full-ui.ps1 编译与启动，确认标准最大化窗口不覆盖任务栏。
2. 不创建正式工程直接打开自动测试；新建用例/公共流程，添加三阶段/条件/CASE/TRY/循环/硬件相关步骤；双击/右键、属性修改、↑↓、撤销/重做、F9。
3. 打开工程资源，搜索并重命名、导出 UI 草稿；重新导入、重启程序确认会话状态可恢复。
4. 打开各专业工具；DBC 录入报文和信号；CAN 使用 DBC 创建多条待发并设置周期；打开台架配置多个 Key 的保持动作；返回主窗检查上下文。
5. UDS 创建多条请求；Flash 选择文件及Profile；Wave切换信号；生产扫码输入；历史/设备观察未接线状态。
6. 检查 1366×768 / 1920×1080、100%/125%/150% DPI、鼠标/键盘焦点、多窗口关闭与重启恢复。
7. 记录 UX 问题及其关联批次；任何系统性不合理须回 R/D 设计，不以旧 G4 控件补丁解决。
## 验证状态（真实）
GitHub 旧UI移除及新源码回读：已执行；源文件括号配对与App/主题XAML标签静态配对：已执行。
**Windows WPF Build = NOT_RUN**；**Windows Launch = NOT_RUN**；**用户统一 UX 验收 = PENDING**；**真实 ECU 运行 = NOT_CONNECTED**。
此源码交付应保持 Draft PR，不得声称 Windows 已可运行或正式产品已验收。若本地 Build 报错，以精确错误定位修复后再集中体验。
## 后续产品阶段
UI 用户集中审查通过后，另起受控真实业务集成研究，不在这个纯 UI 分支偷偷恢复旧服务或模拟物理结果。
