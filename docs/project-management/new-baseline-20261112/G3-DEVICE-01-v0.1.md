# AutoTest｜G3-DEVICE-01 真实工位设备动作与状态证据详细设计（候审）

- 版本：v0.1；日期：2026-11-14；状态：**G3 候审，不构成 G4 编码、实体操作或固件修改授权**。
- 正式上游：`main@ec3f04f1b6331dd22266d3bd1b1fac71aa451418` G2-GATE-REVIEW v0.1，尤其 A-01/A-05、G2-L03/L07/L08；G1 v0.3。
- HOST 静态调查：`local@a9a26ebfba1efcf032ff8ec8c435336a89469647`；主控固件 `Main_Control@c830dbcd56b436688437b446d222473a88b85098`；KEY_SIM 固件 `Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a`。均为仓库源码证据，**不代表现场正在运行这些固件 SHA，也不证明实体接线/效果**。
- UI 分工：IDE 式视觉/交互由独立 UI 专项对话研究。本文件只规定设备功能、动作语义、接口、状态与实测证据，供 UI 对话对接，不冻结窗口样式。

## 1. 决定解决什么

A2 多测试包执行与 F1 只读 UDS 都依赖唯一真实工位。必须把“上位机请求某动作”“主控收到/执行命令”“外部电气输出”“DUT/夹具真实响应”区分开来。现在的问题不是抽象缺一个设备层，而是主控固件的命令能力与 HOST 的 TEST/USB 授权和显示状态之间尚未全部对齐；多包切换及失联时的物理状态不能靠软件猜测。

一个 Windows PC / 一个真实主控 / CAN1=DUT / CAN0=KEY_SIM / 一台 DUT；不引入第二模拟工位、第二 USB 会话或固件假回包。不为追求通用性增加现场无法证明存在的闭环传感器。

## 2. 三方真实能力核对表（只据源码，现场须复核）

| 对象 | 主控/按键板固件行为 | 当前 HOST/TEST 行为 | 设计与验证缺口 |
| --- | --- | --- | --- |
| 主控 USB | 主控 `CDD_USB.h` 定义 0xA5 序列化、状态码、PING、CAN、SET/GET | `HardwareConnectionService.ConnectAsync` 发现主控并 Ping；创建共用 `CanFrameHub` 接收 | 确认现场运行固件、命令版本、断连 epoch；不存在用 ACK 证明 DUT 状态 |
| CAN1 → DUT | `UsbSvc` 将 `0x40 CAN_SEND` 交给 BusProxy；CAN RX 经 `0x41` 上报 | `SendTestCanAsync`、DBC/RawCan 是受限真实 TX；`LiveDbcSignalService` 从同一 RX 解码 | 单包与跨包授权范围、设备独占、CAN/UDS TX 互斥及丢帧证据 |
| CAN0 → KEY_SIM | KEY_SIM `KeySim.c` 接收经典 CAN `0x730`、8 字节；`0x01 + 20-bit mask`，`0x80` 清除；其开关网络形成 5 路模拟电压 | `KeypadCommandCodec` 与 `ConfiguredStationActionPort` 可编码/发送 Mask、Hold/Pulse/Release | mask 写入 ACK 是主控转发事实，不等于 KEY_SIM 已闭合、5 路电压达到目标或 DUT 识别；现场行为单独证实 |
| IGN | 主控 `SET_IGN=0x12`，1=ON/0=OFF；`GET_IGN=0x13` | `SendTestIgnAsync` 有实际授权写路径；旧 `StationControlService.SetIgnAsync` 是禁用占位 | 只认可实际用到的路径，不能被旧占位误判为完全不可用；读取的是 GPIO 不是外部电压 |
| 夹具电磁阀 SOL | 固件 `SET_SOL=0x16`，0=升起、1=下落/压紧；`GET_SOL=0x17` 返回 GPIO 推算动作值 | HOST `StationControlService.OperateFixtureAsync` 仍拒绝，`TestTargetKind` 尚无 SOL，物理 Fence 未开放对应写 | 新增受控共享动作、映射与状态观测须做设计审查；GET_SOL 不是机械到位传感器 |
| ECU 主电源 | 固件 `SET_ECU_PWR=0x10`、`GET_ECU_PWR=0x11`；1 同时驱动继电器与 ECU_PWR GPIO | 当前正式 TEST 未提供对应实体操作通道 | **固件源码支持≠现场电源链路已确认≠自动允许 UI 控制**；核对实装继电器、外部供电与 G1 实际范围后决定是否纳入下个版本 |
| KEY 供电 | 固件 `SET_KEYPWR=0x14`、`GET_KEYPWR=0x15` | 当前正式 TEST 未提供 | 不是强制补齐项；先核对现场接线与测试目的，避免在缺物理证据时扩大动作 |

