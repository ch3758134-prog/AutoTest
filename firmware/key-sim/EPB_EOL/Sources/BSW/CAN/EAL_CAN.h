#ifndef _EAL_CAN_H_
#define _EAL_CAN_H_
//#define C99

#define Vehicle_Type	(1)
#define TYBYD (0)
#define SZJL  (1)
#define ZHYL  (2)
#define TEST  (3)

#if Vehicle_Type == TYBYD
#define Can_Brake_ID			0x18FEF100	//�ƶ�̤��
#define Can_Speed_ID			0x18FEF100	//����
#define Can_Tank_Pressure_ID	0x18FEAE30	//�����ѹ��

#elif Vehicle_Type == SZJL
#define Can_Brake_ID			0x18F001D0	//�ƶ�̤��
#define Can_Speed_ID			0x18FEF117	//����
#define Can_Tank_Pressure_ID	0x0C41D017	//�����ѹ��
#define Can_Wheel_Speed_ID		0X18FEBF0B  //�����ź�
#define Can_Torque_ID			0X18FF20EF  //Ť���ź�
#define Can_Gear_ID				0X18F00503  //��λ�ź�
#define Can_Accelerator_ID		0X0CF00300 	//�����ź�
#define Can_Test1_ID			0x0c000001 	//���Ա���1
#define Can_Test2_ID			0x0c000002 	//���Ա���2
#define Can_Test3_ID			0x0c000003 	//���Ա���3

#elif Vehicle_Type == TEST
#define Can_Brake_ID			0x18000000	//�ƶ�̤��
#define Can_Speed_ID			0x18000001	//����
#define Can_Tank_Pressure_ID	0x18000002	//�����ѹ��
#define Can_Gear_ID				0x18000003  //��λ

#elif Vehicle_Type == ZHYL
#define Can_Brake_ID			0x1004FF1E	//�ƶ�̤��
#define Can_Speed_ID			0x1002FF1E	//����
#endif


// ����ȫ�ֱ���
extern  u8 t_BrakeSta[8];//�ƶ�̤��
extern 	u8 t_Vehicle_Speed[8];//����
extern	u8 t_Tank_Pressure[8];//���ѹ��
extern	u8 t_Wheel_Speed[8];//����
extern	u8 t_Torque[8];//Ť��
extern	u8 t_Gear[8];//��λ
extern  u8 t_Accelerator[8];//����̤��
extern  u8 t_test1[8];//���Ա���1
extern  u8 t_test2[8];//���Ա���2
extern  u8 t_test3[8];//���Ա���3



typedef enum
{
		NOfault=0,				//�޹���
		BatHigh=1,      	 	//ECU��ѹ��һ���Թ��� 
		BatLow=2,				//ECU��ѹ��һ���Թ��� 
		BatOverHigh=3,			//ECU��ѹ��߹��� 
		BatOverLow=4,			//ECU��ѹ��͹��� 
		IgnHigh=5,				//IGN����źŵ�ѹ�߹���
		IgnLow=6,				//IGN����źŵ�ѹ�͹���
		PressPowerHigh=7,		//ѹ������������߹��� 
		PressPowerLow=8,		//ѹ������������͹��� 
		PressSignalErr=9,		//ѹ���������źŹ��� 
		ValveOpen=10,			//��ŷ���·���� 
		ParkValveOpen=11,		//פ����ŷ���·���� 
		ReleaseValveOpen=12,	//�ͷŵ�ŷ���·����
		ReleaseKeyOpen=13,		//�ͷŰ���·����
		ParkKeyOpen=14,			//פ������·���� 
		AKeyOpen=15,			//A��·���� 
		ReleaseLongPress=16,	//�ͷŰ����¹��� 
		ParkKeyLongPull=17,		//פ������������� 
		AKeyLongPress=18,		//A���¹��� 
		BusOff=19,				//CAN����BUSOFF���� 
		BrakeSigFault=20,		//CAN����ɲ���źŹ���
		SpeedSigFault=21,		//CAN���복���źŹ���
		AccelSigFault=22,		//CAN���������źŹ��� 
		ReverseSigFault=23,		//CAN���뵹���źŹ��� 
		NeutralSigFault=24,		//CAN����յ��źŹ��� 
		MotorTorqFault=25,		//CAN������Ť���źŹ��� 
		MotorSpdFault=26		//CAN������ת���źŹ���
}g_EPBFaultStatus;

typedef enum
{
	no_status=0,				//��״̬
	park_off=1,					//פ�������
	park_on=2,					//פ������
	park_error=3,				//פ���������
	rele_off=4,					//�ͷŰ����
	rele_on=5,					//�ͷŰ���
	rele_error=6,				//�ͷŰ������
	epb_park=7,					//epbפ��״̬
	epb_rele=8,					//epb�ͷ�״̬
	epb_error=9,				//epb����״̬
	static_park=10,				//��̬פ��
	static_rele=11,				//��̬�ͷ�
	dynamic_park=12,			//��̬פ��
	dynamic_rele=13,			//��̬�ͷ�
	dynamic_park_end=14,		//��̬פ������
	driving_park=15,			//��ʻפ��
	car_uphill=16,				//��������
	car_downhill=17,			//��������
	car_levelup=18,				//ˮƽ����
	car_leveldown=19,			//ˮƽ����
	car_slopeup=20,				//б������
	car_slopedown=21,			//б������
	forward_gear=22,			//ǰ��
	car_neutral=23,				//�յ�
	reverse_gear=24,			//����
	break_switch_on=25,			//�ƶ�̤�����		
    break_switch_off=26			//�ƶ�̤��δ����
}g_DBUGStatus;


typedef struct {
	g_EPBFaultStatus ECUFaultstatus;
	g_EPBFaultStatus IGNFaultstatus;
	g_EPBFaultStatus ValveFaultstatus;
	g_EPBFaultStatus KEYFaultstatus;
	g_EPBFaultStatus CANFaultstatus;
	g_EPBFaultStatus EPB_status;
}EPB_Fault_Status;

typedef struct {
	g_DBUGStatus park_status;
	g_DBUGStatus rele_status;
	g_DBUGStatus epb_status;
	g_DBUGStatus dys_status;
	g_DBUGStatus hill_status;
	g_DBUGStatus level_status;
	g_DBUGStatus neutral_status;
	g_DBUGStatus gear_status;
	g_DBUGStatus break_status;
}DEBUG_Can_Status;



void CanRcvdEPB_Handle(void);
DEBUG_Can_Status* CanRcvDBUG_Handle(void);
void Can_SendMsg_Task_10ms(void); 


#endif

