# AutoTest｜现行项目管理基线（2026-11-21 重新审定）
**唯一现行业务入口｜负责人本轮逐项确认并直接授权整理 GitHub｜PROJECT MANAGEMENT ACTIVE**

本目录建立在本轮最新的实际业务确认之上，**取代** [current-20261121/](../current-20261121/README.md) 作为日常产品需求与专业交接的优先读取基线。旧 P0～G4、原 2026-11-21 基线、研究分支、Issue 和 PR 仍可追溯，但其中与新决定冲突的内容不能继续当作当前任务合同。**管理落档不等于 R1 编译/真机通过或生产安全授权。**

## 一页读懂新的产品合同
**AutoTest = 独立专业工具 + 可共享资源 + ECU 工程/自动测试 + 生产检测 + 数据与报告 + 真实硬件通信。** 这些体系互相复用而不从属。

- **工程**：一个 ECU 型号、一套当前生效 DBC、固件、生产 Flash 配置、一套有序 TEST 用例树；目录只是分类，只有具体用例有步骤/执行能力；工程编辑自动保存，模板默认填好且可改。
- **工具**：DBC、手动台架、UDS、独立烧录、波形均可无工程运行，可按各工具实际需要使用资源；工具配置不强制等同生产配置。
- **开发 TEST**：单步骤/单用例/勾选范围、流程控制、失败处理、关键数据提取；开发无需扫码或先生产烧录。
- **生产**：放 ECU→扫码校验型号/必要时确认切工程→自动夹紧→等待实体启动键→**每次完整检测重新烧录**→自动执行全部已启用用例→日志/报告→受控收尾释放→显示结果并等待下次扫码；正常无需鼠标。
- **记录**：完整 CAN 底本 + 测试日志 + 简明单台报告 + 独立批量报告。批次/复检统计及真实设备/波形的细节**仍待研究实测**。

## 阅读顺序
| 文件 | 问题 |
|---|---|
| [00 决策与旧结论取代](00_DECISIONS_AND_SUPERSESSION.md) | 用户到底确认了什么、旧 R1/SCAN 哪些结论失效 |
| [01 产品、工具、资源职责](01_PRODUCT_SYSTEM.md) | 三个体系与技术能力如何关系、不再以 TEST 附属工具 |
| [02 ECU 工程与 TEST 树](02_ECU_PROJECT_TEST.md) | 单型号工程、分类目录、用例、模板、自动保存 |
| [03 生产工位与扫码](03_PRODUCTION_STATION_AND_SCAN.md) | 夹具、实体键、每次前置烧录、扫码跨工程、停止 |
| [04 数据、日志与报告](04_RECORD_LOG_REPORT.md) | CAN 底本、关键测量、单台报告、批量报告 |
| [05 硬件/固件和资源事实](05_HARDWARE_AND_RESOURCES.md) | ECU/KEY CAN、IGN、夹具、主控/KEY_SIM 事实与不确定项 |
| [06 R1 差异与验收](06_R1_GAP_AND_ACCEPTANCE.md) | 现有代码保留什么、改变什么、Windows/ECU Gate |
| [07 专项任务交接](07_SPECIALIST_WORKPACKAGES.md) | SCAN×REPORT、FLASH、PROJECT/TEST、WAVE、DEVICE、UI 的实际任务 |
| [08 项目治理与工作台账](08_GOVERNANCE_AND_WORK_REGISTER.md) | 分支/PR、状态、待办、Project Control 区分 |
| [09 来源与未决问题](09_SOURCE_TRACEABILITY_AND_OPEN_DECISIONS.md) | 原历史来源、精确 HEAD 快照、仍需真实研究/用户决定的事项 |

## 必须遵守的管理纪律
1. **USER_CONFIRMED、DESIGN_PROPOSED、SOURCE_EXISTS、BUILD_VERIFIED、FIELD_VERIFIED、PRODUCT_ACCEPTED 分开登记。**
2. 源码开发先回读**实时** GitHub HEAD，不从旧管理 SHA 认作最新；一切烧录/物理接口须真实证据。
3. 新决定优先于冲突的旧 Issue/研究；但不因此清空已有实测的 CanTool/UDS/TEST/RECORD 技术资产。
4. `Project_Management` 仅作管理文档与通用管理工具源码；R1、main、固件和专项分支有独立写入边界。
5. SCAN+REPORT 的条码/批次/复检/固定报告、实体 START/STOP、硬件现场保留与完整 R1 准出范围仍有**未定项**，不借本次整理自行定稿。

**历史入口**：[上一轮 2026-11-21 系统整合基线](../current-20261121/README.md) ｜ [更早 P0/G3 原文](../new-baseline-20261112/) ｜ [通用管理工具](../../../tools/project-control-v2/README.md)。