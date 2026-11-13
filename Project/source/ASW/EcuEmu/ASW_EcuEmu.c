/**
 * @file    ASW_EcuEmu.c
 * @brief   ECU 整车环境模拟 — 实现
 *
 * 关键决策（grill 2026-08-10）：
 *   - 不引合作式调度器；每个广播帧自己持有 last_tick,Task 轮询对比
 *   - START/STOP 幂等：已在 running 时再 START 不破坏状态；已停时再 STOP 也回 OK
 *   - SET_BROADCAST 在 START 中：先静默 STOP → 装填新帧表 → 不自动重启
 *     （上位机拿回显式控制权；如需启动必须再发 START）
 *   - USB 断开（enum=0）→ 静默 STOP（避免无人监控的持续输出）
 */

#include "ASW_EcuEmu.h"
#include "EAL_CAN.h"
#include "EAL_TIME.h"
#include "EAL_USB.h"     /* EAL_USB_IsEnumReady —— USB 断开检测 */
#include <stddef.h>      /* NULL */

#define ECU_EMU_MAX_FRAMES   8U       /* count > 8 → ERR_FMT */

typedef struct {
    uint32_t id;
    uint16_t period_ms;
    uint8_t  dlc;
    uint8_t  ide;
    uint8_t  data[8];
    uint32_t last_tick;       /* EAL_TIME ticks */
} EcuEmuFrame;

static EcuEmuFrame g_table[ECU_EMU_MAX_FRAMES];
static uint8_t     g_count   = 0;    /* 帧表大小 */
static uint8_t     g_running = 0;    /* 1=正在广播 */

/* ============================================
 * SET_BROADCAST: 装填帧表
 * payload = {count, frame×N}, frame={ide,id(4LE),period_ms(2LE),dlc,data[8]} = 16B
 * ============================================ */
static void on_set_broadcast(uint16_t seq, const uint8_t *p, uint8_t len)
{
    uint8_t count, i, k;

    if (len < 1U) goto err_fmt;
    count = p[0];
    if (count == 0U || count > ECU_EMU_MAX_FRAMES) goto err_fmt;
    if (len != (uint8_t)(1U + (uint16_t)count * 16U)) goto err_fmt;

    /* 字段合法性（任一帧字段非法 → 整帧拒绝,帧表不变） */
    for (i = 0; i < count; i++)
    {
        const uint8_t *ff = &p[1 + i * 16U];
        if (ff[7] > 8U) goto err_fmt;                              /* dlc 非法 */
        if ((uint16_t)(ff[5] | ((uint16_t)ff[6] << 8)) < 5U) goto err_fmt; /* period < 5ms */
        if ((uint16_t)(ff[5] | ((uint16_t)ff[6] << 8)) > 60000U) goto err_fmt;
    }

    /* 静默 STOP（保留旧帧表内容;但下面要覆盖,所以无所谓） */
    g_running = 0;

    /* 装填新帧表 */
    for (i = 0; i < count; i++)
    {
        const uint8_t *ff = &p[1 + i * 16U];
        g_table[i].ide       = ff[0];
        g_table[i].id        = (uint32_t)ff[1] | ((uint32_t)ff[2] << 8) |
                                ((uint32_t)ff[3] << 16) | ((uint32_t)ff[4] << 24);
        g_table[i].period_ms = (uint16_t)(ff[5] | ((uint16_t)ff[6] << 8));
        g_table[i].dlc       = ff[7];
        for (k = 0; k < 8; k++) g_table[i].data[k] = ff[8 + k];
        g_table[i].last_tick = EAL_TIME_GetTicks();
    }
    g_count = count;
    /* 不自动 START ——上位机拿回显式控制权 */
    CDD_USB_SendFrame(seq, CDD_USB_CMD_SET_BROADCAST,
                      CDD_USB_STS_OK, NULL, 0);
    return;

err_fmt:
    CDD_USB_SendFrame(seq, CDD_USB_CMD_SET_BROADCAST,
                      CDD_USB_STS_ERR_FMT, NULL, 0);
}

/* ============================================
 * BROADCAST_START: 幂等启动
 * ============================================ */
static void on_start(uint16_t seq)
{
    uint8_t   i;
    uint32_t now = EAL_TIME_GetTicks();
    for (i = 0; i < g_count; i++) g_table[i].last_tick = now;
    g_running = 1;
    CDD_USB_SendFrame(seq, CDD_USB_CMD_BROADCAST_START,
                      CDD_USB_STS_OK, NULL, 0);
}

/* ============================================
 * BROADCAST_STOP: 幂等停止（保留帧表）
 * ============================================ */
static void on_stop(uint16_t seq)
{
    g_running = 0;
    CDD_USB_SendFrame(seq, CDD_USB_CMD_BROADCAST_STOP,
                      CDD_USB_STS_OK, NULL, 0);
}

/* ============================================
 * BROADCAST_STATUS: 查询 (running, count)
 * ============================================ */
static void on_status(uint16_t seq)
{
    uint8_t st[2] = { g_running, g_count };
    CDD_USB_SendFrame(seq, CDD_USB_CMD_BROADCAST_STATUS,
                      CDD_USB_STS_OK, st, 2);
}

/* ============================================
 * 接口:命令分派
 * ============================================ */
void ASW_EcuEmu_HandleCmd(uint16_t seq, uint8_t cmd,
                          const uint8_t *payload, uint8_t len)
{
    switch (cmd)
    {
        case CDD_USB_CMD_SET_BROADCAST:    on_set_broadcast(seq, payload, len); break;
        case CDD_USB_CMD_BROADCAST_START:  on_start(seq);                       break;
        case CDD_USB_CMD_BROADCAST_STOP:   on_stop(seq);                        break;
        case CDD_USB_CMD_BROADCAST_STATUS: on_status(seq);                      break;
        default:
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_CMD, NULL, 0);
            break;
    }
}

/* ============================================
 * 接口:周期任务
 *   1. USB 断开（enum=0）→ 静默 STOP（firmware-requirements.md §7.2）
 *   2. 各帧 now-last_tick >= period_ms → 发 CAN1,更新 last_tick
 * ============================================ */
void ASW_EcuEmu_Task(void)
{
    uint8_t i, k;
    uint32_t now;

    if (g_count == 0U || g_running == 0U) return;

    /* USB 断开:直接停,不发任何响应（上位机已不在） */
    if (!EAL_USB_IsEnumReady())
    {
        g_running = 0;
        return;
    }

    now = EAL_TIME_GetTicks();
    for (i = 0; i < g_count; i++)
    {
        EAL_CAN_FrameType f;
        if ((now - g_table[i].last_tick) >= EAL_TIME_MS_TO_TICKS(g_table[i].period_ms))
        {
            f.id  = g_table[i].id;
            f.dlc = g_table[i].dlc;
            f.ide = g_table[i].ide;
            f.rtr = 0;
            for (k = 0; k < 8; k++) f.data[k] = g_table[i].data[k];
            /* 发送成功才更新 last_tick —— BUSY 时本帧下轮再补,避免丢帧 */
            if (EAL_CAN_Send(EAL_CAN1, &f) == EAL_CAN_OK)
                g_table[i].last_tick = now;
        }
    }
}
