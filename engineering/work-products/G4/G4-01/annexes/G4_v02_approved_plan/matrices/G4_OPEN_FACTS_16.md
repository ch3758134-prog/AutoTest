# matrices/G4_OPEN_FACTS_16.csv

> 原始 CSV 已按行列顺序转录为 Markdown 表格；原字节文件在外部参考 ZIP。

原始文件 SHA256：`7e4f0eb789121d70c2742293448912adf5bd743a3538f692962050618f0282d0`

原始行数（含表头）：17

| ID | 缺失证据 | 最晚关闭 | 关联模块 | 证据方法 | G4证据状态 |
| --- | --- | --- | --- | --- | --- |
| O01 | USB驱动/设备接口枚举 | B0/B1 | A1 | B0设备管理器及USB原始帧 | OPEN / NO_NEW_EVIDENCE |
| O02 | USB/CAN真实吞吐与帧丢失率 | B1/B2 | A1/A3/E3 | 持续流统计/时间质量 | OPEN / NO_NEW_EVIDENCE |
| O03 | 主控固件烧录实际身份与编译Target | B0 | FW1/A1 | 设备版本/烧录归档核验 | OPEN / NO_NEW_EVIDENCE |
| O04 | CAN0/CAN1到实际物理板卡映射 | B2 | A2/A3 | 线束+收发证据 | OPEN / NO_NEW_EVIDENCE |
| O05 | IGN初始输出和实际电气状态 | B0/B3 | A2 | 上电实测电压 | OPEN / NO_NEW_EVIDENCE |
| O06 | 电磁阀气路方向与夹具机械状态 | B0/B3 | A2 | 现场操作/外部观察 | OPEN / NO_NEW_EVIDENCE |
| O07 | KEY16与五路电阻网络 | B4 | A2/FW1 | 逐路输出测量 | OPEN / NO_NEW_EVIDENCE |
| O08 | Honeywell扫码模式/回车/焦点 | E1实物扫描 | E1/D2 | 设备管理器+记事本扫描 | OPEN / NO_NEW_EVIDENCE |
| O09 | 实际EPB DBC电流信号与周期 | B2+E3 | B2/E3 | DBC/原始帧/示波器对照 | OPEN / NO_NEW_EVIDENCE |
| O10 | ECU真实UDS服务与寻址 | E2非破坏实机 | E2 | 正负响应日志 | OPEN / NO_NEW_EVIDENCE |
| O11 | 烧录成功配置/HEX/日志和恢复方式 | E4实机前 | E4/P1 | 用户现场参考及Mock | OPEN / NO_NEW_EVIDENCE |
| O12 | 实际操作员使用方式/节点交互 | WPF首轮试用 | D1/D2/C1 | 实际交互视频/反馈 | OPEN / NO_NEW_EVIDENCE |
| O13 | 生产编号编码与匹配规则数据集 | E1/H4 | E1/P1 | 用户提供样本与规则试用 | OPEN / NO_NEW_EVIDENCE |
| O14 | 生产节拍/报表统计口径 | H4试用 | R1/P1 | 样本记录与人员评审 | OPEN / NO_NEW_EVIDENCE |
| O15 | 主控固件对CAN波特率配置的实际能力 | B0+B2 | A2/E4 | 固件编译路径与硬件实测 | OPEN / NO_NEW_EVIDENCE |
| O16 | Prism9、WPF运行框架与Dock依赖实际许可/工具链 | G4构建前 | D1/D2 | Windows dotnet info/NuGet restore | OPEN / NO_NEW_EVIDENCE |
