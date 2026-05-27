# GetProductMarginSummary — Margin Calculator Abstractions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract `IMarginCalculator` and `IMonthlyBreakdownGenerator` interfaces, relocate `MarginCalculator` from Domain to Application, and switch `GetProductMarginSummaryHandler` (and `MonthlyBreakdownGenerator`) to depend on those abstractions — aligning with the sibling pattern (`IProductFilterService`, `IReportBuilderService`).

**Architecture:** Pure structural refactor. No behavior change, no DTO change, no API change, no DB change. Interfaces and implementations are co-located in a single file each (matching the existing `ProductFilterService.cs` / `ReportBuilderService.cs` convention in the same folder). DI lifetimes stay `Scoped`. `MarginCalculationResult` remains in Domain — Application returning Domain-defined types is allowed under Clean Architecture.

**Tech Stack:** .NET 8, C#, MediatR, xUnit, Moq, FluentAssertions. No new NuGet packages.

---

## Context for the Implementer

### Why this refactor exists

`GetProductMarginSummaryHandler` currently injects two **concrete** classes:

1. `MarginCalculator` — located in the **Domain** project (`Anela.Heblo.Domain.Features.Analytics`), which violates Clean Architecture (no abstraction, Domain owns a stateless service that consumes a stream and returns a DTO — it isn't an entity or value object).
2. `MonthlyBreakdownGenerator` — already in Application but has no interface.

Two sibling services in the same `Services/` folder already follow the correct pattern (interface + implementation in one file, registered by interface). The two outliers were simply missed in a prior extraction. The `AnalyticsModule.cs` comment labeling them "Legacy services (keeping for backward compatibility)" is misleading — these are the **only** path, with no replacement.

### Why `MonthlyBreakdownGenerator`'s constructor MUST change

`MonthlyBreakdownGenerator` itself takes a concrete `MarginCalculator` in its constructor. Once DI registration moves to `services.AddScoped<IMarginCalculator, MarginCalculator>()`, the concrete `MarginCalculator` is no longer in the container as a self-registered type — the existing `ctor(MarginCalculator)` would fail to resolve at runtime. This is a correctness requirement, not a style preference. Task 3 handles it.

### Files involved (verified by solution-wide grep)

The only files that touch `MarginCalculator` / `MonthlyBreakdownGenerator` are:

- `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs` (will be **deleted**)
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` (modified)
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` (modified)
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` (modified)
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` (modified — adds mockability test)
- New: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` (created)

`SafeMarginCalculator` in the Catalog feature is **unrelated** — different type, different module. Do not touch it.

### Conventions to follow

- **Single-file co-location**: interface + implementation in one file, per `ProductFilterService.cs` and `ReportBuilderService.cs`.
- **Nullable reference types enabled**: project already configured; no change needed.
- **Service lifetimes stay `Scoped`**: NFR-1 forbids behavioral change.
- **No comment about "legacy"**: remove the misleading line in `AnalyticsModule.cs`.
- **Use `dotnet format`** between tasks if hooks require it; project uses `dotnet format` as part of the validation gate.

### How to run the tests

Run only the affected test file from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Run the full backend build:

```bash
dotnet build backend/backend.sln
```

Run a format check:

```bash
dotnet format backend/backend.sln --verify-no-changes
```

---

## File Structure

```
backend/src/Anela.Heblo.Application/Features/Analytics/
├── AnalyticsModule.cs                                          (MODIFIED — DI registrations)
├── UseCases/GetProductMarginSummary/
│   └── GetProductMarginSummaryHandler.cs                       (MODIFIED — inject interfaces)
└── Services/
    ├── MarginCalculator.cs                                     (CREATED — IMarginCalculator + impl)
    ├── MonthlyBreakdownGenerator.cs                            (MODIFIED — implements interface, depends on IMarginCalculator)
    ├── ProductFilterService.cs                                 (unchanged — reference pattern)
    └── ReportBuilderService.cs                                 (unchanged — reference pattern)

backend/src/Anela.Heblo.Domain/Features/Analytics/
├── AnalyticsProduct.cs                                         (unchanged — still owns MarginCalculationResult, DateRange)
├── AnalyticsProductType.cs                                     (unchanged)
├── ProductGroupingMode.cs                                      (unchanged)
└── MarginCalculator.cs                                         (DELETED)

backend/test/Anela.Heblo.Tests/Features/Analytics/
└── GetProductMarginSummaryHandlerTests.cs                      (MODIFIED — adds mockability test)
```

---

### Task 1: Relocate `MarginCalculator` from Domain to Application (no interface yet, just move)

**Goal:** Move the concrete class to its proper layer. Existing tests must still pass with concrete construction. This isolates the relocation from the interface change so each step is bisectable.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs`
- No source/test edits needed yet — namespace imports already cover both layers in every consumer (verified).

- [ ] **Step 1.1: Create the new file with the relocated class**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` with the **exact** body of the original (copy verbatim), only the namespace changes:

```csharp
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

/// <summary>
/// 🔒 PERFORMANCE FIX: Extracted margin calculation logic from handler
/// Implements single responsibility principle and improves testability
/// </summary>
public class MarginCalculator
{
    /// <summary>
    /// Calculates margin data using streaming approach to minimize memory usage
    /// </summary>
    public async Task<MarginCalculationResult> CalculateAsync(
        IAsyncEnumerable<AnalyticsProduct> products,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        string marginLevel = "M2",
        CancellationToken cancellationToken = default)
    {
        var groupTotals = new Dictionary<string, decimal>();
        var groupProducts = new Dictionary<string, List<AnalyticsProduct>>();
        var totalMargin = 0m;

        await foreach (var product in products.WithCancellation(cancellationToken))
        {
            if (product.MarginAmount <= 0)
                continue;

            var groupKey = GetGroupKey(product, groupingMode);

            // Calculate total units sold in the period
            var totalSold = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = (decimal)totalSold * GetMarginAmountForLevel(product, marginLevel);

            // Update group totals
            if (!groupTotals.ContainsKey(groupKey))
            {
                groupTotals[groupKey] = 0;
                groupProducts[groupKey] = new List<AnalyticsProduct>();
            }

            groupTotals[groupKey] += marginContribution;
            groupProducts[groupKey].Add(product);
            totalMargin += marginContribution;
        }

        return new MarginCalculationResult
        {
            GroupTotals = groupTotals,
            GroupProducts = groupProducts,
            TotalMargin = totalMargin
        };
    }

    /// <summary>
    /// Gets the group key based on grouping mode
    /// </summary>
    public string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => product.ProductCode,
            ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
            ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
            _ => product.ProductCode
        };
    }

    /// <summary>
    /// Gets display name for a group
    /// </summary>
    public string GetGroupDisplayName(string groupKey, ProductGroupingMode groupingMode, List<AnalyticsProduct> products)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => products.FirstOrDefault(p => p.ProductCode == groupKey)?.ProductName ?? groupKey,
            ProductGroupingMode.ProductFamily => $"Rodina {groupKey}",
            ProductGroupingMode.ProductCategory => $"Kategorie {groupKey}",
            _ => groupKey
        };
    }

    /// <summary>
    /// Gets the margin amount for a specific margin level
    /// </summary>
    public decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel)
    {
        return marginLevel.ToUpperInvariant() switch
        {
            "M0" => product.M0Amount,
            "M1" => product.M1Amount,
            "M2" => product.M2Amount,
            _ => product.M2Amount // Default to M2
        };
    }
}
```

- [ ] **Step 1.2: Delete the old Domain-layer file**

Delete `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs`.

After deletion, `backend/src/Anela.Heblo.Domain/Features/Analytics/` must contain only:
- `AnalyticsProduct.cs`
- `AnalyticsProductType.cs`
- `ProductGroupingMode.cs`

Verify with:

```bash
ls backend/src/Anela.Heblo.Domain/Features/Analytics/
```

Expected output:
```
AnalyticsProduct.cs
AnalyticsProductType.cs
ProductGroupingMode.cs
```

- [ ] **Step 1.3: Build the solution to verify type resolution**

Run:
```bash
dotnet build backend/backend.sln
```

Expected: **Build succeeded** with 0 errors and 0 new warnings.

Why this works without source edits: every consumer (`MonthlyBreakdownGenerator.cs`, `GetProductMarginSummaryHandler.cs`, `AnalyticsModule.cs`, `GetProductMarginSummaryHandlerTests.cs`) already has both `using Anela.Heblo.Application.Features.Analytics.Services;` and `using Anela.Heblo.Domain.Features.Analytics;` (or sits inside one of those namespaces). The unqualified `MarginCalculator` token now resolves to the new Application namespace because the Domain one is gone.

- [ ] **Step 1.4: Run the affected tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected: **All 5 existing test cases pass** (`Handle_ValidRequest_ReturnsCorrectResponse`, three `Handle_DifferentTimeWindows_ParsesCorrectly` theory rows, `Handle_EmptyProductList_ReturnsZeroMargin`).

- [ ] **Step 1.5: Format check**

Run:
```bash
dotnet format backend/backend.sln --verify-no-changes
```

If it reports diffs, run `dotnet format backend/backend.sln` to apply, then re-verify.

- [ ] **Step 1.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs \
        backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs
git commit -m "refactor: relocate MarginCalculator from Domain to Application layer"
```

