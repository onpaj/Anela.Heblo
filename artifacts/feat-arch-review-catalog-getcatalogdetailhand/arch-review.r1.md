# Architecture Review: Extract `ComputeFromDate` helper in `GetCatalogDetailHandler`

## Skip Design: true

## Architectural Fit Assessment

The refactor fits cleanly within existing patterns:

- **Vertical Slice boundary respected.** Change is contained to a single file inside `Application/Features/Catalog/UseCases/GetCatalogDetail/`. No contracts, DTOs, controllers, or module-boundary surface is touched. No cross-module impact.
- **Handler structure unchanged.** `GetCatalogDetailHandler` already groups its history-projection helpers as private instance methods (`GetSalesHistoryFromAggregate`, `GetPurchaseHistoryFromAggregate`, etc.). A private `ComputeFromDate` helper sits naturally alongside them.
- **TimeProvider injection preserved.** The helper uses the already-injected `TimeProvider`, keeping the handler deterministic under test (matches the pattern in `GetCatalogDetailHandlerFullHistoryTests.cs`, which mocks `TimeProvider`).
- **Constants module already curated.** `CatalogConstants` already owns `ALL_HISTORY_MONTHS_THRESHOLD`. The companion floor date `2020-01-01` is the only sibling concept that lives inline — this review recommends moving it into the same module (see Decision 2).

Integration points: existing test suites in `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` and `GetCatalogDetailHandlerFullHistoryTests.cs`. Both already exercise the threshold boundary; behavior preservation is verifiable against them without new tests.

## Proposed Architecture

### Component Overview

```
GetCatalogDetailHandler (Application/Features/Catalog/UseCases/GetCatalogDetail/)
├── Handle(...)                                 [unchanged]
├── GetSalesHistoryFromAggregate(...)           [unchanged — no "all history" mode]
├── GetPurchaseHistoryFromAggregate(...)        [FR-3: uses ComputeFromDate, no early return]
├── GetConsumedHistoryFromAggregate(...)        [unchanged — no "all history" mode]
├── GetManufactureHistoryFromAggregate(...)     [FR-3: uses ComputeFromDate, no early return]
├── GetManufactureCostHistoryFromMargins(...)   [FR-2: uses ComputeFromDate]
├── GetMarginHistoryFromMargins(...)            [FR-2: uses ComputeFromDate]
└── ComputeFromDate(int monthsBack) : DateTime  [NEW — single source of truth]

CatalogConstants  (Application/Features/Catalog/)
├── ALL_HISTORY_MONTHS_THRESHOLD = 999          [unchanged]
└── HISTORY_FLOOR_DATE = new DateTime(2020,1,1) [NEW — recommended, see Decision 2]
```

Note: `GetSalesHistoryFromAggregate` and `GetConsumedHistoryFromAggregate` do **not** participate in the "all history vs. N months back" pattern — they always filter by `currentDate.AddMonths(-monthsBack)` regardless of `monthsBack` size. The refactor must not touch them.

### Key Design Decisions

#### Decision 1: Private instance method, not static / not extension / not class
**Options considered:**
- (a) Private instance method on `GetCatalogDetailHandler` (per spec).
- (b) Private static method taking `TimeProvider` as a parameter.
- (c) Public utility in a `CatalogDateRange` helper class for cross-handler reuse.

**Chosen approach:** (a) Private instance method, exactly as specified.

**Rationale:** Closure over `_timeProvider` keeps the call-site clean (`ComputeFromDate(monthsBack)`). The brief is filed against duplication *inside this handler*; lifting to a shared helper without other consumers is speculative generality (YAGNI). If the same pattern surfaces in another handler later, promote then — not now.

#### Decision 2: Extract `2020-01-01` to `CatalogConstants.HISTORY_FLOOR_DATE`
**Options considered:**
- (a) Inline the literal `new DateTime(2020, 1, 1)` inside `ComputeFromDate` (per spec — explicitly out of scope for this refactor).
- (b) Add `public static readonly DateTime HISTORY_FLOOR_DATE = new(2020, 1, 1);` to `CatalogConstants`.

**Chosen approach:** (b). This is a **specification amendment** — see "Specification Amendments" below.

**Rationale:** `CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD` and the floor date are two halves of the same business concept ("what does 'all history' mean?"). Splitting them — one in the constants file, one inline in a handler helper — recreates the same drift risk the brief is trying to eliminate, just at a smaller scale. The literal `new DateTime(2020, 1, 1)` already appears in `GetMarginReportHandlerTests.cs` line 200, which hints the value is conceptually shared. Cost is one line; benefit is conceptual cohesion with the threshold it depends on. This stays surgical (no DI, no config — just a named constant).

#### Decision 3: Accept Pattern B's filter-based unification (FR-3) only after verifying the data invariant
**Options considered:**
- (a) Unify Pattern B with Pattern A using `.Where(p.Date >= fromDate)` and `fromDate == 2020-01-01` for full history (per spec).
- (b) Keep Pattern B's early-return structure but factor out the `currentDate` line; do not unify.

**Chosen approach:** (a), conditional on the verification step in Risks below.

**Rationale:** The spec correctly identifies that `.Where(p.Date >= 2020-01-01)` and "return all records" are equivalent *only if no record in the source collection has `Date < 2020-01-01`*. The existing production code's defensive comment ("to avoid potential issues with very old dates") suggests this invariant may not be guaranteed. The existing test fixture's earliest record is `2020-01-10` — *above* the floor — so the test suite would not catch a regression where a 2019 purchase record gets dropped. See Risk R1.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits limited to:

- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs` (if Decision 2 is accepted)

### Interfaces and Contracts

None. The helper is `private`. No public surface changes. No DTO, MediatR contract, or OpenAPI generator output is affected — the TypeScript client does not need regeneration.

### Data Flow

Before (Pattern B, e.g. `GetPurchaseHistoryFromAggregate`):
```
monthsBack ──► [threshold check]
                  │
                  ├──► true:  return ALL records, ordered desc
                  └──► false: fromDate = now - monthsBack
                              return Where(Date >= fromDate), ordered desc
```

After:
```
monthsBack ──► ComputeFromDate(monthsBack) ──► fromDate
                                                  │
                                                  ▼
                                  return Where(Date >= fromDate), ordered desc

  ComputeFromDate:
     monthsBack >= 999 ──► HISTORY_FLOOR_DATE (2020-01-01)
     otherwise         ──► _timeProvider.GetUtcNow().Date.AddMonths(-monthsBack)
```

The Pattern A flow is structurally identical (already used `.Where(... >= fromDate)`).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **R1: Pattern B unification silently drops pre-2020 records.** If `catalogItem.PurchaseHistory` or `ManufactureHistory` contains any record with `Date < 2020-01-01`, the refactor changes observable output. Existing test fixtures all use dates ≥ 2020-01-10, so this regression is invisible to the current suite. | **HIGH** | Before merging FR-3: (a) query the production data source (or staging snapshot) for `MIN(Date)` in both `PurchaseHistory` and `ManufactureHistory` projections — if any record predates 2020-01-01, escalate; (b) add one parameterized test case with a fixture record dated 2019-12-31 to lock in the new behavior. If pre-2020 records must be preserved, set `HISTORY_FLOOR_DATE` to `DateTime.MinValue` (or an earlier sentinel) instead of 2020-01-01. |
| **R2: Threshold boundary semantics.** `ALL_HISTORY_MONTHS_THRESHOLD = 999`, and the validator (`GetCatalogDetailRequestValidator`) caps `MonthsBack <= 999`. The comparison `>=` means exactly 999 triggers full history. Easy to flip to `>` accidentally. | **LOW** | The spec is explicit (`>=`). Reviewer to verify the literal operator in the diff matches `>=`. Existing `CatalogConstantsTests.ALL_HISTORY_MONTHS_THRESHOLD_IsUsedInValidation` test covers `justBelowThreshold`, `atThreshold`, `aboveThreshold` — confirm those still pass unmodified. |
| **R3: Untouched siblings drift later.** `GetSalesHistoryFromAggregate` and `GetConsumedHistoryFromAggregate` also call `_timeProvider.GetUtcNow().Date` for a different purpose (no threshold logic). They are correctly out of scope, but a future reader may incorrectly assume the helper should cover them. | **LOW** | A one-line XML doc comment on `ComputeFromDate` stating it encodes the *"all history vs. N months back"* convention (not a general "now()" accessor) is sufficient. No other action. |
| **R4: `dotnet format` reformats adjacent unchanged code.** The handler has consistent formatting; a slip in editor settings could trigger churn. | **LOW** | Run `dotnet format` once on the modified file and confirm the diff is limited to the four target methods + helper. CLAUDE.md mandates `dotnet format` reports no changes after the refactor. |

## Specification Amendments

1. **Promote `HISTORY_FLOOR_DATE` to `CatalogConstants` (overrides spec "Out of Scope" item 1).** Add:
   ```csharp
   public static readonly DateTime HISTORY_FLOOR_DATE = new DateTime(2020, 1, 1);
   ```
   and use it inside `ComputeFromDate`. This pairs the floor date with the threshold constant that selects it, eliminating the cross-file conceptual split. Cost is negligible; the brief's stated goal ("one definition of full history") is more cleanly achieved by including it. FR-1's acceptance criterion *"the hardcoded floor date appears exactly once in the file"* is upgraded to *"appears exactly once in the codebase under Catalog"* — verifiable by `grep -r 'new DateTime(2020, 1, 1)' backend/src/Anela.Heblo.Application/Features/Catalog/`.

2. **Add a Pattern B equivalence test (addresses R1).** Insert a parameterized test under `GetCatalogDetailHandlerFullHistoryTests` whose fixture contains a record dated `2019-12-31` and asserts the expected post-refactor behavior (excluded under new logic, included under old logic). This locks the decision down explicitly rather than relying on the spec's prose claim of equivalence. This is a small additive change to the test suite (acceptable inside the "behavior preservation" framing; the test pins the *intentional* clarification of behavior at the boundary).

3. **Clarify FR-3 acceptance language.** Change *"the records returned are identical to those returned by the pre-refactor early-return code path"* to *"the records returned are identical to those returned by the pre-refactor early-return code path, **assuming no source record has `Date < HISTORY_FLOOR_DATE`**"*. The assumption must be verified per R1 before merge — not implied as obvious.

## Prerequisites

- **Data check (R1):** Confirm via production / staging snapshot that no `CatalogPurchaseRecord` or `CatalogManufactureRecord` has `Date < 2020-01-01`. If any exist, halt and revisit the floor date value before proceeding with FR-3.
- **No migrations.** No EF / schema work.
- **No config / Key Vault.** No appsettings changes; the floor date stays a code constant (per Decision 2 — DI / config promotion is correctly out of scope).
- **No client regeneration.** OpenAPI client output is unaffected; no `npm run` step needed on the frontend.
- **Validation gate before completion (per CLAUDE.md):** `dotnet build` + `dotnet format` + `dotnet test` (Catalog tests, at minimum: `GetCatalogDetailHandlerTests`, `GetCatalogDetailHandlerFullHistoryTests`, `CatalogConstantsTests`) all green.