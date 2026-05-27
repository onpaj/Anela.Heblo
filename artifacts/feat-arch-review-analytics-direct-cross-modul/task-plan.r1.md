# Decouple Analytics from Catalog — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate Analytics's direct dependency on Catalog (`ProductType`, `ICatalogRepository`, `CatalogAggregate`, `MarginData`, `PurchaseHistory`) by introducing an Analytics-owned `IAnalyticsProductSource` contract implemented by a Catalog-side adapter, and lock the new boundary with an architectural test that covers both the Application and Domain assemblies.

**Architecture:** Apply the consumer-owns-contract / provider-owns-adapter pattern documented in `docs/architecture/development_guidelines.md` §"Cross-Module Communication Example" and already used by Leaflet→KnowledgeBase (2026-05-15), Logistics→Manufacture (2026-05-16), PackingMaterials→Invoices (2026-05-21), Purchase→Catalog (2026-05-24), Article→KnowledgeBase (2026-05-25), and ExpeditionListArchive→ExpeditionList (2026-05-26). Analytics declares `IAnalyticsProductSource` in `Application/Features/Analytics/Contracts/` and `AnalyticsProductType` in `Domain/Features/Analytics/`; Catalog provides an `internal sealed CatalogAnalyticsSourceAdapter` in `Application/Features/Catalog/Infrastructure/` that owns the `CatalogAggregate → AnalyticsProduct` mapping previously duplicated across two methods of `AnalyticsRepository`. `CatalogModule.AddCatalogModule` registers the binding (Transient, to match `ICatalogRepository`). The architectural test extends `ModuleBoundaryRule` with an optional `InspectedAssembly` field so the new rule can also catch the `AnalyticsProduct.Type` violation that lives in the Domain assembly. No HTTP API, DTO, or schema changes.

**Tech Stack:** .NET 8, C# (nullable enabled), xUnit, FluentAssertions, Moq, MediatR, `Microsoft.Extensions.DependencyInjection`. No new NuGet packages, no migrations.

---

## File Structure

**Files to create:**

| Path | Responsibility |
|------|----------------|
| `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductType.cs` | Analytics-owned enum mirroring exactly the two `Catalog.ProductType` values Analytics consumes today (`Product`, `Goods`). |
| `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/IAnalyticsProductSource.cs` | Cross-module contract — two methods (`StreamProductsWithSalesAsync`, `GetProductAnalysisDataAsync`) that expose only Analytics-owned types. |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` | `internal sealed` adapter — depends on `ICatalogRepository`, owns the `CatalogAggregate → AnalyticsProduct` mapping (extracted into a single private helper), translates `AnalyticsProductType[] → ProductType[]` at the boundary. |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` | xUnit + Moq tests for the adapter — first-ever coverage of the previously-untested mapping. |

**Files to modify:**

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs` | Drop `using Anela.Heblo.Domain.Features.Catalog;`. Retype `Type` from `ProductType` to `AnalyticsProductType`. |
| `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/IAnalyticsRepository.cs` | Drop `using Anela.Heblo.Domain.Features.Catalog;`. Change `ProductType[] productTypes` → `AnalyticsProductType[] productTypes` on `StreamProductsWithSalesAsync` and `GetGroupMarginTotalsAsync`. |
| `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` | Drop `using Anela.Heblo.Domain.Features.Catalog;`. Replace `ICatalogRepository _catalogRepository` ctor dependency with `IAnalyticsProductSource _productSource`. Delete the mapping bodies of `StreamProductsWithSalesAsync` (lines 30–122) and `GetProductAnalysisDataAsync` (lines 156–232) and delegate to the contract. Retype `productTypes` to `AnalyticsProductType[]`. `GetGroupMarginTotalsAsync`, `GetInvoiceImportStatisticsAsync`, `GetBankStatementImportStatisticsAsync` are kept; only the `productTypes` parameter type changes on `GetGroupMarginTotalsAsync`. |
| `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` | Drop `using Anela.Heblo.Domain.Features.Catalog;`. Change `new[] { ProductType.Product, ProductType.Goods }` → `new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods }`. |
| `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` | Drop `using Anela.Heblo.Domain.Features.Catalog;`. Same enum array replacement. |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | Add `using Anela.Heblo.Application.Features.Analytics.Contracts;`. Register `services.AddTransient<IAnalyticsProductSource, CatalogAnalyticsSourceAdapter>();` immediately after the existing `IMaterialCatalogService` registration (line 45). |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Extend `ModuleBoundaryRule` record with optional `string InspectedAssembly = "Anela.Heblo.Application"`. Replace the hard-coded `Assembly.Load("Anela.Heblo.Application")` call with `Assembly.Load(rule.InspectedAssembly)`. Add two new theory rows: `Analytics (Application) -> Catalog` and `Analytics (Domain) -> Catalog`. Empty allowlists. |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` | Drop `using Anela.Heblo.Domain.Features.Catalog;`. Every `Type = ProductType.Product` (10 occurrences at lines 128, 141, 280, 293, 337, 350, 395, 408, 450, 463) → `Type = AnalyticsProductType.Product`. |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` | Same — `using Catalog` removed; `Type = ProductType.Product` (lines 61, 75) → `AnalyticsProductType.Product`. |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` | Same — `using Catalog` removed; `Type = ProductType.Product` (lines 55, 196, 236, 279) → `AnalyticsProductType.Product`. |

**Files NOT touched:**