---

### Task 2: Add `IMarginCalculator` interface alongside the relocated `MarginCalculator`

**Goal:** Expose the public surface as an interface in the same file. Class implements it. No behavior change.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`

- [ ] **Step 2.1: Add the interface and have the class implement it**

Replace the file content with the version below. The interface declares the four public methods currently consumed; the class body is unchanged.

```csharp
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

/// <summary>
/// 🔒 PERFORMANCE FIX: Extracted margin calculation logic from handler
/// Implements single responsibility principle and improves testability
/// </summary>
public class MarginCalculator : IMarginCalculator
{
    /// <summary>
    /// Calculates margin data using streaming approach to minimize memory usage
    /// </summary>
    public async Task<MarginCalculationResult> CalculateAsync(
        IAsyncEnumerable<AnalyticsProduct> products,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        string marginLevel = "M2",
        CancellationToken cancellationToken = default)
    {
        var groupTotals = new Dictionary<string, decimal>();
        var groupProducts = new Dictionary<string, List<AnalyticsProduct>>();
        var totalMargin = 0m;

        await foreach (var product in products.WithCancellation(cancellationToken))
        {
            if (product.MarginAmount <= 0)
                continue;

            var groupKey = GetGroupKey(product, groupingMode);

            // Calculate total units sold in the period
            var totalSold = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = (decimal)totalSold * GetMarginAmountForLevel(product, marginLevel);

            // Update group totals
            if (!groupTotals.ContainsKey(groupKey))
            {
                groupTotals[groupKey] = 0;
                groupProducts[groupKey] = new List<AnalyticsProduct>();
            }

            groupTotals[groupKey] += marginContribution;
            groupProducts[groupKey].Add(product);
            totalMargin += marginContribution;
        }

        return new MarginCalculationResult
        {
            GroupTotals = groupTotals,
            GroupProducts = groupProducts,
            TotalMargin = totalMargin
        };
    }

