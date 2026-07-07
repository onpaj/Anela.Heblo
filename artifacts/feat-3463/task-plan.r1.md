# Implementation Plan: Remove application-layer concern from `DailyInvoiceCount` Domain type

## Overview
Small Clean Architecture cleanup: remove `IsBelowThreshold` from the Domain type `DailyInvoiceCount`, introduce a new Application-layer `DailyInvoiceCountDto` class in `Contracts/` that carries the computed threshold flag, and update the handler to project into the DTO instead of mutating the Domain object. Two tasks, run serially: the backend change (Domain/Application/tests) first, since it drives the OpenAPI schema rename; then the frontend ripple (regenerated client + two consuming files) once the backend change is buildable and the new schema name is known. No HTTP contract or JSON shape changes — only C# type names change.

### task: backend-dto-extraction
**Goal:** Remove `IsBelowThreshold` from the Domain `DailyInvoiceCount`, add `DailyInvoiceCountDto` to the Analytics `Contracts/` folder, move the threshold computation into `GetInvoiceImportStatisticsHandler`, fix the now-dead `IsBelowThreshold = false` initializers in `InvoiceImportStatisticsSourceAdapter`, and update all affected backend tests.

**Files to change:**
- `backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs` — remove the `IsBelowThreshold` property/setter; keep only `Date` (DateTime) and `Count` (int).
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs` — new file. Plain C# class (not a record), namespace `Anela.Heblo.Application.Features.Analytics.Contracts`, properties `Date` (DateTime), `Count` (int), `IsBelowThreshold` (bool), public getters/setters, matching the style of the sibling `TopProductDto.cs` in the same folder.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — replace the in-place mutation loop over `DailyInvoiceCount` with a `Select` projection into `DailyInvoiceCountDto`, computing `IsBelowThreshold = c.Count < minimumThreshold` (same `<` semantics) at projection time. Add `using Anela.Heblo.Application.Features.Analytics.Contracts;`; drop the `using` for the Domain namespace if it becomes unused in this file.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsResponse.cs` — change `Data` from `List<DailyInvoiceCount>` to `List<DailyInvoiceCountDto>`; update `using` accordingly.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — remove the `IsBelowThreshold = false` initializer from all three `new DailyInvoiceCount { ... }` construction sites (approx. lines 47–52, 71–76, 92–97).
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs` — edit the XML doc comment on `GetDailyCountsAsync` to remove the trailing clause about `IsBelowThreshold` always being `false`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — drop `IsBelowThreshold = false` from the mocked `DailyInvoiceCount` initializers (repository mock still returns Domain-typed objects, now without the property). Keep the assertions on `result.Data[0].IsBelowThreshold` / `result.Data[1].IsBelowThreshold`, now read off `DailyInvoiceCountDto` instances in `result.Data`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTileTests.cs` — drop `IsBelowThreshold = false` from the two `DailyInvoiceCount` literals (lines ~41, ~79); no assertions reference the flag in this file.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — delete the single assertion line `result[0].IsBelowThreshold.Should().BeFalse();` (line ~73, inside `GetDailyCountsAsync_InvoiceDateBranch_ReturnsCountsGroupedByDay`); no replacement assertion, no other changes.

**Steps:**
1. Remove `IsBelowThreshold` from `DailyInvoiceCount.cs` (Domain).
2. Create `DailyInvoiceCountDto.cs` in `Application/Features/Analytics/Contracts/`, matching `TopProductDto.cs` style.
3. Update `GetInvoiceImportStatisticsResponse.cs` so `Data` is `List<DailyInvoiceCountDto>`.
4. Update `GetInvoiceImportStatisticsHandler.cs` to project `DailyInvoiceCount` → `DailyInvoiceCountDto` via `Select`, computing `IsBelowThreshold` there instead of mutating.
5. Remove the three dead `IsBelowThreshold = false` initializers in `InvoiceImportStatisticsSourceAdapter.cs`.
6. Update the XML doc on `IInvoiceImportStatisticsSource.GetDailyCountsAsync` to drop the now-inapplicable sentence about `IsBelowThreshold`.
7. Update the three backend test files as described above so they compile and continue to assert equivalent behavior.
8. Run `dotnet build` from `backend/` and confirm no remaining references to `DailyInvoiceCount.IsBelowThreshold` anywhere in the solution.
9. Run `dotnet format` from `backend/` to apply formatting conventions.
10. Run the affected test suites and confirm they pass.

**Acceptance criteria:**
- `dotnet build` succeeds with zero errors/warnings related to `DailyInvoiceCount.IsBelowThreshold`.
- `dotnet format` reports no outstanding changes (or has been applied) for touched files.
- `DailyInvoiceCount` (Domain) has only `Date` and `Count`; no `IsBelowThreshold` property or setter anywhere in `backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs`.
- `DailyInvoiceCountDto` exists at `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs`, is a `class` (not a `record`), with `Date` (DateTime), `Count` (int), `IsBelowThreshold` (bool).
- `GetInvoiceImportStatisticsResponse.Data` is `List<DailyInvoiceCountDto>`.
- Run `dotnet test --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests"` (in `backend/test/Anela.Heblo.Tests`) — all pass, including `Handle_ShouldReturnStatisticsWithMinimumThreshold` asserting `Count = 15`/threshold `10` → `IsBelowThreshold == false`, and `Count = 5` → `IsBelowThreshold == true`, read from `DailyInvoiceCountDto` objects.
- Run `dotnet test --filter "FullyQualifiedName~InvoiceImportStatisticsTileTests"` — all pass.
- Run `dotnet test --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests"` — all pass, with no assertion remaining on `IsBelowThreshold`.
- No production or test code outside this task's listed files references `DailyInvoiceCount.IsBelowThreshold`; verified by `dotnet build` failing loudly on any miss (compile error, not silent).
- Serialized JSON shape of the `GET` invoice import statistics response is unchanged (`date`, `count`, `isBelowThreshold`, `minimumThreshold` fields) — no controller or route changes were made.

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
