# G4-04 · 接口契约与仿真集成

> 状态：DRAFT / 阶段性实施记录，未接受、未完成 G4 Gate 评审。权威状态见 `engineering/control/work_products.json`。

## 已实现并参与自动测试的范围
- 公共 Core/Contracts 保持先前候选接口，不以编译通过替代合同冻结。
- HOST 中存在 WinUSB、USB Session、SEQ/CRC/超时/会话代际隔离；CanFrameCodec/Hub、硬件接收桥；Station Mock 和安全只读命令拦截。
- PROJECT：工程/DBC 资源保存与引用检查；TEST：文档/计划编译/模拟步骤执行；RECORD：SQLite 历史；SCAN 生产模拟；WAVE：模拟/回放/比较；UDS/FLASH：Mock 诊断与 Mock Bootloader 等。
- 四方向 V1Workspace 接线及后续专项接线已被 Windows 自动测试覆盖，**但 UI 操作、真机协议和输出电平尚需单独验证**。

## 两种数据状态必须独立
- Mock / Simulated：可以证明软件控制流、序列化、错误处理和局部组件契约。
- Physical / Observed：只有从实物供电/万用表/USB-CAN 原始 Trace/ECU 实际响应取得，才可证明台架通路。

## 尚未关闭的接口验证
真实按键板 CAN 0x730、20 Key 组合及五路电压，ECU CAN 通道、DBC 信号样本时间质量、UDS ISO-TP 真实会话、Flash 编程恢复、断连和异常负载等均需要 B0–B6 分层实测。
