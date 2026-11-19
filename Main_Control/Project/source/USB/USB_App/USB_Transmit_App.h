
#ifndef _USB_TRANSMIT_APP_H
#define _USB_TRANSMIT_APP_H

#include "stdint.h"
#include "types.h"


#define  CONTROLLER_ID      (0)   /* ID to identify USB CONTROLLER */ 
#define  KBI_STAT_MASK      (0x0F)
#define  DATA_BUFF_SIZE     (128)
#define  CAN_SEND_QUEUE_SIZE 128


typedef  struct  _VCI_BOARD_INFO{
	uint16_t	hw_Version;
	uint16_t	fw_Version;
	uint16_t	dr_Version;
	uint16_t	in_Version;
	uint16_t	irq_Num;
	uint8_t	can_Num;
	int8_t	str_Serial_Num[8];
	int8_t	str_hw_Type[16];
	int8_t	str_Usb_Serial[4][4];
} VCI_BOARD_INFO,*PVCI_BOARD_INFO;

typedef  struct  _VCI_CAN_OBJ{
	uint32_t	ID;
	uint32_t	TimeStamp;											
	uint8_t	Data[8];	
	uint8_t	DataLen;
}VCI_CAN_OBJ,*PVCI_CAN_OBJ;


typedef  struct  _VCI_SEND_CAN_OBJ{
	uint32_t	ID;
	uint32_t	TimeStamp;				
	uint8_t	TimeFlag;				
	uint8_t	SendType;				
	uint8_t	RemoteFlag;				
	uint8_t	ExternFlag;				
	uint8_t	DataLen;
	uint8_t	Data[8];
	uint8_t	Reserved[3];			
}VCI_SEND_CAN_OBJ,*PVCI_SEND_CAN_OBJ;

typedef struct _VCI_CAN_STATUS{
	
	uint8_t	ErrInterrupt;
	uint8_t	regMode;
	uint8_t	regStatus;
	uint8_t	regALCapture;
	uint8_t	regECCapture; 
	uint8_t	regEWLimit;
	uint8_t	regRECounter; 
	uint8_t	regTECounter;
	uint32_t	Reserved;
}VCI_CAN_STATUS,*PVCI_CAN_STATUS;

typedef struct _INIT_CONFIG{
	uint32_t	AccCode;
	uint32_t	AccMask;
	uint32_t	Reserved;
	uint8_t	Filter;
	uint8_t	kCanBaud;
	uint8_t	Timing0;	
	uint8_t	Timing1;	
	uint8_t	Mode;
  uint8_t	CanRx_IER;
}VCI_INIT_CONFIG,*PVCI_INIT_CONFIG;

typedef struct _REF_NORMAL{
	uint8_t Mode;				
	uint8_t Filter;			
	uint32_t AccCode;			
	uint32_t AccMask;			
	uint8_t kBaudRate;			
	uint8_t Timing0;
	uint8_t Timing1;
	uint8_t CANRX_EN;			
	uint8_t UARTBAUD;			
}VCI_REF_NORMAL,*PVCI_REF_NORMAL;	

typedef struct _BAUD_TYPE{
	uint32_t Baud;				
	uint8_t SJW;				
	uint8_t BRP;				
	uint8_t SAM;				
	uint8_t PHSEG2_SEL;		
	uint8_t PRSEG;				
	uint8_t PHSEG1;			
	uint8_t PHSEG2;			
}VCI_BAUD_TYPE,*PVCI_BAUD_TYPE;

typedef struct _RX_MSG{
	uint8_t Cmd;				
	uint8_t DevType;				
	uint16_t BufLen;
  uint8_t buf[];	
}USB_RX_MSG;


typedef struct {
	uint16_t     giRxBuffNum;			//缓冲区数据量
  uint16_t     giRxBuffGet;   //out
  uint16_t     giRxBuffPut;   //in
  VCI_CAN_OBJ  msg[CAN_SEND_QUEUE_SIZE];
} msgQueue_t;


typedef struct {
	uint16_t     giRxBuffNum;			//缓冲区数据量
  uint16_t     giRxBuffGet;   //out
  uint16_t     giRxBuffPut;   //in
  VCI_SEND_CAN_OBJ  msg[CAN_SEND_QUEUE_SIZE];
} canTxmsgQueue_t;


#define Can_Init      0x01
#define Can_Start     0x02
#define Can_Reset     0x03
#define Can_ClrBuf    0x04
#define Can_BusOffRecovery    0x05
#define CAN_Transmit_In  0x0a
#define CAN_Transmit_Out  0x0b
#define CAN_Transmit_Confirm  0x0c
#define Can_CTR        0x0f


#define KLine_Init  0x10
#define KLine_Start  0x11
#define KLine_Reset  0x12
#define KLine_Transmit_In  0x13
#define KLine_Transmit_Out  0x14
#define KLine_Transmit_Confirm  0x15

#if 0
#define AirALV1ECtr            0x20    //推力气缸控制
#define AirALV2ECtr            0x21    //压力气缸控制
#define SysPowerCtr           0x22    //电源控制
#define IgnPowerCtr           0x23    //IGN控制
#define LampCtr               0x24    //报警灯控制

#define        ValueFLZCtr  0x27
#define        ValueFRZCtr  0x28
#define        ValueRLZCtr  0x29
#define        ValueRRZCtr  0x30
#define        ValueMLZCtr  0x31
#define        ValueMRZCtr  0x32

#define        ValueFLJCtr  0x33
#define        ValueFRJCtr  0x34
#define        ValueRLJCtr  0x35
#define        ValueRRJCtr  0x36
#define        ValueMLJCtr  0x37
#define        ValueMRJCtr  0x38
#define        ValueASRCtr  0x45


#define AbsAlarmCtr  0x39
#define AsrAlarmCtr  0x40
#define RETAlarmCtr  0x41

#define BrkSwitchCtr  0x42
#define AbsSwitchCtr  0x43
#define AsrSwitchCtr  0x44


#define WheelSpeedAnalog      0x26    //轮速模拟

#define ReadValueState        0x27    //读电池阀控制状态
#define ReadAlarmLightState   0x28    //读报警灯状态
#endif


#define        CmdPower12V  0xd0
#define        CmdHout  0xd1
#define        CmdLout  0xd2
#define        CmdPlcMotoRun  0xd3
#define        CmdReadValueState  0xd4
#define        CmdWheelSpeedAng  0xd5
#define        CmdWheelSpeedAngFinish  0xd6
#define        CmdReadGrat2State    0xd7
#define        CmdReadSpeedLinePwm  0xd8

#define        CmdPlcScanStart  0xd9
#define        CmdPlcTestRst    0xda
#define        CmdPower24V      0xdb

#define Adc_DataIn            0xf0
#define Adc_DataStart         0xf1
#define Adc_DataStop          0xf2
#define UCTool_UpdateReq      0xf7

#define ExtSignStart           0xe0
#define ExtSignStop            0xe1
#define ExtSignReset           0xe2
#define ExtSignGrat1            0xe3
#define ExtSignGrat2            0xe4
/*****************************************************************************
 * Global Functions
 *****************************************************************************/
boolean  	IS_USB_APP_ENUM_COMPLETE(void);
uint8_t Query_UCTool_UpdateFlag(void);
boolean  	UCTool_CmdRsp(uint_8 err,uint_8 cmd,uint_8 *buf ,uint_8 len);
void Clear_UCTool_UpdateFlag(void);
void LineSpeedToPc_10ms(void);
extern void USB_Transmit_AppInit(void);
extern void USB_Transmit_AppTask(void);
extern void USB_CtrCmd_Handle(void);
#endif 