    /// <summary>
    /// Gets the group key based on grouping mode
    /// </summary>
    public string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => product.ProductCode,
            ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
            ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
            _ => product.ProductCode
        };
    }

    /// <summary>
    /// Gets display name for a group
    /// </summary>
    public string GetGroupDisplayName(string groupKey, ProductGroupingMode groupingMode, List<AnalyticsProduct> products)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => products.FirstOrDefault(p => p.ProductCode == groupKey)?.ProductName ?? groupKey,
            ProductGroupingMode.ProductFamily => $"Rodina {groupKey}",
            ProductGroupingMode.ProductCategory => $"Kategorie {groupKey}",
            _ => groupKey
        };
    }

    /// <summary>
    /// Gets the margin amount for a specific margin level
    /// </summary>
    public decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel)
    {
        return marginLevel.ToUpperInvariant() switch
        {
            "M0" => product.M0Amount,
            "M1" => product.M1Amount,
            "M2" => product.M2Amount,
            _ => product.M2Amount // Default to M2
        };
    }
}
```

- [ ] **Step 2.2: Build and test**

```bash
dotnet build backend/backend.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected: **Build succeeded**, **all 5 tests pass**.

- [ ] **Step 2.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs
git commit -m "refactor: introduce IMarginCalculator interface"
```

---

### Task 3: Add `IMonthlyBreakdownGenerator`, implement it, and switch its dependency to `IMarginCalculator`

**Goal:** Add the interface to the existing `MonthlyBreakdownGenerator.cs` file. Make the class implement it. **Crucial:** change the constructor parameter from concrete `MarginCalculator` to `IMarginCalculator` — Task 4 will swap the DI registration, after which the concrete self-registration is gone, so the existing concrete-typed constructor would break runtime resolution.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs`

