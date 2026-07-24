# Architecture Assessment — Split `UpdateManufactureOrderStatusHandler` into state-transition + 2 extracted services

## Verdict

Approve the plan-01 / design-01 direction with one correction to a stated convention (below) and
the two open questions resolved. This is a low-risk, mechanically verifiable extract-service
refactor. No new architectural pattern is introduced — it's an application of a pattern already
present three times over in this exact module (`Services/Workflows/*`, `IBatchPlanningService`,
`IItemFilterService`). Proceed to implementation as scoped.

## Alignment with existing patterns (verified against current code, not assumed)

- **Two-file interface/implementation split is the correct convention for `Services/` (not
  `Services/Workflows/`).** I read every file in `Features/Manufacture/Services/` directly:
  `IBatchPlanningService.cs`/`BatchPlanningService.cs`, `IItemFilterService.cs`/`ItemFilterService.cs`,
  `IManufactureAnalysisMapper.cs`/`ManufactureAnalysisMapper.cs`, `IProductNameFormatter.cs`/`ProductNameFormatter.cs`,
  `IConsumptionRateCalculator.cs`/`ConsumptionRateCalculator.cs`, etc. — every single non-workflow
  service in that folder uses two files, no exceptions. By contrast, `Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`
  contains *both* `IConfirmSemiProductManufactureWorkflow` and its implementation in one file — I
  confirmed there is no separate `IConfirmSemiProductManufactureWorkflow.cs`. **Correction to design-01
  §1:** its phrasing ("interface file houses both types" as one option, two-file as the other) implied
  both conventions coexist ambiguously within `Services/`. They don't — the split is folder-determined:
  root `Services/` = two files, `Services/Workflows/` = one file. Since the new services are
  single-purpose action services (not multi-step orchestrations), they belong in root `Services/`,
  and must use two files each. Design-01's final recommendation (two-file) is correct; only its
  stated rationale needed tightening.
- **Verb-based method naming (`WriteDownAsync`, `CaptureAsync`) matches the folder's convention**
  (`CalculateBatchPlan`, `FilterItems`, `MapToDto`), and is correctly distinguished from `ExecuteAsync`,
  which I confirmed is used only by the two multi-step workflow classes in `Services/Workflows/`.
  Confirmed via direct read of `ConfirmSemiProductManufactureWorkflow.cs`.
- **`ManufactureModule.cs` registration point is correct.** Read the file directly: there is a
  "Register application services" block already containing `IConfirmSemiProductManufactureWorkflow`/
  `IConfirmProductCompletionWorkflow` as `AddScoped`. The two new registrations belong immediately
  after those two lines, same block, same lifetime.
- **`TimeProvider` is registered as a process-wide singleton** (confirmed in
  `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131` and three adapter modules) — safe
  to inject into both new scoped services with no lifetime-mismatch risk (singleton → scoped is fine;
  the reverse would not be).
- **Dependency counts and constructor shape verified directly against the two source methods**
  (`WriteDownInventoryAsync` lines 160–233, `CaptureConditionsReadingAsync` lines 235–266 of the
  current handler): the write-down path touches only `_catalogSource`, `_inventoryRepository`,
  `_timeProvider`, `_logger` — no `_currentUserService`, no `_repository`. The conditions-capture path
  touches only `_conditionsProvider`, `_timeProvider`, `_logger`. Design-01's proposed constructors
  (4 deps and 3 deps respectively) are exactly right; nothing is over- or under-injected.
- **Test counts verified by reading the actual test file**, not trusting plan-01's claim blindly:
  grepped `[Fact]`/`[Theory]` in `UpdateManufactureOrderStatusHandlerTests.cs` (1010 lines) and counted
  9 inventory-specific tests (`CreatesInventoryItemsForFinishedProducts`, `MergesIntoExistingRow...`,
  `ReCompletingSameOrder_DoesNotWriteInventoryTwice`, `AggregatesSameProductLotLinesIntoSingleRow`,
  `SkipsProductsWithZeroActualQuantity`, `ExcludesSemiProductsFromInventory`,
  `IncludesOnlyNonSemiProductsWhenMixed`, `WhenAllProductsHaveZeroOrNullQuantity_DoesNotCallAddRangeAsync`,
  `TransitionFromCompleted_DoesNotTouchInventory`) — matches plan-01's "9" exactly. The remaining 12
  tests in that file are genuinely orchestration/state-transition/field-persistence tests unrelated to
  either extracted concern and should not move.

## Proposed architecture (final)

