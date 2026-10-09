using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.MarketingInvoices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ImportedMarketingTransactionTests_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ImportedMarketingTransaction CreateTransaction(string platform, string transactionId)
        => new()
        {
            Platform = platform,
            TransactionId = transactionId,
            Amount = 100m,
            Currency = "CZK",
            TransactionDate = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task GetExistingTransactionIdsAsync_ReturnsOnlyIdsPresentForGivenPlatform()
    {
        // Arrange
        await using var context = CreateContext();
        context.ImportedMarketingTransactions.AddRange(
            CreateTransaction("MetaAds", "TX-001"),
            CreateTransaction("MetaAds", "TX-002"),
            CreateTransaction("GoogleAds", "TX-001")); // same TransactionId, different platform
        await context.SaveChangesAsync();
        var repository = new ImportedMarketingTransactionRepository(context);

        // Act
        var result = await repository.GetExistingTransactionIdsAsync(
            "MetaAds", new[] { "TX-001", "TX-002", "TX-999" }, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new HashSet<string> { "TX-001", "TX-002" });
    }

    [Fact]
    public async Task GetExistingTransactionIdsAsync_EmptyIdList_ReturnsEmptySet()
    {
        // Arrange
        await using var context = CreateContext();
        var repository = new ImportedMarketingTransactionRepository(context);

        // Act
        var result = await repository.GetExistingTransactionIdsAsync(
            "MetaAds", Array.Empty<string>(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
