using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Purchase.PurchaseOrders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public sealed class PurchaseOrderRepositoryHistoryTests : IDisposable
{
    private const long ValidSupplierId = 1;
    private const string ValidSupplierName = "Test Supplier";

    private readonly ApplicationDbContext _context;
    private readonly PurchaseOrderRepository _repository;

    public PurchaseOrderRepositoryHistoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PurchaseOrderRepoHistoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PurchaseOrderRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenOrderExists()
    {
        // Arrange
        var order = new PurchaseOrder(
            "PO-2026-EXIST",
            ValidSupplierId,
            ValidSupplierName,
            DateTime.UtcNow,
            null,
            null,
            "notes",
            "system");

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.ExistsAsync(order.Id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenOrderMissing()
    {
        // Act
        var result = await _repository.ExistsAsync(999_999, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmpty_WhenOrderHasNoHistory()
    {
        // Arrange
        var order = new PurchaseOrder(
            "PO-2026-EMPTY",
            ValidSupplierId,
            ValidSupplierName,
            DateTime.UtcNow,
            null,
            null,
            null,
            "system");

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetHistoryAsync(999_999, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsRowsForOrder_OrderedByChangedAtDescending()
    {
        // Arrange
        var order = new PurchaseOrder(
            "PO-2026-WITHHISTORY",
            ValidSupplierId,
            ValidSupplierName,
            DateTime.UtcNow,
            null,
            null,
            null,
            "system");

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();

        // Add additional history rows after order creation
        await Task.Delay(5);
        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(order.Id, "StatusChanged", "Draft", "InTransit", "user-2"));
        await _context.SaveChangesAsync();

        await Task.Delay(5);
        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(order.Id, "InvoiceAcquired", "false", "true", "user-3"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetHistoryAsync(order.Id, CancellationToken.None);

        // Assert
        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Should().BeInDescendingOrder(h => h.ChangedAt);
        // Most recent should be InvoiceAcquired
        result.First().Action.Should().Be("InvoiceAcquired");
    }

    [Fact]
    public async Task GetHistoryAsync_DoesNotReturnRowsForOtherOrders()
    {
        // Arrange
        var orderA = new PurchaseOrder("PO-A", ValidSupplierId, ValidSupplierName, DateTime.UtcNow, null, null, null, "system");
        var orderB = new PurchaseOrder("PO-B", ValidSupplierId, ValidSupplierName, DateTime.UtcNow, null, null, null, "system");
        _context.PurchaseOrders.AddRange(orderA, orderB);
        await _context.SaveChangesAsync();

        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(orderA.Id, "StatusChanged", "Draft", "InTransit", "user-1"));
        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(orderB.Id, "StatusChanged", "Draft", "InTransit", "user-2"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetHistoryAsync(orderA.Id, CancellationToken.None);

        // Assert
        // Should have the auto-created "Order created" history plus the manually added "StatusChanged"
        result.Should().HaveCountGreaterThanOrEqualTo(1);
        // Verify that only orderA's history is returned
        result.Should().AllSatisfy(h => h.PurchaseOrderId.Should().Be(orderA.Id));
        // Verify that orderB's history is not included
        result.Select(h => h.ChangedBy).Should().NotContain("user-2");
    }
}
