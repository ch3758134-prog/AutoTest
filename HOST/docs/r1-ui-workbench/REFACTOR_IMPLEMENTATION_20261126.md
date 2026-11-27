# AutoTest R1｜破坏性产品与 UI 重构实施记录（2026-11-26）

状态：**源代码已修改并提交 R1；尚未 Windows Release 编译/启动/真机验收，不得称为可现场发布的完成版。**

## 1. 已经实施的产品合同

- 测试工程打开后以 **TEST 测试用例** 为日常主要对象；左侧不再显示旧的工程文件/资源混合树，而是唯一有序 TEST 项目的实际用例调用列表。
- 一个 ECU 工程只有一个有序 TEST 和一份当前生效 DBC；多个公共流程、波形判定可被 TEST 步骤引用；原生 FLASH 工艺由工程烧录工具维护、生产自动调用。
- 用户不再在单个测试用例重复配置扫码、夹具、烧录或 DBC 文件。扫码固定标记仅在生产工程库中选择完整版本。
- 工程 TEST 的临时运行选择不更改正式执行顺序；独立工具实测不升级为生产 PASS。
- 生产和工程是不同 UI 任务，但共享同一资产模型和受控硬件/执行会话。

## 2. 已修改文件和行为

### 导航与主窗口

- `Shell/Navigation/ProjectExplorerView.xaml(.cs)`：用有序平面用例列表替换通用工程树。单击打开用例、搜索用例、F2 重命名；公共流程仅作为次级可折叠列表。
- `Shell/MainWindowViewModel.cs`：投影 `OrderedCaseNodes` 和 `PublicFlowNodes`；工程默认打开首个正式用例而不是项目管理窗口；保留当前/最近用例返回路径；在 DBC 工具往返时不改变 TEST 选中上下文。
- `Shell/MainWindow.xaml(.cs)`：工程顶部直接提供 DBC、FLASH、WAVE、公共流程入口；编辑文档改为当前上下文标题+紧凑切换器，删除可见多 Tab 排；移除“工位设备与扫码枪”旧管理入口及重复工具命令。
- `ProjectConfigurationOverviewView.xaml`：检查后返回上次 TEST 用例。

### 工程唯一 DBC

- `Projects/ProjectWorkspaceService.cs`：新建 DBC 资产只允许在空工程中新增唯一生效项。再次导入时复用同一 DBC 身份，以受版本控制的文件替换和修订更新为准，禁止隐藏添加第二份。
- `MainWindowViewModel.cs`：替换时明确警告需要核对 TEST/WAVE 引用，并要求已有 DBC 工具先保存关闭；不允许 UI 静默切换当前 TEST 用例。
- `Projects/AutomaticTestResourceContract.cs`：旧工程若存在多个 DBC 则拒绝进入正式 TEST 资源闭包，不任意选择其一。
- 旧 DBC 的测试映射修订不自动“迁移通过”；需要由工程人员明确确认信号语义后才能恢复执行。

### 固定夹具与历史配置

- `Shell/ManualBenchWindow.xaml(.cs)`、`ManualBenchViewModel.cs`：手动台架改为唯一“压紧/释放”两个操作；按固定顺序控制两路主控输出，失败停止后续通道并记录状态；主控 ACK/GPIO 电平不被认定为机械到位证明。
- `Projects/ProjectWorkspaceService.cs`：拒绝新建旧扫码配方、TCF/TBC、工程内夹具工艺和第二份 FlashSettings；仍允许读取旧资产供人工修复，不在新工程里继续生成。
- `Projects/EngineeringTestProjectPreview.cs`：工程编辑时不再将扫码期望和工位夹具配置当作 TEST 资源检查的必需项目。

## 3. 已明确不在本轮谎称完成

- 当前 WPF 尚无本地 Windows 编译环境，本次只进行了 XAML 标签结构、事件处理名称、C# 主要分隔符/命令连接等静态检查。
- 正式生产真实物理适配器、START/STOP 输入、机械反馈证据、异常释放、RECORD 最终确认闭环尚未完成。R1 中的 `ProductionPhysicalStation` 明确写明 Desktop 未实现真实适配器，因此正式运行不得显示假 PASS。
- 旧 `ResourceNodes` 仍作为既有文档/资源命令的**内部过渡数据**，已经不再在左侧绘制。全量删除该内部技术结构需在后续受编译回归约束的模块化实施中完成。
- 专业 WAVE 观察/采集/比较及 UDS/FLASH 内核保留原有能力；其内部控制项和部分历史配置代码尚待后续梳理。不能把“隐藏旧 UI”误当作清除全部旧业务代码。
- 暂无 Windows 发布或实际 ECU、扫码枪、夹具验收证据。

## 4. 必须验证的本地路径

1. Release 编译与启动，无 WPF XAML/Binds 异常；启动自动恢复上次工程。
2. 打开已有 HydEPB 工程：左侧仅有正式有序测试用例；单击能切换并打开 TEST 结构化步骤表，当前 DBC/WAVE/FLASH 入口真实可用。
3. 新建用例后自动加入正式执行顺序；在“调整检测顺序”中排序后左侧反映变化；重启恢复。
4. TEST 的单步、用例、跨用例周期 CAN 和 KEY_SIM 持续保持都必须真机验证；右侧 CAN 监视保持可用。
5. DBC：首次导入一份；替换时提示并使受影响步骤重新检查；第二份活动 DBC 无法悄悄加入；项目中旧多 DBC 显示明确阻断。
6. 打开 DBC、FLASH、WAVE 等工具并返回之前 TEST 用例，不丢失编辑草稿；关闭主窗所有子窗退出。
7. 手动台架点击“压紧/释放”，核对两个输出的顺序、真实接线、ACK、GPIO 回读与实际机械动作。尚无机械传感器时不得标识真实到位。
8. 生产扫描原文完整可见，固定标记唯一映射工程；正式 FLASH/TEST 在尚未完成物理工位闭环时继续受阻断，而非伪装成功。

## 5. 重构验收约束

只能用 Windows 编译、桌面启动、真实设备测量和按 RunId 归档的证据完成最终验收。不新建业务第二执行器、生产配方、单用例 DBC 绑定、工位夹具编辑器或 Mock 生产状态。错误必须修复 R1 代码，不能要求本地 Codex 进行源码整改。
