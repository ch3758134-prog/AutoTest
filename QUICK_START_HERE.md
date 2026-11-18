# AutoTest QUICK — 实际启动入口

QUICK 运行主体是用户 `Product.zip` 内 `WPF_APP/EPB_UsbTest`，已完整复制必要源码到 `quick-product/`，不要运行旧 `AutoTest.Desktop`。

```powershell
dotnet restore .\quick-product\EPB_UsbTest\EPB_UsbTest.csproj
dotnet build .\quick-product\EPB_UsbTest\EPB_UsbTest.csproj -c Debug
dotnet run --project .\quick-product\EPB_UsbTest\EPB_UsbTest.csproj -c Debug
```
也可双击根目录 `RUN_QUICK.cmd`。

**USB v0.4 快测首批改动**：原版 `ConnectionChanged` 自动发 ECU/KEY 电源与 IGN 的行为已取消；`UsbLink` 阻断已废弃的 `0x10/0x11/0x14/0x15/0x16/0x17/0x1F` 旧命令，不向硬件发送；连接后只读 PING、GET_VERSION，固件版本匹配 `EPB_TEST_CTRL v0.4-dev` 时再查询 `0x13` IGN 和 `0x19` 两路夹具 GPIO。版本字符串不是固件烧录 SHA，也不能代表真实电气或机械动作已通过。

当前旧 WPF 仍保持其原有 CAN/KEY/测试编辑结构；真实烧录、USB/GUI 构建、CAN/按键动作与夹具输出尚未完成 Windows 实测。旧 SOL/供电不能被当成新版协议操作。
