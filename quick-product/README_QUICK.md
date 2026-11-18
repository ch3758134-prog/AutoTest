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
