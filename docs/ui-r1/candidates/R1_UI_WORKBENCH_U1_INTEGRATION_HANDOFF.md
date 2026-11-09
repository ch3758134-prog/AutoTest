# R1 UI U1 · 组件候选与 INTEGRATION 接入申请

- Specialist: UI / R1 product workbench
- CandidateId: R1-UI-WORKBENCH-U1
- BasedOnCommit: `d9040c461f56617cfaba3821250c6efef0c7e20d`
- Change ownership: `src/AutoTest.Desktop/Shell/R1/**`, `tests/AutoTest.Desktop.Tests/Shell/**`
- Status: source submitted; Windows Build/Test NOT_RUN; HOST integration NOT_RUN; user R1 acceptance NOT_RUN.
- Reference: `docs/ui-r1/00_R1总体UI开发与接入边界_20261109.md` and P01–P13 reference.
- UI source: `R1WorkspaceView.xaml` + `R1WorkspaceViewModel.cs`. Real specialist views are deliberately not instantiated here.

## What is real in U1

1. The 13 pages have stable IDs P01–P13, owners, region keys and required contexts.
2. Search of workspaces; open-tab activation without duplicate open; close guards when busy/dirty; availability guards.
3. Project/Resource/Station/Generation/Origin context is explicit, and open tabs retain the revision they were opened with.
4. No USB/CAN, Station, Test, Record or other specialist domain implementation is copied.
5. View is a presentation component, **not yet** the single released main Shell; no fake professional view can be opened when unwired.

## Exact INTEGRATION request (no automatic merge into current Shell)

- Decide one owner for `Shell/MainWindow.xaml`, `Bootstrap/PrismRegistration.cs`, and shell navigation. These are **not modified** in this candidate.
- Provide one `IWorkspaceDocumentHost` adapter using current Prism DI/Region composition; `TryActivate/TryClose` must genuinely own view navigation and honor the specialist view close/dirty guard, not dispose shared Device Session/CanFrameHub.
- Map the single Project/Resource Revision and Station/SessionGeneration origin from authoritative providers to `AcceptContext`. On revision changes mark stale dependent documents appropriately; do not silently rewrite the revision captured by an open tab.
- Set `UpdateAvailability(id, Available)` only for a real registered, creatable specialist View; otherwise keep Unwired or Faulted.
- Implement the document content host and output/trace region inside the authorized production Shell; the present component is a safe navigation/display proof, not a final Dock implementation. No AvalonDock dependency is assumed.
- Wire menu, shortcuts, focus behavior and layout persistence through the **one** shell coordinator after ownership approval.
- Work with professional page owners rather than replacing any UDS, Wave, Flash, Bench, Test, Project, Scan or Record ViewModel.

## Mechanical Windows checks

```powershell
dotnet build AutoTest.sln -c Release
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Release --no-build
```

If any error appears, return the full source path, line, code and raw output. Codex must not make unplanned repairs. Inspect 100%, 125%, 150% DPI and narrower windows only after integration has supplied a legitimate WPF activation route. U1 cannot be reported as the complete 13-page R1 product UI.

## Outstanding specialist work

- UI: high-DPI/palette/keyboard access, multi-document visual behavior, actual visual quality and docking design, based on the 13-page prototype.
- INTEGRATION: shared app Shell ownership, DI, region hosts, tool lifecycle, status/trace and layout persistence.
- BENCH: remains an **independent** R0 specialist candidate at `r0/ui-bench-obs-01` (PR #2), not blocked by DEVICE physical troubleshooting.

## U1 incremental theme & density candidate (2026-11-09)

Files newly owned only within `src/AutoTest.Desktop/Shell/R1/Themes/`:
- `Dark.xaml`, `Light.xaml`: same named R1 surface/text/status brushes. They are scoped to the R1 component; neither App.xaml nor old global `Themes/Tokens/*` is edited.
- `Controls.xaml`: TextBlock/Button/TextBox/ListBox/TabControl visual and focus/error/disabled states. Font/density is view-scoped.
- `R1WorkspaceView.xaml(.cs)`: view-resident theme/density controls; merged-dictionary palette swap, handler detach on unload.
- `R1WorkspaceViewModel.cs`: two reversible commands without device/session/resource side effects.
- `R1WorkspaceViewModelTests.cs`: 2 extra unit cases for isolation and reversal (11 authored test methods total).

### Visual validation that needs Windows / authorized host
1. Dark and Light legibility: text, selected, disabled, error, warning, focus outline.
2. Compact/Comfortable density with Chinese long labels, 125%/150%/200% scale and 800px minimum viewport.
3. Toggle while specialist document is open: tab identity, project Revision, SessionGeneration and operation origin must remain unchanged.
4. Keyboard Ctrl+Tab / Ctrl+Shift+Tab / Ctrl+F4 must not interfere with professional editor shortcut ownership; INTEGRATION decides final command routing.
5. WPF XAML compilation and visual loading require Windows Build/authorized R1 host. XML appearance or source commit is not PASS.

### Known issue / approval request
View currently contains a non-operational central professional-document display placeholder, intentionally labeled unwired. INTEGRATION must replace this with the one actual authorized Region/Document host; do not claim completed docking/multi-document view rendering. Shell and Prism shared file ownership remains with INTEGRATION/main conversation. No third-party Dock dependency introduced.
