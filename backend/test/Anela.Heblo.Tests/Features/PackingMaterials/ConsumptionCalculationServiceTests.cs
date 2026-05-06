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

    [Fact]
    public async Task ProcessDailyConsumptionAsync_DecrementsQuantityAndReturnsCount_WhenInvoicesExist()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material1 = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 100m);
        var material2 = new PackingMaterial("Stickers", 2m, ConsumptionType.PerOrder, 50m);
        var mockRepository = new MockPackingMaterialRepository();
        mockRepository.SetMaterials(new[] { material1, material2 });
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date, orderCount: 5, productCount: 10);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(2, result.MaterialsProcessed);
        Assert.Equal(95m, mockRepository.UpdatedMaterials[0].CurrentQuantity); // 100 - 1*5
        Assert.Equal(40m, mockRepository.UpdatedMaterials[1].CurrentQuantity); // 50 - 2*5
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_WritesMarkerAndReturnsZero_WhenNoConsumptionCalculated()
    {
        // Arrange — PerOrder material but zero orders means consumption = 0
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var mockRepository = new MockPackingMaterialRepository();
        mockRepository.SetMaterials(new[] { material });
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date, orderCount: 0, productCount: 0);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);

        // Quantity must NOT have changed
        Assert.Single(mockRepository.UpdatedMaterials);
        Assert.Equal(8000m, mockRepository.UpdatedMaterials[0].CurrentQuantity);

        // A marker log must have been written so re-runs are blocked
        var markerLog = mockRepository.UpdatedMaterials[0].Logs.Single();
        Assert.Equal(LogEntryType.AutomaticConsumption, markerLog.LogType);
        Assert.Equal(date, markerLog.Date);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenDayAlreadyProcessed()
    {
        // Arrange — marker already present
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var mockRepository = new MockPackingMaterialRepository();
        mockRepository.SetMaterials(new[] { material });
        mockRepository.SetHasDailyProcessingBeenRun(date, true);
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date, orderCount: 5, productCount: 10);

        // Assert
        Assert.False(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);
        Assert.Empty(mockRepository.UpdatedMaterials);
    }
}