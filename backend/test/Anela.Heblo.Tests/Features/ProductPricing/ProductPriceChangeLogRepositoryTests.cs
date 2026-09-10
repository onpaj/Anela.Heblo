using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.ProductPricing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ProductPriceChangeLogRepositoryTests
{
    [Fact]
    public async Task appends_an_entry_and_persists_it()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"changelog-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var sut = new ProductPriceChangeLogRepository(context);

        // Act
        await sut.AppendAsync(new ProductPriceChangeLog
        {
            ProductCode = "DEO007005",
            OldPriceWithVat = 190.00m,
            NewPriceWithVat = 210.00m,
            ChangedAt = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            ChangedBy = "ondra@anela.cz",
            ShoptetSucceeded = true,
            FlexiSucceeded = false,
            ErrorMessage = "Flexi timeout",
        }, CancellationToken.None);

        // Assert
        var stored = context.ProductPriceChangeLogs.Single();
        stored.ProductCode.Should().Be("DEO007005");
        stored.ShoptetSucceeded.Should().BeTrue();
        stored.FlexiSucceeded.Should().BeFalse();
        stored.ErrorMessage.Should().Be("Flexi timeout");
    }
}
