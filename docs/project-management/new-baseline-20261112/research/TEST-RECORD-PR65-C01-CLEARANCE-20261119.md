# AutoTest｜TEST-RECORD PR #65 C-01 源码修复验收与 Windows 门禁移交（2026-11-19）

**职责**：项目管理复审。**结论**：`B01_CAUSAL_DESIGN_STATIC_ACCEPTED / C01_SOURCE_FIXED_AND_READBACK_VERIFIED / WINDOWS_BUILD_PENDING / PR65_NOT_MERGE_APPROVED / HOST_NOT_WIRED / FIELD_NOT_VERIFIED`。

## 一、准确基线和 GitHub 核对

- 专项 PR：[Draft PR #65](https://github.com/ch3758134-prog/AutoTest/pull/65)，Issue [#64](https://github.com/ch3758134-prog/AutoTest/issues/64)。
- 前序 B-01 复审 HEAD：`5ade1bd9e94d3240e9d931d43005caac5778f515`。
- 本轮 C-01 HEAD：`655e1bfe0d95a4a885574450faa79fcf1cff49ef`。
- 直接比较：ahead=1、behind=0；**仅** `HOST/tests/AutoTest.Application.Tests/RealTestRecordSessionTests.cs`（1 addition/1 deletion）；源码回读约 600 行已变为：`Assert.DoesNotContain("DirectDbcSignal", json);`。
- GitHub PR: Draft/Open/未合并，`mergeable=true` 只证明 Git 层可合并；HEAD 精确匹配上述提交。
- `HOST` 与 `main` HEAD 均为 `ffb23f8c05ec4a64b82cd781db6357685d082c04`，未修改。
- 该 HEAD **无可采信的成功 Windows 编译状态检查**；实际 Restore/Build/Test 尚未提交，不能以源码读回替代。

## 二、准入裁定

本轮核对确认上次 [B-01 源码因果门禁复审](TEST-RECORD-PR65-B01-REVIEW-20261119.md) 的 C-01 C# 字符串语法阻断已修复。**B-01 因果关联源码设计静态认可 + C-01 单行修复静态验收**；将本专项由代码静态阻断转入 `WINDOWS_BUILD_TEST_GATE`。

**注意**：目前未批准整个 PR #65 合并 `HOST`；更未批准向 `main` 推进。GitHub “mergeable” 并非编译证明，也非实体 ECU 测试证明。

## 三、发给本地 Codex 的准确 Windows 验证合同

Codex 只承担机械验证，不自行重构、不用 Mock 证明实体 ECU 行为、不更换分支、不合并：

1. 拉取专项分支并确认 HEAD **恰好为** `655e1bfe0d95a4a885574450faa79fcf1cff49ef`；进入 `HOST/` 目录。
2. 运行 `dotnet restore AutoTest.sln` 和 `dotnet build AutoTest.sln -c Release --no-restore`；如解决方案文件名或入口与源码实际不同，以实际找到的 *.sln/*.slnx 及项目路径为准，在反馈中写明改用的真实路径（不得创造路径）。
3. 构建无错误后，分别执行 `dotnet test tests/AutoTest.Testing.Tests/AutoTest.Testing.Tests.csproj -c Release --no-build`、`dotnet test tests/AutoTest.Records.Tests/AutoTest.Records.Tests.csproj -c Release --no-build`、`dotnet test tests/AutoTest.Application.Tests/AutoTest.Application.Tests.csproj -c Release --no-build`。
4. 输出：准确分支/HEAD、SDK 版本、Restore 结果、Build 0 错误/警告数量、三个项目各自测试 passed/failed/skipped、失败原始错误与日志路径。错误须原样返回项目管理，不擅自修改其他专业私有代码。若构建失败，测试明确 NOT_RUN，不能伪造成功。
5. 本次不要进行实体 ECU 操作；不对未接线的 `RealTestWorkbenchService` 和未实现的真实 `IRealRunEvidenceFinalizer` 声称通过实机验证。

## 四、后续独立门禁

- **源码编译与单元/集成测试**：等本地 Codex 实际验证日志再裁定 PR #65 的代码接收；目前仍 `PENDING`。
- **HOST 真正接线**：RealTestWorkbenchService → RealTestRecordSession；统一 RunSnapshot、真实身份、唯一硬件会话和在线执行结果订阅；独立于本专项 PR 的源代码接收。
- **真实证据链**：HOST 实现连续 CAN/DBC/DEVICE 采集、原始证据落盘、EvidenceRef 与 STEP occurrence 映射、丢帧/断连完整性评估、重启恢复；与生产放行门禁独立。
- **用户现场 ECU 验证**：正负测量、超时、断连、循环、停止、报告与原始事件回放；未经实测一律 `NOT_VERIFIED`。

**最终**：`STATIC_ACCEPTED / BUILD_PENDING / PR_DRAFT / NOT_MERGE_APPROVED`。旧 C-01 阻断的历史记录保留，本报告为其后续状态修正，不覆盖历史审查文档。
