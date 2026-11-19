# AutoTest · UDS Lab (L1 candidate)

Branch: feature/uds-lab-20261118. Independent WPF program \`AutoTest.UdsLab.exe\`; does not modify G4 TEST or FlashLab.

## Build and start on Windows

\`\`\`powershell
dotnet build src/AutoTest.UdsLab/AutoTest.UdsLab.csproj -c Debug
dotnet run --project src/AutoTest.UdsLab/AutoTest.UdsLab.csproj -c Debug
\`\`\`

Requires the solution's .NET 10 SDK and a recognized real main-control board with WinUSB driver. **This commit has not been Windows-built, launched, or verified with a real ECU.** The project owner/Codex should report build and live results separately.

## Usage

1. Do **not** run AutoTest.Desktop, FlashLab, or any other USB/CAN controller program at the same time.
2. Enter the ECU diagnostic request/response CAN IDs (hex; do not guess from a different ECU), P2 and P2*. ISO-TP **Normal** addressing on DUT external connector is currently supported.
3. Connect. A read-only USB Ping and ISO-TP receive subscription are started; no ECU diagnostic requests are sent automatically.
4. Add/edit/copy a named diagnostic action. Enter the UDS **PDU** only (e.g. \`22 F1 90\` **if your ECU actually supports this DID**), optional response prefix, decoding offset/type/scale/unit. The included VIN action is **an unverified example**, not a claim of support.
5. Select "Execute once". Only approved read-only services 0x19/0x22/0x23/0x24 pass the L1 UI preflight and shared UdsClient read-only gate. In particular, 0x2A is disabled because it can activate ongoing ECU behavior. A human must type the full exact 15-byte CAN frame authorization string for **every** physical CAN TX, including ISO-TP flow-control frames. The WinUSB PhysicalCommandFence enforces this token and consumes it before writing.
6. Inspect status, raw response, decoded value, negative NRC and trace. Preserve the exported JSON evidence when reporting issues.
7. Cancelled, interrupted, uncertain or protocol-exception sessions require disconnect/reconnect before another request, to avoid treating stale ISO-TP bytes as new ECU responses.
8. Save/open the diagnostic workspace JSON. It is a **diagnostic action collection**, not a TEST StepNode document or ODX import.

## Current technical limits

- Manual exact-frame confirmation is deliberately cumbersome: it is the **existing hardware safety contract**, not an AutoTest UdsLab invention. Multi-frame interaction and tight ECU timing can fail while waiting for consent, especially for a flow-control response. Do not silently auto-authorize expected frames. A reviewed, bounded read-only UDS operation-level physical permit must be designed with DEVICE/PM before smooth multi-frame/continuous monitoring can be offered.
- The shared \`UdsClient\` does not yet offer managed diagnostic sessions, state changing SIDs, ECU reset, security access, writes, routine execution, continuous polling, ODX import, or arbitrary unknown SIDs. Never bypass that with the raw editor.
- 0x22 positive DID echo is checked as currently implemented for simple DID requests; multi-DID composite decoding and a complete DTC/ODX parser are not implemented. Custom decoder currently supports ASCII, uint8, uint16 LE/BE, raw hex; offsets are into the full response PDU.
- The shared UDS session's CAN transmit lease is 2 minutes and is not renewed by this L1 client. After expiration or failure, disconnect/reconnect. An acknowledged USB CAN TX is **not** proof of ECU reception.
- The screenshot-like UI and error/event traces do **not** constitute field pass. Live-service matrix and ECU-specific IDs must be recorded from the real test.

## Scope / governance

- Source: \`src/AutoTest.UdsLab/\` and a .sln project entry only.
- Reuses \`src/AutoTest.Specialists/Uds\` and \`src/AutoTest.Devices\` unchanged.
- No mock device success path, no duplicate CAN/ISO-TP/UDS interpreter, no G4 step edits, no FLASH image/programming.
- Contract: \`docs/uds-lab/START_HERE.md\`, management branch \`Project_Management\` -> \`UDS-DEDICATED-BRANCH-AND-REAL-LAB-20261118.md\`.
- Candidate status: **SOURCE_IMPLEMENTED / WINDOWS_BUILD_NOT_RUN / FIELD_NOT_VERIFIED**.
