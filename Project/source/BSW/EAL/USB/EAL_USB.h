/**
 * @file    EAL_USB.h
 * @brief   板级 USB 传输适配层 — 接口
 *
 * 封装内容：
 *   - USB 栈初始化 / 连接 / 枚举状态查询
 *   - USB 接收字节流（主循环轮询 FIFO 读取，内部环形缓冲）
 *   - USB 发送字节流（静态队列 + 64 字节边界尾填充，防 bulk 传输挂死）
 *
 * 调用链路：CDD → EAL_USB → source/USB/（FSL 栈）→ USB0 控制器
 *
 * 本层只提供【字节流】传输，不涉及 0xA5 帧协议（帧协议在 CDD_USB）。
 * 仅本模块允许接触 source/USB/ 栈 API。
 */
#ifndef EAL_USB_H
#define EAL_USB_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 接收环形缓冲大小（USB 中断回调写入，主循环读取）*/
#define EAL_USB_RX_BUF_SIZE   (256U)

typedef enum {
    EAL_USB_TX_PRIO_HIGH = 0,   /* 命令响应：不主动让 CAN_RX_IND 抢占 */
    EAL_USB_TX_PRIO_LOW  = 1,   /* 主动上报：队列满时允许丢弃 */
} EAL_USB_TxPrioType;

/**
 * @brief  初始化 USB：USB_Init + USB_Class_BULK_Init + USB_Connect
 * @note   内部含 WCID (MS-OS 1.0) vendor request 免驱处理（WinUSB 绑定 + GUID）
 */
void EAL_USB_Init(void);

/**
 * @brief  查询 USB 枚举是否完成
 * @return true=枚举完成，可收发数据
 */
bool EAL_USB_IsEnumReady(void);

/**
 * @brief  非阻塞读取接收字节流（FIFO 取出，临界区保护）
 * @param  buf   输出缓冲
 * @param  max   最大读取字节数
 * @return 实际读取字节数（0=无数据）
 */
uint16_t EAL_USB_RxRead(uint8_t *buf, uint16_t max);

/**
 * @brief  发送字节流（默认高优先级，兼容旧调用点）
 * @note   长度若为 64 的整数倍，自动补 1 字节 0x00 使 bulk 以 short packet
 *         正常收尾（否则 host 读满 64 会一直等而超时丢帧）。
 * @param  buf  数据缓冲
 * @param  len  字节数（正常 0xA5 响应帧最大约 267B）
 */
void EAL_USB_TxSend(const uint8_t *buf, uint16_t len);

/**
 * @brief  按优先级发送字节流（入队后异步发送）
 * @note   调用返回只表示已提交给 EAL_USB 队列或被计数丢弃，不表示 host 已收到。
 */
void EAL_USB_TxSendPrio(const uint8_t *buf, uint16_t len, EAL_USB_TxPrioType prio);

/**
 * @brief  USB TX 调度任务，可在主循环中调用；发送完成回调内也会自动调度。
 */
void EAL_USB_TxTask(void);

#ifdef __cplusplus
}
#endif

#endif /* EAL_USB_H */
