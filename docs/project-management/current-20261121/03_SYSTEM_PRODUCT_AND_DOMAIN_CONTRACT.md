# 03｜AutoTest 系统产品边界与领域责任总合同

## 1. 产品、工位、用户任务

AutoTest 是 Windows 本地离线的通用 ECU 集成测试/开发系统，首个现场对象为 EPB，不因 EPB 特性改变整体 DUT 无关定位。当前**单 PC / 单工位 / 同时一台 DUT**。系统由三项平级的实物/软件工程组成：`HOST` Windows WPF Prism、`Main_Control` TestCtr V1.5 MK20 主控、`Key_Sim` EPB_ODD V1.1 MC9S08DZ32 按键模拟板；一组可版本化测试工程组织全部业务配置、测试程序与证据。

主要真实工作序列：打开 ECU 工程 → 选择/编辑用例、DBC 与工程配置 → 以 Development 或受控 Production 确定身份 → 设备连接、波特率/安全预检 → 选定 TEST 当前步骤/单用例/勾选项目范围 → 执行准备/测试/清理（需要时包含 Flash）→ 实时日志、判定、测量证据 → RECORD 与报告 → 恢复工程/报告或执行下一台 DUT。

**设备界面显示“已连接”、USB SET 返回 OK、程序代码通过编译、扫码编号有效均不是 ECU 物理动作已完成。**

## 2. 域与系统单一职责

| 域 | 唯一负责 | 典型交付 | 不允许 |
|---|---|---|---|
| PROJECT | 工程文件、资源身份/类型、版本 SHA、TCF/TBC、DBC、用例项目、引用与恢复 | 稳定资源/修订/冻结快照/冲突诊断 | 直接执行设备动作或伪造测量 |
| TEST | 唯一 `TestDocument / StepNode` DSL、Prepare/Test/Cleanup、控制流、编辑规则、`PlanCompiler`、唯一 `RealRunCoordinator` | StepResult、StepOccurrence、运行/取消/清理、标准化业务判定 | 因不同功能复制第二个解释器 |
| DEVICE | 唯一 WinUSB 会话、0xA5 帧、CAN 双通道角色、设备控制 Fence 与物理观测 | RX/TX、IGN、LED、夹具、KEY、电气会话/epoch | 将 ACK 当 ECU 行为或臆造安全引脚 |
| FLASH / UDS | CanTool 源 Profile、UDS/ISO-TP、Seed-Key/HEX、复位与后验、单独工具及可供 TEST 调用的底层能力 | UDS 阶段日志、NRC、烧录输出与后验事实 | 在 TEST 或独立 UI 中再造协议栈 |
| RECORD | 不可变 Run/StepOccurrence/测量事实、原始 CAN/DBC/WAVE/FLASH 证据、附件库、报告、历史查询 | SQLite / HTML / JSON / CSV，同源完整性/质量状态 | 从 UI 日志文字重新捏造正式 PASS |
| WAVE | 使用实际可获得的 DBC 信号时间序列，采集参考及比较、四态判定和分析证据 | Capture/Reference/Comparison/质量/限值 | 假定有未接入高频模拟采集硬件 |
| SCAN / Production | 唯一外部编号输入、规则、编号-配方绑定/冻结 RunPlan（候审）、批次/复检/放行的明确策略 | 匹配决策/生产计划证据/去重记录 | 扫码即物理执行；重复扫描重复新增产量 |
| UI / INTEGRATION | WPF 主 Shell、导航、文档/工具生命周期、交互、可见状态及后端接线 | 浅色、真实可操作 TEST、独立工具窗口、可恢复工作环境 | 后端规则在各 UI 页面重复实现或只换皮肤 |

需要严格分清**测试工程的产线配置**、**设备控制配置**、**运行时操作员授权**、**真实测量证明**，不能混用一个 JSON 字段同时当作安全许可和配置值。

## 3. TEST 语言与组织

