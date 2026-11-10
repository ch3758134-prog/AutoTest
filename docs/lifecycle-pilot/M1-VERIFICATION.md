# M1 验证记录｜KEY_SIM 实体链路（2026-11-09）

> 状态：**PARTIAL PASS / NOT YET END-TO-END VERIFIED**。证据来源：通过已授权 GitHub 连接读取 `main@4530aba0079e8b2344e4ff5e706049450458a45b` 的真实文件；本地用同构临时夹具运行 M1 单测。尚未在用户 Windows 的整仓真实检出上运行 M1，尚未有 B4 物理电压验收。

## 真实源码定位

- `AT-SYS-011`：`SAEF/engineering/work-products/G1/G1-03/index.md`，已经约束“经验证范围内”模拟输入，且不允许将电阻网络视为任意 DAC。
- `IF-03` / `IF-06`：`SAEF/engineering/work-products/G1/G1-05/index.md`，区分主控→按键板 CAN 与按键板→DUT 物理电阻输入。
- `G2-01`：`SAEF/engineering/work-products/G2/G2-01/index.md`，写明 20 键/五路电阻输出。
- `ICD-03`：`SAEF/engineering/work-products/G2/G2-05/index.md`，覆盖台架输出与主控/按键固件边界。
- `G3-A2`：`SAEF/engineering/work-products/G3/G3-02/index.md`，包括 `KeypadCommandCodec` / `KeypadControlService` 的设计文件责任。
- `src/AutoTest.Devices/Station/KeypadCommandCodec.cs`：`KeypadId=0x730`，CAN 帧含 `0x01` + 20-bit mask；`EncodeReleaseAll` 使用 `0x80`。
- `src/AutoTest.Devices/Station/KeypadControlService.cs`：`SetMaskAsync` 只验证编码，返回 `BENCH_NOT_AUTHORIZED`，**并未通过此服务发送真实输出**。
- `tests/AutoTest.Devices.Tests/UsbCanProtocolTests.cs`：`Verify_A2_T04` 检查 mask、release-all 编码，**不是 5 路电压或 ECU 行为的物理验收**。

## 本轮证据结果

| 项 | 结果 | 限制 |
|---|---|---|
| 9 个引用对象在 main 中的文件/锚点定位 | **9/9 找到** | 只证明字符串存在，不证明语义关联全部准确 |
| 当前候选关系图 | 12 个节点、13 条关系 | 部分边显式标记 `review_required` |
| 从现场反馈向下的候选影响 | 10 个可达对象 | `POTENTIALLY_IMPACTED`，不能视为 10 项全需修改 |
| Python 内核单测 | **14/14 PASS** | 在自动生成的本地测试夹具上运行，不是在用户 Windows 整仓上 |
| 工作包 `WP-KEYSIM-001` | **NOT_READY（设计上预期）** | 未经负责人批准、未固定经确认的新设计修订、B4 原始实测缺失、CR 未关闭 |
| 真实电压与 ECU 识别 | **NOT_VERIFIED** | 必须有实体台架及版本确定的安全条件和原始证据 |
| 历史 SAEF 批准快照 | **保留未更改** | M1 只读 |

## 具体的反向设计结论

不应因为现场澄清就宣称 `AT-SYS-011` 和 `G2-01` 错误：两份文件已认识到电阻网络/5 路输出。应优先检查 `G3-A2` 的组合保持语义、实现的实际控制能力与 B4 验证计划，**仅在证据表明上层语义确有缺口时才更新 L1/L2**。这恰恰验证了“受控影响审查”比一遇到新事实就全量回退 Gate 更合理。

## 下一步准入

1. 在 Windows 真实整仓运行 `python tools/lifecycle/lifecycle.py --repo . inspect`、`impact`、`readiness` 及 `unittest`；保留原始输出。
2. 继续核查按键协议与实际硬件组合行为，补齐详细设计和接口之间的真实关系证据。
3. 只有在确认候选关系与状态定义可用后，进入最小本地可视化实现；不会自动修改 SAEF/产品源码。
