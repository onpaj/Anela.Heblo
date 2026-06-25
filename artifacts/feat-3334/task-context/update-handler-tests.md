### task: update-handler-tests


Update `GetProductMarginSummaryHandlerTests` to:
1. Construct `TimeWindowParser` with a `FakeTimeProvider` frozen at `2026-01-15`
2. Remove all `DateTime.Today` references — use the frozen date instead
3. Pass the `TimeWindowParser` instance to the handler constructor
4. Add a test for the new `ArgumentException` path

**File to modify:**
`backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

`FakeTimeProvider` is in `Microsoft.Extensions.TimeProvider.Testing` which is already a `PackageReference` in `Anela.Heblo.Tests.csproj`. No package installation needed.

**Full replacement for the file:**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

/// <summary>
/// Tests use a FakeTimeProvider frozen at 2026-01-15 so date arithmetic
/// is deterministic regardless of when the suite runs.
/// </summary>
public class GetProductMarginSummaryHandlerTests
{
    // Frozen instant: 2026-01-15 12:00:00 UTC
    private static readonly DateTimeOffset FrozenNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    // The "today" date the parser will derive from FrozenNow
    private static readonly DateTime FrozenDate = FrozenNow.Date; // 2026-01-15

    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _marginCalculator = new MarginCalculator();
        _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);

        var fakeTimeProvider = new FakeTimeProvider(FrozenNow);
        _timeWindowParser = new TimeWindowParser(fakeTimeProvider);

        _handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            _marginCalculator,
            _monthlyBreakdownGenerator,
            _timeWindowParser);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsCorrectResponse()
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest
        {
            TimeWindow = "current-year",
            GroupingMode = ProductGroupingMode.Products
        };

        // Frozen date is 2026-01-15, so current-year starts 2026-01-01
        var fromDate = new DateTime(FrozenDate.Year, 1, 1); // 2026-01-01
        var toDate = FrozenDate;                            // 2026-01-15

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Type = AnalyticsProductType.Product,
                MarginAmount = 100m,
                M0Amount = 100m,
                M1Amount = 100m,
                M2Amount = 100m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(FrozenDate.Year, 1, 10), AmountB2B = 10, AmountB2C = 5 }
                }
            },
            new AnalyticsProduct
            {
                ProductCode = "PROD002",
                ProductName = "Product 2",
                Type = AnalyticsProductType.Product,
                MarginAmount = 50m,
                M0Amount = 50m,
                M1Amount = 50m,
                M2Amount = 50m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(FrozenDate.Year, 1, 12), AmountB2B = 20, AmountB2C = 10 }
                }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TimeWindow.Should().Be("current-year");
        result.FromDate.Should().Be(fromDate);
        result.ToDate.Should().Be(toDate);
        result.TotalMargin.Should().Be(3000m); // (15 * 100) + (30 * 50) = 1500 + 1500
        result.TopProducts.Count.Should().Be(2);
        Assert.True(result.MonthlyData.Any());
    }

    [Theory]
    [InlineData("current-year")]
    [InlineData("last-6-months")]
    [InlineData("last-12-months")]
    public async Task Handle_DifferentTimeWindows_ParsesCorrectly(string timeWindow)
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest { TimeWindow = timeWindow, GroupingMode = ProductGroupingMode.Products };

        // All expected dates derive from FrozenDate (2026-01-15), never DateTime.Today
        var expectedDates = timeWindow switch
        {
            "current-year" => (new DateTime(FrozenDate.Year, 1, 1), FrozenDate),
            "last-6-months" => (FrozenDate.AddMonths(-6), FrozenDate),
            "last-12-months" => (FrozenDate.AddMonths(-12), FrozenDate),
            _ => throw new InvalidOperationException($"Unexpected timeWindow in test: {timeWindow}")
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TimeWindow.Should().Be(timeWindow);
        result.FromDate.Should().Be(expectedDates.Item1);
        result.ToDate.Should().Be(expectedDates.Item2);
    }

    [Fact]
    public async Task Handle_EmptyProductList_ReturnsZeroMargin()
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest
        {
            TimeWindow = "current-year",
            GroupingMode = ProductGroupingMode.Products
        };

        var fromDate = new DateTime(FrozenDate.Year, 1, 1);
        var toDate = FrozenDate;

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalMargin.Should().Be(0m);
        result.TopProducts.Should().BeEmpty();
        result.MonthlyData.Should().BeEmpty();
    }

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
            MarginLevel = MarginLevel.M2
        };

        var fromDate = new DateTime(FrozenDate.Year, 1, 1);
        var toDate = FrozenDate;

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
                            new() { Date = new DateTime(FrozenDate.Year, 1, 10), AmountB2B = 5, AmountB2C = 5 }
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
                Year = FrozenDate.Year,
                Month = 1,
                MonthDisplay = "Jan",
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
                MarginLevel.M2,
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
                MarginLevel.M2))
            .Returns(monthlyData);

        // Note: this handler instance gets its own TimeWindowParser with the shared FakeTimeProvider
        var handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            marginCalculatorMock.Object,
            monthlyBreakdownGeneratorMock.Object,
            _timeWindowParser);

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
                MarginLevel.M2,
                It.IsAny<CancellationToken>()),
            Times.Once);

        monthlyBreakdownGeneratorMock.Verify(
            x => x.Generate(
                calculationResult,
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                MarginLevel.M2),
            Times.Once);
    }

    [Fact]
    public void GetMarginAmountForLevel_WithUndefinedEnumValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var product = new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Product 1",
            Type = AnalyticsProductType.Product,
            MarginAmount = 50m,
            M0Amount = 10m,
            M1Amount = 20m,
            M2Amount = 30m,
            SalesHistory = new List<SalesDataPoint>()
        };
        var undefined = (MarginLevel)99;

        // Act
        Action act = () => _marginCalculator.GetMarginAmountForLevel(product, undefined);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("marginLevel");
    }

    [Fact]
    public void ParseTimeWindow_UnknownValue_ThrowsArgumentException()
    {
        // Arrange — _timeWindowParser is already wired to FakeTimeProvider

        // Act
        Action act = () => _timeWindowParser.ParseTimeWindow("not-a-real-window");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown time window value: 'not-a-real-window'*")
            .WithParameterName("timeWindow");
    }
}

// Extension method to convert list to IAsyncEnumerable for testing
public static class TestExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask; // Satisfy async requirement
    }
}
```

