# Plan — Split `UpdateManufactureOrderStatusHandler` into state-transition + 2 extracted services

## Summary

`UpdateManufactureOrderStatusHandler` currently owns three unrelated responsibilities — state
transition, inventory write-down, and conditions capture — behind a 7-parameter constructor. This
is a pure internal refactor: extract `WriteDownInventoryAsync` and `CaptureConditionsReadingAsync`
into two new application services (`IManufactureInventoryWriteDownService`,
`IManufactureConditionsCaptureService`), registered in `ManufactureModule`, and have the handler
depend on those two services instead of the five dependencies they currently need directly. No
behavioural change; no API/contract change.

## Context

Filed by the daily arch-review routine (2026-07-23) against
`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`.
Confirmed by reading the current file (267 lines): `Handle` (lines 41–158) does state transition
+ field updates + orchestration; `WriteDownInventoryAsync` (160–233, 73 lines) does catalog lookup,
semi-product exclusion, product/lot/expiration aggregation, and per-order idempotency; and
`CaptureConditionsReadingAsync` (235–266, 31 lines) calls an external conditions provider with a
fallback on failure. Three independent reasons to change the file today; a 7-arg constructor is
the resulting smell. This is a maintainability-only fix — no bug is being fixed, no behavior is
changing, so the acceptance bar is "identical outward behaviour, verified by the existing test
suite unchanged in intent."

The codebase already follows this extraction pattern elsewhere in the same module: e.g.
`IConfirmSemiProductManufactureWorkflow` / `IConfirmProductCompletionWorkflow` in
`Features/Manufacture/Services/Workflows/`, registered in `ManufactureModule.cs`. The new services
should follow the same shape (interface + implementation, one per file, `Services/` folder,
constructor-injected, scoped lifetime).

## Functional requirements

- **FR-1: Extract inventory write-down into `IManufactureInventoryWriteDownService`.**
  New interface with a single method, e.g. `Task WriteDownAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken)`.
  Implementation (`ManufactureInventoryWriteDownService`) contains the exact body currently in
  `WriteDownInventoryAsync` (lines 160–233), unchanged: filter to `ActualQuantity > 0`, early-return
  if none, look up catalog entries via `IManufactureCatalogSource.GetByIdsAsync`, exclude
  `ProductType.SemiProduct`, group by product code/lot/expiration, fetch existing inventory rows via
  `IManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync`, apply the
  `WasWrittenDownByOrder` idempotency check (skip + log if already written), mutate existing rows via
  `WriteDownFromManufacture` or build new `ManufacturedProductInventoryItem`s, and call
  `AddRangeAsync` for the new ones only if non-empty.
  - Acceptance: all 9 existing inventory-focused tests in `UpdateManufactureOrderStatusHandlerTests.cs`
    (aggregation, semi-product exclusion, idempotency, merge-into-existing-row, zero/null quantity
    skip, no-touch on non-Completed transitions) pass unmodified in intent (mocks/setup may move to
    a new test file for the service, see FR-4).
  - Acceptance: the service takes `TimeProvider`, `ILogger<ManufactureInventoryWriteDownService>`,
    `IManufacturedProductInventoryRepository`, `IManufactureCatalogSource` as constructor deps —
    nothing else.

- **FR-2: Extract conditions capture into `IManufactureConditionsCaptureService`.**
  New interface with a single method, e.g. `Task<ManufactureOrderConditionsReading> CaptureAsync(ManufactureOrder order, ManufactureOrderState stage, CancellationToken cancellationToken)`.
  Implementation (`ManufactureConditionsCaptureService`) contains the exact body currently in
  `CaptureConditionsReadingAsync` (lines 235–266): call `IConditionsReadingProvider.GetCurrentSnapshotAsync`,
  build a `ManufactureOrderConditionsReading` from the snapshot on success, or a fallback reading
  with `Source = ConditionsReadingSource.Unavailable` and `RecordedAt = _timeProvider.GetUtcNow().DateTime`
  on any exception (logged as an error, same message format).
  - Acceptance: all conditions-focused tests in `UpdateManufactureOrderStatusHandlerConditionsTests.cs`
    pass unmodified in intent.
  - Acceptance: the service takes `TimeProvider`, `ILogger<ManufactureConditionsCaptureService>`,
    `IConditionsReadingProvider` as constructor deps — nothing else.

