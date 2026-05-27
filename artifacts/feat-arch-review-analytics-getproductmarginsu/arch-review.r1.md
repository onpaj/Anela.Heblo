# Architecture Review: Refactor GetProductMarginSummary Handler to Depend on Abstractions

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns cleanly with the codebase's established patterns. Verification of the actual code confirms:

- **Clean Architecture violation is real.** `MarginCalculator` lives in `Anela.Heblo.Domain.Features.Analytics` (`backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs:1`). It contains no entity/value-object semantics — it is a stateless service that consumes `IAsyncEnumerable<AnalyticsProduct>` and produces a result DTO. It belongs in Application.
- **Sibling pattern is well-established.** `IProductFilterService`/`ProductFilterService` and `IReportBuilderService`/`ReportBuilderService` both live in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/` and are registered by interface in `AnalyticsModule.cs`. The proposed change brings `MarginCalculator` and `MonthlyBreakdownGenerator` into compliance with this pattern.
- **One missing dependency edge in spec.** `MonthlyBreakdownGenerator` itself injects the concrete `MarginCalculator` (`MonthlyBreakdownGenerator.cs:13-18`). The spec's FR-4 says "no behavioural changes — only the implementation declaration is added," but once `MarginCalculator` is registered only by interface (FR-5), the existing concrete-constructor dependency in `MonthlyBreakdownGenerator` will fail to resolve at runtime unless its constructor is also updated to depend on `IMarginCalculator`. This is a required, not optional, change.
- **Cross-module impact is zero.** A solution-wide search confirms only `GetProductMarginSummaryHandler`, `MonthlyBreakdownGenerator`, `AnalyticsModule`, and `GetProductMarginSummaryHandlerTests` reference the two classes. The unrelated `SafeMarginCalculator` / `IMarginCalculationService` in the Catalog feature are distinct and untouched.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application
└── Features/Analytics/
    ├── Services/
    │   ├── IMarginCalculator.cs            (NEW — interface + impl, single file)
    │   ├── MarginCalculator.cs             (RELOCATED from Domain)
    │   ├── IMonthlyBreakdownGenerator.cs   (NEW — interface + impl, single file)
    │   └── MonthlyBreakdownGenerator.cs    (UPDATED: implements interface,
    │                                                 depends on IMarginCalculator)
    ├── UseCases/GetProductMarginSummary/
    │   └── GetProductMarginSummaryHandler.cs
    │       └── ctor(IAnalyticsRepository, IMarginCalculator, IMonthlyBreakdownGenerator)
    └── AnalyticsModule.cs
        └── services.AddScoped<IMarginCalculator, MarginCalculator>()
        └── services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>()

Anela.Heblo.Domain
└── Features/Analytics/
    ├── AnalyticsProduct.cs                 (unchanged — also defines MarginCalculationResult)
    ├── AnalyticsProductType.cs             (unchanged)
    └── ProductGroupingMode.cs              (unchanged)
    [MarginCalculator.cs REMOVED]
```

`MarginCalculationResult` and `DateRange` remain in Domain. The Application interface returns a Domain-defined type — this is acceptable under Clean Architecture (Application depends on Domain).

### Key Design Decisions

#### Decision 1: Interface co-location vs. separate file
**Options considered:**
1. Spec's approach — interface in its own file (`IMarginCalculator.cs`) separate from `MarginCalculator.cs`.
2. Match sibling convention — interface and implementation in the same file (`MarginCalculator.cs`), as `ProductFilterService.cs` and `ReportBuilderService.cs` already do.

**Chosen approach:** Option 2. Place `IMarginCalculator` and `MarginCalculator` in the same file (`MarginCalculator.cs`); same for `IMonthlyBreakdownGenerator` and `MonthlyBreakdownGenerator`.

**Rationale:** The two sibling services in the same `Services/` folder both use single-file co-location. The whole point of this refactor is to bring the two outlier classes in line with the established pattern — adopting a *different* file structure for them would re-create inconsistency in a refactor whose explicit goal is consistency. This is a spec amendment.

