# SAEF｜资产与克隆恢复能力审计（2026-11-09）

**性质：审计事实和修复待办，不是原始资产文件或 Gate 批准。**

## 1. 当前 GitHub 的真实状态

按远端 `AutoTest/main` 完整 Git 树检查：`SAEF/engineering/control/assets.json` 维护 4 条原件输入登记，但以下路径**均未出现在 Git**：

| 资产 ID | 冻结源登记路径 | 登记大小 | 当前 Git 字节 |
|---|---|---:|---|
| HW-MAIN | `engineering/assets/hardware/HW-MAIN-TestCtr-V1.5.pdf` | 1,956,928 | MISSING |
| HW-KEY | `engineering/assets/hardware/HW-KEY-EPB_ODD-V1.1.pdf` | 357,705 | MISSING |
| FW-MAIN | `engineering/assets/firmware/FW-MAIN-Project.zip` | 17,529,847 | MISSING |
| FW-KEY | `engineering/assets/firmware/FW-KEY-WS.zip` | 6,025,867 | MISSING |

登记的 SHA-256、来源身份、Approval/历史 Gate 状态仍以 `control/assets.json` 为权威。这里绝不修改已登记值、替换被批准的原件，也不将相近的整个开发工程 ZIP 当作等价原件。

## 2. 本轮可以确定的当前硬件源码候选

本次会话已有这两份上位机研究所需的硬件工程 ZIP，文件指纹如下（是**独立来源身份**，不是宣称与上述 4 条相同）：

| 输入 | SHA-256 | 字节数 | 当前使用入口 |
|---|---|---:|---|
| `EPB_Test_Keil.zip` | `41773e72d55525e4518e045c1c6dc77ac57c7414e27acc1dbf705654bce55cc2` | 18,582,780 | `EPB_Test_Keil/Project` |
| `KEY_SIM.zip` | `13493051f0f8a1c3130a72ad448c35ca8f0179f1bef2a2d1bfdb23f0d634be88` | 13,253,741 | `KEY_SIM/WS/EPB_EOL` |

二者包含其他候选目录、工具/生成文件；**不得仅依据 ZIP 名称把它们伪装成原 SAEF 冻结的 `FW-MAIN-Project.zip` 或 `FW-KEY-WS.zip` 原始字节**。当前允许用于核对真实设备协议和板级行为的源码研究，用户未批准修改、重编译签发或更新固件。

## 3. 归档缺口与安全恢复规则

- **A-01 BLOCKED**：4 份原始资产未得到与登记 SHA-256 一致的远端可恢复副本。公开 Git 是否合适保存客户/生产固件与原理图须先审查授权与保密；可以使用受控私人仓库/制品库并在这里记 Commit/Hash/恢复路径。不能直接把原件发到公开 Git。
- **A-02 OPEN**：原 SAEF FRAMEWORK/catalog、`eng.py` 与归档工具的原始文件、完整获批 HTML/原始 ZIP 字节未在本 Git 树查见，需要恢复或在新治理下另建可审计检查程序；不要把“本次目录/登记核对”冒称 `eng.py check PASS`。
- **A-03 HISTORICAL**：`control/project_state.json` 中 `source_repository` 和 `source_revision` 为原 G4 历史 HOST 定位；不得替换当前单仓地址覆盖历史批准信息。今后引用新单仓 `ch3758134-prog/AutoTest` + exact SHA。
- **A-04 OPEN**：新 ADR/CR 必须完成影响产品追踪及受控审批，已批准 G3 内容不擅自改写。
- **A-05 DONE for Git-text-only**：55 份工作产品的登记路径均存在，41 个 ACCEPTED 各有登记 SHA 与批准记录路径；逐个原始字节重算 SHA 和历史 Gate 审查需以实际运行审计脚本/SAEF 检查为证据，尚未声称通过。

## 4. 两份原理图原件已找回，用户授权入 Git（更新记录）

2026-11-09 通过历史 Library 文件实际取得两份原始 PDF，并按**原始二进制字节**重新计算 SHA-256：

- HW-MAIN：1,956,928 bytes，`44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353`，与 `assets.json` 完全一致。
- HW-KEY：357,705 bytes，`2f642b559ddcb4c325e872a67b0d74e0472f0db5bcb9021c1cbb4ff8596a6b2d`，与 `assets.json` 完全一致。

用户明确批准上传两份原理图；`SAEF/.gitignore` 已允许其确切 Git 路径；`engineering/assets/hardware/README.md` 已登记。**当前 GitHub 工具写入接口支持 UTF-8 文本、或由调用方直接提供完整 Base64 Blob，但无法直接读取本轮容器中二进制文件并转交 GitHub。故此轮并未将两份 PDF 原始字节写入远端，不能标为 DONE。** 已生成两个带准确 SHA 的本轮输出副本，供完成后续 GitHub 原件上传；写入完成后必须重新获取远端 Git 树并校验 SHA。

原理图上传授权不等于允许改动两套冻结固件。两份冻结固件源 ZIP / 当前正式源码尚未完整进入 Git。

**现在可以直接拉 Git 开始测试已批准的软件范围，但“删除本地所有档案和固件，仅靠 Git 完整恢复”目前仍为 NO。** 待 A-01/A-02 解决后再给出全量可删结论.
