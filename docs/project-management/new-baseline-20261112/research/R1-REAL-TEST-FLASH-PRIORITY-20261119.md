# AutoTest｜R1 首版真实自动测试、烧录与报告优先推进决定（2026-11-19）

## 1. 负责人最新决定

**原话核心**：UI 专项继续长期研究，但首版实测和交付要尽快；主要完善第一版自动测试，**烧录必须纳入自动 TEST 全流程**；结束旧 G4 分支开发，重新以整机系统固定 `main` 为源建立专门 `R1` 分支；新对话独立负责后续的完整实现与实测闭环。

**管理状态**：`R1_DIRECTION_APPROVED / R1_BRANCH_CREATED / UI_RESEARCH_CONTINUES / G4_DEVELOPMENT_RETIRED / IMPLEMENTATION_PENDING / BUILD_PENDING / FIELD_PENDING`。

这是一项开发优先级、专业分支和首版验收范围的**负责人决定**，不代表 R1 任意功能已运行通过，也不代表全面 UI 视觉方案获批。

## 2. GitHub 分支与本次实际执行

| 对象 | 当前处理 |
|---|---|
| `main` | 固定系统源码基线 `ffb23f8c05ec4a64b82cd781db6357685d082c04`；包含 `HOST/`、`Main_Control/`、`Key_Sim/` 平级工程；此次不改 main |
| `R1` | **已从上述 main 精确 SHA 建立**；初次补充 `docs/r1/START_HERE.md` 形成独立任务入口；该文档提交为 `ab01b51c01d06c883a39ff390a2c945422eb4e65` |
| [Issue #70](https://github.com/ch3758134-prog/AutoTest/issues/70) | 已创建完整 TEST + Flash + Logs/Report P0 现场交付任务 |
| `ui/ide-design` | 保留现行活动 UI 设计源，继续全产品交互理念、对象操作、专业工具与 HTML 研究；**不以其全量设计完成作为 R1 首版实测前置** |
| `feature/g4-html-parity-20261115` | **退役，不再作为活动研发分支**；PR #61 已正式合并，旧 G4 HEAD 是当前 main 的祖先。当前连接没有 GitHub 删除 branch ref 动作，**用户要求的远端物理删除尚未执行**，需在仓库 GitHub Branches 页面删除后再记 `BRANCH_DELETED` |
| `HOST` | 保留旧专业/工作分支，R1 首版自动测试集成与改动从此优先转至 `R1`；不做非批准的自动合并 |
| `feature/test-record-backend-20261119` | Draft PR #65 的最新 `655e1bfe0d95a4a885574450faa79fcf1cff49ef`：B-01 和 C-01 源码静态复核认可，尚未通过 Windows Restore/Build/Test、实体测试，R1 依准入门禁接收而非盲合 |
| `feature/flash-lab-20261114` | Draft PR #55 专业候选；R1 用于梳理可复用专业 Flash 服务、Profile 和操作安全边界，不仅复制单独的 WPF 窗口 |
| `feature/uds-lab-20261118` | Draft PR #60 专业候选；继续以 UDS/ISO-TP 真实后端为基础，避免开发第二套协议栈 |

### 特别说明：G4 删除的实际状态

2026-11-19 当前 GitHub 分支列表里 `feature/g4-html-parity-20261115@92c6cb46a35ae11c1ffdefc0e0ece9339906d21a` 仍存在。根据 Compare，当前 main 相对旧 G4 HEAD ahead=3、behind=0，旧 G4 已成为 main 祖先，并由 PR #61 完成代码接收，**允许清理而无需丢弃未合并 G4 源码**。本次实际未执行分支删除，因为已连接 GitHub 操作只提供创建/更新分支，不提供删除 ref；不能写作删除成功。用 GitHub 仓库 Branches 页中的 Delete Branch 操作完成物理清理。对于 `integration/g4-pr61-resolved-20261119` 是单独的历史集成分支，不把它默认为本次单数 G4 删除目标。

## 3. R1 优先完成的真实用户流程

打开/新建可保存的 ECU 工程 → DBC/设备/编号资料 → 选择用例与运行子集 → 工位连接和前置检查 → 按步骤执行准备/测试/清理，包括真实 KEY/IGN/夹具/CAN/UDS/FLASH/测量断言 → 实时步骤和 CAN/诊断/设备日志 → 错误、取消、断连安全清理 → 结果/证据正式 RECORD → HTML/JSON/CSV 可回读报告 → 与生产计数/扫码流程的合规隔离。

第一阶段以一个可以直接实测的纵向闭环为主，不因全产品 UI 研究、外观风格重做或未集成的 Wave/Scan 全部功能而延误。

## 4. R1 的真实代码缺口与约束

根据 `main@ffb23f8c...` 静态回读：

- `HOST/src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` 列出 `Flash`/部分 UDS/KEY 新步骤但标记不可执行；当前 RealRunCoordinator 无真实 Flash 步骤执行分支。
- `HOST/src/AutoTest.Desktop/Testing/RealTestWorkbenchService.cs` 目前以独立 `RealRunEvidenceRecorder` 运行结束后保存 JSON，未接正式 TEST-RECORD/RECORD 报告。须由 R1 将真实 WPF 入口与唯一后端事实流接通，并与唯一硬件会话/安全 Fence 协同。
- 烧录测试必须引用已有 Profile/HEX/安全文件及冻结资源指纹；运行前检查 ECU 身份与真实固件/通道，复用 UDS/ISO-TP/Flash service，记录阶段、CAN/UDS 响应、传输 ACK、NRC、复位、APP 后验与异常；不得把 Flash WPF UI 嵌入步骤执行器，亦不能以 HEX 发送结束就宣称成功。
- 现 P0 TEST-RECORD 正式 PASS 门禁只对自身 `AssertSignal/WaitUntil` 直接 DBC 证据成立。烧录成功证据类型、纯烧录测试与烧录加 DBC 判定的报告/放行语义，须由 R1 + TEST-RECORD/Flash 专业研究提出符合真实 DUT 的合同，不能擅自放宽为“有 ACK 就 PASS”。
- 真实硬件路由必须按线束物理能力、USB 固件接口角色和版本；主控对外 ECU CAN 为 A1/A12、KEY_SIM CAN 为 B1/B11。KEY_SIM 20 Key 电阻网络输出五路模拟电压；不能按 20 个独立数字输出开发。
- 用户现场实测负责验证实体 ECU 真实动作、信号、烧录、结果。Windows 由 Codex 对准确 R1 HEAD 做机械编译、测试、启动，编译或 Mock 单测均不是实体通过。

## 5. 实施和审批边界

1. R1 新 AI 先自报身份并读取 [R1 启动合同](https://github.com/ch3758134-prog/AutoTest/blob/R1/docs/r1/START_HERE.md) + [Issue #70](https://github.com/ch3758134-prog/AutoTest/issues/70)，核对当前 Git HEAD、源文件和未实现节点；第一轮给源码能力矩阵/阻断图，再直接实施可实测路径。
2. 将细粒度问题尽量在 `R1` 单一交付分支内定点修复，不新增长期并行 G4 风格的漂移分支。大型共享协议合同仍按专业责任与安全门禁审查。
3. `Project_Management` 只记录正式决定、跨专业接口和分阶段 Gate；不会在本次凭项目管理自主增删设备硬件功能。`main` 不作为日常实验分支。
4. UI 专项可继续研究并形成新的 HTML/用户体验证据，R1 只落地当前实测必要的 WPF 功能；不能以 UI 全部未定为理由阻断真实自动 TEST + 烧录闭环，也不能把临时首版标记为最终 Fluent 设计验收。

**归档结论**：`R1_BRANCH_ESTABLISHED / FIRST_DELIVERY_PRIORITY_ON_REAL_TEST_AND_FLASH / G4_DEVELOPMENT_RETIRED_NOT_DELETED / PRODUCT_UI_RESEARCH_UNFROZEN`。
