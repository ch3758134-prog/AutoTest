# 10｜R1 全产品用户语言与操作流程重构记录（2026-11-26）

**性质：** 正式管理追踪、经负责人当轮明确要求实施的产品重构；不是编译/现场验收报告。
**权属：** 管理归档 `Project_Management`；研发源代码仅 `R1`；`main` 不因本报告变化。
**本轮代码基线：** R1 `9ce04479df31f9c33ba540cfe72af54c5ba7edd1`（核对日期 2026-11-26）；重构前 `20ea335258c21cd1d71dd243228e93fff4a54e15`。
**证据等级：** `DECISION_APPROVED`（用户要求）、`SOURCE_EXISTS`（GitHub 变更）、`BUILD_NOT_VERIFIED`、`WPF_NOT_VERIFIED`、`FIELD_NOT_VERIFIED`；不得写为 PASS 或 9 分。

## 1. 触发原因与适用范围

负责人指出：上位机作为成熟交付产品，应整体采用使用者能够理解的语义；工程实测等页面大量暴露内部数据结构、版本沿革、来源免责声明、USB ACK 与执行器原始状态，操作重点被稀释；不是简单修改几句文案，而是**系统性研究、全量查找、重构式整改**。

本次清查依据 R1 实际 XAML、ViewModel 动态消息、工程/生产扫码与工位配置状态、硬件执行器控制逻辑。覆盖主窗口、工程整组实测、正式生产、工程/测试编辑、工位配置、手动台架、扫码枪、Flash、UDS、DBC、波形和记录报告。

**覆盖 ≠ 已验收穷尽。** 尚不能宣称所有运行分支、硬件接线、各显示缩放和操作路径均已正确。

## 2. 取代关系与产品统一原则

下述当轮决定取代早期“功能存在即充分解释内部技术细节”的呈现方式，但不取代硬件安全、资源版本和真实数据合同，也不降低正式检测准入。

| 原呈现/实现问题 | 现行决策 |
|---|---|
| “真实整组实测·非生产”“正式 TEST 事实”“项目周期 CAN USB ACK”等直接占据首页 | 页面围绕**当前 ECU、下一操作、用例进度、当前步骤、结果/异常**组织；详细通信和底层证据按需展开 |
| GUID、Schema、ProjectId、内部发布指纹及旧方案迁移历史上屏 | 常规操作页不展示；内部可保留稳定身份/哈希与详细审计 |
| 文案当成业务状态（例如根据结果中文前缀决定夹具释放/扫码告警） | **状态事实与显示翻译分离**：设备/执行状态用结构化事实，文字仅呈现；保留同等或更严格的实体门禁 |
| 每步的解释与免责声明常驻 | 仅在动作、错误或诊断上下文出现准确且可操作的信息；不能删除真实安全限制 |
| 开发工具、正式生产配置、扫码匹配互相叠层 | 一次工程设置与专业工具调试职责清楚，扫码原文显示独立于工程匹配 |
| 改名导致操作员误解功能已接通 | 显式“未接线/不可用/待验收”，不能把软件层成功充当 ECU 电气实测或生产 PASS |

### 分角色的专业术语边界

- **生产操作员：** 设备状态、扫码编号、检测进度、结果、失败恢复、复检历史。
- **测试工程师：** TEST 用例、CAN/DBC、期望/实际值、断点或停止状态、可追溯证据。
- **设备调试人员：** USB 命令/ACK、GPIO、CAN 帧、UDS/PDU、固件哈希、原始日志等进入诊断/专业工具。
- **必须保留的边界：** ACK 不代表 ECU 收到；GPIO 回读不代表气缸到位；扫码匹配不意味着已授权启动；开发实测不等于正式 Inspection；危险烧录需授权，正式生产真实 START/记录必须接通且验收。

## 3. 本轮 R1 源码变更与结构性整改

以 GitHub compare `20ea335...R1` 为当前代码证据，可持续更新 R1 HEAD，而不是凭“已提交”声称真实完成：

