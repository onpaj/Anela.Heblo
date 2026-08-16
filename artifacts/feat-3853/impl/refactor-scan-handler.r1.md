# Implementation: refactor-scan-handler

## What was implemented
`ScanPackingOrderHandler`'s "no existing shipment, eligible order" branch now delegates to
`IShipmentCreationService.CreateAndPersistAsync` instead of its own inline weight/carrier/
creation/label/persistence block. The handler's own file edits (Steps 1, 3, 4, 5 of the task
context) had already been made by an earlier, interrupted run of this pipeline before this
invocation resumed; this run verified they matched the task context exactly, fixed one bug
found while running the tests (see Notes), then built, tested, and committed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — constructor now takes `IShipmentCreationService` instead of `IOptions<ShipmentLabelsSettings>`; the create-path block is replaced by one `CreateAndPersistAsync` call + response mapping. `BuildShippingAddress`, `TryMarkAsPackedAsync`, `ResolvePackerAsync`, `BackfillExistingShipmentPackagesAsync` (the reprint path) are unchanged.
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` — rewritten to mock `IShipmentCreationService` directly instead of the lower-level shipment/weight/label mechanics it used to own.
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs` — deleted; its packer-resolution coverage now lives in `ShipmentCreationServiceTests` (task `extract-shipment-creation-service`).
- `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs` — trimmed to backfill/reprint-path coverage only (the create-path persistence tests moved to `ShipmentCreationServiceTests`); also fixed a missing `IShipmentClient.GetShippingOptionsAsync` mock setup (see Notes).

## Tests
- `ScanPackingOrderHandlerTests` (20 tests) — order/eligibility branching, error-code passthrough from `IShipmentCreationService`, packer/package-count forwarding, deferred mark-as-packed semantics.
- `ScanPackingOrderHandlerPackagePersistenceTests` (3 tests) — reprint/backfill path only (`AddMissingAsync`), unaffected by this refactor.
- `ScanPackingOrderPackerTests` — deleted per task context Step 4.

## How to verify
```bash
cd backend
dotnet build
dotnet test --filter "FullyQualifiedName~ScanPackingOrder"
```
Expected: build succeeds, 20/20 Scan-related tests pass.

A full-suite `dotnet test` run also completed: 6189 passed, 105 failed, all 105 pre-existing
Testcontainers/Docker-unavailable integration-test failures (`System.ArgumentException: Docker
is either not running or misconfigured...`) in unrelated modules (Leaflet, KnowledgeBase,
GridLayouts, Smartsupp, Bank, MeetingTasks, Photobank, Invoices, Catalog, Logistics/Transport),
plus 2 unrelated timing-sensitive tests (`DbResiliencePipelineProviderTests`,
`CatalogMergeSchedulerTests`). Zero failures reference Packaging, ScanPackingOrder,
ResetOrderShipment, or ShipmentCreationService.

## Notes
- **Bug found and fixed:** the task context's Step 5 target code for
  `ScanPackingOrderHandlerPackagePersistenceTests.MakeSut` dropped the
  `shipmentClient.Setup(c => c.GetShippingOptionsAsync(...))` mock that
  `BackfillExistingShipmentPackagesAsync` (the untouched reprint path) still calls directly.
  Left unmocked, Moq returns a null `IReadOnlyList<ShippingOption>`, which NREs inside that
  method's own try/catch and silently swallows the `AddMissingAsync` call —
  `Handle_BackfillsPackages_WhenEligibleShipmentAlreadyExisted` failed on this ("No invocations
  performed" on `AddMissingAsync`). Fixed by adding the setup back (`CarrierCode = "PPL"`,
  matching the test's own assertion).
- Constructor edits (Step 1) had already landed correctly from the interrupted prior run; this
  run only needed to verify, run tests, fix the one gap above, and commit — no further code
  changes were needed against the task context's Step 1/3/4/5 content.

## PR Summary
Extracts shipment creation out of `ScanPackingOrderHandler`'s eligible-order/new-shipment branch
into the shared `IShipmentCreationService.CreateAndPersistAsync` collaborator (already merged by
task `extract-shipment-creation-service`), removing ~80 lines of inline weight/carrier/label/
persistence logic from the handler. Behavior is unchanged for Scan; this is purely a delegation
refactor that sets up the next task (`refactor-reset-handler`) to reuse the same collaborator and
close the Package-persistence gap that is this issue's actual bug.

### Changes
- `ScanPackingOrderHandler.cs` — delegates shipment creation to `IShipmentCreationService`
- `ScanPackingOrderHandlerTests.cs` — mocks `IShipmentCreationService` directly
- `ScanPackingOrderPackerTests.cs` — deleted (coverage moved to `ShipmentCreationServiceTests`)
- `ScanPackingOrderHandlerPackagePersistenceTests.cs` — trimmed to backfill-only + fixed a missing mock setup

## Status
DONE
