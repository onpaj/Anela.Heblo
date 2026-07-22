# Implementation: fix-hardcoded-desired-state-id-guard

## What was implemented
`PrintExpeditionOrderHandler` previously hardcoded state id `26` inside the static `NonPrintableStates` dictionary as an "order already in desired state" guard, even though the same handler already reads the configurable `DesiredStateId` from `IOptions<PrintPickingListOptions>` when building the print request. If `DesiredStateId` were ever reconfigured away from `26`, the guard would drift from the actual configured value. The fix removes the `26` entry from the static dictionary and adds an explicit runtime equality check (`currentStatusId == _options.Value.DesiredStateId`) before the dictionary lookup, so the guard always tracks the configured value.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs` — removed hardcoded `{ 26, "Balí se" }` entry from `NonPrintableStates`; added a runtime `currentStatusId == _options.Value.DesiredStateId` guard before the dictionary lookup, returning the same `ExpeditionOrderInvalidState` response.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs` — added `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26`, covering both a reconfigured `DesiredStateId = 99` (correctly blocked) and the old hardcoded `26` (no longer incorrectly blocked, proceeds to print).

## Tests
- `PrintExpeditionOrderHandlerTests.cs` — all 9 tests pass, including the pre-existing `Handle_OrderInNonPrintableState_ReturnsInvalidStateError` theory (unmodified, still covers default `DesiredStateId = 26` via the new equality branch instead of the dictionary) and the new non-default-`DesiredStateId` test.

## How to verify
```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9`.

## Notes
`dotnet build` emits an unrelated `MSB3073` warning from the `Anela.Heblo.AccessMatrixGen` post-build codegen tool (a pre-existing JSON-parsing issue in this sandbox, exit code 134) — build still succeeds with 0 errors and is unrelated to this change.

## PR Summary
Fixes an architecture-review finding in `PrintExpeditionOrderHandler`: the "order already in desired state" guard hardcoded state id `26` in a static dictionary instead of reading the configurable `DesiredStateId` from `IOptions<PrintPickingListOptions>`, which the same handler already uses for the print request itself. Under the default configuration this was harmless (both values were `26`), but if `DesiredStateId` were ever reconfigured, the guard would silently go stale — blocking orders in the old state and failing to block (double-printing) orders in the new desired state.

The fix replaces the hardcoded dictionary entry with a runtime equality check against `_options.Value.DesiredStateId`, run before the `NonPrintableStates` lookup for the remaining stable lifecycle states (`-3`, `52`, `70`). Behavior under the default configuration is unchanged; a new test proves the fix by reconfiguring `DesiredStateId = 99` and asserting both that `99` is now blocked and that the old hardcoded `26` is no longer incorrectly blocked.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`

## Status
DONE
