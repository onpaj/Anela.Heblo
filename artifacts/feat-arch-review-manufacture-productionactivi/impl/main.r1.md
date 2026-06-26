All 5 tasks complete. Here is the output summary:

---

# Implementation: TimeProvider Injection in ProductionActivityAnalyzer

## What was implemented

Replaced direct `DateTime.UtcNow` usage in `ProductionActivityAnalyzer` with the injected `TimeProvider` abstraction. The constructor now accepts `TimeProvider` alongside the existing `ILogger<T>`, matching the convention already used by 15 peer handlers in the Manufacture module. The test suite was migrated to `FakeTimeProvider` with a frozen UTC instant, making all time-windowed tests fully deterministic, and four new boundary tests were added to pin the inclusive equality semantics (`m.Date >= cutoffDate`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs` — added `TimeProvider` field + constructor param; replaced 2× `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime`; `GetLastProductionDate` and all logging unchanged
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ProductionActivityAnalyzerTests.cs` — fixture switched to `FakeTimeProvider(FrozenNowUtc = 2026-06-01 12:00 UTC)`; all 12 original tests reseeded from `FrozenNowUtc`; 4 new boundary tests added
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — added `Microsoft.Extensions.TimeProvider.Testing 8.1.0` (8.0.1 was not on NuGet; 8.1.0 is the compatible 8.x release)

## Tests

- **ProductionActivityAnalyzerTests** — 16 tests total (12 original + 4 boundary): all pass
  - `IsInActiveProduction_RecordExactlyAtCutoff_IsConsideredActive` — inclusive boundary
  - `IsInActiveProduction_RecordOneTickBeforeCutoff_IsNotConsideredActive` — exclusive below
  - `CalculateAverageProductionFrequency_RecordExactlyAtAnalysisStart_IsIncluded` — inclusive boundary → 15-day interval
  - `CalculateAverageProductionFrequency_RecordOneTickBeforeAnalysisStart_IsExcluded` — excluded → `PositiveInfinity`
- **Manufacture slice** — 538 tests, all pass

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductionActivityAnalyzerTests" --no-build
grep -nH 'DateTime\.UtcNow' \
  src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs \
  test/Anela.Heblo.Tests/Features/Manufacture/Services/ProductionActivityAnalyzerTests.cs
```
All three commands should complete cleanly.

## Notes

- Package version 8.0.1 was unavailable on NuGet; 8.1.0 was used — still targets `net8.0`, fully compatible.
- `IProductionActivityAnalyzer` interface and all consumers (`ManufactureSeverityCalculator`, etc.) are unchanged.
- No DI registration changes were required — `TimeProvider.System` was already registered as a singleton in `ServiceCollectionExtensions.cs`.

## PR Summary

Injects `TimeProvider` into `ProductionActivityAnalyzer` so that time-windowed business logic (active-production detection and average production frequency) becomes deterministically testable. This is a pure mechanical refactor — no interface changes, no DI wiring changes, no behavioral changes in production.

The test suite is now anchored to a frozen UTC instant (`2026-06-01 12:00:00`) via `FakeTimeProvider`, eliminating silent date-drift in existing tests and enabling precise boundary assertions. Four new boundary tests pin the inclusive equality semantics (`m.Date >= cutoffDate`) that the production code already implements.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs` — added `TimeProvider` ctor param and field; replaced 2× `DateTime.UtcNow`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ProductionActivityAnalyzerTests.cs` — migrated to `FakeTimeProvider`; 4 boundary tests added (16 total)
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — added `Microsoft.Extensions.TimeProvider.Testing 8.1.0`

## Status

DONE