# G4 软件阶段候选存档索引 · 2026-11-05

- 所属分支：`g4/v1-stage-4`。
- DEVICE 源码检查点：`dd65cbd`（`feat(g4-device): checkpoint read-only WinUSB and CAN bench tooling`），已将 16 个 WinUSB/CAN/HardwareDebug/测试/解决方案文件提交到 `g4/v1-stage-4`。
- DEVICE 增量已提交；这份阶段索引作为后续文档提交加入 HOST。原先“工作树尚未提交”的描述已更新为实际 DEVICE 提交 `dd65cbd`。
- 本机重新执行 Restore（退出码 0）、Release Build（退出码 0，0 警告/0 错误）和全量 Tests（退出码 0，145/145 通过）。未取得实机 B0/B1/B2 证据。
- 对应 SAEF 阶段记录：`engineering/evidence/G4/AT-G4-STAGE-20261105/`。
- 本记录不修改 Core/Contracts；不批准真实 CAN 发送、IGN、供电或烧录输出；不宣布 G4 Gate PASS。
- HOST DEVICE 源码提交为 `dd65cbd`；本索引以独立后续文档提交归档。SAEF `source_revision` 使用该文档提交完成后的 HOST 实际 HEAD。
