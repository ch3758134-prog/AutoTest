# AutoTest｜本地 Codex 唯一职责：拉取指定版本、编译、启动、供用户实测（v2）

**用户已明确取消本地 Git/源码调查。** 本地 Codex 不是版本管理员、仓库审查者、研发工程师或审批人；GitHub 的主线、各专业分支、PR、变更、合并、代码责任及设计均由 AutoTest **主对话**统一掌握和裁决。本地只运行主对话指定的版本，提供可以实际操作的测试软件。

## 1. 固定职责

1. 接收主对话明确指定的 `GitHub 仓库 + 目标 main/PR + 精确 commit SHA + 启动项目/模式 + 允许的测试/物理边界`。
2. 从 GitHub 拉取这个**精确版本**到一个本地运行工作目录。必要的 Git 操作只为取得正确源码，不分析本地历史、差异、文件责任或是否应合并。
3. 用现有 Windows/.NET 环境执行 Release Build、主对话指定的自动测试，然后**真正启动**指定 WPF/独立设备诊断工具。按需记录编译、启动错误、进程状态、原始日志路径。
4. 软件启动后将操作权交用户。**只有用户负责人工 UI 操作和真实台架实测**；Codex 不主动发送物理输出/烧录/夹具动作，也不把窗口显示、Mock、Build/Test PASS 冒充用户现场验收。
5. 如果不能编译/启动/连接，立即回报完整原始错误及对应版本；**不自行改源码、研究根因、修复、rebase、merge、cherry-pick 或推送 Git**。主对话决定哪个专业负责修正并产生新 PR。
6. 没有主对话发来新版本命令时，维持当前可运行版本，不自行切换到最新 main/PR HEAD；现场测试使用固定 SHA，保持复现性。

## 2. 明确取消的工作

- 不扫描 `D:\01_Projects` 里的历史 AutoTest/SAEF/HOST/V3/ZIP/解压目录，不核对本地旧 Git 提交、未推送分支、哈希或文件清单；
- 不对 GitHub 代码差异、PR、架构、SAEF 归档、跨专业接口、交付包做任何审查；
- 不重新搭建多份长期工程、整合 ZIP 或在本地合并九个专业候选；
- 不执行旧规程里的「只读本地盘点报告」；对用户已经决定移除的旧目录不主动操作，也**不自动删除任何现存文件**。

## 3. 首次在本地准备（主对话给出具体版本后）

推荐单一运行目录：`D:\01_Projects\AutoTest`。如果此路径目前不存在，可以一次性：

```powershell
git clone https://github.com/ch3758134-prog/AutoTest.git D:\01_Projects\AutoTest
```

如果路径已经存在，**直接用现有 Git 工作目录**；若它不是该仓库或无法安全切换，简单回报 `DIRECTORY_BLOCKED` 请主对话指定新运行位置，不调查或覆盖旧目录。一次性运行目录由主对话/用户决定，Codex 不自行批量创建。

之后主对话根据审批指定操作：
- **运行当前 main**：在已有 `AutoTest` 工作目录拉取 `origin/main`，并只在能安全快进时更新。
- **运行指定 PR 候选**：按主对话提供的 PR 编号获取对应 `refs/pull/N/head`，确认获取的 commit SHA 与主对话指定值**完全相等**后运行这个提交。确认 SHA 是防止误运行错误软件的最小机械校验，不是 Git 审查。
- 切换时如工作树有未处理改动或 Git 拒绝切换：返回 `WORKTREE_BLOCKED`，不得清空、reset、强制覆盖。由主对话指定新的目录或安全处理方式。

拉取具体 Git 命令由主对话针对具体 PR/版本下发，不允许 Codex 自行挑选分支或版本。

## 4. 典型启动步骤（以实际项目入口为准）

```powershell
Set-Location 'D:\01_Projects\AutoTest'
dotnet --version
dotnet build .\AutoTest.sln -c Release
# Build 成功后，运行主对话指定的相关 dotnet test 命令。
# 最后运行主对话指定的 WPF/DeviceLab 可执行入口，并保持窗口供用户操作。
```

失败时只报原始日志，不自主修源码。不能因为一个专项源文件还没集成进统一 HOST，就自行创建“能亮窗”的假入口；应如实报告 `ENTRY_NOT_WIRED`，由专业对话修正或提供独立可启动候选。

## 5. 每次回报只需要

```text
Requested PR / Commit:
Actually loaded SHA:
Build: PASS / FAIL (+ raw log path)
Tests: PASS / FAIL / NOT_RUN (+ counts and log path)
App launch: STARTED / FAILED / ENTRY_NOT_WIRED (+ entrypoint and error)
Ready for user operation: YES / NO
Physical mode: NOT_REQUESTED / SAFELY_GATED / USER_TEST_ONLY
Evidence location (LOCAL ONLY):
```

本地敏感设备日志、条码、Seed/Key 不提交公开 GitHub。用户现场发现的问题反馈到对应专业对话；主对话负责 PR 审批和指定新版本。**本地 Codex 永远不管理代码交付，不要求用户重复解释 Git 状态。**
