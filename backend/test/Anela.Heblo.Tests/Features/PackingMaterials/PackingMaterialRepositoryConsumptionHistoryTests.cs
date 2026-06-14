using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialRepositoryConsumptionHistoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PackingMaterialRepository _repository;

    public PackingMaterialRepositoryConsumptionHistoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialConsumptionHistory_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PackingMaterialRepository(_context);
    }

    [Fact]
    public async Task GetConsumptionHistoryAsync_UnionsBothSources_OrderedByDateDescending()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerOrder, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        var consumption = new PackingMaterialConsumption(
            material.Id, new DateOnly(2026, 1, 10), ConsumptionType.PerOrder, 5m, invoiceId: "INV-1", productCode: "P1");
        var log = new PackingMaterialLog(
            material.Id, new DateOnly(2026, 1, 12), 100m, 90m, LogEntryType.Manual);
        await _context.Set<PackingMaterialConsumption>().AddAsync(consumption);
        await _context.Set<PackingMaterialLog>().AddAsync(log);
        await _context.SaveChangesAsync();

        var filter = new MaterialConsumptionHistoryFilter(null, null, null, null, null, null);

        // Act
        var (items, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip: 0, take: 20, ascending: false, CancellationToken.None);

        // Assert
        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].RecordType.Should().Be(HistoryRecordType.QuantityChange); // 2026-01-12 newest first
        items[0].ChangeAmount.Should().Be(-10m);
        items[1].RecordType.Should().Be(HistoryRecordType.Consumption);    // 2026-01-10
        items[1].Amount.Should().Be(5m);
        items[1].InvoiceId.Should().Be("INV-1");
    }

    [Fact]
    public async Task GetConsumptionHistoryAsync_ConsumptionOnlyFilter_ExcludesQuantityLogs()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerOrder, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        await _context.Set<PackingMaterialConsumption>().AddAsync(
            new PackingMaterialConsumption(material.Id, new DateOnly(2026, 1, 10), ConsumptionType.PerOrder, 5m, invoiceId: "INV-1"));
        await _context.Set<PackingMaterialLog>().AddAsync(
            new PackingMaterialLog(material.Id, new DateOnly(2026, 1, 12), 100m, 90m, LogEntryType.Manual));
        await _context.SaveChangesAsync();

        var filter = new MaterialConsumptionHistoryFilter(null, null, null, null, null, InvoiceId: "INV-1");

        // Act
        var (items, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip: 0, take: 20, ascending: false, CancellationToken.None);

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle()
            .Which.RecordType.Should().Be(HistoryRecordType.Consumption);
    }

    [Fact]
    public async Task GetConsumptionHistoryAsync_PagesResults()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerOrder, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        for (var day = 1; day <= 5; day++)
        {
            await _context.Set<PackingMaterialConsumption>().AddAsync(
                new PackingMaterialConsumption(material.Id, new DateOnly(2026, 1, day), ConsumptionType.PerDay, day));
        }
        await _context.SaveChangesAsync();

        var filter = new MaterialConsumptionHistoryFilter(null, null, null, null, null, null);

        // Act
        var (page1, total) = await _repository.GetConsumptionHistoryAsync(filter, skip: 0, take: 2, ascending: false, CancellationToken.None);
        var (page2, _) = await _repository.GetConsumptionHistoryAsync(filter, skip: 2, take: 2, ascending: false, CancellationToken.None);

        // Assert
        total.Should().Be(5);
        page1.Should().HaveCount(2);
        page1[0].Date.Should().Be(new DateOnly(2026, 1, 5));
        page2.Should().HaveCount(2);
        page2[0].Date.Should().Be(new DateOnly(2026, 1, 3));
    }

    public void Dispose() => _context.Dispose();
}
