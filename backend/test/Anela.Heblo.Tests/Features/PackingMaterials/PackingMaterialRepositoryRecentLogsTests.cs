using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialRepositoryRecentLogsTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PackingMaterialRepository _repository;

    public PackingMaterialRepositoryRecentLogsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialRecentLogs_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PackingMaterialRepository(_context);
    }

    [Fact]
    public async Task GetRecentLogsForMaterialsAsync_ReturnsLogsGroupedByMaterialId_WithinWindow()
    {
        // Arrange
        var m1 = new PackingMaterial("M1", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new PackingMaterial("M2", 1m, ConsumptionType.PerDay, 100m);
        var m3 = new PackingMaterial("M3", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddRangeAsync(m1, m2, m3);
        await _context.SaveChangesAsync();

        var inWindow = DateTime.UtcNow.AddDays(-5);
        var outOfWindow = DateTime.UtcNow.AddDays(-45);

        var log1 = CreateLog(m1.Id, 100m, 90m, inWindow);
        var log2 = CreateLog(m1.Id, 90m, 80m, inWindow.AddHours(1));
        var log3 = CreateLog(m2.Id, 100m, 70m, inWindow);
        var log4 = CreateLog(m2.Id, 70m, 60m, outOfWindow); // outside window — should be excluded
        await _context.Set<PackingMaterialLog>().AddRangeAsync(log1, log2, log3, log4);
        await _context.SaveChangesAsync();

        var fromDate = DateTime.UtcNow.AddMonths(-1);

        // Act
        var result = await _repository.GetRecentLogsForMaterialsAsync(
            new[] { m1.Id, m2.Id, m3.Id },
            fromDate,
            CancellationToken.None);

        // Assert
        result.Should().ContainKey(m1.Id);
        result[m1.Id].Should().HaveCount(2);
        result.Should().ContainKey(m2.Id);
        result[m2.Id].Should().HaveCount(1, "the out-of-window log is excluded");
        result.Should().NotContainKey(m3.Id, "materials without qualifying logs are absent from the result");
    }

    [Fact]
    public async Task GetRecentLogsForMaterialsAsync_ReturnsEmptyDictionary_WhenInputIsEmpty()
    {
        // Act
        var result = await _repository.GetRecentLogsForMaterialsAsync(
            Array.Empty<int>(),
            DateTime.UtcNow.AddMonths(-1),
            CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
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