- `backend/src/Anela.Heblo.Domain/Features/Catalog/*` — Catalog domain types (`CatalogAggregate`, `ProductType`, `MarginData`, `ICatalogRepository`, …) remain unchanged.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — the analytics binding is not registered here; the provider (Catalog) owns the registration per the documented pattern. The stale comment on line 28 ("`IMarginCalculationService` is registered by CatalogModule and injected here") is out of scope per arch-review §"Specification Amendments" item 7.
- `GetBankStatementImportStatistics`, `GetInvoiceImportStatistics` handlers — already independent of Catalog.
- Frontend, OpenAPI clients, controllers — no public API change.

---

## Task 1: Add the failing architecture-test rules for Analytics → Catalog

This locks the boundary in place before any source changes. Subsequent tasks turn it green. Two theory rows are needed because the existing `AnalyticsProduct.Type` violation lives in `Anela.Heblo.Domain` (the current test inspects only the Application assembly). The rule record gains an optional `InspectedAssembly` field so existing rules keep their default behavior with no churn.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Extend `ModuleBoundaryRule` with an `InspectedAssembly` field**

Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. Replace the `public sealed record ModuleBoundaryRule(...)` declaration (currently lines 15–19) with:

```csharp
    public sealed record ModuleBoundaryRule(
        string Name,
        string InspectedNamespacePrefix,
        IReadOnlyList<string> ForbiddenNamespacePrefixes,
        IReadOnlySet<string> Allowlist,
        string InspectedAssembly = "Anela.Heblo.Application");
```

- [ ] **Step 2: Use `rule.InspectedAssembly` in the theory method**

Locate the line that reads:

```csharp
        var assembly = Assembly.Load("Anela.Heblo.Application");
```

inside `Consumer_types_should_not_reference_provider_owned_namespaces` (around line 167). Replace it with:

```csharp
        var assembly = Assembly.Load(rule.InspectedAssembly);
```

Leave the other `Assembly.Load("Anela.Heblo.Application")` calls in `Logistics_types_should_not_reference_Purchase_owned_namespaces` and `Application_types_should_not_reference_AspNetCore_namespaces` unchanged — only the theory method becomes data-driven on `InspectedAssembly`.

- [ ] **Step 3: Append the two new theory rows to `Rules()`**

Inside the `Rules()` method, after the `ExpeditionListArchive -> ExpeditionList` row (currently lines 152–161) and before the closing `};` of the TheoryData initializer, append:

```csharp
        new ModuleBoundaryRule(
            Name: "Analytics (Application) -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Analytics (Domain) -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal),
            InspectedAssembly: "Anela.Heblo.Domain"),
```

- [ ] **Step 4: Verify the new theory cases currently fail**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: the two new theory cases FAIL with violations including (non-exhaustive):

`Analytics (Application) -> Catalog`:
- `…AnalyticsRepository -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository`
- `…AnalyticsRepository -> Anela.Heblo.Domain.Features.Catalog.ProductType`
- `…IAnalyticsRepository -> Anela.Heblo.Domain.Features.Catalog.ProductType`
- `…GetMarginReportHandler -> Anela.Heblo.Domain.Features.Catalog.ProductType`
- `…GetProductMarginSummaryHandler -> Anela.Heblo.Domain.Features.Catalog.ProductType`

`Analytics (Domain) -> Catalog`:
- `…AnalyticsProduct -> Anela.Heblo.Domain.Features.Catalog.ProductType`

All other existing theory cases (Leaflet, Article, Logistics, PackingMaterials, Purchase, ExpeditionListArchive) and the two `[Fact]` tests must still PASS.

- [ ] **Step 5: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(arch): add failing Analytics -> Catalog boundary rules"
```

---

## Task 2: Create the `AnalyticsProductType` enum

Pure type declaration in Analytics's Domain namespace. Mirrors exactly the two `Catalog.ProductType` values used by Analytics today (`Product`, `Goods`) — `Material`, `SemiProduct`, `Set`, `UNDEFINED` are intentionally omitted because Analytics never passes them (verified at `GetMarginReportHandler.cs:57` and `GetProductMarginSummaryHandler.cs:37`). No tests for an inert enum.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductType.cs`

- [ ] **Step 1: Create the enum**

Write the file exactly:

```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Analytics-owned classification of products that margin reporting cares about.
/// Mirrors the subset of <c>Anela.Heblo.Domain.Features.Catalog.ProductType</c>
/// that Analytics consumes today (Product, Goods). If Analytics ever needs
/// another value, mirror it here and update the AnalyticsProductType ->
/// ProductType translation in CatalogAnalyticsSourceAdapter.
/// </summary>
public enum AnalyticsProductType
{
    Product,
    Goods,
}
```

- [ ] **Step 2: Verify it compiles**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductType.cs
git commit -m "feat(analytics): add AnalyticsProductType enum"
```

---

## Task 3: Create the `IAnalyticsProductSource` contract

Pure interface declaration in Analytics's Contracts folder, expressed entirely in Analytics-owned types (`AnalyticsProduct`, `AnalyticsProductType`). No tests — this is an inert abstraction; behavior is covered by the adapter tests in Task 4.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/IAnalyticsProductSource.cs`

- [ ] **Step 1: Create the interface**

Write the file exactly:

