# G4 V0 Core/Contracts public contract snapshot (not the approved revision)

> V0-R2 主对话裁决：CT-01～CT-23 **继续保持候选**，已允许参加 Windows Build/Test 验证；不得因已批准 SDK/路径/Prism 许可就宣称 `CONTRACT_REVISION` 已正式冻结。Core/Contracts 的 `.cs` 字节及候选 SHA 在本轮均不改变。

- Candidate ID: `G4-V0-CT-CANDIDATE-0417108b292353b8`
- Candidate SHA-256 over sorted Core/Contracts `.cs` paths+UTF8 contents: `0417108b292353b896b88f8170b129f1d63c4ef2dfcfbc3491990d2bd8dd7a6a`
- **CONTRACT_REVISION = UNSET** until Windows compilation/tests and main chat approval.
- All 23 CT method names, input classes, return types and ownership directions follow the approved G3/G4 register. Nullability and immutable collection encoding are explicit V0 implementation decisions, not new G3 architecture.
- Namespaces: `AutoTest.Core` for identity/outcome/evidence/fingerprint; `AutoTest.Contracts` for all cross-domain DTO/CT. Domains reference Contracts -> Core, never each other. Application -> Contracts; Desktop -> Application/Contracts; specialized projects -> Contracts.
- `ValueTask` for short request/reply calls, `IAsyncEnumerable` for streams. Rejected/Uncertain are explicit and never converted into PASS.
- Candidate method additions outside CT: `IStationSession.DisconnectAsync`, `ITransmitLeaseService.AcquireAsync/ReleaseAsync`, `IRecordReader.QueryAsync`, `IMockObservationSource.ObserveMockAsync`, `IApplicationCommandGateway.Observe/GetCanExecuteReason`.
- Approved G3 API draft defines `CanObservation.TimeQuality` as an `EvidenceQuality` (draft type mismatch); G4 closure requires separate `TimeQuality` vs `EvidenceQuality` and immutable CAN bytes, implemented as `CanObservation.Time.Quality` and `ImmutableArray<byte>` respectively. Main chat must explicitly review before freezing `CONTRACT_REVISION`.
- CT-11 uses `OperationResult<Unit>` instead of nongeneric `OperationResult` (G3 full prototype already shows `<Unit>`). Other typed results follow the 23-row closure matrix.

## CT status matrix (declared signatures vs actual V0 behavior)

| ID | Frozen interface | V0 implementation level |
|---|---|---|
| CT-01 | `IStationSession.ConnectAsync` | `MOCK_BEHAVIOR_ONLY` |
| CT-02 | `IStationControl.ExecuteAsync` | `MOCK_BEHAVIOR_ONLY` |
| CT-03 | `ICanFrameHub.SubscribeAsync` | `SIGNATURE_ONLY` |
| CT-04 | `ICanFrameHub.TransmitAsync` | `SIGNATURE_ONLY` |
| CT-05 | `IProjectService.OpenAsync` | `SIGNATURE_ONLY` |
| CT-06 | `IProjectService.SaveAsync` | `SIGNATURE_ONLY` |
| CT-07 | `IDbcDecoder.Decode` | `SIGNATURE_ONLY` |
| CT-08 | `ITestAuthoringService.Apply` | `SIGNATURE_ONLY` |
| CT-09 | `IPlanCompiler.PrepareAsync` | `SIGNATURE_ONLY` |
| CT-10 | `IRunController.StartAsync` | `SIGNATURE_ONLY` |
| CT-11 | `IRunController.RequestStopAsync` | `SIGNATURE_ONLY` |
| CT-12 | `IBarcodeInput.ObserveAsync` | `SIGNATURE_ONLY` |
| CT-13 | `IIdentityMatcher.Match` | `SIGNATURE_ONLY` |
| CT-14 | `IUdsClient.RequestAsync` | `SIGNATURE_ONLY` |
| CT-15 | `IWaveformService.CaptureAsync` | `SIGNATURE_ONLY` |
| CT-16 | `IWaveformService.CompareAsync` | `SIGNATURE_ONLY` |
| CT-17 | `IProgrammingService.PreflightAsync` | `SIGNATURE_ONLY` |
| CT-18 | `IProgrammingService.ExecuteAsync` | `SIGNATURE_ONLY` |
| CT-19 | `IRunFactSink.AppendAsync` | `IN_MEMORY_APPEND_ONLY` |
| CT-20 | `IRunFactSink.CommitAsync` | `NOT_DURABLE_BY_DESIGN` |
| CT-21 | `IApplicationCommandGateway.ExecuteAsync` | `MOCK_BEHAVIOR_ONLY` |
| CT-22 | `IProductionService.PrepareAsync` | `SIGNATURE_ONLY` |
| CT-23 | `IProductionService.StartAsync` | `SIGNATURE_ONLY` |

**Status definitions:** `SIGNATURE_ONLY`: builds as contract but no implementation registered; `MOCK_BEHAVIOR_ONLY`: only the no-device in-process simulation is executable; `IN_MEMORY_APPEND_ONLY`: facts are append-only process-memory objects, not persisted records; `NOT_DURABLE_BY_DESIGN`: CommitAsync explicitly reports Durable=false. No CT has been demonstrated compiled on Windows; all implementation-level claims require target verification.

## Known contract review candidates (no Codex discretion)

1. `OperationResult<T>` wraps `OperationFailure` and `ImmutableArray<EvidenceRef>` (instead of an unvalidated string list). The value alone is never proof of physical state.
2. `SessionInfo.Generation` and scoped `Authorization.Generation` isolate old responses after re-connect; V0 authority can only be issued for simulation; real safety authorizations are unavailable.
3. `PreparedPlan` and `RunSnapshot` use immutable arrays; validation of complex graph constraints belongs to V1 C2, not to the V0 mock.
4. Public DTOs for B/C/E/P are **signatures for future owners**, not implemented business functions; specialist teams must not add duplicate declarations.
5. `MockObservation`/`IMockObservationSource` are explicitly V0 testing extensions, **not a physical station capability**.

## Reference checks

- G3 approved source SHA `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`.
- All future request/response refinements require a controlled revision; never ask Codex to silently change these interfaces.
