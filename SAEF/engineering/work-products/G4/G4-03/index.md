# G4-03 · 单元测试与静态检查

> 状态：DRAFT / 阶段性实施记录，未接受、未完成 G4 Gate 评审。权威状态见 `engineering/control/work_products.json`。

## Windows 自动测试汇总（来自 Codex 用户回执，非本环境执行）
| 测试项目 | 通过 / 总数 |
|---|---:|
| Devices.Tests | 24 / 24 |
| Specialists.Tests | 41 / 41 |
| Application.Tests | 16 / 16 |
| Projects.Tests | 10 / 10 |
| Records.Tests | 12 / 12 |
| Testing.Tests | 15 / 15 |
| Desktop.Tests | 6 / 6 |
| Integration.Tests | 13 / 13 |
| Contracts.Tests | 8 / 8 |
| **合计** | **145 / 145** |

## 过程来源
- V0 基线最初 18/18；八方向第一轮集成 68/68；八方向第二轮集成 137/137；WinUSB B0/B1/B2 增量编译后 145/145。
- 新增硬件链路模拟测试为 HW-USB-01～05、HW-CAN-01～03；不等于主控与 ECU 的物理联通验证。
- 解决编译类型、SQLite 依赖、测试工程接线、按键板测试向量以及后续 Records/Wave/Application 测试偏差；详见 G4-06。

## 待补原始证据
- 需从 Windows 导入 Restore、Release Build、`dotnet test` 完整日志（含配置/时间/运行命令/退出码）。
- 需在提交 WinUSB 工作树检查点之后重新测试该**已提交 SHA**，才可形成可重复的 Release 证明。
- 本阶段没有静态分析全量结果，不得宣称源码质量或物理安全认证已通过。
