# Architecture Review: Fix dead `TopProductCount` parameter in GetProductMarginSummary

## Skip Design: true

## Architectural Fit Assessment

This is a narrow, internal API-surface cleanup inside an existing vertical slice (`Analytics/UseCases/GetProductMarginSummary`). It touches one MediatR request DTO, one handler (read-only, no logic change needed), one frontend hook, and the generated OpenAPI client. There is no new component, no new module boundary, and no UI change — the frontend already renders from the full `topProducts` list today, so removing a parameter nobody reads changes nothing observable.

I independently verified the facts the spec relies on:
- `GetProductMarginSummaryRequest.TopProductCount` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs:9`) exists but `GetProductMarginSummaryHandler.Handle` (same folder) never reads `request.TopProductCount` — `GenerateTopProducts` builds `topProductsWithData` from `calculationResult.GroupTotals` with no `Take`/limit anywhere in its body.
- The controller binds the whole request via `[FromQuery] GetProductMarginSummaryRequest request` (`backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs:32`), so removing a property is a pure DTO change — no controller signature edit needed.
- `useProductMarginSummary.ts:27` calls the generated client with a hardcoded `0, // topProductCount = 0 means no limit` positional argument.
- `ProductMarginSummary.tsx` (lines ~66-167) does its own client-side top-N bucketing: it sorts `data.topProducts` by `totalMargin`, takes `TOP_CHART_PRODUCTS = 15` for the chart series, and buckets everything else into an "Ostatní produkty" (Other) series computed from the full remainder of the list. The table (`tableData`, lines ~170+) is built from every entry in `data.topProducts`, not a truncated subset.
- No backend test references `TopProductCount` (`backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — zero matches).
- `docs/architecture/development_guidelines.md` / `docs/development/api-client-generation.md` mandate DTOs-as-classes (already satisfied — `GetProductMarginSummaryRequest` is a class) and require the OpenAPI client to be regenerated whenever a request/response contract changes.

Given this, the parameter is unambiguously dead on the server, and the one real caller structurally depends on receiving the *complete* group list to do its own top-15/Other split and full-table rendering. There is no plan, ticket, or code path anywhere in this repo that wants a truncated response.

## Proposed Architecture

### Component Overview

No new components. Existing flow, unchanged shape, one property removed from the contract:

```
ProductMarginSummary.tsx
        │ (reads data.topProducts — full list; does its own top-15/Other split)
        ▼
useProductMarginSummaryQuery (useProductMarginSummary.ts)
        │ calls analytics_GetProductMarginSummary(timeWindow, groupingMode, marginLevel, sortBy, sortDescending)
        ▼
AnalyticsController.GetProductMarginSummary([FromQuery] GetProductMarginSummaryRequest)
        │
        ▼
GetProductMarginSummaryHandler.Handle
        │ streams products → IMarginCalculator → GenerateTopProducts (no limit, returns all groups)
        ▼
GetProductMarginSummaryResponse { TopProducts: List<TopProductDto> (full), MonthlyData, TotalMargin, ... }
```

### Key Design Decisions

#### Decision 1: Remove `TopProductCount` vs. implement server-side truncation

**Options considered:**
- **Option 1 — Remove the parameter.** Delete `TopProductCount` from `GetProductMarginSummaryRequest`; handler is already unaffected. Update the frontend call site to drop the argument. Regenerate the OpenAPI client.
- **Option 2 — Implement the limit.** Add `.Take(request.TopProductCount > 0 ? request.TopProductCount : int.MaxValue)` in `GenerateTopProducts` after sorting, and have the frontend pass a real value.

**Chosen approach: Option 1 — remove the parameter.** This is the final, binding decision for this change.

**Rationale:**
1. **The only consumer needs the untruncated list, structurally, not incidentally.** `ProductMarginSummary.tsx` computes its own top-15 + "Other" chart bucket and renders a full data table plus a total-group count (`data.topProducts.length`) from the complete set. If the backend truncated to N, the "Other" bucket and the total-group count would silently become wrong or would require a second, unbounded endpoint call anyway — defeating the purpose of truncating in the first place. Implementing Option 2 today would produce a parameter that is technically "wired up" but that no caller could safely use above `0` (no-limit) without a separate frontend rework that is out of scope and unspecified.
2. **YAGNI.** There is no present requirement, ticket, or planned feature that needs the server to truncate this list. Speculative truncation is exactly the kind of unused-but-plausible-looking parameter that created this bug report in the first place (the arch-review routine flagged it precisely because a documented-but-inert parameter is worse than no parameter).
3. **Lower risk, smaller diff.** Option 1 is a subtractive, behavior-preserving change: no handler logic changes, no new sorting/ranking edge cases (Option 2 would need to re-verify that `Rank` stays contiguous 1..N after truncation, per the spec's own FR-2 caveat). Option 1 has nothing new to get wrong.
4. **Contract honesty.** The project's dev guidelines already require DTOs to accurately reflect behavior (classes-not-records DTOs, `[Required]`/validation attributes, etc.) — a parameter that is accepted, documented with a default, and silently ignored is itself a violation of that spirit. Removing it directly resolves the arch-review finding; keeping-but-implementing it defers the real fix to a future, currently-unplanned frontend change.
5. **Reversible later.** If a genuine need for server-side pagination/top-N emerges (e.g., the results table grows large enough to need real pagination — explicitly called out as future, out-of-scope work in the spec), that should be designed as its own feature with its own contract (e.g., a proper `PageSize`/`Take` combined with a documented "does this affect the chart Other-bucket and total count" answer) rather than resurrecting a parameter whose semantics were never fully thought through.

No further clarification round-trip exists after this review — Option 1 is final.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Edit in place, within the existing `Analytics` vertical slice:

- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs`
- `frontend/src/api/hooks/useProductMarginSummary.ts`
- `frontend/src/api/generated/api-client.ts` (regenerated, not hand-edited)
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` (only if it constructs `GetProductMarginSummaryRequest` with `TopProductCount` set anywhere — grep found none, but re-check at implementation time in case object initializers set it without using the property name in a way grep missed, e.g. via reflection/AutoFixture)

### Interfaces and Contracts

**Backend — `GetProductMarginSummaryRequest.cs`:** delete the line:
```csharp
public int TopProductCount { get; set; } = 15; // Configurable, default 15
```
Leave every other property (`TimeWindow`, `GroupingMode`, `MarginLevel`, `SortBy`, `SortDescending`) untouched. `GetProductMarginSummaryHandler.Handle` requires **no changes** — it never referenced `request.TopProductCount`. `GetProductMarginSummaryResponse` and `TopProductDto` are unaffected.

**Controller:** `AnalyticsController.GetProductMarginSummary([FromQuery] GetProductMarginSummaryRequest request)` binds the whole DTO — no signature change needed; the query string simply gains one fewer recognized parameter (`TopProductCount`/`topProductCount`).

**OpenAPI client regeneration is required** per `docs/development/api-client-generation.md` (any request/response contract change must regenerate both clients). Run:
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```
(or let the Debug-mode PostBuild / frontend `prebuild` script do it automatically). After regeneration, `frontend/src/api/generated/api-client.ts`'s `analytics_GetProductMarginSummary` method signature loses its `topProductCount` positional parameter — this is a generated file; do not hand-edit it, just regenerate and commit the diff.

**Frontend — `useProductMarginSummary.ts`:** update the call to match the new (regenerated) positional signature by removing the `0, // topProductCount = 0 means no limit` argument:
```typescript
return apiClient.analytics_GetProductMarginSummary(
  timeWindow,
  groupingMode,
  marginLevel,
  sortBy,
  true // sortDescending = true
);
```
No other frontend file needs to change — `ProductMarginSummary.tsx`'s chart bucketing, table rendering, and "Celkem skupin" total-group count already consume the full `topProducts` array and require no rework, since the response shape (`TopProducts: List<TopProductDto>`) does not change at all, only the request's query parameters shrink by one.

### Data Flow

Unchanged. Request → `TimeWindowParser` → streamed product margin calculation → `GenerateTopProducts` (all groups, ranked) → `GetProductMarginSummaryResponse.TopProducts` (full list) → frontend hook → `ProductMarginSummary.tsx` (client-side top-15 + Other bucketing for the chart; full list for the table and group count). The only thing removed from this flow is an inert query parameter that never influenced any step.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale/uncommitted generated client leaves `topProductCount` in `api-client.ts` after backend change, causing a TS compile mismatch or a silently-passed dead arg | Low | Regenerate via `dotnet msbuild ... -t:GenerateFrontendClientManual` (or `npm run generate-client`) as part of the same commit; `npm run build`'s prebuild step will also catch drift |
| Hidden caller of `TopProductCount` elsewhere in the codebase (e.g. a script, Postman collection, or forgotten test) breaks after removal | Low | Grep across the full repo found only: the property declaration itself, the frontend hook's `0` argument, the generated client, and the old pre-refactor doc (`docs/features/product-margin-summary.md`, historical/inactive). No test or other caller references it. |
| Future request to add real server-side pagination/top-N reintroduces the same "documented but unused" trap if rushed | Low | When that need arises, design it as a dedicated change that also updates `ProductMarginSummary.tsx`'s chart Other-bucket and total-group-count logic in the same PR — do not add a parameter without a consumer exercising it with a non-default value, per FR-3's acceptance criterion |

## Specification Amendments

- The spec's **Open Question** is resolved: **Option 1 is chosen, final, and binding.** FR-2 (the "alternative" implement-the-limit path) should be treated as rejected/out of scope for this change, not merely "not recommended." Remove or mark FR-2 as rejected in the spec status so implementers don't accidentally build it.
- FR-1's acceptance criteria are confirmed accurate as written and require no changes.
- Clarify in FR-1 (or FR-3) explicitly that **no controller code changes** are needed beyond the DTO edit, since `[FromQuery]` binds the whole request object — this review confirms that explicitly so implementers don't go looking for a controller parameter list to edit.
- Add to FR-1's acceptance criteria: after regenerating `frontend/src/api/generated/api-client.ts`, update the positional call in `useProductMarginSummary.ts` to the new 5-argument signature (`timeWindow, groupingMode, marginLevel, sortBy, sortDescending`) — the exact ordering must be re-checked against whatever NSwag actually emits (positional args are ordered by property declaration order in the request class), not assumed.

## Prerequisites

- None. No migrations, no config, no new infrastructure. This can be implemented immediately: edit the DTO, regenerate both OpenAPI clients (backend C# client and frontend TypeScript client), update the one frontend call site, run `dotnet build` + `dotnet format` and `npm run build` + `npm run lint` per the repo's standard validation gate, and re-run `ProductMarginSummary.test.tsx` plus a manual smoke check of the Analytics page.
