using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Bank;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class BankStatementImportRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly BankStatementImportRepository _repository;

    public BankStatementImportRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"BankStatementImportTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new BankStatementImportRepository(_context);
    }

    [Fact]
    public async Task AddAsync_WithValidImport_SavesAndReturnsImport()
    {
        // Arrange
        var import = new BankStatementImport("T12345", DateTime.UtcNow.Date);
        import.Account = "123456789";
        import.Currency = CurrencyCode.CZK;
        import.ItemCount = 10;
        import.ImportResult = "Success";

        // Act
        var result = await _repository.AddAsync(import);

        // Assert
        Assert.NotEqual(0, result.Id);
        Assert.Equal("T12345", result.TransferId);
        Assert.Equal("123456789", result.Account);
        Assert.Equal(CurrencyCode.CZK, result.Currency);
        Assert.Equal(10, result.ItemCount);
        Assert.Equal("Success", result.ImportResult);

        // Verify it was saved to database
        var savedImport = await _context.BankStatements.FindAsync(result.Id);
        Assert.NotNull(savedImport);
        Assert.Equal("T12345", savedImport.TransferId);
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ReturnsImport()
    {
        // Arrange
        var import = new BankStatementImport("T67890", DateTime.UtcNow.Date);
        import.Account = "987654321";
        import.Currency = CurrencyCode.EUR;
        import.ItemCount = 5;
        import.ImportResult = "Failed: Connection timeout";

        var savedImport = await _repository.AddAsync(import);

        // Act
        var result = await _repository.GetByIdAsync(savedImport.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(savedImport.Id, result.Id);
        Assert.Equal("T67890", result.TransferId);
        Assert.Equal("987654321", result.Account);
        Assert.Equal(CurrencyCode.EUR, result.Currency);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByIdAsync(99999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFilteredAsync_WithNoFilters_ReturnsAllImports()
    {
        // Arrange
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-2), "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", DateTime.UtcNow.Date.AddDays(-1), "456", CurrencyCode.EUR);
        var import3 = CreateTestImport("T3", DateTime.UtcNow.Date, "789", CurrencyCode.CZK);

        await _repository.AddAsync(import1);
        await _repository.AddAsync(import2);
        await _repository.AddAsync(import3);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync();

        // Assert
        Assert.Equal(3, totalCount);
        Assert.Equal(3, items.Count());
        // Default order is by ImportDate descending, then Id ascending
        // Since all items created at same time have same ImportDate, they're ordered by Id
        var itemsList = items.ToList();
        Assert.Contains(itemsList, i => i.TransferId == "T1");
        Assert.Contains(itemsList, i => i.TransferId == "T2");
        Assert.Contains(itemsList, i => i.TransferId == "T3");
    }

    [Fact]
    public async Task GetFilteredAsync_WithStatementDateFilter_ReturnsFilteredImports()
    {
        // Arrange
        var targetDate = DateTime.UtcNow.Date.AddDays(-1);
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-2), "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", targetDate, "456", CurrencyCode.EUR);
        var import3 = CreateTestImport("T3", DateTime.UtcNow.Date, "789", CurrencyCode.CZK);

        await _repository.AddAsync(import1);
        await _repository.AddAsync(import2);
        await _repository.AddAsync(import3);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(statementDate: targetDate);

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("T2", items.First().TransferId);
        Assert.Equal(targetDate.Date, items.First().StatementDate.Date);
    }

    [Fact]
    public async Task GetFilteredAsync_WithPagination_ReturnsCorrectPage()
    {
        // Arrange - Create 5 imports
        for (int i = 1; i <= 5; i++)
        {
            var import = CreateTestImport($"T{i}", DateTime.UtcNow.Date.AddDays(-i), $"ACC{i}", CurrencyCode.CZK);
            await _repository.AddAsync(import);
        }

        // Act - Get page 2 with 2 items per page (skip 2, take 2)
        var (items, totalCount) = await _repository.GetFilteredAsync(skip: 2, take: 2);

        // Assert
        Assert.Equal(5, totalCount);
        Assert.Equal(2, items.Count());
        // Default ordering: ImportDate DESC, then Id ASC
        // Items created sequentially have slightly different ImportDates (microseconds apart)
        // Order after sort: T5, T4, T3, T2, T1 (latest ImportDate first)
        // Skip 2 (T5, T4), take 2 (T3, T2)
        var itemsList = items.ToList();
        Assert.Contains(itemsList, i => i.TransferId == "T3");
        Assert.Contains(itemsList, i => i.TransferId == "T2");
    }

    [Fact]
    public async Task GetFilteredAsync_WithAscendingOrder_ReturnsCorrectOrder()
    {
        // Arrange
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-2), "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", DateTime.UtcNow.Date.AddDays(-1), "456", CurrencyCode.EUR);
        var import3 = CreateTestImport("T3", DateTime.UtcNow.Date, "789", CurrencyCode.CZK);

        await _repository.AddAsync(import1);
        await _repository.AddAsync(import2);
        await _repository.AddAsync(import3);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(orderBy: "ImportDate", ascending: true);

        // Assert
        Assert.Equal(3, totalCount);
        var itemsList = items.ToList();
        // Should be ordered by ImportDate ascending (oldest first)
        Assert.Equal("T1", itemsList[0].TransferId);
        Assert.Equal("T2", itemsList[1].TransferId);
        Assert.Equal("T3", itemsList[2].TransferId);
    }

    [Fact]
    public async Task GetFilteredAsync_WithIdFilter_ReturnsSingleMatch()
    {
        // Arrange
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date, "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", DateTime.UtcNow.Date, "456", CurrencyCode.EUR);

        var saved1 = await _repository.AddAsync(import1);
        var saved2 = await _repository.AddAsync(import2);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(id: saved1.Id);

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal(saved1.Id, items.First().Id);
        Assert.Equal("T1", items.First().TransferId);
    }

    [Fact]
    public async Task GetFilteredAsync_WithImportDateFilter_ReturnsFilteredImports()
    {
        // Arrange - Create imports at different times
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date, "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", DateTime.UtcNow.Date, "456", CurrencyCode.EUR);

        await _repository.AddAsync(import1);

        // Simulate import on different day by manually setting ImportDate
        await _context.BankStatements.AddAsync(import2);
        await _context.SaveChangesAsync();

        var targetImportDate = DateTime.UtcNow.Date;

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(importDate: targetImportDate);

        // Assert
        Assert.Equal(2, totalCount); // Both should match today's import date
        Assert.Equal(2, items.Count());
    }

    [Fact]
    public async Task GetFilteredAsync_WithStatementDateOrderBy_SortsCorrectly()
    {
        // Arrange
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-3), "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", DateTime.UtcNow.Date.AddDays(-1), "456", CurrencyCode.EUR);
        var import3 = CreateTestImport("T3", DateTime.UtcNow.Date.AddDays(-2), "789", CurrencyCode.CZK);

        await _repository.AddAsync(import1);
        await _repository.AddAsync(import2);
        await _repository.AddAsync(import3);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(orderBy: "StatementDate", ascending: true);

        // Assert
        Assert.Equal(3, totalCount);
        var itemsList = items.ToList();
        // Should be ordered by StatementDate ascending (oldest first)
        Assert.Equal("T1", itemsList[0].TransferId); // 3 days ago
        Assert.Equal("T3", itemsList[1].TransferId); // 2 days ago
        Assert.Equal("T2", itemsList[2].TransferId); // 1 day ago
    }

    [Fact]
    public async Task GetFilteredAsync_WithIdOrderBy_SortsCorrectly()
    {
        // Arrange
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date, "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", DateTime.UtcNow.Date, "456", CurrencyCode.EUR);
        var import3 = CreateTestImport("T3", DateTime.UtcNow.Date, "789", CurrencyCode.CZK);

        var saved1 = await _repository.AddAsync(import1);
        var saved2 = await _repository.AddAsync(import2);
        var saved3 = await _repository.AddAsync(import3);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(orderBy: "Id", ascending: true);

        // Assert
        Assert.Equal(3, totalCount);
        var itemsList = items.ToList();
        // Should be ordered by Id ascending
        Assert.Equal(saved1.Id, itemsList[0].Id);
        Assert.Equal(saved2.Id, itemsList[1].Id);
        Assert.Equal(saved3.Id, itemsList[2].Id);
    }

    [Fact]
    public async Task GetFilteredAsync_WithInvalidOrderBy_UsesDefaultOrder()
    {
        // Arrange
        var import1 = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-1), "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", DateTime.UtcNow.Date, "456", CurrencyCode.EUR);

        await _repository.AddAsync(import1);
        await _repository.AddAsync(import2);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(orderBy: "InvalidColumn", ascending: true);

        // Assert
        Assert.Equal(2, totalCount);
        var itemsList = items.ToList();
        // Should default to ImportDate descending (newest first)
        Assert.Equal("T2", itemsList[0].TransferId);
        Assert.Equal("T1", itemsList[1].TransferId);
    }

    [Fact]
    public async Task GetFilteredAsync_WithMultipleFilters_AppliesAllFilters()
    {
        // Arrange
        var targetDate = DateTime.UtcNow.Date.AddDays(-1);
        var import1 = CreateTestImport("T1", targetDate, "123", CurrencyCode.CZK);
        var import2 = CreateTestImport("T2", targetDate, "456", CurrencyCode.EUR);
        var import3 = CreateTestImport("T3", DateTime.UtcNow.Date, "789", CurrencyCode.CZK);

        var saved1 = await _repository.AddAsync(import1);
        await _repository.AddAsync(import2);
        await _repository.AddAsync(import3);

        // Act - Filter by both statementDate and id
        var (items, totalCount) = await _repository.GetFilteredAsync(
            id: saved1.Id,
            statementDate: targetDate);

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("T1", items.First().TransferId);
        Assert.Equal(saved1.Id, items.First().Id);
    }

    private static BankStatementImport CreateTestImport(string transferId, DateTime statementDate, string account, CurrencyCode currency)
    {
        var import = new BankStatementImport(transferId, statementDate);
        import.Account = account;
        import.Currency = currency;
        import.ItemCount = 5;
        import.ImportResult = "Success";
        return import;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}