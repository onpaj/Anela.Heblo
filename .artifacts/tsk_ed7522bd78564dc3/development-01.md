# Development — Split `UpdateManufactureOrderStatusHandler` into state-transition + 2 extracted services

## Summary

Implemented the extract-service refactor exactly as specified in plan-01/design-01 and approved
in architecture-01: `WriteDownInventoryAsync` and `CaptureConditionsReadingAsync` were moved
verbatim out of `UpdateManufactureOrderStatusHandler` into two new application services,
`IManufactureInventoryWriteDownService` / `ManufactureInventoryWriteDownService` and
`IManufactureConditionsCaptureService` / `ManufactureConditionsCaptureService`, registered in
`ManufactureModule`. The handler's constructor drops from 7 parameters to 6, with each dependency
now mapping to exactly one concern (state transition vs. inventory write-down vs. conditions
capture). No behavioural change — every existing test scenario is preserved, just relocated to the
layer that now owns the logic it exercises.

## Files created

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureInventoryWriteDownService.cs`
  — single-method interface: `WriteDownAsync(ManufactureOrder order, string changedByUser, CancellationToken ct)`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureInventoryWriteDownService.cs`
  — implementation; body is `WriteDownInventoryAsync` moved verbatim (catalog lookup, semi-product
  exclusion, product/lot/expiration aggregation, idempotency check, merge-or-create). Constructor:
  `TimeProvider`, `ILogger<ManufactureInventoryWriteDownService>`, `IManufacturedProductInventoryRepository`,
  `IManufactureCatalogSource`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureConditionsCaptureService.cs`
  — single-method interface: `CaptureAsync(ManufactureOrder order, ManufactureOrderState stage, CancellationToken ct) : Task<ManufactureOrderConditionsReading>`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureConditionsCaptureService.cs`
  — implementation; body is `CaptureConditionsReadingAsync` moved verbatim (snapshot call, mapping,
  exception fallback to `Unavailable`). Constructor: `TimeProvider`,
  `ILogger<ManufactureConditionsCaptureService>`, `IConditionsReadingProvider`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureInventoryWriteDownServiceTests.cs`
  — the 8 relocated inventory-behavior tests (aggregation, idempotency, merge-into-existing-row,
  semi-product exclusion/mixed-inclusion, zero/null-quantity skip), now calling `WriteDownAsync`
  directly and mocking only `IManufacturedProductInventoryRepository` + `IManufactureCatalogSource`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureConditionsCaptureServiceTests.cs`
  — the 4 relocated conditions-mapping/fallback tests (live snapshot mapping, stage assignment,
  unavailable-snapshot passthrough, provider-throws fallback), calling `CaptureAsync` directly and
  mocking only `IConditionsReadingProvider`.

## Files changed

- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`
  — constructor now takes `IManufactureInventoryWriteDownService` and `IManufactureConditionsCaptureService`
  in place of `IConditionsReadingProvider`, `IManufacturedProductInventoryRepository`,
  `IManufactureCatalogSource` (7 params → 6). The two private methods are deleted; the two call
  sites in `Handle` now call the injected services (same `if` conditions, unchanged). Unused
  `using` directives (`Catalog`, `Manufacture.Conditions`, `Manufacture.Inventory`,
  `Manufacture.Contracts`) removed.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — registers both
  new services as `AddScoped`, alongside the existing `IConfirmSemiProductManufactureWorkflow` /
  `IConfirmProductCompletionWorkflow` registrations.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs`
  — trimmed to orchestration-only: the 8 inventory-behavior tests moved out; the remaining 14
  non-inventory tests are unchanged in intent (mocks/setup updated to the two new service mocks).
  Kept/added inventory-orchestration coverage: `Handle_TransitionToCompleted_CallsInventoryWriteDownService`
  (new — asserts the handler calls `WriteDownAsync` when transitioning to `Completed`) and
  `Handle_TransitionFromCompleted_DoesNotTouchInventory` (kept, now asserts the service is *not*
  called instead of asserting on `AddRangeAsync`).
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs`
  — trimmed to orchestration-only: the 4 mapping/fallback tests moved out. Kept/added:
  `Handle_TransitionToSemiProductManufactured_CallsConditionsCaptureService` and
  `Handle_TransitionToCompleted_CallsConditionsCaptureServiceWithCorrectStage` (new — assert the
  handler calls `CaptureAsync` with the right stage and adds the result to `ConditionsReadings`),
  plus the two existing negative tests (`...DoesNotAddDuplicateConditionsReading...`,
  `...DoesNotCaptureConditionsReading` for non-conditions stages), now asserting the service mock
  is not invoked instead of the old provider mock.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — the `Manufacture ->
  Catalog` allowlist entries for the old
  `UpdateManufactureOrderStatusHandler+<WriteDownInventoryAsync>d__9` / `+<>c__DisplayClass9_0`
  (compiler-generated types for the now-deleted private method) were replaced with a single base
  entry for `ManufactureInventoryWriteDownService -> CatalogAggregate`, which covers the new
  service's own compiler-generated async-state-machine/closure types via the test's existing
  declaring-type fallback check (same pattern already used for `BatchPlanningService` etc. in that
  file).

## Verification

`dotnet` is not available in this environment, so `dotnet build` / `dotnet test` / `dotnet format`
could not be run directly here — **run these before merging**:
```
dotnet build
dotnet format --verify-no-changes
dotnet test --filter "FullyQualifiedName~Manufacture"
```
I compensated with an extensive manual review in place of compilation:
- Traced every type used in the new/changed files against its actual source-file namespace
  (`ManufactureOrder`, `ManufactureOrderProduct`, `ManufacturedProductInventoryItem`,
  `IManufacturedProductInventoryRepository`, `CatalogAggregate`, `ProductType`, `ConditionsSnapshot`,
  `ConditionsReadingSource`, `IConditionsReadingProvider`, `ManufactureOrderConditionsReading`) to
  confirm `using` directives are correct and complete.
- Confirmed constructor/method signatures used in the new services and their tests match the real
  interfaces (`IManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync`/`AddRangeAsync`,
  `IManufactureCatalogSource.GetByIdsAsync`, `IConditionsReadingProvider.GetCurrentSnapshotAsync`,
  `ManufacturedProductInventoryItem` constructor, `WasWrittenDownByOrder`/`WriteDownFromManufacture`).
- Grepped the whole repo for other constructors of `UpdateManufactureOrderStatusHandler` or
  references to the deleted private methods — only the two test files (already updated) matched.
- Read `ModuleBoundariesTests.cs` in full to understand its reflection-based allowlist mechanism
  (including the declaring-type fallback) and updated the one stale entry it would otherwise leave
  dangling; confirmed unused/stale allowlist entries don't fail the test (only *actual* undeclared
  edges do), so no other allowlist cleanup was strictly required.
- Confirmed no other test/prod code depends on `IManufactureCatalogSource`, `IConditionsReadingProvider`,
  or `IManufacturedProductInventoryRepository` being injected into the handler directly.

## How to verify

1. `dotnet build` from `backend/` — should succeed with no errors (this refactor introduces no new
   external dependencies, only moves existing code between files).
2. `dotnet format --verify-no-changes` — style should already match repo conventions (mirrors
   existing `Services/` two-file interface/implementation pattern).
3. `dotnet test --filter "FullyQualifiedName~Manufacture"` — all Manufacture-module tests should
   pass, including the two new service test files, the two trimmed handler test files, and
   `ModuleBoundariesTests`.
4. Manual smoke check (optional): transition a manufacture order through `Planned →
   SemiProductManufactured → Completed` via the API and confirm inventory write-down and
   conditions-reading capture still occur exactly as before (same log messages, same idempotency
   behavior on re-completing an order).

## Deviations from plan/design/architecture

None. Constructor parameter count (6), two-file service convention, verb-based method naming
(`WriteDownAsync`/`CaptureAsync`), and test relocation (move, not duplicate) all match
architecture-01's final decisions. The only addition beyond the three docs' explicit file lists is
the `ModuleBoundariesTests.cs` allowlist update, which is a mechanical consequence of moving
`WriteDownInventoryAsync` (undiscussed in the docs because it's a test-infrastructure detail, not a
design decision) — required for the architecture test to keep passing after the move.
