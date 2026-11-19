# AutoTest｜UDS 专项分支启动与独立实测上位机

**分支**：`feature/uds-lab-20261118`（从 `main@1d6eeba8561a779640b20c5a3f384327a0018079` 建立）。  
**角色**：UDS 专业研究/开发 AI，负责 UDS 专项与 `AutoTest.UdsLab.exe` 独立 WPF 实测程序。  
**状态**：新分支工作入口；尚无单独 UDS Lab 项目、Windows Build/真实 ECU 验收。

## 必读管理合同

先从 GitHub **`Project_Management`** 分支读取正式任务与责任边界：

`docs/project-management/new-baseline-20261112/research/UDS-DEDICATED-BRANCH-AND-REAL-LAB-20261118.md`

参考推进依赖：

`docs/project-management/new-baseline-20261112/research/G4-FLASH-UDS-WAVE-SCAN-SEQUENCING-20261118.md`

这两个文件是管理合同与分期验收依据，**不要复制一套与管理分支脱节的 UDS 任务解释**。

## 请按阶段推进

1. 调查 `src/AutoTest.Specialists/Uds/` 的真实实现、`src/AutoTest.Devices/Usb/` 与设备会话、TEST 适配器和现有专用测试；列出现有/缺失/受限功能，并提交完整 UDS 详细设计供管理审查。
2. 在本分支实施**独立启动的 WPF 应用** `AutoTest.UdsLab.exe`（项目路径建议 `src/AutoTest.UdsLab/`）。复用既有 WinUSB、CAN Frame Hub、ISO-TP、UDS Client、Trace，不另造第二套协议栈或假的设备成功路径。
3. 应用至少可进行真实连接/断开、诊断 Profile 编辑与保存、只读 UDS 请求、十六进制请求/响应、DID 解析、正/负响应、NRC 0x78、超时/取消、完整原始诊断时间线和导出。
4. 对 0x10/0x11/0x27/0x2E/0x31/0x34/0x36 等可能改变 ECU 状态的 SID 保留当前风险门禁；若需要开放，先提交具体 ECU、任务级授权、物理副作用与错误状态合同，不因 UI 能输入 PDU 就自动允许发送。
5. 本地 Codex 做 Windows Build/Test 与 EXE 启动收集；用户现场用**真实 ECU** 完成诊断实测。源码完整、构建通过和实机结果必须单独汇报。
6. 独立 UDS Lab 经实际验证后，再研究接入 G4 的工具入口和正式 TEST UDS 步骤；不为赶集成提前修改 G4 自动测试执行链。

**特别注意**：UDS 专项不接管 FLASH 已验证的 CanTool 烧录、OEM Seed/Key、Flash 镜像、擦除/编程和 APP 版本后验；不自行修改主控板/KEY_SIM 固件；不用 Mock 作为实体验收。

## 交付

此分支直接开发并提交专项源码，形成短期 Draft PR 供技术审查，未经批准不合并 `main`。提交完整设计文档、改动文件、准确 SHA、构建/专测结果、真实 ECU 日志、配置与已实测服务矩阵。**不要把“没有真实设备测试”写成已通过。**
