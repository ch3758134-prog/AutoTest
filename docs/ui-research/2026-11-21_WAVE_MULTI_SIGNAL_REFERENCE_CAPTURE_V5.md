# AutoTest V5｜ECU 多信号波形分析与标准波形采集产品方案

日期：2026-11-21｜交付：高保真可交互 HTML｜Gate：UI 设计候选，非真实 ECU 采集系统

## 1. V4 被否决的关键问题

V4 只能选择一个 DBC 信号的独立绘图，缺失多路因果时序观察能力；左侧虽然有“开始记录”一类按钮，但没有真正的“采集标准波形”入口和参考采集流程。将 CSV 直接标为“参考”也缺少录波来源、质量审查与版本管理。

## 2. 对标成熟 ECU 测量分析产品

- Vector CANape：图形测量窗口可容纳多路信号、在多个图表中共享时间观察，记录配置与窗口视图可保存；同时支持在线测量和离线记录分析。参见 https://www.vector.com/en/product/canape/ 和 https://support.vector.com/kb/?id=kb_article_view&sysparm_article=KB0014196 。
- Vector CANalyzer：总线 Trace、Symbolic Signal、Graphics/Measurement Cursor 分工，适用于结合 CAN 原始记录与信号时间历程诊断。参见 https://www.vector.com/en/product/canalyzer/ 。
- ETAS INCA：多路测量、独立 Recorder、并行触发、示波图和光标工具；记录配置不等于判定配置。参见 https://www.etas.com/en/products-services/data-acquisition-processing-tools/software-products/inca-software-products/ 。
- ETAS MDA：面向离线多通道测量分析与大文件、不同量测信号的关联观察。参见 https://www.etas.com/en/products-services/data-acquisition-processing-tools/software-products/measure-data-analyzer-mda/ 。
- ASAM MDF：工业车辆量测数据存储标准，涵盖时间相关测量、通道分组、事件和元数据；V5 离线原型仅实现 CSV 导入，**并未实现 MDF 读写**。参见 https://www.asam.net/standards/detail/mdf/ 。

借鉴的是测量任务和交互能力，**不是复制这些产品的界面**。

## 3. AutoTest 应具备的真实能力

### 多信号同屏

1. 工程 DBC 信号树支持多选、搜索和快捷添加。每个信号具有 ID、CAN ID、单位、离散/数值语义；可自定义不同信号组。
2. 默认 **紧凑总览**，四路或更多波形共享 X 时间轴：`Debug_702.KeyStat`、`Debug_705.ctl_epb_stat`、`Debug_707.Mot_L_current`、`Debug_707.Mot_R_current`。这些名称来自已有绿通 HydEPB DBC，非凭空编造。
3. 视图可切换 **分轨分析** 与 **同单位叠加**，不能将 `A`、`V`、离散状态混合在无单位的单 Y 轴。
4. 统一平移/缩放与 A/B 双游标；离散值使用阶梯线，不应绘制出假的中间状态；需要记录样本过期与真正缺帧区间。
5. 实测与参考在同一信号轨对比，但必须保留两个数据源各自时间参考和质量标签；不会自动以最小 RMSE 时间平移代替时序检测。
6. 可回到原始 CAN 帧、DBC 解码来源、物理单位、采样时间、丢帧统计和 StepOccurrence；这些属于正式 R1 WAVE+RECORD 的真实集成职责。

### 标准波形采集正式流程

工程师点击 **采集标准波形** → 选择合格真实 DUT、型号/HW/SW、工况和测试动作 → 多选信号/DBC 版本 → 设定触发方式、预触发和时窗 → 确认采集订阅已真实就绪 → 发出 TEST/手动台架动作（共享已有工位调度器）→ 真实多路录制 → 停止/归档原始 CAN + 时间戳 + DBC SHA + 质量差异 → 复核波形/采样完整性 → 创建参考**候选** → 明确审核适用型号/动作/单位/修订/阈值 → 冻结获批准参考 → TEST 的波形判定步骤按资源 ID/Revision/SHA 引用。

**不应在“开始采集”后立即创建已批准标准**。需要保留工程参考、待审核参考、已发布参考三个状态，标注真实来源及证据。

