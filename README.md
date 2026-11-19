# AutoTest｜ECU 测试系统源码基线

**main 是整个 AutoTest 系统的固定集成基线**；上位机、主控板和按键板在这里保留相互可追溯的源码快照。它不再是 HOST 的日常开发分支，也不代表全部功能都通过实机验收。

## 项目目录

```text
AutoTest/
├── src/                         # Windows HOST .NET / WPF 与业务模块
├── tests/                       # HOST / 领域层自动化测试
├── firmware/
│   ├── README.md                # 固件目录索引及验证状态
│   ├── BASELINE_MANIFEST.json   # 来自专业分支的逐文件 SHA 清单
│   ├── main-control/
│   │   ├── Project/             # Keil 工程（Template.uvprojx）及源代码
│   │   └── docs/                # USB/引脚/硬件审查资料
│   └── key-sim/
│       └── EPB_EOL/             # CodeWarrior 工程、Sources 与 Project_Settings
├── docs/                        # 总体研究、工程与设计资料
├── samples/                     # 示例工程和配置
├── tools/                       # 辅助工具
└── AutoTest.sln                 # HOST Windows 解决方案
```

## 代码来源和入口

| 模块 | main 中的正式位置 | 后续研发分支 | 固定来源快照 |
|---|---|---|---|
| Windows HOST | [`src/`](src/) · [`AutoTest.sln`](AutoTest.sln) | [`HOST`](https://github.com/ch3758134-prog/AutoTest/tree/HOST) | G4 阶段代码经 PR #61 接收入 main |
| 主控板固件 | [`firmware/main-control/`](firmware/main-control/) | [`Main_Control`](https://github.com/ch3758134-prog/AutoTest/tree/Main_Control) | `356896e10ff95447395b7903d352f6db201fbe6c` |
| KEY_SIM 按键板固件 | [`firmware/key-sim/`](firmware/key-sim/) | [`Key_Sim`](https://github.com/ch3758134-prog/AutoTest/tree/Key_Sim) | `313ed34ecc8a6e01529998787c8c870cc2e4db4a` |
| 项目管理 | [`docs/project-management/`](docs/project-management/) | [`Project_Management`](https://github.com/ch3758134-prog/AutoTest/tree/Project_Management) | 独立管理批准和版本记录 |
| 新版 UI 设计 | [`docs/ui/`](docs/ui/) | [`ui/ide-design`](https://github.com/ch3758134-prog/AutoTest/tree/ui/ide-design) | 现代 Fluent Light 研究中 |

- 主控 Keil 入口：[`firmware/main-control/Project/Template.uvprojx`](firmware/main-control/Project/Template.uvprojx)。
- 按键板 CodeWarrior 项目：[`firmware/key-sim/EPB_EOL/.project`](firmware/key-sim/EPB_EOL/.project) 与 [`.cproject`](firmware/key-sim/EPB_EOL/.cproject)。
- 两套固件的详细文件筛选、原始文件 SHA 和排除说明：[固件基线说明](firmware/README.md)。

## 基线边界

本次是**可追溯的源码/工程文件归档**：从各固件专业分支保留必要源代码、工程设置与技术文档，排除 Keil 编译产物、CodeWarrior 工作区缓存及用户本地临时文件。原专业分支的完整历史没有删除。

Windows HOST 采用 .NET 10、WPF、Prism；G4 阶段成果已合并，但全链自动测试及现代 UI 仍需单独验收。**本次未进行 Keil、CodeWarrior 编译或实体板卡验证**。GPIO 初始状态、USB 命令、K19/K20 引脚以及 24V 按键电阻功耗等，必须以对应固件版本和现场证据检查，不能用源码存在代替实测。

后续各模块在专业分支开发和实测，获批准后再将明确版本接入 `main`。避免直接在固定基线内进行未经验证的日常修改。
