# AutoTest｜Windows 本地仅作 GitHub 拉取和验证的最简工作区

**适用于用户删除旧本地开发目录、以 GitHub 作为唯一源码主仓库的情形。** 此文档不会执行本地操作。Codex 只按主对话给出的 PR/SHA 机械拉取、编译、测试、启动和收集原始日志；不能自行修改业务代码、修复编译问题或推送合并。用户无需再下载或手动覆盖专业 ZIP。

## 0. 删除之前必须确认

如果旧目录还存在：先检查 `git status --short --branch`、`git log --branches --not --remotes --oneline`、`git remote -v`，保存尚未推送的独有代码及现场原始数据；不要仅凭 GitHub 已有 main 就假定每个旧本地分支已上云。**不执行 `git clean -fdx`、`git reset --hard`、`Remove-Item -Recurse -Force` 或格式化盘**。如果目录已删，不要再清空回收站；GitHub 上已提交的可以重新克隆，未提交/未推送的内容不保证可恢复。

## 1. 全新克隆（仅在目标目录不存在时执行一次）

Windows PowerShell：

```powershell
$target = 'D:\01_Projects\AutoTest'
if (Test-Path -LiteralPath $target) {
    Write-Host "STOP：$target 已存在。先检查其内容与 Git 状态，不覆盖。"
} else {
    git clone https://github.com/ch3758134-prog/AutoTest.git $target
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
    git -C $target status --short --branch
    git -C $target remote -v
    git -C $target rev-parse HEAD
}
```

目标目录应包含 `SAEF/`、`src/`、`tests/`、`docs/`、`AutoTest.sln`；只让 Codex 读 GitHub 上的当前 `main`。**如果旧目录名相同或用户已指定其他盘符，先停在目录检查步骤；不自行重命名、移动、删除任何旧目录**。

## 2. 平时同步 main

```powershell
$repo='D:\01_Projects\AutoTest'
git -C $repo status --short --branch
git -C $repo fetch origin --prune
git -C $repo pull --ff-only origin main
git -C $repo rev-parse HEAD
```

任何非干净工作区、无法快进或有冲突，立即停止并回报。绝不自动丢弃用户改动。不要在 main 工作树中对 PR 执行 checkout、ZIP 覆盖、cherry-pick 或编译后手工替换源码。

## 3. 验证某一已指定 PR（示例 PR #4，不代表自动授权该 PR）

在独立可删除的 worktree 上验证，不动 main 工作树。每个 PR 使用不同路径：

```powershell
$repo='D:\01_Projects\AutoTest'
$number=4
$work='D:\01_Projects\AutoTest_Verify\PR-4'
git -C $repo fetch origin "refs/pull/$number/head:refs/remotes/origin/review/pr-$number"
if ($LASTEXITCODE -ne 0) { throw 'fetch PR failed' }
$sha=git -C $repo rev-parse "refs/remotes/origin/review/pr-$number"
Write-Host "Candidate=$number Commit=$sha"
if (Test-Path -LiteralPath $work) { throw '验证目录已存在；不要覆盖。请检查后使用另一个新目录' }
git -C $repo worktree add --detach $work $sha
if ($LASTEXITCODE -ne 0) { throw 'worktree failed' }
git -C $work status --short --branch
git -C $work rev-parse HEAD
```

**对比 $sha 与主对话发出的精确 Head SHA**，不一致立即停止。Codex 此后只在 `$work` 下按给定命令 Build/Test/执行受控操作。默认先：

```powershell
cd $work
dotnet --version
dotnet build .\AutoTest.sln -c Release
# Build PASS 后按主对话指定范围执行 dotnet test。
```

构建/测试命令、ExitCode、通过/失败/跳过数量、日志绝对路径、工具版本、Git SHA 必须回报。日志原始文件本地保存，涉及密钥/扫码身份/客户数据不得直接上传公开 GitHub。**不能以自动测试 PASS 替代 WPF 用户或真实物理测试**。

## 4. 正式实测只对主对话指定的候选/已审核版本进行

- 设备/UDS/Flash 等实测须按已有受控 Gate；使用主对话指定的 `PR号 + SHA + 启动入口 + 操作步骤 + 物理边界`，不在设备旁同时运行两个抢占 WinUSB Session 的软件。
- 专业可以继续提交新版本，但不改变正在实测的 commit；新 SHA = 新候选，重新说明差异。
- 默认用户把本地实测现象/截图/脱敏日志交**原专业对话**分析；可把 PR URL 发主对话要求统一审查审批。
- 最终合并只允许经过主对话审批的 PR；本地 Codex 不推送、合并或单独改变主线。

## 5. 本地目录只保留这两个概念

```text
D:\01_Projects\
  AutoTest\            # 一份 GitHub main 的干净只读式工作树
  AutoTest_Verify\     # 按 PR 隔离的临时 worktree；Codex 机械验证
    PR-4\
    PR-5\
```

不用另建 `AutoTest_HOST_V1`、`AutoTest_V3_FINAL`、`Wave_Apply`、`Device_Merged` 等长期目录。也不需要另建 SAEF 独立开发仓库（当前 `SAEF/` 已在统一 GitHub 仓库中）。
