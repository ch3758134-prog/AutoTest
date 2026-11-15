# AutoTest｜G3-03 自动测试领域、流程、工位及证据详细合同 v0.1

**状态：G3 设计候审；适配冻结的自动测试 HTML，并以 `local@a9a26ebf` 的真实源码为基础。**
**上游：** P0/G0/G1/G2 已批准的要求、已批准 G3-DEVICE-01/WAVE-01/TECH-INTEGRATION-01，最新 PM-BUSINESS-DECISIONS 与 PM-INPUT-SEMANTICS 勘误。
**关联：** G3-A2 #48、G3-TEST-FLOW #52、G3-UX-AUTO 静态审查、G3-01 严格 WPF 对照。

## 1. 业务身份：不能产生两套测试模型

```
ECU 测试工程
  ├─ 测试项目（TestProjectDocument，可多个）
  │   ├─ 有序 Entry (ItemId) → 测试用例资源（TestDocument）
  │   └─ 一次执行选择 RunSelection（不自动修改 Entry.Enabled）
  ├─ 当前 DUT/测试系统绑定（TCF）
  ├─ 固定台架事实配置（TBC）
  ├─ DBC、Controller、波形参考、诊断/烧录/扫码配置
  └─ 每次执行的独立 Run 记录（不可覆盖）
```

- **测试程序**不作为第三个 UI 业务对象；Package 是底层兼容术语，不在主界面与“测试用例”并列。
- 每个用例的 `Prepare/Test/Cleanup` 是用户可编辑的**逻辑测试阶段**，不是一概自动复位 IGN、KEY_SIM 或电磁阀的硬件策略。
- 同一 Package 可被多个 Entry 引用，**EntryItemId** 区分它在该项目中的执行位置；重试为新的 Attempt；循环调用同一 Step 产生新的 Occurrence。
- 工程师临时选择默认从保存的 `Enabled` 初始化，手动更改当次选择不污染项目；生产扫码始终按已定的完整生产方案和资源快照执行，不能继承工程师上次临时选择。

## 2. 真实编辑、资源关联和保存

| 操作 | 正式合同 | 禁止行为 |
|---|---|---|
| 新增/移动/复制步骤 | `StepEditTransaction` 更新 `StepNode`，Slots 的层级符合验证器 | 用 HTML 演示 `steps[]` 直接落盘或生成重复 StepId |
| Inspector 修改 | 所选 Step 的草稿即时反馈，只有验证通过才提交修改，支持撤销/重做/未保存提示 | 非法数值按 `Number(v)||0` 偷偷改成 0 |
| DBC 选择及信号物理值 | 以 Asset ID/Revision/SHA + 报文/信号名、字节序、有符号、位段、比例/偏移、枚举和有效范围编码 | 只凭显示名称/十六进制直接推算、修改后的 DBC 不提示断链 |
| KEY_SIM 外部输入模拟 | 按当前 ECU 工程 Controller 配置解析有效物理输入条件和状态，映射到 CAN0 20 开关组合 | 把“驻车/释放”作为平台默认，20 Key 当 20 个业务功能按钮 |
| 资源保存和关闭 | 文件内容、版本、项目 manifest 更新受控；另开工程需检查草稿和旧 Run | 不提示覆盖、用 Demo 临时状态替代正式资源文件 |
| 步骤有效性 | 未实现/未配置/资源失效由预检明确指出具体步骤和阻断原因 | 为让 UI 看起来能运行而隐藏或跳过必需步骤 |

## 3. TEST DSL / Flow 的 G3 细化合同（需要逐类型实现而不是只提供菜单）

| 类型 | UI 参数及层级 | 编译与真实运行语义 | 质量/异常要求 |
|---|---|---|---|
| Sequence/准备/测试/清理 | 顺序步骤/阶段；阶段展开编辑 | 顺序执行，支持 Breakpoint 和取消边界 | 保留来源 StepId/Occurrence |
| IF/ELSE | 条件、True/False 两个正式 Slot | 受限布尔表达式求值，只执行选中的分支 | 缺少变量/无效观测不是 False；产生 ExpressionError/Unknown |
| SWITCH/CASE/DEFAULT | 分派表达式、各 Case、默认分支 | 按有类型的匹配规则最多执行一个分支或按明确规范执行 | 重复 Case/不可达/类型不匹配需明确诊断 |
| WHEN/WaitUntil/CanWait | 条件或 CAN ID/DBC 信号、超时、接收模式、年龄/质量 | 新帧/最近有效值与条件等待语义必须分开；超时结束不可用数据冒充成功 | 总线丢帧、过期、None 形成可解释事实 |
| LOOP | 有上限的次数/条件/延时、子步骤 | 迭代次数与嵌套深度限制；每步产生 Occurrence | 取消、异常不能形成无限循环 |
| Try/Catch/Finally | 错误类型/分支，清理步骤 | 仅捕获规定的业务可恢复错误；真硬件 UNKNOWN 不得被“成功”清理掩盖 | Finally 未完成须留 UNKNOWN/FAILED_CLEANUP |
| CallSubroutine | 有版本/参数/返回值的引用 | 仅调用当前快照内有效定义，循环调用和深度可诊断 | 引用缺失、递归限制、参数错必须阻断 |
| SetVariable/Calculate/Expect | 有类型变量、受限表达式、精度和单位 | 真正执行计算/比较并显式产生判定 | 区分业务 Fail、数据质量不确定和内部错误 |
| CAN TX / DBC | 目标总线、帧、信号赋值、周期/停止等有效接口 | 通过授权设备 CAN 发送，不改物理事实源；周期生命周期有显式停止 | ACK≠ECU响应，预算/权限/断连阻断 |
| 输入模拟/IGN/夹具 | 工程配置状态/动作，时序与等待 | 当前硬件可执行的实际激励；信号产生与 DUT 识别分离 | 没有独立测量不能报电压/到位已验证 |
| UDS/Flash | DID/服务/诊断地址、安全和工具参数 | 共享实际 CAN1 ISO-TP；危险指令按独立 F2 批准 | 不因 HTML 有按钮就开放实体刷写 |
| Wave Start/Stop/Analyze | 采样源、时间/覆盖、参考与分析规则 | 真正开始/结束数据捕获，来源和 compare 结果进 TEST | Compare Fail 属比较结论，是否使 Run Fail 由步骤语义决定；Inconclusive 不能被改写为 Fail |

