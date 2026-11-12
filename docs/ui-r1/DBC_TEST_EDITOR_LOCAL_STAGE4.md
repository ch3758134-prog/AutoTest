# AutoTest local｜DBC 原始文件兼容与测试程序编辑器

当前候选按用户实测补充：
- 保留上次工程路径于 %LOCALAPPDATA%/AutoTest/desktop-settings.json；下次启动自动打开最后成功打开的正式工程。失败保持软件启动可用。
- 绿通原始 DBC 使用扩展帧编码（如 BO_ 2566869031 / 0x98FF5027），实际仲裁 ID 是 0x18FF5027；保留 BO_ 原始编码以维持 CM_/BA_/VAL_ 的关联。
- 对已有 BO_/SG_ 行进行参数编辑时保留原文注释、属性和枚举，禁止不安全 ID/信号重命名、删除结构等。非支持语法仍只读，保存失败不允许损坏来源。
- TEST 文件继续使用 AutoTest.Testing.Authoring 的 StepNode、StepEditTransaction、TestDocumentCodec、StepTreeValidator；不生成第二套测试模型。
- 三阶段“准备/测试/清理”；树中真实 IF then/else、循环体和等待；点击阶段、槽位可添加，选中步骤可后插、复制、移动、删除、参数编辑、撤销和重做。
- 保存后更新工程 TEST 资源 Revision/SHA；失败时保留现有文件和 .bak 备份；重载未保存草稿时要求确认。
- 本轮不进行真实设备执行、Mock 运行或声明未实现的硬件步骤能够运行。

本轮待本地 Windows 编译和 GUI 实测，不在 GitHub 上运行 CI；用户实测批准后再接入 main。
