```markdown
# Architecture Review: Extract `ResolveDateRange` Helper in `GiftPackageManufactureService`

## Skip Design: true

This is a backend-only, internal refactor of a single application-layer service class. No UI components, screens, layouts, or visual decisions are introduced.

## Architectural Fit Assessment

The change fits cleanly and requires no architectural deliberation:

- **Layer**: Application layer, vertical slice `Features/Logistics/UseCases/GiftPackageManufacture/Services/`. The refactor stays entirely within `GiftPackageManufactureService.cs` and does not cross any module boundary defined in `docs/architecture/filesystem.md` or `development_guidelines.md`.
- **Time handling**: Aligns with `docs/architecture/Dev_Guidelines_time.md` — the helper continues to read "now" via the injected `TimeProvider` (`_timeProvider.GetUtcNow().DateTime`), not `DateTime.UtcNow`. This is already a project-wide invariant and the spec preserves it.
- **Helper placement convention**: The class already keeps two private helpers (`CalculateSeverity` at line 337, `CalculateStockCoveragePercent` at line 354) below the public methods. The new `ResolveDateRange` belongs in the same trailing helpers region. Unlike those two, it must be **instance** (not `static`) because it touches `_timeProvider` — the spec is correct on this point.
- **No contract surface changes**: No interface (`IGiftPackageManufactureService`), DTO, MediatR request/response, handler, controller, mapping profile, or DI registration is touched. The CLAUDE.md rule that "DTOs are classes, never records" is irrelevant here because no DTO is added; the helper's value-tuple return is an internal implementation detail, not a contract type.
- **Existing test coverage is sufficient.** `GiftPackageManufactureServiceTests.cs` already exercises every observable branch of the duplicated block:
  - `GetAvailableGiftPackagesAsync_WithZeroDaysDiff_ShouldUseDaysDiffAsOne` covers the `Math.Max(..., 1)` floor.
  - `GetAvailableGiftPackagesAsync_WithCustomDateRange_ShouldUseSpecifiedDates` and the matching `GetGiftPackageDetailAsync_…` test cover the explicit-dates path.
  - The default-flow tests (`…ShouldReturnGiftPackagesWithCorrectDailySales`, `…ShouldReturnGiftPackageWithIngredients`) exercise the `null`-defaulting branch via the mocked `TimeProvider`.
  This is enough to prove behavioral parity post-refactor; no new tests are required (and the spec correctly puts new tests out of scope).

## Proposed Architecture

### Component Overview

```
GiftPackageManufactureService (unchanged surface)
├── public  GetAvailableGiftPackagesAsync(...)   ── calls ──┐
├── public  GetGiftPackageDetailAsync(...)       ── calls ──┤
├── public  CreateManufactureAsync(...)                     │
├── public  DisassembleGiftPackageAsync(...)                │
├── private static CalculateSeverity(...)                   │
├── private static CalculateStockCoveragePercent(...)       │
└── private        ResolveDateRange(DateTime?, DateTime?)  ◀┘  ← NEW (instance, not static)
                   returns (DateTime From, DateTime To, int Days)
                   reads _timeProvider for "now"
```

No new files, no new types, no new namespaces, no new DI bindings.

### Key Design Decisions

#### Decision 1: Value tuple vs. dedicated record/struct
**Options considered:**
- (A) `(DateTime From, DateTime To, int Days)` named value tuple — what the spec mandates.
- (B) Private nested `record struct DateRange(DateTime From, DateTime To, int Days)`.
- (C) Three `out` parameters.

**Chosen approach:** (A) — value tuple, as specified.

**Rationale:** The helper is called from exactly two sites in one class. A nested type would add noise (one more name, one more declaration) for no readability gain at this scale, and would invite future widening of scope (extension methods, comparisons, equality semantics) that the spec explicitly puts out of scope. `out` parameters reintroduce mutation at the call site and are objectively worse than the tuple. Named tuple elements give the call site property-style access (`var range = ResolveDateRange(...); range.Days`) or destructuring (`var (from, to, days) = ResolveDateRange(...)`) — both readable.

#### Decision 2: Instance method vs. static
**Options considered:**
- (A) `private` instance method (uses `_timeProvider`).
- (B) `private static` with `TimeProvider` passed as a parameter.

**Chosen approach:** (A) — instance, matching the spec.

**Rationale:** Consistency with how the rest of the class uses `_timeProvider` (lines 189, 270 in `CreateManufactureAsync` / `DisassembleGiftPackageAsync`). Passing `_timeProvider` through as a parameter would be ceremony with no benefit — the helper is private and has no other caller. The two existing static helpers (`CalculateSeverity`, `CalculateStockCoveragePercent`) are static precisely because they take no dependency on instance state; that distinction should be preserved, not erased.

#### Decision 3: Scope of the helper — date-range only, not `dailySales`
**Options considered:**
- (A) Helper returns only `(From, To, Days)`. Per-product `totalSalesInPeriod`/`dailySales` stays inline.
- (B) Helper also accepts the product + `salesCoefficient` and returns `dailySales`.

**Chosen approach:** (A), as specified.

**Rationale:** `dailySales` depends on per-product values (`product.GetTotalSold(...)`, `salesCoefficient`) that are NOT duplicated in the same way — in `GetAvailableGiftPackagesAsync` the computation runs inside a `foreach` over products, while in `GetGiftPackageDetailAsync` it runs once for a single resolved product. Pulling `dailySales` into the helper would force one of the two call sites into an awkward shape and would not actually deduplicate anything that isn't already deduplicated by the date-range extraction. The brief and spec both correctly limit scope here.

#### Decision 4: Preserve `DateTime.Kind` semantics exactly
**Options considered:**
- (A) `_timeProvider.GetUtcNow().DateTime` (current behavior; `Kind == Unspecified`).
- (B) `_timeProvider.GetUtcNow().UtcDateTime` (`Kind == Utc`).

**Chosen approach:** (A), unchanged from current code.

**Rationale:** A "cleanup" to `.UtcDateTime` would silently change `DateTime.Kind` on the returned value, which can ripple into EF Core mappings, serialization, and comparison logic. The spec promises an observable no-op; preserving the exact `.DateTime` expression honors that promise. Flag this explicitly to the implementer so it isn't "improved" during the change.

## Implementation Guidance

### Directory / Module Structure

Single-file edit. No new files.

```
backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/
└── GiftPackageManufactureService.cs   (modify)
```

### Interfaces and Contracts

None added, removed, or changed.

The helper's signature (internal contract for the implementer):
```csharp
private (DateTime From, DateTime To, int Days) ResolveDateRange(DateTime? fromDate, DateTime? toDate)
```

Placement: bottom of the class, alongside `CalculateSeverity` (line 337) and `CalculateStockCoveragePercent` (line 354). Order within the helpers region is not load-bearing; placing `ResolveDateRange` immediately before `CalculateSeverity` reads naturally.

### Data Flow

For both `GetAvailableGiftPackagesAsync` (call site at lines 55–57) and `GetGiftPackageDetailAsync` (call site at lines 112–114):

```
caller passes (fromDate?, toDate?)
        │
        ▼