关键源文件：`Project/source/ASW/PwrCtrl/ASW_PwrCtrl.c`、`Project/source/BSW/CDD/USB/CDD_USB.h`、`WS/EPB_EOL/Sources/APP/KeySim.c`；HOST `HardwareConnectionService.cs`、`ConfiguredStationActionPort.cs`、`PhysicalCommandFence.cs`、`MainControllerCodec.cs`、`StationControlService.cs`。

## 3. 设备动作合同（建议，非既有批准 API）

### 3.1 统一的动作请求和返回事实

动作请求至少包含 `StationSessionGeneration`、`ProjectRunId/PackageAttemptId`（手动操作则关联对应操作会话）、`ActionId`、动作种类、精确目标、预期状态、发起者与授权指纹；以一套工位独占服务串行接收可产生物理影响的操作。

动作结果分层：

1. `Requested`：HOST 形成并审查输入；没有物理执行证明。
2. `USB Accepted/Rejected/Timeout`：主控响应或通信结果。
3. `GPIO Readback`：只有在命令、设备身份与实际固件匹配且 GET 有效时记录为**GPIO 回读事实**。
4. `Electrical/Mechanical Verified`：仅在现场确实有独立可验证证据时允许填写；没有传感器不能虚构。
5. `Unknown`：失联、超时、回读不一致、执行/清理不确定时；禁止由 UI 假定安全状态。

不得将上述阶段混为一个布尔 `Success`。已有 `TestOperationResult(Acknowledged,StateKnown,Code,Diagnostic,At,Evidence)` 可作兼容边界；若增加阶段事实，保留原 Test 语义与可追溯的 Raw trace，不另起第二套测试引擎。

### 3.2 所有权与并发

- USB 连接由统一真实会话所有者管理；CAN 监视可共享接收事实，但 A2 TEST 与 F1 Flash Lab 不得同时获得冲突 TX 权限。
- 项目级 Run 固定 station epoch、DUT、映射资源 Revision/SHA、允许 CAN 帧集合、允许 IGN/SOL 操作、有效期与上限。单包 Cleanup 不能隐式撤销整次项目授权。
- `PhysicalCommandFence` 当前仅对单次 CAN、特定 TEST CAN/IGN 放行；增加 SOL / 只读 UDS 必须在物理 Fence 实际承认，并可追溯、限额、可撤销。**不能只给 UI 按钮加 `IsEnabled` 以代替物理门禁**。
- 连接断开或 session generation 改变，撤销旧授权、让进行中运行停止新动作。重新连接不自动恢复保持按键、IGN、夹具或烧录。

### 3.3 动作边界与异常处理

- `KEY_SIM`：写 mask → 主控发送事实 → 可选 DUT CAN/测量事实；Pulse 时延与 Release 失败记录 Unknown；跨包 Hold 是否保持由明确步骤/清理策略决定；不能多种 RawKey/功能键无规则覆盖。
- `IGN`：必须明确 On/Off 的目标、写入事实和可能的 GPIO 回读；若 DUT 无响应不意味着 IGN 失败。
- `SOL`：严格使用固件 `0=升起/1=下落压紧`；禁止把 `off` 一律映射为“安全松夹”；未验证实际机械运动时只说“请求已发送 / GPIO 读回”，不声称到位。是否自动释放由现场工艺与用户决定。
- `ECU_PWR`：由于现场 ECU 与主控当前供电关系尚未再验证，本次只保留固件能力事实与未来可选扩展，不把 DUT 主供电自动化作为 G3-DEVICE-01 的默认必做项。
- 对需要清理的动作，在 Cancel、超时和 Disconnect 中记录已发送、已 ACK、未证实的清理步骤；失败后阻断后续实体输出，不凭界面复位状态。

