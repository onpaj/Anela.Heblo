# Architecture Review: Extract Duplicated Period Totals Calculation in FinancialAnalysisService

## Skip Design: true

This is a pure backend refactor with zero UI surface. No new components, screens, layouts, or visual decisions. The change touches a single private service class and does not alter any DTO, controller, MediatR contract, or OpenAPI shape.

## Architectural Fit Assessment

The proposal is a textbook in-class private helper extraction and fits the existing architecture cleanly:

- **Vertical Slice boundary respected.** `FinancialAnalysisService` lives in `Anela.Heblo.Application.Features.FinancialOverview.Services/`. The helper stays inside the same file — no new namespace, no new project reference, no cross-module surface.
- **No DTO/contract impact.** Per `docs/architecture/development_guidelines.md`, DTOs must be classes in `contracts/`. The helper returns a value tuple consumed only internally and never crosses a process or module boundary, so no OpenAPI generator concerns apply (DTO-as-class rule is irrelevant here).
- **No DI impact.** The helper is `private static` — no module registration, no service lifetime decision, no constructor change. `FinancialAnalysisService`'s existing five dependencies (`ILedgerService`, `IStockValueService`, `ILogger<>`, `IOptions<FinancialAnalysisOptions>`, `IMemoryCache`) remain identical.
- **Consistent with C# coding standards.** A `private static` pure function for stateless calculation matches `csharp-coding-style.md` (immutability via `init`/value semantics, no in-place mutation, expression-bodied when readable). The value-tuple return preserves immutability at call sites.
- **Consistent with testing strategy.** Existing `FinancialAnalysisServiceTests` (`backend/test/Anela.Heblo.Tests/Application/FinancialOverview/`) already use xUnit + Moq + FluentAssertions and exercise the service through its public surface (`GetFinancialOverviewAsync`). FR-4's preference for public-surface coverage aligns with this established pattern.

**Verified against source:** the three duplicated blocks exist at the exact lines the spec names (219–233, 302–309, 505–521), and `LedgerItem` (`Anela.Heblo.Domain.Accounting.Ledger.LedgerItem`) is already imported. No new `using` directive is required.

## Proposed Architecture

### Component Overview

```
FinancialAnalysisService (unchanged externally)
├── Public API (5 methods)         ─ no signature changes
├── GetFinancialOverviewRealTimeAsync()   ──┐
├── GetHybridWithCurrentMonthAsync()       ──┼── all three call:
├── RefreshMonthlyDataAsync()              ──┘
│       │
│       └── CalculatePeriodTotals(debit, credit) : (decimal income, decimal expenses)
│              [NEW private static — single change-point for accounting convention]
│
└── Other private helpers (CreateStockSummary, GetCachedFinancialOverview, …) — untouched
```

No new files, no new types, no new dependencies. The diagram above is the full delta.

### Key Design Decisions

#### Decision 1: Helper visibility — `private static` inside `FinancialAnalysisService`
**Options considered:**
- (A) `private static` in the same class (spec-mandated).
- (B) `internal static` on a new `LedgerCalculations` utility class in the same namespace, with `InternalsVisibleTo` for tests.
- (C) Move to `Anela.Heblo.Domain.Accounting.Ledger` as a domain service.

**Chosen approach:** (A) `private static` in `FinancialAnalysisService`.

**Rationale:** The spec explicitly scopes (B) and (C) out, and there is currently a single consumer. Promoting visibility prematurely violates YAGNI and would force the test project to declare `InternalsVisibleTo`, expanding the change footprint. The 5/6 prefix convention is an Application-layer policy (how *this* feature interprets ledger data for *this* report), not a domain invariant of `LedgerItem` — moving it to Domain would leak feature-specific accounting policy into a shared type. Keep the helper exactly where its sole caller lives.

#### Decision 2: Signature — `IEnumerable<LedgerItem>` parameters, value-tuple return
**Options considered:**
- (A) `IEnumerable<LedgerItem>` in, `(decimal income, decimal expenses)` out (spec-mandated).
- (B) `IReadOnlyCollection<LedgerItem>` to discourage repeated enumeration.
- (C) A dedicated record `PeriodTotals(decimal Income, decimal Expenses)`.