- **FR-3: Slim the handler down to state transition + orchestration.**
  `UpdateManufactureOrderStatusHandler` keeps: order fetch, `CanTransitionTo` validation, all the
  direct field assignments (`ManualActionRequired`, ERP order codes, weight fields, Flexi doc codes,
  notes), `order.ChangeState(...)`, and the two call sites that decide *whether* to invoke the new
  services (same conditions as today, verbatim):
  ```csharp
  if (request.NewState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed
      && order.ConditionsReadings.All(r => r.Stage != request.NewState))
  {
      var reading = await _conditionsCaptureService.CaptureAsync(order, request.NewState, cancellationToken);
      order.ConditionsReadings.Add(reading);
  }

  if (request.NewState == ManufactureOrderState.Completed)
      await _inventoryWriteDownService.WriteDownAsync(order, currentUserName, cancellationToken);
  ```
  Constructor drops from 7 params to 5: `IManufactureOrderRepository`, `TimeProvider`,
  `ILogger<UpdateManufactureOrderStatusHandler>`, `ICurrentUserService`,
  `IManufactureInventoryWriteDownService`, `IManufactureConditionsCaptureService` — wait, that's 6;
  see note below (still down from 7, and each dependency now maps to exactly one concern).
  - Acceptance: `dotnet build` succeeds; handler no longer references
    `IConditionsReadingProvider`, `IManufacturedProductInventoryRepository`, or
    `IManufactureCatalogSource` directly.

- **FR-4: Register new services in `ManufactureModule`.**
  Add `services.AddScoped<IManufactureInventoryWriteDownService, ManufactureInventoryWriteDownService>();`
  and `services.AddScoped<IManufactureConditionsCaptureService, ManufactureConditionsCaptureService>();`
  under the existing "Register application services" section of
  `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`, alongside the
  existing workflow registrations.
  - Acceptance: app starts and DI container resolves `UpdateManufactureOrderStatusHandler` without
    manual registration elsewhere (MediatR handlers are auto-discovered; only the two new services
    need explicit registration).

- **FR-5: Split and update the test suite to match the new boundaries.**
  - New `ManufactureInventoryWriteDownServiceTests.cs`: move the 9 inventory-behavior tests
    currently embedded in `UpdateManufactureOrderStatusHandlerTests.cs` (aggregation, idempotency,
    merge, semi-product exclusion, zero/null-quantity skip) to test the extracted service directly
    (mock `IManufacturedProductInventoryRepository` + `IManufactureCatalogSource` only).
  - New `ManufactureConditionsCaptureServiceTests.cs`: move the conditions-provider-focused
    assertions currently in `UpdateManufactureOrderStatusHandlerConditionsTests.cs` that test
    snapshot-to-reading mapping and the exception fallback, to test the extracted service directly
    (mock `IConditionsReadingProvider` only).
  - `UpdateManufactureOrderStatusHandlerTests.cs` and `UpdateManufactureOrderStatusHandlerConditionsTests.cs`
    keep the orchestration-level tests (does the handler call the write-down service when
    transitioning to `Completed`; does it call the conditions service when transitioning to
    `SemiProductManufactured`/`Completed` and stage not already recorded; does it skip both on other
    transitions) but now mock `IManufactureInventoryWriteDownService` and
    `IManufactureConditionsCaptureService` directly instead of their five underlying dependencies.
  - Acceptance: `dotnet test` — full existing assertion coverage preserved (same behaviors verified,
    now at the appropriate layer), no test deleted without an equivalent replacement, no regression
    in pass/fail count.

## Non-functional requirements

- **No behavioural change.** This is a structural refactor; every existing scenario (idempotent
  write-down, semi-product exclusion, aggregation, conditions fallback on provider failure,
  duplicate-reading prevention) must produce byte-identical outcomes.
- **No new external dependency or I/O.** Both extracted services call the exact same downstream
  interfaces the handler already calls today.
