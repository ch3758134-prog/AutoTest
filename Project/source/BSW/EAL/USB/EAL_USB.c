/**
 * @file    EAL_USB.c
 * @brief   板级 USB 传输适配层 — 实现
 *
 * 调用链路：EAL_USB.c → source/USB/（FSL 栈）→ USB0 控制器
 *
 * 从 main.c 搬入（2026-08-10，USB 业务封装）：
 *   - UsbAppCallback / WcidVendorReq / 接收环形缓冲（原 main.c）
 *   - 发送 64 边界尾填充（原 SendResponse）
 *   - TX 静态队列，避免 USB 栈异步持有栈 buffer 指针
 * 本模块是唯一接触 source/USB/ 栈 API 的模块。
 */

#include "EAL_USB.h"
#include "USB/common/usb.h"          /* USB_Init / USB_Connect */
#include "USB/common/usb_class.h"    /* USB_APP_* 事件常量 */
#include "USB/class/usb_bulk.h"      /* USB_Class_BULK_* / DIC_* / 端点定义 */
#include "USB/common/hidef.h"        /* DisableInterrupts / EnableInterrupts */

/* ============================================
 * USB 控制器 / 端点 / 缓冲
 * ============================================ */
#define USB_CTRL_ID        (0)             /* 单控制器 */
#define USB_TX_ENDPOINT    DIC_BULK_IN_ENDPOINT1    /* 0x82 上行端点 */
#define EAL_USB_TX_BUF_MAX (300U)          /* 最大发送字节（含 64 边界尾填充）*/
#define EAL_USB_TX_HI_DEPTH  (16U)
#define EAL_USB_TX_LO_DEPTH  (16U)

typedef struct {
    uint16_t len;
    uint8_t  data[EAL_USB_TX_BUF_MAX];
} EAL_USB_TxSlotType;

typedef struct {
    EAL_USB_TxSlotType *slot;
    uint8_t depth;
    volatile uint8_t head;
    volatile uint8_t tail;
    volatile uint8_t count;
} EAL_USB_TxQueueType;

/* ============================================
 * 接收环形缓冲（USB 中断回调写入，主循环读取）
 * ============================================ */
static uint8_t           g_usb_rx[EAL_USB_RX_BUF_SIZE];
static volatile uint16_t g_usb_rx_len = 0;
static volatile uint8_t  g_usb_ready  = 0;   /* 枚举完成标志 */

static EAL_USB_TxSlotType s_tx_hi[EAL_USB_TX_HI_DEPTH];
static EAL_USB_TxSlotType s_tx_lo[EAL_USB_TX_LO_DEPTH];
static EAL_USB_TxQueueType s_txq_hi = { s_tx_hi, EAL_USB_TX_HI_DEPTH, 0U, 0U, 0U };
static EAL_USB_TxQueueType s_txq_lo = { s_tx_lo, EAL_USB_TX_LO_DEPTH, 0U, 0U, 0U };
static volatile uint8_t s_usb_tx_busy = 0U;
static volatile EAL_USB_TxPrioType s_usb_tx_active_prio = EAL_USB_TX_PRIO_HIGH;
static EAL_USB_TxSlotType * volatile s_usb_tx_active_slot = NULL;

/* ============================================
 * 诊断 watch 变量（调试器 Watch 窗口观察；g_wcid_str_req 由 usb_descriptor.c extern 递增）
 * ============================================ */
