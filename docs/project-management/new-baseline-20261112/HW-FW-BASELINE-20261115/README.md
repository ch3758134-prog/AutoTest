# AutoTest｜HW/FW 物理构型与上位机能力边界管理档案

**档案 ID** HW-FW-BASELINE-01 · **建立** 2026-11-15 · **状态** `RESEARCH_ARCHIVED / NOT_FIELD_VERIFIED / CHANGE_NOT_AUTHORIZED`。
归属：AutoTest 新基线 P0→G4 的跨域研究与构型管理；**不是**当前工位的合格发布记录，也不提升 G3/G4 实体授权。关联 [Issue #54](https://github.com/ch3758134-prog/AutoTest/issues/54)。

## 首次建立了什么
本档案保存两份原始可核验 PDF、主控 63 条已在原理图 U7 右侧明确命名的 MCU→Net 信号、KEY_SIM 的 20 个开关引脚、模块清单、连接器证据子集、主控命令及 HOST 能力边界、硬件变更模板和专职 AI 交接。**63 不是 MCU 全部 100 个引脚**，仅是图纸已明确标注的功能映射；其余 Pin、PCB pad、BOM/Netlist 与实物线束由 HW 专项继续补录，不得推算填满。


## 面向项目负责人的优先审查入口（HTML）
**请优先打开 [HW-PIN-REVIEW.html](HW-PIN-REVIEW.html)**：主控100针环形可点击索引（其中63条为已录图纸网络）、KEY_SIM 32针索引（20条开关映射）、连接器、模块、HOST物理能力、差异定位和可导出审查意见。CSV仅作为AI读取与追溯的底层索引，不作为项目负责人的主审查界面。
GitHub源码浏览页面不会执行HTML；在仓库点击 **Download raw file** 后用浏览器打开。另一份带原图页面图像和63+20处实图热点标注的完整离线HTML，已由本轮PM会话交付，适合直接现场审查；两种HTML都不具备真实PCB/现役HEX自动验证能力。

### 原始图纸（Git Blob 与 SHA256 双重确认）
- [主控原理图 TestCtr V1.5 / 5 页](assets/HW-MAIN-TestCtr-V1.5.pdf)：Git Blob `67fe395bfb18ef6aa52f68fde299dbc648866f8c`，SHA256 `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353`。
- [按键板原理图 EPB_ODD V1.1 / 4 页](assets/HW-KEY-EPB_ODD-V1.1.pdf)：Git Blob `ee00af0b25aacc16629661a25bef30a735924dbf`，SHA256 `2f642b559ddcb4c325e872a67b0d74e0472f0db5bcb9021c1cbb4ff8596a6b2d`。
两份已和 Project_Management 历史原件校验；**不能因文件名带 V1.5/V1.1 就认定现场装板属于该 Revision**。

### 固件源基线（仅静态代码）
- [主控 `Main_Control@c830dbcd`](https://github.com/ch3758134-prog/AutoTest/tree/c830dbcd56b436688437b446d222473a88b85098/Project/source)，MCU 型号按图纸为 MK20DN512VLL10。
- [按键板 `Key_Sim@313ed34e`](https://github.com/ch3758134-prog/AutoTest/tree/313ed34ecc8a6e01529998787c8c870cc2e4db4a/WS/EPB_EOL/Sources)，图纸 MCU MC9S08DZ32。
- 旧 `EPB_Test_Keil.zip` / `KEY_SIM.zip` 作为历史输入，**与上述两个 Git 分支的同名源码不自动等价**；未获现役 MCU HEX 的构建 SHA/CRC/读回。
- 上位机当前源码路径分别由 `main/local/G4` 管理，其业务协议需按**受审源 SHA**对照，不能使用历史分支名称作为实机证据。

## 六层事实（独立状态，不能相互代替）
| 层 | 意义 | 本次可确认 | 不可由此推断 |
|---|---|---|---|
| DEVICE_DATASHEET | MCU 封装/复用可用能力 | 芯片型号与图上封装标识 | 当前实际接线 |
| SCHEMATIC_DESIGNED | 图纸元件、信号网络及连接器针位 | 两份 PDF 中可见的设计关系 | 已生产 PCB 网表 |
| PCB_AS_BUILT | PCB 铜箔/焊盘/装配变更 | **UNKNOWN**（未取得 PCB 原生 CAD、Gerber、BOM、装配版本） | 线束和固件 |
| HARNESS_INSTALLED | 当前工位电线、线色、端子、照片/通断 | **UNKNOWN**（SOL 已报告改线） | 自动推断 GPIO |
| FW_SOURCE | C/H 代码对 GPIO、CAN、USB 的使用 | 精确源文件及提交 | 现役已烧录映像 |
| FW_RUNNING + HOST_TEST | 实际镜像与总线/电平/机械证据 | **UNKNOWN**（可用的历史联机反馈不绑定该组精确版本） | 机械到位或 ECU 识别 |

**状态字段规则** `SOURCE_ONLY`、`DRAWING_CHECKED`、`PCB_VERIFIED`、`INSTALLATION_VERIFIED`、`LIVE_MEASURED`、`RELEASED`；仅有对应版本、测量/审查人及证据才可升格，未知保持 UNKNOWN。每项必须绑定 board_id/rev/station_id、source_sha、evidence、effective_date。

## 导航
- [01 资产、版本、证据与关键矛盾](01_ASSETS_AND_EVIDENCE.md)
- [02 主控 MCU→网络/模块与固件差异](02_MAIN_CONTROLLER_PIN_AND_MODULES.md)
- [03 KEY_SIM 20 开关/五路电阻输出关系](03_KEY_SIM_PIN_AND_MODULES.md)
- [04 固件 USB/CAN 命令→HOST 物理能力合同](04_HOST_PHYSICAL_CAPABILITY.md)
- [05 SOL 引脚变更及验证/发布控制](05_SOL_CHANGE_REVIEW.md)
- [06 HW/FW 专职 AI 接手职责与成果要求](06_HW_FW_AI_HANDOFF.md)
- [主控 63 条图纸引脚映射 CSV](data/main_mcu_signal_map.csv) · [按键板 20 Key CSV](data/key_mcu_20key_map.csv) · [连接器证据子集 CSV](data/connector_pin_map.csv) · [功能模块清单 CSV](data/module_inventory.csv)

本档案是**现行 PM 研究管理入口**；原始图纸为证据源、CSV 是可检索派生索引，不可代替 PDF/CAD 和现场装配。
