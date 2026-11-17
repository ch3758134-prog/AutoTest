# FLASH｜CanTool 安全算法来源核实与内部处理整改

**状态：源码候选，Windows 编译/真实 ECU 的 0x27 正响应尚未验证；PR #55 Draft。**

## 原始来源和证据级别
1. GitHub Issue #25 的 CanTool.rar 原源码研究：`Services/SecurityKeyService.cs` 原生 `LoadLibrary`/`GetProcAddress("ZLGKey")`，失败时另有内置 LFSR 备用分支。`ReferencePath/KEY.dll` 也在原包中，但此前鉴别为 .NET Framework 托管程序集，**无原生 ZLGKey**；不可当作 Win32 ABI 的动态库。
2. 用户提供 `SafetyFile.dll` 的实际 SHA256 为 `ee2c4a2791b88dce19db29f308752c8f79bc0654463d43e6880f65c6fdea3833`；文件为 CLR 托管 DLL，可识别 `SafetyVerificate(byte[])`，**无原生 ZLGKey**。对于原源码的 Win32 入口，它是无法加载的。
3. 用户提供 `sa_demo.dll` 的实际 SHA256 为 `a8d22a0f82d6f68910fbbf2d81afad35d19c804953ad33c80f5143da8c98a703`；x64 原生函数 `ZLGKey` 的静态反汇编显示：取 Seed 前四字节按高位优先组装 uint、循环35轮 MSB 左移，若最高位为1 XOR `0xC2DFF1C2`，返回4字节大端结果。这与 Issue #25 当时记录的原版 fallback 算法一致。
4. 没有取得原 CanTool.exe/CanTool.dll 的当前可执行二进制及其对应运行目录，故不能证明用户当年实际运行的版本一定执行了此 fallback，也不能证明当前 ECU 的实际安全算法采用它。

## 本次代码处理
- 外部仍只需要选择配置 XML、固件和 SafetyFile.dll（或选择一个文件夹一次导入三项）。不再导入额外 Seed/Key DLL。
- 对上述**精确哈希**及托管方法签名都匹配的 SafetyFile.dll，当前构建明确锁定 `CanToolReferenceBuiltInLfsr` 这个程序内部 Provider，而非错误调用托管 SafetyVerificate，也不加载演示 `sa_demo.dll`。
- 该内部 Provider 是显式、哈希绑定的特定参考实现，**不是在 DLL 报错后偷偷回退**。对未知文件或不同哈希拒绝继续烧录，禁止无来源算法尝试。
- 冻结任务绑定参考 SafetyFile SHA 与内部 ProviderKind；窗口文件状态、最终真实擦写确认、流程与审计都标记真实算法来源。
- 在拿到 ECU 的真实 `27 01/67 01` 与 `27 02/67 02` 响应之前，不声明 SecurityAccess 成功；若 ECU 返回 `7F 27 35/36/37` 或超时，不自动换算法或重试。

## 验证步骤
1. Windows Build FlashLab + FlashLab.Tests + Devices.Tests；启动 FlashLab 窗口。
2. 离线导入原 XML、HEX 和 SafetyFile，确认界面出现 `CanTool 参考内置算法`，无需任何 Seed/Key DLL 文件，检查窗口没有旧的 `sa_demo.dll` 选择。
3. 不执行实体烧录时，可验证内部 LFSR 对纯人工测试输入的字节计算一致性（测试已加入）；但此测试不能证明 ECU 兼容性。
4. 实体调试前必须由现场操作人员确认 ECU、镜像、地址、稳定供电。首次可记录 0x27 请求、正响应或 NRC，并保留脱敏诊断证据；发现 35/36/37 立即停止，不能重复尝试。

## 未解决
- 原 `CanTool/ReferencePath/KEY.dll` 实际托管 `KeyCreate` 接口是否参与目标版本原 CanTool，需取得完整原程序才能最终证明。本轮不能凭文件存在就调用其猜测方法。
- 从原 CanTool 备用分支复原的算法与 **ECU 实际匹配**仍未被证明；参考源与目标版本可能不同，严禁写成已经成功烧录。
- 无 Windows .NET 10 / WPF 构建环境，当前 SHA 尚未真实构建或运行。
