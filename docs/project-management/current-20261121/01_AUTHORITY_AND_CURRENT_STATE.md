# 01｜权威来源、基线 SHA 与状态分层

## 1. 原则：产品决定、代码存在、真实验收是三件事

| 类型 | 具备什么权力 | 不能代表什么 |
|---|---|---|
| `USER_APPROVED` | 负责人明确批准的业务意图、安全约束、工作组织或特定方案 | 不等于代码已实现或能实际操作 |
| `ENGINEERING_CONTRACT_APPROVED` | 经过审查的领域模型、接口、安全与版本兼容条件 | 不等于硬件/软件功能通过 |
| `RESEARCH_PROPOSED` | 专项研究、经验、算法建议、v0.x 候审方案 | 不能自行成为硬性 UI、生产门禁或新增硬件接口 |
| `SOURCE_CANDIDATE` | 源码存在、分支提交/静态回读 | 不等于编译成功，不能视为实机 PASS |
| `BUILD_VERIFIED` | 锁定 HEAD 的真实 Windows/Keil/CodeWarrior 构建和专项测试证据 | 不等于真实 ECU、夹具/按键、烧录和波形验收 |
| `FIELD_VERIFIED` | 指定 ECU/固件/工位和场景的真实日志、测量、报告与回执 | 不应泛化到未测版本、波特率或风险工况 |

## 2. 本次 GitHub 精确快照（2026-11-21 读回）

| 分支 | 记录的 HEAD | 当前意义 |
|---|---|---|
| [`main`](https://github.com/ch3758134-prog/AutoTest/tree/main) | `ffb23f8c05ec4a64b82cd781db6357685d082c04` | 系统唯一**已接收的整机源码主线**；不代表所有实物与生产门禁通过 |
| [`R1`](https://github.com/ch3758134-prog/AutoTest/tree/R1) | `fa3b18e948043a9d1960adddb627658fd40c8159` | 当前首版真实自动 TEST/FLASH/RECORD 和局部 WAVE/UI **源码候选**，相对 main ahead 98、behind 0 |
| [`Main_Control`](https://github.com/ch3758134-prog/AutoTest/tree/Main_Control) | `e06a820e0d54dc7719cc75af1ecae005f05e164f` | 固件独立分支，0x42/0x43 新能力静态交付，未同步 main 内固件目录 |
| [`Key_Sim`](https://github.com/ch3758134-prog/AutoTest/tree/Key_Sim) | `313ed34ecc8a6e01529998787c8c870cc2e4db4a` | KEY_SIM 固件资源，尚有引脚及电路实测风险 |
| [`ui/ide-design`](https://github.com/ch3758134-prog/AutoTest/tree/ui/ide-design) | `8f690c5c46f5b86af8cb7810b3059699ad9011f2` | 活动全产品 UX/视觉/交互研究，研究或静态 UI 代码不得自动冒充 R1 真实流程 |
| [WAVE 研究分支](https://github.com/ch3758134-prog/AutoTest/tree/research/r1-wave-judgement-20261120) | `7f0e5ad4600811f06def67f0e62d38d8ce868225` | W-00～03 候审设计，尚无新 WAVE 生产算法源码 |
| [SCAN 研究分支](https://github.com/ch3758134-prog/AutoTest/tree/research/r1-scan-configuration-20261120) | `a62d2e422b5607b41c97374466a78e9ad2b51333` | S-00～03 候审设计，生产配置绑定表/计划桥未实施 |
| [TEST-RECORD 专项](https://github.com/ch3758134-prog/AutoTest/tree/feature/test-record-backend-20261119) | `655e1bfe0d95a4a885574450faa79fcf1cff49ef` | Draft PR #65、B-01/C-01 静态修复认可；R1 已**复制/接入相关结果能力**，PR 自身仍 Draft、未合并 HOST |
| [`Project_Management`](https://github.com/ch3758134-prog/AutoTest/tree/Project_Management) | 本次整理前 `783c553cb6cae014d8abf78384367bcdafa93d09` | 唯一正式项目管理事实写入分支；本次整理会更新 HEAD |

以上为一次性截图，**不是动态状态看板**。专业 AI 工作时必须回读最新 Commit/PR/Issue 与依赖文件，给出当前 SHA。

## 3. 真正的仓库结构与分支差异

整机 main 根目录为 `HOST/`、`Main_Control/`、`Key_Sim/` 三套**平级工程**，另有 `docs/` 和 `tools/project-control-v2/`。Windows WPF/Prism/.NET 解决方案仅在 `HOST/AutoTest.sln`；不要写成根目录 `src`/根目录 `firmware`。独立固件 `Main_Control` 分支根是旧 `Project/`，与 main 目录布局不同：**不能直接把整分支合并到整机 main/R1**，应待 Keil/实体审核后定点移动增量到 `Main_Control/`。

main 已接收 G4 PR #61 的历史代码成果，但旧 `feature/g4-html-parity-20261115` 远端 ref 仍存在；`G4_BRANCH_RETIRED` **不等于** `G4_BRANCH_DELETED`。原 G4 标准、效果图、历史 UI PR 必须保留版本和审查证据，不作为当前 R1 必须回去实施的指令。

## 4. R1 当前状态不得继续沿用“刚建分支”描述

2026-11-20 的 [R1-00～03 统一候选](https://github.com/ch3758134-prog/AutoTest/blob/R1/docs/r1/R1_00_03_UNIFIED_SOURCE_CANDIDATE_20261120.md) 已列出顺序执行、自动 Flash、正式 RECORD/报告、波形采集/比较源码候选和局部操作 UI。R1 最新的 `RealTestWorkbenchService` **确实**调用 `RealTestRecordSession`，自动 Flash 适配器、DB 和报告导出路径也已存在，不能再沿用 11-19“还完全未接线”的静态结论。

但 R1 文档明确 `WINDOWS_RELEASE_BUILD_PENDING / TESTS_PENDING / WPF_START_PENDING / ECU_FIELD_PENDING`；未发现固定最新 HEAD 的合格构建和真实 ECU 全链回执。这是**运行准出尚未满足**，不等于所有代码仍待设计。历史独立 FlashLab 真实成功日志仅证明对应版本/流程的**限定场景**，不能推定 R1 集成已实测。

## 5. 真实主控 FW 与 R1 HOST 不能相互代言

`Main_Control` 新 `0x42/0x43` 仅源码静态批准，`main/Main_Control/` 尚无该命令；R1 现有 HOST `HardwareConnectionService` 已有请求/读回/忙态检查和工位 epoch 逻辑，但**真实板需确实烧录新固件**。旧 GET_VERSION 固定 `v0.4-dev` 不能证明能力。确认波特率只说明固件配置，不等于真实总线测速或 ECU 有效响应。

## 6. 本次整理不是额外的设计或测试批准

这里**整理和确认已有明确决定的优先级**；WAVE 新算法 V2、SCAN 逐编号绑定 V2、出厂 ReleaseScope、R1 现场 PASS、全产品 UI 冻结等仍分别走技术/用户验收。管理文件对跨专业改动提出缺口和评审要求，不替代各专业的安全实现。
