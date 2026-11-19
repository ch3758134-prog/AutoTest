# AutoTest｜G4 双主线实测交付：范围核查与最小数据合同申请

**状态：PARTIAL_CODE_CANDIDATE / DOMAIN_CHANGE_PENDING_PM / WINDOWS_UNVERIFIED / HARDWARE_UNVERIFIED**

核查分支：`feature/g4-html-parity-20261115`，工作起点 `524dc4f110c49f550f386e41bd454673e68f73df`。本文件只记录经源代码核查确认的现状和需要项目管理批准的合同差异，不宣称已完成 A/B/C 全项。

## 已确认的本轮产品决定

- **A 自动测试**与**B 真实手动台架**是并行交付主线；手动台架承担真实硬件通路联调和排故，不得延期为附属工具。
- 取消独立按键配置的历史讨论**不取消手动台架命名按键动作编辑/保存/调用**；当前必须支持任意多段 KeySet/等待的动作资源。公共流程无须先建成。
- CAN1 接 DUT，CAN0 接 KEY_SIM；KEY_SIM 的 K01–K20 是电阻网络开关，产生五路模拟电压，不是 ECU 的二十路离散输入或 DAC。
- 不添加 DUT 主电源控制；夹具控制必须在引脚、固件命令及机械连接复核后才开放。USB ACK 不代表 ECU 行为、实际电压或产品合格结论。
- 复用唯一真实 USB/CanHub、设备授权、工程资源版本与 TEST DSL；不得复制 USB/KEY 设备实现。

## 源码核查：已存在、缺失、禁止误报

| 主线 | 核查到的现有实现 | 尚缺／阻断 |
| --- | --- | --- |
| 自动测试编辑 | `TestDocument / StepEditTransaction / TestEditorViewModel`、三阶段、Undo/Redo、Inspector 及正式工程保存 | Windows 连续操作、实际保存/重开与冻结 HTML 验收尚无本轮证据 |
| 用例选择 | `TestProjectWorkbenchViewModel.RunSelected`、全选/清空、项目排序、`ProjectRunSelectionState`、持久 `Enabled` 分离 | `PreflightSelected` 仅检查固定引用是否存在，明确返回 `BLOCKED_BY_BACKEND` |
| 真实单用例执行 | `RealTestWorkbenchService` + `RealRunCoordinator` + `ConfiguredStationActionPort` | **没有统一项目级计划/运行**；不可循环调用若干独立 `ExecuteAsync` 冒充 |
| 手动 USB/CAN | `HardwareConnectionService` 唯一会话/CanHub、CAN0/CAN1 接收、CAN1 单次发送、工程 DBC 编码，单次精确人工授权 | 无周期发送所有权/停止合同，未完成现场验证；日志窗口内最多保留 700 项 |
| 手动 KEY/IGN | 20 位组合、硬件 `HardwareKeyPolicy` 校验、CAN0 ACK、手动 IGN 请求；自动 TEST 写入时手动通道已有物理锁 | 无可保存连续 KEY 动作；停止/断连后实体状态不能推断为已恢复 |
| 夹具 | UI 禁用夹具操作 | 未核准命令码、GPIO 和 J1 输出对应关系，不开放实体指令 |

### 本批已改的安全范围

- 手动台架调整为**左 CAN/USB 实时监视、中 KEY/IGN/夹具、右 CAN1 发送/DBC 编辑**；保留既有真实发送与确认。
- 手动监视增加按时间、通道、方向、ID、数据和说明的文本过滤；过滤只影响显示，**不删除内存原始记录**；导出仍是当次窗口保留的日志（当前上限 700），不是持久化全量采集。
- USB 主控身份仅在真实设备发现和 Ping 成功后暴露给界面，连接世代直接取 `CurrentStationEpoch`；断连清空身份，历史实体状态不可继承。
- 未增加新的设备输出命令、绕开人工授权、伪造 ACK 或更改 TEST/Controller 持久结构。

## PM 定点审批申请：B4 命名 KEY 连续动作资源（最小合同）

**为什么不能直接沿用 ControllerState：** 现有 `EcuControlProfile.Controllers[].States[]` 只有一个 `KeyMask`、`ControlInteraction` 和固定 `ButtonActionTiming`，`ButtonActionPlanner` 可表达预定短按/保持等语义，却**不能无损保存任意长度、有独立 KeySet 和 Wait 段、可任意重排且不隐式释放的连续控制动作**。把它拆成 Pulse/Release 会改变用户的实体输出意图。

