using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class IssuedInvoiceRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IssuedInvoiceRepository _repository;
    private readonly Mock<ILogger<IssuedInvoiceRepository>> _mockLogger;

    public IssuedInvoiceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"IssuedInvoiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _mockLogger = new Mock<ILogger<IssuedInvoiceRepository>>();
        _repository = new IssuedInvoiceRepository(_context, _mockLogger.Object);
    }

    [Fact]
    public async Task AddAsync_WithNewInvoice_SetsAuditFieldsAndSaves()
    {
        // Arrange
        var invoice = new IssuedInvoice { Id = "INV-001", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        // Act
        var result = await _repository.AddAsync(invoice);
        await _repository.SaveChangesAsync();

        // Assert
        Assert.True(result.CreationTime > DateTime.MinValue);
        Assert.NotNull(result.ConcurrencyStamp);
        Assert.NotEmpty(result.ConcurrencyStamp);

        // Verify saved to database
        var savedInvoice = await _context.IssuedInvoices.FindAsync("INV-001");
        Assert.NotNull(savedInvoice);
        Assert.Equal("INV-001", savedInvoice.Id);
    }

    [Fact]
    public async Task UpdateAsync_WithExistingInvoice_UpdatesAuditFields()
    {
        // Arrange
        var invoice = new IssuedInvoice { Id = "INV-002", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        await _repository.AddAsync(invoice);
        await _repository.SaveChangesAsync();

        var originalStamp = invoice.ConcurrencyStamp;

        // Act
        invoice.CustomerName = "Updated Customer";
        await _repository.UpdateAsync(invoice);
        await _repository.SaveChangesAsync();

        // Assert
        Assert.NotNull(invoice.LastModificationTime);
        Assert.True(invoice.LastModificationTime > invoice.CreationTime);
        Assert.NotEqual(originalStamp, invoice.ConcurrencyStamp);
        Assert.Equal("Updated Customer", invoice.CustomerName);
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingInvoice_ReturnsInvoice()
    {
        // Arrange
        var invoice = new IssuedInvoice { Id = "INV-003", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        await _repository.AddAsync(invoice);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync("INV-003");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("INV-003", result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentInvoice_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByIdAsync("NON-EXISTENT");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdWithSyncHistoryAsync_WithInvoiceWithHistory_IncludesSyncHistory()
    {
        // Arrange
        var invoice = new IssuedInvoice { Id = "INV-004", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        var syncData = CreateTestSyncData();
        invoice.SyncSucceeded(syncData);

        await _repository.AddAsync(invoice);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdWithSyncHistoryAsync("INV-004");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("INV-004", result.Id);
        Assert.NotNull(result.SyncHistory);
        // Note: The exact sync history assertion depends on the SyncHistory implementation
    }

    [Fact]
    public async Task GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats()
    {
        // Arrange
        var dateFrom = DateTime.Today.AddDays(-7);
        var dateTo = DateTime.Today;

        // In range invoices
        var syncedInvoice = new IssuedInvoice { Id = "INV-SYNCED", InvoiceDate = DateTime.Today.AddDays(-3), DueDate = DateTime.Today.AddDays(27), TaxDate = DateTime.Today.AddDays(-3) };
        syncedInvoice.SyncSucceeded(CreateTestSyncData());

        var unsyncedInvoice = new IssuedInvoice { Id = "INV-UNSYNCED", InvoiceDate = DateTime.Today.AddDays(-2), DueDate = DateTime.Today.AddDays(28), TaxDate = DateTime.Today.AddDays(-2) };

        var errorInvoice = new IssuedInvoice { Id = "INV-ERROR", InvoiceDate = DateTime.Today.AddDays(-1), DueDate = DateTime.Today.AddDays(29), TaxDate = DateTime.Today.AddDays(-1) };
        errorInvoice.SyncFailed(CreateTestSyncData(), "Error");

        var pairedInvoice = new IssuedInvoice { Id = "INV-PAIRED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        pairedInvoice.SyncFailed(CreateTestSyncData(), new IssuedInvoiceError { ErrorType = IssuedInvoiceErrorType.InvoicePaired, Message = "Invoice paired" });

        // Out of range invoice
        var oldInvoice = new IssuedInvoice { Id = "INV-OLD", InvoiceDate = DateTime.Today.AddDays(-10), DueDate = DateTime.Today.AddDays(20), TaxDate = DateTime.Today.AddDays(-10) };

        await _repository.AddAsync(syncedInvoice);
        await _repository.AddAsync(unsyncedInvoice);
        await _repository.AddAsync(errorInvoice);
        await _repository.AddAsync(pairedInvoice);
        await _repository.AddAsync(oldInvoice);
        await _repository.SaveChangesAsync();

        // Act
        var stats = await _repository.GetSyncStatsAsync(dateFrom, dateTo);

        // Assert
        Assert.Equal(4, stats.TotalInvoices); // Only in-range invoices
        Assert.Equal(1, stats.SyncedInvoices); // Only synced invoice
        Assert.Equal(3, stats.UnsyncedInvoices); // Unsynced, error, paired
        Assert.Equal(2, stats.InvoicesWithErrors); // Error and paired
        Assert.Equal(1, stats.CriticalErrors); // Only error invoice (paired is not critical)
    }

    [Fact]
    public async Task GetPaginatedAsync_WithFilters_ReturnsFilteredAndPaginatedResults()
    {
        // Arrange — use InvoiceId filter (CustomerName uses ILike which requires PostgreSQL, not InMemory)
        var invoice1 = new IssuedInvoice { Id = "INV-A001", Price = 1000, InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        var invoice2 = new IssuedInvoice { Id = "INV-B001", Price = 2000, InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        var invoice3 = new IssuedInvoice { Id = "INV-A002", Price = 1500, InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        await _repository.AddAsync(invoice1);
        await _repository.AddAsync(invoice2);
        await _repository.AddAsync(invoice3);
        await _repository.SaveChangesAsync();

        var filters = new IssuedInvoiceFilters
        {
            InvoiceId = "INV-A",
            PageNumber = 1,
            PageSize = 10,
            SortBy = "Price",
            SortDescending = false
        };

        // Act
        var result = await _repository.GetPaginatedAsync(filters);

        // Assert
        Assert.Equal(2, result.TotalCount);
        var items = result.Items.ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("INV-A001", items[0].Id); // Lower price first
        Assert.Equal("INV-A002", items[1].Id);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public async Task GetPaginatedAsync_WithShowOnlyUnsynced_ReturnsOnlyUnsyncedInvoices()
    {
        // Arrange
        var syncedInvoice = new IssuedInvoice { Id = "INV-SYNCED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        syncedInvoice.SyncSucceeded(CreateTestSyncData());

        var unsyncedInvoice = new IssuedInvoice { Id = "INV-UNSYNCED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        await _repository.AddAsync(syncedInvoice);
        await _repository.AddAsync(unsyncedInvoice);
        await _repository.SaveChangesAsync();

        var filters = new IssuedInvoiceFilters
        {
            ShowOnlyUnsynced = true,
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _repository.GetPaginatedAsync(filters);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("INV-UNSYNCED", result.Items.First().Id);
    }

    [Fact]
    public async Task GetPaginatedAsync_WithShowOnlyWithErrors_ReturnsOnlyErrorInvoices()
    {
        // Arrange
        var successInvoice = new IssuedInvoice { Id = "INV-SUCCESS", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        successInvoice.SyncSucceeded(CreateTestSyncData());

        var errorInvoice = new IssuedInvoice { Id = "INV-ERROR", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        errorInvoice.SyncFailed(CreateTestSyncData(), "Test error");

        await _repository.AddAsync(successInvoice);
        await _repository.AddAsync(errorInvoice);
        await _repository.SaveChangesAsync();

        var filters = new IssuedInvoiceFilters
        {
            ShowOnlyWithErrors = true,
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _repository.GetPaginatedAsync(filters);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("INV-ERROR", result.Items.First().Id);
    }

    [Fact]
    public async Task GetPaginatedAsync_WithPageSizeZero_ReturnsAllItems()
    {
        // Arrange
        await _repository.AddAsync(new IssuedInvoice { Id = "INV-001", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today });
        await _repository.AddAsync(new IssuedInvoice { Id = "INV-002", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today });
        await _repository.AddAsync(new IssuedInvoice { Id = "INV-003", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today });
        await _repository.SaveChangesAsync();

        var filters = new IssuedInvoiceFilters
        {
            PageNumber = 1,
            PageSize = 0 // Return all items
        };

        // Act
        var result = await _repository.GetPaginatedAsync(filters);

        // Assert
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count());
    }

    private static IssuedInvoiceDetail CreateTestSyncData()
    {
        return new IssuedInvoiceDetail
        {
            Code = "TEST-001",
            Price = new InvoicePrice
            {
                WithVat = 1000,
                CurrencyCode = "CZK"
            }
        };
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}