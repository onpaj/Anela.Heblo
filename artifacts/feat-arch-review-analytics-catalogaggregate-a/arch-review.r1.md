I have the full picture. Writing the architecture review now.

# Architecture Review: Extract `CatalogAggregate` → `AnalyticsProduct` Mapping Helper

## Skip Design: true

Backend-only refactor of an internal mapping helper. No UI components, no API surface, no visual design decisions.

## Architectural Fit Assessment

**The spec is largely stale.** The duplication it targets has already been removed by PR #1847 ("Remove Analytics → Catalog Direct Cross-Module Dependency", commit `00c730b5`). The current state of the codebase is:

- `AnalyticsRepository.cs` (the file the brief and spec point at) no longer contains the duplicated mapping blocks at all. Both `StreamProductsWithSalesAsync` (lines 29–36) and `GetProductAnalysisDataAsync` (lines 70–77) are thin delegations to `IAnalyticsProductSource`.
- The single canonical `MapToAnalyticsProduct` helper already exists at `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs:74-119`.
- That location is exactly where the spec said the helper would "naturally move when #1805 is resolved" — and #1805 has been resolved.

Therefore **FR-1, FR-2, and FR-4 are already satisfied** by code in `main`. The only outstanding behavioural item is **FR-3 (sales-history filtering consistency)**, which is now a one-line change inside the existing adapter — not an extraction.

This also means the cross-module pattern is settled (consumer owns `IAnalyticsProductSource` in `Application/Features/Analytics/Contracts/`; Catalog owns the adapter and registers it in `CatalogModule`). No new architectural decisions are required. The task collapses to: change one behaviour, update one test.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application.Features.Analytics
├── Contracts/
│   └── IAnalyticsProductSource             (consumer-owned contract, unchanged)
└── Infrastructure/
    └── AnalyticsRepository                 (thin delegator, unchanged)

Anela.Heblo.Application.Features.Catalog.Infrastructure
└── CatalogAnalyticsSourceAdapter           (provider-owned adapter)
    ├── StreamProductsWithSalesAsync()      (already filters SalesHistory)
    ├── GetProductAnalysisDataAsync()       ← MODIFY: apply same filter
    └── MapToAnalyticsProduct() (private)   (canonical helper, unchanged)