```csharp
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Contracts;

/// <summary>
/// Analytics-owned read abstraction over catalog data. Implemented by the
/// Catalog module via <c>CatalogAnalyticsSourceAdapter</c> per the cross-module
/// communication pattern in <c>docs/architecture/development_guidelines.md</c>.
/// All inputs and outputs are Analytics-owned types; the adapter owns the
/// translation between <see cref="AnalyticsProductType"/> and Catalog's ProductType
/// and the projection from CatalogAggregate to <see cref="AnalyticsProduct"/>.
/// </summary>
public interface IAnalyticsProductSource
{
    /// <summary>
    /// Streams products of the requested types that have sales in the given
    /// period, projected to <see cref="AnalyticsProduct"/>. Items are yielded
    /// one by one; the underlying call still materialises a list internally,
    /// but the iteration boundary preserves the surface that callers rely on.
    /// </summary>
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single product projected to <see cref="AnalyticsProduct"/>,
    /// or <c>null</c> if the product is unknown. Matches the soft-fallback
    /// semantics of <c>ICatalogRepository.GetByIdAsync</c>.
    /// </summary>
    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify it compiles**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/IAnalyticsProductSource.cs
git commit -m "feat(analytics): add IAnalyticsProductSource contract"
```

---

## Task 4: Create `CatalogAnalyticsSourceAdapter` and its tests (TDD)

