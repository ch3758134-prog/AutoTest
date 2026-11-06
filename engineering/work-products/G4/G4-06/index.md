# G4-06 · 缺陷与受控变更实施

> 状态：DRAFT / 阶段性实施记录，未接受、未完成 G4 Gate 评审。权威状态见 `engineering/control/work_products.json`。

## 软件缺陷修复记录（依 Windows 回执）
- C# 构建：sealed 异常继承、目标类型 `new` 推断、ImmutableArray 搜索、测试遍历委托、UI DelegateCommand、SQLite 与测试项目引用等，已修复并通过 145 项回归报告。
- 按键板 CAN `0x730` 测试向量：修正 20 位掩码对应 8 字节的错误断言，不修改生产编码，依据现役 `KeySim.c`。
- 第二轮集成：Records JSON/ImmutableArray、Wave 时间偏移容差、Application 旧 DBC Revision 诊断，用户报告原 10 项失败关闭。
- WinUSB 增量：对 `UsbSessionService` 连接/超时/重连、`CanFrameCodec` DLC 后非零字节、`CanFrameHub` 丢帧合并的变化已由主对话作专项合入决策；真实协议稳定性仍未物理证明。

## 后续设计澄清（不是本阶段软件已实现能力）
- 当前 20 Key 是可组合电阻网络底层资源，而**非 20 个用户操作按钮**。
- 上层控制器名称、状态名称/数量、操作方式和每一状态对应的 20 位掩码均应由项目资源配置；按键板五路电压测量及目标 ECU 判定窗口是独立证据。
- 操作界面根据用户配置生成，自动测试复用同一控制服务。EPB 的“驻车/无操作/释放”只是示例，绝不能写死为全平台枚举。
- 以上为后续需求/设计细化输入，**未自动修改已冻结 G3/G4 设计正文或签发新公共合同**；需通过受控设计更新落地。

## 尚未关闭风险
单电源电流/电压及线束、主控固件 IGN 初态、按键板供电网络差异、CAN 通道映射、HostArrivalOnly 时间精度、WPF 专业工具可用性、真实 Flash 恢复能力。
