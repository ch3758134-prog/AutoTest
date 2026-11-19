# AutoTest｜2026-11-16 G3 补充变更批准与执行状态

## 负责人批准事实

负责人在当前项目管理对话明确表示：**“这两个都是我批准了的，你这边整理一下，特别是主控板，我会给上位机执行ai去改的。”**

该表述承接本对话已展示的两类设计与事实：①“公共流程+取消独立按键配置/直接 KEY_SIM”G3 补充合同（此前已提供 HTML/Markdown 完整审查稿）；②主控板最新 GPIO/USB 硬件事实与目标整改，包括独立双夹具、取消外部供电及旧 SOL 用户功能。产品方向与定点 G3 变更据此按**负责人已批准**归档，不重复要求方向批准。

## 生效范围

| 标识 | 正式管理状态 | 说明 |
|---|---|---|
| `PM-CR-20261116-PF` | `DESIGN_APPROVED / IMPLEMENTATION_PENDING` | 公共流程为可调用工程资源；无生产用例身份；同一 TEST DSL |
| `PM-CR-20261116-KEY` | `DESIGN_APPROVED / IMPLEMENTATION_PENDING` | 取消独立按键配置用户入口；TEST/手动台架直编 KEY；保留设备硬件约束与旧文件 |
| `PM-CR-20261116-MC` | `TARGET_APPROVED / FIRMWARE_SOURCE_UPDATED / KEIL_BUILD_UNVERIFIED / FIELD_UNVERIFIED` | 双夹具独立，IGN=HOUT1/PTC1/J1 C10，LED1～6/USB 0x1A～0x1C；外部 GPIO 初始化 LOW，内部 RL_DRV 末尾 HIGH；取消旧供电/SOL |

主控板 0x18 两字节 `[clamp,state]` 与 0x19 位图**已在 `Main_Control@16abba7f` 固件源码实现**；旧版 `v0.3` 仍是一字节互斥 0x18。Keil Build、烧录到实际主控板及真实夹具验证**尚未完成**，不能把源码更新当成现场可用。HOST 按新格式开发，但在实际板卡兼容和安全许可确证前阻断实体 0x18 发送，不退回旧协议。

此批准不等于：Keil 编译通过、固件已烧录、HOST Windows 编译启动、USB 连接通过、夹具机械动作/五路 KEY_SIM 电压实测、烧录实体许可、G4 Gate PASS、或批准旧工程静默迁移。

## 旧基线与新补充的优先关系

不改写先前已批准 P0/G0/G1/G2/G3 文件正文或批准记录。涉及独立按键配置用户对象、旧功能按键来源、单一 SOL/互斥 Air、供电与已取消业务入口的历史实现假设，以本次批准的**产品变更目标**优先；涉及物理 GPIO 初态、USB 断连安全行为、内部电气依赖、旧工程数据迁移的未知事项**仍保持待实证/待定**，不能自行推断。

相关合同：
- [公共流程](CR-PUBLIC-FLOW.md)
- [直接 KEY_SIM](CR-KEY-DIRECT-STEP.md)
- [主控 USB 与双夹具](CR-MAIN-CONTROL-USB-CLAMPS.md)
- [HOST 执行 AI 交接](HOST-MAIN-CONTROL-IMPLEMENTATION-HANDOFF.md)

## 源码与身份

- 管理分支：`Project_Management`。此归档不写 main、不创建项目管理交付分支/PR。
- 主控最新固件源码：`Main_Control@bf81a64e465aa284155ee73ef44a16e39f739bce`（外部所有识别的高边输出软件初始化 LOW；新增六路 LED 的 0x1A～0x1C 控制；RL_DRV 仅 EAL 内部末尾初始化 HIGH，不受 USB/ASW 读写）。此前 `16abba7f` 和更早 `5d2ad5b7` 为历史版本。
- [本轮源码与旧档案映射纠正](../research/MAIN-CONTROL-IO-USB-SOURCE-UPDATE-20261118.md)：**旧 IGN=PTC10/J1 C6 已废止，现为 IGN=PTC1/J1 C10；PTC10/J1 C6 为 LED4**。现有 `F1` 字符串仍为 `EPB_TEST_CTRL v0.4-dev`，不可当作本次镜像证明。
- HOST 在研核查起点：`feature/g4-html-parity-20261115`；执行前必须回读实时 HEAD 和对应文件事实。 
- 来源：项目负责人当前对话明确批准；无独立电子签名或现场测试证据，故不伪称外部身份认证/实测通过。

## 工作任务状态与交付约束

- MAIN_CONTROL：独立双夹具、LED1～6 USB 与 GPIO 默认状态、内部 RL_DRV 分离均已更新源码；Keil 编译、板卡实装镜像身份、GPIO/J1与断连安全行为实测仍 `PENDING`。
- DEVICE/TEST/PROJECT：正式接口、资源合同、验证与旧工程保护；`PENDING`。
- UI/WPF/INTEGRATION：按合同实施，现有可用 USB/CAN 接口不得回归；`PENDING`。
- Windows/现场测试：`NOT_VERIFIED`。
- Codex 仅执行精确授权的机械操作，不得擅自修复/改协议或对真机执行未批准危险动作。
