# AutoTest R1｜扫码配置 × 原生烧录配置｜产品合同与实测前验收

日期：2026-11-23（UTC）  
分支：仅 `R1`；`main` 不写入。  
状态：**源码候选 / 未执行 Windows Build / 未使用真实扫码枪 / 未使用 ECU 烧录 / 不满足生产现场准出**。

## 1. 设计结论（替代旧配方方案）

扫码只是获取 UUT 身份，不能作为动作命令。正式自动检测的 TEST 范围唯一来自 ECU 工程的有序 TEST 项目；生产每次 Inspection（包括复检）须有独立的前置原生 FlashAttempt、APP 后验，再进入 TEST/RECORD。开发调试不强制扫码，专业工具独立运行。

职责分离：

- **工位传输设置**：Windows USB 虚拟串口 COMx、波特率；固定 8N1、无流控的第一轮候选；保存在 `%LocalAppData%/AutoTest/scanner-com.json`。与主控 USB WinUSB 不混用。
- **工位身份解释设置**：明确选择一种条码格式。保存在 `%LocalAppData%/AutoTest/scanner-identity-policy.json`，并保留 .bak；配置坏/不存在则拒绝身份匹配，不猜测。
- **工位安装索引**：一个正式型号映射一个明确登记的工程 ID 和 manifest 路径，查找时重新核对型号与项目身份，不依赖显示名称。
- **ECU 工程资源**：EcuModelCode、DBC、波形、TEST 项目、工程内固件与原生 Flash Profile，受 SHA/修订保护。工具可复用，但不受生产扫码限制。
- **生产状态**：ScanEvent 与 Inspection、FlashAttempt、CaseRun、StepOccurrence 是不同身份；不能按扫码次数算新增 DUT。

## 2. R1 首轮明确支持的编码

| 格式 | 示例 | 读取字段 | 边界 |
|---|---|---|---|
| `ModelSerial` | `HYDEPB-000000123456` | model、serial | 可选单字符分隔符 -、|、:、/；严格解析，保留前导零 |
| `SerialOnly` | `000000123456` | serial | 必须显式指定并登记唯一固定型号工程；无法从条码得出型号 |
| `UT1` | `UT1-HYDEPB-H02-000000123456-<CRC16>` | model、hardware、serial | 只接受正确的 UT1 CRC；是项目候选自定义编码，不强制供应商条码采用 |

共同约束：序列号是字符串，不转整数；长度在配置范围内（初值 8～20，可设 1～64），不做隐式去零、大小写转换、猜测截取；非法帧/格式/CRC 明确拒绝。没有配置时允许读取原始 COM 样本，但不能匹配型号、切工程或做生产准备。

选择原则：
1. 供应商已有量产标签：先用真枪读一条原始字节，按其格式明确配置，不为适配软件强迫重贴码。
2. 自己控制标签且多型号混线：优先 `ModelSerial`；需要硬件版本和 CRC 时才考虑 UT1。
3. 工位固定单型号、标签只有 SN：选择 `SerialOnly` 且先登记该型号工程，绝不声称纯 SN 包含型号。
4. GS1 DataMatrix/Code128 的 AI 字段如 (01)/(21) 需要另行实现专用解码，不把普通连字符格式称为 GS1。

## 3. UI 操作流程（本轮已接源码，待 Windows 验证）

进入 **开发与调试 → 工程配置 → 扫码与烧录**：

### A. 扫码枪
1. 打开工程，设置正式 EcuModelCode，点击“保存工程型号”“登记到本工位”；重复型号与另一个工程冲突时拒绝自动覆盖。
2. 连接独立 COM；确认原始条码及原始 HEX。设备断开需重新连接，禁止断线后缓存的旧扫描触发任务。
3. 选择条码格式、最小/最大长度、分隔符或固定型号；将真实扫描自动填入的样本“试解析”，检查 model/serial/hardware。
4. 先断开串口，再“保存规则”。保存后旧解析身份失效，重新连接并扫码，核对工位索引、当前工程一致性。
5. 执行“生产配置预检”只检查文件、工程、原生 Job 与 TEST 资源闭包，不发送 CAN，不算合格、不能启动夹具。

### B. 原生烧录
1. 打开“原生烧录配置”。仅在 HYDEPB 工程中可手动填入“已核对 HydEPB 参数”作为编辑草稿的起点，不会自动发布。
2. 校对 APP 请求/应答 0x747/0x74F、Boot 0x18DA50F1/0x18DAF150、CAN 500k、地址 0x10008000..0x1006FFFF、块长 512；只允许当前明确验证的方法实体执行。
3. 导入真实 HEX/S19/BIN（有必要时配置 BIN 基址）和经审查的安全算法 DLL；复制到工程受控目录、固定 SHA。
4. 选择“校验并预览真实 Job”，确认块数、目标字节数和指纹；再显式“发布为当前生产配置”。只自动保存编辑草稿不构成生产发布。
5. 独立烧录工具可在真实硬件上检验；FLASH 正式生产前置执行尚未接通，不因发布配置就宣称已经烧录成功。

## 4. 非法或重复输入合同

- 相同 SN 重复扫码：仍是输入事件，但 UI 提示重复身份；不创建新的 DUT、不能替代复检 Inspection 和新的实体 START。
- 传输不完整/超长、端口断开、跨工程修改、匹配工程丢失：明确拒绝，历史解析结果失效。
- 纯 SN 与未知型号：不得扫描猜测；由工程师预配置固定型号。
- 运行中扫码：在入口拒绝，不把新码排队到运行结束后自动执行。
- 已发布 Flash 配置变更：必须显式重新发布；任何旧指纹不能当作仍有效的原生工艺。
- 扫码、COM ACK、主控 USB ACK、UDS Boot ACK 都不是机械到位/软件版号验证/最终 PASS 的证据。

## 5. 验收路径与缺口

软件侧需依序验证：规则保存/重开、真实 COM CR/LF/CRLF、扫码型号与唯一 SN、前导零、非法 CRC、同码重复、跨工程切换、工位登记冲突、Profile 备份/发布/重开、真实 Job 冻结和旧发布拒绝；然后在 Windows 做 Debug/Release 构建及 WPF UI/退出生命周期。以上工作**尚未执行**，不能当作通过。

实体侧需再验证：扫码 → 唯一型号工程 → 真实夹具（USB ACK 不代替机械到位）→ 新实体 START → 原生 Flash→复位/APP/版号后验→原项目完整 TEST→逐步骤证据→Inspection/报告，故障恢复和复检独立计次。当前这一正式联动尚未完成，**Production NO-GO**。

## 6. 外部成熟产品参考

- NI TestStand Process Models: https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
- NI Guide to Maintainable TestStand Sequences: https://www.ni.com/en/support/documentation/supplemental/06/a-guide-to-developing-effective--maintainable-teststand-sequence.html
- NI TestStand Model Customization: https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html
- GS1 AI identifiers: https://ref.gs1.org/ai/
- GS1 General Specifications: https://ref.gs1.org/standards/genspecs/

这些参考提供设计方法，不代表 AutoTest 已经具备 NI TestStand 或 GS1 完整兼容能力。
