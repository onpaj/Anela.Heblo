using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class GetFinancialOverviewHandlerTests
{
    private readonly Mock<IFinancialAnalysisService> _financialAnalysisServiceMock;
    private readonly Mock<ILogger<GetFinancialOverviewHandler>> _loggerMock;
    private readonly GetFinancialOverviewHandler _handler;

    public GetFinancialOverviewHandlerTests()
    {
        _financialAnalysisServiceMock = new Mock<IFinancialAnalysisService>();
        _loggerMock = new Mock<ILogger<GetFinancialOverviewHandler>>();

        _handler = new GetFinancialOverviewHandler(
            _financialAnalysisServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_CallsFinancialAnalysisService()
    {
        // Arrange
        var request = new GetFinancialOverviewRequest { Months = 6, IncludeStockData = false };

        var expectedResponse = new GetFinancialOverviewResponse
        {
            Data = Enumerable.Range(1, 6).Select(i =>
            {
                var monthDate = DateTime.UtcNow.AddMonths(-i);
                return new MonthlyFinancialDataDto
                {
                    Year = monthDate.Year,
                    Month = monthDate.Month,
                    Income = 1000m * i,
                    Expenses = 800m * i,
                    FinancialBalance = 200m * i
                };
            }).ToList(),
            Summary = new FinancialSummaryDto()
        };

        _financialAnalysisServiceMock.Setup(x => x.GetFinancialOverviewAsync(6, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(6, result.Data.Count);
        _financialAnalysisServiceMock.Verify(x => x.GetFinancialOverviewAsync(6, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UsesCorrectMonthsParameter()
    {
        // Arrange
        var request = new GetFinancialOverviewRequest { Months = 3, IncludeStockData = false };

        var expectedResponse = new GetFinancialOverviewResponse
        {
            Data = new List<MonthlyFinancialDataDto>(),
            Summary = new FinancialSummaryDto()
        };

        _financialAnalysisServiceMock.Setup(x => x.GetFinancialOverviewAsync(3, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        _financialAnalysisServiceMock.Verify(x => x.GetFinancialOverviewAsync(3, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IncludesStockData_WhenRequested()
    {
        // Arrange
        var request = new GetFinancialOverviewRequest { Months = 3, IncludeStockData = true };

        var expectedResponse = new GetFinancialOverviewResponse
        {
            Data = new List<MonthlyFinancialDataDto>
            {
                new()
                {
                    Year = 2024,
                    Month = 7,
                    Income = 1000m,
                    Expenses = 800m,
                    StockChanges = new StockChangeDto { Materials = 100m },
                    TotalStockValueChange = 100m
                }
            },
            Summary = new FinancialSummaryDto
            {
                StockSummary = new StockSummaryDto { TotalStockValueChange = 100m }
            }
        };

        _financialAnalysisServiceMock.Setup(x => x.GetFinancialOverviewAsync(3, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Summary.StockSummary);
        Assert.Equal(100m, result.Summary.StockSummary.TotalStockValueChange);
        _financialAnalysisServiceMock.Verify(x => x.GetFinancialOverviewAsync(3, true, It.IsAny<CancellationToken>()), Times.Once);

        // Verify stock data was included in response
        var monthWithStock = result.Data.FirstOrDefault(d => d.StockChanges != null);
        Assert.NotNull(monthWithStock);
        Assert.NotNull(monthWithStock.StockChanges);
        Assert.Equal(100m, monthWithStock.StockChanges.Materials);
    }
}