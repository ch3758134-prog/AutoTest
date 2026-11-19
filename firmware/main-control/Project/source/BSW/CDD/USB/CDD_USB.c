/**
 * @file    CDD_USB.c
 * @brief   上位机通信帧协议（0xA5 原子指令帧）— 实现
 *
 * 调用链路：CDD_USB.c → EAL_USB.c → source/USB/
 *
 * 从 main.c 搬入（2026-08-10，USB 业务封装）：
 *   - Crc16Modbus / ParseUsbRx / SendResponse（改 CDD_USB_SendFrame）
 *
 * 健壮性改进：接收改为【持久累积缓冲】——字节流攒齐完整帧再解析，
 * 一帧跨多个 bulk 包 / 跨主循环周期不再丢半帧（原 main.c 快照后丢弃剩余）。
 */

#include "CDD_USB.h"
#include "EAL_USB.h"
#include <string.h>

/* 最大请求帧长：8 + 255(payload) = 263 */
#define CDD_USB_MAX_FRAME    (CDD_USB_FRAME_REQ_MIN + 255U)

/* ============================================
 * 帧回调（由上层 ASW_UsbSvc 注册）
 * ============================================ */
static CDD_USB_FrameHandler s_handler = NULL;

/* ============================================
 * 持久累积缓冲（跨周期攒帧）
 * ============================================ */
static uint8_t  s_acc[CDD_USB_MAX_FRAME + 1];
static uint16_t s_acc_len = 0;

/* ============================================
 * 诊断 watch 变量（调试器 Watch 窗口观察）
 * ============================================ */
volatile uint32_t g_frame_ok  = 0;   /* 解析出的完整指令帧数 */
volatile uint32_t g_frame_err = 0;   /* 坏帧数（坏 SOF/坏 CRC/坏 EOF）*/

/* ============================================
 * 工具：软件 MODBUS CRC16（poly 0x8005，init 0xFFFF）
 * 覆盖 [buf .. buf+len) 字节，返回 CRC 低字节在前(小端)。
 * ============================================ */
static uint16_t Crc16Modbus(const uint8_t *buf, uint16_t len)
{
    uint16_t crc = 0xFFFFU;
    uint16_t i, j;
    for (i = 0; i < len; i++)
    {
        crc ^= buf[i];
        for (j = 0; j < 8; j++)
        {
            if (crc & 0x0001U)
                crc = (crc >> 1) ^ 0xA001U;
            else
                crc >>= 1;
        }
    }
    return crc;
}

/* ============================================
 * 解析累积缓冲中的完整帧并分派（已消费前缀移除，半帧保留）
 * 仅本文件调用；s_acc / s_acc_len 只被 CDD_USB_Task 与 ParseAccumulated 访问，
 * 均在主循环上下文，无并发竞争。
 * ============================================ */
static void ParseAccumulated(void)
{
    uint16_t i = 0;

    while ((i + CDD_USB_FRAME_REQ_MIN) <= s_acc_len)
    {
        uint8_t  len;
        uint16_t total, crc_calc, crc_recv;

        /* 找 SOF */
        if (s_acc[i] != CDD_USB_FRAME_SOF)
        {
            i++;
            g_frame_err++;
            continue;
        }
        len = s_acc[i + 4];                             /* 请求帧 LEN 在偏移 4 */
        total = (uint16_t)(CDD_USB_FRAME_REQ_MIN + len);   /* 8 + len */
        if ((i + total) > s_acc_len)
        {
            break;                                      /* 数据未到齐，保留待下次 */
        }
        if (s_acc[i + total - 1] != CDD_USB_FRAME_EOF)
        {
            i++;
            g_frame_err++;
            continue;
        }
        /* 校验 CRC：覆盖 SEQ..PAYLOAD = 偏移 1..(4+len) 共 4+len 字节 */
        crc_calc = Crc16Modbus(&s_acc[i + 1], (uint16_t)(4 + len));
        crc_recv = (uint16_t)(s_acc[i + total - 3] | ((uint16_t)s_acc[i + total - 2] << 8));
        if (crc_calc != crc_recv)
        {
            i++;
            g_frame_err++;
            continue;
        }
        /* 有效帧 → 分派给上层（ASW_UsbSvc）*/
        g_frame_ok++;
        if (s_handler != NULL)
        {
            uint16_t seq = (uint16_t)(s_acc[i + 1] | ((uint16_t)s_acc[i + 2] << 8));
            s_handler(seq, s_acc[i + 3], &s_acc[i + 5], s_acc[i + 4]);
        }
        i += total;
    }

    /* 移除已消费前缀；若 i 停在半帧处，保留 [i..s_acc_len) 待下次 */
    if (i >= s_acc_len)
    {
        s_acc_len = 0;
    }
    else if (i > 0)
    {
        memmove(s_acc, &s_acc[i], (size_t)(s_acc_len - i));
        s_acc_len -= i;
    }
}

/* ============================================
 * 接口实现
 * ============================================ */
void CDD_USB_Init(void)
{
    s_acc_len = 0;
}

void CDD_USB_Task(void)
{
    uint8_t  chunk[64];
    uint16_t n, i;

    if (!EAL_USB_IsEnumReady())
        return;

    n = EAL_USB_RxRead(chunk, sizeof(chunk));
    while (n > 0)
    {
        /* 追加到累积缓冲 */
        for (i = 0; i < n && s_acc_len < sizeof(s_acc); i++)
            s_acc[s_acc_len++] = chunk[i];
        /* 攒出完整帧再解析分派 */
        ParseAccumulated();
        n = EAL_USB_RxRead(chunk, sizeof(chunk));
    }
}

void CDD_USB_RegisterFrameHandler(CDD_USB_FrameHandler h)
{
    s_handler = h;
}

void CDD_USB_SendFrame(uint16_t seq, uint8_t cmd, uint8_t status,
                       const uint8_t *payload, uint8_t len)
{
    uint8_t  frame[CDD_USB_FRAME_RESP_HEAD + 255 + CDD_USB_FRAME_RESP_TAIL];
    uint16_t crc, n = 0;
    uint8_t  i;

    frame[n++] = CDD_USB_FRAME_SOF;
    frame[n++] = (uint8_t)(seq & 0xFF);
    frame[n++] = (uint8_t)(seq >> 8);
    frame[n++] = cmd;
    frame[n++] = status;
    frame[n++] = len;
    if (payload != NULL && len > 0)
    {
        for (i = 0; i < len; i++) frame[n++] = payload[i];
    }
    /* CRC 覆盖 SEQ..PAYLOAD（不含 SOF/EOF）*/
    crc = Crc16Modbus(&frame[1], (uint16_t)(n - 1));
    frame[n++] = (uint8_t)(crc & 0xFF);
    frame[n++] = (uint8_t)(crc >> 8);
    frame[n++] = CDD_USB_FRAME_EOF;

    EAL_USB_TxSendPrio(frame, n,
                       (seq == 0U) ? EAL_USB_TX_PRIO_LOW : EAL_USB_TX_PRIO_HIGH);
}
