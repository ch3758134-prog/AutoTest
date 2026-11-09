# matrices/SOURCE_EVIDENCE_INDEX.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`e21fa0e4bf316e1f2cbb3fe247d5bbe9b211f83c13f94eab5f767292fe4fdb8c`

原始行数（含表头）：42

| 来源 | 文件（相对原始归档） | 研究内容 | 性质 |
| --- | --- | --- | --- |
| MainFW | Project/source/BSW/CDD/USB/CDD_USB.h | USB请求/响应、命令字节常量 | 固件源码可查；现场运行待核 |
| MainFW | Project/source/BSW/CDD/USB/CDD_USB.c | 帧解码与CRC | 固件源码可查；需黄金帧验证 |
| MainFW | Project/source/ASW/UsbSvc/ASW_UsbSvc.c | 命令路由、USB与CAN代理注册 | 固件源码可查；参与编译待最终确认 |
| MainFW | Project/source/ASW/BusProxy/ASW_BusProxy.c | CAN 15B载荷、CAN0/CAN1映射 | 代码依据；物理对应实测 |
| MainFW | Project/source/ASW/PwrCtrl/ASW_PwrCtrl.c | IGN/电磁阀控制代码 | 电气输出需B0/B3 |
| KeyFW | WS/EPB_EOL/Sources/APP/KeySim.c | 0x730/0x01/0x80/20位mask | 代码依据；实际KEY16需B4 |
| KeyFW | WS/EPB_EOL/Sources/APP/main.c | 按键接收处理主循环 | 代码依据；现场固件身份待核 |
| KeyFW | WS/EPB_EOL/Sources/BSW/CAN/Driver_CAN.c | 接收队列与溢出计数 | 代码依据；实际压力待测 |
| HistoricalHOST | src/AutoTest.Integration.Usb/UsbProtocol.cs | 旧USB codec与会话设计 | 研究参考非新架构 |
| HistoricalHOST | src/AutoTest.Integration.Usb/UsbSession.cs | 旧request关联与超时 | 研究参考非实测证明 |
| HistoricalHOST | src/AutoTest.Adapters.MainController/MainControllerCanAndKeyCapabilities.cs | 旧能力适配 | 需过滤过期电源假设 |
| HistoricalHOST | src/AutoTest.Communication.Can/CanSubscription.cs | CAN订阅模型 | 可提炼机制 |
| HistoricalHOST | src/AutoTest.Communication.Can.Usb/CanUsbPayloadCodec.cs | CAN字节编解码 | 用于校对固件 |
| HistoricalHOST | src/AutoTest.Workspace/WorkspaceManifest.cs | 资源/清单模型 | 参照设计，不直接沿用格式 |
| HistoricalHOST | src/AutoTest.Workspace/FileAssetStore.cs | 资产存储 | 参考文件往返与依赖 |
| HistoricalHOST | src/AutoTest.Resources.Runtime/CapabilityBindingResolver.cs | 资源端点绑定 | 结构研究成果 |
| HistoricalHOST | src/AutoTest.Testing.Authoring/AuthoringTransactionEngine.cs | 编辑事务 | 强重点研究 |
| HistoricalHOST | src/AutoTest.Testing.Authoring/UndoRedoHistory.cs | 撤销重做 | 强重点研究 |
| HistoricalHOST | src/AutoTest.Testing.Authoring/NodeTypeRegistry.cs | 节点注册 | 强重点研究 |
| HistoricalHOST | src/AutoTest.Testing.Runtime/Preparation/L5RunPreparation.cs | 执行前准备 | 研究实现思路 |
| HistoricalHOST | src/AutoTest.Testing.Runtime/Execution/RunExecutionKernel.cs | 执行内核 | 研究实现思路 |
| HistoricalHOST | src/AutoTest.Testing.Runtime/Execution/RunControlCoordinator.cs | 运行控制 | 研究实现思路 |
| HistoricalHOST | src/AutoTest.Communication.Dbc/DbcDatabase.cs | DBC语义模型 | 研究参考 |
| HistoricalHOST | src/AutoTest.Communication.Dbc/Authoring/DbcAuthoringSession.cs | DBC编辑会话 | 研究参考 |
| HistoricalHOST | src/AutoTest.Communication.Dbc/Authoring/DbcSourcePatchWriter.cs | DBC改写 | 研究参考 |
| HistoricalHOST | src/AutoTest.QuickDesktop/Uds/QuickUdsService.cs | 有限UDS请求/响应 | 只能作为部分能力参考 |
| HistoricalHOST | src/AutoTest.QuickTesting/Waveform/QuickWaveformCaptureEngine.cs | 波形采集 | 研究参考/要测质量 |
| HistoricalHOST | src/AutoTest.QuickTesting/Waveform/QuickWaveformComparator.cs | 波形对齐与匹配率 | 不能跳过真波形标定 |
| HistoricalHOST | src/AutoTest.QuickTesting/Waveform/QuickReferenceWaveformCodec.cs | 参考波形资源 | 研究参考 |
| HistoricalHOST | src/AutoTest.Records.Sqlite/SqliteExecutionRecordStore.cs | SQLite记录 | 数据恢复与不覆写必须验证 |
| HistoricalHOST | src/AutoTest.Records/ExecutionRecordBuilder.cs | 记录构建 | 研究参考 |
| HistoricalHOST | src/AutoTest.QuickDesktop/Themes/Palette.xaml | 视觉配色 | 可提炼风格，不继承旧Shell |
| HistoricalHOST | src/AutoTest.QuickDesktop/Themes/TreeTable.xaml | 树表风格 | 视觉候选 |
| HistoricalHOST | src/AutoTest.QuickDesktop/Themes/Inspector.xaml | 属性编辑风格 | 视觉候选 |
| CanTool | Config/FlashConfiguration.cs | 烧录配置对象 | 功能参考非默认配置 |
| CanTool | Services/FirmwareParser.cs | HEX/S19解析 | 功能参考需独立测试 |
| CanTool | Services/FlashSequence.cs | UDS烧录状态/序列 | 专用流程不可直接移植 |
| CanTool | Protocol/UdsTransport.cs | UDS传输与NRC | 领域参考，适配新CAN |
| CanTool | Protocol/SessionManager.cs | 诊断会话转换 | 领域参考 |
| CanTool | Protocol/SecurityAccess.cs | Seed/Key执行逻辑 | 具体算法必须按目标授权 |
| CanTool | Services/FlashService.cs | 烧录对外服务 | 领域参考 |
