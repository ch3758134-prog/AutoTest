# AutoTest 新 UI｜Windows 统一验收任务

**当前执行事实：** 本会话未能在 Windows dotnet SDK 上运行编译与 UI 操作；以下为待执行测试清单，状态全为 NOT_RUN。

## 构建和启动
在 Windows SDK 10.0.400 环境运行：
```powershell
cd HOST
dotnet restore .\AutoTest.sln
dotnet build .\src\AutoTest.Desktop\AutoTest.Desktop.csproj -c Debug
dotnet run --project .\src\AutoTest.Desktop\AutoTest.Desktop.csproj
```
记录完整日志、截图、Windows DPI 与分辨率。构建失败直接修复，不自称 PASS。

## 统一任务脚本
1. 首次无真实工程启动，进入工作台→测试项目，确认不会被强制创建真实工程。
2. 新建三个测试用例、勾选部分本次选择、排序/复制/删除；双击打开编辑器。
3. 准备/测试/清理插入 Wait、If、Loop、CanSend、UDS、波形步骤；创建子步骤、连续修改参数、撤销/重做。
4. 打开公共流程，确认编辑环境与常规测试用例一致，但有独立入口。
5. DBC 新增报文与信号，调整起始 bit、长度和中文别名，观察重叠提示；返回 TEST 从 UI 资源选择信号。
6. CAN 增加多个待发报文、复制与周期字段，选择 DBC 报文；观察仍无真实 CAN 收发。
7. 台架切换多个 KEY_SIM 电阻开关、IGN/夹具 UI 条件，建立若干动作，排序后输入配置能查看相同列表。
8. UDS 建立并修改多个诊断请求，检查响应区没有虚构的 ECU 回答。
9. Flash 选择本地文件，修改四个 CAN ID，验证非法字段提示；没有真实烧录进度。
10. Wave 新建信号，输入手工数列，绘制与游标/缩放操作；检查有非实测标签。
11. SCAN 输入合法/非法编号，观察格式验证；没有虚假生产结果。
12. RECORD 过滤和详情空状态，DEVICE 未接线/UNKNOWN 层级。
13. 从工具中心打开独立副窗口，同时返回并编辑 TEST，确认主窗口、页签和工具不互相侵占。
14. Ctrl+S 保存 .at-ui.json，Ctrl+O 重开，复核用例/DBC/CAN/KEY/UDS/波形/公共流程内容；修改未保存时退出警告。
15. 1366×768、1920×1080 和 100%/125%/150% DPI；测试 Windows 任务栏、不失焦和窗口恢复。

## 严格验收状态
BUILD: NOT_RUN
STARTUP: NOT_RUN
STATIC_SOURCE_AUDIT: PENDING
UX_REAL_USE: PENDING
BACKEND_INTEGRATION: NOT_AUTHORIZED
HARDWARE: NOT_RUN
