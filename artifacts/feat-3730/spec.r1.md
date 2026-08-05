# Specification: Migrate `useManufacturingStockAnalysis` to the generated OpenAPI client

## Summary
`frontend/src/api/hooks/useManufacturingStockAnalysis.ts` and the export handler in `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` hand-roll TypeScript types and raw `fetch` calls against `/api/manufacturing-stock-analysis` instead of using the auto-generated OpenAPI client. Investigation for this spec found that a fully typed generated method (`apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)`) and matching request/response types **already exist** in `frontend/src/api/generated/api-client.ts` — the backend requires no changes. This is a pure frontend refactor: replace the hand-coded types and manual `fetch`/URL-building with the generated client, in both the query hook and the export flow that duplicates the same anti-pattern.

## Background
The project generates a TypeScript client from the backend's OpenAPI spec on every build specifically to keep frontend and backend types in sync. `useManufacturingStockAnalysis.ts` instead declares six local types (`GetManufacturingStockAnalysisRequest`, `ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto`, `ManufacturingStockSummaryDto`, `GetManufacturingStockAnalysisResponse`) and calls `(apiClient as any).http.fetch(...)` directly, finishing with an unchecked `as Promise<GetManufacturingStockAnalysisResponse>` cast. Every other Manufacture hook (`useManufactureBatch.ts`, `useManufactureOrders.ts`, `useManufactureSettings.ts`) imports types from `../generated/api-client` and calls the generated, fully-typed client method.

**Root cause correction vs. the original issue brief.** The brief speculated the generated method was missing because of an omitted `[ProducesResponseType]`/`[HttpGet]` attribute on the backend controller. Direct inspection of the repo shows this is not the case:
- `ManufacturingStockAnalysisController.GetStockAnalysis` (`backend/src/Anela.Heblo.API/Controllers/ManufacturingStockAnalysisController.cs`) is a standard `[ApiController]` action with `[HttpGet]`, `[Route("api/manufacturing-stock-analysis")]`, and a typed `ActionResult<GetManufacturingStockAnalysisResponse>` return — nothing is missing.
- The generated client (`frontend/src/api/generated/api-client.ts`) already contains `manufacturingStockAnalysis_GetStockAnalysis(...)` (line ~7622), plus generated classes/enums `GetManufacturingStockAnalysisResponse`, `ManufacturingStockItemDto`, `ManufacturingStockSummaryDto`, `ManufacturingStockSeverity`, and `ManufacturingStockSortBy` that structurally match the hand-coded frontend types field-for-field (including the string-valued severity/sort-by enums).

So the generated client was never the problem — the hook (and a second call site) simply never adopted it, most likely because the hook predates the endpoint's codegen or was written by copying an older, pre-codegen pattern and never revisited. **No backend changes are required or in scope.**

A second call site was found during inspection that duplicates the identical anti-pattern against the same endpoint: `ManufacturingStockAnalysis.tsx`'s `handleExport` (lines ~174–247) manually builds the same query string (with `isExport=true` appended) and calls `(apiClient as any).http.fetch(...)` directly, using untyped `any` row data for the exported columns. Since this is the same root defect against the same endpoint contract in the same feature, it is included in scope — fixing only the hook while leaving `handleExport` hand-rolling would leave the exact same type-drift risk in place for the same data.

## Functional Requirements

### FR-1: Replace hand-coded types in `useManufacturingStockAnalysis.ts` with generated client types
Remove the six locally-declared types (`GetManufacturingStockAnalysisRequest` interface, `ManufacturingStockSortBy` enum, `ManufacturingStockSeverity` enum, `ManufacturingStockItemDto` interface, `ManufacturingStockSummaryDto` interface, `GetManufacturingStockAnalysisResponse` interface) currently at lines 27–111. Replace all internal usages with the equivalent generated types imported from `../generated/api-client`:
- `GetManufacturingStockAnalysisResponse` (class)
- `ManufacturingStockItemDto` (class)
- `ManufacturingStockSummaryDto` (class)
- `ManufacturingStockSeverity` (string enum)
- `ManufacturingStockSortBy` (string enum)

