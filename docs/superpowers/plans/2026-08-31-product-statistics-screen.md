# Product Statistics Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `/products/statistics` screen where the user selects up to 10 products and a month range, and sees — per metric tab (Prodeje / Nákupy / Spotřeba / Výroba) — one chart line per product plus a month-by-month table.

**Architecture:** One new read-only MediatR vertical slice (`GetProductStatistics`) projects the in-memory catalog cache into a dense month series per product, served from the existing `CatalogController`. The frontend adds one page with four tabs sharing a single filter, a multi-series chart.js line chart, and a totals table. Nothing existing changes behavior; the only modified shared component is `CatalogAutocomplete`, which gains an additive `isMulti` mode.

**Tech Stack:** .NET 8, MediatR, FluentValidation, AutoMapper (not needed here), xUnit + Moq / FluentAssertions; React 18, TypeScript, React Query (`@tanstack/react-query`), react-select, chart.js + react-chartjs-2, Tailwind, `react-scripts test`.

**Spec:** `docs/superpowers/specs/2026-08-31-product-statistics-screen-design.md`

## Global Constraints

- **DTOs are classes, never C# records.** The OpenAPI client generator mishandles record parameter order.
- **Every Application `*Response` must inherit `Anela.Heblo.Application.Shared.BaseResponse`.** A reflection contract test fails in CI otherwise.
- **Validators are registered manually.** There is no `AddValidatorsFromAssembly` in this project. Both the `IValidator<TRequest>` and the `IPipelineBehavior<TRequest, TResponse> → ValidationBehavior<,>` must be added in `CatalogModule`, or validation silently never runs.
- **The generated TypeScript client throws on non-200.** `if (!response.success)` branches are dead code. Handle errors via React Query `error` / caught `SwaggerException`.
- **Frontend test runner is `react-scripts test`**, never `npx jest` (TS parse errors).
- **Frontend build gate is `CI=false npm run build`**, never `npx tsc --noEmit` (it false-greens in this repo).
- **Months are `"yyyy-MM"` strings** everywhere in this feature — request, response, and frontend state. Never `DateTime`.
- **Max 10 products** per request; **history floor is `CatalogConstants.HISTORY_FLOOR_DATE`** (2020-01-01).
- **UI copy is Czech.**
- Backend validation commands: `dotnet build` and `dotnet format` from the repo root (`Anela.Heblo.sln`).
- When running `dotnet test` concurrently with other worktrees, build first, then run with `--no-build -p:UseSharedCompilation=false`.

## File Structure

**New — backend** (all under `backend/src/Anela.Heblo.Application/Features/Catalog/`)

| File | Responsibility |
|---|---|
| `Contracts/ProductStatisticsMetric.cs` | The four-value metric enum |
| `Contracts/ProductStatisticsSeriesDto.cs` | One product's name + index-aligned values |
| `UseCases/GetProductStatistics/GetProductStatisticsRequest.cs` | Query inputs |
| `UseCases/GetProductStatistics/GetProductStatisticsResponse.cs` | `Months` + `Products` |
| `UseCases/GetProductStatistics/MonthRange.cs` | Parse/expand `"yyyy-MM"` range — the only place month-string math lives |
| `UseCases/GetProductStatistics/GetProductStatisticsHandler.cs` | Fetch aggregates, project one metric onto the dense month list |
| `Validators/GetProductStatisticsRequestValidator.cs` | Input rules |

**New — frontend**

| File | Responsibility |
|---|---|
| `src/api/hooks/useProductStatistics.ts` | React Query wrapper over the generated client method |
| `src/components/product-statistics/productStatisticsColors.ts` | Index-keyed palette |
| `src/components/product-statistics/ProductStatisticsFilter.tsx` | Multi-product picker + two month inputs |
| `src/components/product-statistics/ProductStatisticsChart.tsx` | chart.js `<Line>`, one dataset per product |
| `src/components/product-statistics/ProductStatisticsTable.tsx` | Months = rows, products = columns, totals both ways |
| `src/components/pages/ProductStatistics.tsx` | Page shell: owns filter + tab state, wires the three children |

**Modified**

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs` | `GET product-statistics` endpoint |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | Validator + `ValidationBehavior` registration |
| `frontend/src/components/common/CatalogAutocomplete.tsx` | Additive `isMulti` mode |
| `frontend/src/App.tsx` | Route + lazy import |
| `frontend/src/components/layout/Sidebar.tsx` | Menu item under Produkty |
| `access-matrix.json` | Route → `Products_Catalog` Read |

**Task order rationale:** Tasks 1–4 build the backend bottom-up (pure month math → contracts → handler → validator), each independently testable with no frontend dependency. Task 5 exposes it and regenerates the client, which is what unblocks the frontend. Tasks 6–10 build the frontend leaf-first (colors → picker → chart → table → page), so every component has its dependencies in place. Task 11 wires routing and access last, when there is a page to route to.

---

### Task 1: Month range expansion

The one piece of month-string arithmetic in the feature. Isolated so the handler never does string math.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/MonthRange.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/MonthRangeTests.cs`

