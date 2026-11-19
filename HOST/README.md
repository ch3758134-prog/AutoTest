# HOST · Windows ECU 测试上位机

本目录是独立的 Windows HOST 工程，使用 .NET 10、WPF、Prism；主控板和按键板固件不属于本工程目录。

- 主解决方案：[`AutoTest.sln`](AutoTest.sln)，后端过滤方案 [`AutoTest.Backend.slnf`](AutoTest.Backend.slnf)
- WPF 启动项目：[`src/AutoTest.Desktop/`](src/AutoTest.Desktop/)
- 领域与设备：`src/AutoTest.Core`、`src/AutoTest.Testing`、`src/AutoTest.Devices`、`src/AutoTest.Specialists`
- 自动化测试：[`tests/`](tests/)
- 工具：[`tools/`](tools/)，样例 [`samples/`](samples/)，脚本 [`scripts/`](scripts/)

在本目录的 Windows PowerShell 中运行：

```powershell
dotnet restore AutoTest.sln
dotnet build AutoTest.sln -c Debug --no-restore
dotnet test AutoTest.sln -c Debug --no-build --no-restore
```

日常功能研发和实测在 GitHub `HOST` 分支；此 `main` 目录为固定源码快照。独立 UDS/FLASH 专项候选经过审查再接入 HOST。最新 UI 设计仍由 `ui/ide-design` 分支负责。

硬件控制接口及两块板固件位于仓库根目录的平级工程 [`Main_Control/`](../Main_Control/) 和 [`Key_Sim/`](../Key_Sim/)。跨专业技术资料位于 [`../docs/`](../docs/)。

**当前限制**：本次目录调整没有执行 Windows Restore/Build/WPF 启动、实体硬件或自动 TEST 全链验收。不能把源码/目录存在当作功能通过。
