# Design — Split `UpdateManufactureOrderStatusHandler` into state-transition + 2 extracted services

No UI is involved. This is a backend-only, application-layer refactor: the MediatR request/response
contract (`UpdateManufactureOrderStatusRequest` / `Response`) is unchanged, so no frontend, OpenAPI
client, or E2E surface is touched. The UX/UI section is omitted per the design-step instructions.

## Component design

### Overview

```
UpdateManufactureOrderStatusHandler          (orchestration + state transition — unchanged contract)
 ├── IManufactureOrderRepository              (unchanged dependency)
 ├── TimeProvider                             (unchanged dependency)
 ├── ILogger<UpdateManufactureOrderStatusHandler>  (unchanged dependency)
 ├── ICurrentUserService                      (unchanged dependency)
 ├── IManufactureInventoryWriteDownService     ← NEW, replaces IManufacturedProductInventoryRepository
 │                                                + IManufactureCatalogSource on the handler
 └── IManufactureConditionsCaptureService      ← NEW, replaces IConditionsReadingProvider
                                                  on the handler

IManufactureInventoryWriteDownService (impl: ManufactureInventoryWriteDownService)
 ├── TimeProvider
 ├── ILogger<ManufactureInventoryWriteDownService>
 ├── IManufacturedProductInventoryRepository
 └── IManufactureCatalogSource

IManufactureConditionsCaptureService (impl: ManufactureConditionsCaptureService)
 ├── TimeProvider
 ├── ILogger<ManufactureConditionsCaptureService>
 └── IConditionsReadingProvider
```

Each box owns exactly one reason to change: state transition, inventory write-down, conditions
capture. The handler's constructor drops from 7 parameters to 6 (`repository`, `timeProvider`,
`logger`, `currentUserService`, `inventoryWriteDownService`, `conditionsCaptureService`) — plan-01's
open question about "5 vs 6" is resolved in favor of 6, since `_repository` and `_currentUserService`
are both genuinely needed for the state-transition concern itself and cannot be removed. What matters
is that each of the 6 now maps to exactly one concern, versus today's 7 where 3 map to concerns the
handler shouldn't own.

### 1. `IManufactureInventoryWriteDownService`

**Location:** `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureInventoryWriteDownService.cs`
(interface + implementation in one file, matching the existing convention in this folder — e.g.
`IBatchPlanningService`/`BatchPlanningService` is the analogous "interface file houses both types" split
used elsewhere in `Services/`, while `IItemFilterService.cs` / `ItemFilterService.cs` show the
two-file variant also present in the folder; either split is acceptable here — recommend the
two-file variant since `ManufactureModule.cs` already imports both `Services` and `Services.Workflows`
namespaces and two files keep this consistent with most of `Services/`).

**Responsibility:** Given a completed manufacture order and the user who triggered the transition,
write down (create or increment) inventory for every finished, non-semi-product output line, with
idempotency per order.

**Interface:**
```csharp
public interface IManufactureInventoryWriteDownService
{
    Task WriteDownAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken);
}
```

Naming follows the folder's existing verb-based convention (`CalculateBatchPlan`, `FilterItems`,
`MapToDto`) rather than the generic `ExecuteAsync` used by `Services/Workflows/*` — those are
multi-step orchestration workflows (a different shape of component), while this and the conditions
service are single-purpose action services like the rest of `Services/`.

**Body (moved verbatim from `WriteDownInventoryAsync`, lines 160–233 of the current handler — no
logic changes):**
1. Filter `order.Products` to `ActualQuantity is > 0`; return early if none.
2. Look up catalog entries for the distinct product codes via `IManufactureCatalogSource.GetByIdsAsync`.
3. Exclude lines whose catalog entry has `Type == ProductType.SemiProduct`.
4. Group remaining lines by `(ProductCode, LotNumber, ExpirationDate)`, summing `ActualQuantity`.
5. Return early if no lines remain.
6. Fetch existing inventory rows for the involved product codes via
   `IManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync`.
7. For each aggregated line: if a matching existing row exists, check
   `existing.WasWrittenDownByOrder(order.Id)` — skip with an info log if already written for this
   order (idempotency), otherwise call `existing.WriteDownFromManufacture(...)`. If no matching row
   exists, build a new `ManufacturedProductInventoryItem`.
8. Persist new rows via `AddRangeAsync`, only if any were created.

**Constructor:** `TimeProvider`, `ILogger<ManufactureInventoryWriteDownService>`,
`IManufacturedProductInventoryRepository`, `IManufactureCatalogSource` — 4 dependencies, all specific
to this concern.

### 2. `IManufactureConditionsCaptureService`

**Location:** `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureConditionsCaptureService.cs`
(paired with `IManufactureConditionsCaptureService.cs`, same convention as above).

**Responsibility:** Given an order and the stage (state) being entered, produce a
`ManufactureOrderConditionsReading` — either from a live environmental snapshot, or a fallback
"unavailable" reading if the snapshot provider fails.

**Interface:**
```csharp
public interface IManufactureConditionsCaptureService
{
    Task<ManufactureOrderConditionsReading> CaptureAsync(
        ManufactureOrder order,
        ManufactureOrderState stage,
        CancellationToken cancellationToken);
}
```

**Body (moved verbatim from `CaptureConditionsReadingAsync`, lines 235–266 — no logic changes):**
1. Call `IConditionsReadingProvider.GetCurrentSnapshotAsync`.
2. On success, build a `ManufactureOrderConditionsReading` with `ManufactureOrderId`, `Stage`, and
   the snapshot's temperature/humidity/`RecordedAt`/`Source` fields.
