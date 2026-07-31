# Plan — GetProductMarginsRequest.DateFrom/DateTo accepted but silently ignored

## Summary

`GetProductMarginsRequest.DateFrom`/`DateTo` are bound from the query string, forwarded by the OpenAPI-generated TS client, and threaded through `useProductMarginsQuery`, but `GetProductMarginsHandler.Handle` never reads them — the handler instead applies a hardcoded 13-month window when slicing `MonthlyHistory`, and the `M0`/`M1`/`M2` averages are taken as-is from the pre-computed `CatalogAggregate.Margins.Averages` (itself built by a background refresh task over its own fixed ~2-year window, capped at 2025-01-01). This is a dead/misleading parameter in the public API contract. The fix removes the parameters rather than implementing them, matching the two prior, directly analogous fixes in this module.

## Context

Verified directly in code during this planning step (no prior pipeline artifacts existed for this task):

- `GetProductMarginsRequest.cs:16-17` declares `DateFrom`/`DateTo` as `DateTime?`.
- `ProductMarginsController.cs` binds the full request `[FromQuery]`, so both are live, public API surface, and are present in the generated `api-client.ts` (`productMargins_GetProductMargins(..., dateFrom, dateTo)`).
- `useProductMargins.ts:20-21,56-57` accepts `dateFrom`/`dateTo` params and passes them to every call.
- **However**, the only current caller of the hook, `ProductMarginsList.tsx:48-56`, does **not** pass `dateFrom`/`dateTo` at all — there is no date-range UI control anywhere in this page. So in practice the parameters are always `undefined`/`null` today; the hook's plumbing is unused dead weight on top of an already-dead backend parameter.
- `GetProductMarginsHandler.Handle` never references `request.DateFrom`/`request.DateTo`. `MapToMarginDto` (handler:190-194) filters `MonthlyHistory` using a hardcoded `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`, and the `M0`/`M1`/`M2` top-level averages use `product.Margins.Averages` unfiltered by any date range.
- The upstream data source (`CatalogModule.cs`, `RefreshMarginData` task) computes `product.Margins` once per refresh cycle over a fixed window (`max(2 years ago, 2025-01-01)` to `last month`) — independent of any request.
- Existing backend test `GetProductMarginsHandlerTests.cs:31-37` asserts the hardcoded 13-month boundary behavior; it does not exercise `request.DateFrom/DateTo` (because nothing does).
- Directly analogous precedent already merged in this codebase, deciding "remove" over "implement" both times:
  - `99dd69e5` — `#3486: remove dead TopProductCount parameter from GetProductMarginSummary`
  - `68206106` — `#3487: Remove dead IncludeDetailedBreakdown flag from GetMarginReport`

## Decision: remove, not implement

