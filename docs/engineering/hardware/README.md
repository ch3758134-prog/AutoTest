# AutoTest｜硬件设计资产

硬件设计按**主控板 TestCtr V1.5** 与 **按键板 EPB_ODD V1.1** 分目录管理。原始图纸、硬件设计连接、历史源码、现场陈述是不同层次的证据；未验证的内容不升级为已确认事实。

| 板卡 | 技术资料 | 原始图纸 |
|---|---|---|
| 主控板 | [TestCtr V1.5 设计基线](boards/testctr-v1.5/README.md) | [5页 PDF](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf) |
| 按键板 | [KEY_SIM V1.1 设计基线](boards/key-sim-v1.1/README.md) | [4页 PDF](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-KEY-EPB_ODD-V1.1.pdf) |
| 接口 | [两板与固件接口入口](interfaces/README.md) | 通过固定提交对照 |

主控板应先阅读工作原理，再查 `design.json`、100脚、J1和功率通道。不能把“硬件为什么工作”的解释、精确索引和固件实现版本混成一个列表。每项事实须有来源编号，现场接线或缺失信息须另列。

**版本边界：** 2026-11-16主控板重新建模，尚未完成全板原生 CAD Pin-to-Pin 网表，也未触及生产固件。旧增补式主控文件已整体替换；按键板资料保持独立原样。
