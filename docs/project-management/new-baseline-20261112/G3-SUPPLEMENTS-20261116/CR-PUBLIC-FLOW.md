# AutoTest｜G3 补充变更：公共流程（复用测试步骤）

**正式管理标识**：`PM-CR-20261116-PF`（本次受控归档唯一标识；不覆盖历史 SAEF 编号）。  
**状态**：负责人已批准产品方向及此前 G3 补充设计；**G4 代码实现与真实运行尚未验证**。  
**关联**：`PM-CR-20261116-KEY`、主控/USB 双夹具补充；已批准 P0/G0/G1/G2/G3 历史原文不修改。

## 1. 已批准产品语义

采用**公共流程**作为正式用户术语。它是同一 ECU 工程内由任意合法 TEST 步骤组成、可被多个测试用例调用的可复用步骤序列；**不是测试用例，不自带生产测试身份，不直接进入 TestProjectEntry**，不独立形成 DUT 生产 PASS/FAIL。是否抽取公共流程由工程师决定，测试用例可直接放置普通步骤。公共流程能够在用例的准备、测试、清理任一阶段被调用，可调用别的公共流程，但必须禁止循环引用。

模型：`ProjectDefinition` → `TestProjectDocument`（只含测试用例条目）→ `TestDocument`（Prepare/Test/Cleanup、StepNode）；公共流程独立 `public-flow` 工程资产，内容为同一 `StepNode/Slots` DSL 的有序 `Body`，无自带 Prepare/Test/Cleanup，调用者提供上下文。公共流程引用的运行行为依旧由真实 TEST/DEVICE/UDS/CAN/波形端口执行，不建立第二套脚本引擎。

## 2. 设计合同

建议资源形状：`PublicFlowDocument { FormatVersion, FlowId, Name, Parameters[], Body[], References[] }`。工程 manifest 中注册 AssetId、Revision、Sha256、相对路径和资源依赖。公共流程名称是用户显示字段，**不能作为运行身份或默默绑定的唯一依据**。

调用步骤内部标识 `CallPublicFlow`：至少包含 FlowAssetId、精确 Revision/SHA、输入参数和显式输出绑定。公共流程运行时持有局部变量作用域；不同用例或重复调用不共享隐式可变数据。可保留调用前后的局部值但输出必须显式返回给调用者。

- 保存时校验直接资源引用与完整依赖闭包；Run 前验证资源及嵌套公共流程的固定 Revision/SHA。
- 对已修改的公共流程，原用例**不得自动跟随最新版本**。若历史 Revision 不能恢复，则明确 `STALE_REFERENCE` 并阻断运行，允许工程师审查后主动更新绑定；不能谎报旧修订可用。
- 预检诊断跨流程无环、有效入口、参数类型、执行深度/步数/总时间预算、物理权限、未支持步骤、源位置；不允许通过子流程绕过安全或权限。
- 运行证据须可从用例、调用点跳转到流程/源步骤，保留 `ProjectRunId / PackageAttemptId / CallSiteStepId / FlowAssetId / FlowRevision / FlowSha / SourceStepId / OccurrenceId / CallStack`，不存在的字段先在 G4 设计补齐。
- 每个 Flow 步骤出现错误、取消、设备 UNKNOWN 时依既定 TEST 失败/异常策略传播；不得只记录外层“调用成功”。
- 不额外赋予公共流程单独的工位所有权、生产身份、硬件授权或复位行为。清理和输出状态由调用 TEST 与经审查的工位策略决定。

## 3. 旧 Subroutine/CallSubroutine

当前 `StepDescriptorCatalog` 有 `Subroutine`（本地 body）及 `CallSubroutine.target`，`ExecutableV1=false`；`RealRunCoordinator` 尚未实现这些节点。旧的本地子程序与新公共流程不是同一个资源，**不得按名称直接重写 `CallSubroutine.target`**。保留旧 TEST v1 文件读取、源结构和兼容诊断；提供“提取为公共流程”显式转换（复制/引用、StepId、作用域、参数/返回、调用版本核对）。无法证明等价时保持旧结构并在运行前阻断。

## 4. 已批准成果的定点影响

- P0：保持 DUT 无关工程、自动测试和通用激励定位，不改产品定义。
- G1：补充 REQ-02/07/08 工程资源，REQ-09/12 步骤复用及子程序，REQ-20/21/22 冻结、停止、执行证据。
- G2：延续单一 TEST DSL、项目级 Run/唯一设备会话原则；公共流程仅作为独立资源与调用栈。
- G3：G3-01 工程树增加公共流程入口与同一编辑器/Inspector；G3-03 补调用、参数、证据与递归阻断；G3-04/05 增分专业工作包。冻结 HTML 主布局不重绘。
- G4：`PF-01` TEST/PROJECT 资源+Codec+调用图；`PF-02` UI/INTEGRATION 编辑+引用；`PF-03` TEST/INTEGRATION 真实解释器与运行事实。各包允许分别达到“可编辑”“可编译”“可实体执行”，不可越级。

## 5. 验收与兼容

- 新建流程含 Wait/IF/KEY/CAN 等合法节点，保存重开；两用例同时引用正确且不改变生产包数。
- Flow A→B→A 递归必须预检阻断；参数不符、资源缺失、旧 Sha 无法恢复均显示可定位错误。
- 修改公共流程后老引用不静默升级；运行中冻结执行依赖；旧工程可读且原件保留。
- 公共流程与用例直接步骤共用正式 TEST 解释器、硬件权限和审计；未实现的语义标记 BLOCKED，禁止 Mock PASS。

**本记录生效为已批准设计边界，但不意味着 Run/Windows/实体已经实测通过；G4 依据准确代码接口与项目管理合同实施，真正通过须附原始验证证据。**