The adapter owns the entire `CatalogAggregate → AnalyticsProduct` mapping that currently lives duplicated across two methods of `AnalyticsRepository` (streaming path at lines 52–116, single-product path at lines 168–231 of the original `AnalyticsRepository.cs`). The two paths differ in exactly one place: the streaming path filters `SalesHistory` by the request period, while the single-product path does not (line 223 — preserve as-is per FR-7; the asymmetry is acknowledged in the spec's "Out of Scope" and arch-review §"Mapping consolidation"). The adapter consolidates the M0/M1/M2 mapping into a single helper and keeps the sales-filter difference visible at the two call sites. The `AnalyticsProductType[] → ProductType[]` conversion happens at the entry point.

This task writes the tests FIRST (RED), then the adapter (GREEN), per project TDD convention.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs`

- [ ] **Step 1: Write the failing adapter tests**

Write `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` exactly:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogAnalyticsSourceAdapterTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private CatalogAnalyticsSourceAdapter CreateAdapter() => new(_repository.Object);

    private static CatalogAggregate MakeAggregate(
        string productCode = "P1",
        string productName = "Product 1",
        ProductType type = ProductType.Product,
        string? family = "FamA",
        string? category = "CatA",
        decimal eshopPriceWithoutVat = 200m,
        IEnumerable<CatalogSaleRecord>? sales = null,
        IEnumerable<CatalogPurchaseRecord>? purchases = null,
        IEnumerable<KeyValuePair<DateTime, MarginData>>? monthlyMargins = null,
        MarginAverages? averages = null)
    {
        var aggregate = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Type = type,
            ProductFamily = family,
            ProductCategory = category,
            EshopPrice = new ProductPriceEshop { PriceWithoutVat = eshopPriceWithoutVat },
            SalesHistory = sales?.ToList() ?? new List<CatalogSaleRecord>(),
            PurchaseHistory = purchases?.ToList() ?? new List<CatalogPurchaseRecord>(),
        };

        aggregate.Margins = new MarginAggregate
        {
            MonthlyData = monthlyMargins?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ?? new Dictionary<DateTime, MarginData>(),
            Averages = averages ?? new MarginAverages
            {
                M0 = new MarginAmount { Amount = 0m, Percentage = 0m, CostLevel = 0m },
                M1 = new MarginAmount(),
                M1_A = new MarginAmount(),
                M2 = new MarginAmount(),
            },
        };

        return aggregate;
    }

    private static MarginData MarginAt(decimal m0Amount, decimal m1Amount, decimal m2Amount, decimal m0Cost = 0m, decimal m1aCost = 0m)
    {
        return new MarginData
        {
            M0 = new MarginAmount { Amount = m0Amount, Percentage = m0Amount, CostLevel = m0Cost },
            M1 = new MarginAmount { Amount = m1Amount, Percentage = m1Amount },
            M1_A = new MarginAmount { CostLevel = m1aCost },
            M2 = new MarginAmount { Amount = m2Amount, Percentage = m2Amount },
        };
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_translates_AnalyticsProductType_array_to_Catalog_ProductType_array_at_boundary()
    {
        ProductType[]? captured = null;
        _repository
            .Setup(r => r.GetProductsWithSalesInPeriod(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, ProductType[], CancellationToken>((_, _, t, _) => captured = t)
            .ReturnsAsync(new List<CatalogAggregate>());

        var adapter = CreateAdapter();

        await foreach (var _ in adapter.StreamProductsWithSalesAsync(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods },
            CancellationToken.None))
        {
            // drain
        }

        captured.Should().BeEquivalentTo(new[] { ProductType.Product, ProductType.Goods });
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_maps_M0_M1_M2_amounts_and_percentages_from_latest_margin_entry_in_period()
    {
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 12, 31);
        var aggregate = MakeAggregate(
            monthlyMargins: new[]
            {
                new KeyValuePair<DateTime, MarginData>(new DateTime(2024, 3, 1), MarginAt(10m, 20m, 30m, m0Cost: 5m, m1aCost: 7m)),
                new KeyValuePair<DateTime, MarginData>(new DateTime(2024, 10, 1), MarginAt(11m, 21m, 31m, m0Cost: 6m, m1aCost: 8m)),
            });

        _repository
            .Setup(r => r.GetProductsWithSalesInPeriod(from, to, It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { aggregate });

        var adapter = CreateAdapter();
        var results = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(from, to, new[] { AnalyticsProductType.Product }, CancellationToken.None))
        {
            results.Add(item);
        }

        results.Should().HaveCount(1);
        var p = results[0];
        p.M0Amount.Should().Be(11m);
        p.M1Amount.Should().Be(21m);
        p.M2Amount.Should().Be(31m);
        p.M0Percentage.Should().Be(11m);
        p.M1Percentage.Should().Be(21m);
        p.M2Percentage.Should().Be(31m);
        p.MarginAmount.Should().Be(11m);
        p.MaterialCost.Should().Be(6m);
        p.HandlingCost.Should().Be(8m);
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_falls_back_to_averages_when_no_monthly_margin_data_present()
    {
        var aggregate = MakeAggregate(
            monthlyMargins: Array.Empty<KeyValuePair<DateTime, MarginData>>(),
            averages: new MarginAverages
            {
                M0 = new MarginAmount { Amount = 42m, Percentage = 0m, CostLevel = 0m },
                M1 = new MarginAmount(),
                M1_A = new MarginAmount(),
                M2 = new MarginAmount(),
            });

        _repository
            .Setup(r => r.GetProductsWithSalesInPeriod(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { aggregate });

        var adapter = CreateAdapter();
        var results = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            new[] { AnalyticsProductType.Product },
            CancellationToken.None))
        {
            results.Add(item);
        }

        results[0].MarginAmount.Should().Be(42m);
        results[0].M0Amount.Should().Be(0m);
        results[0].MaterialCost.Should().Be(0m);
        results[0].HandlingCost.Should().Be(0m);
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_filters_SalesHistory_to_the_requested_period()
    {
        var from = new DateTime(2024, 6, 1);
        var to = new DateTime(2024, 8, 31);
        var aggregate = MakeAggregate(
            sales: new[]
            {
                new CatalogSaleRecord { Date = new DateTime(2024, 1, 15), AmountB2B = 1, AmountB2C = 2 },
                new CatalogSaleRecord { Date = new DateTime(2024, 7, 15), AmountB2B = 3, AmountB2C = 4 },
                new CatalogSaleRecord { Date = new DateTime(2024, 11, 15), AmountB2B = 5, AmountB2C = 6 },
            });

        _repository
            .Setup(r => r.GetProductsWithSalesInPeriod(from, to, It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { aggregate });

        var adapter = CreateAdapter();
        var results = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(from, to, new[] { AnalyticsProductType.Product }, CancellationToken.None))
        {
            results.Add(item);
        }

        results[0].SalesHistory.Should().HaveCount(1);
        results[0].SalesHistory[0].Date.Should().Be(new DateTime(2024, 7, 15));
        results[0].SalesHistory[0].AmountB2B.Should().Be(3);
        results[0].SalesHistory[0].AmountB2C.Should().Be(4);
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_takes_latest_purchase_price_from_purchase_history()
    {
        var aggregate = MakeAggregate(
            purchases: new[]
            {
                new CatalogPurchaseRecord { Date = new DateTime(2024, 3, 1), PricePerPiece = 100m },
                new CatalogPurchaseRecord { Date = new DateTime(2024, 9, 1), PricePerPiece = 150m },
                new CatalogPurchaseRecord { Date = new DateTime(2024, 5, 1), PricePerPiece = 120m },
            });

        _repository
            .Setup(r => r.GetProductsWithSalesInPeriod(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { aggregate });

        var adapter = CreateAdapter();
        var results = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            new[] { AnalyticsProductType.Product },
            CancellationToken.None))
        {
            results.Add(item);
        }

        results[0].PurchasePrice.Should().Be(150m);
    }

    [Fact]
    public async Task GetProductAnalysisDataAsync_returns_null_when_repository_returns_null()
    {
        _repository
            .Setup(r => r.GetByIdAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var adapter = CreateAdapter();
        var result = await adapter.GetProductAnalysisDataAsync(
            "MISSING",
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProductAnalysisDataAsync_preserves_unfiltered_SalesHistory_for_single_product_path()
    {
        // FR-7 / arch-review Risk row: the single-product path historically does NOT filter
        // SalesHistory by the requested period (line 223 of original AnalyticsRepository.cs),
        // unlike the streaming path. Preserve this asymmetry verbatim — fixing it is out of scope.
        var aggregate = MakeAggregate(
            productCode: "P1",
            sales: new[]
            {
                new CatalogSaleRecord { Date = new DateTime(2024, 1, 15), AmountB2B = 1, AmountB2C = 2 },
                new CatalogSaleRecord { Date = new DateTime(2024, 7, 15), AmountB2B = 3, AmountB2C = 4 },
                new CatalogSaleRecord { Date = new DateTime(2024, 11, 15), AmountB2B = 5, AmountB2C = 6 },
            });

        _repository
            .Setup(r => r.GetByIdAsync("P1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregate);

        var adapter = CreateAdapter();
        var result = await adapter.GetProductAnalysisDataAsync(
            "P1",
            new DateTime(2024, 6, 1),
            new DateTime(2024, 8, 31),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.SalesHistory.Should().HaveCount(3); // all three preserved, not filtered
    }

    [Fact]
    public async Task GetProductAnalysisDataAsync_maps_pricing_purchase_price_and_margin_fields()
    {
        var aggregate = MakeAggregate(
            productCode: "P1",
            productName: "Single Product",
            family: "Fam",
            category: "Cat",
            eshopPriceWithoutVat: 333m,
            purchases: new[]
            {
                new CatalogPurchaseRecord { Date = new DateTime(2024, 4, 1), PricePerPiece = 50m },
                new CatalogPurchaseRecord { Date = new DateTime(2024, 9, 1), PricePerPiece = 75m },
            },
            monthlyMargins: new[]
            {
                new KeyValuePair<DateTime, MarginData>(new DateTime(2024, 7, 1), MarginAt(10m, 20m, 30m, m0Cost: 4m, m1aCost: 6m)),
            });

        _repository
            .Setup(r => r.GetByIdAsync("P1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregate);

        var adapter = CreateAdapter();
        var result = await adapter.GetProductAnalysisDataAsync(
            "P1",
            new DateTime(2024, 6, 1),
            new DateTime(2024, 8, 31),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("P1");
        result.ProductName.Should().Be("Single Product");
        result.ProductFamily.Should().Be("Fam");
        result.ProductCategory.Should().Be("Cat");
        result.SellingPrice.Should().Be(333m);
        result.EshopPriceWithoutVat.Should().Be(333m);
        result.PurchasePrice.Should().Be(75m);
        result.MarginAmount.Should().Be(10m);
        result.M0Amount.Should().Be(10m);
        result.M1Amount.Should().Be(20m);
        result.M2Amount.Should().Be(30m);
        result.MaterialCost.Should().Be(4m);
        result.HandlingCost.Should().Be(6m);
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail to compile**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore
```

Expected: build FAILS with `CS0246` style errors — `CatalogAnalyticsSourceAdapter` does not exist yet.

If field names on existing Catalog types (`CatalogAggregate`, `MarginData`, `MarginAverages`, `MarginAmount`, `CatalogSaleRecord`, `CatalogPurchaseRecord`, `ProductPriceEshop`) differ from what's used above, fix the **test** to use the real type members (read the actual files under `backend/src/Anela.Heblo.Domain/Features/Catalog/`). Do **not** invent fields on production code to make the test compile.

- [ ] **Step 3: Implement the adapter**

Write `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` exactly:

```csharp
using System.Runtime.CompilerServices;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogAnalyticsSourceAdapter : IAnalyticsProductSource
{
    private const int BatchSize = 100;

    private readonly ICatalogRepository _catalogRepository;

    public CatalogAnalyticsSourceAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var catalogProductTypes = MapProductTypes(productTypes);

        var allProducts = await _catalogRepository.GetProductsWithSalesInPeriod(
            fromDate, toDate, catalogProductTypes, cancellationToken);

        for (int i = 0; i < allProducts.Count; i += BatchSize)
        {
            var batch = allProducts.Skip(i).Take(BatchSize);

            foreach (var product in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filteredSales = product.SalesHistory
                    .Where(s => s.Date >= fromDate && s.Date <= toDate)
                    .Select(s => new SalesDataPoint
                    {
                        Date = s.Date,
                        AmountB2B = s.AmountB2B,
                        AmountB2C = s.AmountB2C,
                    })
                    .ToList();

                yield return MapToAnalyticsProduct(product, fromDate, toDate, filteredSales);
            }

            GC.Collect();
        }
    }

    public async Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var product = await _catalogRepository.GetByIdAsync(productId, cancellationToken);
        if (product == null)
            return null;

        // Preserve verbatim from original AnalyticsRepository.GetProductAnalysisDataAsync (line 223):
        // single-product path does NOT filter SalesHistory by period, unlike the streaming path.
        var unfilteredSales = product.SalesHistory
            .Select(s => new SalesDataPoint
            {
                Date = s.Date,
                AmountB2B = s.AmountB2B,
                AmountB2C = s.AmountB2C,
            })
            .ToList();

        return MapToAnalyticsProduct(product, fromDate, toDate, unfilteredSales);
    }

    private static AnalyticsProduct MapToAnalyticsProduct(
        CatalogAggregate product,
        DateTime fromDate,
        DateTime toDate,
        List<SalesDataPoint> salesHistory)
    {
        var marginData = product.Margins;

        var relevantMargins = marginData.MonthlyData
            .Where(m => m.Key >= fromDate && m.Key <= toDate)
            .ToList();

        var latestMarginEntry = relevantMargins.LastOrDefault();
        if (latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>)))
        {
            latestMarginEntry = marginData.MonthlyData.LastOrDefault();
        }

        bool hasMargin = !latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>));

        var marginAmount = hasMargin
            ? latestMarginEntry.Value.M0.Amount
            : marginData.Averages.M0.Amount;
        var materialCost = hasMargin ? latestMarginEntry.Value.M0.CostLevel : 0m;
        var handlingCost = hasMargin ? latestMarginEntry.Value.M1_A.CostLevel : 0m;

        var latestPurchase = product.PurchaseHistory?.OrderByDescending(p => p.Date).FirstOrDefault();
        var purchasePrice = latestPurchase?.PricePerPiece ?? 0m;

        return new AnalyticsProduct
        {
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            Type = MapProductType(product.Type),
            ProductFamily = product.ProductFamily,
            ProductCategory = product.ProductCategory,
            MarginAmount = marginAmount,

            M0Amount = hasMargin ? latestMarginEntry.Value.M0.Amount : 0m,
            M1Amount = hasMargin ? latestMarginEntry.Value.M1.Amount : 0m,
            M2Amount = hasMargin ? latestMarginEntry.Value.M2.Amount : 0m,

            M0Percentage = hasMargin ? latestMarginEntry.Value.M0.Percentage : 0m,
            M1Percentage = hasMargin ? latestMarginEntry.Value.M1.Percentage : 0m,
            M2Percentage = hasMargin ? latestMarginEntry.Value.M2.Percentage : 0m,

            SellingPrice = product.EshopPrice?.PriceWithoutVat ?? 0m,
            EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat,
            PurchasePrice = purchasePrice,

            MaterialCost = materialCost,
            HandlingCost = handlingCost,
            SalesHistory = salesHistory,
        };
    }

    private static ProductType[] MapProductTypes(AnalyticsProductType[] types) =>
        types.Select(MapProductTypeToCatalog).ToArray();

    private static ProductType MapProductTypeToCatalog(AnalyticsProductType type) => type switch
    {
        AnalyticsProductType.Product => ProductType.Product,
        AnalyticsProductType.Goods => ProductType.Goods,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type),
            type,
            "AnalyticsProductType has no Catalog.ProductType counterpart. " +
            "Mirror the value in AnalyticsProductType.cs and extend this switch."),
    };

    private static AnalyticsProductType MapProductType(ProductType type) => type switch
    {
        ProductType.Product => AnalyticsProductType.Product,
        ProductType.Goods => AnalyticsProductType.Goods,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type),
            type,
            "Adapter only projects Product and Goods. " +
            "ICatalogRepository.GetProductsWithSalesInPeriod filtering must ensure no other types are returned."),
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogAnalyticsSourceAdapterTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: all 8 adapter tests PASS.

If a test fails because a referenced Catalog type/field name in the test differs from production (e.g., `CatalogSaleRecord.AmountB2B` vs. another name), correct the **test** — the adapter mapping mirrors the original `AnalyticsRepository.cs` lines 52–116 and 168–231 exactly, so the production mapping is correct by construction.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs
git commit -m "feat(catalog): add CatalogAnalyticsSourceAdapter implementing IAnalyticsProductSource"
```

---

## Task 5: Register the adapter binding in `CatalogModule`

Single-line DI registration. The lifetime is **Transient**, matching `ICatalogRepository` (`CatalogModule.cs:41`) — the adapter is stateless and merely delegates, so its lifetime should match the dependency it wraps (per arch-review Decision 3). Spec FR-4's wording "lifetime to match existing Catalog repository registrations" supports Transient over Scoped.

The application still doesn't use `IAnalyticsProductSource` after this task — Analytics's `AnalyticsRepository` still injects `ICatalogRepository` directly. The arch test still fails. This task is intentionally small to keep DI changes reviewable in isolation.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

- [ ] **Step 1: Add the using directive**

At the top of `CatalogModule.cs`, add `using Anela.Heblo.Application.Features.Analytics.Contracts;` to the import block (alphabetical placement: between `Anela.Heblo.Application.Features.Catalog.Validators;` and `Anela.Heblo.Domain.Features.Catalog;`).

- [ ] **Step 2: Register the binding**

Locate the existing line (currently line 45):

```csharp
        services.AddScoped<IMaterialCatalogService, PurchaseMaterialCatalogAdapter>();
```

Immediately after it, add:

```csharp
        services.AddTransient<IAnalyticsProductSource, CatalogAnalyticsSourceAdapter>();
```

- [ ] **Step 3: Verify build succeeds**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat(catalog): register CatalogAnalyticsSourceAdapter binding"
```

---

## Task 6: Switch Analytics consumers to the new contract (the type-swap commit)

This is the cohesive refactor that flips Analytics off Catalog. All of the following must move together because they form one compile-unit:

- `AnalyticsProduct.Type` is retyped from `Catalog.ProductType` to `AnalyticsProductType`.
- `IAnalyticsRepository`'s `productTypes` parameter type changes on two methods.
- `AnalyticsRepository`'s constructor replaces `ICatalogRepository` with `IAnalyticsProductSource`; the duplicated mapping bodies (originally lines 30–122 and 156–232) are deleted and replaced with delegation; `productTypes` parameter type updated on the two methods that take it.
- Both handlers (`GetMarginReportHandler`, `GetProductMarginSummaryHandler`) replace their `new[] { ProductType.Product, ProductType.Goods }` arrays with `new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods }`.
- Three handler test files update their `Type = ProductType.Product` initialisers to `Type = AnalyticsProductType.Product`.

Existing handler tests already mock `IAnalyticsRepository` (verified — no test mocks `ICatalogRepository` in the Analytics test folder), so the test surface changes are limited to the enum rename. No new mocks are needed for handler tests — `IAnalyticsProductSource` is only consumed inside `AnalyticsRepository`, which is mocked at the `IAnalyticsRepository` level above.

After this commit:
- `dotnet build` passes
- The `Analytics (Application) -> Catalog` and `Analytics (Domain) -> Catalog` arch theory rows turn GREEN
- All existing Analytics tests still pass with assertion text unchanged (only enum type changed)

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/IAnalyticsRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`

