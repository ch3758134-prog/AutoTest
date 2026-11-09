# Flash Lab | physical bench candidate | 2026-11-09

## Branch and scope

Branch: `flash/physical-lab-20261109`, based on `integration/r1-real-specialist-host-20261109`.
The existing Flash Dry-run/Mock view is unchanged. Open the specialist ModuleToolsWindow and click
`USB / CAN1 / UDS / FLASH 实测窗口` in the upper header.

Hardware mapping is sourced from current main-controller firmware and AutoTest
`HardwareCanReceiveBridge`: physical CAN0 -> KEY_SIM, physical CAN1 -> DUT/ECU.
There is ONE owner `HardwareBenchController` and ONE WinUSB session per Flash Lab window.
Do not open multiple Flash Lab windows on the same controller.

## Implemented and ready for Windows verification

- Enumerate only the firmware's known WinUSB interface (VID 1183, PID 5850, dedicated interface GUID).
- Connect/disconnect that exact device, verify controller version through firmware 0xF1.
- Read controller GPIO state through read-only 0x1F; **not measured electrical voltage**.
- Observe live physical CAN1 DUT frames from USB async 0x41; preserve raw USB evidence to local JSONL trace.
- Import and validate CanTool `Configuration.xml` settings for APP/Boot request/response IDs,
  flash address range, baud rate, block length and Seed/Key DLL path.
- Read BIN/HEX/S-record image file metadata and SHA-256. This is *not* full image address parsing.
- Side-effect-free protocol builders matching the supplied CanTool `FlashSequence.cs`:
  erase 31 01 FF00, download 34 00 44, transfer 36, transfer exit 37,
  verify 31 01 FF01 and complement-of-sum host checksum. Verification requires ECU checksum.
- Regression tests for protocol PDU shape, transfer chunk splitting and rejection behavior.

## NOT implemented / blocked for actual ECU programming

- The current `HardwareBenchController.Hub` has NO physical TX delegate.
- Existing `PhysicalCommandFence` allows explicitly authorized one-frame CAN TX only,
  not a long-lived programming-session transport.
- No live CAN1 ISO-TP transmit/flow-control/UDS exchange is wired into Flash Lab.
- The CanTool binary is NOT imported or executed. Seed/Key DLL ABI is not established.
- ECU APP/Boot probe, programming session, erase, 34/36/37, checksum and reset have NOT been run.
- The controller firmware CAN baud rate is not configurable through the demonstrated WinUSB read-only
  commands; importing BaudRate does not alter hardware baud.
- Imported image address ranges are NOT yet parsed or checked against the real target flash map.

Therefore: **PHYSICAL FLASH = NOT IMPLEMENTED; PHYSICAL ECU FLASH TEST = NOT RUN**.
Never change the display or report to PASS based on USB connect, version or passive CAN traffic.

## First local test sequence (non-destructive)

```powershell
git fetch origin
git switch flash/physical-lab-20261109
git pull --ff-only
dotnet restore AutoTest.sln
dotnet build AutoTest.sln -c Release --no-restore
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CanToolProgrammingProtocolTests
dotnet run --project src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Release
```

Open Module Tools -> Flash Lab. Enumerate USB, select the main controller, Connect,
Get Version, read GPIO (observe only), and watch live CAN1 frames.
Import the real CanTool XML and firmware. Save the raw local trace file path shown in the log.
Do **NOT** erase or download the ECU in this candidate.

## Follow-on integration required for real programming

1. Integrate DEVICE's scoped real CAN1 TX implementation without deleting the
   transport fence. The existing `p1/device-readonly-uds-can1-tx-20261109`
   work supports only SIDs 19/22; it must not be mislabeled as write authorization.
2. Add one explicitly user-authorized programming session, bound to connected USB
   generation + physical CAN1 + selected ECU profile + frozen firmware SHA-256.
3. Wire the existing UDS/ISO-TP implementation and match CanTool's APP/Boot
   mode switch, security DLL ABI, erase routine, download chunk sizing, checksum,
   ECU reset and post-reset identity verification.
4. Test with negative responses, timing/flow-control, disconnection and an ECU
   with an approved recovery route before any erase attempt.

## Evidence levels

CODE_COMMITTED: Yes.
WINDOWS_BUILD: NOT_RUN in this environment (no .NET SDK/Windows here).
WINUSB_CAN1_LAB: NOT_RUN.
ECU_FLASH: NOT_RUN.
