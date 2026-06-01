using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetPackingMaterialsListHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PackingMaterialRepository _repository;
    private readonly GetPackingMaterialsListHandler _handler;

    public GetPackingMaterialsListHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialsList_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PackingMaterialRepository(_context);
        _handler = new GetPackingMaterialsListHandler(
            _repository,
            NullLogger<GetPackingMaterialsListHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsNumericForecast_WhenMaterialHasNegativeChangeLogsInWindow()
    {
        // Arrange
        var material = new PackingMaterial("WithLogs", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        var inWindow = DateTime.UtcNow.AddDays(-3);
        // Two consumption logs: 100→90 and 90→80, each ChangeAmount = -10
        var log1 = CreateLog(material.Id, oldQty: 100m, newQty: 90m, createdAt: inWindow);
        var log2 = CreateLog(material.Id, oldQty: 90m, newQty: 80m, createdAt: inWindow.AddHours(2));
        await _context.Set<PackingMaterialLog>().AddRangeAsync(log1, log2);
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        var dto = response.Materials.Single();
        dto.ForecastedDays.Should().NotBeNull();
        // CurrentQuantity=100, avg consumption per log = 10, so 100/10 = 10
        dto.ForecastedDays.Should().Be(10m);
    }

    [Fact]
    public async Task Handle_ReturnsZeroForecast_WhenCurrentQuantityIsZero()
    {
        // Arrange
        var material = new PackingMaterial("ZeroQty", 1m, ConsumptionType.PerDay, 0m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        response.Materials.Single().ForecastedDays.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_ReturnsNullForecast_WhenNoQualifyingLogsInWindow()
    {
        // Arrange
        var material = new PackingMaterial("NoLogs", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        response.Materials.Single().ForecastedDays.Should().BeNull();
    }

    private static PackingMaterialLog CreateLog(int materialId, decimal oldQty, decimal newQty, DateTime createdAt)
    {
        var log = new PackingMaterialLog(
            materialId,
            DateOnly.FromDateTime(createdAt),
            oldQty,
            newQty,
            LogEntryType.Manual);

        // Set the private CreatedAt property via reflection so we can control timing
        typeof(PackingMaterialLog)
            .GetProperty(nameof(PackingMaterialLog.CreatedAt))!
            .SetValue(log, createdAt);

        return log;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