ResolveDateRange(fromDate, toDate)
        │   1. to   ← toDate   ?? _timeProvider.GetUtcNow().DateTime
        │   2. from ← fromDate ?? to.AddYears(-1)
        │   3. days ← Math.Max((to - from).Days, 1)
        ▼
(actualFromDate, actualToDate, daysDiff)
        │
        ▼
existing inline computation:
  totalSalesInPeriod = (decimal)product.GetTotalSold(actualFromDate, actualToDate) * salesCoefficient;
  dailySales         = totalSalesInPeriod / daysDiff;
```

**Required call-site form** (preserves all downstream variable names so no other lines change):
```csharp
var (actualFromDate, actualToDate, daysDiff) = ResolveDateRange(fromDate, toDate);
```

Do **not** use `var range = ResolveDateRange(...);` and then `range.From`/`range.To`/`range.Days` — that would force editing all downstream lines and broaden the diff beyond what the spec permits.

Lines to delete in each method:
- `GetAvailableGiftPackagesAsync`: lines 55–57 (three statements).
- `GetGiftPackageDetailAsync`: lines 112–114 (three statements).

The `// Calculate date range for daily sales calculation` / `// Use provided dates or fallback to last 12 months` comments above each block lose their referent once the inline code is replaced. They may be removed, or kept as a one-line lead-in to the destructuring call. Either is acceptable; do not invent new commentary.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent change to `DateTime.Kind` if implementer switches `.DateTime` → `.UtcDateTime` | Medium | Decision 4 above calls this out explicitly. Reviewer must verify the helper uses `_timeProvider.GetUtcNow().DateTime`, not `.UtcDateTime`. |
| Defaulting order regression — `fromDate` defaulting against "now" instead of resolved `to` | Medium | Spec FR-1 mandates the precedence; existing test `WithCustomDateRange_…` does not catch this because both args are supplied. The default-path tests (e.g., `…ShouldReturnGiftPackagesWithCorrectDailySales`) implicitly cover it when only one of the two is null in any test scenario. Reviewer should diff the helper body against the spec's reference implementation line-for-line. |
| Call-site uses `range.From` style and forces editing 5+ downstream lines | Low | Implementation Guidance fixes the destructuring form. PR review rejects deviations. |
| Future-temptation: extracting `dailySales` too | Low | Out-of-scope list in the spec is explicit. Reviewer enforces. |
| New analyzer warning (e.g., IDE0042 "deconstruct variable declaration") on the destructuring line | Low | Already idiomatic C# 8+; project compiles other tuple-deconstructions cleanly. If a warning appears, prefer suppressing inline at the call site over changing the helper's return shape. |
| Hidden third caller introduced concurrently on another branch | Low | Single-file diff makes this trivial to spot in PR review. `git grep` for the duplicated block on `main` before merge if there's any doubt. |

## Specification Amendments

None. The spec is precise, correctly scoped, and consistent with the codebase as it stands. Two clarifications worth surfacing to the implementer (not changes to the spec, but emphasis):

1. **Use destructuring at the call site**, not a named local. This is implicit in FR-2's "no other lines need editing" acceptance criterion but worth stating outright to prevent a wider diff.
2. **Keep `_timeProvider.GetUtcNow().DateTime` verbatim** inside the helper. Do not "modernize" to `.UtcDateTime`. The spec already says "use the existing injected `_timeProvider`," and this is the most likely accidental drift.

## Prerequisites

None. No migrations, configuration, infrastructure, package additions, or feature flags are required. Implementation can begin immediately and ships in a single commit on a single feature branch.
```