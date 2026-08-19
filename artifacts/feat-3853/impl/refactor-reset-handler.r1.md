# Implementation: refactor-reset-handler

## What was implemented
`ResetOrderShipmentHandler`'s inline weight/carrier/creation/label block has been replaced with a
single delegation call to `IShipmentCreationService.CreateAndPersistAsync` (the collaborator added
by task `extract-shipment-creation-service`), following cancellation of the prior shipment(s). This
is the bug fix (FR-3): `CreateAndPersistAsync` always calls
`IPackageRepository.ReplacePackagesForOrderAsync` internally, so for the first time
`ResetOrderShipmentHandler` causes `Package` rows to be written on reset — with `ShipmentGuid` equal
to the new shipment's GUID — and the delete-then-insert-per-order-code semantics of
`ReplacePackagesForOrderAsync` clear the stale rows left by the cancelled shipment(s) in the same
operation.

Reset never supplies an explicit packer (`ResetOrderShipmentRequest` has no `PackingUserId` field),
so the handler always passes `null` for `packingUserId`, and the shared service falls back to the
current logged-in user's email — unchanged behavior from before this refactor.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs` — constructor now takes `IShipmentCreationService` instead of `IOptions<ShipmentLabelsSettings>`; the weight/carrier/creation/label-fetch-and-pad block (~55 lines) is replaced by one `CreateAndPersistAsync` call + response mapping.
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs` — rewritten to mock `IShipmentCreationService` directly instead of the lower-level shipment/weight/label mechanics it used to own. Includes a new regression test that wires the *real* `ShipmentCreationService` (not mocked) into the handler to prove the handler → service → repository chain actually persists `Package` rows on a successful reset.

## Tests
`ResetOrderShipmentHandlerTests` (13 test cases: 11 facts + 1 theory with 2 cases) covering:
- No existing shipment → `NoShipmentToReset`, cancel never called
- Cancel throws → `ShipmentCancelFailed`
- Happy path: cancels old shipment, delegates to the shared service, maps its result
- Shared service failure after successful cancel → error code surfaced unchanged (`ShipmentCarrierNotResolved`, `ShipmentCreationFailed`)
- Multiple distinct shipment GUIDs → each cancelled before the shared service is called
- Two labels sharing the same shipment GUID → cancel called only once
- Second of two cancels fails → `ShipmentCancelFailed`, shared service never called
- Cancel returns silently → handler still delegates to the shared service
- Multi-package recreate: `NumberOfPackages` flows into the service call, `PendingCompletion` true for n ≥ 2
- Out-of-range package count → `InvalidPackageCount`, rejected before any I/O
- **FR-3 regression guard** (`Handle_HappyPath_PersistsPackageRowsForNewShipment_ThroughRealShipmentCreationService`): constructs the real `ShipmentCreationService` (backed by a mocked `IPackageRepository`, `IAuthorizationRepository`, `ICurrentUserService`, `IShipmentClient`) and asserts `ReplacePackagesForOrderAsync` is called exactly once with the correct order code and a `Package` row carrying the *new* shipment's GUID — the literal proof that the original bug (reset never persisting `Package` rows) is fixed.

Two pre-refactor tests (`Handle_ZeroWeightAfterCancel_UsesFallbackPackageWeight`,
`Handle_EventualConsistency_SecondCallReturnsBothOldAndNew_OnlyNewPackagesInResponse`) were removed
per the task context — their logic now lives in `ShipmentCreationService` and is covered by
`ShipmentCreationServiceTests` (from task `extract-shipment-creation-service`).

## How to verify
```bash
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ResetOrderShipment|FullyQualifiedName~ShipmentCreationService" --logger "console;verbosity=normal"
dotnet format ../Anela.Heblo.sln --verify-no-changes
```

Actual results from this run:
- `dotnet build ../Anela.Heblo.sln`: **0 errors** (253 pre-existing warnings, unrelated to this change).
- `dotnet test ... --filter "FullyQualifiedName~ResetOrderShipment|FullyQualifiedName~ShipmentCreationService"`: **Total tests: 31, Passed: 31, Failed: 0** (13 `ResetOrderShipmentHandlerTests` + 16 `ShipmentCreationServiceTests` + 2 `ScanPackingOrderHandlerTests` cases incidentally matched by the filter substring `ShipmentCreationService`, all passing).
- `dotnet format ../Anela.Heblo.sln --verify-no-changes`: **no output / exit 0** — no formatting violations.

Note: `dotnet format` requires the explicit solution path (`../Anela.Heblo.sln`) from the `backend/`
directory — running it bare from `backend/` fails with `FileNotFoundException` (no project/solution
directly in that folder); this is a pre-existing repo-layout characteristic, not introduced by this
change.

## Notes
- The task spec's Step 1/Step 3 code listings were transcribed verbatim (interface/type shapes were
  cross-checked against the actual `IShipmentCreationService.cs` / `ShipmentCreationResult.cs` files
  from task `extract-shipment-creation-service`, which matched exactly).
- Test run took several minutes wall-clock on first invocation in this session (full test-project
  build + xUnit discovery across ~6000 tests before filtering) — no functional issue, just slow
  first-build I/O in this environment.
- An unrelated, pre-existing `AccessMatrixGen` tool exception appears during `dotnet build`'s
  post-build step (`System.Text.Json.JsonException` in `Anela.Heblo.AccessMatrixGen/Program.cs`,
  MSB3073 warning, exit code 134) — this is a non-fatal warning unrelated to Packaging/Reset/Scan
  and does not affect build success (0 errors) or any test outcome.
- `git status` showed `artifacts/feat-3853/state.json` already modified (pipeline bookkeeping,
  `updated_at` timestamps) before this task's work began; it is included in this commit alongside
  the code changes since it was already staged-dirty in the working tree.

## PR Summary
Fixes `ResetOrderShipmentHandler` to persist `Package` rows on reset by delegating shipment creation
to the shared `IShipmentCreationService.CreateAndPersistAsync` collaborator (already merged by task
`extract-shipment-creation-service`, and already wired into `ScanPackingOrderHandler` by task
`refactor-scan-handler`). Previously, Reset ran its own inline copy of the weight/carrier/creation/
label logic that never called `IPackageRepository`, so reset-created shipments left no `Package` row
behind. Now the shared service's internal `ReplacePackagesForOrderAsync` call runs for every reset,
writing rows for the new shipment and clearing stale rows from the cancelled one(s) via
delete-then-insert semantics. A new regression test constructs the real (non-mocked)
`ShipmentCreationService` to prove the handler → service → repository chain actually persists. This
is the last of three tasks in this feature; Scan and Reset now both go through the same collaborator.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs` — delegates shipment creation/persistence to `IShipmentCreationService`, removing ~55 lines of duplicated inline logic
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs` — rewritten to mock `IShipmentCreationService`; adds the FR-3 regression test using the real service

## Status
DONE