建议新增独立的工程资源 kind `bench-key-actions`（一个工程的有序动作目录），不改原 Controller schema 1，不移除硬件策略：
- `KeyActionCatalogDocument`: `schemaVersion=1`、`catalogId`、`name`、按顺序的 `actions`；作为 `ProjectWorkspaceService` 原子资产保存，沿用 `AssetId / Revision / SHA256`、并发冲突、manifest 回读。
- 每个动作 `ActionId: Guid`、`Name`、`Order`、非空 `Segments`，段含稳定 `SegmentId`、`Kind=SetKeys|Wait`、`DurationMs`；SetKeys 另有 `Mask: 0..0xFFFFF`，**允许 mask=0**。不强制首段、末段，不自动补零掩码或恢复。
- Catalog 保存 **`ControllerAssetId + ControllerRevision + ControllerSha` 固定硬件策略引用**。执行前确认文件 hash、当前硬件资源型号、可用位和禁止组合；如配置修订则显式更新引用，不能静默追新。
- UI：命名按钮左键在工位许可下执行，右键编辑；添加、删除、重命名、排序，段的增删复制、移动、撤销、保存重开；`Wait` 不发送任何 CAN0 帧。
- 执行器不复用完整 TEST 解释器；只在统一 `HardwareConnectionService` 上建立**有界手动动作租约**，一次显式授权锁定动作的版本/完整帧序列与连接世代，和自动 TEST lease 互斥。每段写入前检查取消/连接世代，执行记录 `ActionId/SegmentId/RequestedMask/USB-ACK/Error`；ACK 丢失、断连、停止时实体状态标 `UNKNOWN`，不推断 KEY 已释放。
- 新存储不改变既有 Controller 的“ECU 业务功能状态”用途；硬件约束仍取受控 Controller/HardwareKeyPolicy。不得让命名动作配置成为直接单步 KEY 控制的强制前置条件。

**申请项目管理批准的两点：** (1) 新资源 kind 与字段/版本合同；(2) 有界手动动作租约的授权与取消/UNKNOWN 策略。获准前不写新正式资源 codec 或真实连续执行器。

## 同步必做的 A / B / C 实现合同

### A：统一项目 Run（不允许单用例 Run 循环）

先按 `RunSelected` 的正式有序 `TestProjectDocument.Entry` 构建一个不可变 `PreparedProjectRun`：固定 project / 每个 TEST 及 Controller/DBC 依赖的 Revision/SHA、整个选择集与顺序、全量授权 frame 范围、总指纹。整批只获取**一次 Station Lease + 一个 RunId**，共享一条受控会话，逐用例运行同一解释器的阶段步骤并执行每用例 Cleanup，最终项目 Cleanup 只运行一次；取消/故障按合同收尾。用例 Fact、Step Fact、Correlation、设备证据都挂同一 Run，且标记各自用例身份，资源漂移或 Unknown 禁止继续。先定义并验证真实证据结构，再开放“运行所选”。

### B：周期 CAN 和夹具

周期 CAN 需要与手动动作和项目 Run 共享工位实体占用权，须由设备服务拥有发送任务与 CancellationToken；操作者一次批准清楚的报文/周期/持续条件，停止/窗口关闭/连接世代变化/TEST 租约申请均能撤销，且应等待真实发送任务退出。不能用 UI DispatcherTimer 循环调用一次性授权，也不能偷用自动 TEST fingerprint。

夹具在独立 HW/FW 交接中核对 J1/GPIO/输出高低、主控真实命令、ACK/UNKNOWN 后，另开受控实现。本批绝不猜 USB opcode；DUT 外部电源无需上位机新增控制。

### C：共享与验收

所有 CAN1/KEY_SIM/IGN/夹具真实操作应通过唯一 `HardwareConnectionService`，TEST 使用适配同一服务，不应有第二套 USB 通道。实际设备测试分别提供请求内容、会话世代、ACK 或错误、CAN RX、可用外部物理观测；不以设备 ACK 认定 ECU PASS。

## Gate / 必须提供的后续证据

1. Windows 本地 Build/Test 与 1366×768/1920×1080 截图；现场比对冻结 HTML 与新手动三列布局。
2. KEY Catalog 创建、编辑、排序、保存、关闭/重开、修订变化、故意污染 SHA 与硬件禁用组合拒绝。
3. 真实 KEY 动作含 K02+K04→K07→Wait→K01；预期发送三次（不包含自动 Release），每段日志、停止、超时和 UNKNOWN 均有可回读事实。
4. CAN1 DBC 编码、原始单次、受控周期与停止；CAN0/1 RX 真实观测、断连重连、日志及完整审计。
5. 项目勾选 0/部分/全部、有序执行、每用例 Cleanup/最终 Cleanup、单次 Station lease、RunId/StepFact 证据闭合；无真实设备时必须 BLOCKED。
6. 夹具在固件接口和接线核实之前保持阻断。

**当前结论：本提交只是首批无硬件风险的 UI/观测修正和 PM 定点合同申请，不宣称全量实施完成。**
