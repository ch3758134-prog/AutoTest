# 受控记录

- `assets.json`: 受控原始文件的来源、身份和 SHA-256。
- `work_products.json`: 55 个稳定工作产品的真实维护路径及状态。
- `project_state.json`: 阶段工作状态与 Gate（唯一状态入口）。
- `gates/*.md`: 人工审查证据入口，未经签署均为候审。
- `decisions/changes/issues/`: ADR、工程变更与问题记录。

Gate 不能由 HTML、普通 ZIP 或脚本检查自动批准。正式基线应绑定 Git 身份、原始资产、审批记录及恢复证据。
