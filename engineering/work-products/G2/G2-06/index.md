# G2-06｜工程资源、DUT 身份、测试快照和证据（架构候审 v0.7）

## 六个独立数据域

| 数据对象 | 写入者 | 时机 | 关键不变量 |
|---|---|---|---|
| ProjectDefinition | B-PROJECT | 用户编辑/保存 | 资源身份与引用清晰，不等于工位配置 |
| StationProfile | A-STATION/配置管理 | 工位配线及板卡配置变化 | 12/24V要求不是软件自动切换 |
| DutProfile/IdentityRules | B-PROJECT/E-SCAN | 编号解析与资源关联编辑 | HOST 不生成实物条码；规则有修订身份 |
| TestDocument/PreparedPlan | C-AUTHOR/C-RUNTIME | 编辑/编译 | 可变文档与不可变计划隔离 |
| RunSnapshot/RunFacts | C-RUNTIME | 启动/执行 | 同 DUT 重测有独立 RunId，身份与资产快照不被覆盖 |
| RunRecord/Statistics | R-RECORD | 事实追加/提交/查询 | 不完整记录可追踪；统计去重基于 DutIdentity 而非 RunId |

## 实例：同一个 DUT 两次测试

扫描 `ECU-A` → 唯一匹配/预检 → `Run-001` 得 FAIL → 记录提交；同一实物合法复测，生成 `Run-002` 得 PASS；扫描另一个 `ECU-B`，`Run-003` 得 PASS。统计：**3 次 Run、2 个不同 DUT**。`Run-001` 不能被 `Run-002` 覆盖。

## 一次操作与记录链

扫码事实（可仅作为候选）→ 规则匹配结果 → 工位与文件预检 → 冻结资产和 DUT 身份 → 烧录Job/测试Run → 实际观察与判定 → 追加记录 → 成功提交后参与相应统计。

这里的“扫描→烧录→测试”是 H4 的**候选生产使用示例**，不是 G2 要求永远固定该顺序。测试脚本中可能有烧录步骤、生产协调也可能有受控工作流；关键是同一专业服务不被复制实现。

## 资源和数据一致性失败

缺少资源 → 工程验证错误；匹配歧义 → 不选择默认项；版本变动 → 旧预检作废；旧 Run 不重写；记录提交失败 → Incomplete；真实状态未知 → UNKNOWN，不能通过 UI 推断。

## G3 接收

选择 JSON/SQLite/文件布局等实现并设计迁移、事务及校验方法；保持上述身份、版本、运行冻结与追加记录不变量。DBC/HEX、扫描规则和波形资源的实际编辑体验在真 WPF 试用时调整。
