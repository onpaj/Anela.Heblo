# Specification: TimeProvider Injection in ProductionActivityAnalyzer

## Summary
Replace direct `DateTime.UtcNow` usage in `ProductionActivityAnalyzer` with the injected `TimeProvider` abstraction so that time-windowed business logic (active-production detection and average production frequency) becomes deterministically testable. This aligns the service with the rest of the Manufacture module, where every other time-aware handler already injects `TimeProvider`.

## Background
`ProductionActivityAnalyzer` implements two pieces of time-windowed business logic:

- **`IsInActiveProduction`** — classifies a product as "in active production" if any manufacture history record falls within the last N days.
- **`CalculateAverageProductionFrequency`** — averages the intervals between production events within the last M months.

Both outputs feed into severity classifications displayed on the stock analysis dashboard. Today both methods read wall-clock time directly via `DateTime.UtcNow` (lines 17 and 45 of `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs`). The existing test suite in `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ProductionActivityAnalyzerTests.cs` works around this by seeding records with `DateTime.UtcNow.AddDays(...)` / `AddMonths(...)` offsets. These tests are correct on the day they were written but drift silently as time passes, and they cannot exercise boundary scenarios (e.g. "a 25-day-old run is active at threshold 30 but inactive at threshold 20") without flakiness.

The Manufacture module already registers `IProductionActivityAnalyzer` in `ManufactureModule.AddManufactureModule`, and `TimeProvider` is registered as a framework singleton — so no DI wiring changes are required beyond constructor injection.

## Functional Requirements

### FR-1: Inject TimeProvider into ProductionActivityAnalyzer
The `ProductionActivityAnalyzer` constructor must accept `TimeProvider` in addition to its existing `ILogger<ProductionActivityAnalyzer>` dependency, and must store it as a private readonly field.

**Acceptance criteria:**
- The class exposes a single public constructor with the signature `ProductionActivityAnalyzer(ILogger<ProductionActivityAnalyzer> logger, TimeProvider timeProvider)`.
- Both parameters are stored as private readonly fields.
- The class continues to implement `IProductionActivityAnalyzer` without interface changes.
- The DI container resolves the class without any additional registration changes (TimeProvider is supplied by the framework as a singleton).

### FR-2: Replace DateTime.UtcNow in IsInActiveProduction
Line 17 must derive `cutoffDate` from the injected `TimeProvider` instead of `DateTime.UtcNow`.

**Acceptance criteria:**
- `cutoffDate` is computed as `_timeProvider.GetUtcNow().DateTime.AddDays(-dayThreshold)`.
- The method's observable behavior with a real `TimeProvider.System` is identical to today's behavior (within clock-resolution tolerance).
- A unit test using `FakeTimeProvider` with a frozen "now" can deterministically verify both sides of the threshold boundary.

### FR-3: Replace DateTime.UtcNow in CalculateAverageProductionFrequency
Line 45 must derive `analysisStartDate` from the injected `TimeProvider` instead of `DateTime.UtcNow`.

**Acceptance criteria:**
- `analysisStartDate` is computed as `_timeProvider.GetUtcNow().DateTime.AddMonths(-analysisMonths)`.
- Filtering, interval calculation, and the "insufficient data" branch are unchanged in logic and signature.
- A unit test using `FakeTimeProvider` can verify that records exactly on the analysis window boundary are included/excluded correctly and that out-of-window records are filtered.

### FR-4: GetLastProductionDate is unchanged
`GetLastProductionDate` does not depend on current time and must remain functionally and structurally untouched in this change.

**Acceptance criteria:**
- The method signature, body, and logging statement are unchanged.
- Existing tests for `GetLastProductionDate` continue to pass without modification.