volatile uint32_t g_usb_rx_cnt   = 0;        /* USB 收到数据回调次数 */
volatile uint32_t g_usb_tx_cnt   = 0;        /* USB 发送成功字节传输次数 */
volatile uint32_t g_usb_tx_submit_ok = 0;    /* 成功提交到底层 USB IN 次数 */
volatile uint32_t g_usb_tx_submit_accepted_err = 0; /* USB 类队列已接收但底层返回错误 */
volatile uint32_t g_usb_tx_submit_busy = 0;  /* 底层 USB IN busy 次数 */
volatile uint32_t g_usb_tx_kick_fail = 0;    /* 底层提交非 busy 失败次数 */
volatile uint32_t g_usb_tx_complete = 0;     /* USB IN 发送完成回调次数 */
volatile uint32_t g_usb_tx_complete_mismatch = 0; /* 完成回调与当前发送槽不匹配 */
volatile uint32_t g_usb_tx_hi_full = 0;      /* 高优先级 TX 队列满 */
volatile uint32_t g_usb_tx_lo_full = 0;      /* 低优先级 TX 队列满 */
volatile uint32_t g_usb_tx_hi_drop = 0;      /* 高优先级帧因队列满丢弃 */
volatile uint32_t g_usb_tx_lo_drop = 0;      /* 低优先级帧因队列满丢弃 */
volatile uint32_t g_usb_tx_len_drop = 0;     /* 超长/空指针帧丢弃 */
volatile uint32_t g_wcid_str_req    = 0;     /* string 0xEE("MSFT100") 被请求数（usb_descriptor.c 递增） */
volatile uint32_t g_wcid_vendor_req = 0;     /* vendor request 回调到达数 */
volatile uint32_t g_wcid_vendor_ok  = 0;     /* vendor request 命中(返回WINUSB)数 */
volatile uint32_t g_wcid_ext_req    = 0;     /* vendor request wIndex=0x0005 扩展属性(GUID)到达数 */

/* ============================================
 * WCID (MS-OS 1.0) vendor request 回调
 * Windows 8+ 枚举流程：GET string 0xEE → "MSFT100"（usb_descriptor.c）→
 * vendor request 0xEE (wIndex=0x0004) → 本回调返回扩展兼容 ID 描述符，
 * 声明 interface 0 兼容 ID = "WINUSB"，使 Windows 自动绑定内置 winusb.sys
 * （免驱即插即用，供上位机 WinUSB API 访问）。
 * 描述符长度按 setup_packet->length（wLength）返回，栈会再按 wLength 截断。
 * ============================================ */
/* MS-OS 1.0 扩展属性描述符（vendor request 0xEE, wIndex=0x0005）：
 * 把 WinUSB 接口的 DeviceInterfaceGUID 固定为与 inf/epb_winusb.inf 相同的
 * {4da6d065-1e3e-47e6-8cc3-c6977351defb}，使 WCID 免驱路径与 INF 路径的
 * 上位机枚举结果一致（UsbDevice.cs SearchGuids 第一项即是它）。结构按 MS-OS 1.0 spec。
 * sizeof(g_wcid_ext_prop) = 142（dwLength），与 dwLength 字段一致。 */
#pragma pack(push, 1)
typedef struct _ms_os_ext_prop {
    uint32_t dwLength;              /* 总长 = 142 */
    uint16_t bcdVersion;            /* 0x0100 */
    uint16_t wIndex;                /* 0x0005 */
    uint16_t wCount;                /* 属性个数 = 1 */
    uint32_t dwSize;                /* 本属性块大小 = 132 */
    uint32_t dwPropertyDataType;    /* 1 = REG_SZ */
    uint16_t wPropertyNameLength;   /* 20 字符 × 2 = 40 字节（含 NUL） */
    uint16_t wPropertyName[20];     /* "DeviceInterfaceGUID\0" */
    uint32_t dwPropertyDataLength;  /* 39 字符 × 2 = 78 字节（含 NUL） */
    uint16_t wPropertyData[39];     /* "{4da6d065-1e3e-47e6-8cc3-c6977351defb}\0" */
} MS_OS_EXT_PROP_DESC;
#pragma pack(pop)

static const MS_OS_EXT_PROP_DESC g_wcid_ext_prop = {
    142u,                            /* dwLength */
    0x0100u,                         /* bcdVersion */
    0x0005u,                         /* wIndex */
    1u,                              /* wCount */
    132u,                            /* dwSize */
    1u,                              /* dwPropertyDataType = REG_SZ */
    40u,                             /* wPropertyNameLength */
    { 'D','e','v','i','c','e','I','n','t','e','r','f','a','c','e','G','U','I','D','\0' },
    78u,                             /* dwPropertyDataLength */
    { '{','4','d','a','6','d','0','6','5','-','1','e','3','e','-',
      '4','7','e','6','-','8','c','c','3','-','c','6','9','7','7','3','5','1',
      'd','e','f','b','}','\0' }
};

