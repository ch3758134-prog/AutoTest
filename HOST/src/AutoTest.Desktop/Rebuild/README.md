# AutoTest · 完全重建的纯 UI 独立应用
该目录只依赖 .NET Windows WPF 与 Prism，不引用 HOST 任何真实 TEST/PROJECT/DEVICE/UDS/FLASH 业务工程。本阶段的 UiStudyStore 数据属于本机界面交互草稿（%LOCALAPPDATA%/AutoTest/UI-Rebuild-Study），不能用于 ECU 真实测试和正式工程。
入口：dotnet run --project HOST/src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug
用户进入主窗口后通过左栏打开工作台/工程资源/自动测试/专业工具/生产/历史/设备；工具窗有原生 Owner，并随主窗关闭。
UI 研发状态：源码已写入，Windows 编译、启动、交互实测未执行；需由 Windows Codex 编译与用户集中验收。无真实硬件、CAN、Flash 运行或 PASS 证据。
删除边界：旧 Desktop 源文件、旧 UiPreview/G4Workbench 和耦合旧界面的 Desktop.Tests 在当前 feature 分支清理；原 main Git 历史未改，非 UI 领域工程仍保留。