# AutoTest FLASH｜F1 CanTool 原界面复刻要求（2026-11-15）

**状态：USER_UI_DIRECTION_CONFIRMED / F1_CONDITIONAL_ACCEPT / SOURCE_IMPLEMENTATION_NOT_AUTHORIZED**  
**唯一目标分支：** feature/flash-lab-20261114  
**对应：** G4-F1-IMPLEMENTATION-AUTHORIZATION-CANDIDATE-20261115.md；技术审查 G4-F1 · CONDITIONAL_ACCEPT。  
**优先级：** 本文覆盖旧 F0 独立 EXE 设计中所有自行提出的视觉布局（特别是“三栏布局”等），但不改变 F0 已核实的技术接口和安全边界。

## 一、用户明确的 UI 决定
> UI 暂时直接照抄 CanTool。

此要求意味着**以用户现有 CanTool 的原始真实窗口为首版 FlashLab 的 UI 唯一参照**，尽可能逐项对应参考窗口实际可见的：
- 窗口层级、总体布局、分组/Tab、按钮及其位置；
- 配置字段的呈现顺序、名称和编辑路径；
- 设备连接/配置加载/镜像选择/进度/日志的操作顺序；
- 表格列、状态栏、日志区、错误提示、具体控件禁用与恢复逻辑；
- 窗口尺寸、功能区域占比、滚动、输入反馈等操作细节。

不再依据“现代化 UI”“IDE 风格”“通用 UDS 工作台”自行重设计，也不得用原 F0 提出的三栏结构代替 CanTool。复刻是**UI/操作行为层**；底层通信绝不移植第二套 Kvaser/ZLG CAN、UDS、ISO-TP 或 USB 驱动，不将 CanTool 演示算法或缺陷移到正式服务。

## 二、参考原件必须可追溯，不能猜测 CanTool 长什么样
本轮可访问的 GitHub 当前分支与历史 Issue #25/#44 中，已经有 CanTool 的烧录配置/服务层研究，但**未取得可在本轮检验的 CanTool 原始窗口截图、WinForms Designer.cs/WPF XAML、程序可执行窗口或 UI 控件树**。已查验 SAEF CANTOOL_REFERENCE_ACTIONS.md，它列的是源代码业务参考，并非 UI 窗口截图或控件清单。

在取得原件前，不能虚构具体“CanTool 左侧/中间/右侧”“某按钮在某位置”，不能声称 UI 1:1 复刻已获得原始参考证据。实施时优先从合法可访问的原 CanTool 包/已提供运行程序/界面截图回读：主窗体、配置编辑窗、镜像选择、日志、进度及异常提示等，建立逐窗控件映射和 UI 证据引用（文件名/哈希/截图编号）。如原件无法访问，仅能标记 UI_REFERENCE_MISSING，不得用臆造样式承诺像素级一致。

## 三、UI 功能/权限区分
| CanTool 原操作（待原 UI 实证定位） | F1 FlashLab 处理 | F2 后续边界 |
|---|---|---|
| 设备选择/连接 | 原界面交互习惯保留，设备实际仅 WinUSB 主控和 DUT CAN1；CAN0 不作为烧录通道 | 不开第二套驱动 |
| 选择/编辑配置 | 保留 CanTool 原用户语义，配置值来源 XML/JSON/手工/未知可见；保存/重开；真实 ECU 参数 UNKNOWN 不默认套样本 | F2 冻结真实 ECU profile |
| 固件选择/信息 | 沿用原操作习惯，复用 FirmwareImageParser，HEX/S19/BIN、BIN 必填基址、地址段及 SHA；严格拒绝损坏镜像 | F2 仅用经身份/哈希/范围核实的镜像 |
| ECU 信息/读取 | 确切授权后只允许一次范围限定的 0x22 DID；在 UI 明示 CAN1/Req-Rsp ID/DID、真实回复/NRC/超时 | 其余服务必须 F2 专项审查 |
| 烧录启动、擦除、解锁、下载、复位 | **F1 保留对应外观位置时必须禁用且明确显示“F2 未授权/不可执行”**；不得接任何真实发送命令；可选择只读展示状态但不得伪装进度 | F2 经具体 ECU 与镜像授权后才逐项开放 |
| 进度、日志、失败定位 | 优先沿用 CanTool 可见位置；增加必要 USB ACK/CAN TX/RX/UDS PDU/NRC/失败阶段真实证据，可考虑在对应区域添加明细，不改变主操作流程 | F2 才可显示实体传输确认和最终 APP 复验 |

## 四、实施一致性验收
1. 获得 CanTool UI 原件后，提交一张“原 CanTool 窗口/控件 → FlashLab WPF 对应控件 → 可运行状态/差异理由”的逐项表；必须附参考截图/源码可核实引用。没有原件不宣称 1:1。
2. Windows 上打开两者对照：窗口层级、主要分组、功能次序、字段标签和日志操作达到可核对的高一致性；**所有视觉偏离须说明原因，不能擅自改成设计者喜好的新样式**。
3. 改变内容仅限硬件替换（原适配器 → 真主控 WinUSB CAN1）、原程序漏洞修复、必需的 F1 安全状态/证据提示、中文实际用户反馈；高风险按钮 F1 不可执行。
4. 相同按钮路径从选择配置、镜像、连接、读取身份、查看日志到断开，实际可操作且能得到真实结果或明确错误，不能空按钮/随机完成。
5. 底层调用直接复用已有 AutoTest.Devices、AutoTest.Specialists 中的 WinUSB/CAN/UDS/ISO-TP/Flash Parser；不得因界面复刻创建第二套协议栈。
6. HOST 双进程独占、FC 真实会话关联、PhysicalCommandFence 物理门禁、HOST 回归仍是硬性前置，UI 外观不能绕过。

## 五、禁止事项、当前状态与正式授权
- 不因用户说“照抄 UI”而认为已批准 G4-F1 编码；目前仅技术方案 **CONDITIONAL_ACCEPT**，项目负责人正式实施许可仍为 PENDING。
- 不因 UI 中存在“烧录”等参考按钮就绑定 0x27/0x31/0x34/0x36/0x37/0x11；不调用未经许可的 0x22 现场查询。
- 不复制 CanTool 原安全算法静默 fallback、老 parser 跳过坏记录、错误 0x74 长度解析、校验漏检、未经确认的 OEM 常量。
- 不修改 main/local/UI 主线/固件；所有实施仅在获批后于本专项分支进行。
- F1 完成和用户 Windows 现场验证之后才单独审查 F2。

**交付结论：** UI 方向已由用户定为“参考 CanTool 原界面直接复刻”，不是请 FLASH AI 再次自行设计独立 UI；只缺待取得的实际 UI 原始截图/窗体文件来实现可证明的视觉一致性。