static EAL_USB_TxQueueType *EAL_USB_GetTxQueue(EAL_USB_TxPrioType prio)
{
    return (prio == EAL_USB_TX_PRIO_LOW) ? &s_txq_lo : &s_txq_hi;
}

static void EAL_USB_TxQueueClear(EAL_USB_TxQueueType *q)
{
    q->head = 0U;
    q->tail = 0U;
    q->count = 0U;
}

static uint8_t EAL_USB_TxQueuePush(EAL_USB_TxQueueType *q, const uint8_t *buf, uint16_t len)
{
    uint16_t i;

    if (q->count >= q->depth)
        return 0U;

    q->slot[q->head].len = len;
    for (i = 0; i < len; i++)
        q->slot[q->head].data[i] = buf[i];

    q->head++;
    if (q->head >= q->depth)
        q->head = 0U;
    q->count++;
    return 1U;
}

static EAL_USB_TxSlotType *EAL_USB_TxQueuePeek(EAL_USB_TxQueueType *q)
{
    if (q->count == 0U)
        return NULL;
    return &q->slot[q->tail];
}

static void EAL_USB_TxQueuePop(EAL_USB_TxQueueType *q)
{
    if (q->count == 0U)
        return;

    q->tail++;
    if (q->tail >= q->depth)
        q->tail = 0U;
    q->count--;
}

static void EAL_USB_TxKick(void)
{
    EAL_USB_TxSlotType *slot = NULL;
    EAL_USB_TxPrioType prio = EAL_USB_TX_PRIO_HIGH;
    uint_8 status;

    if (g_usb_ready == 0U)
        return;

    DisableInterrupts;
    if (s_usb_tx_busy != 0U)
    {
        EnableInterrupts;
        return;
    }

    slot = EAL_USB_TxQueuePeek(&s_txq_hi);
    if (slot != NULL)
    {
        prio = EAL_USB_TX_PRIO_HIGH;
    }
    else
    {
        slot = EAL_USB_TxQueuePeek(&s_txq_lo);
        prio = EAL_USB_TX_PRIO_LOW;
    }

    if (slot == NULL)
    {
        EnableInterrupts;
        return;
    }

    s_usb_tx_busy = 1U;
    s_usb_tx_active_prio = prio;
    s_usb_tx_active_slot = slot;
    EnableInterrupts;

    status = USB_Class_BULK_Interface_DIC_Send_Data(USB_CTRL_ID, USB_TX_ENDPOINT,
                                                    slot->data, slot->len);
    if (status == USB_OK)
    {
        g_usb_tx_submit_ok++;
        g_usb_tx_cnt++;
    }
    else if (status == USBERR_DEVICE_BUSY)
    {
        DisableInterrupts;
        s_usb_tx_busy = 0U;
        s_usb_tx_active_slot = NULL;
        EnableInterrupts;

        g_usb_tx_submit_busy++;
    }
    else
    {
        /* USB bulk 类驱动已把 app_buff 存入内部队列后才调用底层发送。
         * 非 busy 错误仍保持 active slot，等待 SEND_COMPLETE/ERROR 回调释放，
         * 避免同一槽被重复提交导致类驱动队列不同步。 */
        g_usb_tx_submit_accepted_err++;
        g_usb_tx_kick_fail++;
    }
}