1. `EngineeringVerificationWindow.xaml` 重构为“工程测试”任务中心，减少重复操作导航，把通信诊断与执行日志折叠，保留真实用例步骤列表。
2. `AutomaticTestHomeViewModel.cs` 新增由资源、USB、CAN、扫码和实际运行事实派生的 `EngineeringNextAction`；原错误详细文本留在按需诊断处，不再同时承担操作导航。
3. `AutomaticTestHomeView.xaml.cs` 引入 `OperatorStateTextConverter`，执行器仍持有原内部状态键，UI 另映射“进行中/通过/失败/资源需检查”。
4. `AutomaticTestHomeViewModel.Run.cs` 以 `completedNormally / periodicSessionClosed / operatorCancelled` 等事实代替根据中文显示字串判断夹具释放和终止；Flash 中断后 ECU 状态未知的判断改为独立 `flashAttemptStarted`。最终释放仍受实体授权、步骤成功、无错误和主控会话等原门禁约束。
5. `AutomaticTestHomeViewModel.Scanner.cs` 用独立 `_scanAlertRequired` 判断扫码告警，避免改中文句子导致告警漏报；原始扫码与正式身份是否接受继续分离。
6. `ProductionAdmissionReview.cs` 12 项门禁/稳定错误 `Code` 不变，面向操作员的 Stage/Headline/Guidance 重构为“检测工程/设备连接/等待扫码/启动设备/检测记录”及下一步操作。配套单元测试验证各失败路径不输出实现术语。
7. `MainWindowViewModel.Production.cs` 将启用工位工程的对话框改为可理解的工程名称与行为提示，仍在后端校验固件/TEST 指纹；启用不触发硬件。
8. `MainWindow.xaml`、`AutomaticTestHomeView.xaml`、`SerialScannerSetupView.xaml`、`RecordHistoryWindow.xaml`、`ProjectConfigurationOverviewView.xaml`、`ManualBenchWindow.xaml`、`WaveWorkbenchWindow.xaml`、`TestEditorView.xaml`、`TestProjectWorkbenchView.xaml`、`FlashLabWindow.xaml`、`UdsWorkbenchWindow.xaml`、`DbcWorkbenchView.xaml`、`ProductionPreparationWindow.xaml` 统一导航、标题、控件、技术信息层级。
9. 原 CanTool 文件参考导入未以“兼容”承诺当做正式工程运行路径；保留调试入口并明确与工程原生烧录配置的区别；不允许以导入参考文件替代工程固定资源。
10. `RecordHistoryWindow.xaml.cs`、`SerialScannerWorkbenchViewModel.cs`、`TestEditorViewModel.cs`、`FlashLabWindow.xaml.cs` 等动态消息同步清理无效解释和过度技术信息。

**本轮没有**删除真实硬件约束、没有绕过正式生产 12 项准入、没有以工程实测报告替代正式生产记录、没有创建第二套执行器，也没有将工作合并到 main。

## 4. 待关闭的 P0 / P1

| 等级 | 遗留项 | 本轮状态 |
|---|---|---|
| P0 | 最新 R1 Windows Release Build/启动、XAML 运行时绑定及 WPF 缩放 | `NOT_VERIFIED` |
| P0 | 工程/扫码枪真实 COM 接收、重复编号、非法编号与临时切换 | `FIELD_NOT_VERIFIED` |
| P0 | 实际夹具压紧/释放、实体 START、硬件到位反馈、停止/异常 | `FIELD_NOT_VERIFIED` |
| P0 | 前置真实 FLASH/APP 后验、全部正式 TEST、逐步 RECORD/报告一致性 | `FIELD_NOT_VERIFIED` |
| P0 | 生产准入中 START 与 InspectionWriter 真实连接前必须阻断 | `SOURCE_GATE_RETAINED`，现场待验证 |
| P1 | 剩余动态提示、少见弹窗和专用工具详细页面穷尽复审 | `IN_PROGRESS` |
| P1 | 窗口大小、缩放、对比度、焦点、导航效率、浮窗生命周期 | `WPF_NOT_VERIFIED` |
| P1 | DBC/波形/UDS/Flash 高级诊断展示是否与真实能力一致 | `FIELD_NOT_VERIFIED` |

## 5. 统一验收脚本（Windows Codex + 用户实机）

1. 锁定 R1 HEAD：Windows SDK、Release Build、Release 测试、WPF 主窗口及全部独立工具启动；保存原始错误、警告和测试计数。
2. 普通操作路径：自动打开工程；工程测试扫码原文、当前 ECU、主控/CAN 状态、下一步；完整用例树；开始、停止与报告入口均可发现。
3. 失败恢复：不连 USB、扫码断线/换 COM、非法编号、无工程、旧资源/烧录配置变化；错误说明写“问题+影响+下一步”，不泄漏 ProjectId/Schema/历史重构。
4. 真实动作：工程测试须在首次压紧/烧录前告知影响并确认；取消不会操作 ECU；运行时跟随步骤；功能 FAIL/通讯中断/停止后不能误标 PASS 或擅自释放夹具。
5. 正式生产：无可信物理 START 或生产 InspectionWriter 时不能开始或写合格结论；完整真实链路后才接受正式结果，复检独立归档。
6. 专业工具：手动台架 CAN/KEY、Flash、UDS、DBC、波形在常用任务下突出操作，内部日志可访问但不常驻挤占；浮窗随主窗退出。
7. 报告：按 ECU 编号查询同一 DUT 多次独立检测，查看当前步骤测量/失败原因和原始证据；缺失证据不能称合格。
8. 在至少常用 100% 和 125% Windows 缩放及不同窗口宽度下核对焦点、布局、可读性、错误提示与没有重复弹窗。

## 6. 分支治理与后续处理

- **R1** 是唯一实现分支，本次 UI/UX 重构与真实执行器同源维护，未另建新分支。
- **Project_Management** 仅维护管理事实。本文件作为 11-26 当轮新增决策记录，老档案保留追溯，冲突项以用户后批准决定为准。
- **正式准出尚未发生。** 不以静态源码回读、主观评分或“已提交”替代 Windows Build、WPF 操作和真实 ECU/工位验收。
