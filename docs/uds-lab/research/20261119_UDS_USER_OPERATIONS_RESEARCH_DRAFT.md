# AutoTest UDS 专业工具：成熟产品用户操作研究阶段档案

日期：2026-11-19
状态：RESEARCH_DRAFT；待用户审查；未冻结界面与代码
依据：厂商官方手册/产品页、ASAM ODX 标准、UDS/ISO-TP 公开技术文档与 UDS Lab 专业分支静态回读。未安装实操商业软件，也未执行真实 ECU 测试。

## 一、研究的问题与准则

不是“UDS 窗口有多少按钮”，而是“用户需要完成什么、如何完成、为什么成熟产品这样设计、依赖哪些数据与通信能力”。先研究用户操作，再研究对象合同，最后设计交互和 WPF。没有完整数据描述时，不能伪造 DID 语义；没有真实 ECU 响应时，不能显示模拟成功。

## 二、成熟产品官方证据

1. Softing DTS.monaco（2026-11-13）：ODX 驱动诊断服务与参数化解码；开发阶段 ODX 可能不全，所以保留独立 Hex Service，支持输入原始请求和检查故意异常参数。另将 Diagnostic Services、Service Table（单次/周期）、Symbolic Trace、Bus Trace、Logical Link、OTX（复杂过程）分为不同专业控件。设计推论：结构化与自由 HEX 必须同时存在，功能分层不能互相阻断。
资料：https://automotive.softing.com/service/automotive-blog/ecu-diagnostics-beyond-odx-using-softing-dtsmonaco.html
资料：https://automotive.softing.com/products/softing-dts/softing-dtsmonaco.html

2. Intrepid Vehicle Spy 3：Diagnostics Setup 允许 + 建 Job、直接改名字、设地址/子功能/DID、预览帧并 Launch；Diagnostics View 显示状态、结果/Signals/诊断消息历史。File 保存工程配置（包含诊断 Job），数据保存另行处理。脚本还可 Start、Wait、Stop、Save、Call 已保存 Job。设计推论：编辑原操作、执行和以后复用并不冲突；配置与实测证据不是一个对象。
资料：https://docs.intrepidcs.com/vspy-3-documentation/vehicle-spy-main-menus/main-menu-spy-networks/diagnostics/diagnostics-setup
资料：https://docs.intrepidcs.com/vspy-3-documentation/vehicle-spy-main-menus/main-menu-spy-networks/diagnostics/diagnostics-view
资料：https://docs.intrepidcs.com/vspy-3-documentation/basic-operation-of-vehicle-spy/create-open-and-save-vehicle-spy-setups

3. Vector CANoe / Indigo：无具体 ODX/CDD 也可使用 Basic Diagnostic Description 自定义服务参数与数据类型；Indigo Diagnostic Console 手动执行诊断服务，Script Recorder/Runner 负责重复过程。设计推论：不能因无 ODX 就禁止自由调试，但 Basic Description 不等于完整 ECU 数据库。
资料：https://www.vector.com/zh/business-unit/testing-validation-diagnostics/resources/canoe-quick-tip-diagnostic-ecu-simulation-test-the-tester-gateway/
资料：https://www.vector.com/en/product/indigo/

4. PEAK PCAN-Explorer 7.2（2026-10-14，Beta）：UDS Transmitter 在 ISO-TP 上收发单/多帧 UDS；UDS Downloader 在响应反馈基础上执行序列；常见服务有模板，可自定义。设计推论：人工连续调试与自动序列必须按不同操作合同实现。产品概览不足以确认具体右键/快捷键细节，不能编造。
资料：https://www.peak-system.com/products/software/pcan-explorer-7/pcan-explorer-7-highlights/

5. tracetronic ecu.test：从 ODX/PDX 引入 DIAG-SERVICE 作为自动测试步骤；这是正式 TEST 集成的参考，不应当成为独立 UDS 调试器的唯一操作入口。
资料：https://docs.tracetronic.com/help/ecu.test/2026.2/en/Main-program/Action-window/Diagnostics-tab.html

6. ASAM MCD-2 D：ODX 描述服务、请求/响应、DTC、参数、通信、编程和 ECU 专有数据，允许跨研发、测试、生产复用。内置标准 SID 模板不能凭空推出某台 ECU 的 DID 含义和长度。
资料：https://www.asam.net/standards/detail/mcd-2-d/

