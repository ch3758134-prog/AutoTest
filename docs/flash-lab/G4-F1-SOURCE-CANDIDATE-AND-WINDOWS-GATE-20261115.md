# AutoTest FLASH｜G4-F1 已批准实施的源码交付与现场门禁

日期：2026-11-15。
授权：项目负责人已批准 G4-F1 一次性源码开发，未批准 F2 实体烧录或未经许可的现场诊断。
分支：feature/flash-lab-20261114。
当前状态：SOURCE_COMMITTED / BUILD_NOT_RUN / HOST_REGRESSION_NOT_RUN / WINUSB_EXCLUSIVITY_UNVERIFIED / ECU_UNVERIFIED。

## 一、实施范围
独立 Windows WPF WinExe 工程：src/AutoTest.FlashLab/AutoTest.FlashLab.csproj，独立运行，不依赖 Desktop WPF 窗口。
配置：CanTool XML 字段加载、编辑、另存，保留未知字段、来源标签；不将历史样本设为真实 ECU 默认值。可只导入 settings.json 中的空白字段。
固件：离线严格解析 HEX/H86、S19/MOT、BIN（必须填基址），校验段、大小、SHA256、相对于配置的 Flash 起止范围；不修改镜像、不运行 Seed/Key。
连接：原 WinUSB MainControllerUsbIdentity + UsbSessionService/PING；HardwareCanReceiveBridge 接真实 0x41 CAN1 RX；同一实例不得双进程并占。
只读 DID：复用 UdsSessionService/UdsClient/IsoTpConversation/CanHubIsoTpLink；手工整串确认确切 Tx/Rx/DID，再弹窗二次确认，只能发一次 0x22 指定 DID；USB 物理 Fence 再次独立拦截，不信任上层参数。
原始证据：UsbSessionService/WinUSB/CAN 0x41/UDS 时间线落 JSONL，手动导出 ZIP；ACK 不当 ECU 正响应。
写入类：所有 F2 控件固定禁用，没有实体 Flash 实现，不开 0x10/11/27/31/34/36/37。

## 二、共享 HOST 改动及风险
- PhysicalCommandFence 新单次指定 0x22 DID 限权，绑定 CAN1、指定 Req/Rsp ID、USB generation、30秒；严格一条 SF + 由真实 FF、匹配 0x62 DID 与实际后续 CF 才允许 30 08 00 FC。其他 SID 与 CAN0 拒绝；Disconnect/Reset 撤销。
- UsbSessionService 只在原始 CRC 已校验的 USB 0x41 收包路径通知 WinUsbByteLink，不能由 UI 伪造 FF 放行。
- Can1ReadOnlyDiagnosticTransmitter 经既有 CAN 租约、代际和 UsbSessionService 发送；每次物理写仍通过原 Fence。
- SystemWinUsbTransportNative 的 CreateFileW 打开模式改为 shareMode=0。该变更影响 HOST，因此必须 Windows 实测，若此驱动不允许独占则禁止发送、不可自动回退旧的 READ|WRITE 共用打开模式。

## 三、Windows 本地源代码检查（本轮没有运行）
在仓库根目录执行 PowerShell:
    powershell -ExecutionPolicy Bypass -File tools/flash-lab/verify-windows.ps1 -Configuration Debug -AllHostTests

脚本不连接 USB、不发送 CAN、不启动 CI；结果及原始错误日志写入 artifacts/flash-lab-f1/时间戳目录。
若失败，不标记 PASS。编译通过后可在 Windows 手动打开：
    dotnet run --project src/AutoTest.FlashLab/AutoTest.FlashLab.csproj -c Debug

## 四、必须进行的真实设备验证（均未执行）
EXCL-01：先 HOST 连接同一主控，FlashLab 第二个打开必须拒绝；HOST 不可被干扰。
EXCL-02：先 FlashLab 连接，再 HOST 第二个打开必须拒绝；FlashLab 不可被干扰。
EXCL-03：断开/重连/异常退出后设备必须能重新打开，无双进程帧混入。
HOST-01：HOST 主控连接/PING、CAN0 CAN1 接收监视、断连/重连正常；原 trace 不丢。
HOST-02：HOST 原单帧人工物理许可（另需现场授权）正常；无许可时 CAN 物理写拒绝。
HOST-03：既有 Run 代际、权限过期与异常路径不能回归。
DID-01：必须另行现场许可，实际 TX/RX/DID 有有效 ECU 正响应 62 DID 或 NRC/超时失败证据。USB ACK 不证明 ECU 回复。
DID-02：真实长响应 FC 只允许由匹配 FF/CF 触发；断连、乱序、过期一律拒绝。
若设备支持双进程并发打开，或独占 CreateFileW 失败：保持门禁未通过，不开放现场读取。

## 五、CanTool UI 原件差异
用户决定 UI 直接照 CanTool。但当前研究只有原参考程序业务逻辑、配置结构的历史 Issue 资料，无原主界面截图、WinForms Designer.cs 或现有可运行程序窗体，因此本次 WPF 只按 CanTool 已知的配置、固件、流程和日志操作构成可运行初版。
这是 UI 原件未核对的候选，不得声称像素级或控件级 1:1 复刻已通过。得到原界面/对话框原件后，应逐项核对并修改；不另行自行设计全新风格。

## 六、真实边界
F1 的一次性开发授权不等于现场物理 CAN 诊断许可。禁止 ECU SecurityAccess、擦除、下载、复位及任何写入；不得使用模拟 ECU 结果冒充真实台架证据。代码不得合并 main，直至项目负责人审查授权。