- [ ] **Step 1: Update `AnalyticsProduct.cs`**

Remove the line `using Anela.Heblo.Domain.Features.Catalog;` at the top. Change the `Type` property:

From:

```csharp
    public required ProductType Type { get; init; }
```

To:

```csharp
    public required AnalyticsProductType Type { get; init; }
```

No other changes — `SalesHistory`, `SalesDataPoint`, `DateRange`, `MarginCalculationResult` keep their current shape (already Analytics-owned).

- [ ] **Step 2: Update `IAnalyticsRepository.cs`**

Remove the line `using Anela.Heblo.Domain.Features.Catalog;` at the top. In both `StreamProductsWithSalesAsync` and `GetGroupMarginTotalsAsync`, change the parameter `ProductType[] productTypes` to `AnalyticsProductType[] productTypes`. The full file becomes:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Infrastructure;

/// <summary>
/// Analytics-specific repository with streaming capabilities
/// Prevents memory issues by avoiding loading all data at once
/// </summary>
public interface IAnalyticsRepository
{
    /// <summary>
    /// Streams products with sales data to avoid memory overload
    /// </summary>
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated margin data directly from repository (optimized query)
    /// </summary>
    Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        ProductGroupingMode groupingMode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed product analysis data for a specific product
    /// </summary>
    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets daily invoice import statistics for monitoring purposes
    /// </summary>
    Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets daily bank statement import statistics for monitoring purposes
    /// </summary>
    Task<List<DailyBankStatementStatistics>> GetBankStatementImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Update `AnalyticsRepository.cs`**

Rewrite the top of the file (replacing the mapping bodies with delegation) so it looks exactly like this (the invoice/bank-statement methods at the bottom are unchanged — only their `using` declarations and the top three Analytics methods change):

Replace **everything from line 1 through line 244** (i.e., everything up to and including `private string GetGroupKey(...)` method) of the current `AnalyticsRepository.cs` with:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Analytics.Infrastructure;

/// <summary>
/// Analytics repository — delegates product/sales lookups to the Catalog-side
/// adapter via <see cref="IAnalyticsProductSource"/> and owns the EF-backed
/// invoice/bank-statement statistics queries.
/// </summary>
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IAnalyticsProductSource _productSource;
    private readonly ApplicationDbContext _dbContext;