## 三、面向用户操作的能力地图

A. ECU 连接：选择 ECU/Profile、填写真实 CAN ID、寻址格式、P2/P2*；分清 USB/主控在线、CAN 可发、ECU 已回应、ECU 诊断会话有效。

B. 自由请求：选择标准服务和真实参数、直接输入任意待审的 UDS HEX、查看实际 PDU；编辑/发送/重发无须先存文件。

C. 保存的操作：新建、命名/分组、选择、编辑原操作、保存修改、另存/复制/删除/排序/搜索；重新打开工程后继续编辑；稳定 OperationId，未保存状态明确。

D. 实际交换：每次发送形成独立不可变快照；按 UDS 正响应、NRC、超时、ISO-TP 错误、CAN TX 被拒、用户取消分类；可从历史回填为新草稿，继续修改重发。

E. 服务专用参数：0x22 可以多 DID，但无长度定义不能推断返回边界；0x19 不同子功能需要不同参数；0x10 需要真实 ECU 会话确认；0x31 启动/停止/结果区别；0x27/0x2E/0x2F/0x14 等属权限与物理副作用专项。

F. ECU 专有诊断知识：编辑 DID、DTC、RoutineID、字段/类型/大小端/比例/单位/枚举，标记 ECU 型号、版本、来源；ODX/PDX 导入为后续独立深度研究。

G. 交互追踪与观察：按 CorrelationId 从 UDS 到 ISO-TP 到 CAN/USB 回查；周期轮询与 ECU 0x2A 主动周期报文是不同机制；连续人工交互不等于自动测试序列。

H. 正式自动化：在原有 TEST DSL/执行器下复用诊断资源，不建立第二个 TEST 运行器；FLASH 保留专项 OEM 业务合同。

## 四、必须先定义的软件对象与交互合同（研究推论）

- DiagnosticProfile：通信配置与 ECU 身份。
- ServiceDefinition：SID、请求参数、编码、解码、风险。
- DiagnosticOperation：稳定 ID、名称、参数值、引用及其修订。
- OperationDraft：当前编辑值，可在保存前发送。
- DiagnosticExchange：真正发出的不可变请求快照及真实响应、NRC、质量、证据与时间。
- DiagnosticSessionState：USB/主控、CAN 租约、ECU 会话/安全状态互相独立。
- DiagnosticWorkspace：Profile、定义、操作集合的文件；诊断证据可独立归档。

保存已选操作应更新原操作；另存必须生成新 ID；Ctrl+S 的文件写盘行为需明确。模板与原始 HEX 切换不能静默覆盖修改。编辑时执行取得当前草稿快照；用户继续编辑不能影响在途的 UDS 请求。

## 五、AutoTest 已有事实与能力缺口

静态代码基线：feature/uds-lab-20261118，HEAD 2c977b8cc32978281927c55d8b8339d627b99c07。

已有：UdsOperationTemplates、DiagnosticWorkspace、MainWindow.Operations、UdsLabHardwareSession 以及共享 UDS/ISO-TP 和设备链路。
不足：服务参数仍大多使用简化通用 HEX 结构；完整 DID/DTC/例程语义与 ODX 定义不全；Windows 用户交互、文件回读与实体 ECU 未证明通过。
关键阻断：真实 CAN TX 受 PhysicalCommandFence 逐帧人工输入约束，影响多帧 ISO-TP 时序，不能把“顺序按钮”当自动通信已实现；实体 SID 当前只开放 0x19/0x22/0x23/0x24 只读路径，其余会话、控制、复位、写入、例程等必须经 DEVICE/项目管理审查。不可通过自由 HEX 绕开。FLASH 不由独立 UDS 工具擅自重写。

## 六、待批准的研究路线

A：冻结用户能力与典型任务的操作合同。
B：逐项深研 0x22、0x19、自由 HEX、可编辑操作、响应历史以及诊断定义，形成 UI 状态原型和行为验收。
C：DEVICE 与 PM 单独审查操作级实体授权、ISO-TP 时序、会话和副作用，之后再逐步开放高风险服务。
D：WPF 正式实现、Codex Windows 编译启动、用户真实 ECU 实测；适时引入 ODX 与正式 TEST 资源引用。