- [ ] **Step 3.1: Add interface and update constructor**

Replace the file with the version below. Only three things change vs. the original:
1. A new `IMonthlyBreakdownGenerator` interface declared at the top of the file.
2. Class declaration changes to `public class MonthlyBreakdownGenerator : IMonthlyBreakdownGenerator`.
3. The field and constructor parameter types change from `MarginCalculator` to `IMarginCalculator`. Field name `_marginCalculator` preserved.

Everything else is byte-for-byte identical.

```csharp
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

/// <summary>
/// 🔒 PERFORMANCE FIX: Extracted monthly breakdown logic from handler
/// Implements single responsibility principle and reduces handler complexity
/// </summary>
public class MonthlyBreakdownGenerator : IMonthlyBreakdownGenerator
{
    private readonly IMarginCalculator _marginCalculator;

    public MonthlyBreakdownGenerator(IMarginCalculator marginCalculator)
    {
        _marginCalculator = marginCalculator;
    }

    /// <summary>
    /// Generates monthly breakdown efficiently by processing products once per month
    /// </summary>
    public List<MonthlyProductMarginDto> Generate(
        MarginCalculationResult calculationResult,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        string marginLevel = "M2")
    {
        var monthlyData = new List<MonthlyProductMarginDto>();

        // Generate all months in the date range
        var current = new DateTime(dateRange.FromDate.Year, dateRange.FromDate.Month, 1);
        var end = new DateTime(dateRange.ToDate.Year, dateRange.ToDate.Month, 1);

        while (current <= end)
        {
            var monthStart = current;
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var monthlySegments = GenerateMonthlySegments(
                calculationResult.GroupProducts,
                monthStart,
                monthEnd,
                groupingMode,
                marginLevel);

            monthlyData.Add(new MonthlyProductMarginDto
            {
                Year = current.Year,
                Month = current.Month,
                MonthDisplay = current.ToString("MMM yyyy", CultureInfo.CreateSpecificCulture("cs-CZ")),
                ProductSegments = monthlySegments.segments,
                TotalMonthMargin = monthlySegments.totalMargin
            });

            current = current.AddMonths(1);
        }

        return monthlyData;
    }

    /// <summary>
    /// Generates segments for a specific month, processing each group's products efficiently
    /// </summary>
    private (List<ProductMarginSegmentDto> segments, decimal totalMargin) GenerateMonthlySegments(
        Dictionary<string, List<AnalyticsProduct>> groupProducts,
        DateTime monthStart,
        DateTime monthEnd,
        ProductGroupingMode groupingMode,
        string marginLevel)
    {
        var segments = new List<ProductMarginSegmentDto>();
        var totalMonthMargin = 0m;

        foreach (var (groupKey, products) in groupProducts)
        {
            var groupData = ProcessGroupForMonth(products, monthStart, monthEnd, marginLevel);

            if (groupData.totalMargin <= 0)
                continue;

            totalMonthMargin += groupData.totalMargin;

            var displayName = _marginCalculator.GetGroupDisplayName(groupKey, groupingMode, products);

            segments.Add(new ProductMarginSegmentDto
            {
                GroupKey = groupKey,
                DisplayName = displayName,
                MarginContribution = groupData.totalMargin,
                ColorCode = "", // Color assigned by frontend
                AverageMarginPerPiece = groupData.avgMarginPerPiece,
                UnitsSold = groupData.totalUnitsSold,
                AverageSellingPriceWithoutVat = groupData.avgSellingPrice,
                AverageMaterialCosts = groupData.avgMaterialCosts,
                AverageLaborCosts = groupData.avgLaborCosts,
                ProductCount = groupData.productCount,
                IsOther = false
            });
        }

        // Sort by margin contribution (highest first)
        segments = segments.OrderByDescending(s => s.MarginContribution).ToList();

        // Calculate percentages
        if (totalMonthMargin > 0)
        {
            foreach (var segment in segments)
            {
                segment.Percentage = (segment.MarginContribution / totalMonthMargin) * 100;
            }
        }

        return (segments, totalMonthMargin);
    }

    /// <summary>
    /// Processes a group of products for a specific month
    /// </summary>
    private (decimal totalMargin, int totalUnitsSold, decimal avgMarginPerPiece,
             decimal avgSellingPrice, decimal avgMaterialCosts, decimal avgLaborCosts,
             int productCount) ProcessGroupForMonth(
        List<AnalyticsProduct> products,
        DateTime monthStart,
        DateTime monthEnd,
        string marginLevel)
    {
        var totalMargin = 0m;
        var totalUnitsSold = 0;
        var productCount = 0;
        var totalMarginPerPiece = 0m;
        var totalSellingPrice = 0m;
        var totalMaterialCosts = 0m;
        var totalLaborCosts = 0m;

        foreach (var product in products)
        {
            var salesInMonth = product.SalesHistory
                .Where(s => s.Date >= monthStart && s.Date <= monthEnd)
                .ToList();

            var marginAmount = _marginCalculator.GetMarginAmountForLevel(product, marginLevel);

            if (!salesInMonth.Any() || marginAmount <= 0)
                continue;

            var unitsSold = (int)salesInMonth.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = unitsSold * marginAmount;

            totalMargin += marginContribution;
            totalUnitsSold += unitsSold;
            productCount++;

            // Accumulate for averages
            totalMarginPerPiece += marginAmount;
            totalSellingPrice += product.EshopPriceWithoutVat ?? 0;
            totalMaterialCosts += product.MaterialCost;
            totalLaborCosts += product.HandlingCost;
        }

        return (
            totalMargin,
            totalUnitsSold,
            productCount > 0 ? totalMarginPerPiece / productCount : 0,
            productCount > 0 ? totalSellingPrice / productCount : 0,
            productCount > 0 ? totalMaterialCosts / productCount : 0,
            productCount > 0 ? totalLaborCosts / productCount : 0,
            productCount
        );
    }
}
```

