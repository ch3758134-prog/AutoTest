# AutoTest · 系统开发基线

`main` 保存 **三个平级工程** 的受控源码快照；不是单独的上位机仓库，也不是把固件放到 HOST 里。各专业在对应分支研发，经过审查才更新 `main`。

```text
AutoTest/
├── HOST/                   Windows WPF 上位机工程
│   ├── AutoTest.sln
│   ├── src/
│   ├── tests/
│   ├── tools/
│   ├── samples/
│   └── scripts/
├── Main_Control/           MK20 主控板 Keil 固件工程
│   ├── Project/             Template.uvprojx、源码、启动文件
│   └── docs/                USB / IO / 硬件技术资料
├── Key_Sim/                MC9S08 按键板 CodeWarrior 固件工程
│   └── EPB_EOL/            .project、.cproject、Sources
├── docs/                   跨专业架构、历史研究及受控记录
├── tools/project-control-v2/   系统级项目管理工具
└── .github/                仓库配置
```

| 子工程 | 固定源码入口 | 日常研发分支 |
|---|---|---|
| 上位机 | [HOST/](HOST/) | [HOST 分支](https://github.com/ch3758134-prog/AutoTest/tree/HOST) |
| 主控板 | [Main_Control/](Main_Control/) | [Main_Control 分支](https://github.com/ch3758134-prog/AutoTest/tree/Main_Control) |
| 按键板 | [Key_Sim/](Key_Sim/) | [Key_Sim 分支](https://github.com/ch3758134-prog/AutoTest/tree/Key_Sim) |

**HOST 编译**：进入 `HOST/` 后使用 `dotnet restore AutoTest.sln`、`dotnet build AutoTest.sln -c Debug --no-restore`；启动 `HOST/src/AutoTest.Desktop`。根目录不再放上位机的解决方案或 `src/`。

**固件工程**：打开 [Keil 项目](Main_Control/Project/Template.uvprojx) 或 [CodeWarrior 项目](Key_Sim/EPB_EOL/.project)；两块板单独构建、烧录，不依附 HOST 解决方案。

- [系统结构和迁移说明](docs/architecture/REPOSITORY_STRUCTURE_20261119.md)
- [固件逐文件来源与 SHA 清单](docs/baselines/FIRMWARE_SOURCE_20261119.json)
- [项目管理资料](docs/project-management/README.md)；实时管理审批以 `Project_Management` 分支为准
- [Fluent 浅色 UI 专项设计](https://github.com/ch3758134-prog/AutoTest/tree/ui/ide-design)

**证据边界**：目录迁移只保证源码与 Git 对象可追溯，不能证明 Windows 构建、Keil/CodeWarrior 编译或实体 ECU 测试通过；G4、FLASH、UDS、IO 和 KEY 的未完成验收状态不变。