**Interfaces:**
- Consumes: `CatalogConstants.HISTORY_FLOOR_DATE` (existing, `= new DateTime(2020, 1, 1)`)
- Produces:
  - `static bool MonthRange.TryParse(string month, out int year, out int monthNumber)`
  - `static List<string> MonthRange.Expand(string dateFrom, string dateTo)` — ascending, inclusive of both ends, floored at `HISTORY_FLOOR_DATE`
  - `static string MonthRange.Key(int year, int month)` — `$"{year:D4}-{month:D2}"`
  - `static string MonthRange.Key(DateTime date)`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/MonthRangeTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class MonthRangeTests
{
    [Fact]
    public void Expand_SingleMonth_ReturnsThatMonthOnly()
    {
        var result = MonthRange.Expand("2025-03", "2025-03");

        result.Should().Equal("2025-03");
    }

    [Fact]
    public void Expand_RangeWithinOneYear_IsAscendingAndInclusive()
    {
        var result = MonthRange.Expand("2025-01", "2025-04");

        result.Should().Equal("2025-01", "2025-02", "2025-03", "2025-04");
    }

    [Fact]
    public void Expand_RangeCrossingYearBoundary_RollsOverCorrectly()
    {
        var result = MonthRange.Expand("2024-11", "2025-02");

        result.Should().Equal("2024-11", "2024-12", "2025-01", "2025-02");
    }

    [Fact]
    public void Expand_FromBeforeHistoryFloor_ClampsToFloor()
    {
        var result = MonthRange.Expand("2019-10", "2020-02");

        result.Should().Equal("2020-01", "2020-02");
    }

    [Fact]
    public void Expand_InvertedRange_ReturnsEmpty()
    {
        var result = MonthRange.Expand("2025-05", "2025-02");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_ThirteenMonthRange_HasThirteenEntries()
    {
        var result = MonthRange.Expand("2024-09", "2025-09");

        result.Should().HaveCount(13);
    }

    [Theory]
    [InlineData("2025-01", true, 2025, 1)]
    [InlineData("2025-12", true, 2025, 12)]
    [InlineData("2025-13", false, 0, 0)]
    [InlineData("2025-00", false, 0, 0)]
    [InlineData("2025-1", false, 0, 0)]
    [InlineData("nonsense", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    public void TryParse_ValidatesFormat(string input, bool expectedOk, int expectedYear, int expectedMonth)
    {
        var ok = MonthRange.TryParse(input, out var year, out var month);

        ok.Should().Be(expectedOk);
        year.Should().Be(expectedYear);
        month.Should().Be(expectedMonth);
    }

    [Fact]
    public void Key_FromDate_PadsToFourTwoFormat()
    {
        MonthRange.Key(new DateTime(2025, 7, 19)).Should().Be("2025-07");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MonthRangeTests"
```

Expected: FAIL — `MonthRange` does not exist (compile error `CS0103`/`CS0246`).

- [ ] **Step 3: Write the implementation**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/MonthRange.cs`:

```csharp
using System.Globalization;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

/// <summary>
/// Month-string arithmetic for product statistics. Months travel as "yyyy-MM" strings
/// rather than DateTime: a month is not an instant, and treating it as one invites
/// timezone drift between backend, JSON and browser.
/// </summary>
public static class MonthRange
{
    public static string Key(int year, int month) => $"{year:D4}-{month:D2}";

    public static string Key(DateTime date) => Key(date.Year, date.Month);

    public static bool TryParse(string month, out int year, out int monthNumber)
    {
        year = 0;
        monthNumber = 0;

        if (string.IsNullOrWhiteSpace(month) || month.Length != 7 || month[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(month.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear))
        {
            return false;
        }

        if (!int.TryParse(month.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMonth))
        {
            return false;
        }

        if (parsedMonth < 1 || parsedMonth > 12)
        {
            return false;
        }

        year = parsedYear;
        monthNumber = parsedMonth;
        return true;
    }

    /// <summary>
    /// Ascending, inclusive list of month keys. The lower bound is clamped to
    /// <see cref="CatalogConstants.HISTORY_FLOOR_DATE"/>; an inverted or unparseable
    /// range yields an empty list.
    /// </summary>
    public static List<string> Expand(string dateFrom, string dateTo)
    {
        if (!TryParse(dateFrom, out var fromYear, out var fromMonth) ||
            !TryParse(dateTo, out var toYear, out var toMonth))
        {
            return new List<string>();
        }

        var from = new DateTime(fromYear, fromMonth, 1);
        var to = new DateTime(toYear, toMonth, 1);

        var floor = new DateTime(
            CatalogConstants.HISTORY_FLOOR_DATE.Year,
            CatalogConstants.HISTORY_FLOOR_DATE.Month,
            1);

        if (from < floor)
        {
            from = floor;
        }

        var months = new List<string>();
        for (var cursor = from; cursor <= to; cursor = cursor.AddMonths(1))
        {
            months.Add(Key(cursor.Year, cursor.Month));
        }

        return months;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MonthRangeTests"
```

Expected: PASS, 14 tests (7 facts + 7 `TryParse` theory cases).

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/MonthRange.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/MonthRangeTests.cs
git commit -m "feat: add month range expansion for product statistics"
```

---

### Task 2: Request, response and DTO contracts

Plain contracts, no logic. Split from the handler so the handler task starts with its types already fixed.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductStatisticsMetric.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductStatisticsSeriesDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsResponse.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/ProductStatisticsContractTests.cs`

**Interfaces:**
- Consumes: `Anela.Heblo.Application.Shared.BaseResponse` (existing abstract class)
- Produces:
  - `enum ProductStatisticsMetric { Sales, Purchase, Consumption, Manufacture }`
  - `class ProductStatisticsSeriesDto { string ProductCode; string ProductName; List<double> Values; }`
  - `class GetProductStatisticsRequest : IRequest<GetProductStatisticsResponse> { List<string> ProductCodes; ProductStatisticsMetric Metric; string DateFrom; string DateTo; }`
  - `class GetProductStatisticsResponse : BaseResponse { List<string> Months; List<ProductStatisticsSeriesDto> Products; }`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/ProductStatisticsContractTests.cs`:

```csharp
using System;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class ProductStatisticsContractTests
{
    [Fact]
    public void Response_InheritsBaseResponse()
    {
        typeof(GetProductStatisticsResponse).Should().BeDerivedFrom<BaseResponse>();
    }

    [Fact]
    public void Contracts_AreClassesNotRecords()
    {
        // Records emit a compiler-generated <Clone>$ method; DTOs here must not be records
        // because the OpenAPI generator mishandles record parameter order.
        typeof(GetProductStatisticsResponse).GetMethod("<Clone>$").Should().BeNull();
        typeof(ProductStatisticsSeriesDto).GetMethod("<Clone>$").Should().BeNull();
        typeof(GetProductStatisticsRequest).GetMethod("<Clone>$").Should().BeNull();
    }

    [Fact]
    public void Response_DefaultsToSuccessWithEmptyCollections()
    {
        var response = new GetProductStatisticsResponse();

        response.Success.Should().BeTrue();
        response.Months.Should().BeEmpty();
        response.Products.Should().BeEmpty();
    }

    [Fact]
    public void Metric_HasExactlyFourValues()
    {
        Enum.GetValues<ProductStatisticsMetric>().Should().BeEquivalentTo(new[]
        {
            ProductStatisticsMetric.Sales,
            ProductStatisticsMetric.Purchase,
            ProductStatisticsMetric.Consumption,
            ProductStatisticsMetric.Manufacture,
        });
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductStatisticsContractTests"
```

Expected: FAIL — types do not exist.

- [ ] **Step 3: Write the contracts**

`Contracts/ProductStatisticsMetric.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public enum ProductStatisticsMetric
{
    Sales,
    Purchase,
    Consumption,
    Manufacture
}
```

`Contracts/ProductStatisticsSeriesDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// One product's monthly values, index-aligned to GetProductStatisticsResponse.Months.
/// </summary>
public class ProductStatisticsSeriesDto
{
    public string ProductCode { get; set; } = null!;

    public string ProductName { get; set; } = null!;

    /// <summary>
    /// Same length and order as the response's Months. Months with no data are 0, not gaps.
    /// </summary>
    public List<double> Values { get; set; } = new();
}
```

`UseCases/GetProductStatistics/GetProductStatisticsRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

public class GetProductStatisticsRequest : IRequest<GetProductStatisticsResponse>
{
    public List<string> ProductCodes { get; set; } = new();

    public ProductStatisticsMetric Metric { get; set; } = ProductStatisticsMetric.Sales;

    /// <summary>Inclusive lower bound, "yyyy-MM".</summary>
    public string DateFrom { get; set; } = null!;

    /// <summary>Inclusive upper bound, "yyyy-MM".</summary>
    public string DateTo { get; set; } = null!;
}
```

`UseCases/GetProductStatistics/GetProductStatisticsResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

public class GetProductStatisticsResponse : BaseResponse
{
    /// <summary>
    /// Dense ascending month keys ("yyyy-MM") shared by every series in Products.
    /// </summary>
    public List<string> Months { get; set; } = new();

    public List<ProductStatisticsSeriesDto> Products { get; set; } = new();
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductStatisticsContractTests"
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductStatisticsMetric.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductStatisticsSeriesDto.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsResponse.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/ProductStatisticsContractTests.cs
git commit -m "feat: add product statistics contracts"
```

---

### Task 3: The handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/GetProductStatisticsHandlerTests.cs`

**Interfaces:**
- Consumes:
  - `MonthRange.Expand(string, string)` and `MonthRange.Key(DateTime)` from Task 1
  - `GetProductStatisticsRequest` / `GetProductStatisticsResponse` / `ProductStatisticsSeriesDto` / `ProductStatisticsMetric` from Task 2
  - `ICatalogRepository.GetByIdsAsync(IEnumerable<string> ids, CancellationToken)` → `Task<IReadOnlyDictionary<string, CatalogAggregate>>` (existing)
  - `CatalogAggregate.SaleHistorySummary.MonthlyData` → `Dictionary<string, MonthlySalesSummary>` with `.TotalAmount` (double)
  - `CatalogAggregate.ConsumedHistorySummary.MonthlyData` → `Dictionary<string, MonthlyConsumedSummary>` with `.TotalAmount` (double)
  - `CatalogAggregate.PurchaseHistorySummary.MonthlyData` → `Dictionary<string, MonthlyPurchaseSummary>` with `.TotalAmount` (double)
  - `CatalogAggregate.ManufactureHistory` → `IReadOnlyList<CatalogManufactureRecord>` with `.Date` (DateTime) and `.Amount` (double)
- Produces: `GetProductStatisticsHandler : IRequestHandler<GetProductStatisticsRequest, GetProductStatisticsResponse>`, constructed as `new GetProductStatisticsHandler(ICatalogRepository, ILogger<GetProductStatisticsHandler>)`

Note: all three summary dictionaries are keyed exactly `"yyyy-MM"` — the same format `MonthRange.Key` produces — so the lookup is direct. Only `Manufacture` needs grouping over raw records.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/GetProductStatisticsHandlerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class GetProductStatisticsHandlerTests
{
    private readonly Mock<ICatalogRepository> _repositoryMock = new();

    private GetProductStatisticsHandler CreateHandler() =>
        new(_repositoryMock.Object, NullLogger<GetProductStatisticsHandler>.Instance);

    private void SetupCatalog(params CatalogAggregate[] items)
    {
        _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToDictionary(i => i.ProductCode, i => i)
                as IReadOnlyDictionary<string, CatalogAggregate>);
    }

    private static CatalogAggregate Product(string code, string name) =>
        new() { ProductCode = code, ProductName = name };

    private static void WithSales(CatalogAggregate item, string monthKey, double amount)
    {
        var parts = monthKey.Split('-');
        item.SaleHistorySummary.MonthlyData[monthKey] = new MonthlySalesSummary
        {
            Year = int.Parse(parts[0]),
            Month = int.Parse(parts[1]),
            AmountB2B = amount,
            AmountB2C = 0,
        };
    }

    private static void WithConsumption(CatalogAggregate item, string monthKey, double amount)
    {
        var parts = monthKey.Split('-');
        item.ConsumedHistorySummary.MonthlyData[monthKey] = new MonthlyConsumedSummary
        {
            Year = int.Parse(parts[0]),
            Month = int.Parse(parts[1]),
            TotalAmount = amount,
        };
    }

    private static void WithPurchase(CatalogAggregate item, string monthKey, double amount)
    {
        var parts = monthKey.Split('-');
        item.PurchaseHistorySummary.MonthlyData[monthKey] = new MonthlyPurchaseSummary
        {
            Year = int.Parse(parts[0]),
            Month = int.Parse(parts[1]),
            TotalAmount = amount,
        };
    }

    private static GetProductStatisticsRequest Request(
        ProductStatisticsMetric metric,
        string from,
        string to,
        params string[] codes) =>
        new()
        {
            ProductCodes = codes.ToList(),
            Metric = metric,
            DateFrom = from,
            DateTo = to,
        };

    [Fact]
    public async Task Handle_SalesMetric_PlacesAmountInMatchingMonth()
    {
        var product = Product("PROD-A", "Krém");
        WithSales(product, "2025-02", 120);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-03", "PROD-A"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Months.Should().Equal("2025-01", "2025-02", "2025-03");
        result.Products.Should().ContainSingle();
        result.Products[0].ProductCode.Should().Be("PROD-A");
        result.Products[0].ProductName.Should().Be("Krém");
        result.Products[0].Values.Should().Equal(0, 120, 0);
    }

    [Fact]
    public async Task Handle_SalesMetric_SumsB2BAndB2C()
    {
        var product = Product("PROD-A", "Krém");
        product.SaleHistorySummary.MonthlyData["2025-01"] = new MonthlySalesSummary
        {
            Year = 2025,
            Month = 1,
            AmountB2B = 30,
            AmountB2C = 70,
        };
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-01", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(100);
    }

    [Fact]
    public async Task Handle_ConsumptionMetric_ReadsConsumedSummary()
    {
        var product = Product("MAT-1", "Olej");
        WithConsumption(product, "2025-02", 45);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Consumption, "2025-01", "2025-02", "MAT-1"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(0, 45);
    }

    [Fact]
    public async Task Handle_PurchaseMetric_ReadsPurchaseSummary()
    {
        var product = Product("MAT-1", "Olej");
        WithPurchase(product, "2025-01", 500);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Purchase, "2025-01", "2025-02", "MAT-1"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(500, 0);
    }

    [Fact]
    public async Task Handle_ManufactureMetric_SumsRecordsWithinSameMonth()
    {
        var product = Product("PROD-A", "Krém");
        product.ManufactureHistory = new List<CatalogManufactureRecord>
        {
            new() { Date = new DateTime(2025, 1, 5), Amount = 10, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 1, 22), Amount = 15, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 2, 3), Amount = 7, ProductCode = "PROD-A" },
        };
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Manufacture, "2025-01", "2025-02", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(25, 7);
    }

    [Fact]
    public async Task Handle_ManufactureMetric_IgnoresRecordsOutsideRange()
    {
        var product = Product("PROD-A", "Krém");
        product.ManufactureHistory = new List<CatalogManufactureRecord>
        {
            new() { Date = new DateTime(2024, 12, 31), Amount = 99, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 1, 1), Amount = 5, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 3, 1), Amount = 88, ProductCode = "PROD-A" },
        };
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Manufacture, "2025-01", "2025-02", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(5, 0);
    }

    [Fact]
    public async Task Handle_MonthWithNoData_YieldsZeroNotGap()
    {
        var product = Product("PROD-A", "Krém");
        WithSales(product, "2025-06", 10);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-06", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().HaveCount(result.Months.Count);
        result.Products[0].Values.Should().Equal(0, 0, 0, 0, 0, 10);
    }

    [Fact]
    public async Task Handle_SeriesOrder_MatchesRequestedProductOrder()
    {
        var a = Product("PROD-A", "Krém");
        var b = Product("PROD-B", "Mýdlo");
        var c = Product("PROD-C", "Balzám");
        SetupCatalog(a, b, c);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-01", "PROD-C", "PROD-A", "PROD-B"),
            CancellationToken.None);

        result.Products.Select(p => p.ProductCode)
            .Should().Equal("PROD-C", "PROD-A", "PROD-B");
    }

    [Fact]
    public async Task Handle_UnknownProductCode_IsSkippedAndKnownOnesReturned()
    {
        var known = Product("PROD-A", "Krém");
        WithSales(known, "2025-01", 12);
        SetupCatalog(known);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-01", "PROD-A", "GHOST"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Products.Should().ContainSingle();
        result.Products[0].ProductCode.Should().Be("PROD-A");
    }

    [Fact]
    public async Task Handle_RangeBoundaries_AreInclusiveOnBothEnds()
    {
        var product = Product("PROD-A", "Krém");
        WithSales(product, "2025-01", 1);
        WithSales(product, "2025-04", 4);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-04", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(1, 0, 0, 4);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductStatisticsHandlerTests"
```

Expected: FAIL — `GetProductStatisticsHandler` does not exist.

If instead a test fails with a stack trace that contradicts the source you can read, the binaries are stale from a concurrent build: `touch` the test project file and rebuild before trusting the result.

- [ ] **Step 3: Write the handler**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

public class GetProductStatisticsHandler
    : IRequestHandler<GetProductStatisticsRequest, GetProductStatisticsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<GetProductStatisticsHandler> _logger;

    public GetProductStatisticsHandler(
        ICatalogRepository catalogRepository,
        ILogger<GetProductStatisticsHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<GetProductStatisticsResponse> Handle(
        GetProductStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        var months = MonthRange.Expand(request.DateFrom, request.DateTo);

        var catalogItems = await _catalogRepository.GetByIdsAsync(
            request.ProductCodes,
            cancellationToken);

        var series = new List<ProductStatisticsSeriesDto>();

        // Iterate the requested codes, not the dictionary, so series order is the caller's
        // order — that is what keeps chart colors stable as products are added and removed.
        foreach (var productCode in request.ProductCodes)
        {
            if (!catalogItems.TryGetValue(productCode, out var item))
            {
                _logger.LogDebug(
                    "Product {ProductCode} not found in catalog cache, skipping from statistics",
                    productCode);
                continue;
            }

            series.Add(new ProductStatisticsSeriesDto
            {
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Values = ProjectMetric(item, request.Metric, months)
            });
        }

        return new GetProductStatisticsResponse
        {
            Months = months,
            Products = series
        };
    }

    private static List<double> ProjectMetric(
        CatalogAggregate item,
        ProductStatisticsMetric metric,
        List<string> months)
    {
        var byMonth = BuildMonthLookup(item, metric);

        // Dense output: a month with no data is 0, not a gap. Frontend never handles nulls.
        return months
            .Select(month => byMonth.TryGetValue(month, out var value) ? value : 0d)
            .ToList();
    }

    private static Dictionary<string, double> BuildMonthLookup(
        CatalogAggregate item,
        ProductStatisticsMetric metric)
    {
        switch (metric)
        {
            case ProductStatisticsMetric.Sales:
                return item.SaleHistorySummary.MonthlyData
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TotalAmount);

            case ProductStatisticsMetric.Purchase:
                return item.PurchaseHistorySummary.MonthlyData
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TotalAmount);

            case ProductStatisticsMetric.Consumption:
                return item.ConsumedHistorySummary.MonthlyData
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TotalAmount);

            case ProductStatisticsMetric.Manufacture:
                // The only metric without a pre-aggregated summary on the aggregate.
                return item.ManufactureHistory
                    .GroupBy(record => MonthRange.Key(record.Date))
                    .ToDictionary(group => group.Key, group => group.Sum(record => record.Amount));

            default:
                return new Dictionary<string, double>();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductStatisticsHandlerTests"
```

Expected: PASS, 10 tests.

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/GetProductStatisticsHandlerTests.cs
git commit -m "feat: add product statistics handler"
```

---

### Task 4: Validator and DI registration

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Validators/GetProductStatisticsRequestValidator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` (validator block ~line 132–138, behavior block ~line 141–147)
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/GetProductStatisticsRequestValidatorTests.cs`

**Interfaces:**
- Consumes: `GetProductStatisticsRequest` (Task 2), `MonthRange.TryParse` (Task 1)
- Produces: `GetProductStatisticsRequestValidator : AbstractValidator<GetProductStatisticsRequest>`, plus a `public const int MaxProducts = 10` on it

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/GetProductStatisticsRequestValidatorTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using Anela.Heblo.Application.Features.Catalog.Validators;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class GetProductStatisticsRequestValidatorTests
{
    private readonly GetProductStatisticsRequestValidator _validator = new();

    private static GetProductStatisticsRequest Valid() => new()
    {
        ProductCodes = new List<string> { "PROD-A" },
        Metric = ProductStatisticsMetric.Sales,
        DateFrom = "2025-01",
        DateTo = "2025-06",
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NoProductCodes_Fails()
    {
        var request = Valid();
        request.ProductCodes = new List<string>();

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MoreThanTenProductCodes_Fails()
    {
        var request = Valid();
        request.ProductCodes = Enumerable.Range(1, 11).Select(i => $"PROD-{i}").ToList();

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ExactlyTenProductCodes_Passes()
    {
        var request = Valid();
        request.ProductCodes = Enumerable.Range(1, 10).Select(i => $"PROD-{i}").ToList();

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BlankProductCode_Fails()
    {
        var request = Valid();
        request.ProductCodes = new List<string> { "PROD-A", "  " };

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("2025-1")]
    [InlineData("2025-13")]
    [InlineData("25-01")]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Validate_MalformedDateFrom_Fails(string dateFrom)
    {
        var request = Valid();
        request.DateFrom = dateFrom;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("2025-1")]
    [InlineData("2025-13")]
    [InlineData("")]
    public void Validate_MalformedDateTo_Fails(string dateTo)
    {
        var request = Valid();
        request.DateTo = dateTo;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvertedRange_Fails()
    {
        var request = Valid();
        request.DateFrom = "2025-06";
        request.DateTo = "2025-01";

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SameFromAndTo_Passes()
    {
        var request = Valid();
        request.DateFrom = "2025-03";
        request.DateTo = "2025-03";

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DateFromBeforeHistoryFloor_Passes()
    {
        // Pre-2020 is clamped by MonthRange.Expand, not rejected — a bookmark with an old
        // range should still render, matching how GetCatalogDetailHandler treats the floor.
        var request = Valid();
        request.DateFrom = "2018-01";

        _validator.Validate(request).IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductStatisticsRequestValidatorTests"
```

Expected: FAIL — `GetProductStatisticsRequestValidator` does not exist.

- [ ] **Step 3: Write the validator**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Validators/GetProductStatisticsRequestValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Validators;

public class GetProductStatisticsRequestValidator : AbstractValidator<GetProductStatisticsRequest>
{
    /// <summary>
    /// Chart legibility and payload cap. This is the only rule bounding the response size,
    /// so it lives here rather than in the frontend alone.
    /// </summary>
    public const int MaxProducts = 10;

    public GetProductStatisticsRequestValidator()
    {
        RuleFor(x => x.ProductCodes)
            .NotEmpty()
            .WithMessage("At least one product code is required")
            .Must(codes => codes == null || codes.Count <= MaxProducts)
            .WithMessage($"At most {MaxProducts} product codes can be requested at once");

        RuleForEach(x => x.ProductCodes)
            .NotEmpty()
            .WithMessage("Product code cannot be empty")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.DateFrom)
            .Must(BeAValidMonth)
            .WithMessage("DateFrom must be in yyyy-MM format");

        RuleFor(x => x.DateTo)
            .Must(BeAValidMonth)
            .WithMessage("DateTo must be in yyyy-MM format");

        RuleFor(x => x)
            .Must(HaveOrderedRange)
            .WithMessage("DateFrom must not be later than DateTo")
            .When(x => BeAValidMonth(x.DateFrom) && BeAValidMonth(x.DateTo));
    }

    private static bool BeAValidMonth(string? month) =>
        month != null && MonthRange.TryParse(month, out _, out _);

    private static bool HaveOrderedRange(GetProductStatisticsRequest request) =>
        string.CompareOrdinal(request.DateFrom, request.DateTo) <= 0;
}
```

`RuleForEach(...).NotEmpty()` rejects whitespace-only entries because FluentValidation's `NotEmpty` treats a whitespace string as empty.

- [ ] **Step 4: Register the validator and pipeline behavior**

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, add the `using` for the use case namespace if it is not already present, then add one line to the validator block (next to the existing `GetCatalogDetailRequestValidator` line):

```csharp
services.AddScoped<IValidator<GetProductStatisticsRequest>, GetProductStatisticsRequestValidator>();
```

and one line to the pipeline-behavior block (next to the existing `GetCatalogDetailRequest` behavior line):

```csharp
services.AddScoped<IPipelineBehavior<GetProductStatisticsRequest, GetProductStatisticsResponse>, ValidationBehavior<GetProductStatisticsRequest, GetProductStatisticsResponse>>();
```

Both lines are required. Registering only the validator means validation never runs.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductStatisticsRequestValidatorTests"
```

Expected: build succeeds; PASS, 16 tests (8 facts + 8 theory cases).

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Catalog/Validators/GetProductStatisticsRequestValidator.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/ProductStatistics/GetProductStatisticsRequestValidatorTests.cs
git commit -m "feat: add product statistics request validator"
```

---

### Task 5: Expose the endpoint and regenerate the client

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs` (add after the existing `GetCatalogDetail` action, ~line 47)
- Test: `backend/test/Anela.Heblo.Tests/Controllers/CatalogControllerTests.cs` (append)

**Interfaces:**
- Consumes: `GetProductStatisticsRequest` / `GetProductStatisticsResponse` (Task 2)
- Produces:
  - Route `GET /api/catalog/product-statistics`
  - Controller action `Task<ActionResult<GetProductStatisticsResponse>> GetProductStatistics([FromQuery] GetProductStatisticsRequest request)`
  - Generated TS client method `catalog_GetProductStatistics(...)` — the frontend hook in Task 6 calls it

The controller already carries `[FeatureAuthorize(Feature.Products_Catalog)]` at class level, so the endpoint inherits catalog Read authorization with no attribute of its own.

- [ ] **Step 1: Write the failing test**

Append to `backend/test/Anela.Heblo.Tests/Controllers/CatalogControllerTests.cs` (match the file's existing usings and construction style; add `using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;` and `using Anela.Heblo.Application.Features.Catalog.Contracts;`):

```csharp
[Fact]
public async Task GetProductStatistics_PassesRequestToMediatorAndReturnsOk()
{
    // Arrange
    var mediatorMock = new Mock<IMediator>();
    var expected = new GetProductStatisticsResponse
    {
        Months = new List<string> { "2025-01", "2025-02" },
        Products = new List<ProductStatisticsSeriesDto>
        {
            new()
            {
                ProductCode = "PROD-A",
                ProductName = "Krém",
                Values = new List<double> { 1, 2 },
            },
        },
    };

    mediatorMock
        .Setup(m => m.Send(It.IsAny<GetProductStatisticsRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);

    var controller = new CatalogController(mediatorMock.Object);

    var request = new GetProductStatisticsRequest
    {
        ProductCodes = new List<string> { "PROD-A" },
        Metric = ProductStatisticsMetric.Sales,
        DateFrom = "2025-01",
        DateTo = "2025-02",
    };

    // Act
    var result = await controller.GetProductStatistics(request);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var payload = Assert.IsType<GetProductStatisticsResponse>(okResult.Value);
    Assert.Equal(2, payload.Months.Count);
    Assert.Single(payload.Products);

    mediatorMock.Verify(
        m => m.Send(
            It.Is<GetProductStatisticsRequest>(r =>
                r.Metric == ProductStatisticsMetric.Sales &&
                r.DateFrom == "2025-01" &&
                r.DateTo == "2025-02"),
            It.IsAny<CancellationToken>()),
        Times.Once);
}
```

If the existing `CatalogControllerTests` constructs its controller differently (for example via a shared field), follow that file's pattern rather than the local `new CatalogController(...)` above.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogControllerTests.GetProductStatistics"
```

Expected: FAIL — `CatalogController` has no `GetProductStatistics` method.

- [ ] **Step 3: Add the endpoint**

In `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs`, add the using:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
```

and the action, placed immediately after `GetCatalogDetail` (before the `{productCode}/composition` route — a literal segment must be declared before nothing in particular here, but keeping related catalog reads together is the file's existing convention):

```csharp
[HttpGet("product-statistics")]
public async Task<ActionResult<GetProductStatisticsResponse>> GetProductStatistics(
    [FromQuery] GetProductStatisticsRequest request)
{
    var response = await _mediator.Send(request);
    return HandleResponse(response);
}
```

`[FromQuery]` binds repeated `productCodes=A&productCodes=B` query parameters into `List<string> ProductCodes`.

Note the route literal `product-statistics` does not collide with the `[HttpGet("{productCode}")]` catch-all because ASP.NET Core routing scores literal segments above parameter segments.

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogControllerTests"
```

Expected: build succeeds; all `CatalogControllerTests` pass including the new one.

- [ ] **Step 5: Regenerate the TypeScript client and confirm the method exists**

```bash
dotnet msbuild -t:GenerateFrontendClientManual
grep -n "catalog_GetProductStatistics" frontend/src/api/generated/api-client.ts | head -5
```

Expected: the grep prints at least one match. Record the generated method's exact parameter order — Task 6 must call it with that signature.

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.API backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.API/Controllers/CatalogController.cs \
        backend/test/Anela.Heblo.Tests/Controllers/CatalogControllerTests.cs \
        frontend/src/api/generated/api-client.ts
git commit -m "feat: expose product statistics endpoint"
```

---

### Task 6: Frontend data hook and color palette

Two small leaf modules with no component dependencies, so the chart and table tasks can assume them.

**Files:**
- Create: `frontend/src/api/hooks/useProductStatistics.ts`
- Create: `frontend/src/components/product-statistics/productStatisticsColors.ts`
- Test: `frontend/src/api/hooks/__tests__/useProductStatistics.test.ts`

**Interfaces:**
- Consumes: `getAuthenticatedApiClient`, `QUERY_KEYS` from `frontend/src/api/client.ts`; generated `catalog_GetProductStatistics` (Task 5); generated `ProductStatisticsMetric` enum
- Produces:
  - `type StatisticsMetric = "Sales" | "Purchase" | "Consumption" | "Manufacture"`
  - `useProductStatistics(productCodes: string[], metric: StatisticsMetric, dateFrom: string, dateTo: string)` → React Query result whose `data` is `GetProductStatisticsResponse`
  - `productStatisticsColors.ts`: `getSeriesColor(index: number): { border: string; background: string }` and `SERIES_COLOR_COUNT: number`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/api/hooks/__tests__/useProductStatistics.test.ts`:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useProductStatistics } from "../useProductStatistics";

const mockGetProductStatistics = jest.fn();

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: () => ({
    catalog_GetProductStatistics: mockGetProductStatistics,
  }),
  QUERY_KEYS: { catalog: ["catalog"] },
}));

const wrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe("useProductStatistics", () => {
  beforeEach(() => {
    mockGetProductStatistics.mockReset();
  });

  test("does not fetch when no products are selected", () => {
    const { result } = renderHook(
      () => useProductStatistics([], "Sales", "2025-01", "2025-06"),
      { wrapper },
    );

    expect(result.current.fetchStatus).toBe("idle");
    expect(mockGetProductStatistics).not.toHaveBeenCalled();
  });

  test("does not fetch when the range is inverted", () => {
    const { result } = renderHook(
      () => useProductStatistics(["PROD-A"], "Sales", "2025-06", "2025-01"),
      { wrapper },
    );

    expect(result.current.fetchStatus).toBe("idle");
    expect(mockGetProductStatistics).not.toHaveBeenCalled();
  });

  test("fetches and returns the response when inputs are valid", async () => {
    mockGetProductStatistics.mockResolvedValue({
      months: ["2025-01"],
      products: [{ productCode: "PROD-A", productName: "Krém", values: [5] }],
    });

    const { result } = renderHook(
      () => useProductStatistics(["PROD-A"], "Sales", "2025-01", "2025-01"),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetProductStatistics).toHaveBeenCalledTimes(1);
    expect(result.current.data?.products).toHaveLength(1);
  });

  test("refetches when the metric changes", async () => {
    mockGetProductStatistics.mockResolvedValue({ months: [], products: [] });

    const { rerender, result } = renderHook(
      ({ metric }: { metric: "Sales" | "Purchase" }) =>
        useProductStatistics(["PROD-A"], metric, "2025-01", "2025-01"),
      { wrapper, initialProps: { metric: "Sales" as const } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    rerender({ metric: "Purchase" });

    await waitFor(() => expect(mockGetProductStatistics).toHaveBeenCalledTimes(2));
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="useProductStatistics" --watchAll=false
```

Expected: FAIL — cannot resolve `../useProductStatistics`.

- [ ] **Step 3: Write the palette**

Create `frontend/src/components/product-statistics/productStatisticsColors.ts`:

```typescript
/**
 * Fixed palette for multi-product chart series. Colors are assigned by the product's
 * index in the current selection, so a product keeps its color as long as its position
 * holds — the backend returns series in the order they were requested.
 */
const SERIES_COLORS: ReadonlyArray<{ border: string; background: string }> = [
  { border: "rgba(59, 130, 246, 1)", background: "rgba(59, 130, 246, 0.15)" }, // blue
  { border: "rgba(34, 197, 94, 1)", background: "rgba(34, 197, 94, 0.15)" }, // green
  { border: "rgba(168, 85, 247, 1)", background: "rgba(168, 85, 247, 0.15)" }, // purple
  { border: "rgba(251, 146, 60, 1)", background: "rgba(251, 146, 60, 0.15)" }, // orange
  { border: "rgba(236, 72, 153, 1)", background: "rgba(236, 72, 153, 0.15)" }, // pink
  { border: "rgba(20, 184, 166, 1)", background: "rgba(20, 184, 166, 0.15)" }, // teal
  { border: "rgba(234, 179, 8, 1)", background: "rgba(234, 179, 8, 0.15)" }, // yellow
  { border: "rgba(99, 102, 241, 1)", background: "rgba(99, 102, 241, 0.15)" }, // indigo
  { border: "rgba(239, 68, 68, 1)", background: "rgba(239, 68, 68, 0.15)" }, // red
  { border: "rgba(107, 114, 128, 1)", background: "rgba(107, 114, 128, 0.15)" }, // gray
];

export const SERIES_COLOR_COUNT = SERIES_COLORS.length;

export function getSeriesColor(index: number): {
  border: string;
  background: string;
} {
  return SERIES_COLORS[index % SERIES_COLORS.length];
}
```

Ten colors for a ten-product cap, all legible on both the light and the graphite dark surface.

- [ ] **Step 4: Write the hook**

Create `frontend/src/api/hooks/useProductStatistics.ts`. Adjust the client call to the generated method's actual parameter order recorded in Task 5, Step 5:

```typescript
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

export type StatisticsMetric =
  | "Sales"
  | "Purchase"
  | "Consumption"
  | "Manufacture";

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

export function isValidMonthRange(dateFrom: string, dateTo: string): boolean {
  if (!MONTH_PATTERN.test(dateFrom) || !MONTH_PATTERN.test(dateTo)) {
    return false;
  }
  return dateFrom <= dateTo; // "yyyy-MM" sorts lexicographically the same as chronologically
}

export function useProductStatistics(
  productCodes: string[],
  metric: StatisticsMetric,
  dateFrom: string,
  dateTo: string,
) {
  const isEnabled =
    productCodes.length > 0 && isValidMonthRange(dateFrom, dateTo);

  return useQuery({
    queryKey: [
      ...QUERY_KEYS.catalog,
      "product-statistics",
      productCodes,
      metric,
      dateFrom,
      dateTo,
    ],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      // The generated client throws on non-200; errors surface via React Query's `error`.
      return apiClient.catalog_GetProductStatistics(
        productCodes,
        metric,
        dateFrom,
        dateTo,
      );
    },
    enabled: isEnabled,
    staleTime: 5 * 60 * 1000,
  });
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="useProductStatistics" --watchAll=false
```

Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useProductStatistics.ts \
        frontend/src/api/hooks/__tests__/useProductStatistics.test.ts \
        frontend/src/components/product-statistics/productStatisticsColors.ts
git commit -m "feat: add product statistics query hook and series palette"
```

---

### Task 7: Multi-select mode for CatalogAutocomplete

Additive change to a shared component. Every existing call site keeps working untouched.

**Files:**
- Modify: `frontend/src/components/common/CatalogAutocomplete.tsx`
- Test: `frontend/src/components/common/__tests__/CatalogAutocomplete.test.tsx` (append)

**Interfaces:**
- Consumes: existing `useCatalogAutocomplete`, react-select `MultiValue` / `ActionMeta` (already imported in the file but currently unused)
- Produces: three new optional props on `CatalogAutocompleteProps<T>`:
  - `isMulti?: boolean`
  - `values?: T[]` — the selected items when `isMulti`
  - `onSelectMany?: (items: T[]) => void`

- [ ] **Step 1: Write the failing tests**

Append to `frontend/src/components/common/__tests__/CatalogAutocomplete.test.tsx`, matching the file's existing mocking of `useCatalogAutocomplete`:

```typescript
describe("CatalogAutocomplete multi-select", () => {
  test("renders a chip for every selected value", () => {
    render(
      <CatalogAutocomplete
        isMulti
        values={[
          { productCode: "PROD-A", productName: "Krém" } as any,
          { productCode: "PROD-B", productName: "Mýdlo" } as any,
        ]}
        onSelect={jest.fn()}
        onSelectMany={jest.fn()}
      />,
    );

    expect(screen.getByText(/Krém/)).toBeInTheDocument();
    expect(screen.getByText(/Mýdlo/)).toBeInTheDocument();
  });

  test("calls onSelectMany with an empty array when the selection is cleared", () => {
    const onSelectMany = jest.fn();

    render(
      <CatalogAutocomplete
        isMulti
        values={[{ productCode: "PROD-A", productName: "Krém" } as any]}
        onSelect={jest.fn()}
        onSelectMany={onSelectMany}
      />,
    );

    fireEvent.click(screen.getByLabelText("Remove Krém (PROD-A)"));

    expect(onSelectMany).toHaveBeenCalledWith([]);
  });

  test("single-select mode still calls onSelect and ignores onSelectMany", () => {
    const onSelect = jest.fn();
    const onSelectMany = jest.fn();

    render(
      <CatalogAutocomplete
        value={{ productCode: "PROD-A", productName: "Krém" } as any}
        onSelect={onSelect}
        onSelectMany={onSelectMany}
      />,
    );

    // No multi-value chips are rendered in single mode
    expect(screen.queryByLabelText(/^Remove /)).not.toBeInTheDocument();
  });
});
```

If the existing test file's react-select rendering makes the remove-button label differ, read the rendered output and assert on the label react-select actually emits rather than forcing this one.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="CatalogAutocomplete" --watchAll=false
```

Expected: the three new tests FAIL (no `isMulti` support); the existing tests in the file still PASS.

- [ ] **Step 3: Add the props to the interface**

In `frontend/src/components/common/CatalogAutocomplete.tsx`, add to `CatalogAutocompleteProps<T>` under the "Core props" comment:

```typescript
  // Multi-select mode. When true, `values`/`onSelectMany` replace `value`/`onSelect`.
  isMulti?: boolean;
  values?: T[];
  onSelectMany?: (items: T[]) => void;
```

and destructure them in the component signature alongside `value` and `onSelect`:

```typescript
  isMulti = false,
  values,
  onSelectMany,
```

- [ ] **Step 4: Build the multi value and handle multi change**

Add a multi-value converter next to the existing `getSelectValue`:

```typescript
  // Convert current multi values to select options
  const getSelectValues = (): CatalogSelectOption[] => {
    if (!values) return [];

    return values.map((item) => {
      const catalogItem = item as any;
      const code = catalogItem.productCode || catalogItem.value || "";
      const name = catalogItem.productName || String(item);

      return {
        productCode: code,
        productName: name,
        value: code,
        label: `${name} (${code})`,
      } as CatalogSelectOption;
    });
  };
```

Replace the body of `handleChange` so it branches on `isMulti`:

```typescript
  const handleChange = (
    newValue:
      | SingleValue<CatalogSelectOption>
      | MultiValue<CatalogSelectOption>,
    actionMeta: ActionMeta<CatalogSelectOption>,
  ) => {
    const toAdapted = (option: CatalogSelectOption): T => {
      const catalogItem =
        option.data ||
        new CatalogItemDto({
          productCode: option.productCode,
          productName: option.productName,
          type: option.type,
        });

      return itemAdapter ? itemAdapter(catalogItem) : (catalogItem as T);
    };

    if (isMulti) {
      const selectedOptions = (newValue as MultiValue<CatalogSelectOption>) || [];
      onSelectMany?.(selectedOptions.map(toAdapted));
      return;
    }

    const selectedOption = newValue as SingleValue<CatalogSelectOption>;

    if (!selectedOption) {
      onSelect(null);
      return;
    }

    onSelect(toAdapted(selectedOption));
  };
```

- [ ] **Step 5: Wire the props through to react-select**

In the returned `<Select>`, replace the `value` prop and add `isMulti`, and make the `SingleValue` custom component conditional (react-select ignores `SingleValue` in multi mode, but passing `components` conditionally keeps intent obvious):

```typescript
      <Select
        value={isMulti ? getSelectValues() : getSelectValue()}
        isMulti={isMulti}
        onChange={handleChange}
```

and change the `components` prop to:

```typescript
        components={
          isMulti
            ? { Option: CustomOption }
            : { Option: CustomOption, SingleValue: CustomSingleValue }
        }
```

Leave every other prop on `<Select>` exactly as it is.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="CatalogAutocomplete" --watchAll=false
```

Expected: PASS — all tests in the file, new and pre-existing.

- [ ] **Step 7: Verify no existing call site broke**

```bash
cd frontend && CI=false npm run build
```

Expected: build succeeds. `isMulti`, `values` and `onSelectMany` are all optional, so no existing usage needs a change.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/common/CatalogAutocomplete.tsx \
        frontend/src/components/common/__tests__/CatalogAutocomplete.test.tsx
git commit -m "feat: add multi-select mode to CatalogAutocomplete"
```

---

### Task 8: The filter component

**Files:**
- Create: `frontend/src/components/product-statistics/ProductStatisticsFilter.tsx`
- Test: `frontend/src/components/product-statistics/__tests__/ProductStatisticsFilter.test.tsx`

**Interfaces:**
- Consumes: `CatalogAutocomplete` with `isMulti` (Task 7); `CatalogItemDto` from `../../api/generated/api-client`
- Produces:
  ```typescript
  export const MAX_SELECTED_PRODUCTS = 10;
  export interface SelectedProduct { productCode: string; productName: string; }
  export interface ProductStatisticsFilterProps {
    selectedProducts: SelectedProduct[];
    onProductsChange: (products: SelectedProduct[]) => void;
    dateFrom: string;   // "yyyy-MM"
    dateTo: string;     // "yyyy-MM"
    onDateFromChange: (value: string) => void;
    onDateToChange: (value: string) => void;
  }
  export function defaultDateFrom(now?: Date): string;  // 12 months before defaultDateTo → a 13-month window
  export function defaultDateTo(now?: Date): string;    // current month
  ```

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/product-statistics/__tests__/ProductStatisticsFilter.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ProductStatisticsFilter, {
  defaultDateFrom,
  defaultDateTo,
  MAX_SELECTED_PRODUCTS,
} from "../ProductStatisticsFilter";

jest.mock("../../common/CatalogAutocomplete", () => ({
  __esModule: true,
  default: ({ values }: any) => (
    <div data-testid="catalog-autocomplete">{values?.length ?? 0} vybráno</div>
  ),
  CatalogAutocomplete: ({ values }: any) => (
    <div data-testid="catalog-autocomplete">{values?.length ?? 0} vybráno</div>
  ),
}));

const baseProps = {
  selectedProducts: [],
  onProductsChange: jest.fn(),
  dateFrom: "2025-01",
  dateTo: "2025-06",
  onDateFromChange: jest.fn(),
  onDateToChange: jest.fn(),
};

describe("ProductStatisticsFilter", () => {
  test("renders both month inputs with the given values", () => {
    render(<ProductStatisticsFilter {...baseProps} />);

    expect(screen.getByLabelText("Od")).toHaveValue("2025-01");
    expect(screen.getByLabelText("Do")).toHaveValue("2025-06");
  });

  test("calls onDateFromChange when the from month changes", () => {
    const onDateFromChange = jest.fn();
    render(
      <ProductStatisticsFilter {...baseProps} onDateFromChange={onDateFromChange} />,
    );

    fireEvent.change(screen.getByLabelText("Od"), {
      target: { value: "2024-11" },
    });

    expect(onDateFromChange).toHaveBeenCalledWith("2024-11");
  });

  test("shows an error when the range is inverted", () => {
    render(
      <ProductStatisticsFilter {...baseProps} dateFrom="2025-06" dateTo="2025-01" />,
    );

    expect(
      screen.getByText('Datum "Od" musí být dříve než "Do".'),
    ).toBeInTheDocument();
  });

  test("shows the selection cap message when the maximum is reached", () => {
    const selectedProducts = Array.from(
      { length: MAX_SELECTED_PRODUCTS },
      (_, i) => ({ productCode: `PROD-${i}`, productName: `Produkt ${i}` }),
    );

    render(
      <ProductStatisticsFilter {...baseProps} selectedProducts={selectedProducts} />,
    );

    expect(
      screen.getByText(`Maximálně ${MAX_SELECTED_PRODUCTS} produktů.`),
    ).toBeInTheDocument();
  });

  test("defaultDateTo returns the current month and defaultDateFrom is twelve months earlier", () => {
    const now = new Date(2025, 7, 15); // August 2025

    expect(defaultDateTo(now)).toBe("2025-08");
    expect(defaultDateFrom(now)).toBe("2024-08");
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="ProductStatisticsFilter" --watchAll=false
```

Expected: FAIL — cannot resolve `../ProductStatisticsFilter`.

- [ ] **Step 3: Write the component**

Create `frontend/src/components/product-statistics/ProductStatisticsFilter.tsx`:

```typescript
import React from "react";
import { AlertCircle } from "lucide-react";
import { CatalogAutocomplete } from "../common/CatalogAutocomplete";
import { CatalogItemDto } from "../../api/generated/api-client";

export const MAX_SELECTED_PRODUCTS = 10;

export interface SelectedProduct {
  productCode: string;
  productName: string;
}

export interface ProductStatisticsFilterProps {
  selectedProducts: SelectedProduct[];
  onProductsChange: (products: SelectedProduct[]) => void;
  dateFrom: string;
  dateTo: string;
  onDateFromChange: (value: string) => void;
  onDateToChange: (value: string) => void;
}

function toMonthKey(date: Date): string {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, "0");
  return `${year}-${month}`;
}

/** Current month, "yyyy-MM". */
export function defaultDateTo(now: Date = new Date()): string {
  return toMonthKey(now);
}

/**
 * Twelve months before the current month, which with an inclusive range gives the
 * 13-month window CatalogDetail already shows.
 */
export function defaultDateFrom(now: Date = new Date()): string {
  const from = new Date(now.getFullYear(), now.getMonth() - 12, 1);
  return toMonthKey(from);
}

const ProductStatisticsFilter: React.FC<ProductStatisticsFilterProps> = ({
  selectedProducts,
  onProductsChange,
  dateFrom,
  dateTo,
  onDateFromChange,
  onDateToChange,
}) => {
  const isRangeInverted = Boolean(dateFrom && dateTo && dateFrom > dateTo);
  const isAtCap = selectedProducts.length >= MAX_SELECTED_PRODUCTS;

  const handleProductsChange = (items: CatalogItemDto[]) => {
    const mapped = items
      .filter((item) => Boolean(item.productCode))
      .map((item) => ({
        productCode: item.productCode as string,
        productName: item.productName ?? (item.productCode as string),
      }));

    // Cap defensively: the backend rejects more than MAX_SELECTED_PRODUCTS anyway.
    onProductsChange(mapped.slice(0, MAX_SELECTED_PRODUCTS));
  };

  return (
    <div className="bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg p-4 mb-4">
      <div className="flex flex-col lg:flex-row lg:items-start gap-4">
        <div className="flex-1 min-w-0">
          <label className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1">
            Produkty
          </label>
          <CatalogAutocomplete<CatalogItemDto>
            isMulti
            values={selectedProducts.map(
              (product) =>
                new CatalogItemDto({
                  productCode: product.productCode,
                  productName: product.productName,
                }),
            )}
            onSelect={() => {}}
            onSelectMany={handleProductsChange}
            placeholder="Vyberte produkty..."
          />
          {isAtCap && (
            <div className="mt-1 text-sm text-gray-500 dark:text-graphite-muted">
              Maximálně {MAX_SELECTED_PRODUCTS} produktů.
            </div>
          )}
        </div>

        <div>
          <label
            htmlFor="product-statistics-date-from"
            className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1"
          >
            Od
          </label>
          <input
            id="product-statistics-date-from"
            type="month"
            value={dateFrom}
            onChange={(event) => onDateFromChange(event.target.value)}
            className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-1.5 text-sm"
          />
        </div>

        <div>
          <label
            htmlFor="product-statistics-date-to"
            className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1"
          >
            Do
          </label>
          <input
            id="product-statistics-date-to"
            type="month"
            value={dateTo}
            onChange={(event) => onDateToChange(event.target.value)}
            className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-1.5 text-sm"
          />
        </div>
      </div>

      {isRangeInverted && (
        <div className="mt-2 flex items-center text-sm text-red-600 dark:text-red-400">
          <AlertCircle className="h-4 w-4 mr-1" />
          Datum &quot;Od&quot; musí být dříve než &quot;Do&quot;.
        </div>
      )}
    </div>
  );
};

export default ProductStatisticsFilter;
```

`<label htmlFor>` paired with the input `id` is what makes `getByLabelText("Od")` work.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="ProductStatisticsFilter" --watchAll=false
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/product-statistics/ProductStatisticsFilter.tsx \
        frontend/src/components/product-statistics/__tests__/ProductStatisticsFilter.test.tsx
git commit -m "feat: add product statistics filter component"
```

---

### Task 9: The chart component

**Files:**
- Create: `frontend/src/components/product-statistics/ProductStatisticsChart.tsx`
- Test: `frontend/src/components/product-statistics/__tests__/ProductStatisticsChart.test.tsx`

**Interfaces:**
- Consumes: `getSeriesColor` from `./productStatisticsColors` (Task 6); `Line` from `react-chartjs-2`
- Produces:
  ```typescript
  export interface ProductStatisticsSeries {
    productCode: string;
    productName: string;
    values: number[];
  }
  export interface ProductStatisticsChartProps {
    months: string[];
    series: ProductStatisticsSeries[];
    yAxisLabel: string;
  }
  ```

The page (Task 10) maps the API response's `products` onto `series` and supplies `yAxisLabel` per tab.

Chart.js registration (`ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Title, Tooltip, Legend)`) already happens in `CatalogDetail.tsx`, but this page can render without `CatalogDetail` being mounted, so this component registers the same elements itself. Chart.js `register` is idempotent.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/product-statistics/__tests__/ProductStatisticsChart.test.tsx`:

```typescript
import React from "react";
import { render, screen } from "@testing-library/react";
import ProductStatisticsChart from "../ProductStatisticsChart";

const capturedProps: any = { current: null };

jest.mock("react-chartjs-2", () => ({
  Line: (props: any) => {
    capturedProps.current = props;
    return <div data-testid="line-chart" />;
  },
}));

describe("ProductStatisticsChart", () => {
  beforeEach(() => {
    capturedProps.current = null;
  });

  test("renders one dataset per product", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01", "2025-02"]}
        series={[
          { productCode: "PROD-A", productName: "Krém", values: [1, 2] },
          { productCode: "PROD-B", productName: "Mýdlo", values: [3, 4] },
        ]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(screen.getByTestId("line-chart")).toBeInTheDocument();
    expect(capturedProps.current.data.datasets).toHaveLength(2);
  });

  test("uses the response months as chart labels", () => {
    render(
      <ProductStatisticsChart
        months={["2024-11", "2024-12", "2025-01"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [1, 2, 3] }]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(capturedProps.current.data.labels).toEqual([
      "2024-11",
      "2024-12",
      "2025-01",
    ]);
  });

  test("labels each dataset with the product name and code", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [1] }]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(capturedProps.current.data.datasets[0].label).toBe("Krém (PROD-A)");
  });

  test("gives each product a distinct border color", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01"]}
        series={[
          { productCode: "PROD-A", productName: "Krém", values: [1] },
          { productCode: "PROD-B", productName: "Mýdlo", values: [2] },
        ]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    const [first, second] = capturedProps.current.data.datasets;
    expect(first.borderColor).not.toBe(second.borderColor);
  });

  test("shows the empty state when no products are selected", () => {
    render(
      <ProductStatisticsChart months={[]} series={[]} yAxisLabel="Kusů prodáno" />,
    );

    expect(screen.getByText("Žádná data pro zobrazení grafu")).toBeInTheDocument();
    expect(screen.queryByTestId("line-chart")).not.toBeInTheDocument();
  });

  test("shows the empty state when every series is all zero", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01", "2025-02"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [0, 0] }]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(screen.getByText("Žádná data pro zobrazení grafu")).toBeInTheDocument();
  });

  test("applies the given y-axis label", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [5] }]}
        yAxisLabel="Kusů vyrobeno"
      />,
    );

    expect(capturedProps.current.options.scales.y.title.text).toBe(
      "Kusů vyrobeno",
    );
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="ProductStatisticsChart" --watchAll=false
```

Expected: FAIL — cannot resolve `../ProductStatisticsChart`.

- [ ] **Step 3: Write the component**

Create `frontend/src/components/product-statistics/ProductStatisticsChart.tsx`:

```typescript
import React from "react";
import { BarChart3 } from "lucide-react";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
} from "chart.js";
import { Line } from "react-chartjs-2";
import { getSeriesColor } from "./productStatisticsColors";

// Idempotent: CatalogDetail registers the same elements, but this page can render
// without CatalogDetail ever being mounted.
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
);

export interface ProductStatisticsSeries {
  productCode: string;
  productName: string;
  values: number[];
}

export interface ProductStatisticsChartProps {
  months: string[];
  series: ProductStatisticsSeries[];
  yAxisLabel: string;
}

const ProductStatisticsChart: React.FC<ProductStatisticsChartProps> = ({
  months,
  series,
  yAxisLabel,
}) => {
  const hasData = series.some((item) =>
    item.values.some((value) => value !== 0),
  );

  if (!hasData) {
    return (
      <div className="flex items-center justify-center h-96">
        <div className="text-center text-gray-500 dark:text-graphite-muted">
          <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300 dark:text-graphite-faint" />
          <p>Žádná data pro zobrazení grafu</p>
          <p className="text-sm">
            Vyberte produkty a období pro zobrazení statistik
          </p>
        </div>
      </div>
    );
  }

  const chartData = {
    labels: months,
    datasets: series.map((item, index) => {
      const color = getSeriesColor(index);

      return {
        label: `${item.productName} (${item.productCode})`,
        data: item.values,
        borderColor: color.border,
        backgroundColor: color.background,
        borderWidth: 2,
        tension: 0.1,
        pointRadius: 3,
        pointHoverRadius: 5,
      };
    }),
  };

  const chartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      mode: "index" as const,
      intersect: false,
    },
    plugins: {
      legend: {
        position: "top" as const,
      },
      title: {
        display: false,
      },
    },
    scales: {
      y: {
        beginAtZero: true,
        title: {
          display: true,
          text: yAxisLabel,
        },
      },
      x: {
        title: {
          display: true,
          text: "Měsíc",
        },
      },
    },
  };

  return (
    <div className="h-96">
      <Line data={chartData} options={chartOptions} />
    </div>
  );
};

