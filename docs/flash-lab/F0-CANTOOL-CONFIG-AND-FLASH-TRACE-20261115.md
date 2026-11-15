# AutoTest FLASH F0｜CanTool 原始研究追溯、配置矩阵与烧录状态机
日期：2026-11-15
状态：F0_STATIC_RESEARCH_WITH_SOURCE_LIMITATIONS / F1_DESIGN_INPUT / NO_ECU_VERIFICATION
研究基准：AutoTest feature/flash-lab-20261114@18108e85e80318f3b684799c836029e801067226

## 0. 证据等级和复核边界
- A DIRECT：本轮在专项分支实际 fetch_file 回读 AutoTest C# 源文件；文件路径/行号均可复核。
- B ARCHIVED：GitHub Issue #25 的七条研究评论记录了从原 CanTool.rar 源码、配置样本和 DLL 检查所得的事实。能核对评论文字，但本轮无法取得 RAR/XML/DLL 原始字节重新计算散列或复核反汇编；因此这些不是“本轮原件复验”。
- C UNVERIFIED：当前实体 ECU 的 CAN 地址、会话、Seed/Key、OEM 擦除/校验、波特率、镜像适配及刷写行为，均不能凭参考配置或历史评论证明。
- 原研究 RAR SHA256：8c477b6bf4545dc0f3959c70837533a3693df4b745e0602053846848499b7a2f（历史记录，不是本轮重新散列）。
- 既有研究：https://github.com/ch3758134-prog/AutoTest/issues/25 ；实施任务 #44；接口候选 #49；最新范围约束 docs/flash-lab/FLASH-SCOPE-CORRECTION-20261115.md。
- 本轮检索了当前 Git 树、代码检索和可见 Library 目录/语义索引；未发现可重新提取的 CanTool.rar、Configuration.xml、SafetyFile.dll 或 sa_demo.dll 原始字节。后续可取得时须对 SHA256 后再提升 B 证据等级。不得把用户过去提供过资料等同于当前可复核原件。

## 1. 来源、配置作用和可编辑策略
以下“默认”均指 Issue #25 所追查的参考 FlashConfiguration.cs 对象初始值，不等于当前目标 ECU 已验证值；所有字段在 FlashLab 必须显式展示 Source=XML / JSON / ReferenceDefault / Operator / ECUObserved、修改前后版本及 frozen effective snapshot。没有来源时为 UNKNOWN，不自动把参考值写成现役 Profile。

