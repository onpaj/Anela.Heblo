using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class GetPurchaseStockAnalysisHandlerDiacriticsTests
{
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    public GetPurchaseStockAnalysisHandlerDiacriticsTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();

        _handler = new GetPurchaseStockAnalysisHandler(
            _materialCatalogMock.Object,
            _stockSeverityCalculatorMock.Object,
            _loggerMock.Object);
    }

    [Theory]
    [InlineData("krém", "Krém na ruce", true)]
    [InlineData("krem", "Krém na ruce", true)]
    [InlineData("KREM", "Krém na ruce", true)]
    [InlineData("cokolada", "Čokoláda", true)]
    [InlineData("čokoláda", "Čokoláda", true)]
    [InlineData("ČOKOLÁDA", "Čokoláda", true)]
    [InlineData("mydlo", "Přírodní mýdlo", true)]
    [InlineData("prirodni", "Přírodní mýdlo", true)]
    [InlineData("xyz", "Krém na ruce", false)]
    public async Task Handle_Should_Find_Materials_Using_Diacritic_Insensitive_Search(
        string searchTerm,
        string productName,
        bool shouldBeFound)
    {
        // Arrange
        var snapshot = new MaterialStockSnapshot
        {
            ProductCode = "TEST001",
            ProductName = productName,
            ProductNameNormalized = productName.NormalizeForSearch(),
            ProductType = MaterialProductType.Material,
            MinimalOrderQuantity = string.Empty,
            IsMinStockConfigured = false,
            IsOptimalStockConfigured = false,
            Stock = new MaterialStockLevels
            {
                Available = 0,
                Ordered = 0,
                EffectiveStock = 0,
            },
            StockMinSetup = 0,
            OptimalStockDaysSetup = 0,
            ConsumptionInPeriod = 0,
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { snapshot });

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SearchTerm = searchTerm,
            PageNumber = 1,
            PageSize = 20
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        if (shouldBeFound)
        {
            result.Items.Should().HaveCount(1);
            result.Items[0].ProductName.Should().Be(productName);
            result.Items[0].ProductCode.Should().Be("TEST001");
        }
        else
        {
            result.Items.Should().BeEmpty();
        }
    }
}