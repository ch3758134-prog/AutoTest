# AutoTest · 三工程系统基线工作指引

本仓库 `main` 保存系统整机固定源码快照；**不是只含上位机**。开始修改前必须确认职责、目标分支、文件责任与准确 HEAD。禁止把之前 G4 临时执行指令当作当前仓库管理制度。

## 独立工程及分支

- `HOST/`：Windows .NET 10/WPF/Prism 软件；开发分支 `HOST`。在 `HOST/` 执行解决方案 Restore、Build、Test。不得修改主控板、按键板固件。
- `Main_Control/`：MK20 主控板 Keil 工程；开发分支 `Main_Control`，Keil `Project/Template.uvprojx`。固件源码的提交不能证明板卡已烧录。
- `Key_Sim/`：MC9S08DZ32 按键板 CodeWarrior 工程；开发分支 `Key_Sim`。不把 Eclipse 工作区缓存、自动生成文件提交进 `main`。
- `docs/`：跨专业项目文件、架构和历史记录。当前管理审批记录仍以专用 `Project_Management` 分支为准。
- `tools/project-control-v2/`：通用项目管理工具，非 HOST 专属工具。
- `ui/ide-design`：唯一现代 Fluent UI 设计研究源，正式代码实施须按当前审查结果批准。

## 安全与交付约束

1. 不以编译或 Mock 结果替代真实 ECU 实测；不能将 USB ACK 推定为 GPIO 外部电气、机械或 DUT 真实动作。
2. 不重新添加已废弃的 MOCK / 旧 TEST 执行入口；保留唯一 TEST DSL 与设备实际会话。
3. 固件与 HOST 接口使用可追溯的精确版本、USB 协议和线束映射；特别注意主控 IGN/HOUT1、LED、两夹具与 KEY_SIM 电阻网络的实际限制。
4. `main` 仅由明确批准的受控基线接收更新；专业工作先在独立分支实施与验证，不自动宣称系统 Gate PASS。
5. 在 Windows 本地操作前核对 `git rev-parse HEAD`，运行结果以准确源码版本与原始日志为准。

## 入口

- [三工程目录与迁移边界](docs/architecture/REPOSITORY_STRUCTURE_20261119.md)
- [系统源码基线总览](README.md)
- [项目管理入口](docs/project-management/README.md)