**Chosen approach:** (A).

**Rationale:** `IEnumerable<LedgerItem>` matches what the three call sites already pass (mix of `List<LedgerItem>`, `IEnumerable<LedgerItem>` from `.Where(...)`, and the lazy month-filtered enumerables at lines 502–503). Tightening to `IReadOnlyCollection` would force an upstream `.ToList()` in `GetFinancialOverviewRealTimeAsync` for `monthDebitItems` / `monthCreditItems`, violating NFR-1's "no additional materialization" rule. The double-enumeration cost (one pass for `"5"` filter, one pass for `"6"` filter) is preserved — that is exactly today's behavior, and per NFR-1 must not change. A value tuple beats a record for an internal pair of decimals: zero allocations, ergonomic destructuring at the call site, and no new type to maintain.

#### Decision 3: Test coverage strategy — drive through public surface
**Options considered:**
- (A) Add table-driven (`[Theory]`) tests that exercise `GetFinancialOverviewRealTimeAsync` with crafted `LedgerItem` mixes covering all six FR-4 cases. Behavior preservation for the other two methods is established by snapshot-style equality before/after the refactor.
- (B) Promote helper to `internal` and unit-test it directly via `InternalsVisibleTo`.

**Chosen approach:** (A).

**Rationale:** FR-4 explicitly prefers public-surface coverage and falls back to `internal` only if cases cannot be reached otherwise. All six required cases (debit-5, credit-5, credit-6, debit-6, null account numbers, irrelevant prefixes) are reachable by seeding `ILedgerServiceMock` with crafted `LedgerItem` lists and invoking `GetFinancialOverviewAsync(includeCurrentMonth: false, includeStockData: false)` — the real-time path is the simplest to assert against (no cache interactions, deterministic month bucketing). The existing `FinancialAnalysisServiceTests` constructor already wires the mock infrastructure to do exactly this. Visibility promotion is unnecessary.

## Implementation Guidance

### Directory / Module Structure

No new files. The single change-point is:

```
backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/
└── FinancialAnalysisService.cs   ← ADD private static method, REPLACE 3 blocks
```

Tests:

```
backend/test/Anela.Heblo.Tests/Application/FinancialOverview/
└── FinancialAnalysisServiceTests.cs   ← ADD theories covering FR-4 cases via public surface
```

### Interfaces and Contracts

**No external contract changes.** The only new symbol is:

```csharp
// inside FinancialAnalysisService, place after the constructor and before the public methods
// (or grouped with other private helpers near GetCachedFinancialOverview — match local ordering)
private static (decimal income, decimal expenses) CalculatePeriodTotals(
    IEnumerable<LedgerItem> debitItems,
    IEnumerable<LedgerItem> creditItems)
{
    var debit5 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
    var credit5 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
    var expenses = debit5 - credit5;

    var credit6 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
    var debit6 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
    var income = credit6 - debit6;

    return (income, expenses);
}
```

**Required at each call site:**

```csharp
var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);
```

Local-variable name mappings to preserve:
- `RefreshMonthlyDataAsync` (line 214–215): inputs are `debitItems` / `creditItems` — match exactly.
- `GetHybridWithCurrentMonthAsync` (line 298–299): inputs are `debitItems` / `creditItems` — match exactly.
- `GetFinancialOverviewRealTimeAsync` (lines 502–503): inputs are `monthDebitItems` / `monthCreditItems` (per-month filtered enumerables, **not** the outer `debitItems`/`creditItems`). The call here must be `CalculatePeriodTotals(monthDebitItems, monthCreditItems)`. **This is the one mismatch with the brief's literal example; using the wrong locals here would break per-month bucketing.**

### Data Flow

For all three sites, the dataflow is unchanged:

