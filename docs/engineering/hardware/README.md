# AutoTest｜硬件工程资产（HW Design Reference）

**唯一阅读入口**。本目录整理 TestCtr V1.5 主控板和 EPB_ODD V1.1 按键板的**设计态硬件事实**，作为后续固件开发、扩展、硬件审查的持久参考。**不代表现场已验收，也不授权修改源码或设备操作**。

## 从这里开始

| 对象 | 电路分析与资源 | 原始设计证据 |
|---|---|---|
| 主控板 | [boards/testctr-v1.5/README.md](boards/testctr-v1.5/README.md) | [TestCtr V1.5 原理图（5页）](../../project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf) |
| 按键板 | [boards/key-sim-v1.1/README.md](boards/key-sim-v1.1/README.md) | [EPB_ODD V1.1 原理图（4页）](../../project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-KEY-EPB_ODD-V1.1.pdf) |
| 跨板/源码差异 | [interfaces/README.md](interfaces/README.md) | [design_source_issues.csv](interfaces/design_source_issues.csv) |
| 数据索引 | [manifest.json](manifest.json) | 版本、文件、证据状态 |

**原则：原理图和官方手册是设计/芯片能力证据；CSV 是可检索的派生索引；固件源码是特定 SHA 下的实现，不会覆盖设计事实。** AI 和工程师使用同一套 GitHub 数据，不需要下载另一份“AI 专用 ZIP”。

## 读取顺序

1. 明确板卡与原理图修订，阅读相应 board README 和 PDF。**主控、KEY_SIM 不合并成一套 MCU 映射**。
2. 看 MCU 物理 pin、设计 Net、相关功能模块、连接器触点。命名空白不等于未接线。
3. 要评估复用/改脚时，单独核对官方 datasheet 的 ALT/MUX、工作电压、驱动能力、板级负载与保护，不得把某个“GPIO”自动判为空闲。
4. 需要研究控制语义时再看固定固件提交与 [差异表](interfaces/design_source_issues.csv)。
5. PCB layout/netlist、装配版、线束、运行 HEX、实测均未得到可关联到本版本的证明，全部保持 UNKNOWN；不得推断为现场事实。

## 稳定证据源

- 主控设计图：`docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf`，SHA256 `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353`。
- 按键板设计图：`docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-KEY-EPB_ODD-V1.1.pdf`，SHA256 `2f642b559ddcb4c325e872a67b0d74e0472f0db5bcb9021c1cbb4ff8596a6b2d`。
- 历史主控源码：`Main_Control@c830dbcd56b436688437b446d222473a88b85098`。
- 历史按键板源码：`Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a`。
- 不复制 PDF 二进制，不把旧 HTML Cross-Probe 作为本目录数据权威来源。用户阅读版 HTML 的后续版本另行版本化，不以本次 CSV 提交冒称已发布。

## 记录定义

`SCHEMATIC_ONLY` = 仅原理图；`ALT_NOT_FULLY_REVIEWED` = 复用表未完成；`UNKNOWN` = 缺少对应证据；`UNRESOLVED` = 存在矛盾且未裁决；`SOURCE_ONLY` = 仅所指固件 SHA。所有引脚与触点数是设计索引完整度，不是实装验证率。

此仓库目录提供工程参考和候审数据；在项目管理批准前不修改主控、KEY_SIM、HOST 或现场连线。
