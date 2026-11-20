# AutoTest｜R1 波形判定与扫码测试配置两个专业研究任务成立（2026-11-20）

**项目管理状态**：`WAVE_RESEARCH_SPECIALIST_CREATED / SCAN_RESEARCH_SPECIALIST_CREATED / R1_RESEARCH_PARALLEL / CODE_NOT_MERGED / REAL_DEVICE_NOT_VERIFIED`。

## 一、负责人决定

负责人决定现在分别开启：
- **WAVE**：重点针对真实 ECU 波形判定，研究采集来源、参考波形、参数、比较误差、采样质量、真实判定及结果/报告接线。
- **SCAN**：扫码枪重点服务于 **实际自动 TEST 配置匹配**，而不是独立的字符扫描/演示模块；扫码唯一编号后选对工程内配置、TEST 用例、DBC、按键/工位、CAN 码率、必要烧录资源与 RunPlan，再调用已有 R1 执行/报告机制。研发模式的手动测试不因未扫码而阻断。

两个专项都属于 R1 实际测试能力补足，不修改现代产品 UI 全面研究的职责，不自造第二套 TEST 执行器/RECORD 记录库，也不绕过 FLASH 实物授权。

## 二、实际创建的 GitHub 任务与分支

| 对象 | GitHub | 内容 | 准出条件 |
|---|---|---|---|
| R1 WAVE 波形判定 | [Issue #71](https://github.com/ch3758134-prog/AutoTest/issues/71) / `research/r1-wave-judgement-20261120` | [WAVE 接管文档](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-wave-judgement-20261120/docs/r1-wave/START_HERE.md) | 研究/判定合同、误判边界、真实采集路径和 TEST-RECORD Evidence 对接评审 |
| R1 SCAN 配置匹配 | [Issue #72](https://github.com/ch3758134-prog/AutoTest/issues/72) / `research/r1-scan-configuration-20261120` | [SCAN 接管文档](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/START_HERE.md) | 编号→独立绑定表→资源修订→唯一测试 RunPlan→安全预检→报告追溯的合同和现场扫码操作 |

两个分支均明确从 **`R1@94e82eddb03e69b428fd4c63c2093a570d979c4e`** 建立。R1 由现有研发负责人持续开发，其 HEAD 不断变化；各专业每次调查源码/申请接线时必须回读新 HEAD，不能将建立时分叉点当作永远的最新 R1。

## 三、WAVE 现有资产与研究边界

静态核对 R1 已有 `HOST/src/AutoTest.Specialists/Wave/WaveComparator.cs`、`WaveComparisonConfig.cs`、`WaveCaptureService.cs`、`WaveTimeQuality.cs`、`WaveReferenceRepository.cs`、`WaveStepHandler.cs`，`HOST/src/AutoTest.Desktop/Testing/ConfiguredWavePort.cs`、`WaveSettingsDocument` 与 WaveWorkbench。

**已有基础**：DBC 信号时间序列、参考及采集证据、绝对/相对容差、覆盖率、通过比例、最大容许时间位移、采样间隙、累计超容差持续时间、最短覆盖时间、时间质量/DBC 指纹门禁。**不是**这些算法在实际 ECU 上已通过；尤其“对齐掩盖延时、插值漏掉突变、CAN 原始采样不代表模拟量真实电压、采样质量与报告判定脱节”的风险要研究。

重点第一版：一条真实信号曲线的参考采集→配置冻结→正式判定→附原始 CAN 时间片/证据→RunId/StepOccurrence→RECORD/HTML 报告；正常 Pass、真实偏差 Fail、样本不足/丢帧/错误 DBC 不得冒充 Pass。WAVE 自有判定证据不能冒充 TEST-RECORD 的 `DirectDbcSignal`；要求独立经批准的证据合同与门禁。

## 四、SCAN 现有资产与研究边界

静态核对 R1 已有 `HOST/src/AutoTest.Specialists/Scan/`、`ProjectScanMatcher.cs`、`ScanMatchSettingsDocument`、`HOST/src/AutoTest.Application/Production/`、`ProjectRunSelectionState.cs`。

**实际断点**：`ProjectScanMatcher.MatchAsync` 目前返回匹配 Code/RecipeId/Identity，且文档明示 “Matching only; never starts TEST, physical outputs or flashing”；匹配成功**尚不等于**工程/用例/TCF/DBC/烧录 Profile 自动完成选择和 RunPlan 冻结。研究必须直接围绕此断点展开，不能仅重复编号 Regex 或扫描枪输入控件研究。

**首版重要行为**：扫码只接收唯一编号（由外部业务产生），独立表管理与测试方案的绑定。发布/版本化的规则和配方、歧义拒绝、重复 DUT 不重复新增出厂、生产模式与研发运行模式分离。扫码可以匹配 FLASH 所需固件，但不自动授权烧录；未配置扫描器也不拦截工程师在 R1 自行选择测试程序/单步运行。

## 五、交付与研发关系

1. **两新专业 AI**：读取各自研究分支 `START_HERE.md`、Issue、最新 R1 实现，先提供准确源码能力矩阵 + 目标合同 + 最小真机可验证路径，研究留档在各自分支。
2. **R1 整机负责人**：[Issue #70](https://github.com/ch3758134-prog/AutoTest/issues/70)。正在推进真 TEST + FLASH + 正式 TEST-RECORD，不能等两个研究全面完成才继续用户首版实测；当 WAVE/SCAN 最小接口审查通过后由 R1 定点接入。
3. **UI/UX**：仍由 `ui/ide-design` 负责实际操作体验/现代浅色工作区设计，WAVE/SCAN 只提出必要用户任务、配置字段和可观察的状态，不独立冻结 UI。
4. **PROJECT**：负责资源/DBC/配置/Profile 与真实工程修订及 SHA；**TEST** 负责测试步骤和运行；**RECORD** 负责可追溯结果；**DEVICE** 负责实际采样与输入路径的真实硬件证据。
5. 不因研究计划、已有模块、模拟/回放测试或源码存在而宣布 ECU 波形判定或实际扫码枪的现场验收 PASS；原始报告、日志及实机证据必须由 Windows 和真实工位分开校验。

**最终管理结论**：两个专项立项与研究边界可执行；算法/映射合同尚未审批定版，不对 `R1`、`main` 或任何硬件固件进行源码合并或功能放行。