    public AnalyticsRepository(IAnalyticsProductSource productSource, ApplicationDbContext dbContext)
    {
        _productSource = productSource;
        _dbContext = dbContext;
    }

    public IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        return _productSource.StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken);
    }

    public async Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        ProductGroupingMode groupingMode,
        CancellationToken cancellationToken = default)
    {
        var groupTotals = new Dictionary<string, decimal>();

        await foreach (var product in StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken))
        {
            if (product.MarginAmount <= 0)
                continue;

            var groupKey = GetGroupKey(product, groupingMode);
            var totalSold = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = (decimal)totalSold * product.MarginAmount;

            if (!groupTotals.ContainsKey(groupKey))
                groupTotals[groupKey] = 0;

            groupTotals[groupKey] += marginContribution;
        }

        return groupTotals;
    }

    public Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return _productSource.GetProductAnalysisDataAsync(productId, fromDate, toDate, cancellationToken);
    }

    private string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => product.ProductCode,
            ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
            ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
            _ => product.ProductCode
        };
    }
```

Keep the existing closing brace of the class and the two remaining methods (`GetInvoiceImportStatisticsAsync`, `GetBankStatementImportStatisticsAsync`) **exactly as they are** — they don't reference Catalog and require no changes.

- [ ] **Step 4: Update `GetMarginReportHandler.cs`**

Remove `using Anela.Heblo.Domain.Features.Catalog;` at the top.

Locate line 57:

```csharp
            var productTypes = new[] { ProductType.Product, ProductType.Goods };
