# AutoTest 固件基线

此目录只负责保留 **main 固定基线**的两套固件工程，**不是把 Main_Control 和 Key_Sim 两条分支合并为同一个固件工程**。各板独立开发、编译、烧录、验收。

| 对象 | 入口 | 源分支/精确 SHA | 本次导入 |
|---|---|---|---|
| MK20 主控板 | [main-control/Project/Template.uvprojx](main-control/Project/Template.uvprojx) | `Main_Control@356896e10ff95447395b7903d352f6db201fbe6c` | 180 个源/配置/文档 |
| MC9S08DZ32 按键板 | [key-sim/EPB_EOL/.project](key-sim/EPB_EOL/.project) | `Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a` | 34 个源/配置 |

**完整追溯**：[BASELINE_MANIFEST.json](BASELINE_MANIFEST.json)。其中每个导入文件保留原始 Git blob SHA，可与专业分支逐项核对。

### 收录原则

主控：保留 `Project/source/`、`Project/startup/`、实际被 Keil 工程引用的 `Project/.cmsis/include/`、`Template.uvprojx`、链接/调试依赖文件以及 USB、IO 文档和硬件审查 HTML。Keil 工程内部相对路径不改变。未带入 `debug/`、`output/`、`*.o`、`*.crf`、用户调试状态与备份，以及未在工程内引用的通用 `.cmsis/device/` 模板。编译仍可能依赖本地 Keil 和所需 Device Pack。

按键板：保留 `EPB_EOL` 工程的 `.project/.cproject`、`.settings`、`Sources/`、`Lib/`、`Project_Headers` 中实际头文件、`Project_Settings/`。不收录 `WS/.metadata/`（Eclipse 工作区缓存）、`FLASH/` 构建输出、Source Insight `SI/` 缓存、历史日志和个人调试配置。CodeWarrior 工程内保留原始相对引用，**但原 .cproject 中已有旧源码/生成路径引用，尚未做全新工作区导入与编译验证**。

### 当前证据边界

- 状态：`SOURCE_SNAPSHOT_RECORDED`；不是 `BUILD_PASS` 或 `HARDWARE_PASS`。
- 主控 USB/IGN/LED/双夹具和 CAN 现行合同请看 [主控技术文档](main-control/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md) 与 [固定 IO 映射](main-control/docs/interfaces/BOARD_FIXED_IO_MAP.md)。
- KEY_SIM 20 路为电阻组合开关、形成五路模拟电压；K19/K20 固件宏与原理图 Pad 的映射差异、24V 高功耗组合仍待专项实物验证。
- 已烧录的实际镜像是否与该源码一致、Keil/CodeWarrior 是否在现场编译通过、主控和按键板能否完整联动，均不得仅凭 main 固件文件存在来断言。
