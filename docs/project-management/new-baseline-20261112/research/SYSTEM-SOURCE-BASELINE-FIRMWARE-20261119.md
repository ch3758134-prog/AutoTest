# AutoTest｜main 整机源码固定基线：HOST + 主控板 + KEY_SIM（2026-11-19）

> **项目负责人决定**：`main` 作为完整项目系统开发的**固定集成基线**，不再只装上位机代码；将当前 Main_Control 和 Key_Sim 的固件源码、工程文件及必要技术资料以清晰层级归档到 main。各专业继续在各自分支开发，待受控验收后再更新固定基线。  
> **本轮结果**：`SOURCE_BASELINE_IMPORTED / GIT_TREE_VERIFIED / TOOLCHAIN_BUILD_NOT_RUN / HARDWARE_FIELD_NOT_VERIFIED`。  
> **已写入 GitHub main**：`f00289f3e141f265211f6cf236b55ca435c01f44`。父提交 `eb1b74a3d7eeb6dcbb582d9c04c72f97f5e1204e` 为此前 PR #61 的 G4 阶段源码接收版本。  
> **HOST 同步**：从同一个父提交通过带租约快进 `HOST` 到 `f00289f3e141f265211f6cf236b55ca435c01f44`，并未覆盖 HOST 独立修改或改动 HOST 业务源码。

## 一、整机仓库结构

```text
main/
├── src/                  Windows HOST (.NET 10/WPF/Prism/专业模块)
├── tests/                Windows HOST/领域测试
├── firmware/
│   ├── README.md
│   ├── BASELINE_MANIFEST.json
│   ├── main-control/
│   │   ├── README.md
│   │   ├── Project/         Keil Template.uvprojx、source、startup、.cmsis/include
│   │   └── docs/            USB/IO 固定映射、人审 HTML
│   └── key-sim/
│       ├── README.md
│       └── EPB_EOL/         CodeWarrior .project/.cproject、Sources、Lib、Project_Settings
├── docs/                 项目级设计/工程记录
├── samples/              示例资源
└── AutoTest.sln          Windows HOST 解决方案
```

**主控来源**：`Main_Control@356896e10ff95447395b7903d352f6db201fbe6c`，保留180个原始 blob；**按键板来源**：`Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a`，保留34个原始 blob。导入文件共214个，未混用固件分支历史作为主线的一部分。原各专业分支不删除。

## 二、筛选和验证规则

- 主控：保持 `Project/source`、`Project/startup`、`Project/.cmsis/include`、`Template.uvprojx`、链接脚本和对应接口/硬件技术文档；排除 Keil 编译输出、个人调试配置、备份、未由该工程引用的通用 ARM .cmsis/device 模板。
- 按键板：保留 `EPB_EOL/.project/.cproject`、`Sources`、`Lib`、`Project_Headers` 实际头文件、`Project_Settings` 与必要工程偏好；排除超过5000个 Eclipse工作区缓存、`FLASH` 构建产物、Source Insight 缓存和日志。原 CodeWarrior 工程可能包含历史/自动生成引用，**本轮不称已在全新工作区成功编译**。
- [逐文件源分支 SHA 清单](https://github.com/ch3758134-prog/AutoTest/blob/main/firmware/BASELINE_MANIFEST.json)：每个导入文件有来源路径、目标路径与 Git blob SHA，能直接核对是否修改源码。
- 以原主线递归 Git Tree 为基准验证：HOST 所有608个非根 README 文件原 SHA 不变；主控180 + 按键板34个导入文件全部与源 Git blob SHA 相同；最终候选共828个文件，差异核对为0、没有非计划新增文件。
- `Template.uvprojx` 里的118条文件路径引用均能在整理后的 Keil 目录找到；这不等于能在未安装 Keil/Pack 的环境构建。
- 已回读 GitHub main 更新及 firmware 目录。

## 三、后续版本治理

| 分支 | 职责 |
|---|---|
| `main` | 整个 AutoTest 项目**稳定、固定、可追溯系统源码基线** |
| `HOST` | Windows 上位机日常开发、用户本机实测与候选接收 |
| `Main_Control` | 主控 Keil 固件的后续独立研发和现场烧录 |
| `Key_Sim` | MC9S08DZ32 KEY_SIM 固件的后续独立研发 |
| `ui/ide-design` | Fluent 浅色 UI 设计/交互验收源 |
| `Project_Management` | 管理审批、接口边界、基线变更记录 |

**静态项目快照 ≠ 完整系统现场 PASS**。Main_Control GPIO/USB/LED/IGN、KEY_SIM K19/K20 引脚、24V功率、现场固件烧录版本以及 Windows 自动 TEST 仍各有未验证项；不得由源码归档推断这些全部合格。导入没有动各专业真实固件源码，也没有对板卡进行硬件试验。

## 四、入口

- [main 仓库 README](https://github.com/ch3758134-prog/AutoTest/blob/main/README.md)
- [两套固件入口](https://github.com/ch3758134-prog/AutoTest/tree/main/firmware)
- [主控硬件人审档案](https://github.com/ch3758134-prog/AutoTest/blob/main/firmware/main-control/docs/hardware/HW-PIN-REVIEW.html)
- [HOST 独立分支](https://github.com/ch3758134-prog/AutoTest/tree/HOST)
