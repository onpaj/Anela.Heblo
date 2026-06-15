# Specification: Inject TimeProvider into Manufacture Module Handlers (GetManufactureOutput & CalculateBatchPlan)

## Summary
Two handlers in the Manufacture module — `GetManufactureOutputHandler` and `CalculateBatchPlanHandler` — currently rely on `DateTime.Now` for default date-range boundaries instead of using the injected `TimeProvider` abstraction adopted across the rest of the module. This work injects `TimeProvider` into both handlers, replaces all `DateTime.Now` call sites with `_timeProvider.GetUtcNow().DateTime`, and adds tests that verify the time-shift behavior.

## Background
The Manufacture module previously migrated its handlers from `DateTime.Now`/`DateTime.UtcNow` to the .NET 8 `TimeProvider` abstraction for two reasons: (1) correctness on non-UTC servers (the production Azure Web App and local developer machines run in CET, where `DateTime.Now` near midnight already represents the next UTC day), and (2) unit-test time-shifting via `FakeTimeProvider`.

The daily architecture-review routine on 2026-06-06 identified two handlers that were missed in that migration:

| File | Line(s) | Current code |
|------|---------|--------------|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs` | 31, 128, 129 | `var toDate = DateTime.Now;` and gap-filling loop bounds |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs` | 58 | `var endDate = request.ToDate ?? DateTime.Now;` |

Neither handler has `TimeProvider` injected today — they are full outliers, not partial bypasses. This is distinct from issues #2676–#2680, which cover handlers that have `TimeProvider` injected but still call `DateTime.UtcNow` for some fields.

**Concrete failure mode:** at 23:45 CET (UTC+1 or UTC+2), `DateTime.Now` reads `2026-06-07 23:45` while `DateTime.UtcNow` is `2026-06-07 21:45` (or `22:45` in summer). For a query intended to cover "today in UTC", the range `[fromDate, DateTime.Now]` skews by up to one calendar day relative to data the rest of the system writes in UTC, producing inconsistent or off-by-one results for late-evening queries.

## Functional Requirements

### FR-1: Inject TimeProvider into GetManufactureOutputHandler
Add `TimeProvider` as a constructor dependency on `GetManufactureOutputHandler` and store it in a private readonly field `_timeProvider`. Replace every `DateTime.Now` reference in the handler with `_timeProvider.GetUtcNow().DateTime`.

**Affected call sites:**
- Line 31: `var toDate = DateTime.Now;` → `var toDate = _timeProvider.GetUtcNow().DateTime;`
- Lines 128–129 (gap-filling loop): `var endDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);` → derive `Year`/`Month` from a single `_timeProvider.GetUtcNow().DateTime` local to avoid calling the clock twice.

**Acceptance criteria:**
- `GetManufactureOutputHandler` has no remaining references to `DateTime.Now`, `DateTime.UtcNow`, or `DateTime.Today`.
- The handler constructor accepts `TimeProvider` as a parameter and stores it in `_timeProvider`.
- The gap-filling loop computes the upper bound from a single `_timeProvider.GetUtcNow().DateTime` snapshot (no double-read race window).
- Existing handler behavior — the shape and semantics of the returned `GetManufactureOutputResponse` — is unchanged when the test clock is set to a value where `DateTime.Now == _timeProvider.GetUtcNow().DateTime` (i.e., UTC server, current wall clock).

### FR-2: Inject TimeProvider into CalculateBatchPlanHandler
Add `TimeProvider` as a constructor dependency on `CalculateBatchPlanHandler` and store it in a private readonly field `_timeProvider`. In `ResolveSalesRanges`, replace the `DateTime.Now` fallback with `_timeProvider.GetUtcNow().DateTime`.

**Affected call sites:**
- Line 58: `var endDate = request.ToDate ?? DateTime.Now;` → `var endDate = request.ToDate ?? _timeProvider.GetUtcNow().DateTime;`

**Acceptance criteria:**
- `CalculateBatchPlanHandler` has no remaining references to `DateTime.Now`, `DateTime.UtcNow`, or `DateTime.Today`.
- The handler constructor accepts `TimeProvider` as a parameter and stores it in `_timeProvider`.
- The fallback path (no `TimePeriod`, no `ToDate`) computes `endDate` from `_timeProvider`.
- The explicit-`ToDate` and `TimePeriod` paths are unchanged in behavior.

### FR-3: Unit-test time-shift behavior for both handlers
Add or extend unit tests using `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` (already in use across the Manufacture module tests) to lock the clock to a deterministic, non-current UTC instant and verify each handler produces results consistent with that injected clock.