**表达式实现要求：** 界面可允许工程师输入条件文本，但保存前必须按受控 AST 语法解析、静态校验和给出明确错误位置；不执行任意 JS/C# 表达式。新增表达式语法及 TEST 文档修订策略须写清对旧 v1 用例兼容的规则，不得静默迁移改写用户原文件。

## 4. TEST Run 与调试动作的真实状态机

**推荐状态**：`Idle → Validating → Ready → Running ↔ Paused → Stopping → Finalizing → Completed/Failed/Aborted/Inconclusive/Error`。任何状态变更来自实际服务事件，不是 UI timer。

- **调试**：单步、断点、运行到、跳过、暂停/继续必须与可暂停的**步骤边界**对应。已发出的 USB 命令不能保证中途物理回滚；停止是一个可记录的请求，设备未知保持 UNKNOWN。
- **项目级 Run**：取得物理 Station Lease，冻结项目、TCF、Package、DBC、Controller、Wave 等资源的精确内容，再按 Entry 顺序调用可复用的包内解释器；每次 Run 只有一个项目身份和统一最终清理。
- **包内 Cleanup**：执行用户写在该包的逻辑 Cleanup，但不能自动结束整次项目授权；若硬件状态应该保持，需遵守用户明确步骤及可执行设备策略。**单包现有 `ConfiguredStationActionPort.CleanupAsync` 会撤销许可，不能直接在多个包间反复调用。**
- **结果**：保留 `ProjectRunId/EntryItemId/PackageAttemptId/StepId/StepOccurrenceId`、单调事件序列、实际设备 generation、观测时间/来源/质量、报告导出。Fail、Inconclusive、Error、Aborted 区分；报告保存失败不得标为合格出厂。
- **独占**：自动测试与手动台架不能同时操纵一个工位；UDS/Flash 物理发送也需纳入统一所有权与有限授权。只读 CAN RX 订阅可共享，但有写操作不能共享无条件许可。

## 5. 生产扫码独立于工程师调试选择

`ScannerInput → ValidateIdentity → ResolveProject/TCF/ProductionPlan → PreflightStationAndResources → FreezeRun → StartAutomatically → Execute → Persist → Statistics`。

- 预检通过后自动启动规定工序，不逐件再次要求人工点开始；
- 工位准备的授权状态必须由正式 G3/DEVICE 策略定义，扫码不能自动产生无条件物理写许可；
- 重复编号不计为新增出厂，复测单独保存，不覆盖原 Run；
- `src/AutoTest.Application/Production/ProductionWorkflow.cs` 仍使用 `MockObservation` / `SimulationOnly`，G4 前必须更换为真实 Run adapter/事实；不能“保留旧生产路径作为兼容模式”。

## 6. 实测门槛与不能虚报的状态

- Windows：新建工程→用例与项目→至少 30 条和嵌套步骤→Inspector 编辑→撤销→保存重开→勾选执行→结果定位；即使设备未连接也应能完成编辑。
- USB/CAN：实机连接状态、设备世代、失败与断连、总线 RX、真实 CAN1、CAN0/KEY_SIM、IGN/SOL 要有独立事实。
- Flow：每种控制结构一个确认的输入/分支/错误路径；除非已有真设备，只有编译与单元测试结果不能宣称 ECU 实测完成。
- Wave：现有 G3-WAVE 合同继续有效；用户在自动测试实测中进一步确定采集、容差、对齐和判定 UI，**不因等待未来调整阻止 TEST 编辑器先交付**。
- UDS/Flash：烧录研究与独立 EXE 先行；独立 UDS 专业工具 UI 后置，底层必要 ISO-TP 仍须可用。F2 真正 ECU 写入另需 OEM/安全/实体证据。

## 7. G3 准入仍待关闭的具体事项

- TEST 自由表达式/Flow AST 与 `StepNode` JSON schema 定点合同；
- UI 对每个可添加步骤的完整参数映射；
- A2 共享 Station Lease/父 Run/两层 Cleanup/事件 API 的精确接口定义；
- “工程师调试运行”与“扫码生产就绪授权”具体差异；
- 状态 UNKNOWN 时禁止继续物理输出的边界；
- 关键服务挂接位置的实际 Build/Windows 验证；尚未执行。

这些是 G3 交付的**可追踪限制或需在进入对应 G4 工作包前闭合的接口细节**，不能用“UI 已认可”代替执行层完整设计。建议允许先行 G4 UI 非实体编辑切片，先由用户进行 WPF 操作验收，再逐步接实机执行与 Wave。
