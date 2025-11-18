using Anela.Heblo.Application.Features.PackingMaterials.Services;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class ConsumptionCalculationServiceTests
{
    private readonly ILogger<ConsumptionCalculationService> _mockLogger;

    public ConsumptionCalculationServiceTests()
    {
        _mockLogger = new MockLogger<ConsumptionCalculationService>();
    }

    [Theory]
    [InlineData(ConsumptionType.PerOrder, 2.5, 10, 5, 25.0)] // 2.5 per order * 10 orders = 25
    [InlineData(ConsumptionType.PerProduct, 1.2, 10, 20, 24.0)] // 1.2 per product * 20 products = 24
    [InlineData(ConsumptionType.PerDay, 5.0, 100, 200, 5.0)] // 5.0 per day (fixed)
    public async Task CalculateConsumptionAsync_ShouldCalculateCorrectly(
        ConsumptionType type, 
        decimal rate, 
        int orderCount, 
        int productCount, 
        decimal expectedConsumption)
    {
        // Arrange
        var material = new PackingMaterial("Test Material", rate, type, 100);
        var mockRepository = new MockPackingMaterialRepository();
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);

        // Act
        var result = await service.CalculateConsumptionAsync(material, orderCount, productCount);

        // Assert
        Assert.Equal(expectedConsumption, result);
    }

    [Fact]
    public async Task HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue()
    {
        // Arrange
        var mockRepository = new MockPackingMaterialRepository();
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);
        var date = DateOnly.FromDateTime(DateTime.Today);
        
        mockRepository.SetHasDailyProcessingBeenRun(date, true);

        // Act
        var result = await service.HasDayAlreadyBeenProcessedAsync(date);

        // Assert
        Assert.True(result);
    }
}