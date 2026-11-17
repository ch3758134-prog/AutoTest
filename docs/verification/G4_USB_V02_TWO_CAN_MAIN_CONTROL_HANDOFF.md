# AutoTest｜G4 真实台架 USB / 双 CAN / GPIO v0.2 接口核对

**状态：HOST_CODE_CANDIDATE / NO_WINDOWS_BUILD / NO_KEIL_BUILD / NO_PHYSICAL_BENCH_TEST**  

## 本次权威来源

- [主控 USB 外部接口 v0.2](https://github.com/ch3758134-prog/AutoTest/blob/Main_Control/docs/interfaces/USB_HOST_CONTROL_INTERFACE.md)，已读取 blob `cb9765539d651be551c0e4771f22bca7a00eac1f`，`Main_Control` 审查 HEAD `16abba7fec94dc4b7fbdc05b91ee209b5c7efa12`。
- 上位机实现分支：`feature/g4-html-parity-20261115`；本文件只记录代码候选和现实待验事实。
- KEY_SIM 独立固件源码 `Key_Sim@313ed34ecc8a6e01529998787c8c870cc2e4db4a`，`WS/EPB_EOL/Sources/APP/KeySim.c`。

## 唯一链路模型

```text
Windows AutoTest 编辑/自动测试/手动台架
  └─ WinUSB Bulk 0x03 OUT / 0x82 IN；0xA5 自定义帧
      └─ Main_Control 主控（一个设备，一个 USB Session）
          ├─ 直接主控 IO：IGN(GPIO PTC10 / J1 C6)；
          │   独立夹具1(PTB1 / J1 B5)、夹具2(PTC2 / J1 B4)
          ├─ 软件 CAN1（500 kbit/s）→ DUT/ECU：USB 0x40 发送，0x41 收帧通知
          └─ 软件 CAN0（500 kbit/s）→ KEY_SIM：USB 0x40 转发按键板帧
```

主控不解释 KEY_SIM、DBC 或 UDS；KEY_SIM K01~K20 是电阻网络组合开关，不是单独 20 路 ECU 数字输入。软件 CAN0/CAN1 的编号不得直接等同原理图上的 CAN1/CAN2 网络标注。

## 当前 HOST 对齐详情

| 指令 | 真实载荷 | HOST 落点 | 安全/观测语义 |
| --- | --- | --- | --- |
| PING `0xF0` | 空 → `PONG` | `HardwareConnectionService.ConnectAsync` | 上层字节流已响应 |
| GET_VERSION `0xF1` | 空 → ASCII 版本 | `ConnectAsync` | 记录设备自报 `EPB_TEST_CTRL v0.4-dev`；非固件哈希 |
| CAN TX `0x40` | `{channel,ide,idLE4,dlc,data8}`，15 B | `CanFrameCodec` / `SendCanOnceAsync` / `SendTestCanAsync` | channel=0→KEY_SIM，1→DUT；USB OK 不等于 CAN 远端收到 |
| CAN RX `0x41` | 15 B，SEQ=0，异步上报 | `HardwareCanReceiveBridge` | 主机到达时间；低优先级拥塞可能丢包 |
| SET_IGN `0x12` | 1 B `0/1` | `SendManualIgnAsync` / `SendTestIgnAsync` | 需准确授权及 v0.4-dev 自报版本；上电初态 HIGH |
| GET_IGN `0x13` | 空 → 1 B `0/1` | `ReadIgnLevelAsync` | MCU GPIO 电平，不是 J1 C6 实测电压 |
| SET_CLAMP `0x18` | **2 B** `[1/2,0/1]` | `SendManualClampAsync` | **独立夹具**：0请求释放、1请求压紧；操作一路不自动改变另一路 |
| GET_CLAMPS `0x19` | 空 → 1 B 位图 bit0/bit1 | `ReadClampLevelsAsync` | 只能证明 MCU GPIO 回读，不是机械到位 |
| 旧电源/旧 SOL | `0x10/11/14/15/16/17/1F` | **无业务调用，物理命令栅栏不放行** | 主控 v0.2 已删除 |
| RL_DRV | 无 USB CMD | **不可见** | 仅 PCB 内部共享供电控制 |

KEY_SIM 固件 `KeySim.c` 已核对 CAN ID `0x730`、DLC=8：`0x01 mask[0..2]` 更新 20-bit K01~K20 开关；`0x80` + 全零释放全部。HOST `KeypadCommandCodec` 与之吻合。未检查现场烧录 image，不做“真有电压”的结论。

## 本次 HOST 实现修复

- `MainControllerCodec`：引入 v0.2 命令常量和合法 `SET_CLAMP` 二字节编码；不复用 `0x16`。
- `PhysicalCommandFence` 与 `WinUsbByteLink`：增加**一次精确夹具授权**（夹具号、压紧/释放、15 秒、连接世代）；在 USB OUT 前消耗，TEST lease 不放行手动夹具，旧已删除的只读 CMD 不再绕过物理栅栏。
- `HardwareConnectionService`：PING 内容及 `GET_VERSION` 核验；只有设备自报 v0.4-dev 才开放 v0.2 GPIO 指令；新增 IGN/夹具 GPIO 回读和独立夹具发送，同一个真实 USB Session；Faulted 状态清空旧世代和测试授权。
- `ManualBenchViewModel/Window`：两夹具独立选择（夹具1/2、压紧/释放）、现场安全核对勾选、逐条精确授权、SET ACK 与 GET GPIO 读回；连接重建重置安全授权，禁止把 ACK 写成机械到位；IGN 回读按钮。
- `ManualPhysicalSafetyTests`：只进行纯代码授权栅栏验证，涵盖二字节载荷、授权一次性、错误夹具、跨世代、RESET、TEST lease 拒绝及旧 SOL 指令拒绝。**没有接入 Mock 硬件，也不构成现场实测。**

## 本轮绝不冒充已完成的事项

1. **Main_Control v0.2 仍未 Keil 编译、烧录、USB/J1 实测；按键板源码身份也未确认实物烧录。** 设备自报版本不能替代比对实际固件 image。
2. 上电 `IGN=HIGH`、`RL_DRV=HIGH`，夹具初始化 LOW；USB 枚举丢失后固件可能释放夹具。须先核对真实线束、气路，避免断线导致意外松开。
3. MCU GPIO 回读不是 J1 输出电压、夹具机械位置、KEY 五路电压或 DUT 业务反馈。
4. CAN1 周期发送/明确停止、命名 KEY 连续动作持久化与真实执行、测试项目级统一 Run 仍待接线；不能拿多个单用例 Run 冒充一个项目 Run。
5. 目前未完成 Windows 本地编译/启动与现场设备验证，禁止给出 PASS 或“已验证可用”结论。

## 工程师现场联调次序

先只连主控，验证 WinUSB GUID、端点、PING/PONG、GET_VERSION；再逐项 CAN1 真实 TX/RX、CAN0→KEY_SIM 的 ID 0x730 与五路电压、IGN 0x12/0x13 与 J1 C6 电平。夹具必须先核对危险区、实际接线与气路，再做 `0x19` 无动作读回；确认安全后分别对夹具1/2请求0/1，检查四种组合 00/01/10/11 的 MCU 读回、J1 电压及机械动作。最后测试断连/复位的实体后果。自动 TEST 随后使用同一个 `HardwareConnectionService` 执行已验证的通路。

**本文件是上位机按 v0.2 契约接线的审查与现场验收依据，不是硬件验收签发。**
