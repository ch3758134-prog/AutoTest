# Issue #17 — UDS 专业 P1 ReadOnly CAN1 接线与最小依赖申请

**CandidateId:** P1-UDS-READONLY-17  
**Host integration target:** PR #16 `integration/r1-real-specialist-host-20261109` @ `886a6775dc6bf156229124fe2fa99b16a1d61ab0` (latest verified 2026-11-09).  
**Source baseline:** `main@db2a980b0a37b7404b2a95c361cef3d506880bd5`.  
**Safety:** DO NOT enable unrestricted physical CAN transmit, change firmware, or enable Flash/Seed-Key.

## UDS-owned implementation

- `ReadOnlyPhysicalUdsWorkspace.cs`: private typed port with exactly two allowed SIDs: `0x19`, `0x22`. UDS profile validation fixes `BusRole.DutCan`, `Normal` addressing and matching 11/29-bit IDs, and requires bound Station/SessionGeneration, authorized ReadOnly Tx, live session and trace.
- `IReadOnlyPhysicalUdsSessionProvider` is the *injected* DEVICE/INTEGRATION seam. UDS never constructs WinUSB, CanFrameHub, CanHubIsoTpLink or CAN lease.
- `UdsToolViewModel(UdsDiagnosticWorkspace, IReadOnlyPhysicalUdsSessionProvider?)` supports the existing R1 P08 view. Default/no-provider stays Mock/Replay; Physical is selectable only when authorized session + endpoint are current. No fallback to Mock on Physical failure.
- A single request gets a single CorrelationId. The provider's existing UdsClient performs ISO-TP/NRC/P2/P2* and returns trace snapshots. Physical CAN evidence includes real Tx/Rx, CAN ID, DLC, SessionGeneration, device Tx ACK, host-arrival time, loss and TraceRef; no ECU timestamp is invented.
- If trace incomplete, wrong role/ID/generation, Tx ACK missing, connection altered or request timed out, response is **Uncertain** or explicitly denied, not Physical PASS. No retry/session mutation/TesterPresent.
- Export is local JSON; VIN responses and sensitive messages are redacted by the UI evidence boundary. Do not attach user raw logs, credentials, ECU IDs, or real device payload to public PR.
- This port is in UDS **private source** only; it does not modify public Contracts/ISO-TP/Device code.

## Two minimal dependency requests to main conversation

### DEVICE — bounded, device-owned read-only transport provider

Target existing shared classes: `src/AutoTest.Devices/Diagnostics/HardwareBenchController.cs`, `HardwareBenchController.Physical.cs`, `UsbSessionService`, `PhysicalCommandFence` (exact changes owned by DEVICE). The current `HardwareBenchController.Hub` has no physical transmit delegate and `CanHubIsoTpLink.SendAsync` will return `NO_TRANSMITTER`.

Implement adapter to the UDS-private `IReadOnlyPhysicalUdsSessionProvider` **only after primary approval**. Its `OpenAsync(ReadOnlyDiagnosticSessionSpec, ct)` MUST verify the same current WinUSB Session/Hub, StationIdentity, Generation, DutCan Role, Normal 11/29-bit endpoint, SID allowlist `{0x19,0x22}`, bounded operation time/frames and trace health. It must issue ONLY bounded per-request CAN1 `0x40 CAN_SEND` frames plus ISO-TP FC for that transaction; deny foreign FC, unsolicited Tx, KeySim CAN role, reconnect, expired lease and lost trace.

Provide `IReadOnlyPhysicalUdsSession.Client` as the **existing** `UdsClient` on `IsoTpUdsMessageChannel(IsoTpConversation(CanHubIsoTpLink(...)))` or approved equivalent that uses those exact implementations. `SnapshotCan(correlationId)` must return observed/ACKed physical frames for this operation, not synthetic traces; `SnapshotUds(correlationId)` returns actual `UdsTrace` entries. TX ACK is not an ECU response, only a separate CAN receipt. Must not use a second USB handle or Hub.

**Required evidence for closure:** device-owned Fake WinUSB Tx gate tests, generation/lease deny tests, a trace with physical CAN1 TX, real CAN1 RX, ISO-TP FC/CF, actual SID/NRC and correlation; raw physical logs stored locally in a controlled location, not public PR.

### INTEGRATION — one P08 constructor injection, no second session

The latest PR #16 HEAD `886a6775dc6bf156229124fe2fa99b16a1d61ab0` already exposes:
```csharp
R1SpecialistDocumentHost(
    R1SharedCanSource? sharedCan = null,
    Func<R1SharedCanSource, UdsToolViewModel>? udsFactory = null,
    Func<R1SharedCanSource, FrameworkElement>? waveFactory = null)
```
The default factory deliberately returns `new UdsToolViewModel()` (Mock/Replay only). **Do not alter the R1 Shell in this UDS branch.** Once DEVICE provides the approved read-only provider and physical permission, INTEGRATION supplies an `udsFactory` that returns `new UdsToolViewModel(new UdsDiagnosticWorkspace(), deviceBoundProvider)` from the **same** `R1SharedCanSource` / Station / Hub. Closing P08 disposes neither the shared USB Session nor unrelated consumers. If the safety port is absent or fails validation, continue using the default factory; Physical remains unavailable.

**No cross-owner code is changed in this specialist PR.**

## Mechanical verification and separate acceptance evidence

Windows Codex is run only at main-conversation instruction for the exact PR HEAD:
```powershell
dotnet restore AutoTest.sln
dotnet build AutoTest.sln -c Release --no-restore
dotnet test tests/AutoTest.Specialists.Tests/AutoTest.Specialists.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Release --no-build --no-restore
```
Check R1 P08 on PR16-host integration separately. Expected without DEVICE: Physical NOT AVAILABLE, Mock/Replay unchanged. Fake provider tests establish software behavior only; **actual ECU P2/P2*, CAN1 Tx/Rx, UDS physical response and user lab are NOT_RUN.**

## Follow-up after host integration

- Once DEVICE safely exposes its provider and Main/INTEGRATION approves one-line injection, run the exact joint candidate and collect true CAN/ISO-TP/UDS traces with a DUT-confirmed `0x22` DID or `0x19`. Verify 11/29-bit profile, NRC, disconnect/generation, FC and timeout.
- Physical Flash, ProgrammingSession, 0x10/0x27/0x34/0x36/0x37 remain explicitly forbidden; user physical Flash authorization is separate.
