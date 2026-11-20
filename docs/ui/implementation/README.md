# AutoTest｜全新纯 UI 设计实施交付

此目录为 feature/ui-product-rebuild-20261119 的逐批档案。完整流程采用 P-01~P-03、A-01~A-02、B-01~B-05、C-01~C-07、D-01~D-03、E-01，共 **21** 个交付工作包（不是旧文案误写的 18 个）。

- 设计与需求追溯：ui/ide-design 分支 R01~R06、UX_BATCH_EXECUTION_PLAN_20261119.md。
- 实现：全新 HOST/src/AutoTest.Desktop 项目，纯 UI 会话；旧 Desktop 视图、旧 UI 测试项目已从本候选树删除。
- 总验收：只允许在 Windows 真机完成，不能因为本静态记录而标 PASS。
- 未接线：PROJECT、TEST 执行器、DEVICE、UDS、Flash、Wave 实体服务以及报告/生产档案。
- 纯 UI 手工数据须清晰标记为非物理证据。禁止假 PASS/ACK/波形/运行日志。
- 用户决定：全部批次代码完成后再统一体验；无逐批用户审批阻断。
