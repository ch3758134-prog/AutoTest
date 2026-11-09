# G4-05 · 系统模块集成

> 状态：DRAFT / 阶段性实施记录，未接受、未完成 G4 Gate 评审。权威状态见 `engineering/control/work_products.json`。

## 已形成的软件集成候选
- 八方向：PROJECT / TEST / UI / DEVICE / UDS+FLASH / WAVE / RECORD / SCAN+PRODUCTION 已进入同一 Windows 解决方案。
- 提交 `1afd178c8c30234363bded731203bbd0556bd885` 是已归档的八方向软件检查点。
- 新一轮 DEVICE 真实 WinUSB/HardwareDebug 的 7 项跟踪修改、12 项新增文件仍属于上传工作树差异，已经由用户报告编译并测试通过，**还需正式 Git 受控提交**。
- 已有独立 HardwareDebug CLI 作为只读 PING / GET / CAN RX B0/B1/B2 入口；WPF GUI 尚需专业化重做，不要求它先通过 UI 验收才能进行 B0/B1。

## 当前集成深度和限制
1. 软件路径接线及跨模块 xUnit 已覆盖；未验证真实 ECU CAN、UDS、Flash 和五路按键电压。
2. 八方向工作台的完整人工操作未完成；已截图的 V1 UI 只是起点，不能判为专业设计验收。
3. DEVICE 的真实 CAN 发送与危险控制默认为禁止，不能因为模拟测试通过直接启用按键、IGN、供电或 ECU 烧录。
4. 物理接线是否能由主控板向按键板/ECU 安全分配单台电源，仍需 B0 电气核对。

## 下一步
建立 WinUSB 源码提交检查点，补原始日志；随后先独立做主控 B0/B1，再从简化台架逐步推进 CAN/按键/DBC/波形/UDS，Flash 按独立恢复保障开放。详细验证范围见 `engineering/evidence/G4/AT-G4-STAGE-20261105/LAB_SCOPE.md`。
