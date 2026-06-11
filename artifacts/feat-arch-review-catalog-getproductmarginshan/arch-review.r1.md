# Architecture Review: Inject TimeProvider into GetProductMarginsHandler

## Skip Design: true

## Architectural Fit Assessment

This refactor is a textbook alignment with existing conventions. The Catalog module already uses `TimeProvider` consistently — verified across `GetCatalogDetailHandler` (constructor-injects `TimeProvider`, uses `_timeProvider.GetUtcNow().Date.AddMonths(-monthsBack)` at line 255 for the identical 13-month window pattern), `CatalogDataRefreshService`, `CatalogMergeService`, `CatalogCacheStore`, `EshopStockDomainService`, `LowStockAlertTile`, and `InventoryCountTileBase`. `GetProductMarginsHandler` is the lone outlier.

DI registration already exists: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:128` registers `services.AddSingleton(TimeProvider.System)`. No composition-root changes required.

Test conventions are also established. The Catalog test suite uses **Moq's `Mock<TimeProvider>`**, not `FakeTimeProvider` — see `GetCatalogDetailHandlerTests.cs:23, 31, 57` (`_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate))`). The spec's reference to `Microsoft.Extensions.Time.Testing.FakeTimeProvider` should be reconciled with this — see Specification Amendments.

Integration points are minimal: one constructor signature, one expression. No DTO, MediatR contract, HTTP route, or call-site change. MediatR resolves the handler via DI and is unaffected by the additional dependency.

## Proposed Architecture

### Component Overview

```
DI Container (singleton)
    └─ TimeProvider.System
        │
        ▼ (constructor injection — new edge)
GetProductMarginsHandler
    ├─ ICatalogRepository  (existing)
    ├─ TimeProvider        (NEW — wired identically to GetCatalogDetailHandler)
    └─ ILogger<...>        (existing)
        │
        ▼
MapToMarginDto(product)
    └─ _timeProvider.GetUtcNow().DateTime.AddMonths(-13)   // replaces DateTime.Now
        └─ filter product.Margins.MonthlyData by (m.Key >= dateFrom)
```

No new components. One new edge from DI to handler, one call-site swap inside `MapToMarginDto`.

### Key Design Decisions

#### Decision 1: Inject `TimeProvider` (not `IDateTimeProvider` / custom abstraction)
**Options considered:**
- (A) Constructor-inject `TimeProvider` (the .NET 8 abstraction).
- (B) Introduce a project-specific abstraction (`IClock`, `IDateTimeProvider`).
- (C) Mark the handler partial and provide a static seam.

**Chosen approach:** (A) — `TimeProvider`.
**Rationale:** Every other Catalog handler and service uses `TimeProvider`. Introducing a parallel abstraction would fragment the codebase. Static seams are untestable in parallel and violate the project's DI conventions.

#### Decision 2: Use `_timeProvider.GetUtcNow().DateTime` (not `.UtcDateTime` or `.Date`)
**Options considered:**
- (A) `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)` — matches spec FR-2.
- (B) `_timeProvider.GetUtcNow().UtcDateTime.AddMonths(-13)` — semantically identical (DateTimeOffset returned by `GetUtcNow()` always carries UTC offset).
- (C) `_timeProvider.GetUtcNow().Date.AddMonths(-13)` — matches the sibling pattern in `GetCatalogDetailHandler:255`.

**Chosen approach:** (A) — `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`.
**Rationale:** The original code used `DateTime.Now` (includes time-of-day component, not date-truncated). Preserving that — minus the timezone bug — minimizes behavioral drift. `MonthlyData.Key` is presumably a month-bucketed value where the time-of-day component is irrelevant to `>=` comparison, so (A) and (C) produce identical observable results in practice; but (A) is the literal spec text and a smaller diff. Option (B) is functionally identical to (A).

#### Decision 3: Mock `TimeProvider` via Moq (not `FakeTimeProvider`)
**Options considered:**
- (A) `Mock<TimeProvider>` with `.Setup(tp => tp.GetUtcNow()).Returns(...)`.
- (B) `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.

**Chosen approach:** (A) — Moq.
**Rationale:** The Catalog test suite already standardizes on Moq for `TimeProvider` (verified in `GetCatalogDetailHandlerTests.cs`). `FakeTimeProvider` is **not** used anywhere in `backend/test/` (grep returned zero matches). Adopting it for one new test introduces a third-party test pattern for no gain. This deviates from spec language — see Specification Amendments.

## Implementation Guidance

### Directory / Module Structure

No new files. Edit in place:

- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — add field, constructor parameter, swap one expression.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — **new file**. There is currently no test class for this handler (verified by glob + grep). Mirror the AAA / Moq pattern from `GetCatalogDetailHandlerTests.cs`.

### Interfaces and Contracts

**Constructor signature (after change):**
```csharp
public GetProductMarginsHandler(
    ICatalogRepository catalogRepository,
    TimeProvider timeProvider,
    ILogger<GetProductMarginsHandler> logger)
```

Order matches the parameter ordering convention in `GetCatalogDetailHandler` (repository → cross-cutting services → `TimeProvider` → `ILogger` last).

**Field:** `private readonly TimeProvider _timeProvider;`

