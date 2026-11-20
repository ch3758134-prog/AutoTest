# AutoTest｜项目管理档案导航

**管理唯一写入分支：`Project_Management`。** 当前仓库 `main` 属于 HOST 软件正式集成，不用于新增正式项目管理工作产品。

## 现行新基线资料

- [2026-11-12 新基线 P0/G0/G1 已归档材料](new-baseline-20261112/)
- [2026-11-16 G3 增量变更、公共流程/KEY_SIM 与主控 USB/双夹具交接](new-baseline-20261112/G3-SUPPLEMENTS-20261116/README.md)
- [已批准 P 阶段汇总和审批记录](approved-20261113/)

## 正在整理的缺口

`main` 当前还保留 G2/G3 正式批准文件、冻结 UI HTML 与新版硬件档案，这些尚未全部集中迁移到 `Project_Management`。**本导航不假装资料已经完整迁移**；各项事实以精确 Git SHA 和相应审批记录为准。历史 SAEF 仍在 `SAEF/`，不能与新基线 Gate 状态混写。

## 治理

管理工具本体位于 `tools/project-control-v2/`；普通管理系统源码直接在本分支更新，正式工程成果在对话提交供负责人批准后再写入本分支，无 PM 交付分支或管理 PR。角色、资料与决策/实测证据分开登记。

## 2026-11-17 硬件与历史实测研究事实（不构成设计审批）

- [KEY_SIM V1.1 硬件、WS 固件和供电/引脚风险接收](new-baseline-20261112/research/KEY-SIM-HARDWARE-FACT-INTAKE-20261117.md)：K19/K20 GPIO/封装冲突、24V 理论功耗、三路电源、五路电压及分层实测条件。
- [旧 WPF 真实 ECU 工程与 44 条历史来源用例调查](new-baseline-20261112/research/LEGACY-WPF-FACT-INTAKE-20261117.md)：40项源码细节、KEY/DBC/CAN/TEST/UI操作经验及待迁移事实。

以上都是研究输入。已批准设计仍以 G3 补充合同为准，未完成实测的硬件约束与工程迁移不得自行宣称通过。

## 2026-11-18 FLASH 专项阶段证据

- [指定 HydEPB ECU 真实写入/FF01 校验/Boot 复位阶段结果与 APP 后验限制](new-baseline-20261112/research/FLASH-REFERENCE-VALIDATION-INTAKE-20261118.md)：已核对 FlashLab 源码和 PR #55，记录专项现场报告、确切 Profile、待补原始证据及 FLASH 责任范围；**不是 G4 总体验收或 PR 合并批准**。

## 2026-11-18 下一阶段研发顺序

- [G4 自动测试优先、FLASH 独立工作台接入、UDS 即刻研究，WAVE/SCAN 待自动测试基线实测后完整集成](new-baseline-20261112/research/G4-FLASH-UDS-WAVE-SCAN-SEQUENCING-20261118.md)：负责人已确定的推进依赖，不表示 G4 Gate 或 FLASH APP 后验完成。

## 2026-11-18 UDS 独立实测专项

- [UDS 专用研发分支与独立 WPF 实测上位机及分层验收合同](new-baseline-20261112/research/UDS-DEDICATED-BRANCH-AND-REAL-LAB-20261118.md)：开发分支 `feature/uds-lab-20261118`；独立 EXE 的真实 ECU 验收与 G4 TEST 集成分开管理。

## 2026-11-18 主控 IO / USB 接口更新

- [最新固件 GPIO 初始化、IGN 引脚修订、LED1～6 USB 0x1A～0x1C、RL_DRV 硬件内部化](new-baseline-20261112/research/MAIN-CONTROL-IO-USB-SOURCE-UPDATE-20261118.md)：已回读 `Main_Control@bf81a64e` 源码，Keil 编译和实物验收未完成；旧 IGN=PTC10/J1 C6 映射已废止。

## 2026-11-19 G4 阶段成果接收与新 UI 职责

