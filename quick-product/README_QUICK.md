# AutoTest QUICK · Legacy EPB USB Test

**QUICK 运行主体：Product.zip → Product/WPF_APP/EPB_UsbTest。** 不是 G4 AutoTest.Desktop。

本包只保留可维护的原始源码、EPB.Core、必需的 native/libusb-1.0.dll、configs，丢弃历史 bin/obj、重复子工程和大型 dump。

Windows 11 / .NET 10 SDK / x64：

```powershell
cd quick-product
dotnet restore .\EPB_UsbTest\EPB_UsbTest.csproj
dotnet build .\EPB_UsbTest\EPB_UsbTest.csproj -c Debug
dotnet run --project .\EPB_UsbTest\EPB_UsbTest.csproj -c Debug
```

设备协议注意：QUICK 已在 UsbLink 中阻断旧 0x10/0x11/0x14/0x15/0x16/0x17/0x1F，防止历史配置向现役主控下发废弃供电/SOL 命令。连接后不再自动开 ECU/KEY 电源或 IGN，只读取 PING、GET_VERSION，版本匹配 v0.4-dev 时读取 GET_IGN/GET_CLAMPS 两项 MCU GPIO 电平。最新固件的独立夹具 SET_CLAMP=0x18 [clamp,state] 暂未在旧 WPF 中开放手动操作，禁止将旧 SOL 测试当作双夹具验收。

使用真实板卡时先仅核查设备发现、WinUSB Bulk 端点（IN 0x82、OUT 0x03）、PING、GET_VERSION、GET_IGN/GET_CLAMPS；UI 的“USB 已连接”不代表固件兼容、状态测量或执行安全。旧工程其他 CAN/KEY/IGN 的物理操作尚未进行全面安全和运行校验，操作前需核对现场接线。

原文件由用户上传的 Product.zip 中提取；未验证 Windows 构建和实机控制。

## QUICK 流程控制首批实测（2026-11-17）

本 QUICK 产品的 `EPB.Core/Testing/AutomaticTestRunner` 直接执行测试用例保存的 `TestFlowNode`，不使用 G4 的 TestDocument，也没有新增第二套 Runner。

- **IF / ELSE**：引用此前具备期望值的 CAN 解析步骤 Verdict；将测试动作放在 THEN/ELSE 内。非法、缺失、未运行的结果引用在预检阻断。
- **WHEN**：引用此前的 CAN 解析判定，周期重新检查 Verdict；HIT/TIMEOUT 为两条真实路径。TIMEOUT 无处理步骤时会记录判定失败，不把等待超时当成 PASS。
- **SWITCH / CASE / DEFAULT**：绑定工程通信数据库信号，按新收到的 CAN 信号物理值选择分支。支持 1～60000ms 读取等待，未收到新帧报错，不凭缓存值假装命中。
- **LOOP / BODY**：新加的真实循环，可在右侧直接设置 1～100 次、0～60000ms 轮间等待；循环体可以嵌套普通步骤和控制节点。循环不自动释放 KEY/复位 IGN/控制夹具。一次运行最多展开约 2000 个控制/步骤节点；循环内禁止重复启动周期广播。
- 空用例可以直接添加第一条 IF/WHEN/SWITCH/LOOP；在控制主行下面不能直接添加操作，需先选择对应 THEN/ELSE/HIT/TIMEOUT/CASE/DEFAULT/BODY 路径。
- 以前“静态驻车”展示的假流程示例已经移除，当前编辑列表只显示可保存的真实节点；单击/双击不再隐式将已禁用的步骤改为 Enabled。

**推荐 Windows 验证**：启动旧 WPF，新建用例→ `＋LOOP`→选择 BODY→ `＋操作`→等待 100ms→右侧设置 3 次→保存→关闭重开，检查 3 个运行 Occurrence 与顺序；IF/WHEN/SWITCH 分别用真实 DBC 信号和 ECU 数据验证选中分支。执行前先核对当前主控已刷入的固件和被保护的旧 USB 命令，切勿把代码路径或预检通过当成实测通过。

**构建与实机状态**：此提交仅做静态结构检查和 WPF XAML XML 解析；容器无 Windows/.NET SDK，尚未 Windows Build/Test 和实物联机。请本地 Codex 严格按当前 QUICK SHA 机械编译、收集完整错误与日志，不自行修改逻辑。
