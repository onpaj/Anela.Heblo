# Plan — GetProductMarginsRequest.DateFrom/DateTo accepted but silently ignored

## Summary

`GetProductMarginsRequest.DateFrom`/`DateTo` are bound from the query string, exposed through the generated TypeScript client, and threaded through `useProductMarginsQuery`, but `GetProductMarginsHandler.Handle` never reads either value — it filters `MonthlyHistory` with a hardcoded 13-month window and takes `M0`/`M1`/`M2` from pre-computed, unfiltered `CatalogAggregate.Margins.Averages`. This re-confirms the plan/design/architecture work already done on this task: remove the two dead parameters (request DTO, generated client, hook) rather than implement them, matching the precedent already set by #3486 and #3487 in the same module.

## Context

This is a re-verification pass over the same finding; `plan-01.md`, `design-01.md`, and `architecture-01.md` already exist for this task and `architecture-01.md` recorded a clean "approved as designed" verdict with no changes requested. Re-checked the live code in this step rather than trusting the prior artifacts' quotes, since artifacts can drift from the tree between steps:

- `GetProductMarginsRequest.cs` — confirmed as of now: `DateTime? DateFrom` / `DateTime? DateTo` are still present, still the only two properties beyond the six kept ones, class (not record) — consistent with the mandatory DTO convention in the root `CLAUDE.md`.
- `GetProductMarginsHandler.cs` — grepped for `DateFrom`/`DateTo`: zero matches, confirming the parameters are still fully dead code, not partially wired.
- `useProductMargins.ts` — confirmed current signature still takes `dateFrom`/`dateTo` as trailing optional parameters (positions 8–9), still folded into the React Query `queryKey`, still passed positionally into `apiClient.productMargins_GetProductMargins(...)`.
- No code change has landed between the prior steps and now — the codebase state matches every claim in `plan-01.md`/`design-01.md`/`architecture-01.md` exactly. Nothing here contradicts or requires revising those decisions.

## Decision: remove, not implement (reaffirmed)

Unchanged from `plan-01.md`, and unchallenged by `architecture-01.md`'s review:

1. **No consumer needs it.** `ProductMarginsList.tsx` is the sole caller of the hook and has never passed `dateFrom`/`dateTo` — there is no date-range picker UI anywhere on that page.
2. **Directly analogous precedent.** `#3486` (`TopProductCount` on `GetProductMarginSummary`) and `#3487` (`IncludeDetailedBreakdown` on `GetMarginReport`) both chose removal for the identical defect shape — a documented, request-bound parameter the handler never reads. Consistency across the module favors removal again.
3. **Smaller, safer change.** Removal is a pure deletion with zero new behavior, zero new semantics to design (no need to decide what "only `DateFrom` set" means, whether it should refilter `Averages` vs. only `MonthlyHistory`, or how it interacts with the hardcoded 13-month window) and zero new test surface.

## Functional requirements

- **FR-1**: `GetProductMarginsRequest` no longer declares `DateFrom`/`DateTo`.
  - Acceptance: properties are removed from `GetProductMarginsRequest.cs`; `dotnet build` succeeds.
- **FR-2**: `GetProductMarginsHandler` behavior is unchanged for every currently-exercised path.
  - Acceptance: `GetProductMarginsHandlerTests.cs` passes unmodified — verified all five request constructions in that suite use empty-constructor/object-initializer syntax that never touches `DateFrom`/`DateTo`, so this is a genuine zero-behavior-change deletion.
- **FR-3**: `useProductMarginsQuery` no longer accepts or forwards `dateFrom`/`dateTo`.
  - Acceptance: the hook's signature drops both trailing parameters and the corresponding `queryKey` entries and call-site arguments; `ProductMarginsList.tsx:48-56`'s existing 7-argument call remains source-compatible with no edit needed there (confirmed: it never passes the 8th/9th arg today).
- **FR-4**: The OpenAPI-generated TypeScript client is regenerated, not hand-edited, so `productMargins_GetProductMargins(...)` drops the trailing `dateFrom`/`dateTo` parameters and their query-string-building blocks.
  - Acceptance: regenerated via the project's standard build-triggered generation step (`docs/development/api-client-generation.md`); diff scoped to the `ProductMargins` method only.
