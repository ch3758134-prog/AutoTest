# G2-08｜架构决策、G3 交接、迭代与 Gate 评审（架构候审 v0.7）

## 当前候审决定

以 `AUTHORITY_AND_DECISIONS.md` 为决策阅读入口。本工作产品承担正式 Gate 管理：单一 HOST、统一设备与CAN、资源版本/运行快照、独立扫码身份、UI完整子系统、固件可迭代和按批次验证。**所有是待本轮 G2 批准的技术建议，不能提前写作已经通过。**

## G3 独立开发的检验标准

G3 只通过本包 `docs/G2`、`tables`、`G2_TO_G3_HANDOFF.md` 与本包附带的 G0/G1 证据就能获得系统级设计决定；不必翻聊天记录或在 G2 v0.2～v0.6 之间猜。它仍须把真实 USB 报文、固件参与编译源码、具体 C#/XAML 文件、类方法和测试实现细化，这是 G3 的正常职责，不是重新做总体架构。

## 并行工作包与依赖

`G3-0` 对齐共享身份/错误/时间/命令 DTO → `A1/B1/C1/D1/E1/E2/E3/E4/R1` 可并行详细设计；只有经合同测试的能力才能被后续集成引用。

先真 USB B1，再B2双 CAN；B3/B4输出须 B0 安全放行；B/C/D 的工程编辑/Mock测试/真 WPF 可提前并行获得用户反馈。UDS 先模拟再无破坏实机，波形先回放再采集，刷写先模拟 Bootloader，**真实刷写另行授权**。

## 反馈与变更

现场事实与设计不符 → 记录事件/原始证据 → 判断属于 G0 事实、G1 需求、G2 公共合同、G3 详细实现还是 G4 代码缺陷 → 工作包内的修改按授权处理；跨受控基线变更提交 ADR、影响范围、回归测试、审批记录 → 再实施。每轮都更新实际进度，不能用HTML产出代替实测进度。

## Gate 准出判据

1. 八份文档、12 ICD、唯一模块和状态所有者一致；无 G2 候审之外的第二套“权威版”。
2. 46 条 G1 已映射到 G2/G3 与验证方向；重大未知有实际实验与最迟决策点。
3. G3 能独立接收工作包输入/产出/依赖/首次证据，不能自行做跨模块架构选择。
4. 原始硬件、固件、历史 `src`、Honeywell USB、CanTool 均按证据级别说明；不虚报新 HOST 实机状态。
5. UI 有视觉资源、Shell/文档/选择/运行上下文及初版操作任务合同，并可在真 WPF 中修订使用方式。
6. 安全前置、刷写许可、物理未知与历史记录失败均有拒绝/恢复规则。

本包只提供评审依据，**G2 Gate 仍为 NOT_REVIEWED，且没有授权新 HOST 编码**。

## 注册表D｜G3工作包与最早证据

| 工作包 | 责任 | G2输入 | G3应交付 | 首个可见证据 | 不可跳过的依赖 |
| --- | --- | --- | --- | --- | --- |
| G3-0 | 跨模块契约基础 | 共享对象、错误/时间/身份/取消语义 | 接口草案、契约用例、版本登记、独立模拟接入 | Mock数据互通和禁止无授权发送 | 先于并行编码 |
| G3-A1 | USB会话与真实连通 | ICD-01、G0-04参与编译协议、SessionGeneration | Codec、连接状态、请求关联、超时/断连、模拟主控、诊断最小界面 | 黄金帧→USB B1 | B0检查后 |
| G3-A2 | 双CAN/输出及台架 | ICD-02/03、G0主控/按键电路、A1可用 | 双通道映射、CAN发送/观察、IGN/夹具/按键、安全门槛与诊断UI | B2→B3/B4；先记录物理未知 | B0/B1与现场许可 |
| G3-A3 | 共享CAN流与仲裁 | ICD-08/12、CanFrameObservation | FrameHub、收包扇出、背压、TxLease、压力用例 | 同帧CAN监视+DBC+UDS/波形模拟 | A1/A2框架 |
| G3-B1 | 工程/资源/DBC | ICD-06、AssetRef/ProjectId/SignalRef | 工程文件、资源引用、DBC解析与错误、保存重开、编辑命令 | 工程往返/错误DBC/真实帧解码 | G3-0共享对象 |
| G3-C1 | 测试编辑+Mock运行 | ICD-07/08/10、TestDocument/Plan/RunFacts | 步骤描述、编辑事务、编译检查、最小运行/判定/记录 | 用户编辑→保存→Mock测试→结果 | G3-0; B1资源合同 |
| G3-D1 | WPF视觉/工作台 | ICD-11、视觉Token、Selection与ActiveRun区分 | 真实Prism Shell、资源字典、组件画廊、工程树/属性/日志、测试编辑最小任务 | 真实WPF键鼠/DPI/长文本/状态 | G3-0 应用命令合同 |
| G3-E1 | 扫码输入与匹配 | ICD-04/05、PC USB实物连接 | 扫描探针、适配模式、规则样例、MatchDecision | 实物扫码、无匹配/歧义/重复 | 无需真实CAN |
| G3-E2 | UDS最小诊断 | ICD-12、共享CAN/TxLease | ISO-TP基础、请求/响应/负响应、模拟ECU、工具UI | 模拟→真实ECU非破坏诊断 | A3 CAN合同 |
| G3-E3 | 波形采集/回放 | ICD-12、TimeQuality、DBC SignalRef | CAN信号订阅、时间序列、参考资源、回放、图形显示 | 真实电机电流帧+参考比较 | A3+B1先联动 |
| G3-E4 | 烧录资源与仿真 | ICD-09/10、ProgrammingJob、CanTool领域参考 | 镜像与配置预检、状态、Bootloader模拟、日志/错误 | 模拟完整流程；禁止直接实机刷写 | E2共享诊断传输验证 |
| G3-R1 | 记录与统计 | ICD-10、RunId/AssetHash | RunRecord追加、损坏/失败、统计口径样本 | ECU-A两次+ECU-B一次=3次/2台 | C1基本Run事实 |
| G3-H3 | 研发自动测试集成 | A/B/C/D各自真实/模拟证据 | 集成合同与现场用例，不新增基础业务模型 | 真实ECU一步或多步测试+可追溯记录 | B0-B6适用项通过 |
| G3-H4 | 生产扫码—烧录—检测 | E1/E2/E4/R1以及H3 | 匹配/预检/真实烧录专项放行/测试/统计 | 经另行许可的真实ECU生产流程 | H3+刷写安全证据 |

