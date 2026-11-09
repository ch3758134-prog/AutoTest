# matrices/UI_COMMAND_CATALOG.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`781ae00ddcc1372a18ee89338e8f93db692390ee591068ca9a8b5b6ab850860c`

原始行数（含表头）：20

| 操作 | 命令 | 所有者 | 界面应显示 | 首次验证 |
| --- | --- | --- | --- | --- |
| 新建工程 | Project.Create | B1 | 工程树出现新项目，Dirty/空态 | Mock保存 |
| 打开工程 | Project.Open | B1 | 失败显示路径与schema错误 | 无设备 |
| 新建资源 | Project.AddAsset | B1 | 资源选择器；建立资源而不是测试步骤 | 无设备 |
| 重命名/删除资源 | Project.EditAsset | B1 | 引用影响预检及撤销支持 | 无设备 |
| 选择工程树资源 | Workbench.SelectAsset | D2 | 右侧属性编辑切换，不改运行状态 | 真WPF |
| 新建测试程序 | Test.New | C1 | 中央文档及空步骤态 | 真WPF |
| 添加测试步骤 | Test.InsertStep | C1 | 流程控制分组 + 其余全量列表 | 真WPF |
| 右键插入/复制/删除 | Test.EditTransaction | C1 | 节点事务及撤销 | 真WPF |
| 编辑步骤属性 | Test.EditProperty | C1 | 即时校验并显示来源 | 真WPF |
| 运行/停止 | Run.Start/Stop | C2 | 活动Run和当前选中分离 | Mock→实机 |
| 暂停/单步/跳过 | Run.RequestControl | C2 | 不可用时说明原因/安全边界 | Mock→分步 |
| 打开设备诊断 | Tool.Bench | A1/A2 | 独立连接状态、原始USB和输出证据 | 真WPF |
| 打开CAN日志 | Tool.CanTrace | A3 | 独立Pane/窗口，可筛选和导出 | Mock CAN |
| 打开UDS工具 | Tool.Uds | E2 | 完整请求响应/会话/负响应 | 模拟ECU |
| 打开波形窗口 | Tool.Wave | E3 | 曲线/参考/比较/时间质量 | 回放 |
| 打开烧录窗口 | Tool.Flash | E4 | Profile/映像/预检/阶段/错误 | 模拟Bootloader |
| 扫码输入 | Production.Scan | E1 | 输入模式状态及候选匹配 | 真扫码 |
| 生产启动 | Production.Start | P1 | 电压/文件/型号预检，明确拒绝原因 | Mock→H4 |
| 历史/统计 | Records.Search | R1 | 按DUT/Run检索，次数与台数分离 | 固定样例 |
