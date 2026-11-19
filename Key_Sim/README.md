# KEY_SIM 按键板固件｜固定快照

- 芯片：MC9S08DZ32，硬件为 20 路电阻网络开关、五路模拟信号
- 源分支：[`Key_Sim`](https://github.com/ch3758134-prog/AutoTest/tree/Key_Sim)
- 固定 HEAD：`313ed34ecc8a6e01529998787c8c870cc2e4db4a`
- 本次保留：34 个原始 blob（详情见 [总清单](../docs/baselines/FIRMWARE_SOURCE_20261119.json)）
- 开发工具：CodeWarrior/Eclipse HCS08，工程根 [`EPB_EOL/`](EPB_EOL/)；项目元数据 `EPB_EOL/.project`、`EPB_EOL/.cproject`。

打开方式：在 CodeWarrior 使用“Import Existing Projects into Workspace”，选择本目录的 `EPB_EOL`。不要把仓库原始分支中的整个旧 `WS/.metadata` 当成可迁移工程目录。

核心固件：[20 Key 接收](EPB_EOL/Sources/APP/KeySim.c)、[GPIO 操作宏](EPB_EOL/Sources/BSW/IO/Driver_Gpio.h)。所检协议：标准 CAN `0x730` DLC 8；`Data[0]=0x01` 更新 20-bit 小端完整掩码，`Data[0]=0x80` 全关；K1=bit0、K20=bit19；GPIO 低电平对应 KEY_ON。

**待实物核查**：K19/K20 所检固件 GPIO 与原理图物理 Pad 不一致；24V 下某些组合可能产生高电阻功耗。无 ECU 输入/五路电压/温升的真实证据，不自动视为所有 20-bit 组合均适合长时输出。

**验收状态**：本次仅归档源代码与工程文件，未重新构建、烧录或实测。原 `.cproject` 中存在历史/生成路径引用，可能需要在新工作区核对工程配置，不得宣称首次导入已编译成功。
