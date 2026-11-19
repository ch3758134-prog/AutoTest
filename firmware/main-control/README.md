# 主控板固件｜Main_Control 固定快照

- 芯片：MK20DN512VLL10（工程 Device `MK20DN512xxx10`）
- 源分支：[`Main_Control`](https://github.com/ch3758134-prog/AutoTest/tree/Main_Control)
- 固定 HEAD：`356896e10ff95447395b7903d352f6db201fbe6c`
- 本次保留：180 个原始 blob（详情见 [总清单](../BASELINE_MANIFEST.json)）
- 开发工具：Keil µVision，项目文件 [`Project/Template.uvprojx`](Project/Template.uvprojx)，原配置要求 `Keil.Kinetis_K20_DFP.1.7.1`。

`Project/` 内的 `source/`、`startup/`、`.cmsis/include/` 和链接脚本保持原工程相对路径，方便直接打开 Keil 工程。去掉旧编译产物、个人调试配置和未被当前工程引用的通用 ARM 模板。

- [主控 USB 对外接口](docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)
- [固定 IO / HOUT / CAN 物理映射](docs/interfaces/BOARD_FIXED_IO_MAP.md)
- [人类可审查的引脚硬件 HTML](docs/hardware/HW-PIN-REVIEW.html)

**已登记源码事实，不代表实物验证：** IGN = HOUT1/PTC1/J1 C10；LED4 = HOUT5/PTC10/J1 C6；LED1～6 的 USB 命令为 0x1A～0x1C；外部高边 GPIO 初始化请求 LOW，RL_DRV 为仅板内初始化 HIGH 的独立控制。旧 `0xF1` 固定版本字符串不能证明现场已刷当前 HEAD。

**验收状态**：未在本次导入中执行 Keil Build、烧录镜像比对、USB 报文与板端电压或气阀测试。真实设备接线、上电与安全门禁仍以现场专项验收为准。
