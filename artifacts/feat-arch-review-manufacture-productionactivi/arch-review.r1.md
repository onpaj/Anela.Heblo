# Architecture Review: TimeProvider Injection in ProductionActivityAnalyzer

## Skip Design: true

## Architectural Fit Assessment

This change is a pure backend refactor that brings `ProductionActivityAnalyzer` into alignment with an already-established module convention. Verified facts:

- `TimeProvider.System` is registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:128` — no additional DI wiring is required.
- 15 handlers/services in `backend/src/Anela.Heblo.Application/Features/Manufacture` already inject `TimeProvider` as a constructor parameter (e.g. `SubmitManufactureHandler.cs:17,25`). The proposed constructor shape `(ILogger<T> logger, TimeProvider timeProvider)` matches the dominant ordering used in the module.
- `IProductionActivityAnalyzer` is registered scoped in `ManufactureModule.cs:38`; resolution will pick up the singleton `TimeProvider` without code change.
- The interface in `Anela.Heblo.Domain.Features.Manufacture` is **not** modified — no Domain layer changes, so Clean Architecture boundaries remain intact (Application depends on Domain, Domain has no time concern).

**Main integration point:** `ProductionActivityAnalyzer` is consumed by `ManufactureSeverityCalculator` (and indirectly by stock analysis dashboard handlers). No consumer signature changes — they continue to call interface methods unchanged.

**Architectural verdict:** The change is mechanical, low-risk, conformant. Approve as specified.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.API (composition root)
   └── AddSingleton(TimeProvider.System)            [unchanged]

Anela.Heblo.Application.Features.Manufacture
   └── ManufactureModule.AddManufactureModule(...)  [unchanged]
        └── AddScoped<IProductionActivityAnalyzer, ProductionActivityAnalyzer>

   Services/
      ProductionActivityAnalyzer  [MODIFIED]
         ├── ctor(ILogger, TimeProvider)            ← was (ILogger)
         ├── IsInActiveProduction(...)              ← _timeProvider.GetUtcNow().DateTime
         ├── GetLastProductionDate(...)             ← unchanged
         └── CalculateAverageProductionFrequency(...) ← _timeProvider.GetUtcNow().DateTime

Anela.Heblo.Tests.Features.Manufacture.Services
   └── ProductionActivityAnalyzerTests             [MODIFIED]
        └── uses fake clock at frozen "now"
```

No new files. No new interfaces. No DI registration changes.

### Key Design Decisions

#### Decision 1: Constructor ordering — logger first, TimeProvider second
**Options considered:**
1. `(ILogger, TimeProvider)` — matches `SubmitManufactureHandler` and most peer handlers.
2. `(TimeProvider, ILogger)` — places infrastructure dependency first.

**Chosen approach:** Option 1.
**Rationale:** Module-wide convention places `ILogger<T>` first across the existing 15 TimeProvider-injecting handlers. Consistency wins over abstract ordering preferences.

#### Decision 2: `GetUtcNow().DateTime` rather than `GetUtcNow().UtcDateTime`
**Options considered:**
1. `_timeProvider.GetUtcNow().DateTime` — returns the `DateTime` portion as `DateTimeKind.Unspecified`.
2. `_timeProvider.GetUtcNow().UtcDateTime` — returns `DateTime` with `DateTimeKind.Utc`.

**Chosen approach:** Option 1 (as in spec).
**Rationale:** Replaces `DateTime.UtcNow` (which produces `Kind=Utc`). However, the existing code compares `m.Date` (a `DateTime` whose `Kind` is whatever the persistence layer hydrates — typically `Unspecified` for PostgreSQL `timestamp without time zone`). Using `.DateTime` matches the comparison semantics already in production and preserves bit-for-bit behavior (FR-6). **Note: see Risk R-1** — if `m.Date` is `Utc`-kinded, neither variant changes comparison correctness (DateTime comparison ignores Kind), so the choice is harmless. Spec wording is correct.

#### Decision 3: Test fake clock — package vs. local subclass
**Options considered:**
1. Add `Microsoft.Extensions.TimeProvider.Testing` package to `Anela.Heblo.Tests.csproj` and use `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (as the spec suggests).
2. Reuse the local `FakeTimeProvider` subclass pattern already used in `backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackingDashboardHandlerTests.cs:18` (custom nested subclass).
3. Define a single shared `FakeTimeProvider` test helper in the test project.

**Chosen approach:** Option 1 — add the Microsoft package.
**Rationale:** The test project currently has **zero** reference to `Microsoft.Extensions.TimeProvider.Testing` (verified — `Anela.Heblo.Tests.csproj` package list). The packaging-dashboard test rolled its own subclass for a specific reason (`GetLocalNow()` is non-virtual and the test needed a fake local time-zone behavior). The `ProductionActivityAnalyzer` only needs `GetUtcNow()`, which is virtual and trivially overridable — but using the Microsoft-supported `FakeTimeProvider` is the canonical .NET pattern, removes boilerplate, and adds the standard `Advance()` / `SetUtcNow()` ergonomics that future tests in this class will benefit from. The package is part of the official `Microsoft.Extensions.*` family and is the documented testing companion to .NET 8 `TimeProvider`.

**Implication for spec:** the test project must add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.x" />` to `Anela.Heblo.Tests.csproj`. The spec already notes this conditionally — make it unconditional.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify in place:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs` | Add `TimeProvider` ctor param + field; replace two `DateTime.UtcNow` references. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ProductionActivityAnalyzerTests.cs` | Switch to `FakeTimeProvider`; rewrite relative-date seeds to use frozen now; add boundary tests. |
| `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` | Add `Microsoft.Extensions.TimeProvider.Testing` package reference. |

No changes to `ManufactureModule.cs`, no changes to `IProductionActivityAnalyzer`, no changes to any consumer.

