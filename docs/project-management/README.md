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
