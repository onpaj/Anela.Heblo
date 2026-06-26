I have what I need to write the review. The spec's premise has been overtaken by code that already exists in the repo.

# Architecture Review: Extract `CatalogAggregate` → `AnalyticsProduct` Mapping Helper

## Skip Design: true

Backend-only refactor with no UI surface.

## Architectural Fit Assessment

**The spec is obsolete.** Active exploration of the codebase reveals that the refactor it proposes has already been performed — by the very issue it cites as "upcoming" (#1805, "Decouple Analytics from Catalog"). Concrete evidence:

1. **`AnalyticsRepository` no longer contains the duplicated mapping.** The current file at `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` (note: location moved from `Application/Features/Analytics/Infrastructure/` cited in the spec) is 237 lines and contains only `GetInvoiceImportStatisticsAsync` / `GetBankStatementImportStatisticsAsync` plus thin delegations to `IAnalyticsProductSource` for `StreamProductsWithSalesAsync` (lines 26–33) and `GetProductAnalysisDataAsync` (lines 35–42). Lines 52–116 and 168–231 referenced by the spec no longer exist.

2. **The single mapping helper already exists.** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` lines 72–117 define `private static AnalyticsProduct MapToAnalyticsProduct(CatalogAggregate, DateTime, DateTime, List<SalesDataPoint>)` and both call sites (`StreamProductsWithSalesAsync` at line 43, `GetProductAnalysisDataAsync` at line 69) route through it. The `hasMargin` boolean is collapsed to a single derivation at line 87 — exactly the structure FR-1 prescribes.

3. **The "latent drift bug" is already fixed.** Both call sites pre-filter `SalesHistory` by `[fromDate, toDate]` before invoking the helper (`CatalogAnalyticsSourceAdapter.cs:34–42` and `:59–67`). The asymmetry FR-3 set out to fix does not exist on `main`.

4. **Test coverage exists.** `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` already covers the adapter — including the boundary translation and mapping paths the spec's FR-5 enumerates.

5. **The decoupling plan that delivered this is on disk.** `docs/superpowers/plans/2026-05-27-decouple-analytics-from-catalog.md` explicitly states (line 21): *"`internal sealed` adapter — depends on `ICatalogRepository`, owns the `CatalogAggregate → AnalyticsProduct` mapping (extracted into a single private helper)"*. The mapping extraction was a step **inside** #1805, not something to land "ahead of" it.

The brief (dated 2026-05-27) was correct at the moment of filing but has been overtaken by events. Both the brief and `spec.r1.md` treat #1805 as future work; on the current branch (`feat-arch-review-analytics-catalogaggregate-a`, based on `main`) #1805 is done.

## Proposed Architecture

### Component Overview

Current — already in place:

```
Analytics module (consumer)              Catalog module (provider)
─────────────────────────────            ─────────────────────────
GetMarginReportHandler                   ICatalogRepository
GetProductMarginSummaryHandler                    ▲
GetProductMarginAnalysisHandler                   │ uses
        │                                         │
        ▼                                CatalogAnalyticsSourceAdapter
IAnalyticsRepository                       (internal sealed)
        │                                  ├─ Stream/GetProductAnalysisData
        ▼                                  └─ MapToAnalyticsProduct  ◄── single mapping site
AnalyticsRepository ──────────────────►  IAnalyticsProductSource
  (delegates only)                         (Analytics-owned contract,
                                            Catalog-provided implementation)
```

No new components required.

### Key Design Decisions

#### Decision 1: Close the spec as superseded rather than implement it

**Options considered:**

- (A) Implement the spec verbatim — would re-introduce a `MapToAnalyticsProduct` method inside `AnalyticsRepository`, which now contains no mapping at all. This would conflict with the decoupling pattern (`docs/architecture/development_guidelines.md` §"Cross-Module Communication"), re-pull `CatalogAggregate` into the Analytics module, and trip the `ModuleBoundariesTests` rule added by #1805.
- (B) Re-target the spec to today's code, then close it — verify the post-#1805 implementation against each FR; close as already-satisfied if it matches, file a focused follow-up if it does not.
- (C) Implement only the deltas (if any) between the spec's helper shape and the adapter's current helper shape.

**Chosen approach:** B. Verify, then close as already delivered by #1805. The mapping is in the right place per the architecture guidelines — moving it back is a regression.

**Rationale:** The spec's own "Out of Scope" section says *"Moving the helper into `CatalogAnalyticsSourceAdapter` — that is #1805's job."* #1805 is done. Re-implementing the spec would either be a no-op or actively harmful.

#### Decision 2: Three verifiable deltas worth a follow-up (if anything)

Active comparison of the spec's FR-1 against `CatalogAnalyticsSourceAdapter.cs:72–117` surfaces three minor discrepancies. None justify the original spec's scope; at most they justify a small, separate ticket. Decide explicitly whether each is intended.

1. **`SalesHistory` filter location.** Spec FR-1 places `Where(s => s.Date >= fromDate && s.Date <= toDate).Select(...)` **inside** the helper; the adapter pre-filters **before** the helper (`:34–42`, `:59–67`) and passes the projected list in. Behavior is identical. The duplication the spec was eliminating still exists in a smaller form — six lines repeated across the two call sites. Recommendation: fold the projection into the helper to fully realize FR-1's "one place" intent. Low effort, no behavior change.
2. **`M1Amount` / `M1Percentage` source slice.** The brief's suggested helper reads `M1.Amount` / `M1.Percentage`; the adapter reads `M1_A.Amount` / `M1_A.Percentage` (`:105`, `:108`). The original duplicated blocks (now deleted) are the only source of truth for which slice is "correct preserved behavior"; check git history before changing anything. **Do not silently flip this without confirming with whoever owns margin semantics.**
3. **`Type` mapping.** The adapter calls `MapProductType(product.Type)` (`:100`), translating `Catalog.ProductType → AnalyticsProductType` at the boundary — added by #1805. The spec's helper preserves `product.Type` raw. The adapter version is correct under the post-#1805 type system; do not regress this.

## Implementation Guidance

The only implementation work justified by this review is **verification + closure**, with an optional small follow-up for delta (1).

### Directory / Module Structure

No new files. No moved files. If the optional follow-up for delta (1) is taken:

- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — move the `SalesHistory.Where(...).Select(...).ToList()` projection inside `MapToAnalyticsProduct`, change signature to `MapToAnalyticsProduct(CatalogAggregate, DateTime, DateTime)`, drop the duplicated projection from the two call sites.
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` — add a regression test asserting `SalesHistory` outside `[fromDate, toDate]` is excluded for both `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync`.

### Interfaces and Contracts

`IAnalyticsProductSource` (Domain) and `IAnalyticsRepository` are unchanged. `MapToAnalyticsProduct` is and must remain `private static` on `CatalogAnalyticsSourceAdapter` — never exposed cross-module.

### Data Flow

Unchanged: `Handler → IAnalyticsRepository → AnalyticsRepository → IAnalyticsProductSource → CatalogAnalyticsSourceAdapter → ICatalogRepository → MapToAnalyticsProduct → AnalyticsProduct`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementing spec verbatim re-couples Analytics to `CatalogAggregate`, breaking the `ModuleBoundariesTests` boundary lock added by #1805 | HIGH | Do not implement the spec as written. Verify-and-close instead. |
| Silent semantic change if anyone "fixes" `M1_A` → `M1` based on the brief's suggested code | HIGH | Treat slice selection as load-bearing; do not change without an explicit, separately-owned decision. The brief's code snippet is illustrative, not authoritative. |
| Folding the `SalesHistory` projection into the helper without a regression test could re-introduce drift later | MEDIUM | If delta (1) is taken, add the `Where`-bounds assertion to `CatalogAnalyticsSourceAdapterTests` in the same PR. |
| Spec status was `COMPLETE` even though premise was stale — same drift could happen on other open specs filed before #1805 landed | LOW | When picking up a backlog spec, first run `gh` and `grep` against the proposed identifiers; if the target code has moved, re-validate before planning. |

## Specification Amendments

The spec needs the following before any code is written:

1. **Replace the "Background" section** with the actual post-#1805 state of the code: mapping lives in `CatalogAnalyticsSourceAdapter.MapToAnalyticsProduct`, both call sites already route through it, both already pre-filter `SalesHistory`. Remove the file-path reference to `Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` (the file is now under `Persistence/Features/Analytics/` and contains no mapping).
2. **Strike FR-1, FR-2, FR-3, FR-4** — already delivered by #1805.
3. **Strike FR-5** — `CatalogAnalyticsSourceAdapterTests` already covers this surface. If specific cases listed in FR-5 are missing, file them as a small test-gap ticket against the adapter, not as a refactor.
4. **Strike "Dependencies → Issue #1805"** — #1805 is no longer downstream of this spec; it is upstream and already done.
5. **Status should change from `COMPLETE` to `OBSOLETE` (superseded by #1805).** If the team wants the optional `SalesHistory`-projection consolidation, file it as a new, scoped ticket against `CatalogAnalyticsSourceAdapter` with one FR and one test.

## Prerequisites

None. Nothing infrastructural is needed because the architectural work is already done. Before closing the issue, the implementer should:

- Run `dotnet build` and `dotnet test` against `CatalogAnalyticsSourceAdapterTests` to confirm green on the current branch.
- Run `dotnet test` against `ModuleBoundariesTests` to confirm the `Analytics → Catalog` boundary rule is active and passing.
- Verify by `gh search` / repo grep that no `MapToAnalyticsProduct` duplicate has reappeared elsewhere (e.g. in a handler) since #1805 landed.