### FR-5: Update unit tests to use FakeTimeProvider
The test class `ProductionActivityAnalyzerTests` must construct the analyzer with a `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) so that all time-dependent assertions are deterministic.

**Acceptance criteria:**
- The test fixture instantiates `FakeTimeProvider` with a fixed UTC timestamp ("frozen now") shared across all tests in the class.
- Tests that previously used `DateTime.UtcNow.AddDays(...)` / `AddMonths(...)` to seed history records now compute dates relative to the frozen now exposed by the fake clock.
- At least one new boundary test is added per time-windowed method:
  - `IsInActiveProduction`: a record exactly at `cutoff - 1 day` is excluded; a record at exactly `cutoff` is included (or excluded — chosen behavior must match production code and be documented in the test name).
  - `CalculateAverageProductionFrequency`: a record exactly at `analysisStartDate` is included; a record at `analysisStartDate.AddTicks(-1)` is excluded.
- All previously existing assertions still pass.
- No test reads `DateTime.UtcNow` directly.

### FR-6: Preserve observable behavior in production
Behavior in production (under `TimeProvider.System`, which the DI container injects by default) must remain bit-for-bit equivalent to today's logic for any input where the wall clock advances negligibly during the call.

**Acceptance criteria:**
- Logging statements (debug-level cutoff date and frequency log lines) are unchanged in structure and format.
- Public interface `IProductionActivityAnalyzer` is unchanged.
- No new configuration keys, options, or feature flags are introduced.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. `TimeProvider.GetUtcNow()` is a single virtual call returning a `DateTimeOffset`; equivalent in cost to `DateTime.UtcNow` at production scale.

**Acceptance criteria:**
- No additional allocations per call beyond what `GetUtcNow().DateTime` already introduces (one struct copy).
- Method signatures and call sites in upstream callers (severity classification on the stock analysis dashboard) are unchanged.

### NFR-2: Testability & Determinism
After this change, tests for `ProductionActivityAnalyzer` must run identically on any wall-clock date and produce identical results.

**Acceptance criteria:**
- Running the test class on 2026-01-01 vs 2030-01-01 yields the same pass/fail outcome for every test.
- No test depends on the machine clock.

### NFR-3: Consistency with module conventions
The change must align with how every other time-aware handler in the Manufacture module accepts time.

**Acceptance criteria:**
- `TimeProvider` is injected as a constructor parameter, not via a static or service-locator pattern.
- The injected dependency is used everywhere `DateTime.UtcNow` previously appeared inside the class.

### NFR-4: Backward compatibility
No external API contract changes. Callers of `IProductionActivityAnalyzer` continue to compile and run unchanged.

**Acceptance criteria:**
- The interface `IProductionActivityAnalyzer` is unchanged.
- No call sites outside the class need modification.

## Data Model
No data model changes. The class continues to consume `IEnumerable<ManufactureHistoryRecord>` (with `Date` and `Amount` fields) and produce primitive results (`bool`, `DateTime?`, `double`).

## API / Interface Design

### Public interface (unchanged)
```csharp
public interface IProductionActivityAnalyzer
{
    bool IsInActiveProduction(IEnumerable<ManufactureHistoryRecord> manufactureHistory, int dayThreshold = 30);
    DateTime? GetLastProductionDate(IEnumerable<ManufactureHistoryRecord> manufactureHistory);
    double CalculateAverageProductionFrequency(IEnumerable<ManufactureHistoryRecord> manufactureHistory, int analysisMonths = 12);
}
```

### Constructor (changed)
```csharp
public ProductionActivityAnalyzer(
    ILogger<ProductionActivityAnalyzer> logger,
    TimeProvider timeProvider)
{
    _logger = logger;
    _timeProvider = timeProvider;
}
```

### Method bodies (changed lines)
```csharp
// IsInActiveProduction
var cutoffDate = _timeProvider.GetUtcNow().DateTime.AddDays(-dayThreshold);

// CalculateAverageProductionFrequency
var analysisStartDate = _timeProvider.GetUtcNow().DateTime.AddMonths(-analysisMonths);
```

### DI registration
No change. `IProductionActivityAnalyzer` registration in `ManufactureModule.AddManufactureModule` already resolves the new dependency because `TimeProvider` is registered as a singleton by the framework.

## Dependencies
- **Existing:** `Microsoft.Extensions.Logging`, `Anela.Heblo.Domain.Features.Manufacture` (for `ManufactureHistoryRecord`).
- **Existing framework:** `System.TimeProvider` (.NET 8).
- **Test-only addition (if not already referenced by the test project):** `Microsoft.Extensions.TimeProvider.Testing` for `FakeTimeProvider`.

## Out of Scope
- Any change to `IProductionActivityAnalyzer` (e.g. adding cancellation tokens, making methods async).
- Any change to `GetLastProductionDate` body or signature.
- Refactoring other Manufacture services or analyzers, even if they exhibit similar patterns.
- Changing severity-classification logic in the stock analysis dashboard or its handlers.
- Adding new public configuration / options.
- Renaming files, namespaces, or types.

## Open Questions
None.

## Status: COMPLETE