当前不可宣称商业工具完整复刻、真实 ECU FIELD_PASS、完整 ODX 支持或连续免干预通信。此文件为独立研究阶段留档，尚未冻结 UI，也不授权改动协议与实体发送限制。


## 七、追加研究：一个用例连续调用多个 UDS 服务（用户 2026-11-19 澄清）

**修正首要可编辑对象**：用户编辑和长期保存的“用例”不只是一条 SID 请求，也不是多条独立收藏，而是一组能连续执行、获取信息并与 ECU 交互的有序步骤。单个 UDS 服务调用只是用例中的一项操作。

### 候选对象合同（尚未批准）

- DiagnosticCase：稳定用例 ID、名称、修订、排序的步骤树、所属 ECU 诊断配置。
- DiagnosticStep：一次 UDS 请求、等待、提取/赋值、判断、人工确认或相关子流程调用。
- ServiceDefinition：SID、子功能、参数编码、响应解码以及 ECU 专有定义。
- DiagnosticExecution / StepOccurrence：具体运行中每步实际发送的 PDU、最终响应/NRC、时间、变量、证据。修改用例不能篡改旧运行证据。

### 用户实际应能进行的操作

创建用例并连续添加多个服务步骤；逐步编辑 DID、子功能、请求 HEX、期望响应、数据解析；插入/复制/移动/删除、撤销、保存、重新打开。连接 ECU 后可运行当前一步、执行下一步、从选中步骤继续、运行整个用例；执行中查看每步真实反馈，必要时暂停编辑尚未执行的步骤、重试或终止。响应提取的变量可以作为后续步骤的参数，但必须具有类型、来源和有效期，不能默默沿用旧连接中的数据。人工连续诊断、受限自动顺序执行和有人机暂停的半自动执行都应能够从同一个用例定义派生，但执行方式与权限不能混为一谈。

示例：读取 ECU 身份（0x22）→ 读取 DTC（0x19）→ 根据实测结果确定下一步 → 按明确授权切换扩展会话（0x10）→ 读取扩展会话的 DID（0x22）。当前 UDS Lab 实体通路尚未允许 0x10，示例仅为未来目标能力，不表示已实现或能直接对真实 ECU 执行。

### 成熟产品证据

- Vehicle Spy Function Block：按顺序执行命令、等待条件、变量控制与诊断 Job 调用。
  https://docs.intrepidcs.com/vspy-3-documentation/vehicle-spy-main-menus/main-menu-scripting-and-automation/function-blocks/function-blocks-types/script-type-function-block
  https://docs.intrepidcs.com/vspy-3-documentation/vehicle-spy-main-menus/main-menu-scripting-and-automation/function-blocks/function-blocks-types/script-type-function-block-commands/script-type-function-block-command-diag-job-action
- Softing DTS.monaco：OTX 支持在工作区集成交互式、部分自动化诊断序列。
  https://automotive.softing.com/service/blog/design-options-in-softing-dtsmonaco-interactively-and-individually-designable-user-interfaces.html
- PEAK PCAN-Explorer 7.2：UDS Downloader 可以依据 ECU 响应串行执行多个 UDS 请求。
  https://www.peak-system.com/products/software/pcan-explorer-7/pcan-explorer-7-highlights/
- ecu.test：诊断步骤归入正式可执行测试包，与原始协议服务定义分离。
  https://docs.tracetronic.com/help/ecu.test/2026.2/en/Main-program/Action-window/Diagnostics-tab.html

### AutoTest 的架构边界

UDS 工具应该提供面向诊断的轻量用例编辑与执行/调试操作，但不可由此引入与正式 TEST DSL 并存且不一致的第二套测试执行器。多服务用例最终应复用现有 TEST 公共步骤/执行合同、共享 UDS/ISO-TP/DEVICE 通路和可审计的运行证据；如当前 TEST 尚未接入 UDS，应先把差异列为研究任务，不冒充已有真实执行能力。DEVICE 逐帧人工实体授权会阻碍多帧及自动连续操作，且 0x10/0x3E/写入/例程等仍受审批限制，必须单独研究真实硬件事务授权与会话状态。

**结论更新**：由“单条可编辑诊断请求”提升为“多 UDS 服务、可保存、可持续调试的诊断用例”。下一轮先研究用例编辑、服务调用、运行模式、变量传递与证据模型，再考虑 UI 布局。未批准改动 WPF 或实体协议门禁。