- **Logging parity.** Existing log messages (`"Skipping duplicate inventory write-down for order..."`,
  `"Failed to capture conditions reading for order..."`) move to the new services' loggers verbatim
  — message text unchanged (some tests may assert on log content, e.g.
  `Handle_WhenExceptionOccurs_ShouldLogError` checks the handler's own top-level error log, which is
  unaffected since it's outside the extracted methods).

## Data model

No schema or entity changes. `ManufactureOrder`, `ManufactureOrderConditionsReading`,
`ManufacturedProductInventoryItem` are unchanged. This is purely an application-layer service
boundary change.

## Interfaces

- New: `IManufactureInventoryWriteDownService.WriteDownAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken) : Task`
- New: `IManufactureConditionsCaptureService.CaptureAsync(ManufactureOrder order, ManufactureOrderState stage, CancellationToken cancellationToken) : Task<ManufactureOrderConditionsReading>`
- No changes to `UpdateManufactureOrderStatusRequest`/`UpdateManufactureOrderStatusResponse` (the
  MediatR contract) or any HTTP-facing endpoint. This is invisible to the frontend and to
  `docs/development/api-client-generation.md`-generated clients.

## Dependencies and scope

**In scope:**
- `UpdateManufactureOrderStatusHandler.cs` — remove `WriteDownInventoryAsync` and
  `CaptureConditionsReadingAsync`, replace with calls to the two new services, trim constructor.
- Two new files under `Features/Manufacture/Services/`: interface + implementation for each new
  service (4 files total, matching the existing one-interface-per-file convention in that folder).
- `ManufactureModule.cs` — register the two new services.
- Test files: new `ManufactureInventoryWriteDownServiceTests.cs`,
  `ManufactureConditionsCaptureServiceTests.cs`; edits to
  `UpdateManufactureOrderStatusHandlerTests.cs` and `UpdateManufactureOrderStatusHandlerConditionsTests.cs`.

**Out of scope:**
- Any change to `ManufactureOrder.ChangeState`, `CanTransitionTo`, or other domain-entity logic.
- Any change to the inventory write-down *rules* (grouping, idempotency semantics) or conditions
  capture *rules* (fallback behavior) — this finding is about placement, not logic.
- Any change to `IManufactureCatalogSource`, `IConditionsReadingProvider`, or
  `IManufacturedProductInventoryRepository` contracts.
- Any other handler in the Manufacture module with similar constructor bloat (not raised by this
  finding; flag separately if found).

## Rough plan

1. Create `IManufactureInventoryWriteDownService` + `ManufactureInventoryWriteDownService` in
   `Features/Manufacture/Services/`, moving `WriteDownInventoryAsync`'s body verbatim.
2. Create `IManufactureConditionsCaptureService` + `ManufactureConditionsCaptureService` in the same
   folder, moving `CaptureConditionsReadingAsync`'s body verbatim.
3. Update `UpdateManufactureOrderStatusHandler`: swap the 3 concern-specific constructor deps
   (`IConditionsReadingProvider`, `IManufacturedProductInventoryRepository`,
   `IManufactureCatalogSource`) for the 2 new service interfaces; replace the two private-method
   calls with calls to the injected services; delete the two extracted private methods.
4. Register both new services as scoped in `ManufactureModule.cs`.
5. Split the test files per FR-5: create the two new service-level test files (moving the relevant
   `[Fact]`/`[Theory]` methods and their setup), then trim
   `UpdateManufactureOrderStatusHandlerTests.cs` / `...ConditionsTests.cs` to orchestration-only
   assertions with the two services mocked.
6. `dotnet build` + `dotnet format` (per repo validation rules) + `dotnet test` on the Manufacture
   test project; confirm no regressions.

## Open questions

- **Exact interface method names** (`WriteDownAsync` / `CaptureAsync`) are my proposal, matching the
  suggested-fix in the finding; the implementing step should feel free to align naming with nearby
  conventions (e.g. `IConfirmSemiProductManufactureWorkflow` uses `ExecuteAsync` — worth checking
  for a house style before finalizing).
- **Constructor parameter count in FR-3** — the finding's suggested fix says "drops from 7 to 5 (or
  fewer)"; with `_currentUserService` and `_repository` still needed for state transition, plus
  `TimeProvider`/`ILogger` and the 2 new services, the realistic count is 6, not 5. Flagging this as
  a correction to the finding's estimate rather than a requirement to hit exactly 5 — the important
  outcome is that each dependency now maps to exactly one concern, not a specific number.
- **Test relocation granularity** — FR-5 proposes moving whole test methods to new files. An
  alternative (keep all current tests as handler-level integration tests, add a *smaller* set of new
  unit tests for the extracted services) would preserve more end-to-end coverage but duplicate
  assertions. Given this is a pure refactor with strong existing coverage, I default to relocation
  (no behavior duplication) — reconsider if the design/architecture step prefers duplication for
  regression safety.
