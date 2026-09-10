# Bundle Sales Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A product sold inside a gift package contributes its BoM quantity to that product's sales history, so manufacturing and purchasing plan against real demand.

**Architecture:** A new cached catalog data source holds set composition (`setCode → components`). At merge time a pure expander adds synthetic sale records for each component of each sold bundle — quantities only, revenue zero. Every consumer of `CatalogAggregate.SalesHistory` is corrected by that single change; no consumer is modified.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq, AutoMapper, FlexiBee SDK (`Rem.FlexiBeeSDK.Client` 0.1.139).

**Spec:** `docs/superpowers/specs/2026-08-26-bundle-sales-expansion-design.md`

## Global Constraints

- **Bundle definition:** ERP type `Product` (8) whose code starts with `BAL` or `SET`. This rule must exist in exactly one place — see Task 2. Copying the prefixes is a defect.
- **Revenue is never altered.** Synthetic records carry `SumTotal = SumB2B = SumB2C = 0`. The bundle keeps its own full-revenue record.
- **Quantities go into `AmountB2B`/`AmountB2C`, not only `AmountTotal`.** `CatalogAggregate.GetTotalSold` sums `AmountB2B + AmountB2C`; quantities placed only in `AmountTotal` do not count.
- **One level of expansion only.** A component that is itself a bundle is not recursed.
- **DTOs are classes, never records** — applies to OpenAPI contract types only. `CatalogSaleRecord` and `CatalogSetPart` are internal domain types and stay `record`.
- **Refresh failures retain stale cache** and log a warning; they never throw.

## Running tests

This repo has a known contention issue: `dotnet test` can hang at 0% CPU when another worktree builds concurrently. Always build first, then run with `--no-build`:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~<TestClassName>"
```

Substitute `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj` for adapter tests.

---

### Task 1: Confirm bundle count and settle batching

The spec leaves one open item: how many bundles exist. This determines whether `RefreshSetPartsData` needs batching, because it issues one Flexi call per bundle per refresh.

**Files:**
- Modify: `docs/superpowers/specs/2026-08-26-bundle-sales-expansion-design.md` (the "Known Risk: Flexi Call Volume" section)

- [ ] **Step 1: Count the bundles**

The Gift Package Manufacture screen already lists every `ProductType.Set` product — `LogisticsCatalogSourceAdapter.GetGiftPackageSetsAsync` filters exactly `item.Type == ProductType.Set`. Open that screen against staging and count the rows.

If the UI is unavailable, call the endpoint directly and count the array length in the response.

- [ ] **Step 2: Record the number in the spec**

Replace the paragraph beginning "**The bundle count is not yet known.**" with the actual figure, for example:

```markdown
**Bundle count: 23** (measured 2026-08-26 against staging). One Flexi call per bundle per
refresh is acceptable at this volume; no batching needed.
```

- [ ] **Step 3: Decide batching**

- **50 or fewer bundles:** no batching. `ICatalogSetPartsClient.GetAsync` loops set codes sequentially. Proceed to Task 2 unchanged.
- **More than 50:** stop and raise it. The interface already takes `IEnumerable<string>` so batching is an implementation detail of the Flexi client, but the retry/partial-failure semantics need deciding first, and that is a design change rather than a plan step.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-08-26-bundle-sales-expansion-design.md
git commit -m "docs: record measured bundle count in sales expansion spec"
```

---

### Task 2: Extract the shared bundle rule

`CatalogMergeService.GetProductType` currently owns the `BAL`/`SET` definition privately. Task 6 needs the same rule. Extracting it first means there is never a second copy.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/BundleProductRule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` (private `GetProductType`, near line 285)
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/BundleProductRuleTests.cs`