3. On any exception, log an error (`"Failed to capture conditions reading for order {OrderId}, stage
   {Stage}"` — unchanged message) and return a fallback reading with `Source =
   ConditionsReadingSource.Unavailable` and `RecordedAt = _timeProvider.GetUtcNow().DateTime`.

**Constructor:** `TimeProvider`, `ILogger<ManufactureConditionsCaptureService>`,
`IConditionsReadingProvider` — 3 dependencies.

### 3. `UpdateManufactureOrderStatusHandler` (slimmed)

**Responsibility (unchanged in scope, now singular in mechanism):** fetch the order, validate the
requested transition, apply the state change plus the various field updates already on the handler
(manual-action flag, ERP order codes, weight fields, Flexi doc codes, notes), and — after the state
change — delegate to the two extracted services for their respective side effects, exactly at the
same points in `Handle` as today:

```csharp
if (request.NewState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed
    && order.ConditionsReadings.All(r => r.Stage != request.NewState))
{
    var reading = await _conditionsCaptureService.CaptureAsync(order, request.NewState, cancellationToken);
    order.ConditionsReadings.Add(reading);
}

if (request.NewState == ManufactureOrderState.Completed)
{
    await _inventoryWriteDownService.WriteDownAsync(order, currentUserName, cancellationToken);
}
```

No other line in `Handle` changes. The two private methods `WriteDownInventoryAsync` and
`CaptureConditionsReadingAsync` are deleted from this file entirely, along with the three fields/
constructor params (`_inventoryRepository`, `_catalogSource`, `_conditionsProvider`) they exclusively
served.

### DI registration

`ManufactureModule.cs`, under the existing "Register application services" comment block, alongside
`IConfirmSemiProductManufactureWorkflow`/`IConfirmProductCompletionWorkflow`:

```csharp
services.AddScoped<IManufactureInventoryWriteDownService, ManufactureInventoryWriteDownService>();
services.AddScoped<IManufactureConditionsCaptureService, ManufactureConditionsCaptureService>();
```

Scoped lifetime matches every other service in this module (all are scoped; none are singleton or
transient), and matches the lifetime the handler itself gets via MediatR's per-request scope.

### Test component boundaries

- **New `ManufactureInventoryWriteDownServiceTests.cs`** (same test project/folder as the handler
  tests): constructs `ManufactureInventoryWriteDownService` directly, mocking only
  `IManufacturedProductInventoryRepository` and `IManufactureCatalogSource` (plus `TimeProvider`/
  `ILogger`). Receives the 9 inventory-behavior test methods currently on
  `UpdateManufactureOrderStatusHandlerTests` (aggregation, idempotency, merge-into-existing-row,
  semi-product exclusion/mixed-inclusion, zero/null-quantity skip). Each test now calls
  `WriteDownAsync(order, user, ct)` directly instead of `handler.Handle(...)`.
- **New `ManufactureConditionsCaptureServiceTests.cs`**: constructs
  `ManufactureConditionsCaptureService` directly, mocking only `IConditionsReadingProvider`. Receives
  the snapshot-to-reading mapping and exception-fallback assertions currently in
  `UpdateManufactureOrderStatusHandlerConditionsTests` (live-snapshot mapping, unavailable-snapshot
  passthrough, provider-throws fallback).
- **`UpdateManufactureOrderStatusHandlerTests.cs` / `...ConditionsTests.cs` (trimmed)**: constructor
  now takes `Mock<IManufactureInventoryWriteDownService>` and
  `Mock<IManufactureConditionsCaptureService>` in place of the three removed mocks. Retained tests
  become orchestration-only: does the handler invoke `WriteDownAsync` when transitioning to
  `Completed` and not otherwise (`Handle_TransitionFromCompleted_DoesNotTouchInventory` becomes "does
  not call the write-down service"); does it invoke `CaptureAsync` when transitioning to
  `SemiProductManufactured`/`Completed` with no existing reading for that stage, and not when a
  reading already exists or the target state isn't one of those two. All non-inventory/non-conditions
  tests (state transition validation, field persistence, error handling, user-name resolution) are
  untouched since they don't reference the extracted services' mocks at all beyond construction.

## Data schemas

No schema, DTO, or contract changes:

- `UpdateManufactureOrderStatusRequest` / `UpdateManufactureOrderStatusResponse` (the MediatR
  request/response, HTTP-facing via the controller) — unchanged field-for-field.
- `ManufactureOrder`, `ManufactureOrderConditionsReading`, `ManufacturedProductInventoryItem` domain
  entities — unchanged.
- No new events, no new persisted tables/columns. This is purely an application-service boundary
  change: two new C# interfaces and their implementations, invoked with the same arguments and
  producing the same side effects as the private methods they replace.

## Notes carried from the plan

- Method names `WriteDownAsync` / `CaptureAsync` are chosen (over `ExecuteAsync`) to match the
  verb-based naming already used by sibling services in `Features/Manufacture/Services/` (see
  `IBatchPlanningService.CalculateBatchPlan`, `IItemFilterService.FilterItems`,
  `IManufactureAnalysisMapper.MapToDto`). `ExecuteAsync` is reserved by convention for the
  multi-step orchestrations in `Services/Workflows/`, which this is not.
- Constructor parameter count for the handler is 6, not the finding's suggested "5 or fewer" — this
  is a correction to the finding's estimate, not a shortfall against a requirement. Every dependency
  now maps to exactly one concern, which is the actual SRP goal.
- Test relocation (move, not duplicate) is the intended approach: the extracted services get direct
  unit coverage of their logic, and the handler tests keep only orchestration assertions. This avoids
  duplicating ~13 test methods' worth of assertions across two layers for a pure refactor with no
  behavior change.