| 原字段/别名 | 类型与历史来源 | 参考缺省或样本值 | 实际代码消费/步骤 | 验证及 UI |
|---|---|---|---|---|
| BootloadPhyID | XML hex uint → BootReqId | 对象初始 0；样本 0x18DA50F1 | SessionManager.cs:60-170 Boot 10 03 / 10 02 / 编程链 CurrentReqId | CAN ID、29-bit 校验；需 ECU 实测 |
| BootRespond | XML hex uint → BootRespId | 初始 0；样本 0x18DAF150 | SessionManager Boot RX 匹配 | 不可与 Req 相等；仅样本 |
| APPPhyID | XML hex uint → AppReqId | 初始 0；样本 0x747 | SessionManager.cs:92-154 APP 10 03 / 11 01 | 11-bit；只读 F1 不发 APP reset |
| APPRespond | XML hex uint → AppRespId | 初始 0；样本 0x74F | APP response filter | 需与当前 DUT 匹配 |
| VersionID | XML hex uint | 初始 0；样本 0x74F | FlashConfiguration.cs:113；原主 FlashSequence 未见使用 | 不可声称已做版本复验 |
| DiagnosticsID | XML hex uint | 初始 0；样本 0x18FE150E | FlashConfiguration.cs:114；主流程未证实使用 | 单列为提示/未接线 |
| FlashStartAddr（含中文别名） | XML hex uint，包含起点 | 样本 0x10008000 | FirmwareParser 过滤；FlashSequence 31 01 FF00 地址 | OEM 映射未验证；不允许 F1 擦写 |
| FlashEndAddr（含中文别名） | XML hex uint，包含终点 | 样本 0x1006FFFF | length=End-Start+1；末端签名位置 | 必须用 64-bit 中间值计算 endExclusive |
| BaudRate（波特率） | XML 字符串，250K/500K 归一 | 对象默认 500K；样本 500K | 参考 CAN 设备初始化意图，非 UDS PDU | AutoTest 真实主控 CAN1 配置/速率读回未证实；界面不假称已设定 |
| BlockLength | XML hex uint（样本无此字段） | 对象默认 0x200；0 被重置 | FirmwareParser.cs:222-253 镜像地址对齐块，FlashSequence 每块 34→36→37→31 | 与 0x74 MaxNumberOfBlockLength、ISO-TP FC BS 三者分开显示 |
| SafetyFilePath / SeedToKeyDll_Path | XML 字符串；JSON 亦有 SafetyFilePath | XML 样本无；历史 settings.json 指 SafetyFile.dll | SecurityKeyService.LoadLibrary / GetProcAddress(ZLGKey) | 显示路径来源、文件哈希、PE/ABI；没有有效 provider 则 BLOCK |
| ForceMinInterval / ForceMinIntervalMs | XML 十进制 int 毫秒 | 默认 0；样本未载入 | UdsTransport.cs:367-446 CF 间距策略 | 真实 ECU FC STmin 与本地策略合并；避免擅自硬编码 |
| P2TimeoutMs | 对象内存 int 毫秒，原 XML Loader 未读 | 1000 | 34/36 等应答等待 | UI 可显示但不得标记 XML 已导入 |
| P2StarTimeoutMs | 对象内存 int 毫秒，XML 未读 | 5000 | NRC 78 / 扩展等待 | 实际 ECU 会话定时未实测 |
| S3TimeoutMs | 对象内存 int 毫秒，XML 未读 | 5000 | 主 FlashSequence 未见有效消费 | 标记 UNUSED_IN_REFERENCE_MAIN_FLOW |
| EraseTimeoutMs | 对象内存 int 毫秒，XML 未读 | 30000 | 31 FF00 擦除等待 | F2 目标行为未知 |
| ResetWaitMs | 对象内存 int 毫秒，XML 未读 | 2000 | 主 FlashSequence 未见有效消费 | 不等于复位后的 APP 验收 |
| FlowControlTimeoutMs | 对象内存 int 毫秒，XML 未读 | 2000 | UdsTransport 等待 FC | 既有 AutoTest ISO-TP 的 FrameTimeout 默认 1s，须明确差异 |
| MaxRetries | 对象内存 int 次，XML 未读 | 3 | FlashSequence 擦除重试 | 新实现不默认副作用重试 |
| ChecksumRetryCount | 对象内存 int 次，XML 未读 | 0 | 校验失败后重新擦除/重写 | 新实现默认禁止自动再擦除 |
| BootFeature / APPFeature | 样本 XML 扩展字段 | item=73 / 701 | Issue #25 指原 Loader 不读取 | 原样保留未知 XML 字段并显式 UNMAPPED，不赋予 UDS 意义 |
| settings.json DeviceType / ConfigPath / SafetyFilePath | 运行目录 JSON（非同一 XML） | 历史 DeviceType=Kvaser；路径为特定 Windows 工作站 | 参考程序选择 CAN 设备、配置文件及 DLL 来源 | FlashLab 使用 AutoTest WinUSB CAN1；绝不因为 JSON 是 Kvaser 就切换设备驱动 |

样本 Configuration.xml 历史 SHA256 d085f9adcb22baa4bbec4dc03272c6c172eae2a36ad74ce2d1581be20931f498；样本 Flash 包含区间 0x10008000..0x1006FFFF，长度 0x68000 (416 KiB)。这些是参考 XML 的静态数据，未经现场 ECU 身份、Memory Map、镜像校验验证，不可直接选为默认实际刷写配置。

FlashConfiguration.LoadFromXml 的参考定位：CanTool/Config/FlashConfiguration.cs:96-145；原 FlashService.cs:53-98 使用活动内存 IFlashConfiguration，而不是在 FlashAsync 内从 configPath 重新读取 XML。原 CopyFrom 未复制 FirmwarePath；所以每次操作必须生成真正的 effective-value snapshot，而非只记录文件路径。归档的 CanToolFlashProfile 若比原 Loader 多读 P2/Erase/FC，属于 AutoTest 扩展，不能冒充原版行为。

