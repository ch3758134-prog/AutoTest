# CR-20261109｜真实按键电压设计遗漏与 G3/G4 工作产品影响

- **来源**：2026-11-09 用户纠正跨对话信息丢失；既有《交接AutoTest进展整理》及历史 G4-LAB 讨论。
- **ADR**：`SAEF/engineering/decisions/ADR-20261109-REAL-KEY-VOLTAGE-SEMANTICS.md`。
- **状态**：`RECORDED / IMPACT_REVIEW_REQUIRED`。**本文件已直接归档于 GitHub AutoTest/main 的 `SAEF/engineering`，不是“仅本地 SAEF”。**
- 不覆写早期已接受 G3/G4 工作产品与 Gate；本 CR 负责推动现役受控工作产品增量修订和复核。

## 缺口判定

旧材料已知道模拟板 20 Key/电阻网络/5 路输出，但早期真实台架和 TestProgram 语义曾把 K01～K20 当成操作用户应直接操纵的“真实按键”。用户更早和本次再次澄清：真实按键操作是施加特定模拟电压，20 Key 只是用于合成该电压的底层电阻网络开关；必须对不同 ECU 的输入通道与阈值建立可配置、可版本化的映射。2026-11-06 G4-LAB 的 ControllerDefinition / ControlState / StateHardwareMapping / VoltageAcceptance / MeasurementFact / OperationPolicy 设计已经体现该方向，**但没有自动证明 G3/G4 正式工作产品都同步更新**。

## 工作产品与专业影响矩阵

| 领域/负责人 | 必须核对和落实 |
| --- | --- |
| SYS/HW / G3 | 真实按键→电压→ECU 输入窗口；20 Key 多位保持合成；5 路输出；每 ECU 适配 |
| PROJECT / 资源修订 | Profile/Controller/State/Mask/通道/目标电压与窗口、Revision、映射验证与过期引用 |
| DEVICE / Station | 真实 USB-CAN-KeySim 链路、20-bit 完整状态、保持/释放、发送与实测分离、断连 UNKNOWN、物理门禁 |
| BENCH/UI | 真实控制器按键动态操作；底层 20 Key 仅标定/诊断；短按、长按、保持与实时反馈 |
| TEST / Runtime | 同一映射和 Station 发送；动作序列、WAIT 不释放、明确恢复；失败和清理 |
| RECORD / Trace | Requested/TransportAccepted/Measured/EcuResponse/Verified 各自数据源、五路测量、时间、Revision |
| CAN/DBC/WAVE | DUT 业务 CAN 反馈不得冒充真实按键电气输出已通过 |
| INTEGRATION | 项目 Profile/Controller Revision 与 DEVICE 统一状态/会话/Trace 接线 |
| 验收 | 双 ECU Profile、多 Key 合成、Revision、并发仲裁、实测 UNKNOWN 与五路标定证据 |

## 受控处理

1. 现已直接在 **远端 GitHub** `ch3758134-prog/AutoTest` 的 `SAEF/engineering/decisions` 与 `SAEF/engineering/change-requests` 追加 ADR/CR，供所有专业 `git pull` 后使用。
2. SAEF 管理者下一步须定位现役 `SAEF/engineering/work-products` 中受影响的 G3/G4 正式工作产品，形成**真实受控修订**；保持既有历史记录及审批决定，不伪造新 Gate PASS。
3. 在有 SAEF 的本机可运行 `TOOLS/eng.py check` 并验证，新 ADR/CR 是否需要原生 registry 元数据；运行结果、issue 和 SHA 留痕。未经实际运行不能声称检查 PASS。
4. 软件专业依据本 ADR 先进行专业内设计/修订，公共 Contracts、Station/物理安全策略、资源语义变动走主对话/INTEGRATION 指定唯一负责人，不允许 Codex 自主修复或重构。
5. 对于后续用户在任一专业对话中确定的重大需求/硬件事实，应在当轮形成可归档增量并明确更新当前远端 `SAEF/engineering`，除非用户另行指定；不得只保留在聊天、临时 MD 或个人记忆中。

状态推进：`RECORDED → IMPACT_REVIEW → WORKPRODUCTS_UPDATED → SAEF_CHECK_PASS → BASELINE_LINKED`。本次新文档写入只满足第一项，后续状态必须以真实工作产品及工具检查为证据。
