#include "fsl_clock.h"

/* 硬件参数定义 */
#define XTAL0_CLK_HZ    8000000U   /* 外部晶振频率 8MHz */
#define PLL_TARGET_HZ   96000000U  /* PLL目标频率 96MHz */
#define CORE_DIV        0U         /* 内核分频系数 OUTDIV1=0 → 1分频 */
#define BUS_DIV         1U         /* 总线分频系数 OUTDIV2=1 → 2分频 → 48MHz */
#define FLEXBUS_DIV     3U         /* FlexBus分频系数 OUTDIV3=3 → 4分频 */
#define FLASH_DIV       3U         /* Flash分频系数 OUTDIV4=3 → 4分频 → 24MHz */

void Clock_Init(void)
{
    /* 1. 设置系统分频比为安全值，切换时钟时先降频 */
    CLOCK_SetSimSafeDivs();

    /* 2. 初始化外部晶振 OSC0，使能 OSCERCLK 输出 */
    osc_config_t oscConfig = {
        .freq        = XTAL0_CLK_HZ,
        .capLoad     = kOSC_Cap8P,
        .workMode    = kOSC_ModeOscLowPower,
        .oscerConfig = {
            .enableMode = kOSC_ErClkEnable
        }
    };
    CLOCK_InitOsc0(&oscConfig);
    CLOCK_SetXtal0Freq(XTAL0_CLK_HZ);

    /* 3. 手动指定PLL参考时钟和分压/倍频系数（供后续计算参考） */
    uint8_t prdiv = 1U;   /* 8MHz / (1+1) = 4MHz PLL 参考时钟 (需在 2~4MHz 范围内) */
    uint8_t vdiv  = 0U;   /* VDIV = 0 → 倍频系数 24: 4MHz × 24 = 96MHz VCO */
                          /* 寄存器值 = 倍频系数 - 24, 即 24 - 24 = 0 */

    /* 4. 配置 MCG 结构体，切换到 PEE 模式 */
    mcg_config_t mcgConfig = {
        .mcgMode         = kMCG_ModePEE,
        .oscsel          = kMCG_OscselOsc,
        .irclkEnableMode = 0U,
        .ircs            = kMCG_IrcSlow,
        .fcrdiv          = 0U,
        .frdiv           = 3U,
        .drs             = kMCG_DrsMid,
        .dmx32           = kMCG_Dmx32Default,
        .pll0Config = {
            .enableMode = kMCG_PllEnableIndependent,
            .prdiv      = prdiv,
            .vdiv       = vdiv
        }
    };

    /* 5. 执行模式切换 */
    CLOCK_SetMcgConfig(&mcgConfig);

    /* 6. 设置最终系统分频器 (PLL 锁定后再降低分频, 避免中间态过频) */
    CLOCK_SetOutDiv(CORE_DIV, BUS_DIV, FLEXBUS_DIV, FLASH_DIV);

    /* 7. USB 时钟：MCGPLLCLK(96MHz) / 2 = 48MHz（USB 模块必需精确 48MHz）。
     *    usb.c 只设了 USBSRC 和 USBDIV，没设 PLLFLLSEL；此处补上选择 MCGPLLCLK，
     *    否则 USB 时钟会错落到 FLL 输出(~20MHz)/2，全速枚举失败。
     *    （改动原因：ABS 参考工程时钟初始化设过 PLLFLLSEL，本工程 fsl_clock 版没有） */
    CLOCK_EnableUsbfs0Clock(kCLOCK_UsbSrcPll0, 96000000U);
}
