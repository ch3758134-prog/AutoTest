# G4 软件阶段交付报告（候选）

## 冻结对象的区别
- 设计输入：G3 v0.2、G4 v0.2 继续有效。
- HOST 分支：`g4/v1-stage-4`。DEVICE 软件检查点 `dd65cbdfbd849da98a4618bec8cb31923e695c95`；阶段文档提交后的最终 HEAD `0098b9fcdc83db3331e661f76d1fc4375c4269c6`，工作树干净。
- Windows 本地复验：`dotnet restore .\AutoTest.sln` 退出码 0；`dotnet build .\AutoTest.sln -c Release` 退出码 0，0 errors / 0 warnings；`dotnet test .\AutoTest.sln -c Release --no-build` 退出码 0，145/145 PASS、0 failed、0 skipped。
- 测试项目计数：Devices 24、Specialists 41、Application 16、Projects 10、Records 12、Testing 15、Desktop 6、Integration 13、Contracts 8。
- SAEF：G4 Gate 与 G5 Gate 均 `NOT_REVIEWED`；G4 工作产品为 `DRAFT`。本记录不构成 Gate 批准。

## 已实现（软件实现/测试证据）
- 九生产程序集；工程资源与 DBC 编辑基础、TEST 编辑/执行、Prism WPF 工作台、CAN/Station Mock、记录/扫码生产模拟、波形/UDS/Flash Mock、USB Session、WinUSB Windows 入口和只读硬件调试 CLI。
- 本报告记录的是软件构建和自动化测试证据，不代替 WPF 实际操作验收。

## 尚未完成（不应称为交付通过）
- 真实 USB/CAN/IGN/供电/按键五路电压/ECU 通路；真实 DBC 信号实时记录；UDS ECU 响应；真实 Bootloader Flash 和恢复。
- 完整专业 CAN/DBC/Wave/UDS/Flash 人工 UI 与可配置控制器状态台架 UI，现有 UI 已发现结构和交互缺陷。
- 最终应用场景自动化出厂检测的机械夹具/气路/电气完整工位。

## 结论
可作为**开发分支的阶段性软件候选基线**。真实硬件验证为 `NOT_RUN`；不得将其标为 G4 Gate PASS、R1 发布版本或实际 ECU 出厂合格。