### Interfaces and Contracts

**Unchanged public contract:**
```csharp
public interface IProductionActivityAnalyzer
{
    bool IsInActiveProduction(IEnumerable<ManufactureHistoryRecord> manufactureHistory, int dayThreshold = 30);
    DateTime? GetLastProductionDate(IEnumerable<ManufactureHistoryRecord> manufactureHistory);
    double CalculateAverageProductionFrequency(IEnumerable<ManufactureHistoryRecord> manufactureHistory, int analysisMonths = 12);
}
```

**Constructor contract (new):**
```csharp
public ProductionActivityAnalyzer(
    ILogger<ProductionActivityAnalyzer> logger,
    TimeProvider timeProvider);
```

**Test fixture contract (recommended):**
```csharp
private static readonly DateTime FrozenNowUtc = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(FrozenNowUtc));
// ctor: _analyzer = new ProductionActivityAnalyzer(_loggerMock.Object, _timeProvider);
```

### Data Flow

For `IsInActiveProduction`:
```
caller → IsInActiveProduction(history, dayThreshold)
   → cutoffDate = _timeProvider.GetUtcNow().DateTime.AddDays(-dayThreshold)
   → history.Any(m.Date >= cutoffDate && m.Amount > 0)
   → bool
```

For `CalculateAverageProductionFrequency`:
```
caller → CalculateAverageProductionFrequency(history, analysisMonths)
   → analysisStartDate = _timeProvider.GetUtcNow().DateTime.AddMonths(-analysisMonths)
   → history.Where(Date >= analysisStartDate && Amount > 0).Select(Date).OrderBy
   → if count < 2: PositiveInfinity
   → otherwise: average of consecutive intervals (TotalDays)
```

No call-site changes.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **R-1.** `DateTimeKind` mismatch between `_timeProvider.GetUtcNow().DateTime` (`Unspecified`) and previously `DateTime.UtcNow` (`Utc`) could subtly affect downstream code that inspects `Kind`. | Low | DateTime arithmetic and comparison operators ignore `Kind`. `cutoffDate` and `analysisStartDate` are only used in `>=` comparisons against `m.Date`. Verified: no consumer of the local variables inspects `Kind`. No mitigation needed beyond the existing FR-6 acceptance criterion (observable behavior unchanged). |
| **R-2.** Boundary semantics in new tests must match production code exactly. Spec FR-5 acceptance leaves "record at exactly `cutoff` included or excluded" as a documented choice. | Low | Production code uses `m.Date >= cutoffDate` — equality is **included**. Tests must encode this explicitly with test names like `IsInActiveProduction_WithRecordExactlyAtCutoff_ReturnsTrue`. Reject any reviewer suggestion to change the comparison operator. |
| **R-3.** Adding `Microsoft.Extensions.TimeProvider.Testing` may collide with the locally-defined `FakeTimeProvider` in `GetPackingDashboardHandlerTests`. | Very Low | The local subclass is `private sealed` and namespace-scoped to the test class — no collision. If a future refactor unifies them, that's out of scope. |
| **R-4.** A future writer of additional `ProductionActivityAnalyzer` methods may again reach for `DateTime.UtcNow`. | Low | Convention is now explicit and matches the rest of the module. No structural mitigation required; code review enforces. |
| **R-5.** Test project version of `Microsoft.Extensions.TimeProvider.Testing` must be compatible with .NET 8. | Low | Use version `8.x` (e.g. `8.0.0` or `8.0.1`); avoid 9.x to match the test SDK targeting `net8.0` (`<TargetFramework>net8.0</TargetFramework>` confirmed in csproj). |

## Specification Amendments

1. **Make the test package reference unconditional.** Spec currently says "Test-only addition (if not already referenced)". Verified: it is **not** referenced. The implementation must add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.0.*" />` to `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`. Pin to the `8.0.x` major to align with the project's `net8.0` target.

2. **Fully qualify the test type to avoid collision.** A `FakeTimeProvider` subclass already exists in the same test assembly (`GetPackingDashboardHandlerTests.FakeTimeProvider`). The new tests should import `Microsoft.Extensions.Time.Testing.FakeTimeProvider` explicitly or use the fully-qualified type name on first reference to keep the reader's intent unambiguous.

3. **Document boundary semantics in test names.** FR-5 leaves the equality case as "chosen behavior must match production code." Pin the choice: `m.Date >= cutoffDate` means the boundary is **inclusive**. Test names should reflect that (e.g. `IsInActiveProduction_RecordExactlyAtCutoff_IsConsideredActive`).

4. **Clarify FR-3 boundary test wording.** Spec says: "a record exactly at `analysisStartDate` is included; a record at `analysisStartDate.AddTicks(-1)` is excluded." This is consistent with `m.Date >= analysisStartDate`. Keep the wording — no change beyond confirming it.

5. **Add an explicit assertion in FR-2 / FR-3 acceptance: log lines must continue to emit the cutoff/analysis date** (currently included in `_logger.LogDebug` calls). FR-6 covers this implicitly; making it explicit prevents accidental log-line removal during refactor.

## Prerequisites

None blocking. All prerequisites are already met in the codebase:

- ✅ `TimeProvider.System` registered as singleton (`ServiceCollectionExtensions.cs:127-128`).
- ✅ `IProductionActivityAnalyzer` registered scoped (`ManufactureModule.cs:38`).
- ✅ .NET 8 (provides `System.TimeProvider`).
- ✅ Convention precedent established across 15 peer files in the Manufacture module.

**One implementation prerequisite** (not infrastructure):
- Add `Microsoft.Extensions.TimeProvider.Testing` 8.0.x package reference to `Anela.Heblo.Tests.csproj` **before** updating test code, so the test project compiles incrementally.