### 判定应优先可靠性

- 样本来源、观察时间（Windows 主机接收时间 ≠ ECU 内部事件时间）、订阅就绪、gap/overflow、年龄、有效比例、触发锚点全部检查。
- `StateTransition` 优先判离散状态是否按规定进入和保持；`NumericEnvelope` 判数值阈值/时窗；`ReferenceSimilarity` 用于来源已批准的参考曲线。
- 必须区分 **PASS / FAIL / INCONCLUSIVE / ERROR**，不在数据不足时给 PASS。
- 不内置无依据的 `±5%`、`50ms 对齐`、`95% 覆盖率` 默认生产准则。阈值来自 ECU 实际规格及现场标定。

本部分依据项目研究 `research/r1-wave-judgement-20261120` 分支的 `W-04`～`W-10`，特别是 `W-05_TIME_QUALITY_AND_JUDGEMENT_SEMANTICS`、`W-06_REFERENCE_LIFECYCLE_AND_EVIDENCE_CONTRACT`、`W-09_LIVE_WAVE_TOOL_CAPTURE_WORKBENCH`、`W-10_ACCURACY_ASSURANCE_AND_FIELD_QUALIFICATION`。

## 4. V5 HTML 已实现与不可声称的内容

### 现有可操作

- 默认四轨同屏；从工程 DBC 多选信号（最多 12 条前端轨道，限制仅针对原型 UI）。
- 紧凑总览 / 分轨分析 / 同单位同语义叠加。全轨共享时间轴、时间位置、缩放和双游标。
- 支持宽表 CSV：`time_ms,Debug_702.KeyStat,Debug_705.ctl_epb_stat,Debug_707.Mot_L_current,Debug_707.Mot_R_current`；也支持两列时间与值导入到当前信号。独立的测量与参考文件；拒绝时间倒退/重复时间戳。
- 左侧和右侧均有 **采集标准波形** 可见入口；打开采集设置，指定合格样件编号、工况、触发、预触发和时长。
- 无真实 USB 连接时“开始采集”禁用。用户可导入历史测量 CSV 并保存为仅供审核的参考候选 JSON，包含导入样本与“未核实”的来源标记；不得当作生产标准。
- 缩放与绘图不会更改原文件值；按离散/数值采用阶梯/折线；本地多列 CSV 显示可连续操作。

### 未实现（正式 WPF/R1 专项接线）

- 无实时 USB/CAN 数据、订阅完成事件、全量原始帧/采样质量证明；**不能实际录制标准波形**。
- 无真实审查/批准权限、正式项目资源 SHA 归档、生产判定规则发布；参考候选的 HTML JSON 不是已发布 Reference。
- 未实现 MDF、外部电流电压探头或 ECU 内部高频采样，不可承诺其物理精度。
- 未与 TEST/RECORD 执行器联动，不能输出实际 PASS/FAIL 或回放真实历史运行记录。

## 5. 浏览器回归已验证

1. 从主窗口打开独立波形窗口，默认选中四路 HydEPB 信号；紧凑总览同时显示四个轨道。
2. 点击“采集标准波形”，出现采集设置和四步流程，真实设备未连接时采集不可启动。
3. 导入浏览器测试专用四列 CSV，四个轨道均绘制；“同单位叠加”将两个电流曲线合并一轨，其他离散状态仍分轨。
4. A/B 双游标获得同一时间轴两位置和 Δt；在参考候选页面填写样件、工况和录波窗口后，可导出待审核 JSON。
5. Chrome 浏览器该操作链没有 JavaScript 页面异常。**测试用的人工数据只用于软件交互回归，不在交付工程中冒充 ECU 实测。**

## 6. 现场验收和下一步

用户首先验证多信号是否在同一窗口可看、轨道密度是否合适、标准波形采集流程是否符合真实操作；设计认可后 UI 专项才连接 `R1` 正式 `WaveCaptureService / LiveDbcSignalService / WaveReferenceCatalog / TEST / RECORD`，不能复制第二套采集器或假参考库。首次 R1 实测必须分别证明测量质量、参考版本、安全、断线、触发时序、时间精度与最终证据链。