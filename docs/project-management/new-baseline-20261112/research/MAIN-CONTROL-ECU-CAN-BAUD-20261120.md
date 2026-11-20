# AutoTest｜主控板 ECU CAN 动态波特率 0x42/0x43 技术交付审查（2026-11-20）

## 0. 结论与证据等级

- **交付专业**：Main_Control HW/FW。
- **交付提交**：`Main_Control@e06a820e0d54dc7719cc75af1ecae005f05e164f`；[GitHub commit](https://github.com/ch3758134-prog/AutoTest/commit/e06a820e0d54dc7719cc75af1ecae005f05e164f)。
- **审批状态**：`SOURCE_READBACK_CONFIRMED / PROTOCOL_CONTRACT_STATIC_ACCEPTED_WITH_LIMITATIONS / KEIL_BUILD_NOT_VERIFIED / DEVICE_FIELD_NOT_VERIFIED / MAIN_BASELINE_NOT_UPDATED / R1_HOST_INTEGRATION_PENDING`。
- **审查权限**：接收和登记接口候选；没有证据可宣布固件可用或授权生产依赖。
- **源码差异基线**：从此前已知固件 HEAD `356896e10ff95447395b7903d352f6db201fbe6c` 到本次 HEAD，前进 16 commits、无 behind、修改 14 文件（10 个固件 .c/.h 与 4 份文档/README）；最新一次 commit 自身只更新 README。不能用最后单个提交文件数量替代本轮功能文件数量。

## 1. 冻结的实际外部接口和路由

| 项目 | ECU CAN | KEY CAN |
|---|---|---|
| 业务角色 | `ECU_CAN_PORT` | `KEY_CAN_PORT` |
| MCU 外设 | FlexCAN1 | FlexCAN0 |
| J1 外部接口 | A12(H) / A1(L) | B11(H) / B1(L) |
| USB CAN 帧通道编号 | 1 | 0 |
| 上电默认速率 | 500 kbit/s | 500 kbit/s |
| USB 动态调整 | `0x42/0x43`，仅 ECU | 不支持 |

上位机应使用“ECU CAN / KEY CAN”角色语义，不得凭 USB 0/1 猜芯片原理图编号。KEY_SIM 20 Key 电阻网络及 KEY CAN 500k 的行为本轮不变。

## 2. 新增 USB 指令及端到端静态核对

- `0x42 CDD_USB_CMD_SET_ECU_CAN_BAUD`：请求 1 字节 `baud_code`，成功响应空 Payload 与 STATUS=0。非法长度/枚举 0x02；广播或 TX 邮箱忙 0x05；硬件时序读回失败 0x06。
- `0x43 CDD_USB_CMD_GET_ECU_CAN_BAUD`：请求空 Payload，成功响应 `baud_code` 一字节；非法长度 0x02。**是固件保存的配置状态，不是独立总线速度测量**。
- `baud_code`：0=20k、1=50k、2=100k、3=125k、4=250k、5=500k、6=800k、7=1M（kbit/s）。
- `0x42` 只影响 ECU CAN；动态设置不写 Flash，重启回到 500k；原 0x40/0x41、0x50~0x53、IGN/夹具/LED 不改变。
- `ASW_UsbSvc.c` 路由到 `ASW_BusProxy.c`，后者检查广播运行和命令字段；`EAL_CAN.c` 维护配置值；`MCAL_CAN.c` 检查 TX MB，并调用 `FLEXCAN_SetTimingConfig` 更改 CTRL1 时序，读回指定字段。不重新初始化 CAN、过滤器或 KEY CAN。
- `MCAL_CAN.c` 中基于 48MHz 总线时钟的标称 timing 表已经静态检查；20/50/100/125/250/500/800 kbit/s、1 Mbit/s 与公式匹配。**仍须实物核实 CAN 时钟来源、采样点、容差和持续收发**，静态数学值不能当真实波特率测量。

**USB CRC16/MODBUS 例子单独计算通过（帧 CRC 从 SEQ 到 Payload，不含 SOF/CRC/EOF）**：

```text
SET 250k SEQ=10: A5 0A 00 42 01 04 1C 46 5A
SET OK   SEQ=10: A5 0A 00 42 00 00 1C 15 5A
GET      SEQ=12: A5 0C 00 43 00 32 44 5A
GET 250k SEQ=12: A5 0C 00 43 00 01 04 15 00 5A
```

来源主要是 `Main_Control/Project/source/BSW/CDD/USB/CDD_USB.h` 对应独立分支源码根 `Project/source/BSW/CDD/USB/CDD_USB.h` 及 [USB_HOST_CONTROL_INTERFACE.md](https://github.com/ch3758134-prog/AutoTest/blob/Main_Control/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)。

## 3. 与固定 main / R1 的关系和严禁的合并方式

**重要：`Main_Control` 分支当前根目录是 `Project/`、`docs/`、README，独立固件仓库布局，与 `main` 全系统平级的 `HOST/`、`Main_Control/`、`Key_Sim/` 明显不同。** GitHub `compare main...Main_Control` 显示 `ahead=60, behind=692`，如直接 merge，会涉及海量删除/新增等非本次改动。**禁止把整个 Main_Control 分支直接 merge/cherry-pick 成全系统根布局**。

现行 `main@ffb23f8c05ec4a64b82cd781db6357685d082c04` 中 `Main_Control/Project/source/BSW/CDD/USB/CDD_USB.h` 仍无新 CMD 宏。未来审查通过后应定点同步**本次 14 文件增量**到 `main/Main_Control/` 的相应位置，生成可查证的技术差异并确认未覆盖别的固件变更；`main` 此刻保持固定基线。并不因为固件发布候选存在而使 `R1` 自动获得新 USB 实物能力。

`R1` 上位机研发对应 Issue [#70](https://github.com/ch3758134-prog/AutoTest/issues/70)，需要按明确的固件能力探测和 USB 应答状态接入，不可在运行 TEST 或烧录中未经用户确认切换总线时序。

## 4. 给 R1 的准确协议交接（只提出接口要求，不越权改代码）

1. **连接/运行前能力探测**：连续 USB 读取已经开启后发 GET `0x43`；对 `ERR_CMD`、超时、旧固件版本、CRC 错误等区分 `UNSUPPORTED / UNKNOWN / COMM_ERROR`，不要把不支持的旧设备写作“当前500k已验证”。固件的 `GET_VERSION` 目前硬编码 `EPB_TEST_CTRL v0.4-dev`，不适合作新 CMD 的唯一可靠能力判据。
2. **工程设置**：用户在 DUT 工程/运行配置选择 ECU CAN 波特率（0..7），KEY CAN 固定 500k；预检确认无 ECU CAN 周期广播、无 UDS/Flash 会话与正在发送的数据后发 `0x42`，随后用 `0x43` 核对配置；真实 CAN 接收/诊断响应仍须单独验证。
3. **通信与并发**：`0x41` CAN RX 是不定时的 SEQ=0 异步事件；`0x42/0x43` 按非零 SEQ+CMD 配对，按现有 0xA5 CRC16 解析，不能把主动上报当请求应答。BUSY 返回必须清楚提示先停止 ECU 周期发送，不在 UDS/下载期间偷偷重试切速率。
4. **生命周期和失败**：断开/复位/重新枚举后查询并按工程配置重设；`ERR_HW`、无响应或 GET 与设置不一致时将连接状态标记 UNKNOWN 并阻断依赖指定码率的 TEST/FLASH，不能以先前软件缓存推定硬件已切换。固件时序寄存器可能部分变更而状态记录未更新，特别需要硬件故障路径实测。
5. **证据**：每次 R1 RunSnapshot/预检日志记录设备固件身份、请求码率、SET/GET 响应、验证结果及时间；这一配置 ACK 不是 DUT 成功断言，不能代替 DBC/UDS/FLASH 的真实判定证据。
6. **不跨权**：这项固件协议升级不授权修改 IEC/KEY_CAN、固件主控引脚、MCU CAN 路由或测试工程现有安全合同；保留固件分支/Release 与整机主线引入审核两个独立关卡。

## 5. 验证门禁及待办

| Gate | 所需证据 | 本轮状态 |
|---|---|---|
| FW-K1 Keil 编译 | 精确 HEAD、目标及工具链、0 error/warning 或实际错误、构建日志 | NOT_VERIFIED |
| FW-K2 实物基本连接 | 真实板刷写、0x42/0x43 GET/SET/GET、500→250→500、非法枚举/长度、重启复位500k | NOT_VERIFIED |
| FW-K3 CAN 各速率 | 用真实 ECU/CAN 分析仪确认各速率收发/时序、Rx/Tx 连续性，尤其 20k、800k、1M | NOT_VERIFIED |
| FW-K4 并发安全 | TX MB busy、0x50~0x53 广播中的 BUSY、广播 STOP 后切换、UDS/Flash 中严禁改速率和安全异常恢复 | NOT_VERIFIED |
| HOST-R1 接线 | 根据 R1 Issue #70 做 UI 参数、会话锁、GET/SET、日志证据与真实 RunSnapshot | PENDING |
| main 整机源码同步 | 固件与接口合同通过审核后精确同步 `Main_Control/`，回读路径与差异；禁止旧分支整棵覆盖 | NOT_APPROVED |

**最终判断**：协议命令定义与源码范围的静态交付接收；Keil/硬件/出厂语义不放行。R1 可先研究/实现可降级的 Host 协议入口，但用户真机能力要以已经烧录的新固件为准。不得据此宣布两板系统或首版自动测试全部完成。