```
UpdateManufactureOrderStatusHandler                       [unchanged responsibility: state transition + field orchestration]
 ├── IManufactureOrderRepository                          [unchanged]
 ├── TimeProvider                                          [unchanged — still needed for ERP/Flexi timestamp fields]
 ├── ILogger<UpdateManufactureOrderStatusHandler>          [unchanged]
 ├── ICurrentUserService                                   [unchanged]
 ├── IManufactureInventoryWriteDownService  (NEW)          [replaces IManufacturedProductInventoryRepository + IManufactureCatalogSource on the handler]
 └── IManufactureConditionsCaptureService   (NEW)          [replaces IConditionsReadingProvider on the handler]

IManufactureInventoryWriteDownService → ManufactureInventoryWriteDownService
 ├── TimeProvider
 ├── ILogger<ManufactureInventoryWriteDownService>
 ├── IManufacturedProductInventoryRepository
 └── IManufactureCatalogSource

IManufactureConditionsCaptureService → ManufactureConditionsCaptureService
 ├── TimeProvider
 ├── ILogger<ManufactureConditionsCaptureService>
 └── IConditionsReadingProvider
```

Three components, three single reasons to change. This is a textbook Extract Class refactor — no
new architectural layer, no new cross-module boundary, no change to the MediatR contract.

### Decisions (options considered → chosen → why)

1. **Constructor parameter count: 6, not the finding's suggested "5 or fewer."**
   Considered: forcing it down to 5 by also extracting `_currentUserService` or `_repository`.
   Rejected — both are genuinely state-transition concerns (`_repository` fetches/persists the order
   being transitioned; `_currentUserService` resolves who is making the transition, used both for
   `ChangeState` and passed through to write-down). Folding either into a new service would create an
   artificial dependency for no SRP gain. **Chosen:** accept 6, since the real target of the finding —
   "each dependency maps to exactly one concern" — is fully met at 6. Going from 7 (3 misplaced) to 6
   (0 misplaced) is the correct fix; chasing the number "5" would be optimizing the wrong metric.

2. **Two-file service convention (interface + impl), not workflow-style one-file.**
   Considered: matching `Services/Workflows/*`'s one-file style since these services are invoked from
   a use-case handler (superficially similar to how workflows are invoked). Rejected — the
   distinguishing factor in this codebase is *shape*, not *caller*: workflows are multi-step
   orchestrations that call multiple other services/adapters and return a result DTO
   (`ConfirmSemiProductManufactureResult`); the two new services are single-purpose actions with no
   sub-orchestration, matching every other service in root `Services/`. **Chosen:** two files each,
   consistent with 9-for-9 existing non-workflow services in that folder.

3. **Verbatim logic move, no behavioral cleanup.** Considered: while extracting, also fixing a minor
   nit (e.g. the `existingItems` lookup uses `FirstOrDefault` inside a loop — O(n·m), fine at expected
   scale but technically not a lookup by dictionary). Rejected per CLAUDE.md's "surgical changes"
   rule and per the finding's own framing (SRP/placement, not logic). **Chosen:** move both method
   bodies unchanged; do not touch the `FirstOrDefault` scan, grouping logic, or idempotency check.
   If that inefficiency matters, it's a separate finding.

4. **Test relocation (move), not duplication.** Considered: duplicating the 9 inventory tests and the
   conditions-mapping tests at both the handler level (via mocked service) and the new service level
   (real logic), to maximize regression safety during the refactor window. Rejected — for a pure,
   verbatim-body extraction with unchanged logic, duplicated assertions add maintenance cost (two
   places to update every future behavior change) without added protection: the moved tests exercise
   the exact same code paths, just through a narrower entry point. **Chosen:** move the 9
   inventory tests + the conditions snapshot/fallback tests to two new test files; keep only
   orchestration-level tests (does the handler call the right service under the right condition) in
   the two existing handler test files, with the two new services mocked.

## Implementation guidance

**New files** (both in `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/`):
- `IManufactureInventoryWriteDownService.cs` — single method:
  `Task WriteDownAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken);`
- `ManufactureInventoryWriteDownService.cs` — constructor `(TimeProvider, ILogger<ManufactureInventoryWriteDownService>, IManufacturedProductInventoryRepository, IManufactureCatalogSource)`; body = current `WriteDownInventoryAsync` (handler lines 160–233) moved verbatim, `_catalogSource`/`_inventoryRepository`/`_timeProvider`/`_logger` renamed to the new class's own fields.
- `IManufactureConditionsCaptureService.cs` — single method:
  `Task<ManufactureOrderConditionsReading> CaptureAsync(ManufactureOrder order, ManufactureOrderState stage, CancellationToken cancellationToken);`
- `ManufactureConditionsCaptureService.cs` — constructor `(TimeProvider, ILogger<ManufactureConditionsCaptureService>, IConditionsReadingProvider)`; body = current `CaptureConditionsReadingAsync` (handler lines 235–266) moved verbatim.

**Handler changes** (`UpdateManufactureOrderStatusHandler.cs`):
- Remove fields/ctor params: `_inventoryRepository`, `_catalogSource`, `_conditionsProvider`.
- Add fields/ctor params: `_inventoryWriteDownService` (`IManufactureInventoryWriteDownService`),
  `_conditionsCaptureService` (`IManufactureConditionsCaptureService`).
