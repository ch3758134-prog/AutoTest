# Main_Control — AutoTest main controller firmware

This branch is reserved for the **main controller firmware** (Keil / MK20DN512). It is separate from the Windows host application on `main`.

The frozen initial source is from `EPB_Test_Keil.zip / EPB_Test_Keil/Project/`, independently matched 726/726 paths to the `Project/` tree in the archived `FW-MAIN-Project.zip`.

Source ZIP verified SHA-256: `34b7e11b7d5d0fbb7b2178070f52e41febd88c15033a215516084ca294900239`.

Active project entry: `Project/Template.uvprojx`.

**Source snapshot only:** not a claim that a firmware image was built, flashed, or tested. Hardware schematics and engineering records remain under project management.

## Current board-facing contracts (2026-11-26)

- ECU CAN: J1 A12(H)/A1(L) = `ECU_CAN_PORT` (MCU FlexCAN1, USB channel 1); KEY CAN: J1 B11(H)/B1(L) = `KEY_CAN_PORT` (MCU FlexCAN0, USB channel 0). Both initialize at 500 kbit/s; only ECU CAN supports runtime bitrate selection (USB 0x42 SET / 0x43 GET, code 0..7 → 20k/50k/100k/125k/250k/500k/800k/1M). KEY CAN stays at 500k; runtime changes are not persisted.
- IGN: HOUT1 / PTC1 / J1 C10 (`0x12`/`0x13`); LED1–LED6: HOUT2–HOUT7 (`0x1A`/`0x1B`/`0x1C`); two independent clamps: J1 B5 and B4 (`0x18`/`0x19`).
- **Approved startup behavior (2026-11-18): all external high-side output controls LOW**, including IGN, LEDs, clamps, HOUT9/10 and the unallocated U22 CH4 input. **RL_DRV is strictly board-internal hardware enable**: EAL initializes it HIGH only after all controlled output GPIOs are LOW. USB/ASW never changes RL_DRV. No changes to GPIO USB opcodes; the independent ECU CAN bitrate commands 0x42/0x43 are documented separately. Transient and wiring validation remains pending.
- USB routing for those outputs is implemented by `ASW_IoCtrl.c/.h` (old PwrCtrl source removed). External KEY1–KEY3 on PTC7/6/5 (J1 C17/C18/C16) now use PORTC both-edge IRQ, 20ms debounce and read-only USB 0x44 events / 0x45 level snapshot. The physical pressed polarity remains unverified; these pins are NOT KEY_SIM K1–K20.
- See [`docs/interfaces/BOARD_FIXED_IO_MAP.md`](docs/interfaces/BOARD_FIXED_IO_MAP.md) and [`docs/interfaces/USB_HOST_CONTROL_INTERFACE.md`](docs/interfaces/USB_HOST_CONTROL_INTERFACE.md) for the current source contract.
- Updated human-review HTML (historical schematic images with current firmware mapping): [`HW-PIN-REVIEW.html`](docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/HW-PIN-REVIEW.html).

**Verification limits:** Keil build, flashed-image fingerprint, actual connector voltage, ECU/KEY CAN traffic, clamp motion, and external harness continuity have not been certified by this branch.
