#include "sys_include.h"

  extern near void _EntryPoint(void);

  void (* near const _vect[])(void) @0xFFC0 = { /* Interrupt vector table */
         Cpu_Interrupt,                /* Int.no. 31 VReserved31 (at FFC0)           Unassigned */
         Cpu_Interrupt,                /* Int.no. 30 Vacmp1 (at FFC2)                Unassigned */
         Cpu_Interrupt,                /* Int.no. 29 Vcantx (at FFC4)                Unassigned */
         Drv_CAN_RX_ISR,                /* Int.no. 28 Vcanrx (at FFC6)                Unassigned */
         Drv_CAN_Err_ISR,                /* Int.no. 27 Vcanerr (at FFC8)               Unassigned */
         Cpu_Interrupt,                /* Int.no. 26 Vcanwu (at FFCA)                Unassigned */
         Cpu_Interrupt,                /* Int.no. 25 Vrtc (at FFCC)                  Unassigned */
         Cpu_Interrupt,                /* Int.no. 24 Viic (at FFCE)                  Unassigned */
         Cpu_Interrupt,                /* Int.no. 23 Vadc (at FFD0)                  Used */
         Cpu_Interrupt,                /* Int.no. 22 Vport (at FFD2)                 Unassigned */
         Cpu_Interrupt,                /* Int.no. 21 Vsci2tx (at FFD4)               Unassigned */
         Cpu_Interrupt,                /* Int.no. 20 Vsci2rx (at FFD6)               Unassigned */
         Cpu_Interrupt,                /* Int.no. 19 Vsci2err (at FFD8)              Unassigned */
         Cpu_Interrupt,                /* Int.no. 18 Vsci1tx (at FFDA)               Unassigned */
         Cpu_Interrupt,                /* Int.no. 17 Vsci1rx (at FFDC)               Unassigned */
         Cpu_Interrupt,                /* Int.no. 16 Vsci1err (at FFDE)              Unassigned */
         Cpu_Interrupt,                /* Int.no. 15 Vspi (at FFE0)                  Unassigned */
         Cpu_Interrupt,                /* Int.no. 14 Vtpm2ovf (at FFE2)              Unassigned */
         Cpu_Interrupt,                /* Int.no. 13 Vtpm2ch1 (at FFE4)              Unassigned */
         Cpu_Interrupt,                /* Int.no. 12 Vtpm2ch0 (at FFE6)              Unassigned */
         Timer_1ms_Interrupt,          /* Int.no. 11 Vtpm1ovf (at FFE8)              Unassigned */
         Cpu_Interrupt,                /* Int.no. 10 VReserved10 (at FFEA)           Unassigned */
         Cpu_Interrupt,                /* Int.no.  9 VReserved9 (at FFEC)            Unassigned */
         Cpu_Interrupt,                /* Int.no.  8 Vtpm1ch3 (at FFEE)              Unassigned */
         Cpu_Interrupt,                /* Int.no.  7 Vtpm1ch2 (at FFF0)              Unassigned */
         Cpu_Interrupt,                /* Int.no.  6 Vtpm1ch1 (at FFF2)              Unassigned */
         Cpu_Interrupt,                /* Int.no.  5 Vtpm1ch0 (at FFF4)              Unassigned */
         Cpu_Interrupt,                /* Int.no.  4 Vlol (at FFF6)                  Unassigned */
         Cpu_Interrupt,                /* Int.no.  3 Vlvd (at FFF8)                  Unassigned */
         Cpu_Interrupt,                /* Int.no.  2 Virq (at FFFA)                  Unassigned */
         Cpu_Interrupt//,                /* Int.no.  1 Vswi (at FFFC)                  Unassigned */
        // _EntryPoint                   /* Int.no.  0 Vreset (at FFFE)                Reset vector */
};



