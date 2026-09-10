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

    [Fact]
    public async Task truncates_stored_error_message_without_mutating_the_caller_s_entry()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"changelog-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var sut = new ProductPriceChangeLogRepository(context);

        var overlongErrorMessage = new string('x', 2500);
        var entry = new ProductPriceChangeLog
        {
            ProductCode = "DEO007005",
            OldPriceWithVat = 190.00m,
            NewPriceWithVat = 210.00m,
            ChangedAt = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            ChangedBy = "ondra@anela.cz",
            ShoptetSucceeded = true,
            FlexiSucceeded = false,
            ErrorMessage = overlongErrorMessage,
        };

        // Act
        await sut.AppendAsync(entry, CancellationToken.None);

        // Assert
        var stored = context.ProductPriceChangeLogs.Single();
        stored.ErrorMessage.Should().HaveLength(2000);
        stored.ErrorMessage.Should().Be(overlongErrorMessage[..2000]);

        entry.ErrorMessage.Should().Be(overlongErrorMessage);
        entry.ErrorMessage.Should().HaveLength(2500);
    }
}
