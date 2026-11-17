# AutoTest｜G3 补充变更：主控板 USB 与双夹具接口

- 日期：2026-11-16（PT）
- 管理状态：**目标产品决定已获负责人批准；现有固件整改/编译/实物验证尚未完成**。
- 本文件为 Project_Management 单分支的正式受控变更记录，**不是主控板现有固件的已实现接口声明**。
- 关联：G1 REQ-13/19/21/23/24/26/29/31/43、G2 A-02/A-05、G3-DEVICE、G3-03/04/05，关联公共流程与直接 KEY 变更。
- 固件事实来源：`Main_Control@5d2ad5b7f3ae54c6b3e08e363229a25349f4f8be`，接口文档 `docs/interfaces/USB_HOST_CONTROL_INTERFACE.md`，该提交**仅增加文档**。接口文件 Blob `67a8fcb0da5eaf4cae1af1a2f7caaeb5e0979007`。

## 1. 已批准的物理与产品边界

| 对象 | MCU 管脚 | 物理路径/外接 | 业务含义 |
|---|---|---|---|
| IGN | PTC10 | J1 C6 | 0=OFF，1=ON |
| 夹具1 / Air_ALVE1 | PTB1 | J1 B5 | 0=释放，1=压紧；独立输出 |
| 夹具2 / Air_ALVE2 | PTC2 | J1 B4 | 0=释放，1=压紧；独立输出 |
| RL_DRV | PTC0 | PCB 内部功率电路 | **非 USB/HOST 业务对象** |

主控硬件 TestCtr V1.5，MCU MK20DN512VLL10；USB 至 Windows；业务 CAN0 至 KEY_SIM、CAN1 至 DUT；现有固件两路均初始化 500 kbps。两夹具**不互锁**：改变任何一路不得附带改变另一路。夹具压紧/释放是气路电磁阀的控制**请求与输出电平语义**，不是已测得机械到位。

**已取消的上位机业务能力**：ECU 供电开关、按键板供电开关、原 SOL 电磁阀接口；不创建 RL_DRV 操作。DUT 电源仍由外部独立设备管理。内部 PCB 功率控制仍可能依赖 RL_DRV，取消业务命令**不等于可直接删除/关闭内部功率电路**。

## 2. USB 实际现状与兼容输入

| 项 | 当前固件核查值 |
|---|---|
| WinUSB | VID `1183`、PID `5850`、接口 GUID `4da6d065-1e3e-47e6-8cc3-c6977351defb` |
| USB | 接口0，Bulk OUT `0x03`、IN `0x82`，端点最大包 64B |
| 应用协议 | 请求 SOF `0xA5` / EOF `0x5A`、16位小端 SEQ、CMD/LEN/PAYLOAD、CRC16/MODBUS（SEQ 至 PAYLOAD） |
| 主动 CAN 上报 | `0x41`，SEQ=0；不与普通请求响应配对 |
| PING / 版本 | `0xF0` 返回 PONG；`0xF1` 当前固定文本 `EPB_TEST_CTRL v0.3` |
| CAN | `0x40` CAN_SEND，15B 数据，0=CAN0 KEY_SIM，1=CAN1 DUT；`0x50~0x53` 为旧 CAN1 周期广播实现 |
| IGN | `0x12` SET_IGN(1字节 0/1)，`0x13` GET_IGN（GPIO 级读回） |

读写需增量解帧、支持拆包/合包、CRC/超时/异常、异步 0x41 与请求交错；USB 枚举或 PING 成功不证明 ECU、CAN、夹具已可用。当前 CAN1 周期广播是否保留为正式目标能力仍应另行核对，不在此变更中擅自删除。

## 3. 双夹具新接口：目标合同与旧版阻断

当前固件 `0x18 SET_AIR_ALVE` **仅收1字节**：0=两路关、1=开1关2、2=关1开2；其动作与 RL_DRV 耦合。当前 `0x19 GET_AIR_ALVE` 返回 bit0=Air1 GPIO、bit1=Air2 GPIO。**旧 0x18 无法满足任意独立 00/01/10/11 状态，禁止作为目标夹具接口复用。**

供固件与 HOST 实现对齐的新协议字段（当前接口调查文档提出，**现役固件尚不支持**）：

| CMD | 新目标请求 | 目标响应 | 语义 |
|---|---|---|---|
| `0x18 SET_CLAMP` | 2字节 `[clamp, state]`；clamp=1或2，state=0或1 | Status=0，空 Payload | 只改变指定夹具，另一夹具保持原有命令状态 |
| `0x19 GET_CLAMPS` | 空 | 1字节 bit0/bit1 | 仅主控 GPIO 输出级状态，**不是机械到位确认** |

