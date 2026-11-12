# Project Control｜通用项目研发管理系统

**当前 GitHub `main` 直接维护本目录，不再使用管理系统研发分支作为交付中心。**

本目录与 AutoTest ECU 产品源码彼此独立：AutoTest 是可被管理的项目之一，不是管理系统的固定业务模型。

## 开始使用

管理 AI 首先阅读 [START_HERE.md](START_HERE.md) 和 [PORTABLE_MANIFEST.json](PORTABLE_MANIFEST.json)。使用时，用户提供本文件夹和目标 GitHub 仓库地址。Codex 的本地目录由用户自己指定，其余工作来源、任务和 Git 版本由管理 AI 与 Codex 从目标仓库读取。

本机管理界面（Python 3.11+，Git）：

```powershell
python tools/project-control-v2/portable/web_server.py --port 8791
```

浏览器打开 `http://127.0.0.1:8791`。即使没有任何目标项目，管理界面也可以启动。首次使用请在首页选择“新建 / 接管”，指定真实项目目录和 GitHub 仓库，先预览再确认；也可登记已经初始化的项目。登记保存在本机用户目录下的 `.project-control-local/targets.json`，不会自动向 GitHub 推送。更详细的初始化、角色和权限边界见 [portable/WEB_WORKSPACE.md](portable/WEB_WORKSPACE.md)。

生成给新管理 AI 的独立 ZIP：

```powershell
python tools/project-control-v2/portable/export_package.py --out ProjectControl.zip
```

## 代码状态

`portable/module_control.py`、`portable/remote_module_status.py` 提供模块归属、本地 Git 提交和未提交修改的观察，以及手动核对 GitHub 模块提交和 PR。观察到代码不等于通过集成或现场测试；正式发布和审批依赖真实证据。

## 工作规则

- **只改 main**：管理工具后续变动直接提交本目录到 main。禁止为普通改动继续堆叠新分支/PR。
- **不自动跑管理工具 CI**：需要时人工执行 `python -m unittest discover -s tools/project-control-v2/tests -v`。
- 不动 AutoTest ECU 产品其他目录；不要把旧试点 UI、示例产品数据或旧工作区带入通用发行包。
- 已有工作区和正式工程默认保留，严禁自动清理。
- **候选状态**：真实目标 GitHub 仓库的端到端交付、完整人类授权、Windows 实际现场验收尚未完成。不能把本地脚本测试等同正式交付。
