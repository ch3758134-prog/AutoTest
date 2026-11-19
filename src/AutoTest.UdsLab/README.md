# AutoTest UDS 诊断工具 · L3 简化版

本程序用于同一真实 ECU 上：**编辑 UDS 请求 → 发送 → 查看真实响应 → 修改重发**，不是复杂的工程管理系统。

## 使用方式

1. 展开顶部通信参数，填写目标 ECU 诊断请求/响应 CAN ID 和 P2/P2*，连接实际主控板。USB Ping 不证明 ECU 在线。
2. 左侧始终是请求编辑器。选择 SID 服务并填写 DID/子功能/数据，自动生成实际 UDS PDU；切到“原始 HEX”时直接编辑请求字节，无需创建操作、命名或先保存。
3. 点击发送/重发。右侧请求/响应记录逐条累积，点击旧记录查看 NRC、解析结果、耗时和原始 PDU；“回填到左侧”可继续修改重发。
4. 可选收藏请求、保存/打开诊断配置 JSON、导出真实证据，底层 CAN/ISO-TP/USB 日志按需展开。
5. Ctrl+Enter 发送，Ctrl+S 保存配置。不生成虚拟 ECU 响应或伪造成功。

## 当前技术限制

- 共享真实 UDS Client/DEVICE 通路仅开放 0x19、0x22、0x23、0x24 只读请求；0x10 会话切换、0x3E TesterPresent、清故障、复位、I/O 控制、写入、例程、烧录等尚未获实体发送许可。原始 HEX 不得越权。
- WinUSB PhysicalCommandFence 仍要求对每一帧 CAN TX 单独人工准确输入授权文本，包括 ISO-TP 流控；此限制会阻碍无人值守连续通信与严格多帧时序。本次没有绕过或修改 DEVICE 门禁。受限事务授权方案见 docs/uds-lab/UDS-L12-CONTINUOUS-INTERACTION-DEVICE-REVIEW.md，待项目管理及 DEVICE 批准。
- ECU 专有 DID、故障名称和参数格式需要 ECU 规范或用户定义；不能宣称自动发现全部诊断能力。
- 超时、取消、失联或不确定结果后须重新建立链路。
- 删除旧 L1/L2 诊断操作管理、序列、固定观察与旧 partial UI；JSON 中既有 Actions 可作为收藏导入。

## 本地验证分工

分支 feature/uds-lab-20261118；工程 src/AutoTest.UdsLab/AutoTest.UdsLab.csproj。网页端专业 AI 负责 GitHub 代码，Codex 只负责拉取、Windows 编译和启动，真实 ECU 操作由用户实测。

状态：SOURCE_CANDIDATE / WINDOWS_BUILD_PENDING / FIELD_PENDING。