- **FR-5**: No other caller of `useProductMarginsQuery`/`productMargins_GetProductMargins` breaks.
  - Acceptance: repo-wide grep confirms exactly four hits — the hook itself, the generated client, `ProductMarginsList.tsx`, and its test — and the test mocks the hook's *return value*, not call arity, so it is unaffected by the signature change.

## Non-functional requirements

- No behavior change for any existing, exercised code path — this is contract cleanup, not a feature change; regressions must be caught by the existing test suite since there is no automated deployment gate beyond CI in this repo.
- Surgical scope: do not touch the hardcoded 13-month window, the `RefreshMarginData` background computation, or any sorting/filtering logic unrelated to the two removed fields.

## Data model

- `GetProductMarginsRequest` (Application-layer MediatR request DTO, class per project convention): remove `DateTime? DateFrom` and `DateTime? DateTo`. No other entity is touched.
- `CatalogAggregate.Margins` (`MonthlyMarginHistory`) and its background-computed fixed window are unaffected in scope and behavior — this data model is structurally incompatible with a request-time range without a materially larger redesign, which remains explicitly declined as unrequested (see Dependencies and scope).

## Interfaces

- `GET /api/ProductMargins` — query string drops `DateFrom`/`DateTo` (previously accepted but never honored); `ProductCode`, `ProductName`, `ProductType`, `PageNumber`, `PageSize`, `SortBy`, `SortDescending` unchanged.
- Generated TS client `productMargins_GetProductMargins(...)` loses its trailing `dateFrom`, `dateTo` positional parameters.
- `useProductMarginsQuery(...)` hook loses the same two trailing parameters; its one call site needs no edit.

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsRequest.cs`
- `frontend/src/api/hooks/useProductMargins.ts`
- `frontend/src/api/generated/api-client.ts` (regenerated, not hand-edited)

**Explicitly out of scope:**
- The hardcoded 13-month window in `GetProductMarginsHandler.MapToMarginDto` — untouched.
- The background `RefreshMarginData` task and its fixed computation window in `CatalogModule.cs` — untouched.
- Adding a working date-range filter UI to `ProductMarginsList.tsx` — not requested; no existing date-range control on that page.
- `GetProductMarginSummary`/`GetMarginReport` — already fixed under #3486/#3487; no further action.

## Rough plan

1. Remove `DateFrom`/`DateTo` from `GetProductMarginsRequest.cs`.
2. Regenerate the OpenAPI TypeScript client (standard build-triggered step) so `api-client.ts` reflects the trimmed backend contract — do this before touching the hook, to avoid a transient type mismatch against the stale generated client.
3. Remove the two trailing parameters from `useProductMarginsQuery` in `useProductMargins.ts`, drop them from the `queryKey` array, and drop the corresponding arguments from the `apiClient.productMargins_GetProductMargins(...)` call.
4. Run `dotnet build` + `dotnet format` (backend) and `npm run build` + `npm run lint` (frontend).
5. Run `GetProductMarginsHandlerTests.cs` and any frontend tests touching `ProductMarginsList`/`useProductMargins` — expect no test-file changes needed since none reference the removed properties, and `ProductMarginsList.test.tsx` mocks the hook's return value rather than asserting call arity.
6. Re-grep the frontend for stray `dateFrom`/`dateTo` references tied to `productMargins_GetProductMargins`/`useProductMarginsQuery` post-edit to confirm nothing was missed.

## Open questions

- **Removal vs. implementation** — resolved as "remove," reaffirmed across three prior pipeline steps (plan, design, architecture review) with no dissent. If a later reviewer wants a working date-range filter instead (technically feasible by filtering `MonthlyMarginHistory`'s per-month data against `DateFrom`/`DateTo` before computing `Averages`), that is a materially larger change — new filtering semantics, new test coverage, and a UI date picker to actually exercise it — and should be raised explicitly rather than silently reversing this decision during implementation.