- `TestProjectDocument` 为有序用例条目，可按项目临时勾选本次集合；各条目由原有 TestDocument 定义准备/测试/清理阶段，完整执行范围固定 Revision/SHA。
- `StepNode` 保持稳定 StepId；可容纳条件、等待、循环、异常/变量、子程序和 CAN/DBC/KEY/UDS/FLASH/WAVE 等步骤。未实现类型必须显式诊断阻断，不能“保留图标然后跳过”。
- 公共流程是独立保存的单个 Body 资源，可递归调用并且需要依赖闭包/循环检测，不能等同于子程序旧名字或出厂用例。调用时只复用已有执行器/硬件授权/StepOccurrence 事实。
- R1 当前已提供单步骤、当前用例及选中用例顺序执行源码候选，实际成功/清理/异常及数据冻结必须通过指定 Windows/真机验收。
- 直接 KEY 用户编辑的两个关键量是 **20 Key 完整掩码和保持时间**；允许纯等待，按键的电气组合是否安全由硬件/DEVICE 核验。旧 Controller/RawKey/TCF 文件保留兼容读取，不作为新工程的强制常规前置配置。

## 4. 运行结果不是“若干日志行”

每次真实运行记录 `Project/RunId/CaseAttemptId/StepOccurrenceId/StepId/Phase`、调用/循环路径、起止时间、状态、Expected/Actual/单位/采样质量/资源 SHA/原始证据引用。单个 StepId 可执行多次，不能聚合成一行且丢失次数。

标准事实（CAN/UDS/硬件操作、判定、失败、安全和不可完整采集原因）必须保留；用户可编辑的报告名称、测量附加变量、报告展示策略不允许关闭安全审计。TEST 原始 Verdict 与 RECORD 正式证据确认是不同维度：无测量、错 DBC、附件损坏、缺少因果关联、数据 drop 不得从 TEST Passed 晋升生产 PASS。

**R1 源码现状**：`RealTestWorkbenchService` 已调用 `RealTestRecordSession` 及 Raw Evidence Finalizer；报告已由现有 `RunReportExporter` 输出。同源结果链已在源码候选建立，但单工位真实现场验收未结束。独立 Draft PR #65 不能因为 R1 定点接入就自动算已合并 `HOST`。

## 5. Flash/UDS 与软件升级的判定层

- 独立 UDS 调试工具能够连续交互和保存服务序列是长期产品要求，**与 TEST 内当前支持的 Flash 自动步骤不是同一功能**。
- 自动 Flash 只能引用经严格冻结的 Profile XML、HEX、Seed/Key DLL/文件、目标身份及物理通道，满足危险操作确认和一次性限制，并与其它 TEST 步骤共享唯一设备会话/Fence。
- 下载 ACK、Boot 阶段例程成功、ECU 复位后 APP `10 03/50 03` 应答分别是不同事实；APP 会话响应不证明实际软件 DID/版本或出厂合格，后验缺失时不能强行继续。
- 若未来要定义“纯 Flash 测试正式 PASS”，须由 FLASH+RECORD+生产策略审查该车型要求的独立 ECU 证明方式，不能放宽 DirectDbcSignal 为“有发送就通过”。

## 6. WAVE/SCAN 的研究与技术边界

- WAVE 旧比较器已支持相似度/容差/覆盖/时间对齐，但 W-00/W-01 指出**离散状态线性插值、自动 shift 掩盖响应迟滞、采集时间戳来自 HostArrival、记录中的证明类型冲突**等风险。W-02 v2 的判定模型目前是候审建议，详见 [07](07_WAVE_SCAN_RECONCILIATION.md)。
- SCAN 已有输入、正则、配方匹配与 SHA 校验；`ProjectScanMatcher` 仍仅匹配，不自动准备已冻结的项目级 RunPlan 或执行。S-01 建议为生产增加独立编号绑定表与配方升级，未批准且未实现。研发运行绝不以 SCAN 为前置。
- Production 统计应严格区分扫描数、测试尝试、首次放行与复检；单纯 `COUNT(DISTINCT DUT)` 不必然是新增合格出厂数量。

## 7. 阶段与实际可交付

**R1**：优先支持一套工程、真实设备、最少一条可判定 ECU 信号、真实自动 Flash（安全批准下）、顺序用例、完整日志及报告，以可实际使用为标准。WAVE 已被纳入 R1 当前候选，但判定的物理质量和 RECORD Gate 冲突仍需定点审核。SCAN 生产闭环可以研究/设计并行，不允许因缺扫描枪阻止开发模式首版测试。

**后续**：全产品 UX、DBC 批量编辑、可保存的 UDS 服务链、完整波形特征库、扫码生产配方与独立放行，按各域差异和实际实测滚动实施，不以文档齐全当交付。
