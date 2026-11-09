# AutoTest G4 · 实施准备 v0.2（完整修订候审版）

> 设计审查件，**NOT_AUTHORIZED**，不是现有源码/SAEF增量落档包，不能直接让Codex执行。

- 已批准上游：G3 v0.2 `PASS_WITH_LIMITATIONS`，G3-01～08 `ACCEPTED`。
- 用户回传的 SAEF 文档 Git HEAD：`5de99332170d9434c7e5b4a15c9d091245a7302c`；未连接Windows仓库实时复查。
- 已批准G3 HTML SHA256：`4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`。
- 历史HOST参考：`src(20261030-022559).zip`，SHA256：`067026606ca7948cad3041a8655acf2265ea833e4df73fe176c2c010e582bcf8`；实际检查 27 `csproj`、265 C# 源文件、53 XAML。
- 新 HOST Git / 工程：**NOT_CREATED**；G4 Gate：**NOT_REVIEWED**；所有 G4 预期测试 **NOT_RUN**。

## 重点入口

1. `G4_IMPLEMENTATION_MASTER.html`：可离线打开的完整总档案（推荐先读）。
2. `G4_IMPLEMENTATION_MASTER.md`：相同主文的文本形式。
3. `matrices/OLD_HOST_REUSE_DECISIONS.csv`：41项旧源码逐文件迁移与验证约束；`reference/OLD_HOST_SELECTED` 内附同字节的选定源码。
4. `matrices/PUBLIC_TYPE_CLOSURE_REGISTER.csv`、`PUBLIC_CONTRACT_CLOSURE_23.csv`：公共API原型闭合要求。它们是**实施建议/候审合同**，不是已编译真实代码。
5. `matrices/ADDITIONAL_ENGINEERING_AND_TEST_FILES.csv`：54项新增工程、测试、基础设施文件责任，独立于G3原有147项。
6. `matrices/G4_TEST_EXECUTION_101.csv`：101项测试的负责方向、工程、预计测试类/方法、最早证据层级。
7. `matrices/COMPLETE_FILE_OWNERSHIP_PROPOSAL.csv`：合并后的文件独占责任矩阵。
8. `branch_briefs/`：11个方向的专属交接件；**必须在V0编译基线产生后更新BASE_COMMIT才能授权**。
9. `reference/AutoTest_G3_FullDesign_v02.html` + `G3_WORK_PRODUCTS/`：获批G3只读副本，不允许分支修改。

本包未实际编写新HOST源码，没有任何新HOST构建成功或实机运行结果。
