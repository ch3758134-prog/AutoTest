# G4 INTEGRATION 日志｜WPB-G4-INIT V0-R2

- 本次从原 V0 完整源码包 `b63d607d7ab0d58cfbb07ed059d8e1f21b5a5a0236939634e5fd64ec22a05028` 派生 R2；新 ZIP 的实际 SHA-256 见独立 `.sha256` 和签发执行合同。
- G3/G4 冻结设计输入保持不变，参见 `G3_BASELINE.json`。
- `global.json` SDK 从 `10.0.100/latestPatch` 更新为 `10.0.400/latestPatch`；原始 9 生产程序集源码、XAML、工程文件及所有测试源码未改。
- 11 个原新增路径已由主对话全部批准纳入 V0；Prism Community License 已确认。
- 23 项 CT/DTO 保持候选，`CONTRACT_REVISION = UNSET`。API 候选 SHA `0417108b292353b896b88f8170b129f1d63c4ef2dfcfbc3491990d2bd8dd7a6a` 不变。
- 本地 Windows 历史只读预检发现 `dotnet --info` 初始化异常，裁决为记录项；后续由实际 --version/MSBuild/Restore/Build/Test 判定。
- Windows Restore / Build / Test / WPF 运行与 Git init/commit：均 `NOT_RUN`。
- 物理设备、固件写入、SAEF 写回：`NOT_AUTHORIZED / NOT_RUN`；HOST `BASE_COMMIT = UNSET`；G4/G5 Gate `NOT_REVIEWED`。