export default ProductStatisticsChart;
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="ProductStatisticsChart" --watchAll=false
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/product-statistics/ProductStatisticsChart.tsx \
        frontend/src/components/product-statistics/__tests__/ProductStatisticsChart.test.tsx
git commit -m "feat: add multi-series product statistics chart"
```

---

### Task 10: The table component

**Files:**
- Create: `frontend/src/components/product-statistics/ProductStatisticsTable.tsx`
- Test: `frontend/src/components/product-statistics/__tests__/ProductStatisticsTable.test.tsx`

**Interfaces:**
- Consumes: `ProductStatisticsSeries` from `./ProductStatisticsChart` (Task 9)
- Produces:
  ```typescript
  export interface ProductStatisticsTableProps {
    months: string[];
    series: ProductStatisticsSeries[];
  }
  ```

Rows are months **newest first** (the reverse of the chart's ascending axis), matching every other table in the app.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/product-statistics/__tests__/ProductStatisticsTable.test.tsx`:

```typescript
import React from "react";
import { render, screen, within } from "@testing-library/react";
import ProductStatisticsTable from "../ProductStatisticsTable";

const months = ["2025-01", "2025-02", "2025-03"];
const series = [
  { productCode: "PROD-A", productName: "Krém", values: [120, 98, 143] },
  { productCode: "PROD-B", productName: "Mýdlo", values: [45, 51, 40] },
];

describe("ProductStatisticsTable", () => {
  test("renders a column per product plus a total column", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const headers = screen.getAllByRole("columnheader");
    expect(headers.map((h) => h.textContent)).toEqual([
      "Měsíc",
      "Krém (PROD-A)",
      "Mýdlo (PROD-B)",
      "Celkem",
    ]);
  });

  test("renders months newest first", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const bodyRows = screen.getAllByRole("row").slice(1, 4);
    const firstCells = bodyRows.map(
      (row) => within(row).getAllByRole("cell")[0].textContent,
    );

    expect(firstCells).toEqual(["2025-03", "2025-02", "2025-01"]);
  });

  test("totals each row across products", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const marchRow = screen.getByRole("row", { name: /2025-03/ });
    const cells = within(marchRow).getAllByRole("cell");

    expect(cells[cells.length - 1]).toHaveTextContent("183");
  });

  test("totals each product column in the footer", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const footerRow = screen.getByRole("row", { name: /Celkem/ });
    const cells = within(footerRow).getAllByRole("cell");

    expect(cells[1]).toHaveTextContent("361");
    expect(cells[2]).toHaveTextContent("136");
    expect(cells[3]).toHaveTextContent("497");
  });

  test("renders months with no data as zero", () => {
    render(
      <ProductStatisticsTable
        months={["2025-01", "2025-02"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [0, 7] }]}
      />,
    );

    const januaryRow = screen.getByRole("row", { name: /2025-01/ });
    expect(within(januaryRow).getAllByRole("cell")[1]).toHaveTextContent("0");
  });

  test("renders nothing but a hint when no products are selected", () => {
    render(<ProductStatisticsTable months={[]} series={[]} />);

    expect(screen.getByText("Žádná data k zobrazení")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="ProductStatisticsTable" --watchAll=false
```

