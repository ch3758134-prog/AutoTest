# AutoTest UI Visual Research｜V01 来源与证据等级
日期：2026-11-19。范围：现代 Windows 专业软件，视觉/操作一体设计原则研究。方法：回读 R01～R06 并核对以下官网，**未对厂商软件进行独立上机操作/定量用户研究**。不得将厂商能力存在性当作设计优越性定量证明。

## A. 规范与准规范（强约束或官方设计指南）
| ID | 来源 | 事实支持 | 证据边界 |
|---|---|---|---|
| S01 | Microsoft Fluent 2 Layout https://fluent2.microsoft.design/layout | 网格、间距、对齐、分区、响应式；重要内容应拥有相称空间；固定/弹性/混合网格依内容选型 | Fluent 2 网站设计文档，不能认为其所有 Web 实例在 WPF 自动成立 |
| S02 | Fluent 2 Typography https://fluent2.microsoft.design/typography/ | Windows 字级、语义角色、行高、字号、强调和无障碍对比原则。Windows 示例 Body 14/20、Caption 12/16、Subtitle 20/28 | 数值用于参考，不直接冻结 AutoTest 中文字号或高 DPI 显示 |
| S03 | Fluent 2 Design Tokens https://fluent2.microsoft.design/design-tokens/ | 使用语义 token 管理字体、色彩、间距、层级，避免组件散落的硬编码 | 设计原则，不要求项目直接依赖 Fluent Web 库 |
| S04 | Windows Content Layout and Spacing https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics | 间距/留白应表示语义分组；缩小空间时可用 Body Strong 代替大型标题 | Windows 官方例子，具体 epx 不自动成为本项目定值 |
| S05 | Windows Commanding Basics https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics | 命令表面、上下文与反馈；避免无谓模态中断 | WinUI 指导，WPF 需要独立实现 |
| S06 | WPF Layout https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout | Measure/Arrange、Grid/StackPanel、尺寸需求、DPI 与布局舍入、虚拟化 | 属实现可行性依据，不能代替视觉设计论证 |
| S07 | WCAG 2.2 Text contrast https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum | 普通正文对比度至少 4.5:1；大号文字至少 3:1（有适用条件） | WCAG Web 规范，可作为桌面软件的可读性基线 |
| S08 | WCAG 2.2 Non-text contrast https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast | 识别 UI 组件所必需的非文本视觉信息对比度至少 3:1；并非所有按钮都必须有边框 | 不能简单理解为“一切边框都要 3:1” |

## B. 成熟产品一手资料（机制存在性）
| ID | 来源 | 可核实的设计机制 | 边界 |
|---|---|---|---|
| P01 | JetBrains Islands 主题设计研究 https://blog.jetbrains.com/platform/2025/09/islands-theme-the-new-look-coming-to-jetbrains-ides/ | 关注编辑器/工具窗视觉分离、可识别页签及导航；进行了 mockup 问卷、产品 A/B、访谈与使用统计 | JetBrains 的用户研究不证明 AutoTest 使用同样视觉就同样有效 |
| P02 | JetBrains Islands 发布说明 https://blog.jetbrains.com/platform/2025/12/meet-the-islands-theme-the-new-default-look-for-jetbrains-ides/ | 工具窗边界、布局可调、活动页签可见性、不同区域聚焦 | 特定版本的 JetBrains 设计 |
| P03 | NI TestStand Step Settings Pane https://www.ni.com/docs/en-US/bundle/teststand-api-ref/page/tsref/step-settings-pane.html | 选中步骤对应内建/类型专用参数、模块调用；可多选查看共同属性 | 厂商说明自定义 Step Type 可能需要对话框；不能武断宣称 NI 所有类型均无弹窗 |
| P04 | Ignition Designer 8.3 https://www.docs.inductiveautomation.com/docs/8.3/platform/designer/designer-user-interface | 不同对象激活不同 Designer Space 和面板；全局菜单/局部面板分层 | 不需要复制 Ignition 所有面板 |
| P05 | ecu.test 2026.2 插入步骤 https://docs.tracetronic.com/help/ecu.test/2026.2/en/Main-program/Action-window/Inserting-test-steps.html | 可通过上下文菜单或拖拽从 Action Window 将步骤插入当前位置，必要时先参数化 | 不等于 AutoTest 必须采用其模态步骤参数窗口 |
| P06 | IBM Carbon Data Table https://www.carbondesignsystem.com/building-blocks/core/components/data-table/guidelines | 表格有不同密度（xs=24，sm=32，md=40 px 等）；表格工具栏、搜索、选择、批量操作、行内操作和 Hover 都有规则 | Carbon 面向 Web/企业应用，尺寸体系需 WPF 中文可用性复核 |

## C. UX 研究及既有内部研究
| ID | 来源 | 支持 |
|---|---|---|
| U01 | Nielsen Norman Group《10 Usability Heuristics Applied to Complex Applications》https://www.nngroup.com/articles/usability-heuristics-complex-applications/ | 一致性、状态可见、识别优于回忆、降低无关视觉竞争、错误恢复对于复杂软件同样重要 |
| I01 | AutoTest R01～R06：docs/ui/research/ | 用户任务、上下文、对象、命令、状态、恢复的具体约束及反模式 |
| I02 | 用户 2026-11-19 Windows UI 截图（当前对话） | AutoTest 新 UI 当前真实渲染可见信息：步骤操作区拥挤、中央工作区过空、字号及层级不明、选中对象与详情未形成直观联系。该截图仅是用户环境中的当前状态，不构成算法/控件根因已定位 |

## 来源判读要求
- “应该怎样操作”不能仅引用视觉指南；至少需要 I01 或 P03/P04/P05 支持任务机制。
- “为什么这样看得清/能找到”不能仅引用业务流程；至少需要 S01/S02/S04/P01/P06/U01。
- “能否在 WPF 显示/不遮挡/能缩放”不能仅凭设计意向；必须结合 S06 与后续 Windows 截图实测。
- A 规范、B 厂商存在性、C UX 案例、I 本产品要求之间发生冲突时记录冲突与取舍，不隐瞒、不把研究推论写成用户批准。
