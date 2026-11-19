
#ifndef __CAN1
#define __CAN1

#define CAN_STANDARD_FRAME_MAX_ID    0x07FFU 
#define CAN_EXTENDED_FRAME_MAX_ID    0x1FFFFFFFUL 
#define CAN_EXTENDED_FRAME_MASK      0x60000000UL
#define CAN_MAX_DATA_LEN             0x08U 
#define FULL_RX_BUF                  0x01U

#define MB_ID_IDE                    0x00080000UL
#define CAN_TX_MBUFFERS              0x03U
#define CAN_MAX_RX_FIFO              0x04U

#define CAN_STATUS_OVERRUN_MASK      0x02U
#define CAN_STATUS_TX_MASK           0x0CU
#define CAN_STATUS_RX_MASK           0x30U
#define CAN_STATUS_BOFF_MASK         0x0CU
#define CAN_STATUS_BOFF_EXT_MASK     0x40U
#define CAN_STATUS_TX_PASS_MASK      0x08U
#define CAN_STATUS_RX_PASS_MASK      0x20U
#define CAN_STATUS_TX_WARN_MASK      0x04U
#define CAN_STATUS_RX_WARN_MASK      0x10U
#define CAN_STATUS_WAKEUP_MASK       0x80U


#define GSNR_1_SendCanId             0x230
#define Diag_GSNR_Rsp_CanId          0x73c//0x7CA//0x73c
#define Diag_GSNR_RQ_FilterId        0x73b//0x7C2//0x73b
#define BCM_PO_1_FilterId            0x325
#define Diag_Fun_Rsp_FilterId        0x7df

typedef struct {                     
          byte IDR0;
          byte IDR1;
          byte IDR2;
          byte IDR3;
          byte Data[CAN_MAX_DATA_LEN];
          byte DLR;
          byte TBPR;
          byte Reserved;
          byte Reserved2;
        }TMsgBuff;                    

typedef union {
          dword dw;
          struct 
          {
            byte b0;
            byte b1;
            byte b2;
            byte b3;
          }b;
        }DwordSwap;


typedef struct {
  u32 CanId;
  u8  FrameType;
  u8  Data[8];
  u8  MsgLth;
}CanMsgInf;

#define TWO_32_FILTERS                  0x00U //ʹ������32λ������
#define FOUR_16_FILTERS                 0x01U //ʹ���ĸ�16λ������
#define EIGHT_8_FILTERS                 0x02U //ʹ�ð˸�8λ������
#define FILTER_CLOSED                   0x03U //�������ر�

#define STANDARD_FORMAT                 0x00U //��׼֡��ʽ
#define EXTENDED_FORMAT                 0x01U //��չ֡��ʽ

#define DATA_FRAME                      0x00U	//���֡
#define REMOTE_FRAME                    0x01U	//Զ��֡

#define CAN_EXTENDED_FRAME_ID           0x80000000UL  	//��չ֡�ı�ʶ������
#define CanRecievID                     0x0CF100D0      //����CAN֡��ID��ʶ��
#define Can_RcvID_EPB                 	0x18FEFA50      //����EPB��ID��ʶ��
#define canrecv_epb						0x18FEFB50		//����epb
#define DBUG_CAN1						0x730			//������Ϣ����1ID��ʶ��
#define DBUG_CAN2 						0x732			//������Ϣ����2ID��ʶ��




extern byte Drv_CAN_SendFrame(u32 CanID,u8 *MSG,u8 Len);


void Drv_CAN_ReadFrame(CanMsgInf *Msg);
u8 Drv_CAN_ReceiveFrame(CanMsgInf *Msg);

void Drv_CAN_Init(void);
void Drv_CAN_SetBaudrate(u8 baudSel);
ISR(Drv_CAN_Err_ISR);
ISR(Drv_CAN_RX_ISR) ;

Bool MSCANCheckRcvdMsg(void);


extern u8 BusOff_stata(void);
extern void Drv_CAN_Enable(void);
extern void Drv_CAN_Disable(void);
extern u8 CanRcvdMsg_Handle(void);
extern void SensorTask(void);
extern void Can_SendSensorData(void);
extern void CAN_Sent_counter_dec(void) ;



#endif 
