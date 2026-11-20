# AutoTest｜R1 SCAN 扫码枪与自动 TEST 配置匹配研究接管入口

**身份**：SCAN 扫码/编号/配置匹配与 R1 自动测试启动准备专业 AI。  
**正式任务**：[Issue #72](https://github.com/ch3758134-prog/AutoTest/issues/72)。  
**研究分支**：`research/r1-scan-configuration-20261120`；初始基于 `R1@94e82eddb03e69b428fd4c63c2093a570d979c4e`。  
**状态**：`SPECIALIST_CREATED / SOURCE_INVESTIGATION_PENDING / MATCHING_CONTRACT_PENDING / FIELD_NOT_VERIFIED`。

## 必须先调查的实际后端

`HOST/src/AutoTest.Specialists/Scan/`、`HOST/src/AutoTest.Desktop/Projects/ProjectScanMatcher.cs`、`ProjectConfigurationDocuments.cs` 中的 `ScanMatchSettingsDocument`、`HOST/src/AutoTest.Application/Production/`、`HOST/src/AutoTest.Desktop/Testing/RealTestWorkbenchService.cs`、`ProjectRunSelectionState.cs`、`HOST/src/AutoTest.Records/`。

**当前确认**：扫描匹配类已有，但只返回 RecipeId/Identity/状态，**不会直接配置 TEST 用例运行选择或自动启动真实测试**。研究不能把“条码规则匹配成功”描述成“整条生产自动测试链路已实现”。

## 关键业务约束

- 条码**由外部业务产生**；本软件只采集唯一编号，不自行生成编号。编号规则可编辑/版本化/发布；不同 ECU 型号和硬件版本不默认混在一个工程。
- 独立资源/绑定表解决“唯一编号→配置方案”；扫描字符内提取到的字段不等于得到完整 ECU 配置，必须对明确资源映射做版本验证。
- 匹配到实际 TEST 运行方案：测试程序/测试项目用例集合及顺序、TCF/DBC、KEY/工位配置、ECU CAN 码率、是否烧录及固件/Flash Profile、报告/出厂规则；明确冻结 SHA 和审核状态。
- 扫描应显示具体匹配结果供确认，不能在输入完编号的瞬间绕过安全门禁直接烧录或控制物理 IO；不允许同一个编号重复统计为“新增出厂”。
- **研发/手动运行不依赖扫码枪**：使用者直接选择用例或单步执行，不因未配置条码规则被拦截。生产模式与调试模式不能共用未经区分的出厂计数。
- 键盘模拟枪优先、专用输入区；失焦、CR/LF、连续扫描和意外粘贴都要处理。禁止全局 Hook 抢占编辑器输入；串口枪需真实型号和设备验证。
- 必须把未命中、歧义、旧资源修订、重复扫描、条码不合法、设备断连、运行中重扫等反馈转成可理解的错误/恢复动作。对重复 DUT 允许按审核规则复测，但报告 RunId 不得混同。
- SCAN 本身不做实体动作；最后调用唯一 R1 测试执行/FLASH 安全接口与 TEST-RECORD 报告，不另造运行器或统计库。

## 第一阶段必须提交

1. 源码能力矩阵，特别是扫码配置到真实 TEST 入口的断点；
2. 编号规则+独立绑定表+项目资源冻结+运行计划数据合同，列出当前缺失字段；
3. 研发与生产两套实际使用路径、异常和重复/计数策略、UI 必需状态字段；
4. 一个最小扫码到已预检 TestRun 选择的可实测路径设计；必要实现通过专业和 R1 审查后再进入 R1 集成。不能用 CSV/Mock 代替实物扫码验收。

**边界**：本专项不重画 UI、不自改硬件协议、不改 TEST/Flash 执行器、不在生产模式静默出厂放行。具体 WPF 交互只提出用户任务与字段要求，UI 设计在 `ui/ide-design`。