static uint_8 WcidVendorReq(uint_8 controller_ID, USB_SETUP_STRUCT *req,
                            uint_8_ptr *data, USB_PACKET_SIZE *size)
{
    (void)controller_ID;
    g_wcid_vendor_req++;                  /* 诊断：vendor request 到达数 */
    if ((req->request_type == 0xC0) && (req->request == 0xEE))
    {
        if (req->index == 0x0004)         /* 扩展兼容 ID 描述符 → WINUSB 绑定 */
        {
            g_wcid_vendor_ok++;           /* 诊断：命中 WINUSB 描述符 */
            /* 扩展兼容 ID 描述符（40B，MS-OS 1.0 spec）：
             * dwLength=0x28 | bcdVersion=0x0100 | wIndex=0x0004 | bCount=1 |
             * Reserved[7] | bFirstInterface=0 | Reserved1=1 |
             * CompatibleID="WINUSB\0\0" | SubCompatibleID=全0 | Reserved2[6] */
            static const uint_8 compat_id[] = {
                0x28,0x00,0x00,0x00,                       /* dwLength */
                0x00,0x01,                                 /* bcdVersion */
                0x04,0x00,                                 /* wIndex */
                0x01,                                      /* bCount */
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,        /* Reserved[7] */
                0x00,                                      /* bFirstInterface */
                0x01,                                      /* Reserved1 */
                'W','I','N','U','S','B',0x00,0x00,         /* CompatibleID */
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,   /* SubCompatibleID */
                0x00,0x00,0x00,0x00,0x00,0x00              /* Reserved2[6] */
            };
            *data = (uint_8_ptr)compat_id;
            *size = (USB_PACKET_SIZE)sizeof(compat_id);
            return USB_OK;
        }
        if (req->index == 0x0005)         /* 扩展属性描述符 → DeviceInterfaceGUID */
        {
            g_wcid_ext_req++;             /* 诊断：扩展属性到达数 */
            *data = (uint_8_ptr)&g_wcid_ext_prop;
            *size = (USB_PACKET_SIZE)sizeof(g_wcid_ext_prop);
            return USB_OK;
        }
    }
    return USBERR_INVALID_REQ_TYPE;
}

/* ============================================
 * USB 应用回调（USB_Class_BULK_Init 注册）
 *   - 枚举完成/复位：置/清 g_usb_ready
 *   - 收到数据：拷入 g_usb_rx 环形缓冲，重新武装接收
 * ============================================ */
static void UsbAppCallback(uint_8 controller_ID, uint_8 event_type, void *val)
{
    (void)controller_ID;
    if (event_type == USB_APP_ENUM_COMPLETE)
    {
        g_usb_ready = 1;
        /* EPB 修复 2026-08-04：FSL 栈不会自动武装 bulk OUT 端点，
         * 枚举完成后必须主动 DIC_Recv_Data 挂上 0x03 的 BDT 接收缓冲，
         * 否则主机 WritePipe 永远 NAK，数据进不了固件（握手 PING 必失败）。 */
        (void)USB_Class_BULK_Interface_DIC_Recv_Data(USB_CTRL_ID, DIC_BULK_OUT_ENDPOINT1, NULL, 0);
        EAL_USB_TxKick();
    }
    else if (event_type == USB_APP_BUS_RESET)
    {
        g_usb_ready = 0;
        g_usb_rx_len = 0;
        DisableInterrupts;
        EAL_USB_TxQueueClear(&s_txq_hi);
        EAL_USB_TxQueueClear(&s_txq_lo);
        s_usb_tx_busy = 0U;
        s_usb_tx_active_slot = NULL;
        EnableInterrupts;
    }
    else if (event_type == USB_APP_DATA_RECEIVED)
    {
        PTR_USB_DEV_EVENT_STRUCT ev = (PTR_USB_DEV_EVENT_STRUCT)val;
        uint16_t copy = (uint16_t)ev->len;
        uint16_t space = EAL_USB_RX_BUF_SIZE - g_usb_rx_len;

        g_usb_rx_cnt++;
        if (copy > space) copy = space;    /* 缓冲满则丢弃尾部 */
        if (copy > 0)
        {
            uint16_t i;
            for (i = 0; i < copy; i++)
                g_usb_rx[g_usb_rx_len + i] = ev->buffer_ptr[i];
            g_usb_rx_len += copy;
        }
        /* 重新武装接收（机制同 ABS 参考工程） */
        (void)USB_Class_BULK_Interface_DIC_Recv_Data(USB_CTRL_ID, ev->ep_num, NULL, 0);
    }
    else if (event_type == USB_APP_SEND_COMPLETE)
    {
        APP_DATA_STRUCT *done = (APP_DATA_STRUCT *)val;
        uint8_t matched = 0U;

        DisableInterrupts;
        if (s_usb_tx_busy != 0U)
        {
            if ((done != NULL) && (s_usb_tx_active_slot != NULL) &&
                (done->data_ptr == s_usb_tx_active_slot->data) &&
                ((uint16_t)done->data_size == s_usb_tx_active_slot->len))
            {
                matched = 1U;
            }

            if (matched != 0U)
            {
                if (s_usb_tx_active_prio == EAL_USB_TX_PRIO_LOW)
                    EAL_USB_TxQueuePop(&s_txq_lo);
                else
                    EAL_USB_TxQueuePop(&s_txq_hi);
                s_usb_tx_busy = 0U;
                s_usb_tx_active_slot = NULL;
                g_usb_tx_complete++;
            }
            else
            {
                g_usb_tx_complete_mismatch++;
            }
        }
        EnableInterrupts;
        EAL_USB_TxKick();
    }
}

