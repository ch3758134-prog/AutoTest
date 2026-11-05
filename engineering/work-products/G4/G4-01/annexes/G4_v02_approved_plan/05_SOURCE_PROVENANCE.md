# 源码与归档资料身份

## 已批准的内容和 SHA

| 项目 | SHA-256 | 来源 |
|---|---|---|
| G3 v0.2 批准 HTML | `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46` | 已批准 G3 基线 |
| G4 v0.2 完整重上传 ZIP | `76c375140f746b9422c4542d0f06307354f9229b3c04e6a7b833e9c1fb5dca2a` | 本交付外包 `reference/AutoTest_G4_v02_REUPLOAD.zip` |
| G4 原始 `.md` | `84fd3d3e087867187e9dc789d2bbf8cc45e2765002046a50f33747b5a1f7684f` | 上述 ZIP 的 `G4_IMPLEMENTATION_MASTER.md` |
| G4 原始 HTML | `bf1b8898cd30eddb7715b9f432527bee82743c5da0dfd393c30b7e59b46bb2ea` | 上述 ZIP 的 `G4_IMPLEMENTATION_MASTER.html` |
| G4 原始质量报告 | `3b6d9b3510d82abd0e9eb4a382530cc6ce1365f6d15a763bf7eb3aa41042c74a` | 上述 ZIP 的 `QA_REPORT.md` |
| 旧 HOST src(20261030-022559).zip | `067026606ca7948cad3041a8655acf2265ea833e4df73fe176c2c010e582bcf8` | 用户历史源码；本次交付不重传其70MB原件 |

## 保真说明

- 正文、分支简报和人工验证程序从 v0.2 ZIP **按原字节拷贝**（UTF-8 `.md`）；CSV 矩阵根据其原始单元格转换为 Markdown 表格，保持列顺序和字段文本，不宣称 CSV 与 Markdown 字节相等。
- 本交付 ZIP 内包括完整 114 成员原始参考 ZIP，便于现场恢复源文件；受控 SAEF 文档仓库仅接受 `.md` 等受控扩展名，不把完整 ZIP 或 C# 原始文件强塞到工作产品目录。
- 文件 `FILE_SHA256_MANIFEST` 和最终 Git 提交回执共同固定本次归档身份。
- 参考 CanTool 的原始 RAR 没有在该 G4 v0.2 方案中重新逐文件实证复核；其适用性按方案原注记限制。
