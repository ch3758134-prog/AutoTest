/**
 * @file    ASW_UsbSvc.h
 * @brief   上位机通信路由 + 协议应答 — 接口
 *
 * 本层职责（拆分后）:
 *   1. 协议级应答（自身处理 PING/GET_VERSION）
 *   2. 命令码 → 业务对象的路由（转交 PwrCtrl / BusProxy / EcuEmu）
 *   3. 初始化：注册 CDD_USB 帧回调 + 注册 BusProxy 给 EAL_CAN 收帧
 *
 * 业务对象（PwrCtrl/BusProxy/EcuEmu）只暴露 HandleCmd 接口,UsbSvc 不感知其内部。
 *
 * 调用链：ASW/UsbSvc → CDD/USB + ASW/PwrCtrl + ASW/BusProxy + ASW/EcuEmu
 */
#ifndef ASW_USBSVC_H
#define ASW_USBSVC_H

/**
 * @brief  初始化上位机通信路由
 * @note   1) 注册 CDD_USB 帧回调(本文件 HandleCommand)
 *         2) 注册 BusProxy 给 EAL_CAN0/CAN1 收帧回调(0x41 上报)
 *         3) 调用各 Role 的 Init (当前各 Role 无 Init 需求,留接口对齐)
 */
void ASW_UsbSvc_Init(void);

/**
 * @brief  主循环周期任务:调度各 Role 的 Task
 * @note   CDD_USB_Task 由本函数内调(收帧 → 路由)
 */
void ASW_UsbSvc_Task(void);

#endif /* ASW_USBSVC_H */