## 2. CanTool 参考调用链和各阶段判据（B：Issue #25 源码调查）
| 状态 | 原文件/调用与请求 UDS PDU | 正确证据 / 配置入口 | 失败、恢复或待证事实 |
|---|---|---|---|
| S0 载入/解析 | FlashService.cs:53-155 → FirmwareParser.cs:28-280 | 非空镜像块、内存有效配置 | 原解析遇部分非法记录静默跳过，跨 EndInclusive、重叠校验不足；新程序 fail-closed |
| S1 探测 | SessionManager.cs:73-115 Boot ID 10 03；失败转 APP ID 10 03 | 50 03 + 对应响应 ID | 10 03 本身是状态改变；不能算 F1 只读 DID；无响应不代表没有 ECU |
| S2 转 Boot 编程会话 | SessionManager.cs:126-288 APP 发 11 01，短暂等待，再 Boot ID 发 10 02 | 51 01 / 50 02，适配实际 Boot 地址 | 复位/会话切换时序 OEM 未验证，失联结果可能 UNCERTAIN |
| S3 SecurityAccess | SecurityAccess.cs:48-120 Boot ID 27 01 → Seed → Key provider → 27 02 + Key | 67 01 / 67 02 | 原 SecurityKeyService 失败静默 fallback；新系统严格禁止 |
| S4 擦除 | FlashSequence.cs:293-332 31 01 FF00 + 4B BE start + 4B BE length | 71 01 FF00（还要 OEM 例程结果） | FF00 私有 RID；重试原默认 3 次有副作用，禁止直接移植 |
| S5 Download | FlashSequence.cs:339-377 每镜像块 34 00 44 + 4B BE address + 4B BE size | 74 + lengthFormatIdentifier + maxNumberOfBlockLength | 原版错误从低半字节取长度；应按高半字节，并校验响应大小及 max>=3 |
| S6 Data | FlashSequence.cs:379-438 36 + BSC(01 起、8bit 回卷) + bytes；UdsTransport ISO-TP | 76 + 同一 BSC，逐请求累积 ACK 字节 | 传输上限为 ECU 0x74 报的完整 36 PDU 最大长度减 2，另受本机 ISO-TP 4095 限制；未回包不能认定未写 |
| S7 Exit | FlashSequence.cs:440-449 37（无参数） | 77 ... | 超时可能状态不确定；不自动重新刷 |
| S8 逐块校验 | FlashSequence.cs:451-485 31 01 FF01 + address4 + length4 | 71 01 FF01 + OEM 响应的 2 字节校验结果（按该样本的假设） | 源码若响应不足 5 字节会跳过比较而误标成功，必须拒绝；校验失败不能自动全区重擦 |
| S9 Reset | FlashSequence.cs:232-249 Boot ID 11 01 | 51 01 | 原代码的 complete 不等于 APP 能启动 |
| S10 APP 复验 | 原 CanTool 主流程缺失；AutoTest F2 拟新增 | 确认重新出现的 APP ID、ECU 身份/软件版本 DID 与固件版本匹配 | DID、时序、实际签名和恢复方案均 UNKNOWN |

注：以上参考 UDS 字节及 RID 只作为代码证据，不构成对当前 ECU 的应用参数。原版擦除全区后按镜像对齐块循环 34/36/37/31；不能凭标准 UDS 服务号推定任意 ECU 必须如此操作。
参照调用链：FlashService → SessionManager → SecurityAccess(SecurityKeyService) → FlashSequence(EraseFlashAsync, Download/Transfer/Exit/Verify) → UdsTransport → 原 CAN 适配器。与 AutoTest 的唯一现有后端 API 对应表见 F0-AUTOTEST-CODE-REUSE-AND-EXE-DESIGN-20261115.md。

