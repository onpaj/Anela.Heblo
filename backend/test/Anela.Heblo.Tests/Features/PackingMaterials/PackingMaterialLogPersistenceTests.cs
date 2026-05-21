using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Services;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialLogPersistenceTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public PackingMaterialLogPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialLogPersistence_{Guid.NewGuid()}")
            .EnableSensitiveDataLogging()
            .Options;

        _context = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task UpdatePackingMaterialQuantityHandler_PersistsLogRow()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("user-42", null, null, true));

        // Create a mock repository that delegates to the context for persistence
        var repository = new Mock<IPackingMaterialRepository>();

        repository
            .Setup(r => r.GetByIdAsync(material.Id, It.IsAny<CancellationToken>()))
            .Returns(async (int id, CancellationToken ct) => await _context.PackingMaterials.FindAsync(new object[] { id }, cancellationToken: ct));

        repository
            .Setup(r => r.UpdateAsync(It.IsAny<PackingMaterial>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        repository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _context.SaveChanges())
            .Returns(Task.FromResult(0));

        repository
            .Setup(r => r.GetRecentLogsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<IEnumerable<PackingMaterialLog>>(new List<PackingMaterialLog>()));

        var handler = new UpdatePackingMaterialQuantityHandler(repository.Object, currentUser.Object);

        // Act
        var response = await handler.Handle(
            new UpdatePackingMaterialQuantityRequest
            {
                Id = material.Id,
                NewQuantity = 80m,
                Date = new DateOnly(2026, 5, 21)
            },
            CancellationToken.None);

        // Assert — query the DB, not the in-memory aggregate
        var persistedLogs = await _context.Set<PackingMaterialLog>()
            .Where(l => l.PackingMaterialId == material.Id)
            .ToListAsync();

        persistedLogs.Should().HaveCount(1, "UpdateQuantity must persist exactly one log row");
        var log = persistedLogs.Single();
        log.OldQuantity.Should().Be(100m);
        log.NewQuantity.Should().Be(80m);
        log.LogType.Should().Be(LogEntryType.Manual);
        log.UserId.Should().Be("user-42");

        response.Material.CurrentQuantity.Should().Be(80m);
    }

    [Fact]
    public async Task ConsumptionCalculationService_ProcessDailyConsumptionAsync_PersistsPackingMaterialLogRow()
    {
        // Arrange
        var processingDate = new DateOnly(2026, 5, 21);
        var material = new PackingMaterial("Packing Tape", 2.5m, ConsumptionType.PerDay, 100m);

        // Add material to the REAL context and save
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        // Refresh material to ensure ID is populated
        material = await _context.PackingMaterials.FindAsync(new object[] { material.Id }, cancellationToken: default);

        // Create a REAL repository backed by the InMemory context (NOT a mock)
        var repository = new PackingMaterialRepository(_context);

        // Create a mock invoice repository (no invoices needed for PerDay consumption)
        var invoiceRepository = new MockIssuedInvoiceRepository();

        // Create a mock logger
        var logger = new MockLogger<ConsumptionCalculationService>();

        // Construct the service with the REAL repository
        var service = new ConsumptionCalculationService(repository, invoiceRepository, logger);

        // Act — process daily consumption for the material
        var result = await service.ProcessDailyConsumptionAsync(processingDate);

        // Assert — verify processing ran
        Assert.True(result.WasRun, "ProcessDailyConsumptionAsync should have run");
        Assert.Equal(1, result.MaterialsProcessed);

        // Assert — verify the log was persisted to the database
        var allLogs = await _context.Set<PackingMaterialLog>().ToListAsync();
        var persistedLogs = allLogs
            .Where(l => l.LogType == LogEntryType.AutomaticConsumption)
            .ToList();

        persistedLogs.Should().HaveCount(1, "Should have persisted exactly one AutomaticConsumption log row");
        var log = persistedLogs.Single();
        log.PackingMaterialId.Should().Be(material.Id);
        log.Date.Should().Be(processingDate);
        log.OldQuantity.Should().Be(100m);
        log.NewQuantity.Should().Be(97.5m); // 100 - 2.5 (ConsumptionRate)

        // Assert — verify the consumption row was also persisted
        var allConsumptionRows = await _context.Set<PackingMaterialConsumption>().ToListAsync();
        var consumptionRows = allConsumptionRows
            .Where(c => c.Date == processingDate)
            .ToList();

        consumptionRows.Should().HaveCount(1, "Should have persisted one consumption fact row");
        var consumption = consumptionRows.Single();
        consumption.PackingMaterialId.Should().Be(material.Id);
        consumption.Amount.Should().Be(2.5m);
        consumption.ConsumptionType.Should().Be(ConsumptionType.PerDay);
        consumption.InvoiceId.Should().BeNull();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