Expected: FAIL — cannot resolve `../ProductStatisticsTable`.

- [ ] **Step 3: Write the component**

Create `frontend/src/components/product-statistics/ProductStatisticsTable.tsx`:

```typescript
import React from "react";
import { ProductStatisticsSeries } from "./ProductStatisticsChart";

export interface ProductStatisticsTableProps {
  months: string[];
  series: ProductStatisticsSeries[];
}

const numberFormatter = new Intl.NumberFormat("cs-CZ", {
  maximumFractionDigits: 2,
});

const ProductStatisticsTable: React.FC<ProductStatisticsTableProps> = ({
  months,
  series,
}) => {
  if (months.length === 0 || series.length === 0) {
    return (
      <div className="py-8 text-center text-sm text-gray-500 dark:text-graphite-muted">
        Žádná data k zobrazení
      </div>
    );
  }

  // Newest month first — the chart reads left-to-right ascending, but every table
  // in this app puts the most recent row on top.
  const rowIndexes = months.map((_, index) => index).reverse();

  const columnTotals = series.map((item) =>
    item.values.reduce((sum, value) => sum + value, 0),
  );

  const grandTotal = columnTotals.reduce((sum, value) => sum + value, 0);

  const rowTotal = (index: number) =>
    series.reduce((sum, item) => sum + (item.values[index] ?? 0), 0);

  const cellClass =
    "px-4 py-2 text-sm text-right text-gray-900 dark:text-graphite-text whitespace-nowrap";
  const headerClass =
    "px-4 py-2 text-xs font-medium text-right uppercase tracking-wider text-gray-500 dark:text-graphite-muted whitespace-nowrap";

  return (
    <div className="overflow-x-auto border border-gray-200 dark:border-graphite-border rounded-lg">
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <th scope="col" className={`${headerClass} text-left`}>
              Měsíc
            </th>
            {series.map((item) => (
              <th scope="col" key={item.productCode} className={headerClass}>
                {item.productName} ({item.productCode})
              </th>
            ))}
            <th scope="col" className={`${headerClass} font-semibold`}>
              Celkem
            </th>
          </tr>
        </thead>

        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {rowIndexes.map((index) => (
            <tr key={months[index]}>
              <td className={`${cellClass} text-left font-mono`}>
                {months[index]}
              </td>
              {series.map((item) => (
                <td key={item.productCode} className={cellClass}>
                  {numberFormatter.format(item.values[index] ?? 0)}
                </td>
              ))}
              <td className={`${cellClass} font-semibold`}>
                {numberFormatter.format(rowTotal(index))}
              </td>
            </tr>
          ))}
        </tbody>

        <tfoot className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <td className={`${cellClass} text-left font-semibold`}>Celkem</td>
            {columnTotals.map((total, index) => (
              <td
                key={series[index].productCode}
                className={`${cellClass} font-semibold`}
              >
                {numberFormatter.format(total)}
              </td>
            ))}
            <td className={`${cellClass} font-semibold`}>
              {numberFormatter.format(grandTotal)}
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
};

export default ProductStatisticsTable;
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="ProductStatisticsTable" --watchAll=false
```