**Acceptance criteria:**
- A test for `GetManufactureOutputHandler` sets `FakeTimeProvider` to a fixed instant (e.g., `2026-03-15T10:00:00Z`) and asserts that the upper bound of the date range passed to `IManufactureHistoryClient.GetHistoryAsync` equals `2026-03-15T10:00:00` (or the same instant, depending on the handler's pre-existing `Kind` handling — preserve existing semantics).
- A test for `GetManufactureOutputHandler` verifies the gap-filling loop terminates at the month derived from the injected clock (e.g., `2026-03-01`), not the wall-clock month.
- A test for `CalculateBatchPlanHandler.ResolveSalesRanges` (or its public entry point) sets `FakeTimeProvider` to a fixed instant, omits both `TimePeriod` and `ToDate`, and asserts the resulting `DateRange.End` equals the injected instant.
- All new tests pass; all existing tests in `Anela.Heblo.Tests` continue to pass.

### FR-4: Preserve `DateTimeKind` and value semantics
`_timeProvider.GetUtcNow()` returns `DateTimeOffset`; `.DateTime` returns `DateTimeKind.Unspecified`. `DateTime.Now` returned `DateTimeKind.Local`. The downstream consumers (`IManufactureHistoryClient.GetHistoryAsync`, the gap-filling loop, `ResolveSalesRanges`'s `DateRange`) must continue to function correctly with the new `Kind`.

**Acceptance criteria:**
- Audit downstream code paths for any explicit reliance on `DateTimeKind.Local` (e.g., `ToUniversalTime()` conversions, format strings with `K`/`zzz`). If any are found, document them in Open Questions; otherwise confirm in the PR description that none exist.
- If the cosmetics-business semantic intent was "today in the operator's local timezone" (Europe/Prague) rather than "today in UTC", flag it in Open Questions before merging. The brief asserts UTC is the correct interpretation; this requirement makes that assumption explicit.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. `TimeProvider.GetUtcNow()` is a single virtual call resolving to `DateTimeOffset.UtcNow` in production. The fix may slightly reduce redundant clock reads by snapshotting `_timeProvider.GetUtcNow().DateTime` into a local variable in the gap-filling loop instead of reading it twice per loop bound construction.

### NFR-2: Security
No security impact. Time source is internal-only.

### NFR-3: Backward compatibility
- DI: `TimeProvider` is registered as a framework singleton (`TimeProvider.System`) by the host. No DI registration changes are required.
- API contract: handler request/response DTOs are unchanged.
- Wire compatibility: the OpenAPI-generated TypeScript client is unaffected because no public surface changes.

### NFR-4: Code consistency
Both handlers must match the existing `TimeProvider` injection pattern used by sibling handlers in the Manufacture module (constructor parameter ordering, field naming convention `_timeProvider`, lowercase access pattern). Confirm by reading one already-migrated handler in the module and mirroring its constructor signature style.

## Data Model
No data model changes. Database schemas, entities, and persisted values are untouched.

## API / Interface Design

**Internal interfaces only — no public API surface changes.**

`GetManufactureOutputHandler` constructor:
```csharp
public GetManufactureOutputHandler(
    IManufactureHistoryClient manufactureHistoryClient,
    IManufactureCatalogSource catalogSource,
    ILogger<GetManufactureOutputHandler> logger,
    TimeProvider timeProvider);
```

`CalculateBatchPlanHandler` constructor: add `TimeProvider timeProvider` as the final constructor parameter (or matching the position used by sibling handlers in the module — verify before implementing).

MediatR request/response contracts (`GetManufactureOutputRequest`, `GetManufactureOutputResponse`, `CalculateBatchPlanRequest`, `CalculateBatchPlanResponse`) and the MVC controllers that dispatch them are unchanged.

## Dependencies
- `System.TimeProvider` (in `System.Runtime`, available in .NET 8 — already in use across the module).
- `Microsoft.Extensions.TimeProvider.Testing` (for `FakeTimeProvider` in unit tests — already in use; confirm package reference in the relevant test project).
- No new NuGet packages, no DI registration changes (`TimeProvider.System` is registered by the .NET 8 host).

## Out of Scope
- Issues #2676–#2680 (handlers that have `TimeProvider` injected but still call `DateTime.UtcNow`/`DateTime.Now` for individual fields). Those are tracked separately.
- Any other module (Catalog, Logistics, Purchase, etc.) — this spec is strictly scoped to the two named handlers in Manufacture.
- Migrating production servers' OS timezone. The fix removes the dependency on OS timezone rather than changing the OS configuration.
- Changing the semantic meaning of "current date" from UTC to a configured business timezone (e.g., Europe/Prague). If that's the desired direction, it's a separate design decision (see Open Questions).
- Refactoring the gap-filling loop logic itself.
- Adding integration or E2E tests — unit-level coverage with `FakeTimeProvider` is sufficient for this fix.

## Open Questions
None.

## Status: COMPLETE