```
ILedgerService.GetLedgerItems(date range, debit/credit prefix filter)
        │
        ▼
debitItems, creditItems  (IEnumerable<LedgerItem>)
        │
        ▼  (in GetFinancialOverviewRealTimeAsync, further filtered per month)
monthDebitItems, monthCreditItems
        │
        ▼
CalculatePeriodTotals(debit, credit)
   ├── enumerate debit once for prefix "5" → debit5
   ├── enumerate credit once for prefix "5" → credit5  → expenses = debit5 − credit5
   ├── enumerate credit once for prefix "6" → credit6
   └── enumerate debit once for prefix "6" → debit6   → income   = credit6 − debit6
        │
        ▼
(income, expenses)  → consumed by existing MonthlyFinancialData / MonthlyFinancialDataDto construction
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Wrong locals at `GetFinancialOverviewRealTimeAsync` call site (using outer `debitItems`/`creditItems` instead of per-month `monthDebitItems`/`monthCreditItems`) would silently aggregate across all months and break the report. | **High** | Implementation must use the per-month enumerables at line 502–503. Add a regression test that asserts month-bucketed `Income`/`Expenses` values for a 2-month range with ledger items in both months. The current implementation already buckets correctly; any deviation would surface in tests. |
| `IEnumerable` is enumerated four times inside the helper. If a caller ever passes a non-replayable enumerable (e.g., a raw query iterator), aggregation could produce wrong totals or throw. | Low | Today's call sites pass `List<>` (materialized by `await`) or `IEnumerable` produced by `.Where(...)` over a list — all are replayable. NFR-1 forbids adding `.ToList()` inside the helper. Document the assumption in the spec amendment below. |
| Accidental behavior change from "helpful" cleanup (e.g., switching `?.StartsWith("5") == true` to pattern matching, hoisting the prefix to a constant). | Medium | FR-1 and FR-3 explicitly forbid this. Code review should reject any deviation from byte-for-byte predicate preservation. `dotnet format` will not rewrite these. |
| Test that asserts on `income`/`expenses` numerics may be sensitive to fixture ordering. | Low | LINQ `Sum` is order-independent for `decimal` addition under standard accounting magnitudes (no floating-point concern). No new ordering risk introduced. |
| Future drift: a new fourth call site might be added that bypasses `CalculatePeriodTotals` and re-inlines the logic, undoing the refactor. | Low | This is exactly the duplication the refactor eliminates. No automated guard is justified for one method. If it recurs, escalate to an architecture test (the project already enforces module boundaries via reflection tests in `Architecture/ModuleBoundariesTests.cs`). |

## Specification Amendments

1. **Clarify the call site in `GetFinancialOverviewRealTimeAsync` (line 505 area).** The spec and brief both show:
   ```csharp
   var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);
   ```
   At this third site only, the correct local names are `monthDebitItems` / `monthCreditItems` (line 502–503 in current source), **not** the outer `debitItems` / `creditItems` that exist earlier in the method. The replacement here must be:
   ```csharp
   var (income, expenses) = CalculatePeriodTotals(monthDebitItems, monthCreditItems);
   ```
   This is not a behavior change — it preserves the existing per-month bucketing — but it diverges literally from the brief's snippet and should be called out so the implementer doesn't blindly copy the wrong identifiers.

2. **Add an enumeration-replayability note to NFR-1.** Document that `CalculatePeriodTotals` enumerates each input collection twice (once per prefix) and that callers must pass a replayable `IEnumerable<LedgerItem>` (e.g., a materialized collection or a `.Where(...)` over one). This matches today's behavior and prevents future call sites from passing a one-shot iterator.

3. **FR-4 acceptance criterion refinement.** Specify that the new/updated tests target `GetFinancialOverviewAsync(months, includeStockData: false, excludedDepartments: null, includeCurrentMonth: false)` — this routes through `GetFinancialOverviewRealTimeAsync` when the cache is empty (which it is in test-construct), giving the simplest deterministic path to assert the six FR-4 ledger-item cases. The two other paths (`RefreshMonthlyDataAsync`, `GetHybridWithCurrentMonthAsync`) are then covered by virtue of all three sharing the same helper plus existing tests that already exercise them.

## Prerequisites

None. The refactor requires no migrations, no configuration changes, no infrastructure work, no feature flag, no Azure Key Vault entries, no OpenAPI regeneration, and no frontend coordination. Implementation can begin immediately and ship in a single commit.

Validation gates (`dotnet build`, `dotnet format`, full backend test suite) are sufficient — no E2E run required since no HTTP-observable behavior changes.