Key changes from the original:
- Added `using Microsoft.Extensions.Time.Testing;`
- Added `FrozenNow` and `FrozenDate` static readonly fields — single source of truth for the frozen instant
- Constructor creates `FakeTimeProvider(FrozenNow)`, builds `TimeWindowParser`, passes it to handler
- All `DateTime.Today` references replaced with `FrozenDate`
- `Handle_DifferentTimeWindows_ParsesCorrectly`: the `_ =>` fallback now throws `InvalidOperationException` instead of silently mapping an unrecognised value (matching the new handler behaviour)
- `Handle_ValidRequest_ReturnsCorrectResponse`: sales history dates updated to fall within `2026-01-01..2026-01-15` (the frozen current-year window) so the mock repository setup matches correctly
- Added `ParseTimeWindow_UnknownValue_ThrowsArgumentException` test (new, covers FR-4)

**Run all Analytics tests:**
```bash
cd /home/user/worktrees/feature-3334-Arch-Review-Analytics-Timewindowparser-Uses-Dateti/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests" \
  --verbosity normal
```

Expected: all 6 tests pass (5 original + 1 new ArgumentException test).

**Run the full backend build and format check:**
```bash
cd /home/user/worktrees/feature-3334-Arch-Review-Analytics-Timewindowparser-Uses-Dateti/backend
dotnet build && dotnet format --verify-no-changes
```

**Commit:**
```
git add backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs
git commit -m "test: update GetProductMarginSummaryHandlerTests to use FakeTimeProvider and cover ArgumentException path"
```