## 4. 后端到 UI 的最小交接界面

UI 专项应接收以下**行为数据**而非设备私有命令：设备 `Disconnected/Connecting/Connected/Error`、当前主控身份和 epoch、CAN1/CAN0 事实、每条操作的 Pending/Ack/Rejected/Timeout/Unknown、当前保持的 KEY 掩码（标注来自 HOST 记录、未必实物已验证）、IGN 与 SOL 最近请求及可用回读、错误和原始日志、人工恢复需要做的动作。UI 不直接构造 USB 帧或访问 `WinUsbByteLink`。

具体显示样式、操作控件和交互视觉由 UI 专项确定；设备层提供可区分的客观状态与禁用原因，让 UI 可以清楚反馈。手动台架是否先纳入同一 IDE 侧栏或独立窗口，不在本后端文件冻结。

## 5. 下一阶段 G4 工作包候选（需要 G3 设计批准及独立 G4 授权）

| 顺序 | 允许调整的责任范围 | 核验要求 |
|---|---|---|
| D-00 | 固件/现场身份核对、静态命令对照、无代码改动 | 现场主控实际版本、供电、CAN0/1、能否读回，UNKNOWN 保留 |
| D-01 | `Devices.Usb` 与共享工位服务中建立可枚举受限动作及状态事实；复用现有 session/fence | 本地编译与合同测试；不输出未知实体动作 |
| D-02 | SOL 的独立受控命令、GET 读回与数据映射；A2/手动工具复用 | 用户现场许可，0=升起/1=压紧映射核对，机械状态不能凭 ACK 认定 |
| D-03 | KEY/IGN 的跨包作用域和失联恢复接入 A2 | 实际 DUT 操作中输入、保持、取消、清理时序可追溯 |
| D-04 | F1 只读 UDS 的受限 CAN1 TX 独占适配 | 真实 USB → CAN1 → ECU 对指定 DID 的响应/超时；不得开放写 Flash |

`D-02` 的现场试验需要负责人确认夹具下方无人员、机械安全条件与目标 DUT/工位；不能由于本设计批准自动执行压紧。是否需 ECU_PWR/KEYPWR 另行按现场证据修订范围。

## 6. 首批实测矩阵（全部未执行）

1. D-T01：识别实际主控硬件/固件版本，读取 PING/GET_VERSION；断连后状态及日志正确。
2. D-T02：CAN1 真实观察 DUT 报文；断开/丢帧与复连不沿用旧观测值。
3. D-T03：CAN0 `0x730` 20 Key Mask/Hold/Release；校验主控 ACK 与 DUT 实际响应分开显示。
4. D-T04：IGN 写入、可用 GPIO 回读、实际 DUT 响应分别留存。
5. D-T05：SOL 0/1 实体试验仅在明确现场许可后执行；GET_SOL 与机械现象分别记录。
6. D-T06：运行中停止、断连、重连及跨包切换；Unknown 不被自动清零。
7. D-T07：F1 与 HOST 争抢设备时第二个写会话明确拒绝；设备释放后才允许下一会话。
8. D-T08：失败操作的原始 USB seq/CMD/STATUS、CAN TX、CAN RX、Run/Attempt ID 可关联。

## 7. 审批请求与保留限制

请求批准：三方命令能力矩阵、设备会话/授权/状态分层、D-00～D-04 的受控任务拆分和实测证明方式。**不申请**批准立即编码、实体压紧、电源操作、烧录、设备固件修改、或关闭 G2-L03/L08。固件源码与实机差异一旦查出，先将真实差异提交项目管理审查，再按受影响范围更新方案。
