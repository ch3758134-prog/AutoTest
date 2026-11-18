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

设备协议注意：旧 Frame.cs 仍包含 0x10/0x14/0x16 等已淘汰的控制命令。最新 Main_Control v0.4-dev 使用 0x12/0x13 IGN、0x18 [clamp,state]、0x19 夹具查询、0x40 CAN_TX、0x41 CAN_RX_IND。**未完成兼容整改之前不要向实物发送旧供电/SOL/夹具控制命令。**

使用真实板卡时先仅核查设备发现、WinUSB Bulk 端点（IN 0x82、OUT 0x03）、PING、GET_VERSION，并记录 USB 原始日志，确认现场固件身份；不要根据版本字符串单独判定安全性。

原文件由用户上传的 Product.zip 中提取；未验证 Windows 构建和实机控制。