- Delete `WriteDownInventoryAsync` and `CaptureConditionsReadingAsync` private methods entirely.
- Replace the two call sites at lines 131–141 with calls to the injected services (bodies shown in
  design-01 §3 — verified to match the current call sites exactly, no change to the surrounding
  `if` conditions).
- Remove now-unused `using` directives if any become dead (check `Anela.Heblo.Domain.Features.Catalog`
  and `Anela.Heblo.Domain.Features.Manufacture.Inventory` — both may still be needed transitively for
  other handler code; verify with the compiler, don't assume).

**Data flow** (unchanged end-to-end, only the call boundary moves):
`Controller → MediatR → UpdateManufactureOrderStatusHandler.Handle → order.ChangeState(...) →
[if Completed] _inventoryWriteDownService.WriteDownAsync(order, user, ct) → IManufactureCatalogSource.GetByIdsAsync
→ IManufacturedProductInventoryRepository.{GetByProductCodesWithLogsAsync, AddRangeAsync} → [if
SemiProductManufactured|Completed] _conditionsCaptureService.CaptureAsync(order, state, ct) →
IConditionsReadingProvider.GetCurrentSnapshotAsync → order.ConditionsReadings.Add(reading) →
_repository.UpdateOrderAsync(order, ct)`.

**DI registration** — in `ManufactureModule.cs`, in the existing "Register application services"
block, immediately after the two `IConfirm*Workflow` registrations:
```csharp
services.AddScoped<IManufactureInventoryWriteDownService, ManufactureInventoryWriteDownService>();
services.AddScoped<IManufactureConditionsCaptureService, ManufactureConditionsCaptureService>();
```

**Test split** — two new files alongside the existing handler test files:
- `ManufactureInventoryWriteDownServiceTests.cs`: the 9 inventory tests, constructing
  `ManufactureInventoryWriteDownService` directly, mocking `IManufacturedProductInventoryRepository`
  + `IManufactureCatalogSource` + `TimeProvider`/`ILogger`, calling `WriteDownAsync(...)` directly.
- `ManufactureConditionsCaptureServiceTests.cs`: the conditions snapshot-mapping and
  exception-fallback tests from `UpdateManufactureOrderStatusHandlerConditionsTests.cs`, constructing
  `ManufactureConditionsCaptureService` directly, mocking only `IConditionsReadingProvider`.
- Trim `UpdateManufactureOrderStatusHandlerTests.cs` / `...ConditionsTests.cs`: replace the three
  removed mocks with `Mock<IManufactureInventoryWriteDownService>` /
  `Mock<IManufactureConditionsCaptureService>`; keep only the tests that assert *whether* the handler
  calls each service (and when), not *how* each service behaves internally.

## Risks and mitigations

- **Risk: silent behavior drift during the "verbatim move."** Copy-paste errors (e.g. dropping the
  `existing.WasWrittenDownByOrder` idempotency check, or reordering the semi-product filter after
  grouping instead of before) would be effectively invisible until a production double-write-down.
  *Mitigation:* the 9 relocated tests are the actual verbatim behavioral spec for this method — if
  they pass unmodified (only the construction/call-site changed) against the new service, the move is
  correct by construction. Do not "improve" assertions while moving them; that would weaken the
  safety net for this specific step.
- **Risk: log message parity.** Two log lines (`"Skipping duplicate inventory write-down..."`,
  `"Failed to capture conditions reading..."`) currently originate from
  `ILogger<UpdateManufactureOrderStatusHandler>` and will originate from
  `ILogger<ManufactureInventoryWriteDownService>` / `ILogger<ManufactureConditionsCaptureService>`
  after the move — the category name in structured logs changes. *Mitigation:* this is expected and
  acceptable (it's the correct new category), but flag it as a one-line note if any log-based
  dashboard or alert filters on the old category string. Not blocking; no such filter is known to
  exist in this repo's `docs/`.
- **Risk: test double-counting or accidental deletion.** Moving ~13 test methods across 4 files is
  the highest-touch part of this change. *Mitigation:* after the split, run `dotnet test` and diff
  the total test count before/after — it must be identical (same tests, different location), not
  lower.
- **No risk to the MediatR contract, DB schema, or frontend** — confirmed no `Request`/`Response`
  field changes, no migration, no OpenAPI client regeneration needed.

## Prerequisites before implementation begins

None outstanding. Both open questions from plan-01/design-01 are resolved above (constructor count →
6 is correct and final; test relocation → move, not duplicate, is correct and final). All referenced
types, interfaces, and DI registrations were confirmed to exist exactly as described by direct file
reads in this session — no dependency on unverified assumptions remains.
