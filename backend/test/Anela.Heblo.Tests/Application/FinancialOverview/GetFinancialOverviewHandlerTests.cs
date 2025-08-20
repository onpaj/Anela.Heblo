using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class GetFinancialOverviewHandlerTests
{
    private readonly Mock<ILedgerService> _ledgerServiceMock;
    private readonly Mock<IStockValueService> _stockValueServiceMock;
    private readonly Mock<ILogger<GetFinancialOverviewHandler>> _loggerMock;
    private readonly GetFinancialOverviewHandler _handler;

    public GetFinancialOverviewHandlerTests()
    {
        _ledgerServiceMock = new Mock<ILedgerService>();
        _stockValueServiceMock = new Mock<IStockValueService>();
        _loggerMock = new Mock<ILogger<GetFinancialOverviewHandler>>();
        
        _handler = new GetFinancialOverviewHandler(
            _ledgerServiceMock.Object,
            _stockValueServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ExcludesCurrentMonth_WhenCalculatingDateRange()
    {
        // Arrange
        var request = new GetFinancialOverviewRequest { Months = 6, IncludeStockData = false };
        
        // Setup empty data to focus on date logic
        _ledgerServiceMock.Setup(x => x.GetLedgerItems(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItem>());
        
        _stockValueServiceMock.Setup(x => x.GetStockValueChangesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyStockChange>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert - Verify that the endDate was set to last day of previous month
        var now = DateTime.UtcNow;
        var expectedEndDate = new DateTime(now.Year, now.Month, 1).AddDays(-1); // Last day of previous month
        var expectedStartDate = expectedEndDate.AddMonths(-6 + 1);
        expectedStartDate = new DateTime(expectedStartDate.Year, expectedStartDate.Month, 1); // First day of start month

        // Verify ledger service was called with correct date range
        _ledgerServiceMock.Verify(x => x.GetLedgerItems(
            It.Is<DateTime>(d => d.Date == expectedStartDate.Date),
            It.Is<DateTime>(d => d.Date == expectedEndDate.Date),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(2)); // Called twice for debit and credit
    }

    [Fact]
    public async Task Handle_Returns6MonthsOfData_ExcludingCurrentMonth()
    {
        // Arrange
        var request = new GetFinancialOverviewRequest { Months = 6, IncludeStockData = false };
        
        // Setup empty data
        _ledgerServiceMock.Setup(x => x.GetLedgerItems(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItem>());
        
        _stockValueServiceMock.Setup(x => x.GetStockValueChangesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyStockChange>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(6, result.Data.Count);
        
        // Verify that none of the returned months is the current month
        var currentMonth = DateTime.UtcNow.Month;
        var currentYear = DateTime.UtcNow.Year;
        
        var hasCurrentMonth = result.Data.Any(d => d.Month == currentMonth && d.Year == currentYear);
        Assert.False(hasCurrentMonth, "Result should not include the current month");
        
        // Verify that the latest month in result is the previous month
        var latestMonth = result.Data.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month).First();
        var previousMonth = DateTime.UtcNow.AddMonths(-1);
        
        Assert.Equal(previousMonth.Month, latestMonth.Month);
        Assert.Equal(previousMonth.Year, latestMonth.Year);
    }

    [Fact]
    public async Task Handle_IncludesStockData_WhenRequested()
    {
        // Arrange
        var request = new GetFinancialOverviewRequest { Months = 3, IncludeStockData = true };
        
        _ledgerServiceMock.Setup(x => x.GetLedgerItems(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItem>());
        
        var stockChanges = new List<MonthlyStockChange>
        {
            new() { Year = 2024, Month = 7, StockChanges = new() { Materials = 100m } }
        };
        _stockValueServiceMock.Setup(x => x.GetStockValueChangesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockChanges);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Summary.StockSummary);
        _stockValueServiceMock.Verify(x => x.GetStockValueChangesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}