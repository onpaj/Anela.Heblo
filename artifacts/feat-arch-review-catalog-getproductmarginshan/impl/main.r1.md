The commit contains the expected 2 target files plus a 1-line `using` directive added to `GetIssuedInvoiceDetailHandlerTests.cs` — a pre-existing compilation error that was blocking `dotnet build`. This is noted as a minor out-of-scope deviation.

---

# Implementation: inject-timeprovider-getproductmarginshandler

## What was implemented

Replaced the hardcoded `DateTime.Now` in `GetProductMarginsHandler.MapToMarginDto` (line 191) with the injected `TimeProvider` abstraction, following the established pattern of sibling Catalog handlers. Added the first unit test class for this handler with two test cases — a 13-month boundary inclusion/exclusion test and a UTC-vs-UTC+1 day-boundary correctness test.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — added `private readonly TimeProvider _timeProvider` field, added `TimeProvider timeProvider` constructor parameter (between `ICatalogRepository` and `ILogger`), replaced `DateTime.Now.AddMonths(-13)` with `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — new file; 2 xUnit tests using `Mock<TimeProvider>` / `Mock<ICatalogRepository>` / FluentAssertions, following the `GetCatalogDetailHandlerTests` pattern

## Tests

`GetProductMarginsHandlerTests.cs` — 2 tests, both passing:
- `Handle_FiltersMonthlyMarginsTo13MonthsBeforeInjectedUtcNow` — verifies boundary inclusion (`>=`) and exclusion (`addTicks(-1)`) against the injected UTC time
- `Handle_UsesUtcNotLocalTime_AtDayBoundary` — sets UTC `2025-12-31T23:30:00Z`; asserts key `2024-12-01T00:00:00` is included (correct under UTC semantics, excluded under UTC+1 local semantics)

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginsHandlerTests"
# Expected: 2/2 passed

dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Catalog"
# Expected: all Catalog tests pass

dotnet build
# Expected: 0 errors
```

Also grep to confirm no `DateTime.Now` remains in the handler:
```bash
grep -n "DateTime.Now" backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs
# Expected: no output
```

## Notes

- **Minor out-of-scope change**: The implementer added one `using Anela.Heblo.Application.Features.Invoices.Contracts;` directive to `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — a pre-existing compilation error that was blocking `dotnet build`. Single-line fix, no behavioral change, not a concern.
- DI registration confirmed present at `ServiceCollectionExtensions.cs:128` — no registration changes required.
- `FakeTimeProvider` was intentionally NOT introduced; `Mock<TimeProvider>` is used throughout per arch-review amendment #1.

## PR Summary

Fixes a UTC-vs-local-time discrepancy in `GetProductMarginsHandler` where `DateTime.Now` was used to compute the 13-month history window — the only Catalog handler that did not follow the module-wide `TimeProvider` injection convention. In a UTC+1 timezone, `DateTime.Now` around midnight could shift the window boundary by one month relative to the UTC-based values stored in `MonthlyData.Key`, producing off-by-one results.

The handler now constructor-injects `TimeProvider` (already a singleton in the DI container) and computes `dateFrom` as `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`, matching `GetCatalogDetailHandler` and other sibling services. The new test class pins both the boundary-inclusion semantics and the UTC correctness against a day-boundary edge case.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — added `TimeProvider` field and constructor parameter; replaced `DateTime.Now` with `_timeProvider.GetUtcNow().DateTime`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — first unit test class for this handler; 2 test methods covering boundary inclusion and UTC correctness
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added missing `using` directive (pre-existing compilation error, 1 line)

## Status
DONE