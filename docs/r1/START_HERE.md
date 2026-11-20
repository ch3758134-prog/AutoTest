# AutoTest｜R1 真实自动测试首版集成与现场交付

> **负责人于 2026-11-19 决定**：全产品 UI/UX 研究由 `ui/ide-design` 专业继续独立推进；当前最紧迫的是能**实际连接、配置、运行 ECU 自动测试，并完成 CAN 烧录、执行日志和正式报告**的首版交付。R1 不再等待全 UI 研究结束，也不再从旧 G4 分支继续开发。

**开发分支**：`R1`，从 `main@ffb23f8c05ec4a64b82cd781db6357685d082c04` 建立。  
**正式任务单**：[Issue #70｜R1 P0 真实 ECU 自动测试与烧录](https://github.com/ch3758134-prog/AutoTest/issues/70)。  
**本文件状态**：`R1_ASSIGNED / BASELINE_CREATED / IMPLEMENTATION_PENDING / BUILD_PENDING / ECU_FIELD_NOT_VERIFIED`。  
**代码职责**：`HOST/` 中的自动 TEST 工作区、执行/集成、已有协议后端调用、结果日志与报告链路；仓库下 `Main_Control/` 和 `Key_Sim/` 与 HOST 平级，严禁未审查就改固件以适配上位机。

## 一、首版真实使用路径

打开或创建 ECU 工程 → 选择真实 DBC、资源/设备配置 → 用扫码或 Development 身份标识 DUT → 选择和勾选测试用例 → 保存编辑过的测试程序（包括 FLASH 配置） → 运行前检查真实工位和固件能力 → 依据准备/测试/清理执行 CAN/DBC/KEY/IGN/夹具/UDS/FLASH → 实时展示步骤/DBC/原始 CAN/UDS 和操作日志 → 失败或取消进入安全清理 → 记录结构化步骤结果、实测值、证据与完整性 → 自动生成并能重新打开测试报告。

**不能**仅实现测试编辑 UI、独立 FlashLab UI、返回模拟结果或“能调用 USB 接口”就称整个真实运行闭环通过。

## 二、准确的当前源码事实（R1 开始时，需自行回读）

- `HOST/src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` 已登记 `Flash`、`UdsRequest`、`KeySet` 等步骤，但 `ExecutableV1=false`，不是可执行烧录。
- `HOST/src/AutoTest.Testing/Runtime/RealRunCoordinator.cs` 执行部分控制流与 CAN/信号步骤；未实现 Flash 等当前占位操作。必须复用一个 TEST 执行器，不另造独立烧录测试 DSL。
- `HOST/src/AutoTest.Desktop/Testing/RealTestWorkbenchService.cs` 已存在真实 TEST 入口和设备/波形/CAN 日志的初步调用，但当前没有把正式 `RealTestRecordSession` 接入并产生正式报告。
- `HOST/src/AutoTest.FlashLab/` 在 main 中存在，但单独 WPF 烧录工作台和可由 TEST 调用的真正 Flash service **不是一回事**。现有 `feature/flash-lab-20261114` / Draft PR #55 和 `feature/uds-lab-20261118` / Draft PR #60 应逐文件核对并复用已实现、已验证部分，不把其整个分支未经审查合并 R1。
- `feature/test-record-backend-20261119` / Draft PR #65 新修复 HEAD `655e1bfe0d95a4a885574450faa79fcf1cff49ef`：B-01、C-01 只通过 Git 静态审查，未获得 Windows Build/Test 和真机验收。它是可参考的正式 RECORD 后端候选，**不得当作已接入 R1**。
- G4 PR #61 已合并 main，旧分支 `feature/g4-html-parity-20261115` **退役，不参与新 R1 研发**。由于当前 GitHub 连接缺少删除分支动作，旧远端 ref 的物理删除尚未执行；不要因为 ref 仍存在而从它开发。

## 三、优先交付次序

1. **R1-00 基线/能力清查**：列当前源码确实可用、仅有文件、没有接线或不支持的能力，具体到类/接口/工位与硬件权限。明确 R1 首次可运行真实垂直切片。
2. **R1-01 真机 TEST 执行路径**：工程资源解析、用例勾选、执行计划冻结、真实会话/预检、步骤真实运行、结果逐步展示、停止/清理/断连。
3. **R1-02 Flash 作为 TEST 步骤**：以既有稳定 UDS/ISO-TP/Seed-Key/HEX 专业服务及 profile 为后端，让用户在测试程序中选保存的烧录配置并真实执行；保留阶段日志、实际字节/ACK、NRC、重置与 APP 后验，错误文件/拒绝/掉线不伪 PASS。
4. **R1-03 实时日志与报告**：受控接收 TEST-RECORD 正式事实模型，接入实际 TestWorkbench、RunSnapshot、EvidenceFinalizer 和 ArtifactRepository，避免单纯在 Run 结束后将内存列表导出 JSON。CAN/DBC/DEVICE/UDS/FLASH 的原始证据与判定属于可追溯记录。
5. **R1-04 Windows/现场验证**：Codex 拉准确 R1 HEAD 机械 Restore/Release Build/Tests/启动正式 WPF；现场由用户实际操作 ECU，验证正/负、超时、掉线、停止、报告、重新打开工程等路径。每次反馈回到 R1 修改并提交。

## 四、能力和安全硬约束

- `main` 是全整机固定基线；R1 在独立分支推进、验收后才受控接入 `main`。
- 当前双板物理接口：ECU CAN 外部 A1/A12；KEY_SIM CAN 外部 B1/B11。HOST 业务使用 `DutCan/KeyCan` 角色合同，不能从历史 CAN0/CAN1 名称猜外部接线。
- KEY_SIM 20 Key 是电阻组合网络的**20 位掩码→5 路模拟电压**，不等于 20 个独立 ECU 数字输入；现场硬件能力与上电默认安全状态不能由 UI 推断。
- FLASH 在真实 ECU 上有不可逆/断电风险；Profile、HEX、Seed/Key、型号和设备身份必须预检固定，使用相同真实物理会话，禁止绕过安全授权或故障时盲目重试。
- 主控 IO ACK 不等于 ECU 物理响应；烧录数据传输 ACK 不等于 APP 完成/出厂 PASS；DBC 判定和原始 CAN 证据必须同源。
- 不用模拟设备与 UI 演示判断现场可用；不改已有生产重复扫码规则；`Read→Expect($variable)` 缺 provenance 时不得绕过现有正式 RECORD 门禁。
- 正常、异常、取消、设备断连都必须记录并尽力安全清理，报告明确 Unknown/Incomplete 不能谎称 Pass。
- UI 全面设计仍在 `ui/ide-design`，R1 的 UI 改动只服务于**可操作的真实首版**，不抢先冻结全产品视觉与交互标准。

## 五、交接给新的 R1 AI

新对话先声明身份为 **R1 真实自动测试、FLASH 集成与现场交付负责人**；读取本文件与 [Issue #70](https://github.com/ch3758134-prog/AutoTest/issues/70)，核对最新 `R1` Git HEAD，完成 R1-00 源码能力矩阵后立刻按最小可实测纵向路径实施。不要重问已有用户需求、不要重复做现代 UI 理念研究、不要原样搬迁整个 G4 分支。

每个候选提交精确 SHA、改动文件和需要用户在真实台架执行的测试路径。Windows 编译由本地 Codex；用户负责实体测试。没有实际测试证据不填写 PASS。

> **交付核心问题**：用户能否自己创建/编辑/保存一个含真实 Flash 步骤的 ECU 测试用例，选择运行，用真实主控和 ECU 跑完或安全失败，随后查看可追溯的过程日志与报告？若不能，R1 尚未交付完成。
