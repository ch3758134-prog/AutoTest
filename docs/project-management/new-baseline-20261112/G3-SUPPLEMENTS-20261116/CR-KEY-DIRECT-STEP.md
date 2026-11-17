# AutoTest｜G3 补充变更：取消独立按键配置、直接 KEY_SIM 步骤

**正式管理标识**：`PM-CR-20261116-KEY`。  
**状态**：负责人已批准产品设计；旧工程迁移、真实设备能力与 G4 运行验收尚未完成。  
**关联**：`PM-CR-20261116-PF`、主控板 USB/夹具接口补充。

## 1. 已批准的用户工作方式

移除常规用户侧独立的“按键配置/按键动作配置”工作流。测试人员无需先创建 Controller、输入状态、按下态和恢复态，便可在**测试用例或公共流程**中直接添加 KEY_SIM 控制步骤，勾选 K01～K20 任意组合、设置保持时间并连续编排。也不以建立公共流程为前提；**手动台架依然要能创建、编辑、保存、调用连续 KEY 动作**，不可因为取消“独立按键配置”而取消台架的动作序列编辑。

硬件仍为 20 位组合开关经电阻网络合成 5 路模拟电压条件；**20 个 K 不是 20 个 ECU 业务功能按钮，更不是五路任意电压 DAC**。具体 ECU 业务语义由测试程序定义，不预设驻车/释放。用户允许勾选 20 个 K 不等于任意组合均已通过硬件安全验证。

## 2. TEST 步骤合同

建议使用单一 `KeySet` 节点，属性 `mask`（20-bit `0x00000..0xFFFFF`，K01=bit0、K20=bit19）、`holdMs`（非负、上限经 TEST/DEVICE 审查）、`after=Keep|ReleaseAll`。此前 G3 审查方案建议默认 `Keep`，避免隐式清零；上位机必须在 Inspector 明确展示真实选择，不能把当前旧 `Pulse` 的隐式全释放套进新步骤。

- `KeySet(mask)` 是**设置完整目标掩码**（替换上次完整组合），不是 OR 并集；`mask=0` 表示明确清空；`holdMs` 是输出后的等待，不自动清空。
- `after=Keep`：等待后仍保持该组合，直到下一条已授权操作；`after=ReleaseAll`：等待后再明确发出清空动作。
- 连续示例：`KEY(K01+K04)` → Wait → `KEY(K20)` → `KEY(0)`；步骤保持/切换/结束必须有可见状态。
- 每个 KEY 步骤适用同一 Station Lease、授权、超时、错误、取消、UNKNOWN 与运行证据规则；在公共流程中调用也不能获得额外权限。
- 兼容旧 RawKey/Controller/Button 的状态所有权须明确定义；未经等价证明时旧新混用**阻断而不是去掉检测**。不得把一次主控 CAN ACK 当作五路电压已到位或 DUT 已识别。

## 3. DEVICE 物理实现边界

现有 `KeypadCommandCodec.EncodeMask`：CAN0 经典帧 ID `0x730`，Payload `[0x01, mask低8, mask中8, mask高4,0,0,0,0]`；`EncodeReleaseAll` 使用起始 `0x80`。历史 `RawKeyAction` 支持 `Pulse/Hold/Release`，而 `ConfiguredStationActionPort` 的旧 Pulse 会在延时后发送 ReleaseAll，且原始 KEY 与已配置 Button 模式互斥。新模式不得依赖旧 Pulse 自动释放语义。

必须由 DEVICE 对照 KEY_SIM 真实固件与原理图确认完整 mask 写入、保持、下一次组合替换、清空、固件版本、非法组合限制、CAN ACK/断连后的状态以及五路电压实测。写动作必须经统一物理授权与错误传播；不得因删 UI 配置入口删除安全 Fence 或电路约束。若支持掩码、ForbiddenPairs 或硬件身份未知，可保存编辑草稿但**不得实际输出**。

**特别注意**：主控 USB 连接失败、KEY_SIM 未接入、CAN0 通路或模拟电压未经证实，都不能被 Step UI 的“可编辑”状态冒充“可运行”。

## 4. 旧工程与资源的处理

| 旧对象/位置 | 目标处置 |
|---|---|
| `ControllerKind` / `EcuControlProfile` / ControllerState | 新工程不再创建用户按键配置；旧工程继续可读、可见历史来源；迁移前不删除真实序列化 |
| TCF `ButtonAssetId/Revision/Fingerprint` | 保留旧格式读取；新 KeySet 不以旧字段为前置；保留失效引用报错，不隐式篡改 |
| `TestTargetKind.Button` + `Write(Pulse/Hold/Release)` | 原文保留旧语义；需显式迁移和映射审查，未经等价确认不自动转换 |
| `TestTargetKind.RawKey` + `RawKeyAction` | 可读取、按旧语义解释；旧 Pulse 与新 after 不一定等价 |
| TEST v1 JSON / Manifest / 引用资产 | 旧文件不静默覆盖；新格式按版本升级；迁移先预览差异、备份、可回退，不确定即 BLOCKED |

**禁止**把旧工程打开后自动丢掉 Controller、TCF、RawKey、测试引用；旧用户资源是否能安全彻底废弃，要等兼容与真实工程回归后才能决定。新旧配置界面不得并存为两个用户日常工作流。

## 5. G1/G2/G3 影响和实施责任

- P0：产品定义不改。
- G1：定点补充 REQ-09/12/13/16/24/27/28/30，保留真实硬件外部刺激与安全约束。
- G2：复用同一 TEST 模型/Station 实体端口，不新增独立用户按钮配置子系统。
- G3：工程树移除旧按键配置常规入口，TEST Inspector 提供 K01~K20、保持时间与结束动作；手动台架继续有可保存的 KEY 动作序列；旧资源保留受控读取。冻结 IDE 布局不重画。
- TEST/DEVICE：`G4-KEY-01` 新步骤/参数/运行/硬件策略和全掩码切换；UI/INTEGRATION：`G4-KEY-02` 真正 Inspector/台架编辑和保存；PROJECT/TEST：`G4-COMPAT-01` 历史资产迁移与阻断规则。
- G4 所有实现须取正式合同、实际设备固件 SHA 和用户实测反馈；不能自造独立 USB 协议或模拟设备 PASS。

## 6. 验收

- 无 Controller 资源的新工程可直接插入 KEY 步骤；选 K01+K04+K20 后掩码为 `0x80009`，保存重开准确；Ctrl+Z/Y、复制/插入、右侧属性均真实有效。
- 公共流程和测试用例内 KEY 行为一致；手动台架能保存一串 KEY/Wait/KEY/清空操作，不被公共流程实现阻塞。
- 旧工程继续可读、可预览迁移，找不到资源/旧新冲突/无法等价转译时绝不覆盖原件。
- 真机使用正确 CAN0、KEY_SIM 0x730，逐项测完整掩码、替换、保持、清空、取消/断开/UNKNOWN；硬件未连接或能力未核准时应阻断执行，不能以 UI/Mock 结果验收。

本记录只确认产品及技术设计边界；实际固件/Windows 构建/设备输出按真实证据独立验收。
