---

# Implementation: Strongly-Type `MarginLevel` in Analytics Module

## What was implemented

Replaced the stringly-typed `MarginLevel` parameter throughout the Analytics module with a proper C# enum `Anela.Heblo.Domain.Features.Analytics.MarginLevel`. The silent-fallback-to-M2 bug on invalid input is eliminated — `GetMarginAmountForLevel` now throws `ArgumentOutOfRangeException` on undefined enum values. Invalid wire input (e.g. `marginLevel=M9`) is rejected by ASP.NET Core model binding with a 400 before reaching the handler. The OpenAPI generator regenerated the TypeScript client with a string-valued `MarginLevel` enum; the frontend dropped its local `MarginLevelType` string union in favor of the generated type.

## Files created/modified

**Backend — created:**
- `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginLevel.cs` — New `public enum MarginLevel { M0, M1, M2 }` with XML doc disambiguating from `Catalog.MarginLevel` value object

**Backend — modified:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — Both interface and implementation: `CalculateAsync` and `GetMarginAmountForLevel` signatures use `MarginLevel` enum; switch throws on undefined values
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — Interface and all four methods (`Generate`, `GenerateMonthlySegments`, `ProcessGroupForMonth`) use `MarginLevel` enum
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs` — `MarginLevel` property is now `public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;`
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryResponse.cs` — Same change (response symmetry per arch-review FR-8)
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — `GenerateTopProducts` and `CalculateTotalMarginForLevel` private helpers use enum

**Tests — modified:**
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — Mock setup/verify updated to `MarginLevel.M2`; deleted `Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin` and `Handle_UnknownMarginLevel_FallsBackToM2`; added `GetMarginAmountForLevel_WithUndefinedEnumValue_ThrowsArgumentOutOfRangeException`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` — Added `using CatalogMarginLevel = Anela.Heblo.Domain.Features.Catalog.MarginLevel;` alias to resolve ambiguity; replaced `new MarginLevel(` with `new CatalogMarginLevel(`

**Frontend — modified:**
- `frontend/src/api/generated/api-client.ts` — Regenerated; now contains `export enum MarginLevel { M0 = "M0", M1 = "M1", M2 = "M2" }` and `marginLevel: MarginLevel` parameter on the API method
- `frontend/src/api/hooks/useProductMarginSummary.ts` — Imports and re-exports generated `MarginLevel`; `marginLevel` parameter typed as `MarginLevel = MarginLevel.M2`
- `frontend/src/components/pages/ProductMarginSummary.tsx` — Removed `type MarginLevelType = "M0" | "M1" | "M2"`; `useState<MarginLevel>(MarginLevel.M2)`; `onChange` cast uses `MarginLevel`

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — 7 passing tests including the new throw-on-undefined-enum unit test
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` — 9 passing tests (type alias fix)
- Full backend suite: 4,685 pass / 38 pre-existing Docker/Testcontainers failures (unchanged)

## How to verify

```bash
# Backend
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Analytics"

# Frontend
cd frontend && npm run build

# No string-typed marginLevel remains
grep -rn "string marginLevel\|string MarginLevel" \
  backend/src/Anela.Heblo.Application/Features/Analytics \
  backend/src/Anela.Heblo.Domain/Features/Analytics
```

## Notes

- The `CatalogAnalyticsSourceAdapterTests.cs` type alias fix was an unplanned but necessary addition — the name collision between `Analytics.MarginLevel` (new enum) and `Catalog.MarginLevel` (existing value object class) caused ambiguous reference compiler errors in that test file. Fixed minimally with a `using` alias.
- Frontend lint has 126 pre-existing errors (testing-library rule violations in unrelated test files) that were present before this change. Our modified files (`useProductMarginSummary.ts`, `ProductMarginSummary.tsx`) are not in the error list.
- Wire format `marginLevel=M0|M1|M2` is byte-identical to before — `JsonStringEnumConverter` is globally registered at `Program.cs`.
- No WebApplicationFactory harness was found in the test project (`grep -rn "WebApplicationFactory" backend/test/` returned no matches), so the conditional `Get_WithInvalidMarginLevelQueryString_Returns400` integration test was skipped per plan Task 7.

## PR Summary

Replaces the stringly-typed `MarginLevel` parameter in the Analytics module with a strongly-typed C# enum, closing a silent-fallback-to-M2 correctness bug and aligning with sibling discriminators (`ProductGroupingMode`, `AnalyticsProductType`). The enum is propagated through the full call chain — domain layer, request/response DTOs, `IMarginCalculator`, `IMonthlyBreakdownGenerator`, and `GetProductMarginSummaryHandler` — with no wire-format change (JSON serialisation stays `"M0"/"M1"/"M2"` via the globally registered `JsonStringEnumConverter`). The OpenAPI generator regenerated the TypeScript client with a string-valued enum; the frontend dropped its local string-literal union.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginLevel.cs` — new enum with XML doc disambiguating from `Catalog.MarginLevel`
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — enum signatures; throw on undefined values
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — enum signatures throughout
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs` — enum property
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryResponse.cs` — enum property (symmetry)
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — enum private helpers
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — deleted legacy string tests; added throw-test
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` — type alias to resolve `MarginLevel` name collision
- `frontend/src/api/generated/api-client.ts` — regenerated with `MarginLevel` string enum
- `frontend/src/api/hooks/useProductMarginSummary.ts` — typed `marginLevel: MarginLevel`
- `frontend/src/components/pages/ProductMarginSummary.tsx` — dropped local `MarginLevelType` union

## Status

DONE