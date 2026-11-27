# AutoTest R1｜全系统破坏性重构与源码复审记录

日期：2026-11-27（UTC+8）  
目标分支：`R1`；`main` 保持受控。  
状态：**已经直接修改 GitHub R1 大量源码；未进行 Windows .NET/WPF 编译；未进行 USB/实物 ECU/夹具/扫码枪验收；正式产品闭环不得标记通过。**

## 一、已完成的业务和源码变化

1. 删除桌面生产端 13 项 `ProductionAdmissionReview` 及“TrustedPhysicalStartConnected/TrustedFixtureAndInterlockReady/VerifiedInspectionWriterConnected”人为许可；移除未实际接通的另一套 `ProductionWorkflow`、`ProductionPhysicalStation`、`ProductionStartGate`、`ProductionCommandHandler` 等旧生产路径及对应测试。避免开发尚未完成状态成为运营使用条件。
2. 把当前真实整组执行逻辑纳入生产：扫码匹配一个完整已安装 ECU 工程，统一软件 START 触发实体夹具 + FLASH + 全量有序 TEST + RECORD；结果使用 `RecordRunMode.Production` 与实际扫码规则指纹，工程调试仍使用独立模式。停止命令调用既有实际停止动作，不生成第二套 TEST DSL。
3. 夹具固定两路压紧 1→2、释放 2→1，不在生产配置里要求选择启用/输出/不存在的传感器。新增 `EcuCanFixtureFeedback` 监听主控新收到的 ECU CAN 帧，严格区分 ECU CAN/KEY CAN、真实主控连接代次、实际压紧后接收时间和无效数据；超时终止后续 FLASH。**这只证明 ECU 通信就绪，不是真实机械到位或人员安全反馈。**
4. 去除 Native FLASH 二次发布及 Station Deployment 许可作为工程包运行前置：原生参数和镜像由工程固定保存；导入的 ECU 工程包由自带清单校验完整性，型号标记可以自动匹配工程，操作者不需发布/授权/重新选择测试方案。
5. 修复波形标准的工程包闭包：包含 `wave-records/references/index.json`、实际 `*.wfm.json` 和 `*.sha256`，导入完成重新解析真实用例引用；避免工程里可以判定、生产包中却找不到标准。
6. 修复项目连续执行时变量清空的真实架构缺陷：`RealRunCoordinator` 支持由 `ProjectExecutionSession` 持有运行变量字典，下一用例使用前一次真实观测/赋值；`PlanCompiler` 和 `TestVariableFlowValidator` 识别上游保证可用的变量名称、保守处理 IF/SWITCH/LOOP 的不确定路径；独立测试保留隔离。
7. 生产报告已绑定每台 ECU 的父检测 ID，保存到工位独立目录，不与已安装的项目资源混放；完成收尾和报告后才持久提交终结 PASS/FAIL，崩溃/归档失败保留未完成事实；按父检测单台计数的今日 PASS/FAIL/未完成统计已实现，生产界面可查看本次 HTML 报告。
8. 删除旧 `ProductionRecipe`、`ConfigMatchService`、`ScanDraftService` 相关重复匹配模型；保留单个真实 COM 扫码、固定型号标记与完整原文。

## 二、重构后的具体权限和边界

- R1 可以破坏性删除旧代码、旧工程兼容、旧 UI 和错误数据模型；不再因旧管理 Gate、发布批准或 mock 演示推迟重构。
- 单一 USB/CAN 真实设备会话、单一连续 TEST 执行语义、工程内原生 FLASH 资源。
- 真正设备故障或存储故障仍然如实反馈；取消的是独立的“准入许可平台”，不是 UART/CAN/UDS 协议有效性。
- 软件 START 已接通共享真实执行，实体 START/STOP 需要主控固件上报及上位机订阅的真实协议联调，未完成不能宣称实体按钮已可用。

## 三、仍未完成的交付闭环（不能伪称通过）

1. **Windows Release 编译与 WPF 启动**：本轮代码在云端 GitHub 直接修改，缺少 Windows WPF 构建环境；必须确认 XAML、依赖注入、编译与实际可启动，并在 R1 修复编译报错。不能用静态格式审查替代编译。
2. **真实夹具动作和通信**：主控实际输出/阀方向、电气接线、ECU CAN 预期帧、供电/IGN 条件、超时策略、USB 断开与异常释放均待现场测量。当前接受任意有效新接收的 ECU CAN 帧；面向不同 ECU 时仍须明确型号适用的通信就绪依据。CAN 不替代安全硬件互锁。
3. **实体 START/STOP**：固件输入消抖、事件 USB 上报、Host 接入统一启动/停止处理，尚未真机闭环；不阻碍软件 START。
4. **波形自动判定**：多信号标准组已经具备采集/存储入口，但 TEST 多信号联合判据、统一时基、容差策略、时间对齐与证据一致性尚需全链审查与实测。
5. **DBC 专业编辑器**：存在源格式扩展保护逻辑，CAN ID、信号技术名/值表等复杂引用修改尚非全面无损可编辑；需以标准格式知识构建真正完整合法的编辑闭环，不应用生产阶段再次验证补救。
6. **硬件记录与批次报表**：父检测日统计已按单台计数；跨型号完整批次历史查询、可导出批次报告、从 FLASH 前至释放的完整 CAN 底本与证据落盘质量仍须统一。现有 `ProductionStatistics` 查询的是 TEST 用例 run 级结果，不能误称整台 DUT 数量。
7. **窗口与全系统 UX**：工程自动保存、恢复、TEST 步骤插入/双击、CAN 发送、独立专业工具、手动台架、主窗关闭及所有浮窗退出，要以完整 1366×768/1920×1080 Windows 操作回归验证，不能因为代码存在就认定 UI 完成。
8. **异常恢复**：USB/CAN 掉线、工位气动状态不确定、FLASH 擦写中断、报告落盘失败、重复扫码、停机与复检必须按真实动作测试，不做模拟 PASS。

## 四、必须遵守的本机检查

使用最新 `R1` 的准确 HEAD；Windows 本地 Codex 只拉取、Release 编译、启动并回传完整错误日志；代码修复在 R1 完成，不依赖用户在本地替项目代理重构业务源码。不要运行 GitHub CI，不把静态审查、源代码提交或单个测试用例成功包装成整机可发布通过。现场按**扫码 → START → 固定夹具压紧 → 新 ECU CAN 反馈 → FLASH → 连续 TEST → 记录 PASS/FAIL → 释放 → 下一台**实测，再做故障注入复核。

本文件是实施跟踪和剩余风险台账，非项目最终 PASS 声明。