- [G4 PR #61 源码受控接收、INTEGRATION Issue #62、Fluent UI 单一设计源](new-baseline-20261112/research/G4-PR61-BASELINE-ACCEPTANCE-20261119.md)：**最新用户决策优先**。G4 阶段源码认可但未正式合入 main；ui/ide-design 为唯一活动 UI 设计源，feature/ui-rebuild-20261119 暂停。

## 2026-11-19 PR #61 实际冲突解决进展

- [G4 PR #61 冲突已消解，待 Windows Build 与正式 main 接收](new-baseline-20261112/research/G4-PR61-MERGE-RESOLVED-20261119.md)：受控 merge 候选 `92c6cb46a35ae11c1ffdefc0e0ece9339906d21a`，GitHub `mergeable=true`，主线尚未修改，实机 G4 Gate 未改变。

## 2026-11-19 G4 已合入正式 HOST main

- [PR #61 正式合并及 main 基线验证记录](new-baseline-20261112/research/G4-PR61-MAIN-MERGED-20261119.md)：`main@eb1b74a3d7eeb6dcbb582d9c04c72f97f5e1204e`；Git 冲突解决，后续仅在 main 上做 Windows 构建和现场实测，G4 整体真实准出仍未判 PASS。

## 2026-11-19 整机系统 main 固定基线

- [HOST、Main_Control、KEY_SIM 三套源码归档到 main 的目录及版本清单](new-baseline-20261112/research/SYSTEM-SOURCE-BASELINE-FIRMWARE-20261119.md)：正式 `main@f00289f3e141f265211f6cf236b55ca435c01f44`，`HOST` 已快进至相同版本；原固件分支保留。Keil、CodeWarrior 与实体硬件验证仍分开进行。

## 2026-11-19 系统基线目录修正：三工程平级

- [HOST / Main_Control / Key_Sim 平级目录正式更正](new-baseline-20261112/research/PEER-PROJECT-DIRECTORY-BASELINE-20261119.md)：最新 `main@ffb23f8c05ec4a64b82cd781db6357685d082c04`，原 `firmware/` 不再存在，HOST 分支已同步。此前固件导入记录中的旧目录只是历史快照。

## 2026-11-19 TEST / RECORD 真实结果与报告后端专项

- [TEST-RECORD 独立专业 AI、结果采集和正式报告的 P0 实测闭环](new-baseline-20261112/research/TEST-RECORD-BACKEND-SPECIALIST-20261119.md)：Issue #64；`feature/test-record-backend-20261119`；后端研究和开发已派发，现有真实 TEST 到 RunRecord/Report 的缺口尚待实施与实体验证。

## 2026-11-19 TEST-RECORD PR #65 技术审查结论

- [PR #65 TEST/RECORD 结果、证据门禁及正式报告代码审查](new-baseline-20261112/research/TEST-RECORD-PR65-TECHNICAL-REVIEW-20261119.md)：**TR-00/01 设计方向有条件认可，TR-02 `CHANGES_REQUIRED`，暂不合并 HOST/main**；P0 B-01 正式 PASS 与真实判定 observation/occurrence 未作因果绑定，且 Desktop 接线、Windows Build、原始连续采集与真机均未完成。Issue #64 继续开放。

## 2026-11-19｜现代工业级 PC 软件 UI/UX 基础方法研究 v1.0 正式归档

- [UX-FOUNDATION-RESEARCH-20261119-01｜研究归档入口](new-baseline-20261112/research/UI-UX-FOUNDATION-20261119/README.md)：HTML 阅读版、Markdown 原档、ZIP 来源 SHA-256、分类审查和权限边界均已收录；状态 `RESEARCH_COMPLETED / ARCHIVED / PRODUCT_UI_NOT_FROZEN`。这是一份**全产品 UI/UX 基础设计参考**，不是新的 UI 视觉或交互定版、G4/G5 技术合同或 HOST 源码修改授权。现行设计仍由 `ui/ide-design` 提交并经用户操作验收。

## 2026-11-19 TEST-RECORD PR #65 B-01 复审

- [B-01 新因果证明源码审查及 C-01 编译语法阻断](new-baseline-20261112/research/TEST-RECORD-PR65-B01-REVIEW-20261119.md)：`5ade1bd9e94d3240e9d931d43005caac5778f515` 的 B-01 逻辑已获静态认可；回归测试中 JSON 字符串转义需要先修；PR 仍 Draft / `CHANGES_REQUIRED`，本地 Windows Build、HOST 接线与 ECU 现场测试各自保持独立门禁。

## 2026-11-19 TEST-RECORD PR #65 C-01 修复验收

- [PR #65 C-01 单行修复静态验收与 Windows Codex 构建门禁移交](new-baseline-20261112/research/TEST-RECORD-PR65-C01-CLEARANCE-20261119.md)：专项提交 `655e1bfe0d95a4a885574450faa79fcf1cff49ef` 已回读，B-01 因果逻辑与 C-01 语法静态认可；**尚无 Windows Release Build/Test、HOST 接线和实体 ECU 证据，PR Draft 未获合并批准**。本条取代此前 C-01 当前仍阻断的状态描述，但保留其历史记录。

## 2026-11-19｜R1 首版真实自动测试与烧录优先推进

- [R1 独立分支、UI 研究并行、旧 G4 退役及 FLASH/RECORD 接线范围](new-baseline-20261112/research/R1-REAL-TEST-FLASH-PRIORITY-20261119.md)：从 `main@ffb23f8c05ec4a64b82cd781db6357685d082c04` 建立 `R1`；R1 专注自动测试全流程真实运行、CAN 烧录、执行日志/证据与正式报告，Issue #70；`ui/ide-design` 独立继续。旧 G4 代码已通过 PR #61 接收入 main，但旧开发分支远端 ref **尚未物理删除**（GitHub 连接暂无删除动作）。首版开发及真机验收均待继续。