The hook's request shape (the parameter object accepted by `useManufacturingStockAnalysisQuery`) may remain a locally-declared plain object type (mirroring the request fields) since the generated client method takes positional scalar arguments, not a request object — but every field's type must reference the generated enums (`ManufacturingStockSortBy`, and the generated `TimePeriod` enum per FR-3) rather than re-declared ones.

**Acceptance criteria:**
- No local re-declaration of `ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto`, `ManufacturingStockSummaryDto`, or `GetManufacturingStockAnalysisResponse` remains in `useManufacturingStockAnalysis.ts`.
- `ManufacturingStockSeverity` and `ManufacturingStockSortBy` are imported from `../generated/api-client` and re-exported from the hook module (so existing consumer imports such as `ManufacturingStockAnalysis.tsx`'s `import { ManufacturingStockSortBy, ManufacturingStockSeverity, ManufacturingStockItemDto } from "../../api/hooks/useManufacturingStockAnalysis"` keep working without call-site changes), OR `ManufacturingStockAnalysis.tsx` is updated to import these directly from `../../api/generated/api-client` (see FR-4 — pick one consistently, see Open Questions/Dependencies for the recommended choice).
- `tsc`/`npm run build` reports no type errors anywhere the removed types were previously referenced.

### FR-2: Replace the manual `fetch` call in `useManufacturingStockAnalysisQuery` with the generated client method
Replace the manual `URLSearchParams` construction and `(apiClient as any).http.fetch(...)` call (lines 134–181) with a direct call to `apiClient.manufacturingStockAnalysis_GetStockAnalysis(timePeriod, customFromDate, customToDate, productFamily, criticalItemsOnly, majorItemsOnly, adequateItemsOnly, unconfiguredOnly, searchTerm, pageNumber, pageSize, sortBy, sortDescending, salesMultiplier, isExport)`, matching the generated method's positional signature. Pass `isExport: false` (or `undefined`) from this call site — the query hook is for interactive display, not export.

The `queryFn` must return the awaited result of this call directly (the generated method already parses and returns a typed `GetManufacturingStockAnalysisResponse`), removing the manual `response.ok` check, `response.json()` call, and the unchecked `as Promise<...>` cast. The generated client's `processManufacturingStockAnalysis_GetStockAnalysis` already throws on non-2xx/204 responses (via `throwException`), so existing error handling in consumers (`error` from `useQuery`) continues to work.

**Acceptance criteria:**
- No reference to `(apiClient as any)`, `.http.fetch(`, `baseUrl`, or manual `URLSearchParams`/query-string building remains in `useManufacturingStockAnalysisQuery`.
- `formatDateForApi` helper is removed if no longer used after the switch (the generated method accepts `Date | null | undefined` directly and serializes it internally).
- Existing hook unit tests in `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` are updated to mock `apiClient.manufacturingStockAnalysis_GetStockAnalysis` instead of `mockApiClient.http.fetch`, and continue to pass.
- A manual/exploratory check (or E2E smoke test if one already covers this page) confirms the Manufacturing Stock Analysis page still loads data, filters, sorts, and paginates correctly against a running backend.

### FR-3: Resolve the `TimePeriod` enum mismatch between app domain code and the generated client
The hook currently uses the app-wide `TimePeriod` enum from `frontend/src/utils/timePeriod/timePeriod.ts` (re-exported as `TimePeriodFilter`), which is a separate TypeScript enum declaration from the generated client's own `TimePeriod` enum in `api-client.ts` — even though both declare identical string members (`PreviousQuarter`, `FutureQuarter`, `Y2Y`, `PreviousSeason`, `Q9M`, `CustomPeriod`). TypeScript enums are nominal, not structural, so a value typed as the app's `TimePeriod` cannot be passed directly where the generated method expects its own `TimePeriod` parameter without a compile error.

The app-level `TimePeriod`/`TimePeriodFilter` enum is domain vocabulary used across multiple features (not just this endpoint) and is **out of scope** to consolidate or remove. Instead, convert at the API boundary, inside `useManufacturingStockAnalysisQuery`'s `queryFn`, immediately before calling the generated method — e.g. `request.timePeriod as unknown as GeneratedTimePeriod` (aliasing the generated import as `GeneratedTimePeriod` to avoid a name clash with the app's `TimePeriod`), relying on the identical underlying string values. Do not introduce a value-by-value mapping table unless a future change makes the two enums diverge in membership.

