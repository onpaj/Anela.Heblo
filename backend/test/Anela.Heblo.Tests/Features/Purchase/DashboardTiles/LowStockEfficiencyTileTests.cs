using Anela.Heblo.Application.Features.Purchase.DashboardTiles;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase.DashboardTiles;

public class LowStockEfficiencyTileTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly LowStockEfficiencyTile _tile;
    private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    public LowStockEfficiencyTileTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);

        _tile = new LowStockEfficiencyTile(_mediatorMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_WithMixedEfficiencyAndConfiguration_CountsOnlyLowEfficiencyConfiguredItems()
    {
        // Arrange
        var response = new GetPurchaseStockAnalysisResponse
        {
            Items = new List<StockAnalysisItemDto>
            {
                new StockAnalysisItemDto { StockEfficiencyPercentage = 10, IsConfigured = true }, // counted
                new StockAnalysisItemDto { StockEfficiencyPercentage = 10, IsConfigured = false }, // excluded - not configured
                new StockAnalysisItemDto { StockEfficiencyPercentage = 20, IsConfigured = true }, // excluded - boundary, strict <
                new StockAnalysisItemDto { StockEfficiencyPercentage = 25, IsConfigured = true }, // excluded - above threshold
            }
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus()
    {
        // Arrange
        var response = new GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetString().Should().Be("Failed to load stock analysis data");
    }
}