```

The adapter already accepts a pre-projected `List<SalesDataPoint>` parameter so the mapping helper is independent of where filtering happens. That structural decision was the right one and we keep it.

### Key Design Decisions

#### Decision 1: Apply the fix in-place, do not re-derive structure
**Options considered:**
- (A) Treat spec as a fresh extraction — duplicate the work that PR #1847 already did.
- (B) Re-interpret the spec as a behavioural correction inside the existing adapter.

**Chosen approach:** B.

**Rationale:** A would either no-op (the inline blocks the spec wants to delete don't exist) or regress the cross-module decoupling. The valuable kernel of the spec is FR-3, which is still actionable and unblocks the same maintainability goal the spec articulates.

#### Decision 2: Filter `SalesHistory` at the adapter, not push to callers
**Options considered:**
- (A) Filter `SalesHistory` inside `GetProductAnalysisDataAsync` so both adapter paths are symmetric (the spec's intent).
- (B) Filter at the consumer (`GetProductMarginAnalysisHandler`) and document the adapter as "returns unfiltered, callers must filter".

**Chosen approach:** A.

**Rationale:** `GetProductMarginAnalysisHandler` (the sole caller of `GetProductAnalysisDataAsync`) already re-filters `productData.SalesHistory` in three places (`HasSalesInPeriod`, `CalculateProductMargins`, `BuildSuccessResponse.MonthlyBreakdown`). Filtering at the source is therefore observably equivalent for the only consumer and matches `StreamProductsWithSalesAsync` semantics. The `IAnalyticsProductSource` contract's XML doc does not promise unfiltered output; it says it returns "a single product projected to `AnalyticsProduct`" with the same period parameters — symmetry is the natural reading.

#### Decision 3: Do not simplify the consumer's redundant filters in this change
The three `.Where(s => s.Date >= ... && s.Date <= ...)` calls in `GetProductMarginAnalysisHandler.cs` will become no-ops after the fix. Leave them. They are defensive, cost nothing on already-filtered data, and removing them expands the blast radius beyond the spec's scope.

## Implementation Guidance

### Directory / Module Structure
No new files. Touch only:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` | Replace lines 60–69 (the explicitly-preserved unfiltered projection) with the same filtering projection used in `StreamProductsWithSalesAsync` (lines 35–43). Remove the now-incorrect comment on lines 60–61. |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` | Invert `GetProductAnalysisDataAsync_PreservesUnfilteredSalesHistory` (lines 253–284): rename to `GetProductAnalysisDataAsync_FiltersSalesHistoryByPeriod`, assert one in-window record returned. |

### Interfaces and Contracts
`IAnalyticsProductSource` is unchanged. No update to its XML doc is required (current wording does not state a filter contract). Optionally tighten the doc on `GetProductAnalysisDataAsync` to read "…projected to `AnalyticsProduct` with `SalesHistory` filtered to `[fromDate, toDate]`." — recommended for parity with the streaming method's behaviour.

### Data Flow
For the single-product path:

```
GetProductMarginAnalysisHandler.Handle
  → AnalyticsRepository.GetProductAnalysisDataAsync (delegator)
    → CatalogAnalyticsSourceAdapter.GetProductAnalysisDataAsync
       1. _catalogRepository.GetByIdAsync(productId)
       2. project SalesHistory.Where(date in [fromDate, toDate]) → List<SalesDataPoint>   ← CHANGE
       3. MapToAnalyticsProduct(product, fromDate, toDate, filteredSales)
```

The mapping helper itself is untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A future caller depends on the unfiltered behaviour (none exists today, but the existing test name asserts intent). | Low | Audit at change time: only `GetProductMarginAnalysisHandler` calls this method, and it re-filters internally. Commit message must state the consistency fix explicitly. |
| The flipped test masks the change in code review (looks like a small rename). | Low | Title the PR with "behaviour change: SalesHistory now filtered by period" and call it out in the description. |
| Consumer-side redundant `.Where()` filters remain and could later mislead a reader. | Very low | Out of scope for this spec; leave for a follow-up if it becomes a real readability problem. |
| The brief/spec mismatch with reality is not surfaced. | Medium | This review surfaces it; the PR description must reference PR #1847 and clarify what's actually being changed (FR-3 only). |

## Specification Amendments

1. **FR-1, FR-2 — already implemented.** Mark as "already satisfied by PR #1847 (`00c730b5`)". Do not re-do.
2. **FR-3 — retain as the sole functional change.** Reword scope to "modify `CatalogAnalyticsSourceAdapter.GetProductAnalysisDataAsync` to filter `SalesHistory` by `[fromDate, toDate]` when projecting to `SalesDataPoint`".
3. **FR-4 — clarify file scope.** Replace "`backend/src/.../AnalyticsRepository.cs` only" with "`backend/src/.../Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` (+ adapter test file)". The brief's referenced file no longer contains the mapping.
4. **NFR-3 — clarify test update.** The test that locks in the previously-incorrect behaviour exists today: `CatalogAnalyticsSourceAdapterTests.GetProductAnalysisDataAsync_PreservesUnfilteredSalesHistory` (lines 253–284). It must be inverted; no new test file is required.
5. **Out of Scope addition:** Removing the redundant `.Where()` date filters in `GetProductMarginAnalysisHandler.cs` after they become no-ops.
6. **Status — change from "COMPLETE" to "AMENDED — see arch review".** The "COMPLETE" status is misleading given the staleness.

## Prerequisites

None. No migrations, config, or infrastructure changes. `dotnet build` + `dotnet format` + running the touched test (`CatalogAnalyticsSourceAdapterTests`) is sufficient for completion.