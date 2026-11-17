# AutoTest｜项目管理档案导航

**管理唯一写入分支：`Project_Management`。** 当前仓库 `main` 属于 HOST 软件正式集成，不用于新增正式项目管理工作产品。

## 现行新基线资料

- [2026-11-12 新基线 P0/G0/G1 已归档材料](new-baseline-20261112/)
- [2026-11-16 G3 增量变更、公共流程/KEY_SIM 与主控 USB/双夹具交接](new-baseline-20261112/G3-SUPPLEMENTS-20261116/README.md)
- [已批准 P 阶段汇总和审批记录](approved-20261113/)

## 正在整理的缺口

`main` 当前还保留 G2/G3 正式批准文件、冻结 UI HTML 与新版硬件档案，这些尚未全部集中迁移到 `Project_Management`。**本导航不假装资料已经完整迁移**；各项事实以精确 Git SHA 和相应审批记录为准。历史 SAEF 仍在 `SAEF/`，不能与新基线 Gate 状态混写。

## 治理

管理工具本体位于 `tools/project-control-v2/`；普通管理系统源码直接在本分支更新，正式工程成果在对话提交供负责人批准后再写入本分支，无 PM 交付分支或管理 PR。角色、资料与决策/实测证据分开登记。