Expected: PASS, 6 tests.

If `getByRole("row", { name: /Celkem/ })` matches more than one row (the header also contains "Celkem"), narrow the query to the `tfoot` via `within(screen.getByRole("rowgroup", ...))` or add a `data-testid="totals-row"` to the footer row and assert on that instead. Adjust the test, not the component's markup semantics.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/product-statistics/ProductStatisticsTable.tsx \
        frontend/src/components/product-statistics/__tests__/ProductStatisticsTable.test.tsx
git commit -m "feat: add product statistics table"
```

---

### Task 11: The page shell

**Files:**
- Create: `frontend/src/components/pages/ProductStatistics.tsx`
- Test: `frontend/src/components/pages/__tests__/ProductStatistics.test.tsx`

**Interfaces:**
- Consumes: `useProductStatistics` + `StatisticsMetric` (Task 6); `ProductStatisticsFilter`, `defaultDateFrom`, `defaultDateTo`, `SelectedProduct` (Task 8); `ProductStatisticsChart` (Task 9); `ProductStatisticsTable` (Task 10); `LoadingState` / `ErrorState` from `../common/`; `useScreenView` from `../../telemetry/useScreenView`; `PAGE_CONTAINER_HEIGHT` from `../../constants/layout`
- Produces: default-exported `ProductStatistics` page component, routed in Task 12

Tab definitions — the metric, its Czech label and its y-axis label live in one array so a tab is a data row, not a branch:

| Tab key | Label | Metric | Y-axis label |
|---|---|---|---|
| `Sales` | Prodeje | `Sales` | Kusů prodáno |
| `Purchase` | Nákupy | `Purchase` | Kusů nakoupeno |
| `Consumption` | Spotřeba | `Consumption` | Množství spotřebováno |
| `Manufacture` | Výroba | `Manufacture` | Kusů vyrobeno |

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/pages/__tests__/ProductStatistics.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ProductStatistics from "../ProductStatistics";

const mockUseProductStatistics = jest.fn();

jest.mock("../../../api/hooks/useProductStatistics", () => ({
  useProductStatistics: (...args: any[]) => mockUseProductStatistics(...args),
}));

jest.mock("../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

jest.mock("../../product-statistics/ProductStatisticsFilter", () => {
  const actual = jest.requireActual(
    "../../product-statistics/ProductStatisticsFilter",
  );
  return {
    __esModule: true,
    ...actual,
    default: ({ onProductsChange }: any) => (
      <button
        onClick={() =>
          onProductsChange([{ productCode: "PROD-A", productName: "Krém" }])
        }
      >
        vybrat produkt
      </button>
    ),
  };
});

jest.mock("../../product-statistics/ProductStatisticsChart", () => ({
  __esModule: true,
  default: ({ yAxisLabel }: any) => (
    <div data-testid="chart">{yAxisLabel}</div>
  ),
}));

jest.mock("../../product-statistics/ProductStatisticsTable", () => ({
  __esModule: true,
  default: ({ series }: any) => (
    <div data-testid="table">{series.length} řad</div>
  ),
}));

describe("ProductStatistics page", () => {
  beforeEach(() => {
    mockUseProductStatistics.mockReset();
    mockUseProductStatistics.mockReturnValue({
      data: { months: ["2025-01"], products: [] },
      isLoading: false,
      isError: false,
      error: null,
    });
  });

  test("renders all four metric tabs", () => {
    render(<ProductStatistics />);

    expect(screen.getByRole("button", { name: "Prodeje" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Nákupy" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Spotřeba" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Výroba" })).toBeInTheDocument();
  });

  test("queries the Sales metric by default", () => {
    render(<ProductStatistics />);

    expect(mockUseProductStatistics).toHaveBeenCalledWith(
      [],
      "Sales",
      expect.any(String),
      expect.any(String),
    );
  });

  test("switching tabs re-queries with the new metric", () => {
    render(<ProductStatistics />);

    fireEvent.click(screen.getByRole("button", { name: "Výroba" }));

    expect(mockUseProductStatistics).toHaveBeenLastCalledWith(
      [],
      "Manufacture",
      expect.any(String),
      expect.any(String),
    );
  });

  test("switching tabs changes the chart's y-axis label", () => {
    render(<ProductStatistics />);

    expect(screen.getByTestId("chart")).toHaveTextContent("Kusů prodáno");

    fireEvent.click(screen.getByRole("button", { name: "Spotřeba" }));

    expect(screen.getByTestId("chart")).toHaveTextContent(
      "Množství spotřebováno",
    );
  });

  test("keeps the product selection when switching tabs", () => {
    render(<ProductStatistics />);

    fireEvent.click(screen.getByText("vybrat produkt"));
    fireEvent.click(screen.getByRole("button", { name: "Nákupy" }));

    expect(mockUseProductStatistics).toHaveBeenLastCalledWith(
      ["PROD-A"],
      "Purchase",
      expect.any(String),
      expect.any(String),
    );
  });

  test("shows a prompt instead of the chart when no products are selected", () => {
    render(<ProductStatistics />);

    expect(
      screen.getByText("Vyberte produkty pro zobrazení statistik"),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("chart")).not.toBeInTheDocument();
  });

  test("renders the error state when the query fails", () => {
    mockUseProductStatistics.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new Error("boom"),
    });

    render(<ProductStatistics />);

    fireEvent.click(screen.getByText("vybrat produkt"));

    expect(
      screen.getByText("Nepodařilo se načíst statistiky produktů"),
    ).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="pages/__tests__/ProductStatistics" --watchAll=false
```

