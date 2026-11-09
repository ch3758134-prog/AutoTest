# G4-02 · 构建环境与可复现构建

> 状态：DRAFT / 阶段性实施记录，未接受、未完成 G4 Gate 评审。权威状态见 `engineering/control/work_products.json`。

## 已知 Windows 构建环境
- 平台：Windows WPF / Prism 9，单机离线工作台。
- `global.json`: .NET SDK `10.0.400`，rollForward `latestPatch`，allowPrerelease `false`。
- `Directory.Packages.props`：`Prism.DryIoc 9.0.537`、`Microsoft.Data.Sqlite 10.0.14`、`Microsoft.NET.Test.Sdk 17.14.1`、`xunit 2.9.3`、`xunit.runner.visualstudio 3.1.5`。
- 独立 WinUSB 硬件调试工程：`tools/AutoTest.HardwareDebug/AutoTest.HardwareDebug.csproj`（最新上传工作树存在，尚未提交）。

## 本地复现
在 AutoTest_HOST 仓库根目录运行：
```
dotnet restore AutoTest.sln
dotnet build AutoTest.sln -c Release
dotnet test AutoTest.sln -c Release --no-build
```
在 Windows 编译 `AutoTest.HardwareDebug`；不得在归档复测中执行真实连接命令，必须与物理测量分开。

## 证据与现状
本轮用户提供的 Codex 报告称 Restore 退出码 0、Release Build 0 警告 0 错误、145/145 自动测试通过；因原始 Windows 日志未在本次 ZIP 内形成审定材料，仍标记为 `REPORT_RECEIVED / RAW_LOG_PENDING`，不是本环境再现。
报告对应的源码配置是未提交工作树，务必先确认工作区与归档哈希对应，再建立新的已提交构建基线。禁止用 `bin/obj` 产物替代源码配置证明。