/* ============================================
 * 接口实现
 * ============================================ */
void EAL_USB_Init(void)
{
    g_usb_rx_len = 0;
    g_usb_ready  = 0;
    EAL_USB_TxQueueClear(&s_txq_hi);
    EAL_USB_TxQueueClear(&s_txq_lo);
    s_usb_tx_busy = 0U;
    s_usb_tx_active_slot = NULL;

    /* 初始化 USB 接口（使能模块/时钟/中断）*/
    USB_Init();
    if (USB_Class_BULK_Init(USB_CTRL_ID, UsbAppCallback, WcidVendorReq, NULL, TRUE) == USB_OK)
    {
        USB_Connect();
    }
}

bool EAL_USB_IsEnumReady(void)
{
    return (g_usb_ready != 0);
}

uint16_t EAL_USB_RxRead(uint8_t *buf, uint16_t max)
{
    uint16_t n, i;

    if (buf == NULL || max == 0U)
        return 0U;

    /* 临界区内 FIFO 取出，避免与 USB 中断回调竞争 */
    DisableInterrupts;
    n = (g_usb_rx_len > max) ? max : g_usb_rx_len;
    for (i = 0; i < n; i++) buf[i] = g_usb_rx[i];
    for (i = n; i < g_usb_rx_len; i++) g_usb_rx[i - n] = g_usb_rx[i];
    g_usb_rx_len -= n;
    EnableInterrupts;

    return n;
}

void EAL_USB_TxSend(const uint8_t *buf, uint16_t len)
{
    EAL_USB_TxSendPrio(buf, len, EAL_USB_TX_PRIO_HIGH);
}

void EAL_USB_TxSendPrio(const uint8_t *buf, uint16_t len, EAL_USB_TxPrioType prio)
{
    uint8_t  pkt[EAL_USB_TX_BUF_MAX];
    uint16_t n, i;
    uint8_t pushed;
    EAL_USB_TxQueueType *q;

    if (buf == NULL || len == 0U)
    {
        g_usb_tx_len_drop++;
        return;
    }
    if (len >= EAL_USB_TX_BUF_MAX)
    {
        g_usb_tx_len_drop++;
        return;                          /* 超长丢弃（正常帧最大 ~267B）*/
    }

    for (i = 0; i < len; i++) pkt[i] = buf[i];
    n = len;

    /* 防 bulk 挂死：传输长度若为 64 的整数倍，host 读不到 short packet 收尾，
     * WinUSB_ReadPipe 会一直等满而超时（本帧被丢）。补 1 字节 0x00 作尾填充，
     * 使本次传输以 short packet 正常结束；该多余字节会被 host 帧解析器当坏字节丢弃。 */
    if ((n & 0x3FU) == 0U)
    {
        pkt[n++] = 0x00U;
    }

    q = EAL_USB_GetTxQueue(prio);

    DisableInterrupts;
    pushed = EAL_USB_TxQueuePush(q, pkt, n);
    if (pushed == 0U)
    {
        if (prio == EAL_USB_TX_PRIO_LOW)
        {
            g_usb_tx_lo_full++;
            g_usb_tx_lo_drop++;
        }
        else
        {
            g_usb_tx_hi_full++;
            g_usb_tx_hi_drop++;
        }
    }
    EnableInterrupts;

    if (pushed != 0U)
        EAL_USB_TxKick();
}

void EAL_USB_TxTask(void)
{
    EAL_USB_TxKick();
}
