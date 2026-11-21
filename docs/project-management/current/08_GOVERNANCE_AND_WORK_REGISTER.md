# AutoTest｜项目管理运行规则、分支治理与工作台账
**2026-11-21｜本文件是项目管理方法，不是软件产品运行功能。**

## 1. 唯一管理真值如何运作
[本目录 README](README.md) 是唯一当前业务入口；[00 决策台账](00_DECISIONS_AND_SUPERSESSION.md) 确定现在真正有效的要求；[06](06_R1_GAP_AND_ACCEPTANCE.md) 是一次性源码状态和待验证矩阵；[07](07_SPECIALIST_WORKPACKAGES.md) 是研究/整改交接。原 P0/G0/G1/G2/G3/G4、2026-11-21 旧基线、源码与专业研究保持可查，**不在历史文件上反向改写“当时已批准了什么”**。

每条工作具备六个并列维度，不能混为“已完成”：
- `USER_CONFIRMED`：负责人已经确定的业务方向。
- `DESIGN_PROPOSED`：研究提出但仍需选择的 Schema/政策/交互。
- `SOURCE_EXISTS`：准确 Git SHA 已有源码/静态审查。
- `BUILD_VERIFIED`：同一 SHA 的 Windows 或 FW 构建/测试证据。
- `FIELD_VERIFIED_EXACT_CASE`：对应 ECU、固件、工位、输入与结果的实物证据。
- `PRODUCT_ACCEPTED` / `BLOCKED`：用户完成具体产品验收或有明确阻断。

不允许：`SOURCE_EXISTS` 自动写成 `FIELD_VERIFIED`；试验版支持单一 HydEPB 直接泛化所有 ECU；研究大纲自动变成生产安全策略。

## 2. 分支权属与历史债务
| 分支/PR | 责任和处理 |
|---|---|
| `main` | 唯一已接收整机正式源码，受控集成；管理整理**不改 main** |
| `Project_Management` | 唯一项目管理事实与通用 Project Control 工具研发分支；本轮更新仅文档 |
| `R1` | 真实 HOST TEST/FLASH/RECORD/UI 集成候选，不等于现场通过 |
| `Main_Control`、`Key_Sim` | 各自 FW/原理图/IO/USB/现场验证；不整分支盲目合入 |
| `ui/ide-design` | 活动 UI/UX 产品研究，HTML/预览不是整机已通过 |
| `research/r1-scan-configuration-20261120` | SCAN 研究，旧复杂 Recipe 只保留研究证据，须接收新用户决策后修订 |
| `research/r1-wave-judgement-20261120` | WAVE 研究，算法/时间/证据先专项验证 |
| PR #61 | G4 已合并历史事实；旧 G4 分支仍需清理，不等于 UI 已验收 |
| PR #55/#60/#65 | 独立 Flash/UDS/TEST-RECORD 仍需对照 R1 差异；不再重复把相同代码合入 |
| PR #63/#66/#68/#69/#73/#74 | UI/R1 多分支候选，按真实交互/受控源码差异审查，不因为名称相似一键合并或删除 |

旧分支/文件物理清理必须核验：是否存在主线/现 R1 未接收的有效成果、依赖 PR、是否存在唯一实测证据、用户清理授权。退役不等于已删除。避免治理变成阻碍快速现场实测的繁琐审批仪式。

