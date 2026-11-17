# AutoTest｜G3 已批准补充设计与主控板接口交接

**归档位置**：GitHub `Project_Management` 分支；2026-11-16。  
**定位**：新基线 P0/G0/G1/G2/G3 的**增量变更**，不是阶段重做，不变更原 Gate 结论；G4/真实硬件实施仍按独立证据验收。

## 从这里开始

| 文件 | 读者 | 用途与状态 |
|---|---|---|
| [APPROVAL-AND-STATUS.md](APPROVAL-AND-STATUS.md) | PM/审查 | 负责人批准的准确范围、变更状态和安全限制 |
| [CR-PUBLIC-FLOW.md](CR-PUBLIC-FLOW.md) | TEST/PROJECT/UI | 正式公共流程资源、调用、版本、运行身份/旧 Subroutine 兼容 |
| [CR-KEY-DIRECT-STEP.md](CR-KEY-DIRECT-STEP.md) | TEST/DEVICE/UI | 取消独立按键配置用户入口；KEY_SIM 20-bit mask 直接步骤/台架连续动作；旧项目保护 |
| [CR-MAIN-CONTROL-USB-CLAMPS.md](CR-MAIN-CONTROL-USB-CLAMPS.md) | MAIN_CONTROL/DEVICE | MCU/J1/GPIO、两夹具、WinUSB 实际现状、新旧命令、供电/SOL 清理及硬件风险 |
| [HOST-MAIN-CONTROL-IMPLEMENTATION-HANDOFF.md](HOST-MAIN-CONTROL-IMPLEMENTATION-HANDOFF.md) | **HOST 执行 AI** | 精确文件入口、阶段实施、协议阻断和真实验收清单 |

## 本次管理规则

1. 项目管理唯一直接维护分支 `Project_Management`；不建独立“管理交付分支”，不走管理 PR；用户先在对话审查，批准后直接归档与回读。
2. 主控真实资料以 `Main_Control` 分支最新经证据验证版本为准；对外 USB 文档为当前现状调查，不能自动视为新协议已部署。
3. 原 G3 已冻结自动测试 HTML 主布局不变，公共流程和 KEY 操作只能做定点增量；手动台架仍是必须可实测的真设备工作台。
4. 产品决定已批准 ≠ 所有技术细节已在硬件实测 ≠ 执行代码已集成 ≠ 出厂交付已通过。遇未知必须明确预检阻断。
5. **不要为这次变更重启产品定义或新建第二套 TEST DSL、USB 协议栈、按键配置系统。**

## 现有固件/软件的高优先级差距

- 双夹具硬件为独立 GPIO；`Main_Control@16abba7f` **源码已改成两字节 0x18**、独立 0x19，Keil 编译/现场固件身份/夹具实测仍待证。HOST 按新协议开发，但在版本与实物验证前不发送 0x18。
- 新源码已移除旧 ECU/KEY 供电与 SOL 的 USB 路由；RL_DRV 仍为 PCB 内部功率链的 GPIO 初始化，不向 HOST 提供命令。旧 PTC8/PTC9 复位态与现场线束仍须验证。
- KEY_SIM 直接 mask 的驱动与旧 Controller/RawKey 路径不同；旧 RawKey Pulse 可无条件 ReleaseAll，不等价于新版 Keep。
- `Subroutine/CallSubroutine` 旧节点存在但实际运行器尚未支持公共流程；需版本/递归/作用域/证据合同。
- 用户此前报告 G4 USB 连接失败；应保留详细 WinUSB 阶段诊断与真机恢复验证，不能把 UI 预览器当实测 EXE。

## 外部事实链接

- [主控固件 USB v0.2 接口与整改代码](https://github.com/ch3758134-prog/AutoTest/blob/16abba7fec94dc4b7fbdc05b91ee209b5c7efa12/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)
- [冻结自动测试 HTML](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html)
- [主控/KEY_SIM 图纸审查档案](https://github.com/ch3758134-prog/AutoTest/tree/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115)（后续集中迁移到 PM 分支；目前不删除 main 原件）。
