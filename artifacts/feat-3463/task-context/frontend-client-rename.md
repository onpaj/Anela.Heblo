### task: frontend-client-rename

**Goal:** Regenerate the OpenAPI TypeScript client so the response element type reflects the backend rename (`DailyInvoiceCount` → `DailyInvoiceCountDto`), then propagate the renamed type verbatim (no alias) through the one hook and one component that reference it.

**Files to change:**
- `frontend/src/api/generated/api-client.ts` — auto-regenerated via codegen; do not hand-edit. The class previously named `DailyInvoiceCount` (implements `IDailyInvoiceCount`) becomes `DailyInvoiceCountDto` (implements `IDailyInvoiceCountDto`) as a side effect of the backend `GetInvoiceImportStatisticsResponse.Data` type rename.
- `frontend/src/api/hooks/useInvoiceImportStatistics.ts` — rename the import and re-export of `DailyInvoiceCount` from `../generated/api-client` to `DailyInvoiceCountDto`.
- `frontend/src/components/charts/InvoiceImportChart.tsx` — rename the `DailyInvoiceCount` import (from `'../../api/hooks/useInvoiceImportStatistics'`) used for `InvoiceImportChartProps.data` to `DailyInvoiceCountDto`. No change to `.map()` usage or field access (`.date`, `.count`, `.isBelowThreshold`), since only the type name changes.

**Steps:**
1. Ensure the backend change from `backend-dto-extraction` is complete and builds, so the OpenAPI schema exposes `DailyInvoiceCountDto` as the response element type.
2. Regenerate the frontend OpenAPI client per `docs/development/api-client-generation.md` (the project's standard codegen step, run as part of build) so `frontend/src/api/generated/api-client.ts` picks up the renamed class. Do not hand-edit the generated file.
3. Update `frontend/src/api/hooks/useInvoiceImportStatistics.ts` to import/re-export `DailyInvoiceCountDto` instead of `DailyInvoiceCount`.
4. Update `frontend/src/components/charts/InvoiceImportChart.tsx` to import `DailyInvoiceCountDto` instead of `DailyInvoiceCount` for `InvoiceImportChartProps.data`.
5. Search the frontend tree for any other reference to the bare `DailyInvoiceCount` type name to confirm none remain (per spec, only these two files reference it; `InvoiceImportStatistics.tsx` only does field access, no type-name reference, and needs no change).
6. Do not introduce any local type alias (e.g. `export type DailyInvoiceCount = DailyInvoiceCountDto`) anywhere.
7. Run `npm run build` from `frontend/` and confirm it succeeds with no TypeScript errors.
8. Run `npm run lint` from `frontend/` and confirm it passes.
9. Run the existing hook test to confirm it still passes unmodified.

**Acceptance criteria:**
- `npm run build` (in `frontend/`) succeeds with no TypeScript errors related to `DailyInvoiceCount`/`DailyInvoiceCountDto`.
- `npm run lint` (in `frontend/`) passes with no new violations.
- No local type alias for `DailyInvoiceCount` exists anywhere in `frontend/src`; grep for `DailyInvoiceCount` (exact, non-`Dto`-suffixed) in `frontend/src` returns no matches other than as a substring of `DailyInvoiceCountDto`.
- `frontend/src/api/hooks/useInvoiceImportStatistics.ts` and `frontend/src/components/charts/InvoiceImportChart.tsx` both reference `DailyInvoiceCountDto` directly.
- Run `npm test -- useInvoiceImportStatistics.test.ts` (or equivalent test runner invocation) against `frontend/src/api/hooks/__tests__/useInvoiceImportStatistics.test.ts` — passes unmodified, since it mocks the API response as a plain object literal with `isBelowThreshold` and is insensitive to the TS class rename.
- `InvoiceImportChart.tsx` and `InvoiceImportStatistics.tsx` render identical `isBelowThreshold`-driven behavior (red reference dot, tooltip warning, problematic-day count) — no visual or logic change, verified by inspection/existing component tests if present.
