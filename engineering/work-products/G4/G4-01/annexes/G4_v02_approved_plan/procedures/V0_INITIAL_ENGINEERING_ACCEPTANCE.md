# V0 首个可运行HOST工作包验收合同（设计候选）

1. 用户授权 `WPB-G4-INIT` 后，主对话输出所有工程/源码/测试的实际字节及机械执行指令。
2. Codex报告Windows SDK/许可/恢复包版本、`dotnet restore/build/test`的原命令、stdout/exitcode。
3. 用户在Windows启动真正WPF程序，看到Prism主工作台，操作最小FakeStation请求并确认运行事实/状态区分。
4. FakeStation命令不得调用真实USB；必须有一条正常路径和一条超时/断连/未授权拒绝的负测。
5. 工程源码Git提交/干净状态，记录G3 HTML SHA和已编译合同修订版本；本阶段任何真实设备动作=违规。
6. 未达到上述任一项：V0不可宣告PASS，其他网页分支可以只做独立算法预研，不得改公共接口或声称已集成。