## 注册表E｜开放问题的关闭实验及最迟决策点

| ID | 问题 | 关闭实验/证据 | 最迟决定点 | 当前设计允许的边界 |
| --- | --- | --- | --- | --- |
| U-01 | 扫码枪 Windows 枚举/后缀/焦点 | E1 扫描探针；不同输入框/无焦点/连扫 | G3-E1 适配实现前 | 只冻结 ScanCaptured 语义，不冻结设备模式 |
| U-02 | 主控 USB 命令字节/响应相关性/板固件身份 | G0参与编译文件复核+B0/B1黄金帧 | G3-A1首次真实连通前 | 不能伪造设备支持 seq |
| U-03 | 双 CAN 物理映射/波特率 | B2 逐通路收发/示波/日志 | 真实CAN发送前 | 按逻辑角色抽象 |
| U-04 | IGN 默认电平、夹具气路与许可 | B0/B3断上电与真实物理核查 | 第一次输出命令前 | 所有未知输出保持 UNKNOWN |
| U-05 | 按键板 KEY16 与五路网络 | B4逐键逻辑与电气复验 | 开放该端点前 | 不能预设20键全部可用 |
| U-06 | 一个工程是否服务多 DUT 型号 | G3-B1 真实工程/多资产试用 | 正式工程资源组织冻结前 | 资源稳定ID支持扩展，不设固定1:1 |
| U-07 | 测试步骤类型、资源创建与编排顺序 | G3-C1/D1真WPF编辑+Mock运行 | 首批编辑器体验验收 | 编辑文档/计划语义先冻结 |
| U-08 | 暂停/单步/跳过的安全边界 | 可中断/不可中断handler模拟 | 每类具体步骤发布前 | 取消结果明确，不强制中途停 |
| U-09 | 专业工具独立/停靠与默认入口 | G3-D1真WPF操作走查 | UI工作台第一轮改版 | 不改变共享业务服务 |
| U-10 | UDS 支持的具体服务/目标 ECU | ISO-TP模拟+非破坏实机诊断 | 发布对应诊断服务前 | 基本诊断通道与服务目录可扩 |
| U-11 | 电机电流波形时基、采样与判定阈值 | DBC真实CAN采集/参考波形比较 | 正式波形判定启用前 | 信号质量/源数据透明 |
| U-12 | ECU Flash配置/安全访问/恢复与授权 | CanTool认识对象+参考配置+模拟Bootloader | 任何真实刷写前 | 模拟成功不能开放实机 |
| U-13 | 编号格式/型号匹配优先级/冲突 | 实物标签样本 + 匹配规则集负测 | 正式扫码启动生产前 | 不等于host生成码 |
| U-14 | 生产扫码/装夹/烧录/检测实际先后 | H4 使用场景任务实验与实机复盘 | H4流程冻结前 | Production编排可变 |
| U-15 | 历史原始帧/波形保存时间 | 真实流量测量+查询需求 | 生产发布前 | 关键判定与版本追溯必须存 |
| U-16 | 真实性能、时间预算、工位节拍 | B1/B2/B5与WPF运行采样 | 对应发布质量指标前 | 不编造阈值 |

## 注册表F｜46条已批准G1要求的G2与G3交接映射