- [ ] **Step 3.2: Build and test**

```bash
dotnet build backend/backend.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected: **Build succeeded** (the test file constructs `new MonthlyBreakdownGenerator(_marginCalculator)` where `_marginCalculator` is the concrete `MarginCalculator`; since `MarginCalculator` implements `IMarginCalculator`, this assignment is valid). **All 5 tests pass.**

- [ ] **Step 3.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs
git commit -m "refactor: introduce IMonthlyBreakdownGenerator, depend on IMarginCalculator"
```

---

### Task 4: Switch DI registrations to interface-based and drop the misleading comment

**Goal:** Register both services by their interfaces with `AddScoped` (preserving lifetime per NFR-1). Remove the "Legacy services" comment.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`

- [ ] **Step 4.1: Update the registration block**

Replace lines 36–38 of `AnalyticsModule.cs`:

**Before:**
```csharp
        // Legacy services (keeping for backward compatibility)
        services.AddScoped<MarginCalculator>();
        services.AddScoped<MonthlyBreakdownGenerator>();
```

**After:**
```csharp
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
```

Do not change any other registration in the file. The full updated file (for reference):

```csharp
using Anela.Heblo.Application.Features.Analytics.DashboardTiles;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// Enhanced analytics module with refactored services and validation
/// Registers new services for better separation of concerns and testability
/// </summary>
public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register repository
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // Register refactored services for clean separation of concerns
        // Note: IMarginCalculationService is registered by CatalogModule and injected here
        services.AddScoped<IProductFilterService, ProductFilterService>();
        services.AddScoped<IReportBuilderService, ReportBuilderService>();

        // Register validators for FluentValidation
        services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>();
        services.AddScoped<IValidator<GetProductMarginAnalysisRequest>, GetProductMarginAnalysisRequestValidator>();

        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();

        // Register dashboard tiles
        services.RegisterTile<InvoiceImportStatisticsTile>();

        return services;
    }
}
```

- [ ] **Step 4.2: Build**

```bash
dotnet build backend/backend.sln
```

Expected: **Build succeeded.** The handler still references the concrete classes (Task 5 fixes that), but since the concrete classes still exist and the test file uses them directly via `new`, the build remains green.

Note: at this checkpoint, **a runtime DI resolution of `GetProductMarginSummaryHandler` would fail** because the handler's constructor still asks for `MarginCalculator` (concrete) and `MonthlyBreakdownGenerator` (concrete), which are no longer self-registered. The fix lands in Task 5. Existing **unit tests** still pass because they wire the handler manually with `new`, not via DI.

- [ ] **Step 4.3: Run unit tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected: **All 5 tests pass.**

- [ ] **Step 4.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
git commit -m "refactor: register margin calculator services by interface"
```