**Acceptance criteria:**
- The generated `TimePeriod` export from `api-client.ts` is imported under a distinct local alias (e.g. `GeneratedTimePeriod`) to avoid collision with the existing `TimePeriod` import from `utils/timePeriod`.
- A single, clearly-commented conversion point exists where the app's `TimePeriodFilter` value is converted to the generated `TimePeriod` type before the generated client call.
- No behavior change to any existing consumer of `TimePeriodFilter`/`calculateTimePeriodRange`/`getTimePeriodDisplayText`, which continue to use the app-level enum unchanged.

### FR-4: Update `ManufacturingStockAnalysis.tsx`'s import of Manufacture Stock types
Decide and apply one consistent import strategy for the types the page consumes from the hook module (`GetManufacturingStockAnalysisRequest`, `ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto`): keep re-exporting them from `useManufacturingStockAnalysis.ts` (minimizes churn in the page component) rather than changing the page's import paths, since the hook module already serves as this feature's local API-hook surface and other pages import filter/request shapes from their corresponding hook files in this codebase.

**Acceptance criteria:**
- `ManufacturingStockAnalysis.tsx`'s existing two import statements from `../../api/hooks/useManufacturingStockAnalysis` (lines 18–29 and line 38) continue to compile unchanged.
- `ManufactureBatchPlanning.tsx`'s import of `calculateTimePeriodRange` from the same hook module (line 26) continues to compile unchanged.

### FR-5: Migrate `handleExport` in `ManufacturingStockAnalysis.tsx` to the generated client
Replace the manual `URLSearchParams`/`(apiClient as any).http.fetch(...)` construction in `handleExport` (lines 174–247) with a call to `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)` using the same positional arguments derived from `filters`, passing `isExport: true`. Replace the `any`-typed `result.items` column accessors (lines 220–238) with accessors typed against the generated `ManufacturingStockItemDto` class.

**Acceptance criteria:**
- No reference to `(apiClient as any)`, `.http.fetch(`, or manual `URLSearchParams` remains in `handleExport`.
- The exported `.xlsx` file's column set and per-row values are unchanged from current behavior (same headers, same fields, same formatting) — this is a pure internal refactor, not a feature change to the export.
- Existing test coverage for the export flow (if any in `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx`) is updated to mock the generated client method instead of `http.fetch`, and continues to pass.

