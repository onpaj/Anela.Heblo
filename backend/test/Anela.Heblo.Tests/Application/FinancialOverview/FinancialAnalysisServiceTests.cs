using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.FinancialOverview;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class FinancialAnalysisServiceTests
{
    private readonly Mock<ILedgerService> _ledgerServiceMock;
    private readonly Mock<IStockValueService> _stockValueServiceMock;
    private readonly IMemoryCache _memoryCache;
    private readonly FinancialAnalysisService _service;

    public FinancialAnalysisServiceTests()
    {
        _ledgerServiceMock = new Mock<ILedgerService>();
        _stockValueServiceMock = new Mock<IStockValueService>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        var options = Options.Create(new FinancialAnalysisOptions { MonthsToCache = 24 });

        _service = new FinancialAnalysisService(
            _ledgerServiceMock.Object,
            _stockValueServiceMock.Object,
            NullLogger<FinancialAnalysisService>.Instance,
            options,
            _memoryCache);

        // Default: ledger returns empty list
        _ledgerServiceMock
            .Setup(x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItem>());

        // Default: stock service returns empty list
        _stockValueServiceMock
            .Setup(x => x.GetStockValueChangesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyStockChange>());

        // Default: partial stock primitive returns a zero change stamped with the period's year/month
        _stockValueServiceMock
            .Setup(x => x.GetStockValueChangeForPeriodAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime start, DateTime _, CancellationToken __) =>
                new MonthlyStockChange { Year = start.Year, Month = start.Month });
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_WhenIncludeCurrentMonthTrue_AndCachePopulated_OnlyFetchesCurrentMonthFromLedger()
    {
        // Arrange: seed the cache with some completed months so cache is not empty
        var now = DateTime.UtcNow;
        var prevMonth = now.AddMonths(-1);
        SeedCacheForMonth(prevMonth.Year, prevMonth.Month);

        // Act
        await _service.GetFinancialOverviewAsync(
            months: 3,
            includeStockData: false,
            includeCurrentMonth: true);

        // Assert: ledger should only be called with the current month date range
        var currentMonthStart = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = now.Date;

        _ledgerServiceMock.Verify(
            x => x.GetLedgerItems(
                It.Is<DateTime>(d => d == currentMonthStart),
                It.Is<DateTime>(d => d == currentMonthEnd),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(1),
            "Ledger should be called with current month range only");

        // Assert: ledger should NOT be called with a start date more than 1 month ago
        var twoMonthsAgo = currentMonthStart.AddMonths(-1);
        _ledgerServiceMock.Verify(
            x => x.GetLedgerItems(
                It.Is<DateTime>(d => d < twoMonthsAgo),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "Ledger should NOT be called with historical date range when cache is populated");
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_WhenIncludeCurrentMonthTrue_AndCacheEmpty_FallsBackToFullRealTime()
    {
        // Arrange: cache is empty (not seeded)

        var now = DateTime.UtcNow;

        // Act
        await _service.GetFinancialOverviewAsync(
            months: 3,
            includeStockData: false,
            includeCurrentMonth: true);

        // Assert: ledger is called with a start date that is months ago (full real-time range)
        var threeMonthsAgo = new DateTime(now.Year, now.Month, 1).AddMonths(-3 + 1);
        // With includeCurrentMonth=true, endDate=today, startDate=endDate-months+1 months
        _ledgerServiceMock.Verify(
            x => x.GetLedgerItems(
                It.Is<DateTime>(d => d <= threeMonthsAgo.AddDays(1)),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(1),
            "With empty cache, full real-time range should be used");
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_WhenIncludeCurrentMonthTrue_AndCachePopulated_ReturnsCorrectMonthCount()
    {
        // Arrange: seed cache for 2 completed months
        var now = DateTime.UtcNow;
        SeedCacheForMonth(now.AddMonths(-1).Year, now.AddMonths(-1).Month);
        SeedCacheForMonth(now.AddMonths(-2).Year, now.AddMonths(-2).Month);

        // Act: request 3 months including current
        var result = await _service.GetFinancialOverviewAsync(
            months: 3,
            includeStockData: false,
            includeCurrentMonth: true);

        // Assert: result contains current month + 2 completed = 3 months
        result.Data.Should().HaveCount(3);
        result.Data[0].Year.Should().Be(now.Year);
        result.Data[0].Month.Should().Be(now.Month, "current month should be first (most recent)");
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_WhenDepartmentsExcludedAndCurrentMonthRequested_UsesFullRealTime()
    {
        // Arrange: seed cache so it's not empty
        var now = DateTime.UtcNow;
        SeedCacheForMonth(now.AddMonths(-1).Year, now.AddMonths(-1).Month);

        var excludedDepartments = new List<string> { "Marketing" };

        // Act
        await _service.GetFinancialOverviewAsync(
            months: 3,
            includeStockData: false,
            excludedDepartments: excludedDepartments,
            includeCurrentMonth: true);

        // Assert: with department filter, real-time is used — ledger is called with wide date range
        _ledgerServiceMock.Verify(
            x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task RefreshFinancialDataAsync_WhenLastRefreshWithinThrottleWindow_DoesNotInvokeDownstreamServices()
    {
        // Arrange: seed the last-refresh timestamp inside the 10-minute throttle window.
        // The throttle check uses the cache key "financial_last_refresh" and skips the refresh
        // when the timestamp is younger than 10 minutes.
        _memoryCache.Set("financial_last_refresh", DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromHours(24));

        // Act
        await _service.RefreshFinancialDataAsync(startDate: null, endDate: null);

        // Assert: the throttle short-circuits before any downstream call.
        _ledgerServiceMock.Verify(
            x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "throttle should prevent any ledger query when last refresh was less than 10 minutes ago");

        _stockValueServiceMock.Verify(
            x => x.GetStockValueChangesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "throttle should prevent any stock-value query when last refresh was less than 10 minutes ago");
    }

    private void SeedCacheForMonth(int year, int month)
    {
        var key = $"financial_monthly_data_{year}_{month}";
        _memoryCache.Set(key, new Anela.Heblo.Domain.Features.FinancialOverview.MonthlyFinancialData
        {
            Year = year,
            Month = month,
            Income = 10000m,
            Expenses = 8000m
        }, TimeSpan.FromHours(1));
    }

    private void SeedStockCacheForMonth(int year, int month, decimal materials, decimal semiProducts, decimal products)
    {
        var key = $"financial_stock_data_{year}_{month}";
        _memoryCache.Set(key, new MonthlyStockChange
        {
            Year = year,
            Month = month,
            StockChanges = new StockChangeByType
            {
                Materials = materials,
                SemiProducts = semiProducts,
                Products = products
            }
        }, TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_RealTime_ComputesIncomeAndExpensesByAccountPrefix_PreservingAllFr4Cases()
    {
        // Arrange — last completed month (includeCurrentMonth=false ends at last day of previous month)
        var now = DateTime.UtcNow;
        var lastMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var (debitItems, creditItems) = CreateFr4LedgerItems(lastMonthStart.AddDays(10));
        SetupLedgerMockForDebitCredit(debitItems, creditItems);

        // Act — empty cache routes to real-time path
        var response = await _service.GetFinancialOverviewAsync(
            months: 2,
            includeStockData: false,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert — expenses = debit5(100) - credit5(20) = 80; income = credit6(200) - debit6(50) = 150
        var lastMonthData = response.Data
            .FirstOrDefault(d => d.Year == lastMonthStart.Year && d.Month == lastMonthStart.Month);

        lastMonthData.Should().NotBeNull("the response must include the last completed month");
        lastMonthData!.Expenses.Should().Be(80m, "expenses = debit5(100) - credit5(20)");
        lastMonthData!.Income.Should().Be(150m, "income = credit6(200) - debit6(50)");
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_RealTimePath_WithMatchingStockChange_ComputesStockSummaryFromDtoValues()
    {
        // Arrange — empty cache routes to real-time path; months:1 + includeCurrentMonth:false
        // means the only month in range is last month (end date = last day of previous month).
        var now = DateTime.UtcNow;
        var lastMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        // Ledger mock keeps the default empty-list setup from the constructor, so
        // Income = 0, Expenses = 0, FinancialBalance = 0 for the month.

        _stockValueServiceMock
            .Setup(x => x.GetStockValueChangesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyStockChange>
            {
                new MonthlyStockChange
                {
                    Year = lastMonthStart.Year,
                    Month = lastMonthStart.Month,
                    StockChanges = new StockChangeByType { Materials = 100m, SemiProducts = 50m, Products = 25m }
                }
            });

        // Act
        var response = await _service.GetFinancialOverviewAsync(
            months: 1,
            includeStockData: true,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert — TotalStockValueChange = 100 + 50 + 25 = 175; FinancialBalance = 0 (empty ledger)
        response.Summary.StockSummary.Should().NotBeNull();
        response.Summary.StockSummary!.TotalStockValueChange.Should().Be(175m);
        response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(175m, "only one month is in range");
        response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(175m, "TotalBalance(0) + TotalStockValueChange(175)");
        response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(175m, "AverageBalance(0) + AverageStockChange(175)");
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_RealTimePath_WithNoMatchingStockChange_TreatsStockValueAsZero()
    {
        // Arrange — stock service returns a change for a month OUTSIDE the requested range,
        // so the per-month lookup in GetFinancialOverviewRealTimeAsync finds no match for the
        // one month actually returned (last month), and TotalStockValueChange falls back to 0.
        var now = DateTime.UtcNow;
        var lastMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var unrelatedMonth = lastMonthStart.AddMonths(-5);

        _stockValueServiceMock
            .Setup(x => x.GetStockValueChangesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyStockChange>
            {
                new MonthlyStockChange
                {
                    Year = unrelatedMonth.Year,
                    Month = unrelatedMonth.Month,
                    StockChanges = new StockChangeByType { Materials = 999m, SemiProducts = 999m, Products = 999m }
                }
            });

        // Act
        var response = await _service.GetFinancialOverviewAsync(
            months: 1,
            includeStockData: true,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert — no stock change matches the returned month, so it contributes 0
        response.Summary.StockSummary.Should().NotBeNull();
        response.Summary.StockSummary!.TotalStockValueChange.Should().Be(0m);
        response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(0m);
        response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(0m, "TotalBalance(0) + TotalStockValueChange(0)");
        response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(0m);
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_CachedPath_WithMatchingStockChange_ComputesStockSummaryFromDtoValues()
    {
        // Arrange — seed both the monthly financial data and the stock data cache entries for
        // last month, so GetFinancialOverviewAsync routes to the cached path (CachedMonthsCount > 0)
        // and months:1 selects exactly that one cached month.
        var now = DateTime.UtcNow;
        var prevMonth = now.AddMonths(-1);
        SeedCacheForMonth(prevMonth.Year, prevMonth.Month);
        SeedStockCacheForMonth(prevMonth.Year, prevMonth.Month, materials: 100m, semiProducts: 50m, products: 25m);

        // Act
        var response = await _service.GetFinancialOverviewAsync(
            months: 1,
            includeStockData: true,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert — FinancialBalance = Income(10000) - Expenses(8000) = 2000 (from SeedCacheForMonth);
        // TotalStockValueChange = 100 + 50 + 25 = 175 (from SeedStockCacheForMonth).
        response.Summary.StockSummary.Should().NotBeNull();
        response.Summary.StockSummary!.TotalStockValueChange.Should().Be(175m);
        response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(175m, "only one month is in range");
        response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(2175m, "TotalBalance(2000) + TotalStockValueChange(175)");
        response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(2175m, "AverageBalance(2000) + AverageStockChange(175)");
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_RealTimePath_IncludeStockDataFalse_StockSummaryIsNull()
    {
        // Arrange — empty cache routes to real-time path; stock service default (empty list) applies.

        // Act
        var response = await _service.GetFinancialOverviewAsync(
            months: 1,
            includeStockData: false,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert
        response.Summary.StockSummary.Should().BeNull();
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_CachedPath_IncludeStockDataFalse_StockSummaryIsNull()
    {
        // Arrange — seed monthly cache so the cached path is used (CachedMonthsCount > 0).
        var now = DateTime.UtcNow;
        var prevMonth = now.AddMonths(-1);
        SeedCacheForMonth(prevMonth.Year, prevMonth.Month);

        // Act
        var response = await _service.GetFinancialOverviewAsync(
            months: 1,
            includeStockData: false,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert
        response.Summary.StockSummary.Should().BeNull();
    }

    [Fact]
    public async Task GetFinancialOverviewAsync_ZeroMonthsRequested_ReturnsEmptySummaryWithZeroedAverages()
    {
        // Arrange — months: 0 makes the real-time path's computed startDate land after endDate
        // (startDate = end-of-previous-month.AddMonths(1), first-of-month), so the month-by-month
        // loop never executes and monthlyData/orderedData stay empty. Cache is empty by default,
        // so this routes to the real-time path.

        // Act
        var response = await _service.GetFinancialOverviewAsync(
            months: 0,
            includeStockData: true,
            excludedDepartments: null,
            includeCurrentMonth: false);

        // Assert — data is empty; all totals are 0; averages are 0 (not NaN/exception) because
        // BuildSummary/CreateStockSummary guard every Average() call with data.Any().
        response.Data.Should().BeEmpty();
        response.Summary.TotalIncome.Should().Be(0m);
        response.Summary.AverageMonthlyIncome.Should().Be(0m);
        response.Summary.AverageMonthlyBalance.Should().Be(0m);
        response.Summary.StockSummary.Should().NotBeNull("includeStockData is true, even with zero months");
        response.Summary.StockSummary!.TotalStockValueChange.Should().Be(0m);
        response.Summary.StockSummary!.AverageMonthlyStockChange.Should().Be(0m);
        response.Summary.StockSummary!.TotalBalanceWithStock.Should().Be(0m);
        response.Summary.StockSummary!.AverageMonthlyTotalBalance.Should().Be(0m);
    }

    private static (List<LedgerItem> debitItems, List<LedgerItem> creditItems) CreateFr4LedgerItems(DateTime midLastMonth) =>
        (
            new List<LedgerItem>
            {
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = "501100", CreditAccountNumber = null!, Amount = 100m },
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = "601200", CreditAccountNumber = null!, Amount = 50m },
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = null!, Amount = 1000m },
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = "311000", CreditAccountNumber = null!, Amount = 999m },
            },
            new List<LedgerItem>
            {
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = "501100", Amount = 20m },
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = "601200", Amount = 200m },
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = null!, Amount = 1000m },
                new LedgerItem { Date = midLastMonth, DebitAccountNumber = null!, CreditAccountNumber = "311000", Amount = 999m },
            }
        );

    private void SetupLedgerMockForDebitCredit(List<LedgerItem> debitItems, List<LedgerItem> creditItems)
    {
        _ledgerServiceMock
            .Setup(x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IEnumerable<string>?>(p => p != null),
                It.Is<IEnumerable<string>?>(p => p == null),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(debitItems);

        _ledgerServiceMock
            .Setup(x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.Is<IEnumerable<string>?>(p => p == null),
                It.Is<IEnumerable<string>?>(p => p != null),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(creditItems);
    }

    [Fact]
    public async Task RefreshFinancialDataAsync_WhenDatesNull_UsesDefaultDateRange()
    {
        // Arrange — fresh service with MonthsToCache=3 so the expected range is predictable
        var options3 = Options.Create(new FinancialAnalysisOptions { MonthsToCache = 3 });
        var cache3 = new MemoryCache(new MemoryCacheOptions());
        var service3 = new FinancialAnalysisService(
            _ledgerServiceMock.Object,
            _stockValueServiceMock.Object,
            NullLogger<FinancialAnalysisService>.Instance,
            options3,
            cache3);

        // No financial_last_refresh set, so throttle check passes.
        var now = DateTime.UtcNow;
        var expectedEndDate = new DateTime(now.Year, now.Month, 1).AddDays(-1); // last day of previous month
        var expectedStartMonth = expectedEndDate.AddMonths(-2);
        var expectedStartDate = new DateTime(expectedStartMonth.Year, expectedStartMonth.Month, 1);

        // Act
        await service3.RefreshFinancialDataAsync(startDate: null, endDate: null);

        // Assert — ledger must be called with dates within the computed default range
        _ledgerServiceMock.Verify(
            x => x.GetLedgerItems(
                It.Is<DateTime>(d => d >= expectedStartDate),
                It.Is<DateTime>(d => d <= expectedEndDate),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(1),
            "ledger must be called within the computed default date range when both dates are null");
    }

    [Fact]
    public async Task RefreshFinancialDataAsync_WhenOutsideThrottleWindow_CallsServicesOncePerMonth()
    {
        // Arrange — fresh cache so the throttle check passes.
        // MonthsToCache=4 → endDate=last-day-of-prev-month, startDate=endDate-3months.
        // The loop iterates while currentDate (first-of-month) >= startDate (last-of-month-3-earlier),
        // which always yields exactly 3 months of actual calls regardless of which month we're in.
        var cache4 = new MemoryCache(new MemoryCacheOptions());
        var options4 = Options.Create(new FinancialAnalysisOptions { MonthsToCache = 4 });
        var service4 = new FinancialAnalysisService(
            _ledgerServiceMock.Object,
            _stockValueServiceMock.Object,
            NullLogger<FinancialAnalysisService>.Instance,
            options4,
            cache4);

        // Act
        await service4.RefreshFinancialDataAsync(startDate: null, endDate: null);

        // Assert — each of the 3 months triggers 2 ledger calls (debit + credit) and 1 stock call
        _ledgerServiceMock.Verify(
            x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(6),
            "ledger should be called twice per month (debit + credit) × 3 months = 6 total calls");

        _stockValueServiceMock.Verify(
            x => x.GetStockValueChangesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "stock service should be called once per month × 3 months = 3 total calls");

        // Assert the refresh timestamp is persisted after a successful run
        cache4.TryGetValue("financial_last_refresh", out DateTime? lastRefresh).Should().BeTrue(
            "last refresh timestamp should be cached after a successful refresh");
        lastRefresh.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(9, 3)]
    public async Task GetFinancialComparisonAsync_ClampsYearsToBetween2And3(int requested, int expectedYears)
    {
        var response = await _service.GetFinancialComparisonAsync(
            years: requested, includeStockData: false, excludedDepartments: null, includePartialMonth: true);

        response.Series.Should().HaveCount(expectedYears);
        response.Metadata.Years.Should().HaveCount(expectedYears);
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_PartialMonth_IsCutAtSameDayForEveryYear()
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var anchorYear = cutoff.Year;
        var partialMonth = cutoff.Month;
        var cutoffDay = cutoff.Day;

        await _service.GetFinancialComparisonAsync(
            years: 2, includeStockData: false, excludedDepartments: null, includePartialMonth: true);

        // Anchor year: partial month queried through the cutoff date itself.
        var anchorStart = new DateTime(anchorYear, partialMonth, 1);
        _ledgerServiceMock.Verify(x => x.GetLedgerItems(
            anchorStart, cutoff,
            It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Prior year: same month cut at the same day-of-month (clamped to that month's length).
        var priorYear = anchorYear - 1;
        var priorStart = new DateTime(priorYear, partialMonth, 1);
        var priorEnd = new DateTime(priorYear, partialMonth,
            Math.Min(cutoffDay, DateTime.DaysInMonth(priorYear, partialMonth)));
        _ledgerServiceMock.Verify(x => x.GetLedgerItems(
            priorStart, priorEnd,
            It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_MarksPartialCells_AndOmitsAnchorFutureMonths()
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var anchorYear = cutoff.Year;

        var response = await _service.GetFinancialComparisonAsync(
            years: 2, includeStockData: true, excludedDepartments: null, includePartialMonth: true);

        var anchorSeries = response.Series.Single(s => s.Year == anchorYear);
        anchorSeries.Months.Should().OnlyContain(m => m.Month <= cutoff.Month);
        anchorSeries.Months.Max(m => m.Month).Should().Be(cutoff.Month);

        var partialCells = response.Series.SelectMany(s => s.Months).Where(m => m.IsPartial == true).ToList();
        partialCells.Should().NotBeEmpty();
        partialCells.Should().OnlyContain(m => m.Month == cutoff.Month);
        partialCells.Should().OnlyContain(m =>
            m.PartialDayOfMonth == Math.Min(cutoff.Day, DateTime.DaysInMonth(m.Year, m.Month)));
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_WhenPartialExcluded_DropsPartialMonthFromEveryYear()
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var partialMonth = cutoff.Month;

        var response = await _service.GetFinancialComparisonAsync(
            years: 2, includeStockData: false, excludedDepartments: null, includePartialMonth: false);

        response.Series.Should().HaveCount(2);
        response.Series.SelectMany(s => s.Months).Should().NotContain(m => m.Month == partialMonth);
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_ServesCompletedMonthFromCache_WhenNoDepartmentFilter()
    {
        // Arrange — pick a completed (non-partial) month in the prior year and seed its cache entry.
        // Every month of a fully-past prior year is completed, so we only need to dodge the current
        // cutoff's partial month (which only matters for the anchor year, but picking a different
        // month keeps the test robust regardless of anchor/prior alignment).
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var priorYear = cutoff.Year - 1;
        var month = cutoff.Month == 1 ? 2 : 1;
        SeedCacheForMonth(priorYear, month);

        // Act
        var response = await _service.GetFinancialComparisonAsync(
            years: 2, includeStockData: false, excludedDepartments: null, includePartialMonth: false);

        // Assert — the cached month's start date must never reach the ledger.
        var monthStart = new DateTime(priorYear, month, 1);
        _ledgerServiceMock.Verify(x => x.GetLedgerItems(
            monthStart, It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a completed month with no department filter must be served from cache, not the ledger");

        // Assert — the returned cell reflects the seeded cache values (proves it came from cache).
        var priorSeries = response.Series.Single(s => s.Year == priorYear);
        var cachedCell = priorSeries.Months.Single(m => m.Month == month);
        cachedCell.Income.Should().Be(10000m, "the cell must be populated from the seeded cache entry");
        cachedCell.Expenses.Should().Be(8000m, "the cell must be populated from the seeded cache entry");
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_BypassesCache_WhenDepartmentFilterPresent()
    {
        // Arrange — seed the same completed prior-year month, but request with an active department
        // filter. BuildComparisonCellAsync's cache short-circuit requires excludedSet == null, so a
        // filter must force a live ledger computation even though the month is fully cached.
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var priorYear = cutoff.Year - 1;
        var month = cutoff.Month == 1 ? 2 : 1;
        SeedCacheForMonth(priorYear, month);

        // Act
        await _service.GetFinancialComparisonAsync(
            years: 2,
            includeStockData: false,
            excludedDepartments: new List<string> { "SomeDept" },
            includePartialMonth: false);

        // Assert — the department filter must bypass the cache and query the ledger for that month.
        var monthStart = new DateTime(priorYear, month, 1);
        _ledgerServiceMock.Verify(x => x.GetLedgerItems(
            monthStart, It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "a department filter must bypass the cache and compute the month live");
    }
}