Expected: FAIL — cannot resolve `../ProductStatistics`.

- [ ] **Step 3: Write the page**

Create `frontend/src/components/pages/ProductStatistics.tsx`:

```typescript
import React, { useState } from "react";
import { BarChart3 } from "lucide-react";
import {
  useProductStatistics,
  StatisticsMetric,
} from "../../api/hooks/useProductStatistics";
import ProductStatisticsFilter, {
  SelectedProduct,
  defaultDateFrom,
  defaultDateTo,
} from "../product-statistics/ProductStatisticsFilter";
import ProductStatisticsChart, {
  ProductStatisticsSeries,
} from "../product-statistics/ProductStatisticsChart";
import ProductStatisticsTable from "../product-statistics/ProductStatisticsTable";
import LoadingState from "../common/LoadingState";
import ErrorState from "../common/ErrorState";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";

interface MetricTab {
  metric: StatisticsMetric;
  label: string;
  yAxisLabel: string;
}

const METRIC_TABS: MetricTab[] = [
  { metric: "Sales", label: "Prodeje", yAxisLabel: "Kusů prodáno" },
  { metric: "Purchase", label: "Nákupy", yAxisLabel: "Kusů nakoupeno" },
  {
    metric: "Consumption",
    label: "Spotřeba",
    yAxisLabel: "Množství spotřebováno",
  },
  { metric: "Manufacture", label: "Výroba", yAxisLabel: "Kusů vyrobeno" },
];

const ProductStatistics: React.FC = () => {
  useScreenView("Catalog", "ProductStatistics");

  // Filter state is shared across tabs: switching metric keeps the selection and range.
  const [selectedProducts, setSelectedProducts] = useState<SelectedProduct[]>(
    [],
  );
  const [dateFrom, setDateFrom] = useState<string>(defaultDateFrom());
  const [dateTo, setDateTo] = useState<string>(defaultDateTo());
  const [activeMetric, setActiveMetric] = useState<StatisticsMetric>("Sales");

  const activeTab =
    METRIC_TABS.find((tab) => tab.metric === activeMetric) ?? METRIC_TABS[0];

  const productCodes = selectedProducts.map((product) => product.productCode);

  const { data, isLoading, isError } = useProductStatistics(
    productCodes,
    activeMetric,
    dateFrom,
    dateTo,
  );

  const months: string[] = data?.months ?? [];
  const series: ProductStatisticsSeries[] = (data?.products ?? []).map(
    (product: any) => ({
      productCode: product.productCode,
      productName: product.productName,
      values: product.values ?? [],
    }),
  );

  const hasSelection = selectedProducts.length > 0;

  const renderContent = () => {
    if (!hasSelection) {
      return (
        <div className="flex items-center justify-center h-96">
          <div className="text-center text-gray-500 dark:text-graphite-muted">
            <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300 dark:text-graphite-faint" />
            <p>Vyberte produkty pro zobrazení statistik</p>
          </div>
        </div>
      );
    }

    if (isLoading) {
      return <LoadingState />;
    }

    if (isError) {
      return <ErrorState message="Nepodařilo se načíst statistiky produktů" />;
    }

    return (
      <>
        <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-4 mb-4">
          <ProductStatisticsChart
            months={months}
            series={series}
            yAxisLabel={activeTab.yAxisLabel}
          />
        </div>
        <ProductStatisticsTable months={months} series={series} />
      </>
    );
  };

  return (
    <div className={`flex flex-col ${PAGE_CONTAINER_HEIGHT}`}>
      <h1 className="text-xl font-semibold text-gray-900 dark:text-graphite-text mb-4">
        Statistiky produktů
      </h1>

      <ProductStatisticsFilter
        selectedProducts={selectedProducts}
        onProductsChange={setSelectedProducts}
        dateFrom={dateFrom}
        dateTo={dateTo}
        onDateFromChange={setDateFrom}
        onDateToChange={setDateTo}
      />

      <div className="flex border-b border-gray-200 dark:border-graphite-border mb-4">
        {METRIC_TABS.map((tab) => (
          <button
            key={tab.metric}
            onClick={() => setActiveMetric(tab.metric)}
            className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
              activeMetric === tab.metric
                ? "border-indigo-500 text-indigo-600 dark:text-graphite-accent dark:border-graphite-accent"
                : "border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-auto">{renderContent()}</div>
    </div>
  );
};

export default ProductStatistics;
```

