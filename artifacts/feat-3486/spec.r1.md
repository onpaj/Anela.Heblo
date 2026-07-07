# Specification: Fix dead `TopProductCount` parameter in GetProductMarginSummary

## Summary
`GetProductMarginSummaryRequest.TopProductCount` is a documented API parameter (default `15`) that the handler never reads, so it has zero effect on the response — a contract violation flagged by the arch-review routine. The frontend's only caller already works around this by hard-coding `topProductCount: 0`, because it needs the *full* unfiltered group list to build its own "top 15 + Other" chart bucketing and a complete results table. This spec recommends **removing the parameter** (Option 1) rather than implementing server-side truncation (Option 2), because truncation would work against the frontend's actual, current data needs.

## Background
`GetProductMarginSummary` used to be a simple top-N report: the original design (`docs/features/product-margin-summary.md`) had the handler itself select the top N products by margin and build monthly segments only for those, with everything else implicitly excluded.

The feature was since refactored for performance (see `GetProductMarginSummaryHandler`, header comment "🔒 PERFORMANCE FIX: Refactored handler using streaming architecture"). The current handler streams all products, computes margin data for **every** group (`GenerateTopProducts`, lines 69–117), and returns the complete list in `TopProducts` — `TopProductCount` is left over from the old design and is never referenced in `Handle` or `GenerateTopProducts`.

The frontend evolved alongside this: `frontend/src/components/pages/ProductMarginSummary.tsx` now does its own client-side top-N logic:
- It sorts `data.topProducts` and takes the top 15 for the stacked chart (`TOP_CHART_PRODUCTS = 15`), bucketing everything else into an "Ostatní produkty" ("Other products") series (lines 72–131).
- It renders a full data table from **all** entries in `data.topProducts`, not just the top 15 (lines 169–200+).
- It displays a total group count, "Celkem skupin" ("Total groups"), driven by `data.topProducts.length` (line 462).

To make all three behaviors work, the frontend genuinely needs the complete, untruncated group list — which is exactly what `useProductMarginSummary.ts` requests by passing `topProductCount: 0` with the comment `// topProductCount = 0 means no limit` (line 27). This is not an accidental workaround for a bug; it reflects an actual, current product requirement: the UI wants everything and does its own top-N/aggregation client-side.

## Functional Requirements

### FR-1: Remove the dead `TopProductCount` parameter (recommended fix)
Remove `TopProductCount` from `GetProductMarginSummaryRequest` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs:9`). No handler behavior changes, since the value was never consumed.

**Acceptance criteria:**
- `GetProductMarginSummaryRequest` no longer declares a `TopProductCount` property.
- `GetProductMarginSummaryHandler` compiles and behaves identically (it never referenced the property, so no logic changes are needed there).
- The OpenAPI spec / generated TypeScript client (`frontend/src/api/generated/api-client.ts`) is regenerated and no longer exposes `topProductCount` as a parameter of `analytics_GetProductMarginSummary`.
- `useProductMarginSummary.ts` (line 25–32) is updated to call `analytics_GetProductMarginSummary` without the `topProductCount` argument, removing the `0, // topProductCount = 0 means no limit` line and the now-obsolete comment.
- Any existing backend tests referencing `TopProductCount` are updated or removed; no test asserts a truncation behavior that no longer exists (there currently is none — grep found no test coverage for this field).
- Full response payload (`TopProducts`, chart data, table, total group count) is unchanged for the one existing caller (`ProductMarginSummary.tsx`) after the change — verified by running the existing component test suite (`frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx`) and manual smoke-check of the Analytics page.

### FR-2: (Alternative, not recommended) Implement the limit server-side
For completeness, if the architect prefers Option 2 instead: implement truncation in `GenerateTopProducts` by taking `request.TopProductCount` items after sorting (treating `<= 0` as "no limit", per the brief's suggested `sortedProducts.Take(request.TopProductCount > 0 ? request.TopProductCount : int.MaxValue).ToList()`), and update `useProductMarginSummary.ts` to pass a meaningful positive value (or continue passing `0` for "no limit", which would leave today's dead-code smell mostly resolved but the parameter effectively unused by the only caller).

**Acceptance criteria (only if this option is chosen instead of FR-1):**
- `GenerateTopProducts` truncates the sorted group list to `request.TopProductCount` when it is a positive integer, and returns all groups when it is `0` or negative.
- `Rank` values (assigned after sorting, lines 111–114) are computed on the truncated list, i.e., `Rank` still starts at 1 and is contiguous.
- Because the frontend chart (top-15 + "Other" bucket) and table (all groups) both depend on receiving the complete group list today, the frontend must **not** switch to passing a small `topProductCount` (e.g. `15`) without also reworking the "Other" aggregation and the "Celkem skupin" total-group-count display, since both currently assume they receive every group. If this option is chosen, that frontend rework is in scope and must be specified before implementation — it is not a drop-in one-line change.

