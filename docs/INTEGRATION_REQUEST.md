# U-BENCH / U-COMMON integration request

Baseline specified by G4-LAB: `0098b9fcdc83db3331e661f76d1fc4375c4269c6`.

**Source caveat:** The only HOST bytes available to this UI developer were the older `be08abe5...` snapshot, NOT the specified `0098b9f` tree. All payload files here are ADD-only and confined to UI ownership. Verify every target path is absent before applying. Do not overwrite any newer file. No Build/PASS claim is made.

## Integration-owned actions

1. Add `BenchWorkbenchView` to the existing Prism navigation/region registration; navigation caption `手动台架`, key `BenchWorkbench`.
2. Register `BenchWorkbenchViewModel` and exactly one `IBenchWorkbenchPort` adapter in the shared application composition. The adapter is constructed over the existing `LabDeviceCoordinator`/DEVICE session, PROJECT controller configuration, RECORD audit and authorized Station command bridge. **Do not instantiate a second USB session.**
3. Port must acquire one shared current session identity/generation; device discovery must use DEVICE's verified target filter (not HID/scanner search). The connector must not bypass Native/PhysicalCommandFence. Actual physical output defaults to DENY. Mock/Replay must be explicitly marked.
4. `LoadConfigurationAsync` maps configured arbitrary controller IDs/states and stable revision, not hardcoded K1-K20 buttons. `ApplyStateAsync` resolves the revision and state to configured mask/interaction and executes via DEVICE safety gate. It returns real OperationId/CorrelationId/TraceRef/source/ACK; NEVER fabricate a physical measurement or mark ECU PASS.
5. `SaveManualMeasurementAsync` uses PROJECT/RECORD durable storage with revision/controller/state/operation/source/5 nullable voltages/operator/time. Null = not measured, never 0. Data retrieved/reopened in the specialist records/config UI.
6. Include newly added `.xaml`, `.xaml.cs`, `.cs` in the existing WPF SDK-style Desktop project (glob includes by default). Add no new EXE, DLL project, solution edits from UI branch. New test `.cs` likewise auto-included in existing Desktop.Tests project.
7. Merge the optional `Themes/BenchTokens.xaml` resource dictionary into existing theme via INTEGRATION only, after resolving token collisions. View currently owns local resources, so this is not a blocker.
8. Preserve RECORD's authoritative operation log, as Bench's capped 200-entry view is only a transient summary. RECORD owns query/export/trace UI.

## Evidence and gate

- Build Windows `dotnet build AutoTest.sln -c Release`, then `dotnet test AutoTest.sln -c Release --no-build`; report old 145 tests + new 9 tests separately. Test count is a *target*, NOT verified.
- WPF inspect: empty/multi/one device; arbitrary state count; physical refused; revision mismatch; Mock accepted; blank voltages; manual voltage reopen; REPLAY source; external disconnect; legacy professional views unaffected.
- B0/B1/B2/B3 physical checks remain NOT_RUN, do not send physical hardware commands on UI test alone.

## Known limitations

- Without live `0098b9f` bytes, exact interface adapter cannot be mechanically authored against new APIs. The interface declaration alone does **not** block compilation; a missing DI registration/production adapter prevents ViewModel construction at runtime after navigation. Compile errors must be diagnosed from exact CS/XAML messages, not inferred from adapter absence. No fake production implementation is included.
- Momentary/ReturnToDefault is descriptive metadata in UI. Hold/release semantics are exclusively executed by DEVICE/Station in `ApplyStateAsync` and must be validated against physical safety gate.
- The UI summary log is non-durable and capped; RECORD service is the sole authoritative audit store.
- Actual low-level WinUSB failure correction belongs to DEVICE.

## V3 clarification and UI-only changes

- The previous statement that a production `IBenchWorkbenchPort` adapter is needed **to compile** was incorrect. It is required for a functioning composition, not for C# compilation.
- UI never retries a stale discovered device identity after failure. A multi-device choice is valid only for its discovery list; a new attempt re-enumerates after failure.
- `AcceptConnectionSnapshot` is the UI notification entrypoint for INTEGRATION to feed the shared DEVICE session's externally observed state. It does not create a second USB client. Invoke on the WPF dispatcher when subscribed to off-thread events.
- Applying a Bench state now rejects stale view items and invalidated source/revision state; final authorization and revision revalidation remain the backend's responsibility.
- **LabLink tests:** inspect the project dependency graph before adding a `Desktop.Tests -> LabLinkGui` reference. If `LabLinkGui -> Desktop` already exists or the project is an executable host, move affected tests into a separately integrated LabLink test project rather than introducing a circular project reference. This change is INTEGRATION-owned and is not part of the UI overlay.
- V3 updates owned `BenchWorkbenchViewModel.cs`, `BenchWorkbenchViewModelTests.cs`, and this request only. No Shell, csproj, native USB or physical output files changed.
