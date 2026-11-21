# AutoTest｜项目管理正式入口

**当前唯一管理基线：[现行系统总入口](current/README.md)**（2026-11-21 依据负责人本轮逐项确认的产品要求重新全面整理并直接落档）。

## 当前应阅读什么
- [00 负责人已确认决策与旧要求取代](current/00_DECISIONS_AND_SUPERSESSION.md)
- [01 产品、独立工具、通用资源](current/01_PRODUCT_SYSTEM.md)
- [02 一个 ECU 型号一个工程、TEST 目录树、模板和自动保存](current/02_ECU_PROJECT_TEST.md)
- [03 扫码、夹具、实体启动、每次必烧录、生产自动执行](current/03_PRODUCTION_STATION_AND_SCAN.md)
- [04 完整 CAN 底本、测试日志、单台报告、批量报告](current/04_RECORD_LOG_REPORT.md)
- [05 主控、CAN、IGN、KEY、固件真实物理事实](current/05_HARDWARE_AND_RESOURCES.md)
- [06 R1 源码差异、交付门禁与真机证据](current/06_R1_GAP_AND_ACCEPTANCE.md)
- [07 SCAN×REPORT、FLASH、TEST、WAVE、UI、DEVICE 专项交接](current/07_SPECIALIST_WORKPACKAGES.md)
- [08 项目治理、分支/PR、工作台账](current/08_GOVERNANCE_AND_WORK_REGISTER.md)
- [09 原始证据、历史资料和未决事项](current/09_SOURCE_TRACEABILITY_AND_OPEN_DECISIONS.md)

**重要**：旧 P0～G4、R1/SCAN 研究和历史管理基线的原文继续保留；与上述新决策冲突的旧要求仅作为历史证据。管理文档更新不等于 R1 源码改完、Windows/Keil 构建成功或真实 ECU/扫码枪/夹具通过实测。固件与物理接口真值不得被新的业务设想替代。

## 历史档案（供溯源，不再作为现行合同）
- [旧项目管理首页及历史时间线完整快照](archive/HISTORIC_README_BEFORE_20261121_REBASE.md)
- [2026-11-21 上一次全系统管理基线](current-20261121/README.md)
- [2026-11-12 P0/G0/G1/G3 与专项原档](new-baseline-20261112/)
- [已批准的旧 P 阶段文档](approved-20261113/)
- [现行 Project Control 通用管理工具说明](../../tools/project-control-v2/README.md)

**分支权属**：本轮只更新 `Project_Management` 的管理文档与已更新的相关 Issue 说明；`main`、`R1`、固件及研究源码不会因为业务整理而被自动修改或合并。
