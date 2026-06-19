using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
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

    private static InvoiceConsumptionHeader MakeHeader(string id, int itemsCount)
    {
        return new InvoiceConsumptionHeader(id, itemsCount);
    }

    private static ConsumptionCalculationService BuildService(
        MockPackingMaterialRepository materialRepo,
        MockInvoiceConsumptionSource invoiceSource,
        ILogger<ConsumptionCalculationService> logger)
    {
        return new ConsumptionCalculationService(materialRepo, invoiceSource, logger);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_PerDay_EmitsOneFactRowPerMaterial()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Tape", 3m, ConsumptionType.PerDay, 100m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        var invoiceSource = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed);
        Assert.Single(materialRepo.AddedConsumptionRows);

        var row = materialRepo.AddedConsumptionRows[0];
        Assert.Equal(3m, row.Amount);
        Assert.Null(row.InvoiceId);
        Assert.Equal(ConsumptionType.PerDay, row.ConsumptionType);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_PerOrder_EmitsOneFactRowPerInvoicePerMaterial()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Box", 2m, ConsumptionType.PerOrder, 50m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });

        var invoiceSource = new MockInvoiceConsumptionSource();
        invoiceSource.SetHeaders(date, new[]
        {
            MakeHeader("INV-1", itemsCount: 3),
            MakeHeader("INV-2", itemsCount: 5)
        });

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed);
        Assert.Equal(2, materialRepo.AddedConsumptionRows.Count);

        Assert.All(materialRepo.AddedConsumptionRows, row => Assert.Equal(2m, row.Amount));
        Assert.All(materialRepo.AddedConsumptionRows, row => Assert.Equal(ConsumptionType.PerOrder, row.ConsumptionType));
        Assert.NotNull(materialRepo.AddedConsumptionRows[0].InvoiceId);
        Assert.NotNull(materialRepo.AddedConsumptionRows[1].InvoiceId);

        var totalDecrement = materialRepo.AddedConsumptionRows.Sum(r => r.Amount);
        Assert.Equal(4m, totalDecrement);

        // Quantity should be decremented by 4 — material mutated in-place
        var updated = materialRepo.Materials[0];
        Assert.Equal(46m, updated.CurrentQuantity);
        var log = updated.Logs.Single();
        Assert.Equal(50m, log.OldQuantity);
        Assert.Equal(46m, log.NewQuantity);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_PerProduct_ScalesByItemsCount()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Sticker", 1m, ConsumptionType.PerProduct, 100m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });

        var invoiceSource = new MockInvoiceConsumptionSource();
        invoiceSource.SetHeaders(date, new[]
        {
            MakeHeader("INV-A", itemsCount: 3),
            MakeHeader("INV-B", itemsCount: 5)
        });

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed);
        Assert.Equal(2, materialRepo.AddedConsumptionRows.Count);

        var amounts = materialRepo.AddedConsumptionRows.Select(r => r.Amount).OrderBy(a => a).ToList();
        Assert.Equal(new[] { 3m, 5m }, amounts);

        var totalDecrement = materialRepo.AddedConsumptionRows.Sum(r => r.Amount);
        Assert.Equal(8m, totalDecrement);

        var updated = materialRepo.Materials[0];
        Assert.Equal(92m, updated.CurrentQuantity);
        var log = updated.Logs.Single();
        Assert.Equal(100m, log.OldQuantity);
        Assert.Equal(92m, log.NewQuantity);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        materialRepo.SetHasDailyProcessingBeenRun(date, true);
        var invoiceSource = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.False(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);
        Assert.Empty(materialRepo.AddedConsumptionRows);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_RecordsDailyRun_WhenZeroConsumption()
    {
        // Arrange — PerOrder material but zero invoices means zero consumption
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        var invoiceSource = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);

        // No PackingMaterialLog entries — no synthetic marker
        var updated = materialRepo.Materials[0];
        Assert.Empty(updated.Logs);

        // Daily run record written
        var dailyRun = Assert.Single(materialRepo.AddedDailyRuns);
        Assert.Equal(date, dailyRun.Date);
        Assert.Equal(0, dailyRun.MaterialsProcessed);

        // No fact rows for zero consumption
        Assert.Empty(materialRepo.AddedConsumptionRows);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_MixedTypes_ZeroInvoices_PerDayDecrementsPerOrderHasNoLog()
    {
        // Arrange — PerDay material always decrements; PerOrder material has no consumption when zero invoices
        var date = new DateOnly(2025, 6, 15);
        var perDayMaterial = new PackingMaterial("Tape", 5m, ConsumptionType.PerDay, 200m);
        var perOrderMaterial = new PackingMaterial("Box", 2m, ConsumptionType.PerOrder, 100m);

        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { perDayMaterial, perOrderMaterial });
        var invoiceSource = new MockInvoiceConsumptionSource(); // no invoices

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed); // only PerDay counted

        // PerDay material should be decremented
        var tape = materialRepo.Materials.First(m => m.Name == "Tape");
        Assert.Equal(195m, tape.CurrentQuantity);
        Assert.Single(tape.Logs); // real consumption log

        // PerOrder material should be completely untouched — no marker log
        var box = materialRepo.Materials.First(m => m.Name == "Box");
        Assert.Equal(100m, box.CurrentQuantity);
        Assert.Empty(box.Logs);

        // One PerDay fact row only
        Assert.Single(materialRepo.AddedConsumptionRows);
        Assert.Equal(ConsumptionType.PerDay, materialRepo.AddedConsumptionRows[0].ConsumptionType);

        // Daily run recorded
        var dailyRun = Assert.Single(materialRepo.AddedDailyRuns);
        Assert.Equal(date, dailyRun.Date);
        Assert.Equal(1, dailyRun.MaterialsProcessed);
    }

    [Fact]
    public async Task HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue()
    {
        // Arrange
        var materialRepo = new MockPackingMaterialRepository();
        var invoiceSource = new MockInvoiceConsumptionSource();
        var service = BuildService(materialRepo, invoiceSource, _mockLogger);
        var date = DateOnly.FromDateTime(DateTime.Today);
        materialRepo.SetHasDailyProcessingBeenRun(date, true);

        // Act
        var result = await service.HasDayAlreadyBeenProcessedAsync(date);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task LogDecrement_Invariant()
    {
        // Arrange: 1 PerDay rate=5, 1 PerOrder rate=2 with 3 invoices, 1 PerProduct rate=1 with invoices ItemsCount=[4,6,0]
        var date = new DateOnly(2025, 6, 15);

        var perDayMaterial = new PackingMaterial("Tape", 5m, ConsumptionType.PerDay, 200m);
        var perOrderMaterial = new PackingMaterial("Box", 2m, ConsumptionType.PerOrder, 100m);
        var perProductMaterial = new PackingMaterial("Sticker", 1m, ConsumptionType.PerProduct, 150m);

        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { perDayMaterial, perOrderMaterial, perProductMaterial });

        var invoiceSource = new MockInvoiceConsumptionSource();
        invoiceSource.SetHeaders(date, new[]
        {
            MakeHeader("INV-1", itemsCount: 4),
            MakeHeader("INV-2", itemsCount: 6),
            MakeHeader("INV-3", itemsCount: 0)
        });

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert: verify SUM(fact rows per material) == |ChangeAmount in log| for each material
        Assert.True(result.WasRun);

        var allRows = materialRepo.AddedConsumptionRows;

        // PerDay: 1 row, amount = 5
        var perDayRows = allRows.Where(r => r.ConsumptionType == ConsumptionType.PerDay).ToList();
        Assert.Single(perDayRows);
        Assert.Equal(5m, perDayRows.Sum(r => r.Amount));

        var tapeLog = materialRepo.Materials.First(m => m.Name == "Tape").Logs.Single();
        Assert.Equal(5m, Math.Abs(tapeLog.ChangeAmount));
        Assert.Equal(perDayRows.Sum(r => r.Amount), Math.Abs(tapeLog.ChangeAmount));

        // PerOrder: 3 rows (one per invoice), each amount = 2, total = 6
        var perOrderRows = allRows.Where(r => r.ConsumptionType == ConsumptionType.PerOrder).ToList();
        Assert.Equal(3, perOrderRows.Count);
        Assert.Equal(6m, perOrderRows.Sum(r => r.Amount));

        var boxLog = materialRepo.Materials.First(m => m.Name == "Box").Logs.Single();
        Assert.Equal(6m, Math.Abs(boxLog.ChangeAmount));
        Assert.Equal(perOrderRows.Sum(r => r.Amount), Math.Abs(boxLog.ChangeAmount));

        // PerProduct: 2 rows (zero-amount row for INV-3 is filtered), amounts = 4, 6. Total = 10.
        var perProductRows = allRows.Where(r => r.ConsumptionType == ConsumptionType.PerProduct).ToList();
        Assert.Equal(2, perProductRows.Count);
        var perProductTotal = perProductRows.Sum(r => r.Amount);
        Assert.Equal(10m, perProductTotal);

        var stickerLog = materialRepo.Materials.First(m => m.Name == "Sticker").Logs.Single();
        Assert.Equal(10m, Math.Abs(stickerLog.ChangeAmount));
        Assert.Equal(perProductRows.Sum(r => r.Amount), Math.Abs(stickerLog.ChangeAmount));
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_RecordsDailyRun_WhenMaterialListIsEmpty()
    {
        // Arrange — no materials configured at all (FR-4)
        var date = new DateOnly(2025, 6, 15);
        var materialRepo = new MockPackingMaterialRepository(); // no materials
        var invoiceRepo = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceRepo, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);

        var dailyRun = Assert.Single(materialRepo.AddedDailyRuns);
        Assert.Equal(date, dailyRun.Date);
        Assert.Equal(0, dailyRun.MaterialsProcessed);

        Assert.Empty(materialRepo.AddedConsumptionRows);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_SecondRun_ReturnsWasRunFalse_WithoutMutating()
    {
        // Arrange — simulate day already processed via HasDailyProcessingBeenRunAsync (FR-2)
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        materialRepo.SetHasDailyProcessingBeenRun(date, true); // simulate existing daily run
        var invoiceRepo = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceRepo, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.False(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);

        // No mutations
        Assert.Empty(materialRepo.AddedDailyRuns);
        Assert.Empty(materialRepo.AddedConsumptionRows);
        Assert.Equal(8000m, materialRepo.Materials[0].CurrentQuantity);
        Assert.Empty(materialRepo.Materials[0].Logs);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions()
    {
        // Arrange — SaveChangesAsync throws a DbUpdateException unrelated to the daily run constraint
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerDay, 8000m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        var invoiceRepo = new MockInvoiceConsumptionSource();

        // Set up a non-duplicate DbUpdateException (no inner PostgresException).
        // After the refactor, AddDailyRunAsync returns true (mock default), so execution
        // reaches the second SaveChangesAsync (consumption rows + quantity updates), where
        // this exception is thrown.
        var unrelatedEx = new Microsoft.EntityFrameworkCore.DbUpdateException("some other db error", new Exception("inner"));
        materialRepo.SetSaveChangesException(unrelatedEx);

        var service = BuildService(materialRepo, invoiceRepo, _mockLogger);

        // Act & Assert — the service must NOT swallow unrelated DbUpdateExceptions
        var thrown = await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => service.ProcessDailyConsumptionAsync(date));
        Assert.Same(unrelatedEx, thrown);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAddDailyRunReturnsFalse()
    {
        // Arrange — simulate concurrent duplicate: AddDailyRunAsync returns false
        var date = new DateOnly(2025, 6, 15);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetAddDailyRunReturns(date, false);
        var invoiceSource = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.False(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);
        Assert.Empty(materialRepo.AddedConsumptionRows);
    }
}