**Call-site (line ~189):**
```csharp
var dateFrom = _timeProvider.GetUtcNow().DateTime.AddMonths(-13);
```

**Public surface:** unchanged. `GetProductMarginsRequest`, `GetProductMarginsResponse`, `ProductMarginDto`, `MonthlyMarginDto` — all untouched.

### Data Flow

```
HTTP request
  → MediatR.Send(GetProductMarginsRequest)
    → GetProductMarginsHandler.Handle
      → _catalogRepository.GetAllAsync          (unchanged)
      → ApplyFilters / ApplySorting / paging    (unchanged)
      → MapToMarginDto(product)                 (interior change)
          ├─ dateFrom = _timeProvider.GetUtcNow().DateTime.AddMonths(-13)   ◀ CHANGED
          ├─ filter product.Margins.MonthlyData where Key >= dateFrom        (unchanged)
          └─ build ProductMarginDto                                          (unchanged)
      → return GetProductMarginsResponse        (unchanged)
```

Only one expression's source-of-truth changes. Output shape, ordering, and downstream consumers are byte-equivalent for any UTC-resident caller.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Off-by-one month for callers near month boundaries that previously relied on local-time accidental behavior | Low | This is the **correctness fix** itself — local-time semantics were the bug. Frontend consumers display the window as a chart range; a one-day shift around midnight in a non-UTC tz is invisible at month granularity. |
| `MonthlyData.Key` comparison semantics change if Key's `Kind` is `Local`/`Unspecified` | Low | Comparison uses raw `DateTime.>=`, which compares ticks regardless of `Kind`. Verify by reading `CatalogAggregate.Margins.MonthlyData` key type during implementation; if `Kind` mismatch is observed, the dateFrom expression remains correct because `MonthlyData` is bucketed to month-start and the 13-month window has hours of slack. |
| Test project lacks an existing test class for this handler, so coverage debt is exposed | Low | Add the new test file (see Implementation Guidance). Scope it to FR-4 only — do not backfill unrelated tests (out of scope per spec). |
| Spec mandates `FakeTimeProvider`; project convention is `Mock<TimeProvider>` | Low | Spec is amended (below). The spec already permits "the test project's existing fake time provider, if one is already in use" — Moq satisfies that clause. |
| Spec's claim "the DI container resolves `TimeProvider` without additional registration changes" is true here, but worth confirming | Trivial | Confirmed at `ServiceCollectionExtensions.cs:128`. No action. |

## Specification Amendments

1. **FR-4 — test framework choice.** The spec recommends `FakeTimeProvider` but allows "the test project's existing fake time provider, if one is already in use." Verified: the test project does **not** use `FakeTimeProvider` (zero matches in `backend/test/`); the Catalog suite uses `Mock<TimeProvider>` via Moq (e.g. `GetCatalogDetailHandlerTests.cs:23, 31, 57`). **Amendment:** the new `GetProductMarginsHandlerTests` should use `Mock<TimeProvider>` with `.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(fakeUtcNow, TimeSpan.Zero))`, matching the sibling test class. Do **not** introduce `Microsoft.Extensions.TimeProvider.Testing` as a new dependency.

2. **FR-4 — timezone-sensitive test case.** Spec acceptance criterion mentions `2026-01-01T00:30:00Z` and "as it would have with local time in a UTC+1 zone." Since the test injects a mocked `TimeProvider` that returns a fixed `DateTimeOffset` in UTC, the test cannot directly demonstrate the *bug* in the old code — it can only assert the *new* behavior is UTC-based and deterministic. **Amendment:** rephrase the third bullet of FR-4 to: "At least one test sets the mocked UTC time to a value where the local time (in a UTC+1 zone) would fall in the previous day, and asserts the computed `dateFrom` equals the injected UTC time minus 13 months exactly (not the local-time-derived value)." The assertion is on the new contract, not on a regression reproduction.

3. **FR-2 — expression form.** Spec endorses `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`. Note that sibling handler `GetCatalogDetailHandler:255` uses `_timeProvider.GetUtcNow().Date.AddMonths(-monthsBack)` (date-truncated). Both produce identical filtering results against month-bucketed `MonthlyData.Key`. Implementer should use the spec form (`.DateTime`) for minimum diff and exact spec compliance; no amendment required, but flagged for awareness.

4. **Test file location.** The spec doesn't name the test file. Per `backend/test/Anela.Heblo.Tests/Features/Catalog/` convention, the file MUST be `GetProductMarginsHandlerTests.cs` under that folder.

## Prerequisites

All prerequisites are already satisfied — no infrastructure, migration, or registration work blocks implementation:

- ✅ `TimeProvider.System` is registered as a singleton at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:128`. No DI change required.
- ✅ `System.TimeProvider` is available (.NET 8 target).
- ✅ Test project (`backend/test/Anela.Heblo.Tests`) already references Moq and FluentAssertions and has an established `Mock<TimeProvider>` pattern in the Catalog suite — no new test dependencies needed.
- ✅ MediatR handler resolution is by DI and tolerates the new constructor parameter without any registration change.
- ⚠️ No existing test class for `GetProductMarginsHandler` — implementer must create `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` as part of this work (covered by FR-4).

Validation gate before completion: `dotnet build`, `dotnet format`, `dotnet test` on the Catalog test project — all must pass.