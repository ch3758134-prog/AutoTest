# R1｜工程扫码期望匹配（负责人确认后的整改）

## 现行约束
- 一只 ECU 扫一个完整码，内容可自主设计，不强制 UT1、多个码或软件信息读取。
- 工程先编辑分隔符、有序字段、用途、期望内容，不依赖设备连接或先试扫。
- ModelCode 与 HardwareCode 为精确匹配；DutSerial 为唯一身份；批次、日期和自定义字段默认仅记录，也可设为精确匹配。
- 每个工程声明其适用型号及硬件版本。不同硬件版本确实需要不同 HEX/DBC/TEST 时，分别维护各自完整工程配置。当前 R1 对已部署工程做匹配校验，不擅自跨工程搜索、切换或拼装资源。
- 工程的原生烧录、TEST、DBC、WAVE 与工位配置继续由工程引用和冻结；扫码期望不重复选择这些文件。
- COM、连接、设备绑定、真实接收诊断留在独立扫码枪设备窗口。旧工位全局扫码规则、多级 Recipe 源码为历史兼容，不进入本次正式扫码预检链。
- 无扫码仍可工程开发/调试；开发状态下试扫不会进入正式生产身份准备。

## 源码链路
ProjectBarcodeExpectation → ProjectDefinition/ProjectRepository → 工程检测准备扫码匹配编辑器/统一自动保存 → StationDeploymentStore 的 manifest SHA → ProductionBarcodePreflight.InspectExpected → AutomaticTestHomeViewModel.Scanner → 原有 ProductionPreparationService 资源冻结及 FrozenScanIdentity。

工程期望保存在 manifest 内，随工程复制。编辑改变工程修订和指纹，正式生产需重新部署。旧工程若无期望仍能编辑，但不能走正式部署和预检，不猜测字段。校验失败显示字段名、期望值、实际值或字段数错误。预览不会给出生产授权；生产仅消费真实串口输入。规则快照序列化及 SHA、原始码、各字段随真实扫码观察冻结，尚未接到正式 RECORD 持久化。

## 操作
开发与调试 → 检测准备 → 扫码匹配。默认展示五行（型号、硬件版本、SN、批次、日期），填写期望并可添加自定义字段、移动、删除非必需行；自动保存或显式保存。可选验证使用人工文本或最近的真实码，不依赖 COM。设备窗口独立设置。保存后重新部署工程，生产按完整期望核对实际码。

示例（非强制格式）`HYDEPB|EP03|00001234|LOT01|20261123`。型号与版本不符均拒绝；保留 SN 前导零。禁止把失败匹配当作新 DUT 或改换 HEX。

## 核验范围
新增 ProjectBarcodeExpectationTests 覆盖单码多字段、型号/HW 不符、缺失/多余字段、空 SN、自定义匹配、旧工程缺期望、工程存储往返、必需字段规则。源码回读与接口检查，不执行 Windows 编译、CI 或实体测试。旧全局/Recipe 研究仍可查阅，非新生产合同。
正式 START、实体夹具、FLASH→全量 TEST→RECORD 的整机执行仍有已知 P0；本次只整改扫码配置及实际预检链，不宣称整机 PASS。
