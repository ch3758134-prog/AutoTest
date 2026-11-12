# AutoTest｜旧分支清退与 main 基线记录（2026-11-11）

## 唯一正式分支

- `main`：保留当前独立整理的后端领域源码与最小 WPF Shell。此记录前 main HEAD 为 `a0848643a72434e55e4a156d5adc58969df2a11f`。
- **不将**未实测的 R1 UI、旧 G4/V1 演示链路、Mock 测试候选混入 main。
- 主线源码清理范围详见 `docs/engineering/MAIN_CLEANUP_SCOPE_20261112.md`；当前 main 不代表完整 R1 已交付。
- 本次 Git 管理清理不运行 Build/Test/CI，不修改设备/DBC/TEST 等业务源码。用户本地后续验证后，再明确批准接入真实功能。

## 历史恢复点（不加入 main 的代码）

Git 对象：`8081921f18b593d0df2c6a3df3623246499032f2`。
这是一个**22 个父提交、树与清理前 main 一致**的独立归档提交，第一个父提交为 main，另 21 个父提交为以下分支 HEAD。它**不是要合入 main 的提交**。

预定唯一归档 Git tag：`archive/retired-branches-20261111`。只有 GitHub 上能读取这个 tag 且 SHA 与上述对象一致时，才允许清除旧远端分支。

| 待清退分支 | 退役前 HEAD |
| --- | --- |
| `cleanup/main-remove-legacy-labs-20261112` | `b90c15b9f153ba526d11776304c1123071b02790` |
| `feature/cantool-flash-lab-20261110` | `e5fcb64f81982652e8ab275c5d203b3091c31fed` |
| `feature/r1-editor-layout-real-sample-20261111` | `3e8babbd9b89ad6bcc70768a6e691858b007d126` |
| `feature/r1-full-product-ui-system-20261111` | `628a1a014573804a43f94796f4ede4b9742c3bb1` |
| `feature/r1-html-product-ui-20261110` | `a8779a685b6f2c70aa4ff8d0aa0716bf14b76e9a` |
| `feature/r1-ide-final-integration-20261112` | `915dcd33a5c9855e08e81fcfd827de39c01a82ae` |
| `feature/r1-lightblue-ide-full-redesign-20261111` | `004ffbd4ade6189da07c589bc6c546b765d4eb0d` |
| `feature/r1-manual-bench-action-editor-20261111` | `f178e78dc52ed445041c61f8dfc5c92593cccd3a` |
| `feature/r1-test-editor-direct-connect-20261111` | `a82940b9ba30506ec0c52fcf38de403b29073488` |
| `feature/r1-ui-sample-startup-overhaul-20261112` | `e75cf87835f5c01c7adcd0b37954e2d192c2022f` |
| `feature/r1-unified-ide-shell-rebuild-20261111` | `d2bae009be566575e8df6220022702ab9158353c` |
| `feature/r1-v6-selective-testcases-20261110` | `59867d5a52f2d58e1bd536c93d16c58b5a01d348` |
| `feature/real-hardware-studio-20261109` | `45d5cce40c7cdc1c45f506e8e9b57d61f0ce71c3` |
| `maintenance/archive-retire-legacy-20261109` | `162a6924436c4001d172517c63b46f108713c938` |
| `management/evolutionary-lifecycle-pilot-20261109` | `f3859ffcb1e0bb52ed6f9a8aefaa01d1d5c141cf` |
| `management/project-control-clean-20261111` | `76ae0185e199bbccc09e439980e037511ea714af` |
| `management/project-control-portable-init-20261111` | `6ae2d087f41a9988071dc40d0021aeb20eb101ed` |
| `management/project-control-rebuild-20261111` | `76622b8ab6974cd0a29126c30a7b2e2ad1de21fc` |
| `management/project-control-redesign-20261111` | `7bc0aa27fd63cbcecba2c2da06e5ed1a948e1c49` |
| `recovery/product-led-baseline-20261109` | `e006ec71168ab28c5228d2cee007026a3b81c103` |
| `ui/foundation-gallery-20261109` | `2808bdd7c8c4b23c154cf9cb891cdaa900fdce8a` |

## 退役 PR

以下历史 Draft PR 不合并：#21、#26、#27、#29、#30、#31、#32、#36、#38、#39、#40、#41。关闭 PR 不等于将代码吸收入 main，PR 的历史讨论继续在 GitHub 可查。

## 恢复方式

需要恢复某个历史分支时，从上表的完整 HEAD 创建**临时检查点**；不要直接把其内容合并回 main。单一 archive tag 保留整个旧分支提交图（归档提交拥有所有旧 HEAD 作为父提交），且不在 main 代码树中新增旧模块。

## 当期保留资产与明确限制

保留 `SAEF/` 工程记录、`tools/project-control-v2/` 管理系统资产、`docs/` 以及当前后端源码；本次“清理旧代码”优先保留历史文档和独立资产，避免误删尚未迁出的唯一副本。是否拆分这些**非当前运行时**文件，需要之后单独明确迁移目标，不在此次 Git 退役时不可逆删除。