```

Replace with:

```csharp
            var productTypes = new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods };
```

- [ ] **Step 5: Update `GetProductMarginSummaryHandler.cs`**

Remove `using Anela.Heblo.Domain.Features.Catalog;` at the top.

Locate line 37:

```csharp
        var productTypes = new[] { ProductType.Product, ProductType.Goods };
```

Replace with:

```csharp
        var productTypes = new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods };
```

- [ ] **Step 6: Update `GetMarginReportHandlerTests.cs`**

Remove `using Anela.Heblo.Domain.Features.Catalog;` at the top.

Use the Edit tool with `replace_all: true` to replace every occurrence of `Type = ProductType.Product,` with `Type = AnalyticsProductType.Product,` (10 occurrences — lines 128, 141, 280, 293, 337, 350, 395, 408, 450, 463).

- [ ] **Step 7: Update `GetProductMarginSummaryHandlerTests.cs`**

Remove `using Anela.Heblo.Domain.Features.Catalog;` at the top.

Replace every `Type = ProductType.Product,` with `Type = AnalyticsProductType.Product,` (2 occurrences — lines 61, 75).

- [ ] **Step 8: Update `GetProductMarginAnalysisHandlerTests.cs`**

Remove `using Anela.Heblo.Domain.Features.Catalog;` at the top.

Replace every `Type = ProductType.Product,` with `Type = AnalyticsProductType.Product,` (4 occurrences — lines 55, 196, 236, 279).

- [ ] **Step 9: Build and verify**

Run:

```bash
dotnet build backend/Anela.Heblo.sln --no-restore
```

Expected: solution builds with zero errors and no new warnings.

If a `using Anela.Heblo.Domain.Features.Catalog;` remains needed in any Analytics file because it is genuinely consumed for an unrelated reason (e.g., an `ImportDateType` reference that happens to live under Catalog — verify with the actual file), keep it and note the case here. But based on inspection (`Grep "Anela.Heblo.Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/Analytics`), the only consumers are the four files listed above, all touched by this task.

- [ ] **Step 10: Run the full Analytics test suite**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Analytics" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: all Analytics tests PASS, including the four handler test files and the new adapter test file. No assertions should need to change.

- [ ] **Step 11: Run the architecture tests**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: all theory cases PASS, including both `Analytics (Application) -> Catalog` and `Analytics (Domain) -> Catalog`.

