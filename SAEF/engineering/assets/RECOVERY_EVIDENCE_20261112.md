# AutoTest G0｜原始硬件/固件资产检索与哈希复核记录（2026-11-12）

> **记录类型：检索事实 / 非 G0-01 工作产品内容批准 / 非 Gate 决定。** 项目负责人指示自行从 ChatGPT 资料库查找原理图和 `KEY_SIM` / `EPB_Test_Keil` 固件源码并上传。此文仅保留已完成的检索及核对证据；**二进制对象尚未进入此 GitHub 仓库**，故不得称为 GITHUB_ASSET_RECOVERABLE。

## 1. 原始输入已检索到并校验

在 ChatGPT Library 中检索 2026-11-12 的可用原件，提取原始字节后独立计算 SHA-256，结果：

| 数据对象 | 来源文件或归档路径 | 字节数 | SHA-256 |
|---|---|---:|---|
| 主控板原理图 | TestCtr-V1.5 20201117原理图(2).pdf | 1,956,928 | `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353` |
| 按键板原理图 | EPB_ODD_V1.1.pdf | 357,705 | `2f642b559ddcb4c325e872a67b0d74e0472f0db5bcb9021c1cbb4ff8596a6b2d` |
| 主控活跃工程归档 | AutoTest_G0_唯一图纸与现役固件事实基线_v0.2_待审.zip / evidence/Project.zip | 17,529,847 | `34b7e11b7d5d0fbb7b2178070f52e41febd88c15033a215516084ca294900239` |
| 按键活跃工程归档 | 同归档 / evidence/WS.zip | 6,025,867 | `ce032e5063d09f7d4fdce9dd190a8284bbf02ecbbd0258936d8f48387603e93d` |
| 原始主控外层 ZIP | EPB_Test_Keil.zip | 36,210,845 | `1cb761f08f9b67d27371bd724449c39e636a18cedd7aec83cd5457d59abd001f` |
| 原始按键外层 ZIP | KEY_SIM.zip | 13,253,741 | `13493051f0f8a1c3130a72ad448c35ca8f0179f1bef2a2d1bfdb23f0d634be88` |

当前 `engineering/control/assets.json` 的四个 SHA-256 均与实际恢复的两个 PDF 和两个子工程 ZIP 匹配，无登记哈希冲突。

## 2. 原始 ZIP 与台账工程 ZIP 的内容关系（已逐文件比对）

- `EPB_Test_Keil.zip` 中 `EPB_Test_Keil/Project/` 的 726 个文件与 `evidence/Project.zip` 中 `Project/` 的 726 个文件，**相对路径集合完全相同且文件内容 726/726 相同**。
- `KEY_SIM.zip` 中 `KEY_SIM/WS/` 的 5,651 个文件与 `evidence/WS.zip` 中 `WS/` 的 5,651 个文件，**相对路径集合完全相同且文件内容 5651/5651 相同**。
- 外层 ZIP 还包含其他文档、工程辅助资料、工作区缓存等，因此外层 ZIP 哈希与子工程 ZIP 不相同是预期情况；不是不同现役源码版本的证明。
- 以上证据证明了指定工程文件在两个归档之间的同一性，**不证明正在使用的现场板卡已烧录这些源码对应的二进制**。

## 3. 当前 GitHub 落档限制与后续核对

本次通过 ChatGPT 已连接的 GitHub 接口可以创建/读取文字与 Git 对象，但它不支持直接从已恢复的本地路径传入大型二进制；当前执行容器也无法解析 github.com，因此尚未完成二进制 push。本次记录不冒充已经上传。

两份原理图与两份归档版固件应按照 `assets.json` 的既定路径导入；原始外层 ZIP 则保留于子目录 `firmware/original/` 供精确回溯。所有对象必须以 SHA-256 再校验，完成 GitHub 实际非强制推送、远端 SHA/字节恢复核对以后，才能升级为 `GITHUB_ASSET_RECOVERABLE`。

本次未修改现役固件、未运行硬件动作、未宣告 G0-01 或 G0 Gate PASS。已生成含六个原始字节对象与受控机械导入脚本的本地交接包，供具备 GitHub 网络与认证的执行环境完成剩余二进制上传；其执行回执必须独立补交。