注意：这份消息层布局是目前双方**目标对齐方案**，不是已经烧录在板上的事实。若固件专业经审查更改编码，须形成新的同版本接口修订，通知 HOST，不能两边悄悄采用不同 Payload。

### 强制能力判定

1. HOST 不得只凭 VID/PID、PING 或现有固定版本字符串 `EPB_TEST_CTRL v0.3` 推断新 `SET_CLAMP` 已实现。新的固件应提供可核验的协议/能力版本标识及精确固件身份（方式由固件/DEVICE 对齐后形成明确接口修订）。
2. 在确认新接口已存在、适配与联机验证前，HOST 可以实现模型、编辑器和预检，但**禁止发送旧0x18或假定生效的新两字节0x18进行夹具动作**；界面显示“固件未支持/未验证”，不要回退到互斥旧协议。
3. 新固件对不合法 clamp/state/长度须明确报错，不得默默解释为旧行为；HOST 对异常和超时显示失败/UNKNOWN，禁止用 GPIO ACK 推算夹具真实到位。
4. 主控固件与 HOST 应在更新后完成协议版本兼容测试，覆盖 00、01、10、11 及分别修改一路而保持另一路。

## 4. 废弃接口、GPIO 初始化、断连

| 当前旧 USB 命令 | 当前代码事实 | 新目标处置 |
|---|---|---|
| `0x10/0x11` ECU 供电 | `0x10` 同时影响 ECU_PWR_DRV / RL_DRV 且可能清两路 Air | HOST 禁止调用；固件审查内部依赖后清除旧业务入口 |
| `0x14/0x15` KEY_PWR | 旧 KEY_PWR_DRV 路径 | HOST 禁止调用；固件审查后撤销业务入口 |
| `0x16/0x17` SOL | 当前返回 ERR_HW | HOST 禁止调用；撤销旧 SOL 业务入口 |
| `0x1F` GET_ALL_PWR | 仍返回已取消供电位 | HOST 不作为目标业务使用 |

源码现状：Air1/Air2 初始化 LOW；RL_DRV、IGN、旧供电脚初始化 HIGH；USB 枚举状态丢失时可能执行 AirOff。这些是**调查事实，不是正式安全默认值**。固件专业须形成上电、断开、重连、固件复位时夹具和 IGN 的安全策略及证据，并与上位机保持一致；未经批准不得擅自认定“断开自动释放”或“断开保持”。

USB GET 类命令反映 GPIO 级信息，机械实际位置若无独立传感器/外部测量，保持 UNKNOWN；电平输出/主控 ACK 与夹具动作成功分层记录。

## 5. 主控固件专业应做

- 对照具体 MCU、PCB/J1、原理图与现有源码，核实 J1 B5/B4/C6 的实际有效电平。
- 实现两夹具独立控制、只影响指定 GPIO；解除旧互斥逻辑与错误 RL_DRV 业务耦合，**不破坏内部功率工作条件**。
- 审查并停用已取消 USB 业务命令；不要未经审查删除内部驱动。
- 确定启动初态、断连行为、复连与复位安全处理；提供与 HOST 可核验的协议/能力版本。
- 交付源码精确 SHA、Keil Build 日志、固件镜像身份、协议更新文档、现场 GPIO/J1 和夹具实测；没有以上证据时标记 `NOT_FIELD_VERIFIED`。

## 6. HOST/DEVICE/G4 合同及阶段验收

- **DEVICE**：协议 Codec、状态/能力识别、USB 连接错误分阶段定位、夹具独立命令/查询、会话及授权/UNKNOWN；禁止绕过 PhysicalCommandFence。
- **TEST**：两个独立夹具目标和步骤；能用于用例与公共流程，不另生生产身份；不将旧 SOL 语义作为执行入口。
- **WPF/UI**：手动台架两个独立夹具操作，步骤 Inspector 明确“夹具1/夹具2”“压紧/释放”；禁用原因、请求与 ACK/未知清楚可见；旧供电/SOL 不出现。
- **INTEGRATION**：连接现有真实设备会话与测试服务，检查固件兼容；禁止另造 USB 驱动与运行器；不因 UI 变更影响 CAN/DBC/USB 原有真实通路。
- **现场验收分层**：源码实现 ≠ Windows Build ≠ USB/PING ≠ GPIO/J1 实测 ≠ 夹具机械到位。未通过对应层级不得写成 PASS。

## 7. 来源

- 主控固件完整接口文档：`https://github.com/ch3758134-prog/AutoTest/blob/Main_Control/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md`
- 主控原理图和硬件映射：`main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/`（迁移至管理分支前保留引用）
- 固件分支：`Main_Control@5d2ad5b7f3ae54c6b3e08e363229a25349f4f8be`
- G1/G2/G3 已批准原文保留，不回写篡改；本变更按补充文件处理。