**Interfaces:**
- Produces: `static class BundleProductRule` with `bool IsBundleCode(string? productCode)` and `ProductType Resolve(ProductType erpType, string? productCode)`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/BundleProductRuleTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public sealed class BundleProductRuleTests
{
    [Theory]
    [InlineData("BAL001", true)]
    [InlineData("SET042", true)]
    [InlineData("KRM001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBundleCode_RecognizesBalAndSetPrefixes(string? code, bool expected)
    {
        // Act
        var result = BundleProductRule.IsBundleCode(code);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_PromotesProductWithBundleCodeToSet()
    {
        // Arrange
        var erpType = ProductType.Product;

        // Act
        var result = BundleProductRule.Resolve(erpType, "BAL001");

        // Assert
        result.Should().Be(ProductType.Set);
    }

    [Fact]
    public void Resolve_LeavesNonProductTypesUntouchedEvenWithBundleCode()
    {
        // Arrange
        var erpType = ProductType.Material;

        // Act
        var result = BundleProductRule.Resolve(erpType, "BAL001");

        // Assert
        result.Should().Be(ProductType.Material);
    }

    [Fact]
    public void Resolve_LeavesOrdinaryProductAsProduct()
    {
        // Act
        var result = BundleProductRule.Resolve(ProductType.Product, "KRM001");

        // Assert
        result.Should().Be(ProductType.Product);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
```

Expected: FAIL — build error, `BundleProductRule` does not exist.

- [ ] **Step 3: Write the implementation**

Create `backend/src/Anela.Heblo.Domain/Features/Catalog/BundleProductRule.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Catalog;

/// <summary>
/// Single source of truth for what counts as a bundle ("balíček") in this system.
/// ERP has no bundle product type — a bundle is a Product whose code carries a known prefix.
/// Both the catalog merge and the set-parts refresh depend on this rule agreeing with itself.
/// </summary>
public static class BundleProductRule
{
    private const string GiftPackagePrefix = "BAL";
    private const string SetPrefix = "SET";

    public static bool IsBundleCode(string? productCode) =>
        !string.IsNullOrEmpty(productCode)
        && (productCode.StartsWith(GiftPackagePrefix, StringComparison.Ordinal)
            || productCode.StartsWith(SetPrefix, StringComparison.Ordinal));

    public static ProductType Resolve(ProductType erpType, string? productCode) =>
        erpType == ProductType.Product && IsBundleCode(productCode)
            ? ProductType.Set
            : erpType;
}
```

- [ ] **Step 4: Point `CatalogMergeService` at the shared rule**

In `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs`, replace the body of the private `GetProductType`:

```csharp
    private static ProductType GetProductType(ErpStock s) =>
        BundleProductRule.Resolve((ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED, s.ProductCode);
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~BundleProductRuleTests|FullyQualifiedName~CatalogMergeServiceTests"
```

Expected: PASS. `CatalogMergeServiceTests` must stay green — the refactor is behaviour-preserving.
In particular `ExecutePriorityMergeAsync_PrefixedErpProductCode_BecomesProductTypeSet` already
covers this rule end-to-end; if it fails, the extraction changed behaviour and must be corrected
rather than the test adjusted.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/BundleProductRule.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/BundleProductRuleTests.cs
git commit -m "refactor: extract bundle product rule to single source of truth"
```

---

### Task 3: Set-parts client (domain contract + Flexi implementation)

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSetPart.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/ICatalogSetPartsClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/FlexiCatalogSetPartsClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` (near line 62, alongside `ICatalogSalesClient`)
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Sales/FlexiCatalogSetPartsClientTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks
- Produces:
  - `record CatalogSetPart { string SetCode; string ComponentCode; string ComponentName; double Amount; }`
  - `ICatalogSetPartsClient.GetAsync(IEnumerable<string> setCodes, CancellationToken) → Task<IReadOnlyList<CatalogSetPart>>`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Sales/FlexiCatalogSetPartsClientTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.Sets;
using Rem.FlexiBeeSDK.Model.Products.Sets;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Sales;

public sealed class FlexiCatalogSetPartsClientTests
{
    private readonly Mock<IProductSetsClient> _productSetsClient = new();
    private readonly Mock<ILogger<FlexiCatalogSetPartsClient>> _logger = new();

    [Fact]
    public async Task GetAsync_FlattensPartsAcrossSetsAndStampsSetCode()
    {
        // Arrange
        _productSetsClient
            .Setup(c => c.GetAsync("BAL001", 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductSetFlexiDto>
            {
                BuildDto(quantity: 2, code: "KRM001", name: "Krém"),
            });
        _productSetsClient
            .Setup(c => c.GetAsync("BAL002", 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductSetFlexiDto>
            {
                BuildDto(quantity: 1, code: "MYD001", name: "Mýdlo"),
            });

        var sut = new FlexiCatalogSetPartsClient(_productSetsClient.Object, _logger.Object);

        // Act
        var result = await sut.GetAsync(new[] { "BAL001", "BAL002" }, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new[]
        {
            new CatalogSetPart { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
            new CatalogSetPart { SetCode = "BAL002", ComponentCode = "MYD001", ComponentName = "Mýdlo", Amount = 1 },
        });
    }

    [Fact]
    public async Task GetAsync_LogsWarningAndSkipsSetWithNoParts()
    {
        // Arrange
        _productSetsClient
            .Setup(c => c.GetAsync("BAL003", 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductSetFlexiDto>());

        var sut = new FlexiCatalogSetPartsClient(_productSetsClient.Object, _logger.Object);

        // Act
        var result = await sut.GetAsync(new[] { "BAL003" }, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("BAL003")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static ProductSetFlexiDto BuildDto(double quantity, string code, string name) =>
        new()
        {
            Quantity = quantity,
            ProductList = new List<ProductSetsProductFlexiDto>
            {
                new() { Code = code, Name = name },
            },
        };
}
```

Note: `ProductSetsProductFlexiDto` property names must match the SDK. Verify with
`gh api "repos/onpaj/FlexiBeeSDK/contents/src/Rem.FlexiBeeSDK.Model/Products/Sets/ProductSetsProductFlexiDto.cs" --jq '.content' | base64 -d`
and adjust the two property names in `BuildDto` if they differ.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj -p:UseSharedCompilation=false
```

Expected: FAIL — `FlexiCatalogSetPartsClient` and `CatalogSetPart` do not exist.

- [ ] **Step 3: Write the domain contract**

Create `backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSetPart.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Sales;

/// <summary>
/// One component of a bundle, as defined in the ERP "sady-a-komplety" evidence.
/// </summary>
public record CatalogSetPart
{
    public required string SetCode { get; init; }
    public required string ComponentCode { get; init; }
    public required string ComponentName { get; init; }
    public double Amount { get; init; }
}
```

Create `backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/ICatalogSetPartsClient.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Sales;

public interface ICatalogSetPartsClient
{
    Task<IReadOnlyList<CatalogSetPart>> GetAsync(
        IEnumerable<string> setCodes,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write the Flexi implementation**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/FlexiCatalogSetPartsClient.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.Sets;

namespace Anela.Heblo.Adapters.Flexi.Sales;

/// <summary>
/// Reads bundle composition from the FlexiBee "sady-a-komplety" evidence — the same source the
/// gift package screen uses. Note this is NOT the kusovnik (BoM); bundle composition is not there.
/// </summary>
public class FlexiCatalogSetPartsClient : ICatalogSetPartsClient
{
    private readonly IProductSetsClient _productSetsClient;
    private readonly ILogger<FlexiCatalogSetPartsClient> _logger;

    public FlexiCatalogSetPartsClient(
        IProductSetsClient productSetsClient,
        ILogger<FlexiCatalogSetPartsClient> logger)
    {
        _productSetsClient = productSetsClient ?? throw new ArgumentNullException(nameof(productSetsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<CatalogSetPart>> GetAsync(
        IEnumerable<string> setCodes,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<CatalogSetPart>();

        foreach (var setCode in setCodes.Distinct(StringComparer.Ordinal))
        {
            var setParts = await _productSetsClient.GetAsync(setCode, cancellationToken: cancellationToken);

            if (setParts.Count == 0)
            {
                _logger.LogWarning(
                    "Bundle {SetCode} has no components in Flexi — its sales will not be expanded onto any product.",
                    setCode);
                continue;
            }

            parts.AddRange(setParts
                .Where(p => p.Product != null)
                .Select(p => new CatalogSetPart
                {
                    SetCode = setCode,
                    ComponentCode = p.Product.Code,
                    ComponentName = p.Product.Name,
                    Amount = p.Quantity,
                }));
        }

        return parts;
    }
}
```

- [ ] **Step 5: Register in DI**

In `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`, directly below the `ICatalogSalesClient` line (near line 62):

```csharp
        services.AddSingleton<ICatalogSetPartsClient, FlexiCatalogSetPartsClient>();
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --no-build --filter "FullyQualifiedName~FlexiCatalogSetPartsClientTests"
```

Expected: PASS, both tests.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSetPart.cs \
        backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/ICatalogSetPartsClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/FlexiCatalogSetPartsClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Sales/FlexiCatalogSetPartsClientTests.cs
git commit -m "feat: add catalog set parts client reading bundle composition from Flexi"
```

---

### Task 4: The expander

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSaleRecord.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/BundleSalesExpander.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/BundleSalesExpanderTests.cs`

**Interfaces:**
- Consumes: `CatalogSetPart` (Task 3)
- Produces: `BundleSalesExpander.Expand(IEnumerable<CatalogSaleRecord> sales, IEnumerable<CatalogSetPart> setParts) → IReadOnlyList<CatalogSaleRecord>`; new property `CatalogSaleRecord.SourceBundleCode` (nullable `string`)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/BundleSalesExpanderTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class BundleSalesExpanderTests
{
    private static readonly DateTime SaleDate = new(2026, 8, 20);
    private readonly BundleSalesExpander _sut = new();

    [Fact]
    public void Expand_MultipliesComponentQuantityByBomAmountForBothChannels()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 2) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        var component = result.Single(r => r.ProductCode == "KRM001");
        component.AmountB2B.Should().Be(6);
        component.AmountB2C.Should().Be(10);
        component.AmountTotal.Should().Be(16);
    }

    [Fact]
    public void Expand_LeavesRevenueAtZeroOnSyntheticRecords()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 2) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        var component = result.Single(r => r.ProductCode == "KRM001");
        component.SumB2B.Should().Be(0);
        component.SumB2C.Should().Be(0);
        component.SumTotal.Should().Be(0);
    }

    [Fact]
    public void Expand_StampsSourceBundleCodeForTraceability()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 1, amountB2C: 0) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 1) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        result.Single(r => r.ProductCode == "KRM001").SourceBundleCode.Should().Be("BAL001");
    }

    [Fact]
    public void Expand_KeepsOriginalBundleRecordUntouched()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 2) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        var bundle = result.Single(r => r.ProductCode == "BAL001");
        bundle.AmountB2B.Should().Be(3);
        bundle.SumB2C.Should().Be(500);
        bundle.SourceBundleCode.Should().BeNull();
    }

    [Fact]
    public void Expand_PassesNonBundleRecordsThroughUnchanged()
    {
        // Arrange
        var sales = new[]
        {
            new CatalogSaleRecord
            {
                Date = SaleDate,
                ProductCode = "KRM001",
                ProductName = "Krém",
                AmountB2B = 4,
                AmountB2C = 1,
                SumB2C = 250,
            },
        };

        // Act
        var result = _sut.Expand(sales, Array.Empty<CatalogSetPart>());

        // Assert
        result.Should().HaveCount(1);
        result[0].AmountB2B.Should().Be(4);
        result[0].SumB2C.Should().Be(250);
    }

    [Fact]
    public void Expand_DoesNotRecurseWhenComponentIsItselfABundle()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 1, amountB2C: 0) };
        var parts = new[]
        {
            Part("BAL001", "BAL002", amount: 1),
            Part("BAL002", "KRM001", amount: 10),
        };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainSingle(r => r.ProductCode == "BAL002");
        result.Should().NotContain(r => r.ProductCode == "KRM001");
    }

    [Fact]
    public void Expand_ReturnsInputUnchangedWhenPartsAreEmpty()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };

        // Act
        var result = _sut.Expand(sales, Array.Empty<CatalogSetPart>());

        // Assert
        result.Should().HaveCount(1);
        result[0].ProductCode.Should().Be("BAL001");
    }

    [Fact]
    public void Expand_EmitsOneRecordPerComponentOccurrence()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 1, amountB2C: 0) };
        var parts = new[]
        {
            Part("BAL001", "KRM001", amount: 1),
            Part("BAL001", "MYD001", amount: 3),
        };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        result.Should().HaveCount(3);
        result.Single(r => r.ProductCode == "MYD001").AmountB2B.Should().Be(3);
    }

    private static CatalogSaleRecord BundleSale(double amountB2B, double amountB2C) => new()
    {
        Date = SaleDate,
        ProductCode = "BAL001",
        ProductName = "Dárkový balíček",
        AmountB2B = amountB2B,
        AmountB2C = amountB2C,
        AmountTotal = amountB2B + amountB2C,
        SumB2C = 500,
        SumTotal = 500,
    };

    private static CatalogSetPart Part(string setCode, string componentCode, double amount) => new()
    {
        SetCode = setCode,
        ComponentCode = componentCode,
        ComponentName = componentCode + " name",
        Amount = amount,
    };
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
```

Expected: FAIL — `BundleSalesExpander` and `SourceBundleCode` do not exist.

- [ ] **Step 3: Add the traceability property**

In `backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSaleRecord.cs`, add below `SumB2C`:

```csharp
    /// <summary>
    /// Set when this record was derived from a bundle sale rather than an invoice line.
    /// Such records carry quantity only — all Sum* values are zero, so the bundle's own record
    /// keeps the full revenue. Null on records that came straight from the ERP.
    /// </summary>
    public string? SourceBundleCode { get; set; }
```

- [ ] **Step 4: Write the expander**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/BundleSalesExpander.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Sales;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Adds component sales to the sales stream for every bundle sold.
///
/// A bundle sells as a single ERP invoice line carrying the bundle's own product code, so its
/// contents are invisible to manufacturing planning. This expander emits one synthetic record per
/// component per bundle sale, carrying quantity only — revenue stays entirely on the bundle's own
/// record so company totals are unaffected.
///
/// Pure: no I/O, no state, safe to call from the merge path.
/// </summary>
public sealed class BundleSalesExpander
{
    public IReadOnlyList<CatalogSaleRecord> Expand(
        IEnumerable<CatalogSaleRecord> sales,
        IEnumerable<CatalogSetPart> setParts)
    {
        var salesList = sales as IReadOnlyList<CatalogSaleRecord> ?? sales.ToList();

        var partsBySet = setParts
            .GroupBy(p => p.SetCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        if (partsBySet.Count == 0)
            return salesList;

        var expanded = new List<CatalogSaleRecord>(salesList);

        foreach (var sale in salesList)
        {
            // One level only: a record already derived from a bundle is never expanded again.
            if (sale.SourceBundleCode != null)
                continue;

            if (!partsBySet.TryGetValue(sale.ProductCode, out var parts))
                continue;

            foreach (var part in parts)
            {
                var amountB2B = sale.AmountB2B * part.Amount;
                var amountB2C = sale.AmountB2C * part.Amount;

                expanded.Add(new CatalogSaleRecord
                {
                    Date = sale.Date,
                    ProductCode = part.ComponentCode,
                    ProductName = part.ComponentName,
                    AmountB2B = amountB2B,
                    AmountB2C = amountB2C,
                    AmountTotal = amountB2B + amountB2C,
                    SumB2B = 0,
                    SumB2C = 0,
                    SumTotal = 0,
                    SourceBundleCode = sale.ProductCode,
                });
            }
        }

        return expanded;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~BundleSalesExpanderTests"
```

Expected: PASS, all eight tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSaleRecord.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/BundleSalesExpander.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/BundleSalesExpanderTests.cs
git commit -m "feat: add bundle sales expander mapping set sales onto component quantities"
```

---

### Task 5: Cache the set parts

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs`

**Interfaces:**
- Consumes: `CatalogSetPart` (Task 3)
- Produces: `CatalogCacheStore.GetSetPartsData() → IList<CatalogSetPart>`, `CatalogCacheStore.SetSetPartsData(IList<CatalogSetPart>)`

- [ ] **Step 1: Write the failing test**

Append to `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs`, inside the existing test class. That class builds the store inline in each test from constructor-initialised fields — follow the same shape:

```csharp
    [Fact]
    public void SetSetPartsData_RoundTripsThroughCache()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        var parts = new List<CatalogSetPart>
        {
            new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
        };

        // Act
        store.SetSetPartsData(parts);
        var result = store.GetSetPartsData();

        // Assert
        result.Should().BeEquivalentTo(parts);
    }

    [Fact]
    public void GetSetPartsData_ReturnsEmptyWhenNeverSet()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        // Act
        var result = store.GetSetPartsData();

        // Assert
        result.Should().BeEmpty();
    }
```

Add `using Anela.Heblo.Domain.Features.Catalog.Sales;` if not already present.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
```

Expected: FAIL — `SetSetPartsData` does not exist.

- [ ] **Step 3: Add the cache slot**

In `CatalogCacheStore.cs`, add the key alongside the other per-source keys (near line 25):

```csharp
    private const string CachedSetPartsDataKey = "CachedSetPartsData";
```

And add the accessors directly below the existing `GetSalesData`/`SetSalesData` pair (near line 164), matching their shape exactly:

```csharp
    public IList<CatalogSetPart> GetSetPartsData() =>
        _cache.Get<List<CatalogSetPart>>(CachedSetPartsDataKey) ?? new List<CatalogSetPart>();

    public void SetSetPartsData(IList<CatalogSetPart> value)
    {
        _cache.Set(CachedSetPartsDataKey, value);
        InvalidateSourceData(CachedSetPartsDataKey);
        SetLoadDateInCache(CachedSetPartsDataKey);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~CatalogCacheStoreTests"
```

Expected: PASS, including the pre-existing tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs
git commit -m "feat: cache bundle set parts in catalog cache store"
```

---

### Task 6: Refresh task

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs`

**Interfaces:**
- Consumes: `ICatalogSetPartsClient` (Task 3), `BundleProductRule` (Task 2), `CatalogCacheStore.SetSetPartsData` (Task 5)
- Produces: `CatalogDataRefreshService.RefreshSetPartsData(CancellationToken)`, `ICatalogRepository.RefreshSetPartsData(CancellationToken)`

- [ ] **Step 1: Write the failing test**

The class builds the service through a private `CreateService(...)` factory (near line 262) whose
parameters are all optional mocks, and uses fields `_cacheStore` and `_serviceLoggerMock`. First
add the new client to that factory — a `ICatalogSetPartsClient? setPartsClient = null` parameter,
passed as `setPartsClient ?? new Mock<ICatalogSetPartsClient>().Object` in the position matching
the constructor (immediately after `salesClient`).

Then append these two tests:

```csharp
    [Fact]
    public async Task RefreshSetPartsData_FetchesPartsOnlyForBundleCodedProducts()
    {
        // Arrange
        _cacheStore.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Balíček", ProductTypeId = (int)ProductType.Product },
            new() { ProductCode = "KRM001", ProductName = "Krém",    ProductTypeId = (int)ProductType.Product },
        });

        var setPartsClient = new Mock<ICatalogSetPartsClient>();
        setPartsClient
            .Setup(c => c.GetAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogSetPart>
            {
                new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
            });

        var resilienceServiceMock = new Mock<ICatalogResilienceService>();
        resilienceServiceMock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IList<CatalogSetPart>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IList<CatalogSetPart>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));

        var service = CreateService(
            setPartsClient: setPartsClient.Object,
            resilienceService: resilienceServiceMock.Object);

        // Act
        await service.RefreshSetPartsData(CancellationToken.None);

        // Assert
        setPartsClient.Verify(
            c => c.GetAsync(It.Is<IEnumerable<string>>(codes => codes.SequenceEqual(new[] { "BAL001" })),
                            It.IsAny<CancellationToken>()),
            Times.Once);
        _cacheStore.GetSetPartsData().Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshSetPartsData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning()
    {
        // Arrange
        _cacheStore.SetSetPartsData(new List<CatalogSetPart>
        {
            new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
        });

        var resilienceServiceMock = new Mock<ICatalogResilienceService>();
        resilienceServiceMock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IList<CatalogSetPart>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test failure"));

        var service = CreateService(resilienceService: resilienceServiceMock.Object);

        // Act
        var ex = await Record.ExceptionAsync(() => service.RefreshSetPartsData(CancellationToken.None));

        // Assert
        ex.Should().BeNull("RefreshSetPartsData should not throw even when resilience fails");
        _cacheStore.GetSetPartsData().Should().HaveCount(1, "stale cache must be retained");
        _serviceLoggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("retaining stale cache")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
```

Expected: FAIL — `RefreshSetPartsData` does not exist.

- [ ] **Step 3: Add the refresh method**

In `CatalogDataRefreshService.cs`, add the field, constructor parameter and assignment for
`ICatalogSetPartsClient _setPartsClient` following the exact style of the surrounding members
(`?? throw new ArgumentNullException(...)`), then add directly below `RefreshSalesData` (near line 137):

```csharp
    public async Task RefreshSetPartsData(CancellationToken ct)
    {
        try
        {
            var bundleCodes = _cacheStore.GetErpStockData()
                .Where(s => BundleProductRule.Resolve((ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED, s.ProductCode) == ProductType.Set)
                .Select(s => s.ProductCode)
                .ToList();

            _cacheStore.SetSetPartsData(await _resilienceService.ExecuteWithResilienceAsync(
                async (cancellationToken) => (IList<CatalogSetPart>)(await _setPartsClient.GetAsync(
                    bundleCodes,
                    cancellationToken)).ToList(),
                "RefreshSetPartsData", ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSetPartsData failed after all retries — retaining stale cache. Items in cache: {Count}", _cacheStore.GetSetPartsData().Count);
        }
    }
```

- [ ] **Step 4: Expose it on the repository and register the refresh task**

In `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs`, below `RefreshSalesData`:

```csharp
    Task RefreshSetPartsData(CancellationToken ct);
```

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`, below line 113:

```csharp
    public Task RefreshSetPartsData(CancellationToken ct) => _refreshService.RefreshSetPartsData(ct);
```

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, below the
`RefreshSalesData` registration (near line 191):

```csharp
        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshSetPartsData),
            (r, ct) => r.RefreshSetPartsData(ct)
        );
```

In `backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs`, below its existing
`RefreshSalesData` stub (near line 181):

```csharp
    public Task RefreshSetPartsData(CancellationToken ct) => Task.CompletedTask;
```

- [ ] **Step 5: Build to find any other `ICatalogRepository` implementers**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
```

If the build reports other classes not implementing `RefreshSetPartsData`, add the same
`Task.CompletedTask` stub to each, then rebuild until clean.

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~CatalogDataRefreshServiceTests"
```

Expected: PASS, including the pre-existing tests.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs \
        backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs \
        backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs
git commit -m "feat: refresh bundle set parts as a catalog data source"
```

---

### Task 7: Wire the expansion into the merge

This is the task that changes behaviour. Everything before it was inert.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` (constructor near line 24; `salesMap` near line 113)
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` (near line 91)
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeServiceTests.cs`

**Interfaces:**
- Consumes: `BundleSalesExpander` (Task 4), `CatalogCacheStore.GetSetPartsData` (Task 5)
- Produces: no new public surface — `CatalogAggregate.SalesHistory` now includes bundle-derived quantities

- [ ] **Step 1: Write the failing test**

This class uses a private `Create()` factory returning `(store, service)` and drives merges through
`ExecutePriorityMergeAsync()` (`Merge()` itself is `internal`). Append:

```csharp
    private static void SeedBundleSale(CatalogCacheStore store)
    {
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Balíček", ProductId = 1, ProductTypeId = (int)ProductType.Product },
            new() { ProductCode = "KRM001", ProductName = "Krém",    ProductId = 2, ProductTypeId = (int)ProductType.Product },
        });
        store.SetSalesData(new List<CatalogSaleRecord>
        {
            new()
            {
                Date = new DateTime(2026, 8, 20),
                ProductCode = "BAL001",
                ProductName = "Balíček",
                AmountB2C = 5,
                AmountTotal = 5,
                SumB2C = 2500,
                SumTotal = 2500,
            },
        });
        store.SetSetPartsData(new List<CatalogSetPart>
        {
            new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
        });
    }

    [Fact]
    public async Task Merge_AddsBundleQuantitiesToComponentSalesHistory()
    {
        // Arrange
        var (store, service) = Create();
        SeedBundleSale(store);

        // Act
        var result = await service.ExecutePriorityMergeAsync();

        // Assert
        var component = result.Single(p => p.ProductCode == "KRM001");
        component.GetTotalSold(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31))
            .Should().Be(10);
    }

    [Fact]
    public async Task Merge_DoesNotAddRevenueToComponentFromBundleSale()
    {
        // Arrange
        var (store, service) = Create();
        SeedBundleSale(store);

        // Act
        var result = await service.ExecutePriorityMergeAsync();

        // Assert
        result.Single(p => p.ProductCode == "KRM001")
            .SaleHistorySummary.MonthlyData["2026-08"].TotalB2C.Should().Be(0);
        result.Single(p => p.ProductCode == "BAL001")
            .SaleHistorySummary.MonthlyData["2026-08"].TotalB2C.Should().Be(2500);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~CatalogMergeServiceTests"
```

Expected: FAIL — the component's `GetTotalSold` returns 0 because expansion is not wired in.

- [ ] **Step 3: Inject the expander**

In `CatalogMergeService.cs`, add the field and constructor parameter following the existing style:

```csharp
    private readonly BundleSalesExpander _bundleExpander;
```

```csharp
    public CatalogMergeService(
        CatalogCacheStore cacheStore,
        BundleSalesExpander bundleExpander,
        TimeProvider timeProvider,
        ILogger<CatalogMergeService> logger)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _bundleExpander = bundleExpander ?? throw new ArgumentNullException(nameof(bundleExpander));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

- [ ] **Step 4: Expand when building the sales map**

Replace the `salesMap` assignment (near line 113):

```csharp
        var salesMap = _bundleExpander
            .Expand(_cacheStore.GetSalesData(), _cacheStore.GetSetPartsData())
            .GroupBy(s => s.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
```

- [ ] **Step 5: Register the expander in DI**

In `CatalogModule.cs`, directly above the `CatalogMergeService` registration (near line 91):

```csharp
        services.AddSingleton<BundleSalesExpander>();
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~CatalogMergeServiceTests"
```

Expected: PASS. The `Create()` factory in `CatalogMergeServiceTests` must pass the new argument:

```csharp
        var service = new CatalogMergeService(
            store,
            new BundleSalesExpander(),
            _timeProviderMock.Object,
            Mock.Of<ILogger<CatalogMergeService>>());
```

Search for any other `new CatalogMergeService(` call site and update it the same way.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeServiceTests.cs
git commit -m "feat: count bundle components in product sales history"
```

---

### Task 8: Full validation

**Files:** none modified unless a failure is found.

- [ ] **Step 1: Build and format the backend**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -p:UseSharedCompilation=false
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes
```

Expected: build succeeds; format reports no changes. If format reports changes, run it without
`--verify-no-changes` and commit the result.

- [ ] **Step 2: Run the full backend test suite**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "Category!=Integration"
dotnet build backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --no-build
```

Expected: all green. CI filters `Category!=Integration`, so this matches what CI will run.

- [ ] **Step 3: Manual verification against staging**

Pick one bundle with recent sales. Confirm all three:

1. Each component's total sold rose by exactly `bundleQty × partAmount` for the period.
2. Each component's monthly **revenue** is unchanged.
3. The bundle's own sales figures and revenue are unchanged.

If quantities did not move, check that `RefreshSetPartsData` actually ran and that
`GetSetPartsData()` is non-empty — an empty parts cache makes `Expand` a silent no-op by design.

- [ ] **Step 4: Commit any formatting fallout**

```bash
git add -A
git commit -m "chore: formatting after bundle sales expansion"
```

Skip if there is nothing to commit.

---

## Notes for the implementer

**The silent-failure mode to watch.** If the set-parts cache is empty, `Expand` returns its input
untouched and everything looks normal — same behaviour as before the change. That is deliberate
(it degrades safely rather than throwing) but it means "no visible change" after deployment is
ambiguous: it could mean the refresh never ran. Task 8 Step 3 exists specifically to disambiguate.

**Why `SumB2B`/`SumB2C` must stay zero.** `CatalogAggregate.UpdateSaleHistorySummary` sums these
into `SaleHistorySummary.MonthlyData`, which feeds margin and financial reporting. Any non-zero
value here double-counts revenue against the bundle's own record.

**Do not "fix" the average-price oddity.** A component that sells through bundles will show more
units than revenue justifies, so any `Sum / Amount` average unit price is skewed for it. This is a
known and accepted consequence of the revenue decision in the spec, not a bug. `SourceBundleCode`
exists to explain it.
