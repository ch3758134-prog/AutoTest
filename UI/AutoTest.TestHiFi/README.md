# AutoTest TEST 高保真 WPF 视觉版 v0.1

**专门依据用户已认可的 2026-11-19 TEST 高保真截图制作。** 这是独立 WPF UI 设计验证项目，不接正式 AutoTest.Core、TEST、DEVICE、USB、CAN、UDS 或生产后端。用于检查**现代化视觉的真实 Windows 落地质量**，不是正式生产检测工作台。

本项目使用 .NET 10 WPF，不依赖额外的 NuGet UI 库；文件、样例报文和错误均为界面研究用的演示数据，非实测 ECU 事实。

从仓库根目录构建：
```powershell
dotnet build UI/AutoTest.TestHiFi/AutoTest.TestHiFi.csproj -c Debug
dotnet run --project UI/AutoTest.TestHiFi/AutoTest.TestHiFi.csproj
```

默认直接打开“外部输入与状态响应”（TC-003），**测试阶段 29 步，右侧选中第 16 步 CAN 信号判定，底部三条问题**。初始即为完整内容画面，不允许上次原型的空用例界面先出现。

首轮可检查布局、选择步骤/切换参数、阶段选择、用例导航、搜索和问题定位。其他工具栏是**视觉预览控件，暂不实际修改资源**；执行和单步操作禁用。UI 不伪造 CAN/UDS/主控板实际执行成功或 PASS 结果。演示绿色图标只表示设计稿中的样本行状态，不是运行证据。

字体、布局比例、浅色/白色主调及分区参照用户已认可的主设计图。请勿将本项目当作正式 WPF AutoTest UI 或合入 main。