### FR-3: Keep OpenAPI contract and generated client in sync
Whichever option is chosen, regenerate the backend-driven OpenAPI spec and the frontend TypeScript client (per `docs/development/api-client-generation.md`) as part of the same change, so the public contract accurately reflects the implementation.

**Acceptance criteria:**
- No parameter appears in the generated client that has no effect on the server (Option 1), or every parameter that appears has real effect and is exercised by at least one caller with a non-default value (Option 2).

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. Option 1 is a no-op change on the hot path (removes an unused field). Option 2, if chosen, must not change algorithmic complexity — truncation is an O(1) `Take` after the existing `O(n log n)` sort in `ApplySorting`, so no meaningful performance impact either way. This fix is not motivated by a performance problem; the streaming refactor already addressed memory/perf concerns for large group counts.

### NFR-2: Security
No security-relevant surface. This is an internal analytics query with no PII sensitivity beyond what's already returned by the endpoint today.

### NFR-3: Backward compatibility
This is an internal API consumed by exactly one known caller in this codebase (`useProductMarginSummaryQuery`). No external/public consumers were found (grep across the repo found only the generated client, the hook, and documentation references). Removing the parameter (Option 1) is therefore a safe, low-risk breaking change to the OpenAPI contract, contained entirely within this repository and released in one atomic change (backend + regenerated client + frontend caller).

## Data Model
No data model changes. Affected types:
- `GetProductMarginSummaryRequest` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs`): loses the `TopProductCount` property under Option 1; unchanged under Option 2.
- `GetProductMarginSummaryResponse` and `TopProductDto`: unaffected by either option — the response always carries `TopProducts: List<TopProductDto>` (fields: `GroupKey`, `DisplayName`, `TotalMargin`, `ColorCode`, `M0Amount`/`M1Amount`/`M2Amount`, `M0Percentage`/`M1Percentage`/`M2Percentage`, `SellingPrice`, `PurchasePrice`, `Rank`).

## API / Interface Design
Endpoint: `GET /analytics/product-margin-summary` (via `analytics_GetProductMarginSummary` in the generated client), backed by `GetProductMarginSummaryRequest` → `GetProductMarginSummaryHandler` → `GetProductMarginSummaryResponse`.

Under the recommended Option 1, the query parameters become:
- `timeWindow: string`
- `groupingMode: ProductGroupingMode`
- `marginLevel: MarginLevel`
- `sortBy?: string`
- `sortDescending: bool`

(`topProductCount` removed.) `useProductMarginSummaryQuery` (`frontend/src/api/hooks/useProductMarginSummary.ts`) calls this with one fewer positional argument; downstream consumption in `ProductMarginSummary.tsx` (chart top-15/"Other" split, full table, total group count) is unaffected because it already relies on receiving the complete group list.

## Dependencies
- OpenAPI client regeneration tooling (`docs/development/api-client-generation.md`) must be run after the backend change so `frontend/src/api/generated/api-client.ts` stays in sync.
- No external services or new libraries required.

## Out of Scope
- Any change to the actual margin calculation logic in `GetProductMarginSummaryHandler`, `IMarginCalculator`, or `IMonthlyBreakdownGenerator`.
- Adding pagination or a genuine server-side "top N with real limit" feature for the results table (would be a separate, larger UX change to `ProductMarginSummary.tsx`, since the table and "Celkem skupin" count currently assume the full list).
- Reworking the frontend's chart "top 15 + Other" bucketing logic.
- Updating `docs/features/product-margin-summary.md`, which still describes the old (pre-refactor) top-N design — left as-is unless the architect wants documentation cleanup bundled in.

## Open Questions
- **Confirm the recommended direction with the architect:** this spec recommends **Option 1 (remove `TopProductCount`)** because the only real caller (`ProductMarginSummary.tsx`) needs the full, untruncated group list for its client-side "top 15 + Other" chart bucketing and its full results table with a total-group count — implementing server-side truncation (Option 2) would work against that existing behavior rather than serve it, and would require additional frontend rework (not specified here) to remain useful. Please confirm Option 1 is acceptable before implementation proceeds, or direct that Option 2 be pursued along with the additional frontend rework it would require.

## Status: HAS_QUESTIONS
