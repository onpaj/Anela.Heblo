### task: add-top-product-sorter

Creates the new `ITopProductSorter`/`TopProductSorter` service as a verbatim move of the handler's current `ApplySorting` method, registers it in `AnalyticsModule.cs`, and adds a new test file covering all 13 named sort keys (both directions), the default (no `sortBy`) case, and the unrecognized-key fallback. This task does **not** touch the handler yet — `ApplySorting` continues to exist untouched in the handler until the next task.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs`

Context — `TopProductDto` (existing, in `Anela.Heblo.Application.Features.Analytics.Contracts`, unchanged by this refactor):

```csharp
public class TopProductDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }
    public string ColorCode { get; set; } = string.Empty;
    public int Rank { get; set; }
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
}
```

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class TopProductSorterTests
{
    private readonly TopProductSorter _sorter = new();

    // Values are monotonically increasing Low -> Mid -> High across every sortable
    // field, and GroupKey/DisplayName are alphabetically ordered the same way, so
    // ascending order is always [Low, Mid, High] and descending is always
    // [High, Mid, Low], regardless of which field is being sorted on.
    private static List<TopProductDto> MakeProducts() => new()
    {
        new TopProductDto
        {
            GroupKey = "B-Mid",
            DisplayName = "B Product",
            TotalMargin = 200m,
            M0Amount = 20m,
            M1Amount = 25m,
            M2Amount = 30m,
            M0Percentage = 10m,
            M1Percentage = 15m,
            M2Percentage = 20m,
            SellingPrice = 150m,
            PurchasePrice = 70m
        },
        new TopProductDto
        {
            GroupKey = "A-Low",
            DisplayName = "A Product",
            TotalMargin = 100m,
            M0Amount = 10m,
            M1Amount = 15m,
            M2Amount = 20m,
            M0Percentage = 5m,
            M1Percentage = 10m,
            M2Percentage = 15m,
            SellingPrice = 100m,
            PurchasePrice = 50m
        },
        new TopProductDto
        {
            GroupKey = "C-High",
            DisplayName = "C Product",
            TotalMargin = 300m,
            M0Amount = 30m,
            M1Amount = 35m,
            M2Amount = 40m,
            M0Percentage = 15m,
            M1Percentage = 20m,
            M2Percentage = 25m,
            SellingPrice = 200m,
            PurchasePrice = 90m
        }
    };

    [Theory]
    [InlineData("groupkey")]
    [InlineData("productcode")]
    [InlineData("displayname")]
    [InlineData("productname")]
    [InlineData("totalmargin")]
    [InlineData("m0amount")]
    [InlineData("m1amount")]
    [InlineData("m2amount")]
    [InlineData("m0percentage")]
    [InlineData("m1percentage")]
    [InlineData("m2percentage")]
    [InlineData("sellingprice")]
    [InlineData("purchaseprice")]
    public void Sort_NamedKey_Ascending_OrdersLowMidHigh(string sortBy)
    {
        var result = _sorter.Sort(MakeProducts(), sortBy, sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Theory]
    [InlineData("groupkey")]
    [InlineData("productcode")]
    [InlineData("displayname")]
    [InlineData("productname")]
    [InlineData("totalmargin")]
    [InlineData("m0amount")]
    [InlineData("m1amount")]
    [InlineData("m2amount")]
    [InlineData("m0percentage")]
    [InlineData("m1percentage")]
    [InlineData("m2percentage")]
    [InlineData("sellingprice")]
    [InlineData("purchaseprice")]
    public void Sort_NamedKey_Descending_OrdersHighMidLow(string sortBy)
    {
        var result = _sorter.Sort(MakeProducts(), sortBy, sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_NullSortBy_DefaultsToTotalMarginDescending()
    {
        var result = _sorter.Sort(MakeProducts(), null, sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_EmptySortBy_DefaultsToTotalMarginAscending()
    {
        var result = _sorter.Sort(MakeProducts(), "", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Fact]
    public void Sort_WhitespaceSortBy_DefaultsToTotalMargin()
    {
        var result = _sorter.Sort(MakeProducts(), "   ", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Fact]
    public void Sort_UnrecognizedSortBy_FallsBackToTotalMargin()
    {
        var result = _sorter.Sort(MakeProducts(), "not-a-real-key", sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_UppercaseSortByKey_IsCaseInsensitive()
    {
        var result = _sorter.Sort(MakeProducts(), "TOTALMARGIN", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TopProductSorterTests"`

Expected: **build error** — `CS0246: The type or namespace name 'TopProductSorter' could not be found` (neither the interface nor the class exists yet).

- [ ] **Step 3: Create `TopProductSorter.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface ITopProductSorter
{
    List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending);
}

/// <summary>
/// Extracted sorting logic for the top products list
/// </summary>
public class TopProductSorter : ITopProductSorter
{
    /// <summary>
    /// Applies sorting to the top products list
    /// </summary>
    public List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by TotalMargin descending
            return sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList();
        }

        return sortBy.ToLower() switch
        {
            "groupkey" or "productcode" => sortDescending
                ? products.OrderByDescending(p => p.GroupKey).ToList()
                : products.OrderBy(p => p.GroupKey).ToList(),
            "displayname" or "productname" => sortDescending
                ? products.OrderByDescending(p => p.DisplayName).ToList()
                : products.OrderBy(p => p.DisplayName).ToList(),
            "totalmargin" => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList(),
            // M0-M2 margin levels - amounts
            "m0amount" => sortDescending
                ? products.OrderByDescending(p => p.M0Amount).ToList()
                : products.OrderBy(p => p.M0Amount).ToList(),
            "m1amount" => sortDescending
                ? products.OrderByDescending(p => p.M1Amount).ToList()
                : products.OrderBy(p => p.M1Amount).ToList(),
            "m2amount" => sortDescending
                ? products.OrderByDescending(p => p.M2Amount).ToList()
                : products.OrderBy(p => p.M2Amount).ToList(),
            // M0-M2 margin levels - percentages
            "m0percentage" => sortDescending
                ? products.OrderByDescending(p => p.M0Percentage).ToList()
                : products.OrderBy(p => p.M0Percentage).ToList(),
            "m1percentage" => sortDescending
                ? products.OrderByDescending(p => p.M1Percentage).ToList()
                : products.OrderBy(p => p.M1Percentage).ToList(),
            "m2percentage" => sortDescending
                ? products.OrderByDescending(p => p.M2Percentage).ToList()
                : products.OrderBy(p => p.M2Percentage).ToList(),
            // Pricing
            "sellingprice" => sortDescending
                ? products.OrderByDescending(p => p.SellingPrice).ToList()
                : products.OrderBy(p => p.SellingPrice).ToList(),
            "purchaseprice" => sortDescending
                ? products.OrderByDescending(p => p.PurchasePrice).ToList()
                : products.OrderBy(p => p.PurchasePrice).ToList(),
            _ => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList()
        };
    }
}
```

- [ ] **Step 4: Register the service in `AnalyticsModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`, change:

```csharp
        services.AddScoped<TimeWindowParser>();
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
```

to:

```csharp
        services.AddScoped<TimeWindowParser>();
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
        services.AddScoped<ITopProductSorter, TopProductSorter>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TopProductSorterTests"`

Expected: PASS — all 30 test cases pass (13 keys × 2 directions = 26, plus 4 default/fallback/case-insensitivity tests).

Then run a full build to confirm the DI registration compiles:

Run: `dotnet build`

Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs
git commit -m "feat(analytics): add ITopProductSorter service"
```

---
