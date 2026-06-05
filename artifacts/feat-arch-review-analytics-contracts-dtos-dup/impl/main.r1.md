Final review: **READY TO MERGE**. Two cosmetic MEDIUM observations (collection expression style, trailing newline) — both non-blocking per the reviewer. All 72 Analytics tests pass, build clean, zero stale references, wire shape unchanged.

---

# Implementation: Consolidate Duplicate Analytics DTOs

## What was implemented

Removed three pairs of duplicate DTO types from the Analytics module. Each pair had an identical Contracts/ DTO and a response-nested class with a verbatim identity-projection mapping in the handler. The nested classes are deleted; handlers now assign service results directly to the response properties using the Contracts/ DTOs. The HTTP/JSON wire shape is byte-equivalent before and after.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs` — deleted `ProductMarginSummary` and `CategoryMarginSummary` nested classes; added `using Contracts;`; changed both collection types to `List<ProductMarginSummaryDto>` / `List<CategoryMarginSummaryDto>`. File reduced from 54 → 17 lines.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — collapsed two `Select(dto => new NestedClass { 16/6 fields })` blocks to direct assignments `reportData.ProductSummaries` / `reportData.CategorySummaries`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs` — deleted `MonthlyMarginBreakdown` nested class; changed collection type to `List<MonthlyMarginBreakdownDto>`. File: 17 lines.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — collapsed `Select(dto => new ...)` block to `_reportBuilderService.BuildMonthlyBreakdown(...) ?? new List<MonthlyMarginBreakdownDto>()`.
- `frontend/src/api/generated/api-client.ts` — regenerated: `ProductMarginSummary` → `ProductMarginSummaryDto`, `CategoryMarginSummary` → `CategoryMarginSummaryDto`, `MonthlyMarginBreakdown` → `MonthlyMarginBreakdownDto`. JSON property names unchanged.

## Tests

- `GetMarginReportHandlerTests.cs` — 7/7 pass (unchanged, now exercises direct-assignment path)
- `GetProductMarginAnalysisHandlerTests.cs` — 6/6 pass (unchanged)
- Full Analytics suite: 72/72 pass

## How to verify

```bash
# Backend build
dotnet build Anela.Heblo.sln

# Analytics tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Analytics" --no-build

# Zero stale references
grep -rn "GetMarginReportResponse\.ProductMarginSummary\|GetMarginReportResponse\.CategoryMarginSummary\|GetProductMarginAnalysisResponse\.MonthlyMarginBreakdown" backend/

# Format check
dotnet format Anela.Heblo.sln --verify-no-changes

# Frontend build
npm --prefix frontend run build
```

## Notes

- **No interface changes**: `IReportBuilderService` already returned the Contracts/ DTOs; no service-layer edits were needed.
- **No test edits**: existing tests already used `Contracts/` DTO names, not nested-class names.
- **No hand-written FE changes**: only the generated `api-client.ts` changed in the frontend.
- **TS client regeneration**: the NSwag prebuild also picked up unrelated backend schema changes (new BankStatements query parameters, UpdateManufactureOrder.manufactureType field, SubmitArticleFeedbackResponse 409 handler). These are valid regenerated content — the generated file always reflects the full current schema.
- **Frontend lint**: 144 pre-existing lint errors (not introduced by this branch — no hand-written `.ts`/`.tsx` files were modified). The generated `api-client.ts` file is not excluded from the ESLint config; this is a pre-existing repo configuration issue.
- **Cosmetic MEDIUM findings from final review** (non-blocking): `?? new List<MonthlyMarginBreakdownDto>()` could use C# 12 `?? []` syntax; two Response files lack trailing newlines. Both are below `dotnet format` severity threshold.

## PR Summary

Eliminated three duplicate DTO pairs in the Analytics module. Each pair had an identical Contracts/ DTO and a response-nested class connected by a verbatim identity `Select(dto => new NestedType { ... })` projection. Deleting the nested classes and collapsing the projections to direct assignments removes ~90 lines of boilerplate and means future field additions need only one edit instead of two.

The HTTP/JSON wire shape is unchanged — same property names, same casing, same types. The NSwag-generated TypeScript client was regenerated: three type names gain the `Dto` suffix (`ProductMarginSummaryDto`, `CategoryMarginSummaryDto`, `MonthlyMarginBreakdownDto`). All 72 Analytics backend tests pass without modification.

### Changes
- `GetMarginReportResponse.cs` — removed `ProductMarginSummary` and `CategoryMarginSummary` nested classes; collection types now reference Contracts/ DTOs directly
- `GetMarginReportHandler.cs` — two Select projection blocks collapsed to direct assignments
- `GetProductMarginAnalysisResponse.cs` — removed `MonthlyMarginBreakdown` nested class; collection type now references `MonthlyMarginBreakdownDto`
- `GetProductMarginAnalysisHandler.cs` — Select projection block collapsed to direct assignment with null-guard preserved
- `frontend/src/api/generated/api-client.ts` — regenerated: three Analytics type renames + other schema changes present in current backend

## Status
DONE_WITH_CONCERNS

Concerns: (1) Frontend `npm run lint` exits non-zero with 144 pre-existing errors unrelated to this branch — the generated `api-client.ts` is not excluded from ESLint config. (2) The TS client regeneration also captured unrelated backend schema changes that have accumulated since the last regeneration commit; these are correct but broaden the PR's diff surface.