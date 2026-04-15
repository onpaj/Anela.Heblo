using Anela.Heblo.Adapters.GoogleAds;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.GoogleAds;

public class GoogleAdsTransactionSourceTests
{
    private static GoogleAdsTransactionSource CreateSource(IReadOnlyList<RawAccountBudget> rows)
        => new(new FakeAccountBudgetFetcher(rows), NullLogger<GoogleAdsTransactionSource>.Instance);

    [Fact]
    public async Task GetTransactionsAsync_ValidRows_MapsFieldsCorrectly()
    {
        // Arrange
        var startDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var source = CreateSource([new("budget-001", "Spring Campaign Budget", startDate, 15_000_000L, "CZK")]);

        // Act
        var transactions = await source.GetTransactionsAsync(
            startDate.AddDays(-1), startDate.AddDays(1), CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(1);
        var tx = transactions[0];
        tx.TransactionId.Should().Be("budget-001");
        tx.Platform.Should().Be("GoogleAds");
        tx.Amount.Should().Be(15m); // 15_000_000 micros / 1_000_000
        tx.Currency.Should().Be("CZK");
        tx.Description.Should().Be("Spring Campaign Budget");
        tx.TransactionDate.Should().Be(startDate);
    }

    [Fact]
    public async Task GetTransactionsAsync_AmountConversion_MicrosToDecimal()
    {
        // Arrange
        var source = CreateSource([new("budget-002", null, DateTime.UtcNow.AddDays(-1), 1_234_567L, "CZK")]);

        // Act
        var transactions = await source.GetTransactionsAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(1);
        transactions[0].Amount.Should().Be(1.234567m); // exact micros conversion
    }

    [Fact]
    public async Task GetTransactionsAsync_NullName_UsesDefaultDescription()
    {
        // Arrange
        var source = CreateSource([new("budget-003", null, DateTime.UtcNow.AddDays(-1), 5_000_000L, "CZK")]);

        // Act
        var transactions = await source.GetTransactionsAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        // Assert
        transactions[0].Description.Should().Be("Google Ads billing period");
    }

    [Fact]
    public async Task GetTransactionsAsync_EmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var source = CreateSource([]);

        // Act
        var transactions = await source.GetTransactionsAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        // Assert
        transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsAsync_MultipleRows_AllMapped()
    {
        // Arrange
        var source = CreateSource(
        [
            new("budget-010", "Budget A", DateTime.UtcNow.AddDays(-5), 10_000_000L, "CZK"),
            new("budget-011", "Budget B", DateTime.UtcNow.AddDays(-3), 20_000_000L, "CZK"),
            new("budget-012", "Budget C", DateTime.UtcNow.AddDays(-1), 30_000_000L, "CZK"),
        ]);

        // Act
        var transactions = await source.GetTransactionsAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(3);
        transactions.Select(t => t.TransactionId).Should().Contain(["budget-010", "budget-011", "budget-012"]);
        transactions.Select(t => t.Amount).Should().Contain([10m, 20m, 30m]);
    }
}

/// <summary>Test double that returns a fixed list of account budget rows.</summary>
file sealed class FakeAccountBudgetFetcher(IReadOnlyList<RawAccountBudget> rows) : IAccountBudgetFetcher
{
    public Task<IReadOnlyList<RawAccountBudget>> FetchAsync(DateTime from, DateTime to, CancellationToken ct)
        => Task.FromResult(rows);
}