## 3. 解析、校验、块长的特殊风险
- 参考 FirmwareParser.cs:28-39 实际仅 HEX/H86、S19/SX/MOT；不支持 BIN。AutoTest 当前 FirmwareImageParser 支持 BIN，必须由操作员提供起始地址。
- 参考 parser 使用 BlockLength 做对齐，空洞填 0xFF，并在 FlashEndAddr 附近写入硬编码 12 34 56 78（原研究 FirmwareParser.cs:222-281）。此签名是否适用于目标 Bootloader 未验证；绝不自动注入。
- 参考主机 checksum：块内所有字节求和，取 16-bit 按位反码；FF01 响应尾部两字节由 ECU 给出、主机比较。其算法、字节顺序、参与数据范围在目标 ECU 上未证实。
- 明确三个独立数：镜像对齐 BlockLength=默认0x200（参考值）；ECU 0x74 最大完整 TransferData UDS 请求 PDU 长度；ISO-TP FlowControl BlockSize（CAN 连续帧窗口）。
- 0x74 maxNumberOfBlockLength 的高四位为长度字段字节数（原代码取低四位有缺陷）；禁止把 ECU 的 0x74 与固定 512 字节混同。还需检查 ALFID、DFI、ECU 接受的 34 格式。
- 真实 USB ACK、CAN RX、0x76、0x77、例程应答、最终 APP 可运行，是六类不同证据，不允许合并成一个“成功”勾选。

## 4. Seed/Key DLL 实证分级（仅用于合法受授权设备研究）
| 文件 | 历史检查所得 | 当前可证明 | 当前不可证明 |
|---|---|---|---|
| SafetyFile.dll | SHA256 ee2c4a2791b88dce19db29f308752c8f79bc0654463d43e6880f65c6fdea3833；4096B PE32/CLR .NET Framework v4.8、ClassLibrary1/SafetyVerificater，原生导出目录为空 | 若历史文件确实相同，其原生 GetProcAddress(ZLGKey) 不会成功 | 托管 API/算法内容与目标 ECU 匹配，尚未核实 |
| sa_demo.dll | SHA256 a8d22a0f82d6f68910fbbf2d81afad35d19c804953ad33c80f5143da8c98a703；52,736B x64 原生 DLL，导出 ZLGKey | 历史静态 ABI 证据，函数六参数，成功码0、keyLen=4，回退 LFSR 同源 | 目标 ECU 认可该算法、进程加载行为、独立合法性 |
| CanTool/ReferencePath/KEY.dll | SHA256 b7c476d325012452a9988c545b64267944b7fae96e1b2dae8f4ca8de885ac8b6；x86 .NET Framework4 托管，含 KeyCreate/seedTokey 类方法名 | 不能原生导出 ZLGKey | 不能认定目标 ECU 的正式 key provider |
| zlgcan.dll | 原生 CAN 通信库，ZCAN_* 导出而非 ZLGKey | CAN 驱动，不是该 ABI 的 Seed/Key 库 | 不可改文件名后当算法库 |

原 CanTool SecurityKeyService.cs:16-33,77-103 使用 LoadLibrary + GetProcAddress("ZLGKey")、Cdecl、签名 int ZLGKey(byte* seed, ushort seedSize, uint securityLevel, char* variant, byte* output, ushort* outputSize)；参考调用 level=0、variant=null、4 字节输出 buffer，原代码检查返回0但未严格检查输出长度。运行时 settings.json 指向 SafetyFile.dll，与原生 ZLGKey 调用方式存在静态不匹配。必须先核实实际运行二进制版本与 DLL，才能推导当年发生了什么。不得以 sa_demo 或内置 LFSR 代替真实认证 provider；日志只记录 DLL 来源哈希、加载结果、导出结果、长度和错误，不泄露 Seed/Key 明文。

## 5. F2 必须补足的 ECU 专用证据
目标 ECU 实际身份/硬件版本/Bootloader 版本、APP/Boot ID/11或29-bit/比特率、读 DID/会话策略、真实固件文件+SHA、厂商烧录规程、擦除例程参数和范围、0x34/0x74 格式、0x31 校验返回语义/签名政策、适用算法库和 ABI、供电及恢复工艺、断连后复核流程、授权范围。这些未完成前不得向物理 ECU 发 27/31/34/36/37/复位。

## 6. 可审查结论
Issue #25 的 CanTool 静态流程研究可作为设计输入，但当前不能宣称已再次复验原件，更不能宣称指定 ECU 实机流程已完整查清。F0 已完成仓库可访问部分的静态差异复核，原始 RAR/XML/DLL 与实体 ECU 证据是待关闭限制，不应以编造值伪造 F0 PASS。