# Plan — GetWarehouseStatisticsHandler: TimeProvider + hardcoded capacity constant

## Summary

`GetWarehouseStatisticsHandler` (Catalog module) uses `DateTime.UtcNow` directly instead of the module's established `TimeProvider` pattern, and embeds a business-critical warehouse capacity constant (3000 kg) as a local `const` instead of a discoverable module constant. Both issues are confirmed present in the current source at `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetWarehouseStatistics/GetWarehouseStatisticsHandler.cs` (lines 29 and 44). This is a small, mechanical consistency fix — no behavior change.

## Context

Two sibling handlers in the same module (`GetCatalogDetailHandler`, `GetProductMarginsHandler`) already inject `TimeProvider` and call `_timeProvider.GetUtcNow()`. `TimeProvider` is registered in DI at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` and used pervasively across the test suite (`TimeProvider.System` swapped for fakes in ~15 test files), so wiring it into this handler follows a well-trodden path. `CatalogConstants.cs` already exists in the module and holds comparable business constants (`ALL_HISTORY_MONTHS_THRESHOLD`, `HISTORY_FLOOR_DATE`), making it the natural home for `WarehouseCapacityKg`.

## Functional requirements

- **FR-1**: `GetWarehouseStatisticsHandler` must accept `TimeProvider` via constructor injection and use `_timeProvider.GetUtcNow().UtcDateTime` (or equivalent) to populate `LastUpdated` in `GetWarehouseStatisticsResponse`.
  - Acceptance: no reference to `DateTime.UtcNow` remains in the handler; a unit test can inject a fake `TimeProvider` (e.g. `FakeTimeProvider` from `Microsoft.Extensions.Time.Testing`, matching the pattern in existing tests) and assert `LastUpdated` equals the fake's fixed instant exactly, with no time-window tolerance needed.
- **FR-2**: The warehouse capacity value (currently `const double warehouseCapacityKg = 3000;`) must move to `CatalogConstants.cs` as `public const double WarehouseCapacityKg = 3000.0;`, and the handler must reference `CatalogConstants.WarehouseCapacityKg` instead of a local constant.
  - Acceptance: no local `const` capacity declaration remains in the handler; `WarehouseCapacityKg` is defined once in `CatalogConstants.cs`; `GetWarehouseStatisticsResponse.WarehouseCapacityKg` and the utilization calculation both derive from it, and existing behavior (value = 3000, same utilization formula) is unchanged.
- **FR-3**: Existing response contract (`GetWarehouseStatisticsResponse`) and API behavior must be unchanged — this is a refactor, not a behavior change. Utilization percentage formula (`totalWeight / capacity * 100`, 0 when capacity ≤ 0) stays identical.

## Non-functional requirements

- No performance impact (constant lookup vs. local const is equivalent; `TimeProvider.GetUtcNow()` is already used identically elsewhere in the module).
- No new external dependencies — `TimeProvider` is a BCL type already wired into this project's DI container.
- Consistency: the change should make this handler indistinguishable in style from `GetCatalogDetailHandler` and `GetProductMarginsHandler` for this concern.

## Data model

No entity/schema changes. Touches only:
- `GetWarehouseStatisticsHandler` (constructor + `Handle` body)
- `CatalogConstants` (new public const field)

`GetWarehouseStatisticsResponse` DTO is unchanged (still a class per project convention; no field additions/removals).

## Interfaces

No API surface change — same MediatR request/response contract (`GetWarehouseStatisticsRequest` → `GetWarehouseStatisticsResponse`), same HTTP endpoint behavior. This is an internal implementation change only.

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetWarehouseStatistics/GetWarehouseStatisticsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs`
- Any DI registration check for `GetWarehouseStatisticsHandler` (none expected beyond MediatR auto-registration, since `TimeProvider` is already a container singleton)
- Unit test coverage for `LastUpdated` determinism (new or added-to test file if one exists for this handler — none found currently, so a new test file may be warranted, but is not strictly required by the finding; leaving as an open question below)

**Out of scope:**
- Any other handler or module's use of `DateTime.UtcNow` (this finding is scoped to this one file)
- Changing the actual capacity value (3000 kg) or the utilization formula
- Making warehouse capacity configurable via appsettings/environment (finding only asks to make it discoverable in `CatalogConstants`, not externally configurable)
- Any frontend changes (response DTO shape is unchanged)

## Rough plan

1. Add `public const double WarehouseCapacityKg = 3000.0;` to `CatalogConstants.cs`.
2. In `GetWarehouseStatisticsHandler`: add `TimeProvider timeProvider` constructor parameter, store as `_timeProvider`, replace `DateTime.UtcNow` with `_timeProvider.GetUtcNow().UtcDateTime`.
3. Remove the local `const double warehouseCapacityKg = 3000;` and replace both usages (response field + utilization calc) with `CatalogConstants.WarehouseCapacityKg`.
4. Check for and update any existing test/mock construction of `GetWarehouseStatisticsHandler` that would break from the added constructor parameter (none found in a repo-wide search for "GetWarehouseStatistics" under `backend/test`, but re-verify at implementation time since search may miss indirect DI-container-based instantiation).
5. Build (`dotnet build`) and run `dotnet format` per repo validation rules.
6. Run the Catalog-module test suite (and full backend suite if fast enough) to confirm nothing else references the handler's old constructor signature.

## Open questions

- No existing test file targets `GetWarehouseStatisticsHandler` directly. The finding's "why it matters" argues the whole point is testability of `LastUpdated` — implementing the fix without adding a test that exercises this would leave the stated benefit unverified. Default: add a minimal unit test asserting `LastUpdated` matches an injected `FakeTimeProvider` value, following the pattern already used in the ~15 test files that construct `FakeTimeProvider`/`TimeProvider.System` doubles. If the reviewer prefers a pure refactor with zero new test surface, this can be dropped.
- The finding's suggested code snippet uses `_timeProvider.GetUtcNow().UtcDateTime`; `GetCatalogDetailHandler` uses `.Date` and `GetProductMarginsHandler` uses `.DateTime`. Since `GetWarehouseStatisticsResponse.LastUpdated` is presumably a `DateTime` (not `DateTimeOffset`), `.UtcDateTime` is the correct conversion to preserve UTC — confirmed by checking the response DTO type at implementation time.
