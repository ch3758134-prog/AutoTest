# P0-07｜AutoTest 工具与开发环境配置

> SAEF v2.0 / `P0-07` · **M2 修订候审，尚未替换本地 ACCEPTED 版本**。以本地已接受文档为父版本，旧哈希、审批记录及差异表见本包。

## 一、工具、平台和依赖版本

本文件只固定**目前可以证明**的 P0 文档管理环境及后续专业工具的识别要求，不假定最新 HOST 工程已经能编译。

| 工具/环境 | 版本或身份 | 证据等级 | 用途 |
|---|---|---|---|
| Windows 开发主机 | Windows，具体版本及架构未在当前回执记录 | 部分确认 | 本地工程、Codex、SAEF 工具 |
| Python | **3.12.10**（历史 P0-01 Codex 回执，非本轮复验） | `SOURCE_VERIFIED`（执行方回报） | SAEF 脚本；标准库，无需 pip |
| SAEF | `FRAMEWORK_VERSION.json` 与 `catalog.json` 记载 **v2.0.0** | 原件可核对 | 55 工作产品、工作区检查、交付、审批、站点生成 |
| SAEF 工作根 | `D:\01_Projects\auto\AutoTest_SAEF_Local_Initialize_v1\AutoTest_SAEF_Local_Initialize_v1` | 执行方回报 | 当前本地 SAEF 运行位置 |
| Git | 当前 **无仓库**（P0-01 回执） | 执行方回报 | P0-04 将规定项目文档本地版本管理 |
| HOST C#/WPF/Prism | 目标技术体系已知；现用 .NET SDK、Visual Studio、Prism/包版本和源 HEAD 未核对 | **待 G0/G3** | Windows 上位机开发/构建 |
| FW-MAIN / FW-KEY 编译 | 固件原件已归档；Keil / CodeWarrior 等具体现场配置、编译器版本和可复现构建未获新证据 | **待 G0** | 固件基线审计与未来必要修改 |

## 二、安装与配置

- **P0 阶段仅要求**：已完整解压的 SAEF v2.0、`WORKSPACE/AutoTest`、Python 3.10+、能够读取本地文件。不得为了填充工具表而自动安装第三方依赖、SDK 或升级固件工具链。
- 交付操作前确认当前目录为含 `TOOLS/eng.py`、`FRAMEWORK/catalog.json` 的 SAEF 根；确认四项资产在 `engineering/control/assets.json` 指定路径存在。
- 项目 `.gitignore`、独立备份、Git 仓库按 **P0-04 的当前受控规则及后续批准变更** 执行；本工作产品不执行 `git init`、不规定开发 IDE 固定安装路径。
- 涉及 WinUSB 驱动、主控连接和真实硬件动作的专用环境在后续 G0/G3 记录和审批，不能把“Python 脚本可运行”当成 HOST 已可操作硬件。

## 三、构建及运行命令

从 SAEF 根目录执行以下**已使用且可重放的管理命令**（Windows）：

```powershell
py -3 --version
py -3 TOOLS/eng.py check
py -3 TOOLS/sitegen.py .
py -3 -m unittest discover -s TOOLS/tests -v
```

在批准的工作包下，增量交付执行：

```powershell
py -3 TOOLS/eng.py apply "<已批准的交付ZIP绝对路径>" --dry-run
py -3 TOOLS/eng.py apply "<已批准的交付ZIP绝对路径>"
py -3 TOOLS/eng.py check
```

备份与恢复校验的命令形态：

```powershell
py -3 TOOLS/eng.py backup "<独立备份目标绝对路径>.zip"
py -3 TOOLS/eng.py verify-backup "<上一步实际生成的ZIP绝对路径>"
```

`<...>` 必须由执行合同填成实际路径。**没有获批准的交付包时，不运行 apply；没有负责人真实批准的 JSON 时，不登记批准决定。** HOST 和 FW 的具体构建命令在取得相应真实工程与工具链后补入对应专业配置，不得自行猜测。

## 四、复现与检查

- 已有证据：P0-01 Codex 回执中 `check` 返回 PASS（55 项工作产品、4 项资产、0 问题）、备份 `verify-backup` 成功、`sitegen.py` 生成 80 页。**P0-02/03 实际归档与 P0 自检回归仍待对应回执**。
- P0 阶段的复现要求：独立目录中用同版 SAEF 工具运行 `check`、自动测试及站点生成，记录实际 stdout、退出码和 Python 版本；后续以此环境清单建立 Git/备份身份。
- 专业环境（HOST/FW/台架）未到可复现构建证据级别，禁止标注 BUILD_VERIFIED / BENCH_VERIFIED。这一缺口不阻碍 P0 制度审批，但阻断相应专业 Gate 的实测/构建宣称。

## 五、证据存放位置

- 工具发行身份：`FRAMEWORK_VERSION.json`、`FRAMEWORK/catalog.json`、`TOOLS/eng.py`、`TOOLS/sitegen.py`；项目事实：`WORKSPACE/AutoTest/engineering/control/assets.json`。
- P0-01 现场回执由项目负责人本轮提供，历史归档路径由 SAEF `_operations/` 和 `engineering/control/approvals/` 记录。正式验收需将日志存入经批准的可恢复证据目录，由 P0-04/P0-05 指定，不在本文件捏造新文件。
- Git 初始化、独立恢复演练、HOST/FW 工具清单均登记为 P0 Gate / 后续适用 Gate 的待关闭证据，不得无来源补全。

**本项验收判断**：P0 管理环境的工具识别、实际命令及复现检查方法已明确；最终“在指定环境可复现”的充分性，还需要按此清单收到用户 Windows 复现回执，不能只靠本文件自审宣称已经验收通过。
## 实际研发环境与双固件/新 HOST 工具链控制

- 本机 SAEF 使用已上传归档提供的 `TOOLS/eng.py`、`FRAMEWORK/`、`WORKSPACE/AutoTest`；上传归档中 HEAD 为 `33a59d9…`，实际本机状态需再次核查。
- 主控、按键两套 MCU 固件应分别保留准确编译工程/Target、Keil 版本、编译器、烧录器/步骤、映像 SHA、板卡身份及运行证据；目前仅收到压缩包，**不能宣称固件已经用当前环境重新编译**。
- 新 Windows HOST 采用 WPF/C#，Prism 9 是已确认的框架方向，但依赖版本、工作台 Dock 选型、模块程序集拆分和项目源码组织仍由 G2/G3 设计。旧 `src` 工程目录只是研究资料，非强制重建方案。
- 真实台架验证需要现场 PC、扫码枪、两路电源、主控与按键板、ECU、夹具和连接；B0 阶段核实具体可用设备及物理安全条件。虚拟协议/模拟设备用于提前单测和契约验证，不替代 B0～B6。
- Codex 只执行明确提交的复制、`dotnet`/Keil 编译、本地 Git、测试和日志收集命令，不独立运行未经批准的试验操作。
