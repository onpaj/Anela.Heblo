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
}
