> **V0.3 更新：本页以下内容保留为 V0.1/V0.2 历史试点记录。当前默认工作台已改为可写入的通用工程管理软件，最新 Windows 使用说明请优先阅读 [STUDIO_V03.md](STUDIO_V03.md)。要运行旧 KEY_SIM 只读页面请使用 `server.py --legacy`。**

# 本地启动与首轮验收｜AutoTest Lifecycle V0.1

> 仅 PR #28 候选，**不修改 main**、不执行硬件输出、不启动 AutoTest DUT 软件。使用 Python 3.10+ 标准库；不需要 XAMPP、ProjeQtOr、MySQL、npm、pip 或账号。

## 建议独立工作树（在现有 AutoTest Git 仓库根目录执行）

```powershell
git fetch origin pull/28/head:refs/heads/review/lifecycle-pr28
git worktree add ../AutoTest-Lifecycle review/lifecycle-pr28
cd ../AutoTest-Lifecycle
```

如果本地已存在 `review/lifecycle-pr28`，不要强制覆盖；先确认此前检出和当前 PR HEAD 的差异，更新候选后再使用。现有研发工作树无需切换分支。

## 验证环境与自动化测试

```powershell
py -3 --version
py -3 -m unittest discover -s tools/lifecycle/tests -v
py -3 tools/lifecycle/lifecycle.py --repo . inspect
py -3 tools/lifecycle/lifecycle.py --repo . impact OBS-KEYSIM-20261109
py -3 tools/lifecycle/lifecycle.py --repo . readiness WP-KEYSIM-001
```

没有 Python 启动器时，可以使用 `python` 替代 `py -3`，但应确认其版本至少为 3.10。

## 打开本地工作台

```powershell
py -3 tools/lifecycle/server.py --repo . --port 8765
```

浏览器打开：**http://127.0.0.1:8765/**。关闭 PowerShell 服务用 `Ctrl+C`。

- **系统全貌**：显示目前仅 KEY_SIM 样例图中的 12 个工程对象、13 条候选关系及原始文件身份。
- **设计变更**：从 `OBS-KEYSIM-20261109` 查看 10 个潜在受影响对象，明确哪些边还未证实。
- **实施准入**：`WP-KEYSIM-001` 应返回 **NOT_READY**，因为其新版设计审批、B4 电压证据、版本指纹与变更复核均未关闭。
- UI 是**只读**的真实数据投影，不能从页面改写 SAEF 或批准变更，暂不承诺完整生命周期编辑功能。

## 预期状态及信息完整性

- 本地单元/API 测试：开发环境中已运行 **18/18 PASS**，用户 Windows 全仓结果尚待检验。
- 通过 GitHub 读取当前参考 main `4530aba0079e8b2344e4ff5e706049450458a45b` 的 **9/9 关键文件与字符串锚点均存在**；这不是 9 条语义关系或 B4 的物理验收。
- 本 PR 尚未审批或合并；`main` 仍是正式 AutoTest 源码中心。
- 如果出现 `MISSING`/`ANCHOR_MISSING`，先检查所检出的 Git HEAD、路径和真实文件，不得让工具自动修正正式受控设计。
- 本例 KEY_SIM 的 CAN 命令编码存在，并不代表已具备对真实 ECU 的按键电压闭环输出；真实设备控制仍受独立安全门禁约束。

## 当前有意不做

不登录、多用户、工时财务、自动审批、自动向真实设备下发操作、自动写入 SAEF 或扩大到所有 AutoTest 工程对象。这些需根据 M1/M2 的真实运行证据决定。

## V0.2 浅色工程工作台（新增）

用户反馈 V0.1 的深色数据调试页无法表达有效工程信息。V0.2 改为浅色设计、以产品和真实工程内容为首屏：

- **项目概览**：根据 G2-01 显示产品边界和平台定位；根据 SAEF 控制文件显示当前 G4 阶段及 Gate 状态；列出 KEY_SIM 变更与真实验证缺口。首页所有数字均说明统计范围。
- **工程档案**：从 `SAEF/engineering/control/work_products.json` 读取全部已注册工作产品；按 P0～G5 检索，点击后打开仓库中原始 Markdown（包括需要阅读的需求、架构和详细设计）。
- **变更与影响**：将现场反馈、需求、总体架构、详细设计、源码、验证分开呈现；点击查看实际文本摘录和来源。影响关系仍标记候选，不能伪称自动判定需求已失效。
- **实施准入**：解释真实阻断原因，区分未批准设计、缺少验证和版本未固定；当前工作包仍应该返回 `NOT_READY`。

可访问只读 JSON 接口：`/api/catalog`、`/api/document?id=G2-01`、`/api/evidence`。此版本尚不支持可编辑需求、设计修订、人工审批或自动变更落档，**不能把只读展示称作完整的工程管理应用**。

Windows 更新已运行的 PR #28：按既有 Git 流程更新候选工作树，**停止旧服务并重新启动**，之后在 Chrome 浏览 `http://127.0.0.1:8765/`。不要使用带 `/**` 的非根路径。旧端口被占用时确保旧 Python 进程已经停止，而不是同时运行两个版本。
