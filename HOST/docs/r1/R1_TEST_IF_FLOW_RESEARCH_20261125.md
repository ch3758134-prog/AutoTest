# AutoTest R1｜IF / ELSE 流程控制专项研究与整改记录

**研究基线**：R1 5226a5ba529a15654f0c33a838109626eaf2315a  
**研究成熟度**：84/100（设计成熟度，自评分，不是代码或整机验收分）  
**实施分支**：R1（main 不修改）  
**状态**：研究门槛已满足；代码已实施，尚未 Windows 编译 / WPF 交互 / ECU 实机验收。

## 1. 研究评分（满分 100）

| 维度 | 分数 | 证据 |
| --- | ---: | --- |
| 条件执行语义 | 19/20 | 区分 TRUE、FALSE、UNKNOWN；UNKNOWN 不落入 ELSE，阻断后续常规 TEST 写入，清理仍执行 |
| 变量与编译检查 | 17/20 | 顺序与分支路径检查，先定义后引用，双分支汇合取交集，循环可能零次不外泄 |
| 编辑交互设计 | 17/20 | 条件变量选择、六种运算符、THEN/ELSE 按语义顺序、分支独立快捷添加 |
| 旧资源与唯一 DSL | 16/20 | 保留 StepNode、StepId、Slot、equals；旧 IF 缺少 operator 默认等于 |
| 验收与风险识别 | 15/20 | 设计了纯变量 TRUE/FALSE、无数据、变更保存、单步依赖、分支嵌套覆盖；真实 ECU 和 WPF 仍未测试 |
| **合计** | **84/100** | 超过用户设定的实施门槛 |

## 2. 事实与问题

研究时 RealRunCoordinator 只有 IF 变量与比较值 ==。StepUiCatalog 给 IF 的变量默认名为 value，但未检查是否有任何上游生产者。TestEditorViewModel.AddNodes 依字母排序 Slot，导致 ELSE 显示在 THEN 前。WPF 使用通用属性字段，难以理解分支；源数据无效的 IF 可以记录 NoData，却未明确隔离后续测试步骤。老的“运行此步骤”是隔离执行，只含所选节点和其子节点，不带前面的变量生产步骤。

## 3. 冻结实现决策

- **统一 DSL**：仍使用 TestDocument / StepNode / StepEditTransaction / PlanCompiler / RealRunCoordinator，禁止引入第二套脚本引擎或运行器。
- **比较操作**：==、!=、>、>=、<、<=。IF 属性保留 variable + equals；optional operator 保证既有资源无需迁移。
- **变量身份**：IF 左操作数是变量名，由前置 Read/SetVariable/Calculate/WaitUntil 或波形分析等产生；右操作数是字面量或 $引用变量。不能将设备无数据当成 FALSE。
- **路径检查**：准备阶段到测试阶段传递保证可用的变量；THEN/ELSE 出口只保留两条路径均保证的变量；循环体变量不保证出循环；Cleanup 从准备阶段保证值重新分析，因为 TEST 可能跳过。
- **故障处理**：UNKNOWN 形成 NoData 事实并拒绝后续普通测试动作；保留实体和脚本清理。条件 false 只是流程分派，不自动宣称 ECU FAIL。
- **交互**：按“变量/操作符/比较值”组织编辑；条件行展示完整表达式和实际执行分支；两个 Slot 按 THEN→ELSE 展示，各自有直接添加入口。
- **单步**：只运行 IF 时如果没有原步骤产生的变量，必须被 PlanCompiler 拦截；不隐式模拟数据，也不执行其前面的物理操作。

## 4. 仍需的真实验收

1. Windows WPF 界面：打开 IF、切换比较方式、选择上游变量；新增 THEN/ELSE 子步骤，撤销、重做、保存、重启后对照 StepId 与顺序。
2. 执行器：TRUE、FALSE、UNKNOWN、上游 CAN 丢帧、非法比较操作符、条件嵌套、循环内 IF、无定义单步运行。
3. 实际工位：无有效 CAN 时禁止后续普通物理写入，正常清理仍执行；逐条追溯运行日志与 StepOccurrence。
4. 不把静态评分、源码提交和单元测试定义当成正式生产准出证据。

## 5. 外部设计对照

- NI TestStand Step Execution: https://www.ni.com/docs/en-US/bundle/teststand/page/step-execution.html
- NI TestStand Using Variables and Properties: https://www.ni.com/docs/en-US/bundle/teststand/page/using-variables-and-properties.html
- NI TestStand flow control: https://www.ni.com/en/shop/electronic-test-instrumentation/application-software-for-electronic-test-and-instrumentation-category/what-is-teststand/using-flow-control-effectively-with-teststand.html

只借鉴条件校验、变量可见性、执行事实模型，不引入 TestStand 的表达式语言、第二运行器或额外生产选择。
