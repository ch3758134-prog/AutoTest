# Engineering Workbench｜单人离线工程生命周期管理

**当前默认程序为 V0.3 通用可编辑工作台，不再是早期 KEY_SIM 只读演示。**

运行与实际操作流程请先看 [STUDIO_V03.md](STUDIO_V03.md)；在 AutoTest 源码根目录：

```powershell
py -3 -m unittest discover -s tools/lifecycle/tests -v
py -3 tools/lifecycle/server.py --repo . --port 8765
```

打开 `http://127.0.0.1:8765/`；没有账号。数据写入本机 SQLite（Windows 默认 `%LOCALAPPDATA%\EngineeringWorkbench\workspace.sqlite3`），项目可独立导出为 JSON，但**尚无 JSON 一键恢复**，应定期备份 SQLite 文件。

工作台已经实现多个项目、工程对象增改、显式上下游关系、关系人工确认、技术决策/变更、历史修订、审批、变更影响审查、工作包准入、受控 AI 上下文生成，以及可选 SAEF 历史档案导入。

旧试点保留在 `lifecycle.py`、`catalog.py`、`ui.html`、`data/keysim-trace.json`。需要旧版只读资料分析时：

```powershell
py -3 tools/lifecycle/server.py --legacy --repo . --port 8766
```

该旧版不得冒充通用项目管理软件。新版项目数据不自动写回 AutoTest 的正式 SAEF/代码。详细验收边界见 [STUDIO_V03.md](STUDIO_V03.md) 与 [PRODUCT-REQUIREMENTS-V03.md](../../docs/lifecycle-pilot/PRODUCT-REQUIREMENTS-V03.md)。
