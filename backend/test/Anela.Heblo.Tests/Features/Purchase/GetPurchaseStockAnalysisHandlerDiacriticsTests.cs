using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class GetPurchaseStockAnalysisHandlerDiacriticsTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    public GetPurchaseStockAnalysisHandlerDiacriticsTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();

        _handler = new GetPurchaseStockAnalysisHandler(
            _catalogRepositoryMock.Object,
            _stockSeverityCalculatorMock.Object,
            _loggerMock.Object);
    }

    [Theory]
    [InlineData("krém", "Krém na ruce", true)] // krém should find Krém
    [InlineData("krem", "Krém na ruce", true)] // krem should find Krém (without diacritic)
    [InlineData("KREM", "Krém na ruce", true)] // KREM should find Krém
    [InlineData("cokolada", "Čokoláda", true)] // cokolada should find Čokoláda
    [InlineData("čokoláda", "Čokoláda", true)] // exact match should work
    [InlineData("ČOKOLÁDA", "Čokoláda", true)] // case insensitive exact match
    [InlineData("mydlo", "Přírodní mýdlo", true)] // mydlo should find mýdlo
    [InlineData("prirodni", "Přírodní mýdlo", true)] // prirodni should find Přírodní
    [InlineData("xyz", "Krém na ruce", false)] // no match
    public async Task Handle_Should_Find_Materials_Using_Diacritic_Insensitive_Search(
        string searchTerm,
        string productName,
        bool shouldBeFound)
    {
        // Arrange
        var catalogItem = new CatalogAggregate
        {
            ProductCode = "TEST001",
            ProductName = productName, // This triggers normalization
            Type = ProductType.Material
        };

        var catalogItems = new List<CatalogAggregate> { catalogItem };
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

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