### FR-6: Keep backend request/response contracts unchanged
No changes to `GetManufacturingStockAnalysisRequest.cs`, `GetManufacturingStockAnalysisResponse.cs`, `GetManufacturingStockAnalysisHandler.cs`, or `ManufacturingStockAnalysisController.cs` are required by this spec. If the generated client is regenerated as part of this change (e.g. via the project's standard `npm run generate-client` / build-time codegen step), the regenerated output for this endpoint must be a no-op diff (aside from unrelated endpoints that may have changed independently), confirming the backend contract was already correct.

**Acceptance criteria:**
- `git diff` on `backend/` is empty for this change.
- Regenerating the OpenAPI client (if run) produces no changes to the `manufacturingStockAnalysis_*` symbols beyond formatting/ordering noise already present in the generated file from unrelated endpoints.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or required — this replaces one HTTP call mechanism with another against the same endpoint, same query parameters, same response payload. The existing `staleTime: 1000 * 60 * 2` on the query hook is preserved unchanged.

### NFR-2: Security
No change. Authentication continues to flow through `getAuthenticatedApiClient()` exactly as before; the generated client method uses the same underlying authenticated `http.fetch` transport. `[FeatureAuthorize(Feature.Manufacture_ManufactureStock)]` on the controller is unaffected (backend unchanged).

### NFR-3: Type safety
This is the primary motivation for the change: after this work, the compiler must catch any future drift between the backend's `GetManufacturingStockAnalysisResponse`/`GetManufacturingStockAnalysisRequest`/`ManufacturingStockSeverity`/`ManufacturingStockSortBy` and what the frontend consumes, since both sides derive from the same generated artifact. No `as any` or unchecked type assertions may remain in `useManufacturingStockAnalysis.ts` or in `handleExport`.

## Data Model
No new entities. This spec reuses existing backend types, already mirrored in the generated client:
- `GetManufacturingStockAnalysisRequest` (backend, `IRequest<GetManufacturingStockAnalysisResponse>`) — query parameters: `TimePeriod`, `CustomFromDate`, `CustomToDate`, `ProductFamily`, `CriticalItemsOnly`, `MajorItemsOnly`, `AdequateItemsOnly`, `UnconfiguredOnly`, `SearchTerm`, `PageNumber`, `PageSize`, `SortBy` (`ManufacturingStockSortBy`), `SortDescending`, `SalesMultiplier`, `IsExport`.
- `GetManufacturingStockAnalysisResponse` (backend, extends `BaseResponse`) — `Items: List<ManufacturingStockItemDto>`, `TotalCount`, `PageNumber`, `PageSize`, `Summary: ManufacturingStockSummaryDto`.
- `ManufacturingStockItemDto` — per-product stock/consumption/severity fields (see backend source for the full field list; the generated client mirrors it exactly).
- `ManufacturingStockSummaryDto` — aggregate counts by severity plus the resolved analysis period and product family list.
- `ManufacturingStockSeverity` (string enum: `Critical`, `Major`, `Minor`, `Adequate`, `Unconfigured`) and `ManufacturingStockSortBy` (string enum, 13 members) — both already generated as TypeScript string enums matching the backend C# enums' JSON string serialization.

The only mapping concern is the `TimePeriod` enum duality described in FR-3 (app-level `TimePeriod` vs. generated-client `TimePeriod`) — same string members, different TypeScript nominal types.

## API / Interface Design
No new or changed API surface. Existing endpoint, unchanged:

```
GET /api/manufacturing-stock-analysis
  ?TimePeriod=...&CustomFromDate=...&CustomToDate=...&ProductFamily=...
  &CriticalItemsOnly=...&MajorItemsOnly=...&AdequateItemsOnly=...&UnconfiguredOnly=...
  &SearchTerm=...&PageNumber=...&PageSize=...&SortBy=...&SortDescending=...
  &SalesMultiplier=...&IsExport=...
→ 200 GetManufacturingStockAnalysisResponse
```

Frontend interface change only: internal call site switches from a hand-built `fetch` to `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)` in two places (`useManufacturingStockAnalysisQuery`'s `queryFn`, and `ManufacturingStockAnalysis.tsx`'s `handleExport`).

## Dependencies
- `frontend/src/api/generated/api-client.ts` — already contains the needed `manufacturingStockAnalysis_GetStockAnalysis` method and associated types; no regeneration is strictly required, though running the standard codegen step is harmless and recommended to confirm FR-6's no-op-diff expectation.
- `frontend/src/utils/timePeriod/timePeriod.ts` — source of the app-level `TimePeriod`/`TimePeriodFilter` enum that must be reconciled per FR-3.
- Existing test suites: `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`, `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx`.

## Out of Scope
- Any backend changes to the controller, handler, request/response DTOs, or validators for manufacturing stock analysis (confirmed unnecessary — see Background).
- Consolidating the app-level `TimePeriod`/`TimePeriodFilter` enum with the generated client's `TimePeriod` enum, or changing any other consumer of the app-level enum.
- Any new fields, filters, or behavior changes to the manufacturing stock analysis feature itself (this is a type-safety refactor, not a feature change).
- Changing the export file format, columns, or filename convention in `handleExport`.

## Open Questions
None.

## Status: COMPLETE