#### Decision 2: Where `MarginCalculationResult` lives
**Options considered:**
1. Leave `MarginCalculationResult` in Domain (`AnalyticsProduct.cs:59`).
2. Move it to Application alongside the relocated `MarginCalculator`.

**Chosen approach:** Option 1 — leave it in Domain.

**Rationale:** The spec annotates `MarginCalculationResult` as "(Application layer DTO)" but verification shows it currently lives in Domain. Moving it expands the refactor's blast radius and creates churn for an isolated structural goal. Application-layer code returning Domain-defined types is allowed under Clean Architecture. The spec's annotation is incorrect and should be amended (see Specification Amendments).

#### Decision 3: `MonthlyBreakdownGenerator` constructor change
**Options considered:**
1. Leave `MonthlyBreakdownGenerator`'s constructor untouched (spec FR-4 as written).
2. Update it to inject `IMarginCalculator` instead of the concrete `MarginCalculator`.

**Chosen approach:** Option 2 — required, not optional.

**Rationale:** Once `services.AddScoped<IMarginCalculator, MarginCalculator>()` replaces `services.AddScoped<MarginCalculator>()`, the concrete `MarginCalculator` is no longer in the DI container. `MonthlyBreakdownGenerator`'s current `ctor(MarginCalculator)` would fail to resolve at runtime. This is a correctness issue, not a style preference.

#### Decision 4: Exact `CalculateAsync` signature on the interface
**Options considered:** Preserve the existing signature verbatim, or trim.

**Chosen approach:** Preserve verbatim:
```csharp
Task<MarginCalculationResult> CalculateAsync(
    IAsyncEnumerable<AnalyticsProduct> products,
    DateRange dateRange,
    ProductGroupingMode groupingMode,
    string marginLevel = "M2",
    CancellationToken cancellationToken = default);
```

**Rationale:** The spec uses a placeholder. The handler's call site at `GetProductMarginSummaryHandler.cs:40` passes all five arguments; any divergence breaks compilation. Note: `dateRange` is unused inside `MarginCalculator.CalculateAsync` today — leave that alone; it's out of scope.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Analytics/Services/
├── MarginCalculator.cs               (contains IMarginCalculator + MarginCalculator)
├── MonthlyBreakdownGenerator.cs      (contains IMonthlyBreakdownGenerator + MonthlyBreakdownGenerator)
├── ProductFilterService.cs           (unchanged — reference pattern)
├── ReportBuilderService.cs           (unchanged — reference pattern)
└── TimeWindowParser.cs               (unchanged)

DELETE: backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs
```

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IMarginCalculator
{
    Task<MarginCalculationResult> CalculateAsync(
        IAsyncEnumerable<AnalyticsProduct> products,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        string marginLevel = "M2",
        CancellationToken cancellationToken = default);

    string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode);

    string GetGroupDisplayName(
        string groupKey,
        ProductGroupingMode groupingMode,
        List<AnalyticsProduct> products);

    decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel);
}

public class MarginCalculator : IMarginCalculator { /* body unchanged */ }
```

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs
using System.Globalization;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IMonthlyBreakdownGenerator
{
    List<MonthlyProductMarginDto> Generate(
        MarginCalculationResult calculationResult,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        string marginLevel = "M2");
}

public class MonthlyBreakdownGenerator : IMonthlyBreakdownGenerator
{
    private readonly IMarginCalculator _marginCalculator;   // interface, not concrete