If `LoadingState` or `PAGE_CONTAINER_HEIGHT` have a different export shape than assumed, read the module and match it — do not change those shared modules.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="pages/__tests__/ProductStatistics" --watchAll=false
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/ProductStatistics.tsx \
        frontend/src/components/pages/__tests__/ProductStatistics.test.tsx
git commit -m "feat: add product statistics page shell"
```

---

### Task 12: Routing, menu and access

**Files:**
- Modify: `frontend/src/App.tsx` (import block; route block near `/products/margins`, ~line 427)
- Modify: `frontend/src/components/layout/Sidebar.tsx` (Produkty section, ~line 165–174)
- Modify: `access-matrix.json` (routes array, near the `/catalog` entry, ~line 45)

**Interfaces:**
- Consumes: `ProductStatistics` page (Task 11)
- Produces: reachable route `/products/statistics`

- [ ] **Step 1: Add the route**

In `frontend/src/App.tsx`, import the page following the file's existing import style for pages (check whether neighbours use `React.lazy` or a direct import, and match it):

```typescript
import ProductStatistics from "./components/pages/ProductStatistics";
```

and add the route immediately after the `/products/margins` route:

```tsx
<Route path="/products/statistics" element={guard("/products/statistics", <ProductStatistics />)} />
```

- [ ] **Step 2: Add the menu item**

In `frontend/src/components/layout/Sidebar.tsx`, add to the `produkty` section's `items` array, after the `marze-produktu` entry:

```typescript
{ id: "statistiky-produktu", name: "Statistiky", href: "/products/statistics", key: "/products/statistics" },
```

- [ ] **Step 3: Add the access-matrix route entry**

In `access-matrix.json`, add to the `routes` array immediately after the `/catalog` entry:

```json
{ "path": "/products/statistics", "requires": [{ "feature": "Products_Catalog", "level": "Read" }] },
```

No new entry in `features` — the screen reuses the existing `Products_Catalog` key, so no group seeding and no role change is needed. The backend endpoint is already covered by the controller-level `[FeatureAuthorize(Feature.Products_Catalog)]`.

- [ ] **Step 4: Verify the frontend builds and lints**

```bash
cd frontend && CI=false npm run build && npm run lint
```

Expected: build succeeds, lint clean. `npx tsc --noEmit` is not a valid substitute — it false-greens in this repo.

- [ ] **Step 5: Run the whole frontend test suite**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false
```

Expected: PASS — including every pre-existing test. If a shell-component test fails on a missing context mock, that test needs the new context mocked; do not change the page to work around it.

- [ ] **Step 6: Run the whole backend suite**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false
```

Expected: PASS. `Category=Integration` tests are excluded in CI; running them locally needs `podman machine start`.

- [ ] **Step 7: Format and commit**

```bash
dotnet format Anela.Heblo.sln
git add frontend/src/App.tsx frontend/src/components/layout/Sidebar.tsx access-matrix.json
git commit -m "feat: route and menu entry for product statistics"
```

---

## Final Verification

- [ ] `dotnet build Anela.Heblo.sln` — succeeds
- [ ] `dotnet format Anela.Heblo.sln` — no diff left uncommitted
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — passes
- [ ] `cd frontend && CI=false npm run build` — succeeds
- [ ] `cd frontend && npm run lint` — clean
- [ ] `cd frontend && CI=true npx react-scripts test --watchAll=false` — passes
- [ ] Manual smoke: open `/products/statistics`, select two products, set a range, and confirm each of the four tabs renders a chart line per product and a table whose column totals match the chart

No E2E test is added. The Playwright suite runs nightly against deployed staging and cannot validate uncommitted frontend work.