## 3. 实际工作台账（当前；先研究/整改，不声明源码已完成）
| ID | 当前任务 | 优先关注的真实缺口 | 主责/协作 | Gate |
|---|---|---|---|---|
| PM-01 | 新业务基线和旧 Issue 指向更新 | 已确认需求不能被旧 Issue #70/#72 覆盖 | PM | 本次正式落档，后续 GitHub 回读 |
| PJ-01 | 单型号一套资源/TEST 目录树、模板/自动保存 | 树形编译、版本引用、真实编辑/恢复 | PROJECT/TEST/UI | Windows+保存/重启 |
| TS-01 | TEST 用例失败后按编辑逻辑继续/终止 | R1 外层任意非 PASS 停止 | TEST/R1 | 真实 FAIL 仍保留最终 FAIL |
| FL-01 | 原生 Flash Profile、当前唯一固件、独立工具与生产配置边界 | XML 运行依赖、TEST Flash Step 绑定 | FLASH/PROJECT/R1 | Windows+真实烧录 |
| SC-01 | 型号+SN、跨工程匹配、扫码后夹具 | 真枪输入、Recipe 过度设计、跨工程未接 | SCAN/PROJECT/DEVICE | 扫码枪+真实工位 |
| HW-01 | 实体 START/STOP、夹具到位与断线事实 | IO/固件 USB 接入和机械状态未知 | Main_Control/DEVICE | 真实电气测试 |
| PR-01 | 工位状态编排（扫码→夹具→键→Flash→TEST） | 生产页面未接现有真实执行 | R1/SCAN/DEVICE/FLASH | 真实完整自动运行 |
| RC-01 | CAN 底本、关键测量、测试日志、简报和批量报 | 批次定义/复检口径/证据质量 | RECORD+SCAN | 数据复算、真实多次 |
| WV-01 | 真实采集参考和判定证据 | Wave 与 RECORD 型别及时间质量 | WAVE/TEST/RECORD | 正反例+报告 |
| UI-01 | 双工作区及无工程独立工具，统一浅色/保存/退出 | 初始布局、工具窗口、生产无鼠标 | UI/R1 | 实际 Windows 操作 |
| GA-01 | 建立精确的当前 R1 构建与实物证据 | PM 旧静态状态滞后、无最新 HEAD 闭环 | R1/PM | SHA+日志+RunId |

**当前最大不确定项**：R1 首版准出具体是否包含完整生产工位，用户尚未批准新的里程碑。以上是一份连续任务矩阵，不擅自把它变成新的 R0/R1/R2 强制版本分工。

## 4. 管理系统本体与 AutoTest 产品必须隔离
`tools/project-control-v2/` 是通用 GitHub 项目管理/证据管理工具，不是 AutoTest HOST 的业务模块。它可以管理决策、模块责任、变更、Gate、交接和来源追溯，但**不能把管理软件自己的数据 Schema、演示工位 UI 或内部审批流程塞到 AutoTest 产品里**。当前本轮重整只修改 AutoTest 的 PM 业务记录，尚未证明 Project Control 本地 Web/Windows 工具已验收；工具自身的研发仍遵循 [Project Control 原规则](../../../tools/project-control-v2/README.md)。

## 5. 给后续 AI 的最低交接合同
1. 先读现行 PM README/00/01~04 和与任务有关的 05/06/07，再回读**当天**的专业 Git SHA，不仅仅看早期概要。
2. 发现旧文件/Issue 与用户新决策冲突时，以新决策为产品行为，并把源实现列为差异；不隐性把旧策略继续开发。
3. 候选源、Windows/FW 构建、物理 DUT 实测分别交付；没有证据不写 PASS。真实 Flash 不许 Mock 代跑。
4. 优先保留已实测技术资产，简化产品入口而不是推倒真实协议后端。
5. 正式需求、设计要全文向负责人确认；本次用户已明确授权**管理系统全面整理并直接落档**。未来新增高风险产品政策仍需单独表决。
6. 硬件/危害/外设不能由 AI 猜测；对用户已批准的具体设计不要反复提问。

## 6. 后续审查节奏
用户验收应集中在**可实际操作的完成度**，而不是每个小改动都让用户重启工程。基础 BUG、异常、跨入口一致性先由代码/Windows 测试/静态审查主动发现；真实 ECU 验收必须有清晰工况、准备方法、证据导出和失败恢复。管理层需要主动关闭已明确过时的设计冲突，而不是让多个专项在废弃假设上重复劳动。