---

### Task 5: Update `GetProductMarginSummaryHandler` to inject the interfaces

**Goal:** Switch handler fields and constructor parameters to interface types. Field names preserved. No behavioral change.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`

- [ ] **Step 5.1: Change field and constructor parameter types**

Apply this targeted edit to `GetProductMarginSummaryHandler.cs` lines 15–27.

**Before:**
```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        MarginCalculator marginCalculator,
        MonthlyBreakdownGenerator monthlyBreakdownGenerator)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
    }
```

**After:**
```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
    }
```

Do not touch any other line in the file. The `Handle` method, `GenerateTopProducts`, `CalculateGroupMarginData`, `ApplySorting`, and `CalculateTotalMarginForLevel` are unchanged — they call methods that exist on `IMarginCalculator` (`GetGroupDisplayName`, etc.) so the body still compiles.

- [ ] **Step 5.2: Build the full solution and run all backend tests**

```bash
dotnet build backend/backend.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected: **Build succeeded**, **all 5 tests pass.** (The existing tests pass `_marginCalculator` and `_monthlyBreakdownGenerator` to the constructor; both concrete fields satisfy the new interface parameters via implicit upcast.)

- [ ] **Step 5.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs
git commit -m "refactor: GetProductMarginSummaryHandler depends on margin calculator abstractions"
```

---

### Task 6: Add a mockability test demonstrating the handler is now testable in isolation (FR-8)

**Goal:** Add one new test that constructs `GetProductMarginSummaryHandler` with `Mock<IMarginCalculator>` and `Mock<IMonthlyBreakdownGenerator>` and asserts both mocks are invoked. This is the verification test for the refactor's success criterion. Existing tests that use the real `MarginCalculator` / `MonthlyBreakdownGenerator` stay as-is — they now serve as integration-style coverage against the real services.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

- [ ] **Step 6.1: Add the test method**

Insert this new test method into `GetProductMarginSummaryHandlerTests` (e.g. after `Handle_EmptyProductList_ReturnsZeroMargin`, before the closing brace of the class). It uses the existing mocking convention (`Moq`) and `FluentAssertions`, both already imported at the top of the file.

```csharp
    [Fact]
    public async Task Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator()
    {
        // Arrange
        var marginCalculatorMock = new Mock<IMarginCalculator>();
        var monthlyBreakdownGeneratorMock = new Mock<IMonthlyBreakdownGenerator>();

        var request = new GetProductMarginSummaryRequest
        {
            TimeWindow = "current-year",
            GroupingMode = ProductGroupingMode.Products,
            MarginLevel = "M2"
        };

        var today = DateTime.Today;
        var fromDate = new DateTime(today.Year, 1, 1);
        var toDate = today;

        var calculationResult = new MarginCalculationResult
        {
            GroupTotals = new Dictionary<string, decimal> { ["PROD001"] = 500m },
            GroupProducts = new Dictionary<string, List<AnalyticsProduct>>
            {
                ["PROD001"] = new List<AnalyticsProduct>
                {
                    new AnalyticsProduct
                    {
                        ProductCode = "PROD001",
                        ProductName = "Product 1",
                        Type = AnalyticsProductType.Product,
                        MarginAmount = 50m,
                        M0Amount = 50m,
                        M1Amount = 50m,
                        M2Amount = 50m,
                        SellingPrice = 100m,
                        PurchasePrice = 50m,
                        SalesHistory = new List<SalesDataPoint>
                        {
                            new() { Date = new DateTime(today.Year, 3, 1), AmountB2B = 5, AmountB2C = 5 }
                        }
                    }
                }
            },
            TotalMargin = 500m
        };

        var monthlyData = new List<MonthlyProductMarginDto>
        {
            new MonthlyProductMarginDto
            {
                Year = today.Year,
                Month = 3,
                MonthDisplay = "Mar",
                ProductSegments = new List<ProductMarginSegmentDto>(),
                TotalMonthMargin = 500m
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        marginCalculatorMock
            .Setup(x => x.CalculateAsync(
                It.IsAny<IAsyncEnumerable<AnalyticsProduct>>(),
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                "M2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(calculationResult);

        marginCalculatorMock
            .Setup(x => x.GetGroupDisplayName(
                It.IsAny<string>(),
                It.IsAny<ProductGroupingMode>(),
                It.IsAny<List<AnalyticsProduct>>()))
            .Returns<string, ProductGroupingMode, List<AnalyticsProduct>>((key, _, _) => key);

        monthlyBreakdownGeneratorMock
            .Setup(x => x.Generate(
                calculationResult,
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                "M2"))
            .Returns(monthlyData);

        var handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            marginCalculatorMock.Object,
            monthlyBreakdownGeneratorMock.Object);

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TotalMargin.Should().Be(500m);
        result.MonthlyData.Should().BeSameAs(monthlyData);

        marginCalculatorMock.Verify(
            x => x.CalculateAsync(
                It.IsAny<IAsyncEnumerable<AnalyticsProduct>>(),
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                "M2",
                It.IsAny<CancellationToken>()),
            Times.Once);

        monthlyBreakdownGeneratorMock.Verify(
            x => x.Generate(
                calculationResult,
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                "M2"),
            Times.Once);
    }
```

Notes for the implementer:
- `_analyticsRepositoryMock` is already a field on the test class — reuse it.
- The handler's `Handle` calls `_marginCalculator.GetGroupDisplayName` from inside `GenerateTopProducts` when iterating `calculationResult.GroupTotals`. The mock setup for `GetGroupDisplayName` above covers that call.
- `MarginLevel` default in `GetProductMarginSummaryRequest` is unknown at this layer; setting it explicitly to `"M2"` matches the test's mock setup. If `GetProductMarginSummaryRequest.MarginLevel` is non-nullable and has a different default, set it to whatever value the request uses by default — but match it in the `.Setup(...)` calls. (Check `GetProductMarginSummaryRequest.cs` in the same UseCases folder if uncertain.)

- [ ] **Step 6.2: Run the new test alone to confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator"
```

Expected: **1 test passed.**

- [ ] **Step 6.3: Run the full handler test class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected: **6 tests passed** (5 original + 1 new).

- [ ] **Step 6.4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs
git commit -m "test: add mockability test for GetProductMarginSummaryHandler"
```

---

### Task 7: Full verification gate

**Goal:** Confirm the refactor is complete, clean-architecture-compliant, and ready to ship. Verifies the CLAUDE.md completion gates (build + format + tests).

- [ ] **Step 7.1: Full backend build**

```bash
dotnet build backend/backend.sln
```

Expected: **Build succeeded. 0 Warning(s), 0 Error(s).** (Zero **new** warnings — pre-existing warnings unrelated to this change are tolerable but should not increase in count.)

- [ ] **Step 7.2: Format check**

```bash
dotnet format backend/backend.sln --verify-no-changes
```

Expected: **Format complete** (no diffs). If diffs are reported, run `dotnet format backend/backend.sln`, then re-verify, then amend the most recent commit only if the diffs are formatting-only.

- [ ] **Step 7.3: Full backend test run**

```bash
dotnet test backend/backend.sln
```

Expected: **All tests pass.** Pay attention to:
- All 6 `GetProductMarginSummaryHandlerTests` cases pass.
- No other test broke (cross-module impact should be zero — `SafeMarginCalculator` etc. are unrelated).

- [ ] **Step 7.4: Manual grep — confirm no concrete-type injections remain (FR-7)**

Run a grep for stale concrete references:

```bash
grep -rn "MarginCalculator " backend/src backend/test \
  | grep -vE "(IMarginCalculator|MarginCalculator\(|MarginCalculator\.cs|SafeMarginCalculator|IMarginCalculationService|MarginCalculationResult|MarginCalculationService)"
```

Expected output: **no lines**, OR only lines that are clearly:
- The interface↔class registration in `AnalyticsModule.cs` (e.g. `IMarginCalculator, MarginCalculator>`).
- The `new MarginCalculator()` construction in the existing test setup (`GetProductMarginSummaryHandlerTests.cs:32`).
- The `public class MarginCalculator : IMarginCalculator` declaration in the implementation file.

Anything else (e.g. a constructor parameter typed `MarginCalculator` somewhere) is a bug — fix before proceeding.

Same check for `MonthlyBreakdownGenerator`:

```bash
grep -rn "MonthlyBreakdownGenerator " backend/src backend/test \
  | grep -vE "(IMonthlyBreakdownGenerator|MonthlyBreakdownGenerator\(|MonthlyBreakdownGenerator\.cs)"
```

Expected output: **no lines**, OR only the registration and test instantiation.

- [ ] **Step 7.5: Confirm Domain folder is clean (FR-3 strengthened acceptance)**

```bash
ls backend/src/Anela.Heblo.Domain/Features/Analytics/
```

Expected output exactly:
```
AnalyticsProduct.cs
AnalyticsProductType.cs
ProductGroupingMode.cs
```

If `MarginCalculator.cs` still appears: it was not deleted in Task 1. Delete it now and amend the Task 1 commit only if no other tasks have been pushed.

- [ ] **Step 7.6: Final commit if any formatting changes accumulated**

If Steps 7.1–7.5 produced any auto-format diffs:

```bash
git status
# If there are formatting-only diffs:
git add -A
git commit -m "chore: dotnet format pass"
```

If `git status` is clean, skip this step.

---

## Spec Coverage Check

| Requirement | Covered by |
|---|---|
| FR-1: Define `IMarginCalculator` interface | Task 2 |
| FR-2: Define `IMonthlyBreakdownGenerator` interface | Task 3 |
| FR-3: Relocate `MarginCalculator` to Application layer | Task 1 (+ Task 7.5 verification) |
| FR-4: `MonthlyBreakdownGenerator` implements `IMonthlyBreakdownGenerator` | Task 3 |
| FR-4b (arch-review amendment): `MonthlyBreakdownGenerator` ctor depends on `IMarginCalculator` | Task 3 |
| FR-5: Update DI registrations + remove misleading comment | Task 4 |
| FR-6: Handler injects interfaces | Task 5 |
| FR-7: Update other concrete consumers (none expected) | Task 7.4 grep verification |
| FR-8: Existing tests pass + mockability test added | Task 6 |
| NFR-1: No performance change (same lifetimes preserved) | Task 4 (uses `AddScoped`) |
| NFR-2: No security change | n/a — no surface change |
| NFR-3: Backwards compatible (no API/DTO/DB changes) | n/a — not touched |
| NFR-4: Build clean, format clean, tests pass | Task 7 gates |

Arch-review amendments incorporated:
1. ✅ `MonthlyBreakdownGenerator` constructor switch made explicit (Task 3).
2. ✅ Single-file interface co-location (Tasks 2, 3 — both interfaces declared in the same file as their implementations).
3. ✅ Explicit `CalculateAsync` signature (Task 2).
4. ✅ `MarginCalculationResult` stays in Domain (verified — no task moves it).
5. ✅ Post-state of Domain folder verified explicitly (Task 7.5).
6. ✅ Mockability test uses `Mock<IMarginCalculator>` and `Mock<IMonthlyBreakdownGenerator>` with invocation verification (Task 6).