    public MonthlyBreakdownGenerator(IMarginCalculator marginCalculator)
    {
        _marginCalculator = marginCalculator;
    }
    // remaining body unchanged
}
```

`AnalyticsModule.cs` lines 36–38 replaced with:
```csharp
services.AddScoped<IMarginCalculator, MarginCalculator>();
services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
```
Misleading "Legacy services" comment removed.

### Data Flow
Unchanged. The handler streams products from `IAnalyticsRepository`, hands them to `IMarginCalculator.CalculateAsync` (streaming aggregation), then `IMonthlyBreakdownGenerator.Generate` materializes the monthly view from the already-aggregated result. Both calls are now through abstractions; runtime behavior is identical.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `MonthlyBreakdownGenerator` constructor not updated → DI resolution failure at runtime | HIGH | Make the update explicit (Decision 3). Add a smoke test or integration test that resolves `IMonthlyBreakdownGenerator` from the container. |
| `MarginCalculationResult` mis-located by spec annotation, leading implementer to move the type unnecessarily | MEDIUM | Spec amendment clarifies the type stays in Domain. |
| Other callers of concrete classes outside the searched scope | LOW | Solution-wide grep performed: only the four expected sites match. FR-7 already covers this; PR description should confirm "no other callers found." |
| Tests still construct concrete `new MarginCalculator()` / `new MonthlyBreakdownGenerator(...)` | LOW | Existing test in `GetProductMarginSummaryHandlerTests.cs:25-33` continues to compile (concrete class still exists, just relocated). New mockability test (FR-8) is additive. |
| Service lifetime drift | LOW | Spec NFR mandates preserving `Scoped`. Both new registrations must use `AddScoped`. |
| Domain assembly metadata change due to file removal (e.g., consumers reflecting over types) | LOW | No reflection-based consumers found; safe to remove. |

## Specification Amendments

1. **FR-2 wording — append**: `MonthlyBreakdownGenerator`'s constructor must be updated to depend on `IMarginCalculator` rather than the concrete `MarginCalculator`, because the concrete type will no longer be registered in DI after FR-5. This is required for correctness, not stylistic — treat it as part of FR-4 or as a new FR-4b. Add as acceptance criterion: "After the change, `MonthlyBreakdownGenerator` has no compile-time reference to the concrete `MarginCalculator` class."

2. **FR-1/FR-2 file layout**: Change "Create a new interface `IMarginCalculator` in `…/IMarginCalculator.cs`" to "Define `IMarginCalculator` alongside the `MarginCalculator` implementation in `…/MarginCalculator.cs`" (and analogously for `IMonthlyBreakdownGenerator`). Rationale: matches the existing `ProductFilterService.cs` / `ReportBuilderService.cs` convention in the same folder.

3. **FR-1 `CalculateAsync` signature**: Replace the placeholder `/* current parameter list preserved verbatim */` with the explicit signature:
   ```csharp
   Task<MarginCalculationResult> CalculateAsync(
       IAsyncEnumerable<AnalyticsProduct> products,
       DateRange dateRange,
       ProductGroupingMode groupingMode,
       string marginLevel = "M2",
       CancellationToken cancellationToken = default);
   ```

4. **Data Model annotation correction**: `MarginCalculationResult` is currently defined in **Domain** (`backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs:59`), not Application. The spec's "(Application layer DTO)" tag is incorrect. The refactor must **not** move this type — it stays in Domain. Update the Data Model section accordingly.

5. **FR-3 acceptance criterion — strengthen**: Add "After deletion, `backend/src/Anela.Heblo.Domain/Features/Analytics/` contains only `AnalyticsProduct.cs`, `AnalyticsProductType.cs`, and `ProductGroupingMode.cs`." This makes the post-state explicit and verifiable.

6. **FR-8 — be specific about the mockability test**: Add an acceptance criterion that the new test instantiates `GetProductMarginSummaryHandler` with `Mock<IMarginCalculator>` and `Mock<IMonthlyBreakdownGenerator>` (project uses Moq — confirmed via existing `GetProductMarginSummaryHandlerTests` imports) and asserts both mocks are invoked with the expected arguments for one representative request. Existing handler tests that use the real `MarginCalculator`/`MonthlyBreakdownGenerator` are fine to keep as-is — they become integration-style tests against the real services.

## Prerequisites

None. The refactor is self-contained:
- No DB migration.
- No config change.
- No new NuGet packages.
- No infrastructure or environment change.
- The Domain project's existing references (used by the unchanged entities/enums) remain valid; nothing needs to be added to or removed from Domain's `.csproj`.

Implementation can begin immediately.