Choosing removal over honoring the range, for these reasons:
1. **No consumer needs it.** The only UI caller never sets these values — there is no date-range picker feature waiting on this. Implementing it would be speculative functionality with no current user.
2. **Directly analogous precedent** in the same module chose removal twice (#3486, #3487) for the same shape of defect (documented, unread request parameter). Consistency of decision-making across the module matters more here than the (real but unrequested) feasibility of honoring the range via `MonthlyData` filtering.
3. **Smaller, safer change.** Removal is a pure deletion with no new behavior to design, validate, or add test coverage for. Implementing would require deciding semantics (does a custom range affect only `MonthlyHistory`, or also recompute `Averages`? what's the fallback when only one bound is given? does it interact with the hardcoded 13-month default?) — none of which any stakeholder has asked for.

If the user disagrees and wants the range honored instead, flag this decision in review before the design/architecture steps proceed on the removal path (see Open Questions).

## Functional requirements

- **FR-1**: `GetProductMarginsRequest` no longer declares `DateFrom`/`DateTo` properties.
  - Acceptance: `GetProductMarginsRequest.cs` has no `DateFrom`/`DateTo` members; `dotnet build` succeeds.
- **FR-2**: `GetProductMarginsHandler` behavior is unchanged for all currently-exercised paths (no behavior change since the properties were never read).
  - Acceptance: `GetProductMarginsHandlerTests.cs` passes unmodified (the hardcoded 13-month window logic is untouched).
- **FR-3**: The frontend `useProductMarginsQuery` hook no longer accepts or forwards `dateFrom`/`dateTo` parameters.
  - Acceptance: `useProductMargins.ts` signature drops both parameters; the call to `apiClient.productMargins_GetProductMargins(...)` drops the corresponding positional arguments, matching the regenerated client signature.
- **FR-4**: The OpenAPI-generated TypeScript client (`frontend/src/api/generated/api-client.ts`) is regenerated so `productMargins_GetProductMargins` no longer has `dateFrom`/`dateTo` parameters, keeping it in sync with the backend contract.
  - Acceptance: regeneration is run via the project's standard client-generation step (build-triggered per `docs/development/api-client-generation.md`), diff is limited to the `ProductMargins` method signature/body (mirrors the shape of the `99dd69e5` diff).
- **FR-5**: No other caller of `useProductMarginsQuery` or `productMargins_GetProductMargins` is broken.
  - Acceptance: repo-wide grep for both symbols confirms `ProductMarginsList.tsx` is the only call site (already verified during planning) and it does not pass `dateFrom`/`dateTo` today, so no call-site edit beyond the hook's own body is required there.

## Non-functional requirements

- No behavior change for any existing, exercised code path (this is a contract-cleanup fix, not a feature change) — matches project fact that this repo has no automated deployment gate beyond CI, so regressions must be caught by existing tests.
- Keep the change surgical: do not touch the hardcoded-13-month logic, the sorting/filtering of other fields, or unrelated parts of the handler/DTOs.

## Data model

- `GetProductMarginsRequest` (Application layer DTO, class not record per project convention): remove `DateTime? DateFrom` and `DateTime? DateTo` properties. No other entity/table/aggregate is touched — `CatalogAggregate.Margins` (a `MonthlyMarginHistory`) and its background-computed window are unaffected in both scope and behavior.

## Interfaces

- `GET /api/ProductMargins` — query string no longer accepts (and previously never honored) `DateFrom`/`DateTo`; all other query parameters (`ProductCode`, `ProductName`, `ProductType`, `PageNumber`, `PageSize`, `SortBy`, `SortDescending`) unchanged.
- Generated TS client method `productMargins_GetProductMargins(...)` loses its trailing `dateFrom`, `dateTo` positional parameters.
- `useProductMarginsQuery(...)` hook loses its trailing `dateFrom`, `dateTo` parameters; its existing call site (`ProductMarginsList.tsx`) requires no change since it never passed them.

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsRequest.cs`
- `frontend/src/api/hooks/useProductMargins.ts`
- `frontend/src/api/generated/api-client.ts` (regenerated, not hand-edited)

**Explicitly out of scope:**
- The hardcoded 13-month window logic in `GetProductMarginsHandler.MapToMarginDto` — untouched; this task is about removing an unread parameter, not changing existing date-window behavior.
- The background `RefreshMarginData` task and its fixed computation window in `CatalogModule.cs` — untouched.
- Adding a working date-range filter UI/feature to `ProductMarginsList.tsx` — no such feature was requested; the page has never exposed date-range controls.
- `GetProductMarginSummary` / `GetMarginReport` (already fixed under #3486/#3487) — different endpoints, no further action needed there.

## Rough plan

1. Remove `DateFrom`/`DateTo` from `GetProductMarginsRequest.cs`.
2. Remove the two trailing parameters from `useProductMarginsQuery` in `useProductMargins.ts` and drop the corresponding arguments from the `apiClient.productMargins_GetProductMargins(...)` call.
3. Regenerate the OpenAPI TypeScript client (standard build-triggered generation step) so `api-client.ts` reflects the trimmed backend contract.
4. Run `dotnet build` + `dotnet format` (backend) and `npm run build` + `npm run lint` (frontend) per repo validation rules.
5. Run the existing `GetProductMarginsHandlerTests.cs` suite and any frontend tests touching `ProductMarginsList`/`useProductMargins` to confirm no regression (expect no changes needed to test files themselves, since none reference the removed request properties).
6. Grep the frontend for any other reference to `dateFrom`/`dateTo` tied to `productMargins_GetProductMargins`/`useProductMarginsQuery` to make sure nothing else breaks (already checked in this planning step: none found).

## Open questions

- **Removal vs. implementation** — resolved in this plan as "remove," on the strength of the #3486/#3487 precedent and the absence of any current UI need. If a design/architecture reviewer disagrees and prefers implementing a working date-range filter (e.g., recomputing `Averages` from a `DateFrom`/`DateTo`-filtered slice of `MonthlyData`, which is technically feasible since `MonthlyMarginHistory` already stores per-month granularity), that would be a materially larger change (new filtering semantics, new test coverage, and a UI date picker to actually exercise it) and should be raised explicitly before the design step proceeds, rather than silently reversing this decision downstream.
