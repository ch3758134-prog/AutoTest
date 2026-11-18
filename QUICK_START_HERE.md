# AutoTest QUICK — 快速实测入口

本分支 QUICK 的实际运行主体是 **`quick-product/EPB_UsbTest/EPB_UsbTest.csproj`**（用户 Product.zip 的 `WPF_APP/EPB_UsbTest`）；不是仓库原来的 AutoTest.Desktop 工程。

Windows / .NET 10 SDK：

```powershell
dotnet restore .\quick-product\EPB_UsbTest\EPB_UsbTest.csproj
dotnet build .\quick-product\EPB_UsbTest\EPB_UsbTest.csproj -c Debug
dotnet run --project .\quick-product\EPB_UsbTest\EPB_UsbTest.csproj -c Debug
```

也可以双击根目录 `RUN_QUICK.cmd`。源码、EPB.Core、真实 ECU 配置、native DLL 位于 `quick-product/`。

⚠️ 这批旧源码尚未完成 Main_Control v0.4-dev USB 命令兼容整改；特别是旧 ECU/KEY 供电与 SOL 指令不得拿去直接控制现役硬件。首次实测先收集设备发现、驱动、PING/版本和 CAN 的日志；不要以 USB 设备可枚举推定控制正常。

该 QUICK 分支与正式 `main` / G4 分支隔离。当前提交是**源码导入，不代表 Windows Build 或现场测试通过**。
