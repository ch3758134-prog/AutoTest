# AutoTest R1-04｜Windows 构建、真实台架首版验证交接

状态：**SOURCE_COMMITTED / WINDOWS_RELEASE_BUILD_PENDING / TESTS_PENDING / WPF_START_PENDING / ECU_FIELD_PENDING**  
正式任务：[Issue #70](https://github.com/ch3758134-prog/AutoTest/issues/70)  
分支：`R1`（禁止本轮修改 `main`）  
本交接对应源码 HEAD（落档前）：`6e6ca28e0ca3e7c1fa82456f258919c20de30df7`。使用 R1 最新 HEAD 作为 Codex 的唯一构建输入。

## 1. 本轮已写入 R1 的真实源代码

- **R1-01**：TEST 项目选择从仅校验资源升级为顺序执行。所有勾选用例先确认项目、用例 Revision/SHA、TCF、DBC/KEY、步骤计划和单个 FLASH 资源；同一次 USB epoch、同一 DUT 输入；每个用例复用 `RealTestWorkbenchService.ExecuteAsync`（唯一 `RealRunCoordinator` 和正式 `RealTestRecordSession`），获得独立的 RunId、SQLite 事实和报告。非 PASS/取消/设备或资产变化即停止后续条目。首版限制：**每批最多一个启用的 FLASH**，不把批次临时勾选覆盖项目默认 Enabled。
- **R1-02**：原 `CanToolFlashWorkflow.ExecuteAsync(ct)` 的独立烧录终点保持 Boot 51 01；只有自动 TEST 经现有 `HardwareConnectionService.ProgramTestFlashAsync` 的显式调用，会在同一闪存任务 CAN/ISO-TP 租约下探测冻结 APP 端点的 `10 03 → 50 03`。探测最多 8 次，未通过则 `AppSessionObserved=false`，不恢复 TEST 写入授权，停止正常测试并尝试清理；不盲目重试擦除/下载。**APP 诊断会话并不等于固件版本、功能或生产 PASS**。
- **R1-03**：`RealRunEvidenceRecorder` 增加真实 FLASH 观察窗口和证据身份；`ConfiguredRealTestFlashPort` 将观察范围关联到该 FLASH 步骤（异常路径仍保留关联）；`RecordedRealRunEvidenceFinalizer` 用已有 `ArtifactRepository` 持久化真实 FLASH 设备/UDS/CAN 轨迹，`RealTestRecordSession` 通过 `StepOccurrence` 保存证据引用。采集缺失、丢帧、损坏仍阻止正式 PASS；原始 Seed/Key 安全数据仍遵从敏感窗口脱敏。
- 前一轮修复：`RunFact.cs` 括号，以及 Desktop 8 文件 28 处 `Application.Current` 命名空间冲突。**后续未重新 Windows 编译。**

## 2. 本地 Codex 的唯一机械工作

在 Windows 下先确保 R1 本地没有会被覆盖的未提交工作，再拉取远端最新 R1，核对 HEAD。工作目录是仓库的 **`HOST/`**，解决方案为 **`AutoTest.sln`**。

按顺序执行：.NET Restore → `AutoTest.sln` Release Build → 全量 Release Tests → 正式 Desktop WPF 启动。**不编译或拉取 G4；不因失败自动修改源码/切换分支/改 main；不以 Mock/合成台架代替实测。**

请回传：
1. 实际 Git HEAD（完整 SHA）和工作树状态；
2. Restore/Release Build 的原始错误与警告（优先首个具体错误，完整日志留存）；
3. Tests 总数、通过/失败/跳过及失败名称；
4. 桌面程序是否真实启动到操作界面、若失败附实际堆栈；
5. 不执行 ECU 烧录，不自动连接实体工位，不改变固件。

**R1-04 Windows 门禁：** Release Build 0 error、正式 WPF 可启动才进入现场验证。测试失败必须反馈和修复，不能直接标为通过。

## 3. 实体 ECU 验收顺序（由用户现场控制）

开始前核查主控实际 CAN 接口（ECU A1/A12、KEY_SIM B1/B11）、IO 默认安全状态、ECU 型号、现场合法固件与安全授权。键盘扫码或人工 DUT 编号在 R1 仍按 **Development** 归档，不计新增出厂件。

| 序号 | 真实工况 | 必须观察/保存 | 不可误判 |
| --- | --- | --- | --- |
| 1 | 保存并重开工程、TCF/DBC、TEST 用例与项目勾选 | 项目和每用例固定 Revision/SHA、运行前检查结果 | 文件存在不代表实际可运行 |
| 2 | 主控 USB 真实连接、确认设备 epoch、CAN 接收 | 主控识别信息、DUT 实际响应、原始 CAN 数据 | USB ACK 不等于 ECU 响应 |
| 3 | 无 FLASH 的单用例正常 + 故意不满足的 DBC 判定 | 正式 RunId、PASS/FAIL/Timeout 的实际信号、时间与 EvidenceRef | `Read→Expect` 尚不构成正式直接 DBC PASS |
| 4 | 两个选中用例按序执行，第二个失败时停止其后步骤 | 每用例独立 RunId、报告、顺序与终态；再次打开记录 | 批次状态不能替代每用例真实记录 |
| 5 | **经现场批准的**单个 FLASH 步骤：Profile/HEX/Key/型号核查与手动输入完整授权 | ISO-TP/UDS 阶段、传输 ACK 字节数/校验、Boot 复位、后续 APP `50 03`、原始 FLASH 归档 | Boot `51 01` 不等于 APP 恢复；`50 03` 不等于软件版本正确 |
| 6 | FLASH 后独立 DBC 判定、真实操作清理 | APP 后 CAN/DBC 测量、直接判定 evidence、报告附件完整性 | 仅烧录成功不授予生产 PASS |
| 7 | 无效资源路径/授权/DBC 映射、取消、接收丢帧或断连 | 必须拒绝/失败/UNKNOWN、停止后续操作、记录缺口与清理结果 | 不进行未经许可的危险重复烧录；不凭未确认引脚状态宣称安全 |
| 8 | 程序退出再开启，打开历史记录与报告 | SQLite RunRecord/RunStepFact、HTML/JSON/CSV 与附件校验 | 丢失或损坏的物理证据不可恢复成 PASS |

实体 ECU 测试的主动危险行为（尤其断电、拔线、错误 Seed/Key、擦写失败注入）仅在现场制定安全方案后执行；普通验收不主动破坏 ECU。真实整车/台架状态以操作员实测为准。

## 4. 每次现场反馈请提供

- R1 HEAD、RunId、项目名和 TEST 资源 Revision/SHA、ECU 实际编号、主控 FW 自报版本及现场接口配置；
- 使用的 FLASH Profile / HEX 文件 SHA / SafetyFile SHA（**不上传密钥或敏感数据**），用户实际使用的授权是否被接受；
- 整段原始 CAN/UDS/DEVICE 诊断 ZIP 或归档、步骤事实、SQLite/HTML 报告及具体故障阶段；
- 测试是否真实执行、原始证据是否完整、实体状态是否确认，以及期望/实际的差异。

## 5. 尚未确认/不可伪造的结论

- `R1-01` 目前按用例逐次获得物理服务、各自持久化 RunId；**未建立单个跨用例 RunId 或统一生产批次放行门禁**，不能据此声称完整生产工作流通过。
- `UdsRequest` 独立通用步骤仍不是本轮已验收的可执行步骤；自动 FLASH 使用已存在的专业 UDS/ISO-TP 服务，不应将其混淆为任意 UDS 的自动执行授权。
- APP `10 03` 不是 DID 软件版本读取，若该型号不支持，R1 会保守拒绝继续；需依据现场配置和日志定点研究。
- WPF 实际运行、USB/CAN/KEY/IGN/夹具响应、FLASH 后验和 RECORD 报告均 **NOT_VERIFIED_BY_REAL_ECU**。不要根据编译或静态检查写 PASS。
- 不改变 `Main_Control/`、`Key_Sim/` 固件；绝不修改 `main`，后续缺陷只回 `R1`。

**停止点：R1-04 准备完成，等待 Codex 的 Windows 构建启动结果。此时 R1 不能标记 ECU_FIELD_PASS。**
