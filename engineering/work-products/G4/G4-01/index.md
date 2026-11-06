# G4-01 · 源码实施与配置身份

> 状态：DRAFT / 阶段性实施记录，未接受、未完成 G4 Gate 评审。权威状态见 `engineering/control/work_products.json`。

## 已批准的设计输入（不改动）
- G3 v0.2 为既有已批准设计输入，G3 Gate 为 `PASS_WITH_LIMITATIONS`。
- G4 v0.2 实施方案此前已批准，原始附件仍位于 `annexes/G4_v02_approved_plan/`，本记录不覆盖其中内容。
- 9 个生产程序集继续为 Core、Contracts、Devices、Projects、Testing、Specialists、Records、Application、Desktop。当前样本 EPB 并非平台硬编码产品模型。

## HOST 源码的实际配置身份
- HOST 仓库分支：`g4/v1-stage-4`。
- DEVICE 软件检查点：`dd65cbdfbd849da98a4618bec8cb31923e695c95`，提交信息 `feat(g4-device): checkpoint read-only WinUSB and CAN bench tooling`。
- 阶段文档提交后的 HOST 当前 HEAD：`0098b9fcdc83db3331e661f76d1fc4375c4269c6`；工作树干净，未合并 `main`。
- 原始上传快照中的 7 个跟踪修改文件和 12 个新增文件已逐项按 SHA-256 核对并纳入 HOST 提交；生成物 `bin/`、`obj/`、`.vs/` 未提交。
- 本机 Windows 复验：Restore 退出码 0；Release Build 退出码 0，0 warnings / 0 errors；解决方案全量 Tests 退出码 0，145/145 通过、0 失败、0 跳过。
- 测试项目计数和阶段追加验证见 `engineering/evidence/G4/AT-G4-STAGE-20261105/SOURCE_SNAPSHOT.json` 与 `SOFTWARE_STAGE_REPORT.md`。
- 真实 USB/CAN/按键/ECU/IGN/Flash 硬件实测均为 `NOT_RUN`；自动化测试不代表 WPF 人工验收或物理验收。

## 合并与归档边界
当前仅构成**软件阶段候选交付**，并非完成版 ECU 测试台架。HOST 源码检查点及阶段文档已提交到开发分支，SAEF 本次登记阶段证据；不改变 Gate 状态、不表示正式接受，也不合并 `main`。

## 未关闭项
- Windows 硬件枚举、真实 USB PING、两路 CAN 物理收发、真实按键模拟电压、ECU/IGN 电气、DBC 实时采集、UDS 诊断及 Flash 均尚无实测通过证据。
- 完整 WPF 手动联调工具与专业 CAN/DBC/UDS/波形 UI、自动测试操作闭环尚未完成人工验收。
- `CONTRACT_REVISION` 原本为候选状态；本次未重新批准公共 CT 合同。