| G1需求 | G2分配 | 首选G3承接 | 原始状态 |
| --- | --- | --- | --- |
| AT-SYS-001 | G2-01/G2-08 | G3-0 | CONFIRMED_INTENT |
| AT-SYS-002 | G2-01/G2-08 | G3-0 | CONFIRMED_INTENT |
| AT-SYS-003 | G2-01/G2-08 | G3-0 | CONFIRMED_INTENT |
| AT-SYS-004 | G2-01/G2-08 | G3-0 | CONFIRMED_INTENT |
| AT-SYS-005 | G2-01/G2-08 | G3-0 | CONFIRMED_INTENT |
| AT-SYS-006 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | CONFIRMED_INTENT |
| AT-SYS-007 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | CONFIRMED_INTENT |
| AT-SYS-008 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | CONFIRMED_INTENT |
| AT-SYS-009 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | CONFIRMED_INTENT |
| AT-SYS-010 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | CONFIRMED_INTENT |
| AT-SYS-011 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | CONFIRMED_INTENT |
| AT-SYS-012 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | CONFIRMED_INTENT |
| AT-SYS-013 | G2-01/G2-02/G2-07 | G3-A1/G3-A2 | ENGINEERING_PROPOSAL |
| AT-SYS-014 | G2-03/G2-05/G2-06 | G3-B1 | CONFIRMED_INTENT |
| AT-SYS-015 | G2-03/G2-05/G2-06 | G3-B1 | CONFIRMED_INTENT |
| AT-SYS-016 | G2-03/G2-05/G2-06 | G3-B1 | CONFIRMED_INTENT |
| AT-SYS-017 | G2-03/G2-05/G2-06 | G3-B1 | ENGINEERING_PROPOSAL |
| AT-SYS-018 | G2-03/G2-05/G2-06 | G3-B1 | ENGINEERING_PROPOSAL |
| AT-SYS-019 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | CONFIRMED_INTENT |
| AT-SYS-020 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | CONFIRMED_INTENT |
| AT-SYS-021 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | CONFIRMED_INTENT |
| AT-SYS-022 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | CONFIRMED_INTENT |
| AT-SYS-023 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | CONFIRMED_INTENT |
| AT-SYS-024 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | ENGINEERING_PROPOSAL |
| AT-SYS-025 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | CONFIRMED_INTENT |
| AT-SYS-026 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | ENGINEERING_PROPOSAL |
| AT-SYS-027 | G2-03/G2-04/G2-05/G2-07 | G3-C1 | ENGINEERING_PROPOSAL |
| AT-SYS-028 | G2-03/G2-05/G2-06 | G3-E2/G3-E3 | CONFIRMED_INTENT |
| AT-SYS-029 | G2-03/G2-05/G2-06 | G3-E2/G3-E3 | CONFIRMED_INTENT |
| AT-SYS-030 | G2-03/G2-05/G2-06 | G3-E2/G3-E3 | CONFIRMED_INTENT |
| AT-SYS-031 | G2-05/G2-06/G2-07 | G3-E4 | CONFIRMED_INTENT |
| AT-SYS-032 | G2-05/G2-06/G2-07 | G3-E4 | ENGINEERING_PROPOSAL |
| AT-SYS-033 | G2-01/G2-05/G2-06 | G3-E1 | CONFIRMED_INTENT |
| AT-SYS-034 | G2-01/G2-05/G2-06 | G3-E1 | CONFIRMED_INTENT |
| AT-SYS-035 | G2-01/G2-05/G2-06 | G3-E1 | CANDIDATE_DETAIL |
| AT-SYS-036 | G2-01/G2-05/G2-06 | G3-E1 | ENGINEERING_PROPOSAL |
| AT-SYS-037 | G2-05/G2-06/G2-07 | G3-R1 | CONFIRMED_INTENT |
| AT-SYS-038 | G2-05/G2-06/G2-07 | G3-R1 | CONFIRMED_INTENT |
| AT-SYS-039 | G2-05/G2-06/G2-07 | G3-R1 | ENGINEERING_PROPOSAL |
| AT-SYS-040 | G2-05/G2-07/G2-08 | G3-0/G3-A1 | CONFIRMED_INTENT |
| AT-SYS-041 | G2-05/G2-07/G2-08 | G3-0/G3-A1 | ENGINEERING_PROPOSAL |
| AT-SYS-042 | G2-05/G2-07/G2-08 | G3-0/G3-A1 | EXPERIMENT_DEPENDENT |
| AT-SYS-043 | G2-05/G2-07/G2-08 | G3-0/G3-A1 | ENGINEERING_PROPOSAL |
| AT-SYS-044 | G2-05/G2-07/G2-08 | G3-0/G3-A1 | ENGINEERING_PROPOSAL |
| AT-SYS-045 | G2-05/G2-07/G2-08 | G3-0/G3-A1 | ENGINEERING_PROPOSAL |
| AT-SYS-046 | G2-01/G2-08 | G3-0 | CONFIRMED_INTENT |