- [ ] **Step 12: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/IAnalyticsRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs
git commit -m "refactor(analytics): consume IAnalyticsProductSource instead of Catalog types"
```

---

## Task 7: Final validation and format pass

The change is functionally complete after Task 6. This task runs the project's mandated validation gates (per `CLAUDE.md` "Validation before completion") and applies `dotnet format` to pick up any stylistic drift.

**Files:**
- (no new files; may touch any of the above for `dotnet format` whitespace fixes only)

- [ ] **Step 1: `dotnet build` on the full solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: solution builds with zero errors and no new warnings.

- [ ] **Step 2: `dotnet format` on the solution**

Run:

```bash
dotnet format backend/Anela.Heblo.sln
```

Apply any whitespace/style fixes the formatter produces. If the formatter modifies files **outside** the eight files listed in Task 6, stop and inspect — surgical-change rule: don't bundle unrelated formatting.

- [ ] **Step 3: Run the full backend test suite**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --no-build --logger "console;verbosity=normal"
```

Expected: all tests PASS. Pay attention to:
- All eight `CatalogAnalyticsSourceAdapterTests` cases.
- All Analytics handler tests (`GetMarginReportHandlerTests`, `GetProductMarginSummaryHandlerTests`, `GetProductMarginAnalysisHandlerTests`, `GetInvoiceImportStatisticsHandlerTests`, `DashboardTiles/InvoiceImportStatisticsTileTests`).
- All `ModuleBoundariesTests` theory rows and facts.
- `PurchaseMaterialCatalogAdapterTests` (Task 5 added a binding in the same module — make sure nothing regressed).

- [ ] **Step 4: Spec NFR-2 verification — zero Catalog references in Analytics namespaces**

Run:

```bash
grep -r "Anela.Heblo.Domain.Features.Catalog" \
  backend/src/Anela.Heblo.Application/Features/Analytics/ \
  backend/src/Anela.Heblo.Domain/Features/Analytics/
```

Expected: no output (zero matches).

Also confirm no Analytics file references the specific Catalog types listed in spec NFR-2:

```bash
grep -rE "CatalogAggregate|\bMarginData\b|PurchaseHistory|\bProductType\b|ICatalogRepository" \
  backend/src/Anela.Heblo.Application/Features/Analytics/ \
  backend/src/Anela.Heblo.Domain/Features/Analytics/
```

Expected: no output. (`AnalyticsProductType` references are fine — the search uses `\bProductType\b` to avoid matching `AnalyticsProductType`.)

- [ ] **Step 5: If `dotnet format` produced changes, commit them**

```bash
git status
# If any modified files appear:
git add <changed files>
git commit -m "style: apply dotnet format after analytics decoupling"
```

If `git status` is clean, skip this step.

---

## Scope Boundary

The following are explicitly out of scope (per spec "Out of Scope" and arch-review §"Specification Amendments"):

- Refactoring `CatalogAggregate`, `MarginData`, `PurchaseHistory`, or any Catalog-owned type.
- Fixing the asymmetric `SalesHistory` filtering between streaming and single-product paths (preserved verbatim per FR-7).
- Removing the stale comment in `AnalyticsModule.cs:28` about `IMarginCalculationService` (flag in a follow-up).
- Auditing other modules for similar violations.
- True streaming (the underlying `ICatalogRepository.GetProductsWithSalesInPeriod` returns a materialised `List<CatalogAggregate>`; the `IAsyncEnumerable` façade is preserved as-is).
- Frontend, OpenAPI client, or HTTP-surface changes.

---

## Self-Review

**Spec coverage:**
- FR-1 `IAnalyticsProductSource` interface — Task 3.
- FR-2 `AnalyticsProductType` enum (`Product`, `Goods` only) and `AnalyticsProduct.Type` retyping — Tasks 2 and 6 Step 1.
- FR-3 `CatalogAnalyticsSourceAdapter` with consolidated mapping helper and adapter-boundary type translation — Task 4.
- FR-4 DI registration in `CatalogModule` (Transient, per arch-review Decision 3 / spec amendment item 4) — Task 5.
- FR-5 `AnalyticsRepository` drops `ICatalogRepository`, and (per arch-review amendment item 1) `IAnalyticsRepository` interface signature also drops `ProductType[]` — Task 6 Steps 2–3.
- FR-6 `GetMarginReportHandler` and `GetProductMarginSummaryHandler` lose Catalog imports — Task 6 Steps 4–5. `GetProductMarginAnalysisHandler` already does not import Catalog; its test file is updated in Task 6 Step 8 per arch-review amendment item 2.
- FR-7 Behavior preservation — mapping moves verbatim, sales-filter asymmetry preserved, `GC.Collect()` batching loop preserved; verified by Task 6 Step 10 (all existing assertions unchanged) and Task 7 Steps 3–4.
- FR-8 (arch-review amendment 3) `ModuleBoundariesTests` rule(s) covering both Application and Domain assemblies — Task 1.
- NFR-1 (with arch-review amendment 5 wording) — same underlying `ICatalogRepository` calls, no new SQL, no allocation change in mapping — by-construction from Task 4 implementation.
- NFR-2 grep returns zero matches — verified by Task 7 Step 4.
- NFR-3 test isolation + new adapter mapping tests — Task 4 Step 1.
- NFR-4 backwards compatibility — no HTTP/DTO/schema changes; verified by Task 7 Step 3.

**Placeholder scan:** no TODO/TBD/"implement later"; every step has actual file contents, commands, and expected output. The only "if X then …" branches are honest failure paths (Task 4 Step 2 if Catalog field names differ from the test fixture; Task 6 Step 9 if an unexpected `using Catalog` lingers; Task 7 Step 5 conditional commit).

**Type consistency:** `IAnalyticsProductSource` method signatures in Task 3 match the implementation in Task 4 and the call sites in Task 6 Step 3. `AnalyticsProductType` is referenced consistently as the Domain-namespace enum. `MapProductType` and `MapProductTypes` (plural) are both defined in Task 4 Step 3.
