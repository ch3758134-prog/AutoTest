# AutoTest HOST → GitHub：独立分支安全导入指令

## 前提

目标：`https://github.com/ch3758134-prog/AutoTest.git`。远端目前为 **Public**；建议你先在 GitHub 将其改成 Private，因为本项目涉及真实硬件与 ECU 研发源码。未确认公开范围前不要把 SAEF 的固件、原理图、历史备份上传。

本指令不写远端 main，不覆盖本地 `D:\01_Projects\auto\AutoTest_HOST`，不要求 Codex 自主分析。以下操作需要你的 Windows Git 凭据认证，认证由你本人完成；不要向 AI 提供密码或 PAT。

## 文件

下载 `AutoTest_HOST_G4LAB_source_git_bundle_20261108.bundle` 放到 `D:\01_Projects\auto\`。SHA-256：`591998411480a5d9798abf6ed578626bd4baadcb9eae7288d7b0e7baabfa2963`。

## 机械命令（PowerShell）

```powershell
$Bundle = 'D:\01_Projects\auto\AutoTest_HOST_G4LAB_source_git_bundle_20261108.bundle'
$Publish = 'D:\01_Projects\auto\AutoTest_GitHub_Import'

Get-FileHash -Algorithm SHA256 $Bundle

git clone -b upload/host-candidate-20261108 $Bundle $Publish
Set-Location $Publish

git status --short
git rev-parse HEAD
git log --oneline -5

git remote add github https://github.com/ch3758134-prog/AutoTest.git
git ls-remote github refs/heads/main

git push github refs/heads/upload/host-candidate-20261108:refs/heads/import/host-g4lab-20261108

git ls-remote github refs/heads/import/host-g4lab-20261108
```

**停止条件**：文件 Hash 不符、克隆失败、HEAD 不是 `bfd8fb3caf7e1d6ac68ad6f6dd7567c9ed436f4c`、目标目录已存在、认证异常或 push 失败时，立即报告原始错误，不允许 Codex 自主修复、force push、修改 main、清理工作区。

## 导入完成后的工作

只审核分支 `https://github.com/ch3758134-prog/AutoTest/tree/import/host-g4lab-20261108`；**不要自动合入 main**。因为远端 main 是独立 README 历史，与本地 HOST 的 Git 历史不同，需要单独进行受控迁移和版本基线裁决。

这个 Git Bundle 保留上传 HOST 的本地 Git 提交历史（包括 `be08abe`、`1afd178`、`dd65cbd`、`0098b9f`）及上传 ZIP 时未提交的候选源码（导入分支快照 commit `bfd8fb3`）。不包含 obj/bin 生成产物和 `.git` 系统内部文件的文件级复制；Git 历史通过 bundle 真实保留。

SAEF 不在这一份 HOST 导入包中。不要把两套仓库的 Git 历史合并成同一个仓库。